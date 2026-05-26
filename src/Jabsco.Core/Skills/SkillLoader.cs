using Jabsco.Core.Platform;

namespace Jabsco.Core.Skills;

public static class SkillLoader
{
    private const string SkillFileName = "SKILL.md";

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads and parses the named skill, optionally substituting positional arguments.
    /// Throws <see cref="FileNotFoundException"/> with a clear message if the skill is absent.
    /// </summary>
    public static SkillInfo LoadInfo(string name, string? dir = null)
    {
        dir ??= KnownPaths.SkillsDir;
        var file = FindSkillFile(name, dir)
            ?? throw new FileNotFoundException(
                $"Skill '{name}' not found. Expected: {Path.Combine(dir, name, SkillFileName)}");

        return ParseSkillFile(name, file);
    }

    /// <summary>
    /// Returns the skill body with positional arguments substituted, or throws if not found.
    /// </summary>
    public static string Load(string name, string[]? args = null, string? dir = null)
    {
        var info = LoadInfo(name, dir);
        return args is { Length: > 0 } ? SubstituteArgs(info.Content, info.Arguments, args) : info.Content;
    }

    /// <summary>
    /// Returns metadata for all available skills, sorted by key.
    /// Returns an empty list if the skills directory does not exist.
    /// </summary>
    public static IReadOnlyList<SkillInfo> List(string? dir = null)
    {
        dir ??= KnownPaths.SkillsDir;
        if (!Directory.Exists(dir)) return [];

        return Directory
            .GetDirectories(dir)
            .Select(subdir =>
            {
                var key = Path.GetFileName(subdir)!;
                var file = Path.Combine(subdir, SkillFileName);
                return File.Exists(file) ? ParseSkillFile(key, file) : null;
            })
            .Where(s => s is not null)
            .Cast<SkillInfo>()
            .OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Resolves leading /skill-name [arg1 arg2…] tokens in a prompt and prepends their
    /// content. Parsing stops at the first line that does not begin with '/'.
    /// Throws <see cref="FileNotFoundException"/> if a referenced skill is missing.
    /// </summary>
    public static string Resolve(string prompt, string? dir = null)
    {
        var lines = prompt.Split('\n');
        var blocks = new List<string>();
        int consumed = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('/') || trimmed.Length < 2) break;

            var parts = trimmed[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var skillName = parts[0];
            var args = parts.Length > 1 ? parts[1..] : null;
            blocks.Add(Load(skillName, args, dir));
            consumed++;
        }

        if (blocks.Count == 0) return prompt;

        var remainder = string.Join('\n', lines[consumed..]).Trim();
        var injected = string.Join("\n\n", blocks);
        return string.IsNullOrEmpty(remainder) ? injected : injected + "\n\n" + remainder;
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private static string? FindSkillFile(string name, string baseDir)
    {
        if (!Directory.Exists(baseDir)) return null;

        // Exact match first, then case-insensitive fallback for Linux compatibility.
        var exact = Path.Combine(baseDir, name, SkillFileName);
        if (File.Exists(exact)) return exact;

        var match = Directory
            .GetDirectories(baseDir)
            .FirstOrDefault(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));

        if (match is null) return null;
        var candidate = Path.Combine(match, SkillFileName);
        return File.Exists(candidate) ? candidate : null;
    }

    private static SkillInfo ParseSkillFile(string key, string filePath)
    {
        var raw = File.ReadAllText(filePath);
        var (fm, body) = FrontMatterParser.Parse(raw);

        var name = fm.GetString("name") ?? key;
        var description = fm.GetString("description") ?? FirstParagraph(body);
        var whenToUse = fm.GetString("when_to_use");
        var argumentHint = fm.GetString("argument-hint");
        var arguments = fm.GetStringList("arguments");

        return new SkillInfo(key, name, description, whenToUse, argumentHint, arguments, body);
    }

    private static string? FirstParagraph(string content)
    {
        var para = new List<string>();
        bool started = false;
        foreach (var line in content.Split('\n'))
        {
            if (line.StartsWith('#')) continue;
            if (string.IsNullOrWhiteSpace(line)) { if (started) break; continue; }
            started = true;
            para.Add(line.Trim());
        }
        return para.Count > 0 ? string.Join(' ', para) : null;
    }

    private static string SubstituteArgs(string content, IReadOnlyList<string> names, string[] values)
    {
        var result = content;
        for (int i = 0; i < names.Count && i < values.Length; i++)
            result = result.Replace("$" + names[i], values[i]);
        return result;
    }

}
