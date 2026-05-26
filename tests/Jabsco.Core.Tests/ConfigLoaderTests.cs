using Jabsco.Core.Config;

namespace Jabsco.Core.Tests;

public sealed class ConfigLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public ConfigLoaderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string ConfigPath => Path.Combine(_dir, "config.toml");

    private void WriteConfig(string content)
        => File.WriteAllText(ConfigPath, content);

    // ── Missing file ──────────────────────────────────────────────────────────

    [Fact]
    public void Load_MissingFile_CreatesTemplateAndReturnsEmpty()
    {
        var config = ConfigLoader.Load(ConfigPath);

        Assert.True(File.Exists(ConfigPath));
        Assert.Contains("anthropic_api_key", File.ReadAllText(ConfigPath));
        Assert.Null(config.AnthropicApiKey);
        Assert.Null(config.Agent.SystemPrompt);
    }

    [Fact]
    public void Load_MissingFile_CalledTwice_DoesNotThrow()
    {
        ConfigLoader.Load(ConfigPath);
        var config = ConfigLoader.Load(ConfigPath);
        Assert.Null(config.AnthropicApiKey);
    }

    // ── Key reading ───────────────────────────────────────────────────────────

    [Fact]
    public void Load_ApiKeyPresent_ReturnsIt()
    {
        WriteConfig("""anthropic_api_key = "sk-ant-test123" """);
        var config = ConfigLoader.Load(ConfigPath);
        Assert.Equal("sk-ant-test123", config.AnthropicApiKey);
    }

    [Fact]
    public void Load_ApiKeyAbsent_ReturnsNull()
    {
        WriteConfig("# no key here");
        var config = ConfigLoader.Load(ConfigPath);
        Assert.Null(config.AnthropicApiKey);
    }

    // ── System prompt ─────────────────────────────────────────────────────────

    [Fact]
    public void Load_SystemPromptPresent_ReturnsIt()
    {
        WriteConfig("""
            [agent]
            system_prompt = "Use the red arrow to orient yourself."
            """);
        var config = ConfigLoader.Load(ConfigPath);
        Assert.Equal("Use the red arrow to orient yourself.", config.Agent.SystemPrompt);
    }

    [Fact]
    public void Load_SystemPromptAbsent_ReturnsNull()
    {
        WriteConfig("""anthropic_api_key = "sk-ant-x" """);
        var config = ConfigLoader.Load(ConfigPath);
        Assert.Null(config.Agent.SystemPrompt);
    }

    [Fact]
    public void Load_AgentSectionAbsent_ReturnsNullSystemPrompt()
    {
        WriteConfig("# empty");
        var config = ConfigLoader.Load(ConfigPath);
        Assert.Null(config.Agent.SystemPrompt);
    }

    // ── Agent options ─────────────────────────────────────────────────────────

    [Fact]
    public void Load_AgentOptions_ParsesAllFields()
    {
        WriteConfig("""
            [agent]
            max_steps = 25
            post_action_delay_ms = 500
            time_budget_seconds = 120
            tool_policy = "allow"
            """);
        var a = ConfigLoader.Load(ConfigPath).Agent;
        Assert.Equal(25, a.MaxSteps);
        Assert.Equal(500, a.PostActionDelayMs);
        Assert.Equal(120, a.TimeBudgetSeconds);
        Assert.Equal("allow", a.ToolPolicy);
    }

    [Fact]
    public void Load_AgentOptions_AbsentFieldsAreNull()
    {
        WriteConfig("[agent]\nsystem_prompt = \"hi\"");
        var a = ConfigLoader.Load(ConfigPath).Agent;
        Assert.Null(a.MaxSteps);
        Assert.Null(a.PostActionDelayMs);
        Assert.Null(a.TimeBudgetSeconds);
        Assert.Null(a.ToolPolicy);
    }

    // ── Full config ───────────────────────────────────────────────────────────

    [Fact]
    public void Load_FullConfig_ParsesAllFields()
    {
        WriteConfig("""
            anthropic_api_key = "sk-ant-full"

            [agent]
            system_prompt = "You are helpful."
            max_steps = 75
            post_action_delay_ms = 1000
            time_budget_seconds = 600
            tool_policy = "deny"
            """);
        var config = ConfigLoader.Load(ConfigPath);
        Assert.Equal("sk-ant-full", config.AnthropicApiKey);
        Assert.Equal("You are helpful.", config.Agent.SystemPrompt);
        Assert.Equal(75, config.Agent.MaxSteps);
        Assert.Equal(1000, config.Agent.PostActionDelayMs);
        Assert.Equal(600, config.Agent.TimeBudgetSeconds);
        Assert.Equal("deny", config.Agent.ToolPolicy);
    }

    // ── Malformed TOML ────────────────────────────────────────────────────────

    [Fact]
    public void Load_MalformedToml_ThrowsWithClearMessage()
    {
        WriteConfig("anthropic_api_key = [unclosed");
        var ex = Assert.Throws<InvalidDataException>(() => ConfigLoader.Load(ConfigPath));
        Assert.Contains(ConfigPath, ex.Message);
    }
}
