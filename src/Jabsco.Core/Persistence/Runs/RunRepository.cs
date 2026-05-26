using Dapper;
using Jabsco.Common.Events;
using Microsoft.Data.Sqlite;

namespace Jabsco.Core.Persistence.Runs;

public sealed class RunRepository
{
    private readonly string _connectionString;

    public RunRepository(string connectionString) => _connectionString = connectionString;

    public async Task InsertAsync(Run run, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.ExecuteAsync("""
            INSERT INTO runs (id, profile_id, host, model, started_at, ended_at, prompt,
                final_response, stopped_reason, steps, input_tokens, output_tokens, transcript_path)
            VALUES (@Id, @ProfileId, @Host, @Model, @StartedAt, @EndedAt, @Prompt,
                @FinalResponse, @StoppedReason, @Steps, @InputTokens, @OutputTokens, @TranscriptPath)
            """,
            new
            {
                run.Id, run.ProfileId, run.Host, run.Model,
                StartedAt = run.StartedAt.ToString("O"),
                EndedAt = run.EndedAt?.ToString("O"),
                run.Prompt, run.FinalResponse,
                StoppedReason = run.StoppedReason?.ToString(),
                run.Steps, run.InputTokens, run.OutputTokens, run.TranscriptPath
            });
    }

    public async Task UpdateCompletionAsync(
        string id,
        DateTimeOffset endedAt,
        string? finalResponse,
        StoppedReason? stoppedReason,
        int? steps,
        int? inputTokens,
        int? outputTokens,
        CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        await db.ExecuteAsync("""
            UPDATE runs SET ended_at = @EndedAt, final_response = @FinalResponse,
                stopped_reason = @StoppedReason, steps = @Steps,
                input_tokens = @InputTokens, output_tokens = @OutputTokens
            WHERE id = @Id
            """,
            new
            {
                EndedAt = endedAt.ToString("O"),
                FinalResponse = finalResponse,
                StoppedReason = stoppedReason?.ToString(),
                Steps = steps, InputTokens = inputTokens, OutputTokens = outputTokens,
                Id = id
            });
    }

    public async Task<IReadOnlyList<Run>> GetByProfileAsync(int profileId, int limit = 50, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        var rows = await db.QueryAsync<RunRow>(
            "SELECT * FROM runs WHERE profile_id = @profileId ORDER BY started_at DESC LIMIT @limit",
            new { profileId, limit });
        return rows.Select(RunRow.ToRun).ToList();
    }

    public async Task<Run?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connectionString);
        var row = await db.QuerySingleOrDefaultAsync<RunRow>(
            "SELECT * FROM runs WHERE id = @id", new { id });
        return row is null ? null : RunRow.ToRun(row);
    }

    private sealed class RunRow
    {
        public string id { get; set; } = string.Empty;
        public int? profile_id { get; set; }
        public string host { get; set; } = string.Empty;
        public string model { get; set; } = string.Empty;
        public string started_at { get; set; } = string.Empty;
        public string? ended_at { get; set; }
        public string prompt { get; set; } = string.Empty;
        public string? final_response { get; set; }
        public string? stopped_reason { get; set; }
        public int? steps { get; set; }
        public int? input_tokens { get; set; }
        public int? output_tokens { get; set; }
        public string transcript_path { get; set; } = string.Empty;

        public static Run ToRun(RunRow r) => new(
            Id: r.id,
            ProfileId: r.profile_id,
            Host: r.host,
            Model: r.model,
            StartedAt: DateTimeOffset.Parse(r.started_at),
            EndedAt: r.ended_at is null ? null : DateTimeOffset.Parse(r.ended_at),
            Prompt: r.prompt,
            FinalResponse: r.final_response,
            StoppedReason: r.stopped_reason is null ? null : Enum.Parse<StoppedReason>(r.stopped_reason, ignoreCase: true),
            Steps: r.steps,
            InputTokens: r.input_tokens,
            OutputTokens: r.output_tokens,
            TranscriptPath: r.transcript_path);
    }
}
