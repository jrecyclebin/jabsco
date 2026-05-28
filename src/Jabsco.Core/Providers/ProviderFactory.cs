using Jabsco.Core.Config;
using Jabsco.Core.Providers.Claude;
using Jabsco.Core.Providers.Gemini;

namespace Jabsco.Core.Providers;

public static class ProviderFactory
{
    public static IComputerUseProvider Create(JabscoConfig config, string? modelOverride = null)
    {
        var model = modelOverride ?? config.ModelId;

        if (model?.StartsWith("gemini", StringComparison.OrdinalIgnoreCase) == true)
        {
            var apiKey = config.GeminiApiKey ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException(
                    "No Gemini API key: set gemini_api_key in config.toml or GOOGLE_API_KEY env var.");
            var opts = new GeminiOptions(apiKey, SystemPrompt: config.Agent.SystemPrompt);
            if (model is not null) opts = opts with { Model = model };
            return new GeminiProvider(opts);
        }
        else
        {
            var apiKey = config.AnthropicApiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException(
                    "No Anthropic API key: set anthropic_api_key in config.toml or ANTHROPIC_API_KEY env var.");
            var opts = new ClaudeOptions(apiKey,
                Thinking: config.Agent.Thinking ?? ThinkingMode.Low,
                SystemPrompt: config.Agent.SystemPrompt);
            if (model is not null) opts = opts with { Model = model };
            return new ClaudeProvider(opts);
        }
    }
}
