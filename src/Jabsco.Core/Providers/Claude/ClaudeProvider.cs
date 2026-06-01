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

    private string BuildRequest(ProviderRequest request)
    {
        var messages = new JsonArray();

        BuildMessages(request, messages);

        var tools = BuildTools(_opts, request.Options.Strategy, request.Options.HasScreen, request.Options.HasVmHost);

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

    // AgentLoop has already pruned screenshots to what the strategy keeps, so a screenshot
    // present on a turn or round is attached as-is. cache_control goes on the load_skill
    // tool (see BuildTools) plus the latest few settled (history) screenshots.
    private void BuildMessages(ProviderRequest request, JsonArray messages)
    {
        var cached = CacheBreakpoints(request.History);

        for (int r = 0; r < request.History.Count; r++)
        {
            var round = request.History[r];
            messages.Add(UserMessage(round.UserPrompt, Image(round.PromptScreenshotPng)));
            foreach (var turn in round.Turns)
            {
                messages.Add(AssistantToolUse(turn.ToolUseId, turn.Action));
                messages.Add(UserToolResult(turn.ToolUseId, turn.Result, Image(turn.ScreenshotPng)));
            }
            messages.Add(AssistantTextMessage(round.FinalResponse ?? "[Interrupted]"));
        }

        messages.Add(UserMessage(request.UserPrompt, Image(request.PromptScreenshotPng)));
        foreach (var turn in request.CurrentTurns)
        {
            messages.Add(AssistantToolUse(turn.ToolUseId, turn.Action));
            messages.Add(UserToolResult(turn.ToolUseId, turn.Result, Image(turn.ScreenshotPng)));
        }

        // With no screen there's no frame to show; the observation text is the model's
        // current view of the connection, placed last so it's the freshest input.
        if (!string.IsNullOrEmpty(request.Observation))
            messages.Add(UserMessage(request.Observation, Image(null)));

        (byte[]? png, bool cache) Image(byte[]? png) => (png, png != null && cached.Contains(png));
    }

    // The latest few history screenshots carry cache breakpoints. History is already pruned,
    // so these are simply the newest present screenshots, walked from the end. Current-round
    // screenshots are never cached - we wait for a round to settle first.
    public static HashSet<byte[]> CacheBreakpoints(IReadOnlyList<ConversationTurn> history)
    {
        var cached = new HashSet<byte[]>(ReferenceEqualityComparer.Instance);
        for (int r = history.Count - 1; r >= 0 && cached.Count < ScreenshotPlan.Window; r--)
        {
            var round = history[r];
            for (int j = round.Turns.Count - 1; j >= 0 && cached.Count < ScreenshotPlan.Window; j--)
                if (round.Turns[j].ScreenshotPng is { } png)
                    cached.Add(png);
            if (cached.Count < ScreenshotPlan.Window && round.PromptScreenshotPng is { } prompt)
                cached.Add(prompt);
        }
        return cached;
    }

    // The tool set is gated by connection capability: the computer tool only when there's a
    // screen; vm tools only on a Hyper-V host. load_skill is always available.
    public static JsonArray BuildTools(ClaudeOptions opts, ModelStrategy strategy, bool hasScreen, bool hasVmHost)
    {
        var tools = new JsonArray();

        if (hasScreen)
            tools.Add(new JsonObject
            {
                ["type"] = ToolTypeFor(opts.Model),
                ["name"] = "computer",
                ["display_width_px"] = opts.DisplayWidth,
                ["display_height_px"] = opts.DisplayHeight
            });

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
        if (strategy is ModelStrategy.CacheAware or ModelStrategy.ModelManaged)
            loadSkillTool["cache_control"] = new JsonObject { ["type"] = "ephemeral" };
        tools.Add(loadSkillTool);

        tools.Add(new JsonObject
        {
            ["name"] = "switch",
            ["description"] = "Change what you are connected to. Provide exactly one of: "
                + "'profile' (a saved connection name), 'vm_id' (a VM GUID on the current Hyper-V host), "
                + "or 'disconnect': true. After switching, your next observation reflects the new connection.",
            ["input_schema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["profile"] = new JsonObject { ["type"] = "string" },
                    ["vm_id"] = new JsonObject { ["type"] = "string" },
                    ["disconnect"] = new JsonObject { ["type"] = "boolean" }
                }
            }
        });

        if (hasVmHost)
            tools.Add(new JsonObject
            {
                ["name"] = "vm_action",
                ["description"] = "Perform a lifecycle action on a Hyper-V VM. 'operation' is one of: "
                    + "start, shutdown (graceful, always preferred), poweroff (force off), save, pause, resume, restart. "
                    + "Omit 'vm_id' to act on the VM you're connected to, or pass a VM GUID to act on another VM on this host.",
                ["input_schema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["operation"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray { "start", "shutdown", "poweroff", "save", "pause", "resume", "restart" }
                        },
                        ["vm_id"] = new JsonObject { ["type"] = "string" }
                    },
                    ["required"] = new JsonArray { "operation" }
                }
            });

        if (hasVmHost)
            tools.Add(new JsonObject
            {
                ["name"] = "vm_setup",
                ["description"] = "Create or reconfigure a Hyper-V VM. Pass 'vm_id' to alter an existing VM "
                    + "(only the fields you include change); omit it to create a new VM ('name' and "
                    + "'generation' are required for create). Hardware changes (memory, processor_count, "
                    + "vhd_size_gb, iso_path, tpm, secure_boot, network_adapter, generation) require the VM "
                    + "to be powered off first; checkpoints, guest_services, and enhanced_session can change "
                    + "while it runs. Sizes are in MB (memory) and GB (disk).",
                ["input_schema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["vm_id"] = new JsonObject { ["type"] = "string" },
                        ["name"] = new JsonObject { ["type"] = "string" },
                        ["generation"] = new JsonObject { ["type"] = "integer", ["enum"] = new JsonArray { 1, 2 } },
                        ["memory_mb"] = new JsonObject { ["type"] = "integer" },
                        ["vhd_size_gb"] = new JsonObject { ["type"] = "integer" },
                        ["iso_path"] = new JsonObject { ["type"] = "string" },
                        ["tpm"] = new JsonObject { ["type"] = "boolean" },
                        ["secure_boot"] = new JsonObject { ["type"] = "boolean" },
                        ["processor_count"] = new JsonObject { ["type"] = "integer" },
                        ["network_adapter"] = new JsonObject { ["type"] = "string" },
                        ["checkpoints"] = new JsonObject { ["type"] = "boolean" },
                        ["guest_services"] = new JsonObject { ["type"] = "boolean" },
                        ["enhanced_session"] = new JsonObject { ["type"] = "boolean" }
                    }
                }
            });

        return tools;
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
                case "tool_use" when block.GetProperty("name").GetString() == "switch":
                    toolUseId = block.GetProperty("id").GetString();
                    action = ParseSwitch(block.GetProperty("input"));
                    break;
                case "tool_use" when block.GetProperty("name").GetString() == "vm_action":
                    toolUseId = block.GetProperty("id").GetString();
                    action = ParseVmAction(block.GetProperty("input"));
                    break;
                case "tool_use" when block.GetProperty("name").GetString() == "vm_setup":
                    toolUseId = block.GetProperty("id").GetString();
                    action = ParseVmSetup(block.GetProperty("input"));
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

    private static string SwitchToJson(SwitchAction sw)
    {
        var o = new JsonObject();
        if (sw.Disconnect) o["disconnect"] = true;
        else if (sw.VmId is { } vmId) o["vm_id"] = vmId.ToString();
        else if (sw.Profile is { } profile) o["profile"] = profile;
        return o.ToJsonString();
    }

    private static string VmActionToJson(VmAction va)
    {
        var o = new JsonObject { ["operation"] = va.Operation.ToToken() };
        if (va.VmId is { } vmId) o["vm_id"] = vmId.ToString();
        return o.ToJsonString();
    }

    private static string VmSetupToJson(VmSetupAction vs)
    {
        var o = new JsonObject();
        if (vs.VmId is { } vmId) o["vm_id"] = vmId.ToString();
        var s = vs.Spec;
        if (s.Name is { } name) o["name"] = name;
        if (s.Generation is { } gen) o["generation"] = (int)gen;
        if (s.MemoryMB is { } mem) o["memory_mb"] = mem;
        if (s.VhdSizeGB is { } vhd) o["vhd_size_gb"] = vhd;
        if (s.IsoPath is { } iso) o["iso_path"] = iso;
        if (s.Tpm is { } tpm) o["tpm"] = tpm;
        if (s.SecureBoot is { } sb) o["secure_boot"] = sb;
        if (s.ProcessorCount is { } cpu) o["processor_count"] = cpu;
        if (s.NetworkAdapter is { } nic) o["network_adapter"] = nic;
        if (s.Checkpoints is { } cp) o["checkpoints"] = cp;
        if (s.GuestServices is { } gs) o["guest_services"] = gs;
        if (s.EnhancedSession is { } es) o["enhanced_session"] = es;
        return o.ToJsonString();
    }

    private static SwitchAction ParseSwitch(JsonElement input)
    {
        string? profile = input.TryGetProperty("profile", out var p) ? p.GetString() : null;
        Guid? vmId = input.TryGetProperty("vm_id", out var v) && Guid.TryParse(v.GetString(), out var g) ? g : null;
        bool disconnect = input.TryGetProperty("disconnect", out var d) && d.GetBoolean();
        return new SwitchAction(profile, vmId, disconnect);
    }

    private static AgentAction ParseVmAction(JsonElement input)
    {
        var op = VmOperations.Parse(input.TryGetProperty("operation", out var o) ? o.GetString() : null);
        if (op is null) return new DoneAction("Error: vm_action needs a valid operation.");
        Guid? vmId = input.TryGetProperty("vm_id", out var v) && Guid.TryParse(v.GetString(), out var g) ? g : null;
        return new VmAction(op.Value, vmId);
    }

    private static AgentAction ParseVmSetup(JsonElement input)
    {
        Guid? vmId = input.TryGetProperty("vm_id", out var v) && Guid.TryParse(v.GetString(), out var g) ? g : null;
        var spec = new VmSpec(
            Name:           Str(input, "name"),
            Generation:     input.TryGetProperty("generation", out var gen) && gen.TryGetInt32(out var gn) ? (VmGeneration)gn : null,
            MemoryMB:       Int(input, "memory_mb"),
            VhdSizeGB:      Int(input, "vhd_size_gb"),
            IsoPath:        Str(input, "iso_path"),
            Tpm:            Bool(input, "tpm"),
            SecureBoot:     Bool(input, "secure_boot"),
            ProcessorCount: Int(input, "processor_count"),
            NetworkAdapter: Str(input, "network_adapter"),
            Checkpoints:    Bool(input, "checkpoints"),
            GuestServices:  Bool(input, "guest_services"),
            EnhancedSession: Bool(input, "enhanced_session"));
        return new VmSetupAction(spec, vmId);
    }

    private static string? Str(JsonElement input, string prop) =>
        input.TryGetProperty(prop, out var e) ? e.GetString() : null;
    private static int? Int(JsonElement input, string prop) =>
        input.TryGetProperty(prop, out var e) && e.TryGetInt32(out var i) ? i : null;
    private static bool? Bool(JsonElement input, string prop) =>
        input.TryGetProperty(prop, out var e) ? e.GetBoolean() : null;

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
        var (toolName, inputJson) = action switch
        {
            LoadSkillAction ls => ("load_skill", $$$"""{"name":"{{{ls.SkillName}}}"}"""),
            SwitchAction sw    => ("switch", SwitchToJson(sw)),
            VmAction va        => ("vm_action", VmActionToJson(va)),
            VmSetupAction vs   => ("vm_setup", VmSetupToJson(vs)),
            _                  => ("computer", ActionToJson(action))
        };

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

    private static JsonObject UserToolResult(string id, string result, (byte[]? Png, bool Cache) image)
        => UserToolResult(id, result, image.Png, image.Cache);

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

    private static JsonObject UserMessage(string text, (byte[]? Png, bool Cache) image)
    {
        var content = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } };
        if (image.Png != null)
        {
            var img = ImageContent(image.Png);
            if (image.Cache)
                img["cache_control"] = new JsonObject { ["type"] = "ephemeral" };
            content.Add(img);
        }
        return new JsonObject { ["role"] = "user", ["content"] = content };
    }

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
