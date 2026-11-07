namespace FatePilot.Core;

// A keyboard shortcut: Ctrl and/or Alt (Shift optional) plus a letter, digit or F-key.
// Key is a Windows virtual-key code. Requiring Ctrl or Alt keeps chat typing from firing it.
public sealed class HotkeyBinding
{
    public string Action { get; set; } = "";
    public int Key { get; set; }
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
}

public static class Hotkeys
{
    public const int CtrlKey = 0x11, AltKey = 0x12, ShiftKey = 0x10, EscapeKey = 0x1B;
    // toggle = pause / resume, window = show or hide the full window.
    public static readonly string[] Actions = ["toggle", "skip", "stop", "start", "window"];

    public static bool IsAllowedKey(int key) => key is (>= 0x30 and <= 0x39) or (>= 0x41 and <= 0x5A) or (>= 0x70 and <= 0x7B);
    public static bool IsValid(HotkeyBinding b) => (b.Ctrl || b.Alt) && IsAllowedKey(b.Key) && Actions.Contains(b.Action);
    public static IEnumerable<int> CandidateKeys() =>
        Enumerable.Range(0x30, 10).Concat(Enumerable.Range(0x41, 26)).Concat(Enumerable.Range(0x70, 12));

    public static string KeyName(int key) => key switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)key).ToString(),
        >= 0x70 and <= 0x7B => "F" + (key - 0x6F),
        _ => "?",
    };

    public static string Label(HotkeyBinding b) =>
        string.Join("+", new[] { b.Ctrl ? "Ctrl" : null, b.Alt ? "Alt" : null, b.Shift ? "Shift" : null, KeyName(b.Key) }.Where(x => x != null));

    public static bool SameCombo(HotkeyBinding a, HotkeyBinding b) => a.Key == b.Key && a.Ctrl == b.Ctrl && a.Alt == b.Alt && a.Shift == b.Shift;
}

// Fires an action once per press of its exact combination (rising edge). While the player
// is typing, presses are swallowed and a combination still held afterwards does not fire.
public sealed class HotkeyTracker
{
    private readonly HashSet<string> held = [];

    public List<string> Update(Func<int, bool> isDown, bool typing, IReadOnlyList<HotkeyBinding> bindings)
    {
        var fired = new List<string>();
        bool ctrl = isDown(Hotkeys.CtrlKey), alt = isDown(Hotkeys.AltKey), shift = isDown(Hotkeys.ShiftKey);
        foreach (var b in bindings)
        {
            if (!Hotkeys.IsValid(b)) continue;
            var down = isDown(b.Key) && ctrl == b.Ctrl && alt == b.Alt && shift == b.Shift;
            var id = b.Action;
            if (!down) { held.Remove(id); continue; }
            if (held.Add(id) && !typing) fired.Add(b.Action);
        }
        return fired;
    }
}
