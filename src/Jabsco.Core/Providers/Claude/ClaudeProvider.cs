using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jabsco.Common.Events;
using Jabsco.Core.Config;
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

    // Number of screenshots kept in context in cache-aware mode (current + history).
    public const int CacheAwareScreenshotWindow = 3;

    private string BuildRequest(ProviderRequest request)
    {
        var messages = new JsonArray();

        BuildMessages(request, messages, request.Options.Strategy);

        var tools = BuildTools(request.Options.Strategy);

        var requestObj = new JsonObject
        {
            ["model"] = _opts.Model,
            ["max_tokens"] = request.Options.MaxTokens,
            ["system"] = BuildSystemPrompt(),
            ["tools"] = tools,
            ["messages"] = messages
        };

        // Suggested effort levels from the Claude docs:
        // https://platform.claude.com/docs/en/agents-and-tools/tool-use/computer-use-tool#combining-with-extended-thinking
        var effort = "off";
        if (_opts.Model.Contains("opus-4-7")) {
            switch (_opts.Thinking) {
                case ThinkingMode.Low:
                    effort = "low";
                break;
                case ThinkingMode.High:
                    effort = "high";
                break;
            }
        } else {
            switch (_opts.Thinking) {
                case ThinkingMode.Off:
                    effort = "low";
                break;
                case ThinkingMode.Low:
                    effort = "medium";
                break;
                case ThinkingMode.High:
                    effort = "high";
                break;
            }
        }
        
        if (effort != "off")
        {
            requestObj["thinking"] = new JsonObject
            {
                ["type"] = "adaptive"
            };
            requestObj["output_config"] = new JsonObject
            {
                ["effort"] = effort
            };
        }

        return requestObj.ToJsonString();
    }

    //
    // Screenshots are attached to the last tool result in each round of model
    // work. We then attach cache_control messages to the last three screenshots
    // that aren't currently underway. (We wait for the current round to be finished
    // before caching it.)
    //
    private void BuildMessages(ProviderRequest request, JsonArray messages, ModelStrategy strategy)
    {
        var cacheSlotsLeft = CacheAwareScreenshotWindow;
        for (int i = request.History.Count - 1; i >= 0; i--)
        {
            var round = request.History[i];
            messages.Insert(0, AssistantTextMessage(round.FinalResponse ?? "[Interrupted]"));
            for (int j = round.Turns.Count - 1; j >= 0; j--)
            {
                var turn = round.Turns[j];
                var id = turn.ToolUseId;
                bool includeScreenshot = false;
                if (strategy == ModelStrategy.CacheAware)
                {
                    bool isLastTurn = j == round.Turns.Count - 1;
                    includeScreenshot = isLastTurn && round.LastScreenshotPng != null;
                    if (includeScreenshot && cacheSlotsLeft > 0)
                        cacheSlotsLeft--;
                    else
                        includeScreenshot = false; // don't include screenshot if we've exhausted cache slots
                }

                messages.Insert(0, UserToolResult(id, turn.Result,
                    includeScreenshot ? round.LastScreenshotPng : null,
                    includeScreenshot));
                messages.Insert(0, AssistantToolUse(id, turn.Action));
            }
            messages.Insert(0, UserMessageText(round.UserPrompt));
        }

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
                var id = turn.ToolUseId;
                bool isLast = i == request.CurrentTurns.Count - 1;
                messages.Add(AssistantToolUse(id, turn.Action));
                messages.Add(UserToolResult(id, turn.Result,
                    isLast ? request.ScreenshotPng : null,
                    false));
            }
        }
    }

    private JsonArray BuildTools(ModelStrategy strategy)
    {
        var computerTool = new JsonObject
        {
            ["type"] = ToolTypeFor(_opts.Model),
            ["name"] = "computer",
            ["display_width_px"] = _opts.DisplayWidth,
            ["display_height_px"] = _opts.DisplayHeight
        };
        var loadSkillTool = new JsonObject
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
        };

        if (strategy == ModelStrategy.CacheAware)
            loadSkillTool["cache_control"] = new JsonObject { ["type"] = "ephemeral" };

        return new JsonArray { computerTool, loadSkillTool };
    }

    public string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        var staticPart = _opts.SystemPrompt
            ?? "You have been given access to a user account on a machine over RDP. Screenshots will be supplied to you after every tool call and user prompt - there's no need to request additional screenshots. You can simply use the `wait` tool to request an additional screenshot after a period of time.\n\nYour cursor position is shown in each screenshot as a red arrow. Use it to orient yourself on the screen.";
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
            "wait" => new WaitAction(input.TryGetProperty("duration", out var d) ? d.GetInt32() : 1),
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

    private static JsonObject UserToolResult(string id, string result, byte[]? png, bool cacheBreakpoint = false)
    {
        var content = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = result } };
        if (png != null)
        {
            var img = ImageContent(png);
            if (cacheBreakpoint)
                img["cache_control"] = new JsonObject { ["type"] = "ephemeral" };
            content.Add(img);
        }
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
        WaitAction w => $$"""{"action":"wait","duration":{{w.Seconds}}}""",
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
