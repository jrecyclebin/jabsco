namespace Jabsco.Core.Platform;

public static class KnownPaths
{
    private static string? _overrideStateDir;

    public static string StateDir
    {
        get
        {
            if (_overrideStateDir != null) return _overrideStateDir;

            var envOverride = Environment.GetEnvironmentVariable("JABSCO_STATE_DIR");
            if (!string.IsNullOrEmpty(envOverride)) return envOverride;

            return OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jabsco")
                : Path.Combine(
                    Environment.GetEnvironmentVariable("HOME")
                        ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local", "share", "Jabsco");
        }
    }

    // For testing only
    internal static void OverrideStateDir(string? path) => _overrideStateDir = path;

    public static string DbPath => Path.Combine(StateDir, "jabsco.db");

    public static string ConfigDir
    {
        get
        {
            var envOverride = Environment.GetEnvironmentVariable("JABSCO_CONFIG_DIR");
            if (!string.IsNullOrEmpty(envOverride)) return envOverride;

            return OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jabsco")
                : Path.Combine(
                    Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                        ?? Path.Combine(
                            Environment.GetEnvironmentVariable("HOME")
                                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                            ".config"),
                    "jabsco");
        }
    }

    public static string SkillsDir => Path.Combine(ConfigDir, "skills");

    public static string CommandsDir => Path.Combine(ConfigDir, "commands");
}
