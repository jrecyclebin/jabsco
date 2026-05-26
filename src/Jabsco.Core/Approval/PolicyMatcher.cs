using System.Text.RegularExpressions;
using Jabsco.Core.Persistence.Policies;

namespace Jabsco.Core.Approval;

public sealed class PolicyMatcher
{
    // Returns the first matching rule's decision, or Prompt if no rules match.
    public ToolDecision Match(ToolPolicy policy, string tool, string payloadJson)
    {
        foreach (var rule in policy.Rules)
        {
            if (!string.Equals(rule.Tool, tool, StringComparison.OrdinalIgnoreCase)
                && rule.Tool != "*")
                continue;

            if (rule.Pattern != null && !Regex.IsMatch(payloadJson, rule.Pattern))
                continue;

            return rule.Decision;
        }
        return ToolDecision.Prompt;
    }
}
