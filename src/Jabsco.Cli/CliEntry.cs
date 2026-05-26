using System.CommandLine;
using Jabsco.Cli.Commands;

namespace Jabsco.Cli;

public static class CliEntry
{
    public static async Task<int> RunAsync(string[] args)
    {
        var rootCommand = new RootCommand("Jabsco — AI-driven RDP agent");
        rootCommand.Add(RunCommand.Build());

        var profiles = new Command("profiles", "Manage connection profiles");
        profiles.Add(ProfilesListCommand.Build());
        rootCommand.Add(profiles);

        return await rootCommand.Parse(args).InvokeAsync();
    }
}
