using System.CommandLine;
using Jabsco.Core.Persistence;

namespace Jabsco.Cli.Commands;

public static class ProfilesListCommand
{
    public static Command Build()
    {
        var cmd = new Command("list", "List saved connection profiles");
        cmd.SetAction(async _ =>
        {
            var db = await JabscoDb.OpenAsync();
            var profiles = await db.Profiles.GetAllAsync();

            if (profiles.Count == 0)
            {
                Console.WriteLine("No saved profiles.");
                return;
            }

            foreach (var p in profiles)
            {
                var label = p.Name ?? $"{p.Username}@{p.Host}";
                Console.WriteLine($"{p.Id,4}  {label,-40}  {p.Host}:{p.Port}  (used {p.UseCount}x)");
            }
        });
        return cmd;
    }
}
