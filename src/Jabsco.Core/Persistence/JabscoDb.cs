using Jabsco.Core.Persistence.Migrations;
using Jabsco.Core.Persistence.Policies;
using Jabsco.Core.Persistence.Profiles;
using Jabsco.Core.Persistence.Runs;
using Jabsco.Core.Platform;

namespace Jabsco.Core.Persistence;

public sealed class JabscoDb
{
    public string ConnectionString { get; }

    public ProfileRepository Profiles { get; }
    public ToolPolicyRepository ToolPolicies { get; }
    public RunRepository Runs { get; }

    private JabscoDb(string connectionString)
    {
        ConnectionString = connectionString;
        Profiles = new ProfileRepository(connectionString);
        ToolPolicies = new ToolPolicyRepository(connectionString);
        Runs = new RunRepository(connectionString);
    }

    public static async Task<JabscoDb> OpenAsync(string? dbPath = null, CancellationToken ct = default)
    {
        var path = dbPath ?? KnownPaths.DbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var connectionString = $"Data Source={path};Cache=Shared";
        await MigrationRunner.RunAsync(connectionString, ct);

        var db = new JabscoDb(connectionString);
        await db.ToolPolicies.SeedDefaultPoliciesAsync(ct);
        return db;
    }
}
