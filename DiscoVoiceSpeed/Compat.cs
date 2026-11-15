using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace DiscoVoiceSpeed;

/// <summary>
/// Checks, by name, that the game still has what the speed-up hooks into. A game
/// update can rename or remove these; then the speed-up stays off (one warning,
/// voices at normal speed) instead of the plugin failing to load or throwing.
/// </summary>
internal static class Compat
{
    const string GameAssembly = "Assembly-CSharp";
    public const string PlayerType = "VOTool.VoiceOverClipsPlayer";
#if FAILSAFE_TEST
    public const string PlayMethod = "PlayAudioClipDelegateRenamed"; // simulates a game update renaming it
#else
    public const string PlayMethod = "PlayAudioClipDelegate";
#endif
    static readonly string[] PlayerMembers = { "audioSource", "stopTime" };

    public static bool Check(out string problem)
    {
        var missing = new List<string>();
        try
        {
            // Load the game's interop assembly by name: at plugin load it usually isn't
            // in the AppDomain yet, so AccessTools.TypeByName wouldn't find anything.
            var asm = Assembly.Load(GameAssembly);
            var t = asm.GetType(PlayerType);
            if (t == null)
            {
                problem = $"{PlayerType} not found";
                return false;
            }
            if (AccessTools.Method(t, PlayMethod) == null)
                missing.Add($"{PlayerType}.{PlayMethod}");
            foreach (var m in PlayerMembers)
                if (AccessTools.Property(t, m) == null && AccessTools.Field(t, m) == null)
                    missing.Add($"{PlayerType}.{m}");
        }
        catch (Exception e)
        {
            problem = $"{e.GetType().Name}: {e.Message}";
            return false;
        }
        problem = missing.Count == 0 ? null : "missing " + string.Join(", ", missing);
        return missing.Count == 0;
    }

    /// <summary>
    /// Identifies the game build for bug reports. Application.version is just "1.0", so
    /// add the Steam build id (from steamapps\appmanifest_632470.acf, if this is a Steam
    /// install) and GameAssembly.dll's size, which changes with every update.
    /// </summary>
    public static string GameVersion()
    {
        var parts = new List<string>();
        try { parts.Add(UnityEngine.Application.version); }
        catch { parts.Add("?"); }
        try
        {
            var root = BepInEx.Paths.GameRootPath;
            var manifest = Path.Combine(root, "..", "..", "appmanifest_632470.acf");
            if (File.Exists(manifest))
                foreach (var line in File.ReadLines(manifest))
                {
                    var l = line.Trim();
                    if (!l.StartsWith("\"buildid\"")) continue;
                    parts.Add("Steam build " + l.Substring("\"buildid\"".Length).Trim().Trim('"'));
                    break;
                }
            parts.Add($"GameAssembly.dll {new FileInfo(Path.Combine(root, "GameAssembly.dll")).Length:N0} bytes");
        }
        catch { /* best effort */ }
        return string.Join(", ", parts);
    }
}
