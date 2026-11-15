using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace DiscoVoiceSpeed;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInProcess("disco.exe")]
public class Plugin : BasePlugin
{
    internal static ManualLogSource L;
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<float> Speed;
    internal static ConfigEntry<bool> ShowMessages;
    internal static ConfigEntry<string> FasterKey, SlowerKey, ToggleKey;
    internal static ConfigEntry<bool> Trace;

    internal static Hotkey Faster, Slower, Toggle;

    /// <summary>False when this game version lacks what the speed-up hooks into.</summary>
    internal static bool SpeedAvailable;

    public const float MinSpeed = 1.0f, MaxSpeed = 4.0f, Step = 0.25f;

    const string KeyHelp =
        "Key combinations joined with '+', alternatives separated by ','. " +
        "Modifiers: Ctrl, Shift, Alt. Key names are Unity KeyCode names, e.g. Equals, Minus, Alpha0, " +
        "KeypadPlus, F6, PageUp (https://docs.unity3d.com/ScriptReference/KeyCode.html). Leave empty to disable.";

    public override void Load()
    {
        L = Log;
        Enabled = Config.Bind("General", "Enabled", true,
            "Speed up voiced dialogue.");
        Speed = Config.Bind("General", "Speed", 2.0f,
            new ConfigDescription("Voice playback speed; 1.0 = normal. Pitch stays the same.",
                new AcceptableValueRange<float>(MinSpeed, MaxSpeed)));
        ShowMessages = Config.Bind("General", "ShowMessages", true,
            "Show a short on-screen message when a hotkey changes the speed or turns the speed-up on or off.");

        FasterKey = Config.Bind("Hotkeys", "Faster", "Ctrl + Equals, Ctrl + KeypadPlus", "Increase speed by 0.25. " + KeyHelp);
        SlowerKey = Config.Bind("Hotkeys", "Slower", "Ctrl + Minus, Ctrl + KeypadMinus", "Decrease speed by 0.25. " + KeyHelp);
        ToggleKey = Config.Bind("Hotkeys", "Toggle", "Ctrl + Alpha0, Ctrl + Keypad0", "Turn the speed-up on or off. " + KeyHelp);

        Trace = Config.Bind("Debug", "Trace", false,
            "Log detailed [VO] lines (every voice line, subtitle and continue click, with speed-up timings) " +
            "to BepInEx\\LogOutput.log. Useful for bug reports. Takes effect after a restart.");

        ParseHotkeys();
        Config.SettingChanged += (_, e) =>
        {
            if (e.ChangedSetting.Definition.Section == "Hotkeys") ParseHotkeys();
        };
        Config.ConfigReloaded += (_, _) => ParseHotkeys();

        var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);

        // The speed-up: only if the game still has what it hooks into.
        SpeedAvailable = Compat.Check(out var problem);
        if (SpeedAvailable)
        {
            try { harmony.PatchAll(typeof(VoSpeedPatch)); }
            catch (Exception e)
            {
                SpeedAvailable = false;
                problem = $"patching failed: {e.Message}";
                TryUnpatch(harmony);
            }
        }
        if (SpeedAvailable)
        {
            // Optional safety net: cancel a pending speed-up when the game stops the voice.
            try { harmony.PatchAll(typeof(VoSpeedPatch.StopPatches)); }
            catch (Exception e) { L.LogWarning($"Voice-stop hooks unavailable ({e.Message}); speed-up still works"); }
        }
        if (!SpeedAvailable)
            L.LogWarning($"Voice speed-up is OFF: this game version isn't supported ({problem}). " +
                         "Voices play at normal speed; check for a mod update.");

        // Trace logging is optional; a missing trace target must not affect the speed-up.
        if (Trace.Value)
        {
            try { harmony.PatchAll(typeof(VoTracePatches)); }
            catch (Exception e) { L.LogWarning($"Trace logging partly unavailable: {e.Message}"); }
        }

        AddComponent<VoWatcher>();
        L.LogInfo($"DiscoVoiceSpeed {MyPluginInfo.PLUGIN_VERSION} loaded: game {Compat.GameVersion()}, " +
                  $"speed-up {(SpeedAvailable ? "available" : "UNAVAILABLE")}, enabled={Enabled.Value} speed={Speed.Value:F2}x " +
                  $"faster=[{Faster}] slower=[{Slower}] toggle=[{Toggle}] trace={Trace.Value}");
    }

    /// <summary>Remove any half-applied speed-up patch, so a failed patch can't run.</summary>
    static void TryUnpatch(Harmony harmony)
    {
        try { harmony.UnpatchSelf(); }
        catch (Exception e) { L.LogWarning($"Unpatch failed: {e.Message}"); }
    }

    static void ParseHotkeys()
    {
        Faster = new Hotkey("Faster", FasterKey.Value);
        Slower = new Hotkey("Slower", SlowerKey.Value);
        Toggle = new Hotkey("Toggle", ToggleKey.Value);
    }
}
