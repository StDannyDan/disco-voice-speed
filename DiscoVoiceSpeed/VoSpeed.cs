using System;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;
using VOTool;
using Object = UnityEngine.Object;

namespace DiscoVoiceSpeed;

/// <summary>
/// When the game starts a dialogue voice clip, replace it with a time-stretched
/// copy (same pitch, shorter) and restart playback.
///
/// Voice clips load "in background": when the game calls Play(), the data may not
/// be there yet (GetData fails, Unity defers the actual playback). In that case we
/// mute the source and finish the job from <see cref="Tick"/> (called every frame by
/// <see cref="VoWatcher"/>) once it's loaded. If the game stops the voice meanwhile
/// (player clicked Continue), the pending speed-up is cancelled.
///
/// Fail-safe: this runs inside the game's own voice code, so nothing may throw out
/// of it, and the source is always unmuted again. A line that fails plays at normal
/// speed; after <see cref="MaxFailures"/> failures (or load timeouts) in a row the
/// speed-up turns itself off for the session.
/// </summary>
internal static class VoSpeedPatch
{
    /// <summary>The sped-up clip we created most recently (we own it and must destroy it).</summary>
    internal static AudioClip Ours;

    /// <summary>True once the circuit breaker has tripped.</summary>
    internal static bool Broken { get; private set; }

    const int MaxFailures = 3;
    static int _failuresInARow, _timeoutsInARow;

    /// <summary>Clips shorter than this are left alone (nothing to gain, and 0-frame output).</summary>
    const float MinClipSeconds = 0.05f;

    sealed class PendingClip
    {
        public VoiceOverClipsPlayer Player;
        public AudioSource Src;
        public AudioClip Clip;
        public bool WasMuted;
        public float Since;
    }

    static PendingClip _pending;
    const float LoadTimeout = 10f; // seen: ~1.4 s for the first line after loading a save

    /// <summary>Frame of the last <see cref="Tick"/>; if ticks stop, don't defer (don't mute).</summary>
    static int _lastTickFrame = int.MinValue / 2;
    static bool _warnedNoTick;

    [HarmonyPatch(typeof(VoiceOverClipsPlayer), Compat.PlayMethod)]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)] // after the trace postfix, so the trace logs the original clip
    static void Postfix(VoiceOverClipsPlayer __instance)
    {
        if (Broken) return;
        try
        {
            CancelPending("a new line started");
            if (!Plugin.Enabled.Value || Plugin.Speed.Value < 1.01f) return;

            var src = __instance.audioSource;
            var clip = src != null ? src.clip : null;
            if (clip == null) return;

            if (clip.loadState == AudioDataLoadState.Loaded)
            {
                Apply(__instance, src, clip, 0f, src.mute);
                return;
            }

            // Not loaded yet. Deferring needs Tick() to run every frame; if it doesn't
            // (watcher gone or switched off after an error), muting would leave the line
            // silent, so play it at normal speed instead.
            if (Time.frameCount - _lastTickFrame > 5)
            {
                if (!_warnedNoTick)
                {
                    _warnedNoTick = true;
                    Plugin.L.LogWarning("Per-frame update isn't running; lines that are still loading play at normal speed");
                }
                return;
            }

            // Keep the original silent until we can swap it.
            _pending = new PendingClip
            {
                Player = __instance, Src = src, Clip = clip,
                WasMuted = src.mute, Since = Time.realtimeSinceStartup,
            };
            src.mute = true;
        }
        catch (Exception e)
        {
            UnmutePending();
            Failed($"Speed-up hook error: {e}");
        }
    }

    /// <summary>
    /// The game stops the current voice (Continue clicked, next line starting): drop a
    /// pending speed-up so a late-loading clip can't restart over the next line.
    /// Patched separately (optional): if these methods disappear, only this safety is lost.
    /// </summary>
    internal static class StopPatches
    {
        [HarmonyPatch(typeof(VoiceOverClipsPlayer), nameof(VoiceOverClipsPlayer.StopPreviousVOClipIfActive))]
        [HarmonyPrefix]
        static void StopPrevious() => GameStoppedVoice();

        [HarmonyPatch(typeof(VoiceOverClipsPlayer), nameof(VoiceOverClipsPlayer.StopVoiceClipIfOneIsAlreadyPlaying))]
        [HarmonyPrefix]
        static void StopIfPlaying() => GameStoppedVoice();

        static void GameStoppedVoice()
        {
            try { CancelPending("the game stopped the voice"); }
            catch { /* never throw into the game */ }
        }
    }

    /// <summary>Called every frame by VoWatcher.</summary>
    internal static void Tick()
    {
        _lastTickFrame = Time.frameCount;
        var p = _pending;
        if (p == null) return;
        try
        {
            if (Broken) { CancelPending(null); return; }
            if (p.Src == null || p.Clip == null || p.Src.clip != p.Clip) { CancelPending("the game moved on"); return; }
            // While the clip loads, Unity reports the deferred play as playing. If that stops,
            // the game stopped or paused the voice itself (left the conversation, menu, load):
            // don't start the line again.
            if (!p.Src.isPlaying) { CancelPending("the game stopped the voice"); return; }

            var state = p.Clip.loadState;
            float waited = Time.realtimeSinceStartup - p.Since;
            if (state == AudioDataLoadState.Loaded)
            {
                _pending = null;
                Apply(p.Player, p.Src, p.Clip, waited, p.WasMuted);
            }
            else if (state == AudioDataLoadState.Failed)
            {
                // The game's own clip failed to load: not our failure, so it doesn't count
                // toward the circuit breaker.
                CancelPending(null);
                Plugin.L.LogWarning($"\"{p.Clip.name}\" failed to load (game side); left alone");
            }
            else if (waited > LoadTimeout)
            {
                // One slow load (e.g. right after loading a save) isn't an error; the line just
                // plays normally. But if clips *never* finish loading (e.g. after a game update),
                // every line would sit muted for the timeout, so repeated timeouts trip the breaker.
                CancelPending($"still loading after {waited:F1}s");
                if (++_timeoutsInARow >= MaxFailures)
                    Trip($"{MaxFailures} voice lines in a row never finished loading");
            }
        }
        catch (Exception e)
        {
            UnmutePending();
            Failed($"Speed-up error while waiting for a clip: {e}");
        }
    }

    static void CancelPending(string why)
    {
        var p = _pending;
        if (p == null) return;
        UnmutePending();
        if (why != null && Plugin.Trace.Value)
            Plugin.L.LogInfo($"[VO] not speeding up \"{(p.Clip != null ? p.Clip.name : "?")}\": {why}");
    }

    /// <summary>Restore the pending source's mute state and forget it. Never throws.</summary>
    static void UnmutePending()
    {
        var p = _pending;
        _pending = null;
        try { if (p?.Src != null) p.Src.mute = p.WasMuted; }
        catch { /* the source is gone; nothing to restore */ }
    }

    /// <param name="wasMuted">The source's mute state before we touched it; restored at the end.</param>
    static void Apply(VoiceOverClipsPlayer player, AudioSource src, AudioClip clip, float waitedForLoad, bool wasMuted)
    {
        float speed = Plugin.Speed.Value;
        string name = "?";
        // Keep the original silent while we process (the audio thread keeps playing it).
        src.mute = true;
        try
        {
            name = clip.name;
            if (clip.length < MinClipSeconds) return; // too short to bother; plays as is

            var sw = Stopwatch.StartNew();
            int ch = clip.channels, hz = clip.frequency;

            var data = ClipData.Read(clip, out var readError);
            if (data == null)
            {
                Failed($"Can't read \"{name}\": {readError}; played at normal speed");
                return;
            }
            long tRead = sw.ElapsedMilliseconds;

            float[] fast = Sonic.SpeedUp(data, ch, hz, speed);
            long tSonic = sw.ElapsedMilliseconds;

            int outFrames = fast.Length / ch;
            var newClip = AudioClip.Create($"{name} x{speed:F2}", outFrames, ch, hz, false);
            if (!ClipData.Write(newClip, fast, out var writeError))
            {
                Object.Destroy(newClip);
                Failed($"Can't write sped-up \"{name}\": {writeError}; played at normal speed");
                return;
            }

            if (Ours != null) Object.Destroy(Ours);
            Ours = newClip;

            float playedAlready = src.time;
            src.clip = newClip;
            src.Play();
            player.stopTime = newClip.length;
            VoWatcher.Watch(src);
            _failuresInARow = 0;
            _timeoutsInARow = 0;

            if (Plugin.Trace.Value)
                Plugin.L.LogInfo($"[VO] sped up \"{name}\" {clip.length:F2}s -> {newClip.length:F2}s at {speed:F2}x " +
                                 $"(waited {waitedForLoad * 1000:F0} ms for load; read {tRead} ms, sonic {tSonic - tRead} ms, " +
                                 $"total {sw.ElapsedMilliseconds} ms; original had played {playedAlready:F2}s)");
        }
        catch (Exception e)
        {
            Failed($"Speed-up failed for \"{name}\": {e}");
        }
        finally
        {
            try { src.mute = wasMuted; } catch { /* source gone */ }
        }
    }

    /// <summary>Count a failed line; trip the breaker after too many in a row.</summary>
    static void Failed(string message)
    {
        _failuresInARow++;
        Plugin.L.LogWarning(message);
        if (_failuresInARow >= MaxFailures)
            Trip($"{MaxFailures} failed lines in a row");
    }

    static void Trip(string reason)
    {
        if (Broken) return;
        Broken = true;
        CancelPending(null);
        Plugin.L.LogError($"Voice speed-up turned off for this session after {reason} " +
                          "(maybe a game update changed something). Voices play at normal speed; check for a mod update.");
        Overlay.Show("Voice speed mod stopped after errors (see BepInEx log)");
    }
}
