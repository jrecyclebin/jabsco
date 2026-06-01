using Microsoft.Data.Sqlite;

namespace Jabsco.Core.Persistence.Migrations;

internal static class MigrationRunner
{
    // Schema version — increment when adding migrations.
    private const int CurrentVersion = 1;

    private const string InitialSql = """
        CREATE TABLE IF NOT EXISTS schema_version (
          version INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS tool_policies (
          id          INTEGER PRIMARY KEY,
          name        TEXT NOT NULL UNIQUE,
          description TEXT
        );

        CREATE TABLE IF NOT EXISTS profiles (
          id              INTEGER PRIMARY KEY,
          name            TEXT,
          host            TEXT NOT NULL,
          port            INTEGER NOT NULL DEFAULT 3389,
          vm_id           TEXT,
          username        TEXT,
          credential_ref  TEXT,
          transport       TEXT NOT NULL DEFAULT 'tcp',
          resolution      TEXT NOT NULL DEFAULT '1280x800',
          last_model      TEXT,
          tool_policy_id  INTEGER REFERENCES tool_policies(id),
          created_at      TEXT NOT NULL,
          last_used_at    TEXT NOT NULL,
          use_count       INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS tool_policy_rules (
          id          INTEGER PRIMARY KEY,
          policy_id   INTEGER NOT NULL REFERENCES tool_policies(id) ON DELETE CASCADE,
          tool        TEXT NOT NULL,
          decision    TEXT NOT NULL,
          pattern     TEXT
        );

        CREATE TABLE IF NOT EXISTS runs (
          id              TEXT PRIMARY KEY,
          profile_id      INTEGER REFERENCES profiles(id),
          host            TEXT NOT NULL,
          model           TEXT NOT NULL,
          started_at      TEXT NOT NULL,
          ended_at        TEXT,
          prompt          TEXT NOT NULL,
          final_response  TEXT,
          stopped_reason  TEXT,
          steps           INTEGER,
          input_tokens    INTEGER,
          output_tokens   INTEGER,
          transcript_path TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_profiles_last_used ON profiles(last_used_at DESC);
        CREATE INDEX IF NOT EXISTS idx_runs_profile ON runs(profile_id, started_at DESC);
        """;

    internal static async Task RunAsync(string connectionString, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(connectionString);
        await db.OpenAsync(ct);

        // Check current schema version
        var version = await GetVersionAsync(db, ct);
        if (version >= CurrentVersion) return;

        // Apply initial schema
        if (version < 1)
        {
            await using var cmd = db.CreateCommand();
            cmd.CommandText = InitialSql;
            await cmd.ExecuteNonQueryAsync(ct);
            await SetVersionAsync(db, 1, ct);
        }
    }

    private static async Task<int> GetVersionAsync(SqliteConnection db, CancellationToken ct)
    {
        // Check if schema_version table exists yet
        await using var checkCmd = db.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_version'";
        var exists = (long)(await checkCmd.ExecuteScalarAsync(ct) ?? 0L) > 0;
        if (!exists) return 0;

        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_version LIMIT 1";
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is long v ? (int)v : 0;
    }

    private static async Task SetVersionAsync(SqliteConnection db, int version, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM schema_version; INSERT INTO schema_version (version) VALUES (@v)";
        cmd.Parameters.AddWithValue("@v", version);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
