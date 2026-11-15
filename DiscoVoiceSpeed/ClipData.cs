using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace DiscoVoiceSpeed;

/// <summary>
/// Read/write AudioClip samples by calling Unity's native bindings directly.
/// The interop wrappers for AudioClip.GetData/SetData don't work in this game:
/// the instance one returns false, the static one fails with a TypeLoadException.
/// </summary>
internal static class ClipData
{
    // bool AudioClipBindings::GetData(AudioClip clip, float[] data, int numSamples, int samplesOffset)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate byte DataICall(IntPtr clip, IntPtr data, int numFrames, int offsetFrames);

    static DataICall _get, _set;
    static bool _resolved;

    static void Resolve()
    {
        if (_resolved) return;
        _resolved = true;
        _get = Try("UnityEngine.AudioClip::GetData");
        _set = Try("UnityEngine.AudioClip::SetData");
    }

    static DataICall Try(string name)
    {
        try
        {
            // IL2CPP.ResolveICall never throws for a missing icall (it returns a stub that
            // throws when called), so look the pointer up directly.
            var ptr = IL2CPP.il2cpp_resolve_icall(name);
            if (ptr == IntPtr.Zero)
            {
                Plugin.L.LogWarning($"icall {name} not found");
                return null;
            }
            var d = Marshal.GetDelegateForFunctionPointer<DataICall>(ptr);
            Plugin.L.LogInfo($"Resolved icall {name}");
            return d;
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning($"Could not resolve icall {name}: {e.Message}");
            return null;
        }
    }

    /// <summary>Interleaved samples of the whole clip, or null on failure.</summary>
    public static float[] Read(AudioClip clip, out string error)
    {
        Resolve();
        error = null;
        if (_get == null) { error = "GetData icall unavailable"; return null; }
        int frames = clip.samples, n = frames * clip.channels;
        var il = new Il2CppStructArray<float>(n);
        bool ok = _get(clip.Pointer, il.Pointer, frames, 0) != 0;
        GC.KeepAlive(clip);
        if (!ok)
        {
            error = $"native GetData returned false (load={clip.loadType}, state={clip.loadState})";
            return null;
        }
        var data = new float[n];
        CopyOut(il, data);
        GC.KeepAlive(il);
        return data;
    }

    public static bool Write(AudioClip clip, float[] data, out string error)
    {
        Resolve();
        error = null;
        if (_set == null) { error = "SetData icall unavailable"; return false; }
        Il2CppStructArray<float> il = data;
        bool ok = _set(clip.Pointer, il.Pointer, data.Length / clip.channels, 0) != 0;
        GC.KeepAlive(il);
        GC.KeepAlive(clip);
        if (!ok)
        {
            error = "native SetData returned false";
            return false;
        }
        return true;
    }

    // An IL2CPP array is a header (klass, monitor, bounds, max_length: 4 pointer-sized
    // fields) followed by the elements. Bulk-copying from there is far faster than the
    // interop indexer; it's checked against the indexer once before being trusted.
    static readonly int ElementsOffset = 4 * IntPtr.Size;
    static bool? _fastCopyOk;

    static void CopyOut(Il2CppStructArray<float> il, float[] dest)
    {
        int n = dest.Length;
        if (_fastCopyOk != false && n > 0)
        {
            Marshal.Copy(IntPtr.Add(il.Pointer, ElementsOffset), dest, 0, n);
            if (_fastCopyOk == true) return;

            bool same = true;
            int step = Math.Max(1, n / 64);
            for (int i = 0; i < n && same; i += step) same = dest[i] == il[i];
            same &= dest[n - 1] == il[n - 1];
            _fastCopyOk = same;
            if (same) return;
            Plugin.L.LogWarning("Fast sample copy didn't match; using the slow copy");
        }
        for (int i = 0; i < n; i++) dest[i] = il[i];
    }

    public static float Peak(float[] d)
    {
        float p = 0;
        foreach (var v in d) p = Math.Max(p, Math.Abs(v));
        return p;
    }

    /// <summary>Main-menu self test: try reading clips already in memory both ways.</summary>
    public static void SelfTest()
    {
        try
        {
            var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<AudioClip>());
            Plugin.L.LogInfo($"[selftest] {all.Length} AudioClips in memory");
            int tested = 0;
            foreach (var o in all)
            {
                var clip = o.TryCast<AudioClip>();
                if (clip == null || clip.loadType != AudioClipLoadType.DecompressOnLoad || clip.samples == 0) continue;
                if (tested++ >= 4) break;

                string viaWrapper;
                try
                {
                    var il = new Il2CppStructArray<float>(clip.samples * clip.channels);
                    bool ok = clip.GetData(il, 0);
                    float p = 0;
                    for (int i = 0; i < il.Length; i++) p = Math.Max(p, Math.Abs(il[i]));
                    viaWrapper = $"{ok} peak={p:F3}";
                }
                catch (Exception e) { viaWrapper = e.GetType().Name; }

                var d = Read(clip, out var err);
                string viaICall = d == null ? $"FAIL ({err})" : $"ok peak={Peak(d):F3}";

                Plugin.L.LogInfo($"[selftest] \"{clip.name}\" {clip.length:F2}s ch={clip.channels} state={clip.loadState} " +
                                 $"wrapper={viaWrapper} icall={viaICall}");
            }
            if (tested == 0) Plugin.L.LogInfo("[selftest] no DecompressOnLoad clips found");

            // Round trip: create a clip, write a tone, read it back.
            var tone = new float[4800];
            for (int i = 0; i < tone.Length; i++) tone[i] = 0.5f * Mathf.Sin(i * 0.1f);
            var test = AudioClip.Create("dvs-selftest", tone.Length, 1, 48000, false);
            bool w = Write(test, tone, out var werr);
            var back = Read(test, out var rerr);
            Plugin.L.LogInfo($"[selftest] Create+SetData+GetData round trip: create={(test != null)} write={w}{(werr != null ? " " + werr : "")} " +
                             $"read={(back != null ? $"peak={Peak(back):F3}" : rerr)}");
            UnityEngine.Object.Destroy(test);
        }
        catch (Exception e)
        {
            Plugin.L.LogError($"[selftest] crashed: {e}");
        }
    }
}
