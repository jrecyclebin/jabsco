using Dapper;
using Microsoft.Data.Sqlite;

namespace Jabsco.Core.Persistence.Policies;

public sealed class ToolPolicyRepository
{
    private readonly string _connectionString;

    public ToolPolicyRepository(string connectionString) => _connectionString = connectionString;

    public async Task<IReadOnlyList<ToolPolicy>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        var policies = (await db.QueryAsync<PolicyRow>("SELECT * FROM tool_policies")).ToList();
        var rules = (await db.QueryAsync<RuleRow>("SELECT * FROM tool_policy_rules")).ToList();

        return policies.Select(p => BuildPolicy(p, rules)).ToList();
    }

    public async Task<ToolPolicy?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        var policy = await db.QuerySingleOrDefaultAsync<PolicyRow>(
            "SELECT * FROM tool_policies WHERE name = @name", new { name });
        if (policy is null) return null;
        var rules = (await db.QueryAsync<RuleRow>(
            "SELECT * FROM tool_policy_rules WHERE policy_id = @id", new { id = policy.id })).ToList();
        return BuildPolicy(policy, rules);
    }

    public async Task SeedDefaultPoliciesAsync(CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        var existing = (await db.QueryAsync<string>("SELECT name FROM tool_policies")).ToHashSet();

        if (!existing.Contains("default"))
            await InsertAsync(new ToolPolicy(0, "default", "Standard policy — allows common actions, prompts for elevated ones", [
                new ToolPolicyRule(0, 0, "click",        ToolDecision.Allow,  null),
                new ToolPolicyRule(0, 0, "type",         ToolDecision.Allow,  null),
                new ToolPolicyRule(0, 0, "key",          ToolDecision.Allow,  null),
                new ToolPolicyRule(0, 0, "scroll",       ToolDecision.Allow,  null),
                new ToolPolicyRule(0, 0, "screenshot",   ToolDecision.Allow,  null),
                new ToolPolicyRule(0, 0, "move",         ToolDecision.Allow,  null),
                new ToolPolicyRule(0, 0, "drag",         ToolDecision.Allow,  null),
                new ToolPolicyRule(0, 0, "load_skill",   ToolDecision.Allow,  null),
                new ToolPolicyRule(0, 0, "*",            ToolDecision.Prompt, null),
            ]), ct);

        if (!existing.Contains("prompt-all"))
            await InsertAsync(new ToolPolicy(0, "prompt-all", "Prompt for every action", [
                new ToolPolicyRule(0, 0, "*", ToolDecision.Prompt, null),
            ]), ct);

        if (!existing.Contains("allow-all"))
            await InsertAsync(new ToolPolicy(0, "allow-all", "Allow everything without prompting", [
                new ToolPolicyRule(0, 0, "*", ToolDecision.Allow, null),
            ]), ct);
    }

    public async Task<ToolPolicy?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        var policy = await db.QuerySingleOrDefaultAsync<PolicyRow>(
            "SELECT * FROM tool_policies WHERE id = @id", new { id });
        if (policy is null) return null;

        var rules = (await db.QueryAsync<RuleRow>(
            "SELECT * FROM tool_policy_rules WHERE policy_id = @id", new { id })).ToList();
        return BuildPolicy(policy, rules);
    }

    public async Task<ToolPolicy> InsertAsync(ToolPolicy policy, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);

        var policyId = await db.ExecuteScalarAsync<int>("""
            INSERT INTO tool_policies (name, description) VALUES (@Name, @Description);
            SELECT last_insert_rowid();
            """,
            new { policy.Name, policy.Description }, tx);

        foreach (var rule in policy.Rules)
        {
            await db.ExecuteAsync("""
                INSERT INTO tool_policy_rules (policy_id, tool, decision, pattern)
                VALUES (@PolicyId, @Tool, @Decision, @Pattern)
                """,
                new { PolicyId = policyId, rule.Tool, Decision = rule.Decision.ToString(), rule.Pattern },
                tx);
        }

        await tx.CommitAsync(ct);

        var updatedRules = (await db.QueryAsync<RuleRow>(
            "SELECT * FROM tool_policy_rules WHERE policy_id = @policyId", new { policyId })).ToList();

        return policy with { Id = policyId, Rules = updatedRules.Select(ToRule).ToList() };
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.ExecuteAsync("DELETE FROM tool_policies WHERE id = @id", new { id });
    }

    private static ToolPolicy BuildPolicy(PolicyRow p, IEnumerable<RuleRow> allRules) =>
        new(p.id, p.name, p.description,
            allRules.Where(r => r.policy_id == p.id).Select(ToRule).ToList());

    private static ToolPolicyRule ToRule(RuleRow r) =>
        new(r.id, r.policy_id, r.tool, Enum.Parse<ToolDecision>(r.decision, ignoreCase: true), r.pattern);

    private sealed class PolicyRow
    {
        public int id { get; set; }
        public string name { get; set; } = string.Empty;
        public string? description { get; set; }
    }

    private sealed class RuleRow
    {
        public int id { get; set; }
        public int policy_id { get; set; }
        public string tool { get; set; } = string.Empty;
        public string decision { get; set; } = string.Empty;
        public string? pattern { get; set; }
    }
}
