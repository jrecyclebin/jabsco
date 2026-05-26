using Jabsco.Core.Persistence.Policies;

namespace Jabsco.Core.Approval;

public interface IApprovalSink
{
    Task<ToolDecision> RequestAsync(string tool, object payload, TimeSpan timeout, CancellationToken ct);
}
