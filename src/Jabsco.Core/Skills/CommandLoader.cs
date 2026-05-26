using Jabsco.Core.Platform;

namespace Jabsco.Core.Skills;

public static class CommandLoader
{
    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads a command file by name and substitutes $ARGUMENTS or appends trailing text.
    /// Throws <see cref="FileNotFoundException"/> with a clear message if the command is absent.
    /// </summary>
    public static string Load(string name, string arguments = "", string? dir = null)
    {
        dir ??= KnownPaths.CommandsDir;
        var file = FindCommandFile(name, dir)
            ?? throw new FileNotFoundException(
                $"Command '{name}' not found. Expected: {Path.Combine(dir, name + ".md")}");

        var (_, body) = FrontMatterParser.Parse(File.ReadAllText(file));
        return Apply(body.Trim(), arguments);
    }

    /// <summary>
    /// Returns the names of all available commands, sorted alphabetically.
    /// Returns an empty list if the commands directory does not exist.
    /// </summary>
    public static IReadOnlyList<string> List(string? dir = null)
    {
        dir ??= KnownPaths.CommandsDir;
        if (!Directory.Exists(dir)) return [];

        return Directory
            .GetFiles(dir, "*.md")
            .Select(f => Path.GetFileNameWithoutExtension(f)!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// If <paramref name="prompt"/> begins with a /name token, attempts to resolve it as a command.
    /// Returns the resolved prompt when a matching command file exists, null when not found.
    /// Any additional lines after the /name line are appended to the resolved content.
    /// </summary>
    public static string? Resolve(string prompt, string? dir = null)
    {
        dir ??= KnownPaths.CommandsDir;
        var trimmed = prompt.TrimStart();
        if (!trimmed.StartsWith('/') || trimmed.Length < 2) return null;

        var newlineIdx = trimmed.IndexOf('\n');
        var firstLine = newlineIdx < 0 ? trimmed : trimmed[..newlineIdx];
        var spaceIdx = firstLine.IndexOf(' ');

        string name, arguments;
        if (spaceIdx < 0)
        {
            name = firstLine[1..];
            arguments = "";
        }
        else
        {
            name = firstLine[1..spaceIdx];
            arguments = firstLine[(spaceIdx + 1)..].Trim();
        }

        var file = FindCommandFile(name, dir);
        if (file == null) return null;

        var (_, body) = FrontMatterParser.Parse(File.ReadAllText(file));
        var content = Apply(body.Trim(), arguments);

        var remaining = newlineIdx < 0 ? "" : trimmed[(newlineIdx + 1)..].Trim();
        return string.IsNullOrEmpty(remaining) ? content : content.TrimEnd() + "\n\n" + remaining;
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private static string? FindCommandFile(string name, string dir)
    {
        if (!Directory.Exists(dir)) return null;

        var exact = Path.Combine(dir, name + ".md");
        if (File.Exists(exact)) return exact;

        // Case-insensitive fallback for Linux
        return Directory
            .GetFiles(dir, "*.md")
            .FirstOrDefault(f => string.Equals(
                Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase));
    }

    private static string Apply(string content, string arguments)
    {
        if (content.Contains("$ARGUMENTS"))
            return content.Replace("$ARGUMENTS", arguments);
        return string.IsNullOrEmpty(arguments) ? content : content + "\n\n" + arguments;
    }
}
