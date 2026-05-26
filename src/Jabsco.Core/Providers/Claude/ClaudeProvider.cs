using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jabsco.Common.Events;
using Jabsco.Core.Skills;

namespace Jabsco.Core.Providers.Claude;

public sealed class ClaudeProvider : IComputerUseProvider, IDisposable
{
    private static readonly Uri ApiEndpoint = new("https://api.anthropic.com/v1/messages");
    private readonly HttpClient _http;
    private readonly ClaudeOptions _opts;
    private readonly bool _ownsHttpClient;

    public string ModelId => _opts.Model;

    public ClaudeProvider(ClaudeOptions opts, HttpClient? http = null)
    {
        _opts = opts;
        _ownsHttpClient = http is null;
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.Add("x-api-key", opts.ApiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _http.DefaultRequestHeaders.Add("anthropic-beta", BetaHeaderFor(opts.Model));
    }

    public async Task<ProviderResponse> NextActionAsync(ProviderRequest request, CancellationToken ct)
    {
        var body = BuildRequest(request);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(ApiEndpoint, content, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Anthropic API {(int)response.StatusCode}: {json}", null, response.StatusCode);
        return ParseResponse(json);
    }

    private string BuildRequest(ProviderRequest request)
    {
        var messages = new JsonArray();

        // Replay complete past rounds — no screenshots, just text and tool exchanges
        foreach (var round in request.History)
        {
            messages.Add(UserMessageText(round.UserPrompt));
            foreach (var turn in round.Turns)
            {
                var id = turn.ToolUseId ?? $"tool_{messages.Count}";
                messages.Add(AssistantToolUse(id, turn.Action));
                messages.Add(UserToolResult(id, turn.Result, png: null));
            }
            // Null = interrupted round; synthetic assistant message keeps user/assistant alternation valid
            messages.Add(AssistantTextMessage(round.FinalResponse ?? "[Interrupted]"));
        }

        // Current prompt: screenshot goes with it on the first step, or on the last tool result otherwise
        if (request.CurrentTurns.Count == 0)
        {
            messages.Add(UserMessage(request.UserPrompt, request.ScreenshotPng));
        }
        else
        {
            messages.Add(UserMessageText(request.UserPrompt));
            for (int i = 0; i < request.CurrentTurns.Count; i++)
            {
                var turn = request.CurrentTurns[i];
                var id = turn.ToolUseId ?? $"tool_{i}";
                bool isLast = i == request.CurrentTurns.Count - 1;
                messages.Add(AssistantToolUse(id, turn.Action));
                messages.Add(UserToolResult(id, turn.Result, isLast ? request.ScreenshotPng : null));
            }
        }

        var system = BuildSystemPrompt();
        var tools = new JsonArray
        {
            new JsonObject
            {
                ["type"] = ToolTypeFor(_opts.Model),
                ["name"] = "computer",
                ["display_width_px"] = _opts.DisplayWidth,
                ["display_height_px"] = _opts.DisplayHeight
            },
            new JsonObject
            {
                ["name"] = "load_skill",
                ["description"] = "Load a skill's full instructions by its internal name.",
                ["input_schema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["name"] = new JsonObject { ["type"] = "string" }
                    },
                    ["required"] = new JsonArray { "name" }
                }
            }
        };

        var requestObj = new JsonObject
        {
            ["model"] = _opts.Model,
            ["max_tokens"] = request.Options.MaxTokens,
            ["system"] = system,
            ["tools"] = tools,
            ["messages"] = messages
        };

        if (_opts.ExtendedThinking)
        {
            requestObj["thinking"] = new JsonObject
            {
                ["type"] = "enabled",
                ["budget_tokens"] = 1024
            };
        }

        return requestObj.ToJsonString();
    }

    public string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        var staticPart = _opts.SystemPrompt
            ?? "Your cursor position is shown in each screenshot as a red arrow. Use it to orient yourself on the screen.";
        sb.AppendLine(staticPart);

        var skills = SkillLoader.List();
        if (skills.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Available skills");
            sb.AppendLine("Use `load_skill` to fetch a skill's full instructions before applying it. Once a skill is loaded, you shouldn't need to load it again.");
            foreach (var s in skills)
            {
                sb.Append($"- **{s.Name}** (internal name: `{s.Key}`)");
                if (!string.IsNullOrEmpty(s.ListingDescription))
                    sb.Append($": {s.ListingDescription}");
                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static ProviderResponse ParseResponse(string json)
    {
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        int inputTokens = 0;
        int outputTokens = 0;
        int cachedInputTokens = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("input_tokens", out var inp)) inputTokens = inp.GetInt32();
            if (usage.TryGetProperty("output_tokens", out var out_)) outputTokens = out_.GetInt32();
            if (usage.TryGetProperty("cache_read_input_tokens", out var cached)) cachedInputTokens = cached.GetInt32();
        }
        var tokenUsage = new TokenUsage(inputTokens, outputTokens, cachedInputTokens);

        string? thinking = null;
        AgentAction? action = null;
        string? toolUseId = null;
        var textParts = new List<string>();

        foreach (var block in root.GetProperty("content").EnumerateArray())
        {
            var type = block.GetProperty("type").GetString();
            switch (type)
            {
                case "thinking":
                    thinking = block.GetProperty("thinking").GetString();
                    break;
                case "text":
                    textParts.Add(block.GetProperty("text").GetString() ?? string.Empty);
                    break;
                case "tool_use" when block.GetProperty("name").GetString() == "computer":
                    toolUseId = block.GetProperty("id").GetString();
                    action = ParseAction(block.GetProperty("input"));
                    break;
                case "tool_use" when block.GetProperty("name").GetString() == "load_skill":
                    toolUseId = block.GetProperty("id").GetString();
                    var skillName = block.GetProperty("input").GetProperty("name").GetString() ?? string.Empty;
                    action = new LoadSkillAction(skillName);
                    break;
            }
        }

        // No tool_use means the model is done
        action ??= new DoneAction(string.Join("\n", textParts));
        return new ProviderResponse(action, thinking, toolUseId, tokenUsage);
    }

    private static AgentAction ParseAction(JsonElement input)
    {
        var actionType = input.GetProperty("action").GetString();
        return actionType switch
        {
            "screenshot" => new ScreenshotAction(),
            "left_click" => new ClickAction(MouseButton.Left, GetX(input), GetY(input)),
            "right_click" => new ClickAction(MouseButton.Right, GetX(input), GetY(input)),
            "middle_click" => new ClickAction(MouseButton.Middle, GetX(input), GetY(input)),
            "double_click" => new ClickAction(MouseButton.Left, GetX(input), GetY(input), 2),
            "mouse_move" => new MouseMoveAction(GetX(input), GetY(input)),
            "left_click_drag" => new DragAction(
                GetCoordinate(input, "start_coordinate", 0),
                GetCoordinate(input, "start_coordinate", 1),
                GetX(input),
                GetY(input)),
            "scroll" => new ScrollAction(GetX(input), GetY(input), ParseDirection(input), GetAmount(input)),
            "key" => new KeyAction(input.GetProperty("text").GetString() ?? string.Empty),
            "type" => new TypeAction(input.GetProperty("text").GetString() ?? string.Empty),
            _ => new ScreenshotAction()
        };
    }

    private static int GetX(JsonElement input) => GetCoordinate(input, "coordinate", 0);
    private static int GetY(JsonElement input) => GetCoordinate(input, "coordinate", 1);

    private static int GetCoordinate(JsonElement input, string prop, int index)
    {
        if (input.TryGetProperty(prop, out var coord))
            return coord[index].GetInt32();
        return 0;
    }

    private static int GetAmount(JsonElement input) =>
        input.TryGetProperty("amount", out var a) ? a.GetInt32() : 3;

    private static ScrollDirection ParseDirection(JsonElement input)
    {
        var dir = input.TryGetProperty("direction", out var d) ? d.GetString() : null;
        return dir switch
        {
            "up" => ScrollDirection.Up,
            "down" => ScrollDirection.Down,
            "left" => ScrollDirection.Left,
            "right" => ScrollDirection.Right,
            _ => ScrollDirection.Down
        };
    }

    private static JsonObject AssistantToolUse(string id, AgentAction action)
    {
        var (toolName, inputJson) = action is LoadSkillAction ls
            ? ("load_skill", $$$"""{"name":"{{{ls.SkillName}}}"}""")
            : ("computer", ActionToJson(action));

        return new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "tool_use",
                    ["id"] = id,
                    ["name"] = toolName,
                    ["input"] = JsonNode.Parse(inputJson)
                }
            }
        };
    }

    private static JsonObject UserToolResult(string id, string result, byte[]? png)
    {
        var content = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = result } };
        if (png != null) content.Add(ImageContent(png));
        return new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray
            {
                new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = id, ["content"] = content }
            }
        };
    }

    private static JsonObject AssistantTextMessage(string text) => new JsonObject
    {
        ["role"] = "assistant",
        ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } }
    };

    private static JsonObject UserMessage(string text, byte[] png) => new JsonObject
    {
        ["role"] = "user",
        ["content"] = new JsonArray
        {
            new JsonObject { ["type"] = "text", ["text"] = text },
            ImageContent(png)
        }
    };

    private static JsonObject UserMessageText(string text) => new JsonObject
    {
        ["role"] = "user",
        ["content"] = new JsonArray
        {
            new JsonObject { ["type"] = "text", ["text"] = text }
        }
    };

    private static JsonObject ImageContent(byte[] png) => new JsonObject
    {
        ["type"] = "image",
        ["source"] = new JsonObject
        {
            ["type"] = "base64",
            ["media_type"] = "image/png",
            ["data"] = Convert.ToBase64String(png)
        }
    };

    private static string ActionToJson(AgentAction action) => action switch
    {
        ScreenshotAction => """{"action":"screenshot"}""",
        ClickAction c => $$"""{"action":"{{ButtonName(c.Button)}}_click","coordinate":[{{c.X}},{{c.Y}}]}""",
        MouseMoveAction m => $$"""{"action":"mouse_move","coordinate":[{{m.X}},{{m.Y}}]}""",
        DragAction d => $$"""{"action":"left_click_drag","start_coordinate":[{{d.StartX}},{{d.StartY}}],"coordinate":[{{d.EndX}},{{d.EndY}}]}""",
        ScrollAction s => $$"""{"action":"scroll","coordinate":[{{s.X}},{{s.Y}}],"direction":"{{s.Direction.ToString().ToLower()}}","amount":{{s.Amount}}}""",
        KeyAction k => $$"""{"action":"key","text":"{{k.Keys}}"}""",
        TypeAction t => $$"""{"action":"type","text":{{JsonSerializer.Serialize(t.Text)}}}""",
        DoneAction => """{"action":"screenshot"}""",
        _ => """{"action":"screenshot"}"""
    };

    private static string ButtonName(MouseButton b) => b switch
    {
        MouseButton.Left => "left",
        MouseButton.Right => "right",
        MouseButton.Middle => "middle",
        _ => "left"
    };

    // computer_20251124 + computer-use-2025-11-24: Opus 4.7, Opus 4.6, Sonnet 4.6, Opus 4.5
    // computer_20250124 + computer-use-2025-01-24: Sonnet 4.5, Haiku 4.5, Opus 4.1, older
    private static bool UsesNewComputerTool(string model) =>
        model.Contains("opus-4-7") || model.Contains("opus-4-6") ||
        model.Contains("sonnet-4-6") || model.Contains("opus-4-5");

    private static string ToolTypeFor(string model) =>
        UsesNewComputerTool(model) ? "computer_20251124" : "computer_20250124";

    private static string BetaHeaderFor(string model) =>
        UsesNewComputerTool(model) ? "computer-use-2025-11-24" : "computer-use-2025-01-24";

    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }
}
