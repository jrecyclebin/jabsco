using System.Collections.Generic;
using Avalonia.Input;

namespace Jabsco.Ui.Controls;

public static class KeyTranslator
{
    public static string? ToChord(KeyEventArgs e)
    {
        var parts = new List<string>();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) parts.Add("ctrl");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))   parts.Add("shift");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))     parts.Add("alt");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta))    parts.Add("Win");

        var key = e.Key switch
        {
            Key.Return or Key.Enter => "Return",
            Key.Escape              => "Escape",
            Key.Tab                 => "Tab",
            Key.Back                => "BackSpace",
            Key.Delete              => "Delete",
            Key.Insert              => "Insert",
            Key.Home                => "Home",
            Key.End                 => "End",
            Key.PageUp              => "PageUp",
            Key.PageDown            => "PageDown",
            Key.Up                  => "Up",
            Key.Down                => "Down",
            Key.Left                => "Left",
            Key.Right               => "Right",
            Key.F1  => "F1",  Key.F2  => "F2",  Key.F3  => "F3",  Key.F4  => "F4",
            Key.F5  => "F5",  Key.F6  => "F6",  Key.F7  => "F7",  Key.F8  => "F8",
            Key.F9  => "F9",  Key.F10 => "F10", Key.F11 => "F11", Key.F12 => "F12",
            Key k when k >= Key.A && k <= Key.Z   => k.ToString().ToLower(),
            Key k when k >= Key.D0 && k <= Key.D9 => ((int)(k - Key.D0)).ToString(),
            _ => null
        };

        if (key == null) return null;

        // Ctrl+Alt+End → Ctrl+Alt+Del (Windows intercepts the real chord)
        if (parts.Contains("ctrl") && parts.Contains("alt") && key == "End")
            key = "Delete";

        parts.Add(key);
        return string.Join("+", parts);
    }
}
