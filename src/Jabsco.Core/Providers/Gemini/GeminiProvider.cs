using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jabsco.Common.Events;

namespace Jabsco.Core.Providers.Gemini;

public sealed class GeminiProvider : IComputerUseProvider, IDisposable
{
    private static readonly string ApiBase = "https://generativelanguage.googleapis.com/v1beta/models/";
    private readonly HttpClient _http;
    private readonly GeminiOptions _opts;
    private readonly bool _ownsHttpClient;

    public string ModelId => _opts.Model;

    public GeminiProvider(GeminiOptions opts, HttpClient? http = null)
    {
        _opts = opts;
        _ownsHttpClient = http is null;
        _http = http ?? new HttpClient();
    }

    public async Task<ProviderResponse> NextActionAsync(ProviderRequest request, CancellationToken ct)
    {
        var body = BuildRequest(request);
        var url = $"{ApiBase}{Uri.EscapeDataString(_opts.Model)}:generateContent?key={_opts.ApiKey}";
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(url, content, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini API {(int)response.StatusCode}: {json}", null, response.StatusCode);
        return ParseResponse(json);
    }

    private string BuildRequest(ProviderRequest request)
    {
        var contents = new JsonArray();
        BuildContents(request, contents);

        var requestObj = new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = BuildSystemPrompt() } }
            },
            ["tools"] = new JsonArray
            {
                new JsonObject
                {
                    ["computerUse"] = new JsonObject { ["environment"] = "ENVIRONMENT_DESKTOP" }
                }
            },
            ["contents"] = contents
        };

        return requestObj.ToJsonString();
    }

    private void BuildContents(ProviderRequest request, JsonArray contents)
    {
        foreach (var turn in request.History)
        {
            contents.Add(UserMessage(turn.UserPrompt, null));
            for (int i = 0; i < turn.Turns.Count; i++)
            {
                var t = turn.Turns[i];
                bool isLast = i == turn.Turns.Count - 1;
                contents.Add(ModelFunctionCall(t.ToolUseId, t.Action));
                contents.Add(UserFunctionResponse(t.ToolUseId, t.Action, t.Result,
                    isLast ? turn.LastScreenshotPng : null));
            }
            if (turn.FinalResponse is not null)
                contents.Add(ModelTextMessage(turn.FinalResponse));
        }

        if (request.CurrentTurns.Count == 0)
        {
            contents.Add(UserMessage(request.UserPrompt, request.ScreenshotPng));
        }
        else
        {
            contents.Add(UserMessage(request.UserPrompt, null));
            for (int i = 0; i < request.CurrentTurns.Count; i++)
            {
                var t = request.CurrentTurns[i];
                bool isLast = i == request.CurrentTurns.Count - 1;
                contents.Add(ModelFunctionCall(t.ToolUseId, t.Action));
                contents.Add(UserFunctionResponse(t.ToolUseId, t.Action, t.Result,
                    isLast ? request.ScreenshotPng : null));
            }
        }
    }

    public string BuildSystemPrompt() =>
        _opts.SystemPrompt ?? "Control the computer by issuing actions. A screenshot is provided after each action.";

    private ProviderResponse ParseResponse(string json)
    {
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        int inputTokens = 0, outputTokens = 0;
        if (root.TryGetProperty("usageMetadata", out var usage))
        {
            if (usage.TryGetProperty("promptTokenCount", out var inp)) inputTokens = inp.GetInt32();
            if (usage.TryGetProperty("candidatesTokenCount", out var out_)) outputTokens = out_.GetInt32();
        }

        var parts = root.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts");
        string? thinking = null;
        string? toolUseId = null;
        AgentAction? action = null;
        var textParts = new List<string>();

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True)
            {
                thinking = part.TryGetProperty("text", out var t) ? t.GetString() : null;
            }
            else if (part.TryGetProperty("functionCall", out var fc))
            {
                var name = fc.TryGetProperty("name", out var n) ? n.GetString() : null;
                var args = fc.GetProperty("args");
                toolUseId = fc.TryGetProperty("id", out var id) ? id.GetString() : Guid.NewGuid().ToString("N");
                action = ParseGeminiAction(name, args);
            }
            else if (part.TryGetProperty("text", out var text))
            {
                textParts.Add(text.GetString() ?? string.Empty);
            }
        }

        action ??= new DoneAction(string.Join("\n", textParts));
        return new ProviderResponse(action, thinking, toolUseId, new TokenUsage(inputTokens, outputTokens));
    }

    private AgentAction ParseGeminiAction(string? name, JsonElement args)
    {
        int Px(string key) => Denorm(GetInt(args, key), _opts.DisplayWidth);
        int Py(string key) => Denorm(GetInt(args, key), _opts.DisplayHeight);

        return name switch
        {
            "click_at" => new ClickAction(MouseButton.Left, Px("x"), Py("y")),
            "hover_at" => new MouseMoveAction(Px("x"), Py("y")),
            "type_text_at" => new TypeAction(GetStr(args, "text")),
            "key_combination" => new KeyAction(GetStr(args, "keys")),
            "scroll_at" => new ScrollAction(Px("x"), Py("y"), ParseDirection(args), GetInt(args, "amount", 3)),
            "scroll_document" => new ScrollAction(_opts.DisplayWidth / 2, _opts.DisplayHeight / 2, ParseDirection(args), 3),
            "drag_and_drop" => new DragAction(Px("start_x"), Py("start_y"), Px("end_x"), Py("end_y")),
            "wait_5_seconds" => new WaitAction(5),
            _ => new ScreenshotAction()
        };
    }

    // Gemini uses a 0–999 normalized coordinate grid; map back to actual pixels.
    private static int Denorm(int norm, int dimension) => (int)Math.Round(norm * dimension / 1000.0);

    private static int GetInt(JsonElement el, string key, int fallback = 0) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : fallback;

    private static string GetStr(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) ? v.GetString() ?? string.Empty : string.Empty;

    private static ScrollDirection ParseDirection(JsonElement args)
    {
        var dir = args.TryGetProperty("direction", out var d) ? d.GetString() : null;
        return dir switch
        {
            "up" => ScrollDirection.Up,
            "down" => ScrollDirection.Down,
            "left" => ScrollDirection.Left,
            "right" => ScrollDirection.Right,
            _ => ScrollDirection.Down
        };
    }

    private JsonObject ModelFunctionCall(string id, AgentAction action)
    {
        var (name, argsObj) = ActionToGemini(action);
        return new JsonObject
        {
            ["role"] = "model",
            ["parts"] = new JsonArray
            {
                new JsonObject
                {
                    ["functionCall"] = new JsonObject { ["id"] = id, ["name"] = name, ["args"] = argsObj }
                }
            }
        };
    }

    private JsonObject UserFunctionResponse(string id, AgentAction action, string result, byte[]? screenshot)
    {
        var (name, _) = ActionToGemini(action);
        var parts = new JsonArray
        {
            new JsonObject
            {
                ["functionResponse"] = new JsonObject
                {
                    ["id"] = id,
                    ["name"] = name,
                    ["response"] = new JsonObject { ["result"] = result }
                }
            }
        };

        if (screenshot is not null)
            parts.Add(InlineImage(screenshot));

        return new JsonObject { ["role"] = "user", ["parts"] = parts };
    }

    private (string name, JsonObject args) ActionToGemini(AgentAction action)
    {
        int Nx(int px) => px * 999 / Math.Max(_opts.DisplayWidth - 1, 1);
        int Ny(int py) => py * 999 / Math.Max(_opts.DisplayHeight - 1, 1);

        return action switch
        {
            ClickAction c => ("click_at", new JsonObject { ["x"] = Nx(c.X), ["y"] = Ny(c.Y) }),
            MouseMoveAction m => ("hover_at", new JsonObject { ["x"] = Nx(m.X), ["y"] = Ny(m.Y) }),
            TypeAction t => ("type_text_at", new JsonObject { ["text"] = t.Text }),
            KeyAction k => ("key_combination", new JsonObject { ["keys"] = k.Keys }),
            ScrollAction s => ("scroll_at", new JsonObject
            {
                ["x"] = Nx(s.X), ["y"] = Ny(s.Y),
                ["direction"] = s.Direction.ToString().ToLower()
            }),
            DragAction d => ("drag_and_drop", new JsonObject
            {
                ["start_x"] = Nx(d.StartX), ["start_y"] = Ny(d.StartY),
                ["end_x"] = Nx(d.EndX), ["end_y"] = Ny(d.EndY)
            }),
            WaitAction => ("wait_5_seconds", new JsonObject()),
            _ => ("wait_5_seconds", new JsonObject())
        };
    }

    private static JsonObject UserMessage(string text, byte[]? screenshot)
    {
        var parts = new JsonArray { new JsonObject { ["text"] = text } };
        if (screenshot is not null)
            parts.Add(InlineImage(screenshot));
        return new JsonObject { ["role"] = "user", ["parts"] = parts };
    }

    private static JsonObject ModelTextMessage(string text) => new JsonObject
    {
        ["role"] = "model",
        ["parts"] = new JsonArray { new JsonObject { ["text"] = text } }
    };

    private static JsonObject InlineImage(byte[] png) => new JsonObject
    {
        ["inlineData"] = new JsonObject
        {
            ["mimeType"] = "image/png",
            ["data"] = Convert.ToBase64String(png)
        }
    };

    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }
}
