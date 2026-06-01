using System.Text.Json.Nodes;
using Jabsco.Core.Config;
using Jabsco.Core.Providers.Claude;

namespace Jabsco.Core.Tests;

// The tool set the model is offered is gated by connection capability: the computer tool
// only appears when there's a screen; load_skill is always available.
public sealed class ClaudeToolGatingTests
{
    private static readonly ClaudeOptions Opts = new(ApiKey: "k");

    private static List<string> ToolNames(bool hasScreen, bool hasVmHost)
    {
        var tools = ClaudeProvider.BuildTools(Opts, ModelStrategy.CacheAware, hasScreen, hasVmHost);
        return tools.Select(t => t!["name"]!.GetValue<string>()).ToList();
    }

    [Fact]
    public void WithScreen_OffersComputerTool()
    {
        Assert.Contains("computer", ToolNames(hasScreen: true, hasVmHost: false));
    }

    [Fact]
    public void WithoutScreen_OmitsComputerTool()
    {
        Assert.DoesNotContain("computer", ToolNames(hasScreen: false, hasVmHost: false));
    }

    [Fact]
    public void LoadSkill_AlwaysAvailable()
    {
        Assert.Contains("load_skill", ToolNames(hasScreen: true, hasVmHost: true));
        Assert.Contains("load_skill", ToolNames(hasScreen: false, hasVmHost: false));
    }

    [Fact]
    public void Switch_AlwaysAvailable()
    {
        Assert.Contains("switch", ToolNames(hasScreen: true, hasVmHost: true));
        Assert.Contains("switch", ToolNames(hasScreen: false, hasVmHost: false));
    }

    [Fact]
    public void VmAction_OnlyWithVmHost()
    {
        Assert.Contains("vm_action", ToolNames(hasScreen: false, hasVmHost: true));
        Assert.DoesNotContain("vm_action", ToolNames(hasScreen: true, hasVmHost: false));
    }

    [Fact]
    public void VmSetup_OnlyWithVmHost()
    {
        Assert.Contains("vm_setup", ToolNames(hasScreen: false, hasVmHost: true));
        Assert.DoesNotContain("vm_setup", ToolNames(hasScreen: true, hasVmHost: false));
    }
}
