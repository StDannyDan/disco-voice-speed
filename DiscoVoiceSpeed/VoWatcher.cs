using System;
using UnityEngine;

namespace DiscoVoiceSpeed;

/// <summary>
/// Per-frame helper: finishes deferred speed-ups, hotkeys, the on-screen message,
/// cleanup of our sped-up clips and (when tracing) voice start/stop logging.
/// Each part is guarded separately: if one throws, it logs once and turns itself
/// off for the session, so a broken part can't flood the log every frame.
/// </summary>
public class VoWatcher : MonoBehaviour
{
    public VoWatcher(IntPtr ptr) : base(ptr) { }

    static AudioSource _src;
    static bool _wasPlaying;
    static string _clip;

    static bool _speedOff, _keysOff, _overlayOff, _cleanupOff, _traceOff;

    internal static void Watch(AudioSource src)
    {
        _src = src;
        _wasPlaying = src != null && src.isPlaying;
        _clip = src != null && src.clip != null ? src.clip.name : null;
    }

    void Update()
    {
        if (Plugin.SpeedAvailable) Guard(ref _speedOff, "Speed-up", VoSpeedPatch.Tick);
        Guard(ref _keysOff, "Hotkeys", Hotkeys);
        Guard(ref _overlayOff, "On-screen message", Overlay.Tick);
        Guard(ref _cleanupOff, "Clip cleanup", Cleanup);
        if (Plugin.Trace.Value) Guard(ref _traceOff, "Trace watcher", TraceTick);
    }

    static void Guard(ref bool off, string what, Action action)
    {
        if (off) return;
        try { action(); }
        catch (Exception e)
        {
            off = true;
            Plugin.L.LogError($"{what} turned off for this session after an error: {e}");
        }
    }

    /// <summary>The game has moved on from our clip (stopped/unloaded or replaced): free it.</summary>
    static void Cleanup()
    {
        var ours = VoSpeedPatch.Ours;
        if (ours == null || _src == null || _src.clip == ours) return;
        Destroy(ours);
        VoSpeedPatch.Ours = null;
    }

    static void TraceTick()
    {
        if (_src == null) return;
        bool playing = _src.isPlaying;
        var clip = _src.clip != null ? _src.clip.name : null;
        if (playing == _wasPlaying && clip == _clip) return;
        VoTracePatches.Log(playing
            ? $"VO source PLAYING \"{clip}\" at {_src.time:F2}s"
            : $"VO source STOPPED \"{_clip}\" at {_src.time:F2}s (len {(_src.clip != null ? _src.clip.length : 0):F2}s)");
        _wasPlaying = playing;
        _clip = clip;
    }

    static void Hotkeys()
    {
        bool faster = Plugin.Faster.IsDown(), slower = !faster && Plugin.Slower.IsDown();
        bool toggle = !faster && !slower && Plugin.Toggle.IsDown();
        if (!faster && !slower && !toggle) return;

        if (!Plugin.SpeedAvailable || VoSpeedPatch.Broken)
        {
            Overlay.Show("Voice speed mod inactive (see BepInEx log)");
            return;
        }

        if (faster) SetSpeed(Plugin.Speed.Value + Plugin.Step);
        else if (slower) SetSpeed(Plugin.Speed.Value - Plugin.Step);
        else
        {
            Plugin.Enabled.Value = !Plugin.Enabled.Value; // saved to the .cfg automatically
            var msg = Plugin.Enabled.Value ? $"Voice speed {Speed(Plugin.Speed.Value)}" : "Voice speed normal (mod off)";
            Plugin.L.LogInfo(msg);
            Overlay.Show(msg);
        }
    }

    static void SetSpeed(float value)
    {
        value = Mathf.Clamp(Mathf.Round(value / Plugin.Step) * Plugin.Step, Plugin.MinSpeed, Plugin.MaxSpeed);
        Plugin.Speed.Value = value; // saved to the .cfg automatically
        if (!Plugin.Enabled.Value) Plugin.Enabled.Value = true; // changing speed implies "on"
        string limit = value >= Plugin.MaxSpeed ? " (max)" : value <= Plugin.MinSpeed ? " (normal)" : "";
        var msg = $"Voice speed {Speed(value)}{limit}";
        Plugin.L.LogInfo(msg + " (applies from the next line)");
        Overlay.Show(msg);
    }

    static string Speed(float v) => $"{v:0.##}x"; // plain x: the game font may lack '×'
}
