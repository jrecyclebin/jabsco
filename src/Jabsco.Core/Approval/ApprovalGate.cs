using Jabsco.Core.Persistence.Policies;

namespace Jabsco.Core.Approval;

public sealed class ApprovalGate
{
    private readonly PolicyMatcher _matcher;
    private readonly IApprovalSink _sink;

    public ApprovalGate(PolicyMatcher matcher, IApprovalSink sink)
    {
        _matcher = matcher;
        _sink = sink;
    }

    public async Task<ToolDecision> EvaluateAsync(
        ToolPolicy policy,
        string tool,
        string payloadJson,
        CancellationToken ct)
    {
        var decision = _matcher.Match(policy, tool, payloadJson);
        if (decision != ToolDecision.Prompt)
            return decision;

        return await _sink.RequestAsync(tool, payloadJson, TimeSpan.FromSeconds(30), ct);
    }
}
