using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DiscoVoiceSpeed;

/// <summary>
/// A configurable hotkey, parsed from text like "Ctrl + Equals, Ctrl + KeypadPlus".
/// Each comma-separated alternative is one key combination: modifiers plus one key,
/// joined with '+'. Key names are Unity KeyCode names (F6, PageUp, Alpha1, KeypadPlus...).
/// Ctrl/Shift/Alt match either the left or the right key. Empty text disables the hotkey.
/// (BepInEx's KeyboardShortcut isn't available in its IL2CPP build.)
/// </summary>
internal sealed class Hotkey
{
    enum Mod { Ctrl, Shift, Alt }

    static readonly Dictionary<Mod, KeyCode[]> ModKeys = new()
    {
        [Mod.Ctrl] = new[] { KeyCode.LeftControl, KeyCode.RightControl },
        [Mod.Shift] = new[] { KeyCode.LeftShift, KeyCode.RightShift },
        [Mod.Alt] = new[] { KeyCode.LeftAlt, KeyCode.RightAlt },
    };

    sealed class Combo
    {
        public KeyCode Key;
        public HashSet<Mod> Mods = new();
    }

    readonly List<Combo> _combos = new();
    public string Name { get; }

    public Hotkey(string name, string text)
    {
        Name = name;
        if (string.IsNullOrWhiteSpace(text)) return;

        foreach (var alt in text.Split(',', '|'))
        {
            if (string.IsNullOrWhiteSpace(alt)) continue;
            var combo = new Combo();
            bool ok = true;
            KeyCode? key = null;
            foreach (var raw in alt.Split('+'))
            {
                var part = raw.Trim();
                if (part.Length == 0) { ok = false; break; }
                if (TryMod(part, out var mod)) { combo.Mods.Add(mod); continue; }
                if (key != null || !TryKey(part, out var k))
                {
                    ok = false;
                    break;
                }
                key = k;
            }
            if (!ok || key == null)
            {
                Plugin.L.LogWarning($"Hotkey {name}: can't understand \"{alt.Trim()}\" " +
                                    "(use e.g. \"Ctrl + Equals\"; key names are Unity KeyCode names). Ignoring it.");
                continue;
            }
            combo.Key = key.Value;
            _combos.Add(combo);
        }
    }

    /// <summary>
    /// A key name: a Unity KeyCode name, a single digit (the number row: "0" = Alpha0),
    /// or "=" / "-". Other numbers are rejected: Enum.TryParse would silently turn
    /// e.g. "8" into Backspace (KeyCode 8).
    /// </summary>
    static bool TryKey(string s, out KeyCode key)
    {
        key = default;
        if (s.Length == 1 && s[0] >= '0' && s[0] <= '9')
        {
            key = KeyCode.Alpha0 + (s[0] - '0');
            return true;
        }
        if (s == "=") { key = KeyCode.Equals; return true; }
        if (s == "-") { key = KeyCode.Minus; return true; }
        if (s.All(char.IsDigit)) return false;
        return Enum.TryParse(s, true, out key) && Enum.IsDefined(typeof(KeyCode), key) && key != KeyCode.None;
    }

    static bool TryMod(string s, out Mod mod)
    {
        switch (s.ToLowerInvariant())
        {
            case "ctrl": case "control": case "leftcontrol": case "rightcontrol": mod = Mod.Ctrl; return true;
            case "shift": case "leftshift": case "rightshift": mod = Mod.Shift; return true;
            case "alt": case "leftalt": case "rightalt": mod = Mod.Alt; return true;
            default: mod = default; return false;
        }
    }

    /// <summary>True on the frame the key goes down with exactly the listed modifiers held.</summary>
    public bool IsDown()
    {
        foreach (var c in _combos)
        {
            if (!Input.GetKeyDown(c.Key)) continue;
            bool modsMatch = true;
            foreach (var (mod, keys) in ModKeys)
            {
                bool held = keys.Any(Input.GetKey);
                if (held != c.Mods.Contains(mod)) { modsMatch = false; break; }
            }
            if (modsMatch) return true;
        }
        return false;
    }

    public override string ToString() =>
        _combos.Count == 0
            ? "(off)"
            : string.Join(", ", _combos.Select(c => string.Join(" + ", c.Mods.OrderBy(m => m).Select(m => m.ToString()).Append(c.Key.ToString()))));
}
