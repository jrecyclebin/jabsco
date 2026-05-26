namespace Jabsco.Core.Skills;

/// <summary>
/// Parsed metadata and body content of a skill loaded from skills/&lt;name&gt;/SKILL.md.
/// </summary>
public sealed record SkillInfo(
    /// <summary>Directory name; used as the lookup key.</summary>
    string Key,
    /// <summary>Display name from front matter, or Key if omitted.</summary>
    string Name,
    /// <summary>From front matter, or the first paragraph of content if omitted.</summary>
    string? Description,
    /// <summary>Additional trigger context from front matter.</summary>
    string? WhenToUse,
    /// <summary>Autocomplete argument hint from front matter.</summary>
    string? ArgumentHint,
    /// <summary>Named positional argument identifiers used for $name substitution.</summary>
    IReadOnlyList<string> Arguments,
    /// <summary>Skill body with front matter stripped.</summary>
    string Content)
{
    /// <summary>
    /// Combined description for the skill listing, truncated at 1,536 characters.
    /// </summary>
    public string ListingDescription
    {
        get
        {
            const int MaxLen = 1536;
            var combined = WhenToUse is { Length: > 0 }
                ? $"{Description} {WhenToUse}"
                : Description ?? string.Empty;
            return combined.Length <= MaxLen ? combined : combined[..MaxLen];
        }
    }
}
