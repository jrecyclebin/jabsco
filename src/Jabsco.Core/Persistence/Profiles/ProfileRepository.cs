using Dapper;
using Microsoft.Data.Sqlite;

namespace Jabsco.Core.Persistence.Profiles;

public sealed class ProfileRepository
{
    private readonly string _connectionString;

    public ProfileRepository(string connectionString) => _connectionString = connectionString;

    public async Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        var rows = await db.QueryAsync<ProfileRow>(
            "SELECT * FROM profiles ORDER BY last_used_at DESC");
        return rows.Select(ProfileRow.ToProfile).ToList();
    }

    public async Task<Profile?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        var row = await db.QuerySingleOrDefaultAsync<ProfileRow>(
            "SELECT * FROM profiles WHERE id = @id", new { id });
        return row is null ? null : ProfileRow.ToProfile(row);
    }

    public async Task<Profile> InsertAsync(Profile profile, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        var id = await db.ExecuteScalarAsync<int>("""
            INSERT INTO profiles (name, host, port, vm_id, username, credential_ref, transport, resolution,
                last_model, tool_policy_id, created_at, last_used_at, use_count)
            VALUES (@Name, @Host, @Port, @VmId, @Username, @CredentialRef, @Transport, @Resolution,
                @LastModel, @ToolPolicyId, @CreatedAt, @LastUsedAt, @UseCount);
            SELECT last_insert_rowid();
            """,
            new
            {
                profile.Name, profile.Host, profile.Port,
                VmId = profile.VmId?.ToString("D"), profile.Username,
                profile.CredentialRef, profile.Transport, profile.Resolution,
                profile.LastModel, profile.ToolPolicyId,
                CreatedAt = profile.CreatedAt.ToString("O"),
                LastUsedAt = profile.LastUsedAt.ToString("O"),
                profile.UseCount
            });
        return profile with { Id = id };
    }

    public async Task UpdateAsync(Profile profile, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.ExecuteAsync("""
            UPDATE profiles SET
                name = @Name, host = @Host, port = @Port, 
                vm_id = @VmId, username = @Username,
                credential_ref = @CredentialRef, transport = @Transport,
                resolution = @Resolution, last_model = @LastModel,
                tool_policy_id = @ToolPolicyId, last_used_at = @LastUsedAt,
                use_count = @UseCount
            WHERE id = @Id
            """,
            new
            {
                profile.Name, profile.Host, profile.Port,
                VmId = profile.VmId?.ToString("D"), profile.Username,
                profile.CredentialRef, profile.Transport, profile.Resolution,
                profile.LastModel, profile.ToolPolicyId,
                LastUsedAt = profile.LastUsedAt.ToString("O"),
                profile.UseCount, profile.Id
            });
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.ExecuteAsync("DELETE FROM runs WHERE profile_id = @id", new { id }, tx);
        await db.ExecuteAsync("DELETE FROM profiles WHERE id = @id", new { id }, tx);
        await tx.CommitAsync(ct);
    }

    public async Task<Profile?> FindAsync(string host, int port, string? username, Guid? vmGuid, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        string? vmId = vmGuid?.ToString("D");
        var sql =
            "SELECT * FROM profiles WHERE host = @host AND port = @port " +
            (username is null ? "" : "AND username = @username ") +
            (vmId is null     ? "" : "AND vm_id = @vmId ") +
            "ORDER BY last_used_at DESC LIMIT 1";
        var row = await db.QuerySingleOrDefaultAsync<ProfileRow>(sql, new { host, port, username, vmId });
        return row is null ? null : ProfileRow.ToProfile(row);
    }

    public async Task RecordUsageAsync(int id, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.ExecuteAsync(
            "UPDATE profiles SET last_used_at = @now, use_count = use_count + 1 WHERE id = @id",
            new { now = DateTimeOffset.UtcNow.ToString("O"), id });
    }

    // Dapper flat-row type for SQLite column mapping
    private sealed class ProfileRow
    {
        public int id { get; set; }
        public string? name { get; set; }
        public string host { get; set; } = string.Empty;
        public int port { get; set; }
        public string? vm_id { get; set; }
        public string? username { get; set; }
        public string? credential_ref { get; set; }
        public string transport { get; set; } = "tcp";
        public string resolution { get; set; } = "1280x800";
        public string? last_model { get; set; }
        public int? tool_policy_id { get; set; }
        public string created_at { get; set; } = string.Empty;
        public string last_used_at { get; set; } = string.Empty;
        public int use_count { get; set; }

        public static Profile ToProfile(ProfileRow r) => new(
            Id: r.id,
            Name: r.name,
            Host: r.host,
            Port: r.port,
            VmId: r.vm_id is null ? null : Guid.Parse(r.vm_id),
            Username: r.username,
            CredentialRef: r.credential_ref,
            Transport: r.transport,
            Resolution: r.resolution,
            LastModel: r.last_model,
            ToolPolicyId: r.tool_policy_id,
            CreatedAt: DateTimeOffset.Parse(r.created_at),
            LastUsedAt: DateTimeOffset.Parse(r.last_used_at),
            UseCount: r.use_count);
    }
}
