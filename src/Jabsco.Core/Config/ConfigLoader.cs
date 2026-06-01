using Jabsco.Core.Platform;
using Tomlyn;

namespace Jabsco.Core.Config;

public static class ConfigLoader
{
    private const string ConfigFileName = "config.toml";

    private static readonly string DefaultTemplate = """"
        # Jabsco configuration
        # Uncomment and set the values you need.

        # Your Anthropic API key. Overrides the ANTHROPIC_API_KEY environment variable.
        # anthropic_api_key = "sk-ant-..."

        # Your Google AI Studio API key for Gemini models. Overrides GOOGLE_API_KEY.
        # gemini_api_key = "AIza..."

        # Model to use across all LLM providers (overrides --model and built-in defaults).
        # model_id = "claude-opus-4-7"

        # [agent]
        # Override the default system prompt sent to the model.
        # system_prompt = """
        # Your cursor position is shown as a red arrow.
        # Additional instructions here.
        # """
        #
        # Maximum number of agent steps before stopping.
        # max_steps = 50
        #
        # Delay in milliseconds between a tool result and the next screenshot.
        # post_action_delay_ms = 800
        #
        # Hard time limit in seconds for an agent run (omit for no limit).
        # time_budget_seconds = 300
        #
        # Tool approval policy: "allow" auto-approves all tool calls, "deny" blocks them.
        # tool_policy = "allow"
        #
        # Screenshot strategy. "cache_aware" (default) sends a screenshot after every turn,
        # pruning to the latest 3 every 25 turns, with cache_control breakpoints.
        # "model_managed" only sends one after each prompt and each screenshot action.
        # "latest_only" sends only the current screenshot each turn.
        # model_strategy = "cache_aware"
        #
        # Extended thinking effort: "low" (default), "high" (best accuracy), or "off".
        # thinking = "low"
        #
        # [feature]
        # Enable experimental features.
        # hyperv = true
        """";

    public static JabscoConfig Load(string? path = null)
    {
        path ??= Path.Combine(KnownPaths.ConfigDir, ConfigFileName);

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, DefaultTemplate);
            return new JabscoConfig(null, null, null, new AgentConfig(null, null, null, null, null), new FeatureFlags());
        }

        var content = File.ReadAllText(path);

        ConfigDto? dto;
        try { dto = TomlSerializer.Deserialize<ConfigDto>(content); }
        catch (TomlException ex)
        {
            throw new InvalidDataException($"Malformed config {path}:\n{ex.Message}", ex);
        }

        var a = dto?.agent;
        var f = dto?.feature;

        ModelStrategy? modelStrategy = a?.model_strategy?.ToLowerInvariant() switch
        {
            "cache_aware" or "cacheaware" => ModelStrategy.CacheAware,
            "latest_only" or "latestonly" => ModelStrategy.LatestOnly,
            "model_managed" or "modelmanaged" => ModelStrategy.ModelManaged,
            _ => null
        };

        ThinkingMode? thinking = a?.thinking?.ToLowerInvariant() switch
        {
            "off"  => ThinkingMode.Off,
            "low"  => ThinkingMode.Low,
            "high" => ThinkingMode.High,
            _ => null
        };

        return new JabscoConfig(
            dto?.anthropic_api_key,
            dto?.gemini_api_key,
            dto?.model_id,
            new AgentConfig(
                a?.system_prompt,
                a?.max_steps,
                a?.post_action_delay_ms,
                a?.time_budget_seconds,
                a?.tool_policy,
                modelStrategy,
                thinking),
            new FeatureFlags(
                HyperV: f?.hyperv ?? false));
    }

    // Internal DTO with snake_case names matching TOML keys
    private sealed class ConfigDto
    {
        public string? anthropic_api_key { get; set; }
        public string? gemini_api_key { get; set; }
        public string? model_id { get; set; }
        public AgentDto? agent { get; set; }
        public FeatureDto? feature { get; set; }
    }

    private sealed class FeatureDto
    {
        public bool? hyperv { get; set; }
    }

    private sealed class AgentDto
    {
        public string? system_prompt { get; set; }
        public int? max_steps { get; set; }
        public int? post_action_delay_ms { get; set; }
        public int? time_budget_seconds { get; set; }
        public string? tool_policy { get; set; }
        public string? model_strategy { get; set; }
        public string? thinking { get; set; }
    }
}
