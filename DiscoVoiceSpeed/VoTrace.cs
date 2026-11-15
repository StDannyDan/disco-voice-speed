using System;
using HarmonyLib;
using PixelCrushers.DialogueSystem;
using UnityEngine;
using VOTool;

namespace DiscoVoiceSpeed;

/// <summary>
/// Trace-only patches (Debug.Trace): log when voice clips start/stop and when the
/// dialogue moves on. Grep the BepInEx log for "[VO]". Every patch body is guarded:
/// these run inside the game's own methods, so an exception must never escape.
/// After the first error, tracing stops for the session.
/// </summary>
internal static class VoTracePatches
{
    internal static VoiceOverClipsPlayer Player;
    static bool _broken;

    internal static void Log(string msg) =>
        Plugin.L.LogInfo($"[VO] t={Time.realtimeSinceStartup:F2} {msg}");

    /// <summary>Log a lazily built message; on error, stop tracing instead of throwing into the game.</summary>
    static void T(Func<string> msg)
    {
        if (_broken) return;
        try { Log(msg()); }
        catch (Exception e)
        {
            _broken = true;
            Plugin.L.LogWarning($"Trace logging stopped after an error: {e}");
        }
    }

    static string Entry(DialogueEntry e) =>
        e == null ? "<null>" : $"#{e.conversationID}:{e.id} \"{Trim(e.Title)}\"";

    static string Trim(string s, int n = 60) =>
        s == null ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…").Replace("\n", " ");

    static string Src(AudioSource a)
    {
        if (a == null) return "src=<null>";
        var c = a.clip;
        var clip = c == null ? "<none>" : $"\"{c.name}\" len={c.length:F2}s ch={c.channels} hz={c.frequency} load={c.loadType}";
        return $"clip={clip} playing={a.isPlaying} time={a.time:F2} pitch={a.pitch:F2}";
    }

    // ---- VoiceOverClipsPlayer ----

    [HarmonyPatch(typeof(VoiceOverClipsPlayer), nameof(VoiceOverClipsPlayer.PlayVoiceClip))]
    [HarmonyPostfix]
    static void PlayVoiceClip(DialogueEntry entry, bool __result) =>
        T(() => $"PlayVoiceClip {Entry(entry)} -> {__result}");

    [HarmonyPatch(typeof(VoiceOverClipsPlayer), Compat.PlayMethod)]
    [HarmonyPostfix]
    static void PlayAudioClipDelegate(VoiceOverClipsPlayer __instance) =>
        T(() =>
        {
            Player = __instance;
            VoWatcher.Watch(__instance.audioSource);
            return $"PlayAudioClipDelegate {Src(__instance.audioSource)} startTime={__instance.startTime:F2} stopTime={__instance.stopTime:F2}";
        });

    [HarmonyPatch(typeof(VoiceOverClipsPlayer), nameof(VoiceOverClipsPlayer.StopPreviousVOClipIfActive))]
    [HarmonyPrefix]
    static void StopPrevious(VoiceOverClipsPlayer __instance) =>
        T(() => $"StopPreviousVOClipIfActive {Src(__instance.audioSource)}");

    [HarmonyPatch(typeof(VoiceOverClipsPlayer), nameof(VoiceOverClipsPlayer.StopVoiceClipIfOneIsAlreadyPlaying))]
    [HarmonyPrefix]
    static void StopIfPlaying() => T(() => "StopVoiceClipIfOneIsAlreadyPlaying");

    [HarmonyPatch(typeof(VoiceOverClipsPlayer), nameof(VoiceOverClipsPlayer.PlayBarkVoiceOver))]
    [HarmonyPrefix]
    static void Bark(DialogueEntry entry) => T(() => $"PlayBarkVoiceOver {Entry(entry)}");

    // ---- Pixel Crushers ConversationView ----

    [HarmonyPatch(typeof(ConversationView), nameof(ConversationView.StartSubtitle))]
    [HarmonyPostfix]
    static void StartSubtitle(ConversationView __instance, Subtitle subtitle, bool isPCResponseMenuNext, bool isPCAutoResponseNext) =>
        T(() =>
        {
            var speaker = subtitle?.speakerInfo?.Name ?? "?";
            var text = subtitle?.formattedText?.text;
            return $"StartSubtitle {Entry(subtitle?.dialogueEntry)} speaker={speaker} text=\"{Trim(text)}\" " +
                   $"seq=\"{Trim(subtitle?.sequence, 200)}\" waitForContinue={__instance.waitForContinue} " +
                   $"pcMenuNext={isPCResponseMenuNext} pcAutoNext={isPCAutoResponseNext} " +
                   $"defaultDur={__instance.GetDefaultSubtitleDuration(text ?? ""):F2}";
        });

    [HarmonyPatch(typeof(ConversationView), nameof(ConversationView.OnFinishedSubtitle))]
    [HarmonyPrefix]
    static void FinishedSubtitle(ConversationView __instance) =>
        T(() => $"OnFinishedSubtitle (sequence done) waitForContinue={__instance.waitForContinue} vo: {Src(Player?.audioSource)}");

    [HarmonyPatch(typeof(ConversationView), nameof(ConversationView.FinishSubtitle))]
    [HarmonyPrefix]
    static void FinishSubtitle() => T(() => $"FinishSubtitle vo: {Src(Player?.audioSource)}");

    [HarmonyPatch(typeof(ConversationView), nameof(ConversationView.HandleContinueButtonClick))]
    [HarmonyPrefix]
    static void ContinueClick() => T(() => $"HandleContinueButtonClick vo: {Src(Player?.audioSource)}");

    [HarmonyPatch(typeof(ConversationView), nameof(ConversationView.OnConversationContinue))]
    [HarmonyPrefix]
    static void ConvContinue() => T(() => "OnConversationContinue");

    // ---- The game's own continue button ----

    [HarmonyPatch(typeof(SunshineContinueButton), nameof(SunshineContinueButton.WasClicked))]
    [HarmonyPrefix]
    static void Clicked() => T(() => $"SunshineContinueButton.WasClicked vo: {Src(Player?.audioSource)}");

    [HarmonyPatch(typeof(SunshineContinueButton), nameof(SunshineContinueButton.SetState))]
    [HarmonyPrefix]
    static void ButtonState(SunshineContinueButton __instance, ContState buttonState)
    {
        if (_broken) return;
        try
        {
            if (__instance.state != buttonState)
                Log($"ContinueButton {__instance.state} -> {buttonState}");
        }
        catch (Exception e)
        {
            _broken = true;
            Plugin.L.LogWarning($"Trace logging stopped after an error: {e}");
        }
    }
}
