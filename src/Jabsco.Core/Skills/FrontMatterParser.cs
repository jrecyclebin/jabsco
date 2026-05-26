namespace Jabsco.Core.Skills;

/// <summary>
/// Parses YAML front matter delimited by --- lines. Handles string scalars and
/// both inline space-separated lists and block list syntax.
/// </summary>
internal static class FrontMatterParser
{
    internal static (FrontMatter Fm, string Body) Parse(string content)
    {
        var fm = new FrontMatter();
        var lines = content.Split('\n');

        if (lines.Length < 2 || lines[0].Trim() != "---")
            return (fm, content);

        int closeIdx = -1;
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---") { closeIdx = i; break; }
        }
        if (closeIdx < 0) return (fm, content);

        string? currentKey = null;
        var listItems = new List<string>();

        void FlushList()
        {
            if (currentKey != null && listItems.Count > 0)
            {
                fm.Lists[currentKey] = [.. listItems];
                listItems.Clear();
            }
            currentKey = null;
        }

        for (int i = 1; i < closeIdx; i++)
        {
            var line = lines[i];
            var stripped = line.TrimStart();

            if (stripped.StartsWith("- "))
            {
                listItems.Add(stripped[2..].Trim());
                continue;
            }

            FlushList();

            var colon = line.IndexOf(':');
            if (colon <= 0) continue;

            var k = line[..colon].Trim();
            var v = line[(colon + 1)..].Trim();

            if (string.IsNullOrEmpty(v))
                currentKey = k;
            else
            {
                fm.Scalars[k] = v;
                currentKey = null;
            }
        }
        FlushList();

        var body = string.Join('\n', lines[(closeIdx + 1)..]).TrimStart('\n', '\r');
        return (fm, body);
    }
}

internal sealed class FrontMatter
{
    public Dictionary<string, string> Scalars { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> Lists { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? GetString(string key) => Scalars.GetValueOrDefault(key);

    public IReadOnlyList<string> GetStringList(string key)
    {
        if (Lists.TryGetValue(key, out var list)) return list;
        if (Scalars.TryGetValue(key, out var inline))
            return inline.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return [];
    }
}
