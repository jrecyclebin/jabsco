namespace Jabsco.Core.Rdp.Input;

internal static class KeyMap
{
    // Scancode map: key name → (scancode, extended)
    // Extended flag means the key sends a 0xE0 prefix in the RDP keyboard event.
    private static readonly Dictionary<string, (byte Scancode, bool Extended)> Keys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Modifiers
            ["ctrl"]      = (0x1D, false),
            ["control"]   = (0x1D, false),
            ["shift"]     = (0x2A, false),
            ["alt"]       = (0x38, false),
            ["win"]       = (0x5B, true),
            ["super"]     = (0x5B, true),
            ["meta"]      = (0x5B, true),

            // Navigation / editing
            ["return"]    = (0x1C, false),
            ["enter"]     = (0x1C, false),
            ["escape"]    = (0x01, false),
            ["esc"]       = (0x01, false),
            ["tab"]       = (0x0F, false),
            ["backspace"] = (0x0E, false),
            ["delete"]    = (0x53, true),
            ["del"]       = (0x53, true),
            ["insert"]    = (0x52, true),
            ["ins"]       = (0x52, true),
            ["home"]      = (0x47, true),
            ["end"]       = (0x4F, true),
            ["pageup"]    = (0x49, true),
            ["page_up"]   = (0x49, true),
            ["prior"]     = (0x49, true),
            ["pagedown"]  = (0x51, true),
            ["page_down"] = (0x51, true),
            ["next"]      = (0x51, true),

            // Arrows
            ["up"]        = (0x48, true),
            ["down"]      = (0x50, true),
            ["left"]      = (0x4B, true),
            ["right"]     = (0x4D, true),

            // Function keys
            ["f1"]  = (0x3B, false),
            ["f2"]  = (0x3C, false),
            ["f3"]  = (0x3D, false),
            ["f4"]  = (0x3E, false),
            ["f5"]  = (0x3F, false),
            ["f6"]  = (0x40, false),
            ["f7"]  = (0x41, false),
            ["f8"]  = (0x42, false),
            ["f9"]  = (0x43, false),
            ["f10"] = (0x44, false),
            ["f11"] = (0x57, false),
            ["f12"] = (0x58, false),

            // Space
            ["space"] = (0x39, false),

            // Letters a–z
            ["a"] = (0x1E, false),
            ["b"] = (0x30, false),
            ["c"] = (0x2E, false),
            ["d"] = (0x20, false),
            ["e"] = (0x12, false),
            ["f"] = (0x21, false),
            ["g"] = (0x22, false),
            ["h"] = (0x23, false),
            ["i"] = (0x17, false),
            ["j"] = (0x24, false),
            ["k"] = (0x25, false),
            ["l"] = (0x26, false),
            ["m"] = (0x32, false),
            ["n"] = (0x31, false),
            ["o"] = (0x18, false),
            ["p"] = (0x19, false),
            ["q"] = (0x10, false),
            ["r"] = (0x13, false),
            ["s"] = (0x1F, false),
            ["t"] = (0x14, false),
            ["u"] = (0x16, false),
            ["v"] = (0x2F, false),
            ["w"] = (0x11, false),
            ["x"] = (0x2D, false),
            ["y"] = (0x15, false),
            ["z"] = (0x2C, false),

            // Digits
            ["0"] = (0x0B, false),
            ["1"] = (0x02, false),
            ["2"] = (0x03, false),
            ["3"] = (0x04, false),
            ["4"] = (0x05, false),
            ["5"] = (0x06, false),
            ["6"] = (0x07, false),
            ["7"] = (0x08, false),
            ["8"] = (0x09, false),
            ["9"] = (0x0A, false),
        };

    // Parses a chord string like "ctrl+c", "Return", "ctrl+shift+f5", etc.
    // Returns the scancodes in order (modifiers first).
    internal static IReadOnlyList<(byte Scancode, bool Extended)> ParseChord(string keys)
    {
        var parts = keys.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<(byte, bool)>(parts.Length);
        foreach (var part in parts)
        {
            if (Keys.TryGetValue(part, out var entry))
                result.Add(entry);
            // Unknown key names are silently skipped — caller gets best-effort.
        }
        return result;
    }
}
