# Development

## Building

1. Install [BepInEx 6.0.0-be.755 (IL2CPP, x64)](https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755%2B3fab71a.zip) into the game folder and start the game once, so BepInEx generates the interop assemblies in `BepInEx\interop`. The project compiles against be.688, the oldest supported build (Furious Tare's), and runs on both; it was tested on both on 2026-11-14.
2. Install the .NET SDK, version 6 or newer. The project targets `net6.0`; SDK 8 builds it fine.
3. In `DiscoVoiceSpeed\`, run:

   ```
   dotnet build -c Release
   ```

   The project references every DLL in `<game>\BepInEx\interop`. After building, it copies the plugin to `<game>\BepInEx\plugins\DiscoVoiceSpeed\` (close the game first, or the copy fails). The default game folder is `D:\SteamLibrary\steamapps\common\Disco Elysium`. For a different one:

   ```
   dotnet build -c Release -p:GameDir="C:\path\to\Disco Elysium"
   ```

## Packaging a release

1. Bump `<Version>` in `DiscoVoiceSpeed\DiscoVoiceSpeed.csproj` and add a `CHANGELOG.md` entry. Commit; the script refuses to package uncommitted changes (`-AllowDirty` makes a test package anyway).
2. Run `.\package.ps1`, or `.\package.ps1 -GameDir "C:\path\to\Disco Elysium"`. It needs the game with BepInEx run once, for the interop assemblies. It also refuses to package a DLL that contains a local path (the csproj sets `DebugType=none` and a `PathMap` for that).

It builds in Release mode, without copying into the game, and writes two zips to `dist\`. Both unpack straight into the game folder:

| Zip | Contents |
|---|---|
| `DiscoVoiceSpeed-<version>-with-BepInEx.zip` | BepInEx 6.0.0-be.755, the build the Disco Elysium Vortex extension installs (downloaded once into `.cache\`, checked against a fixed SHA256), the plugin folder, and `BepInEx\config\BepInEx.cfg` from `packaging\`, which only turns off the console window. The plugin folder also gets `THIRD-PARTY-NOTICES.txt` and BepInEx's LGPL-2.1 license |
| `DiscoVoiceSpeed-<version>-plugin-only.zip` | `BepInEx\plugins\DiscoVoiceSpeed\` with the DLL, README, CHANGELOG, LICENSE and NOTICE |

Tested end to end on 2026-11-14: fresh install from the full zip into the game folder with no BepInEx. The first start took 19 s, the plugin loaded with defaults and no errors or warnings, no console window appeared, and BepInEx kept `Enabled = false` while adding its other defaults.

## Project layout

| File | What it does |
|---|---|
| `Plugin.cs` | Entry point: config, the startup compatibility check, Harmony patching |
| `VoSpeed.cs` | The speed-up. A postfix on `VOTool.VoiceOverClipsPlayer.PlayAudioClipDelegate` reads the clip, stretches it and swaps in the copy. It waits while the clip is still loading, keeps the original muted meanwhile (cancelled if the game stops the voice: prefixes on `StopPreviousVOClipIfActive` / `StopVoiceClipIfOneIsAlreadyPlaying`), and has a circuit breaker for failures and repeated load timeouts |
| `Sonic.cs` | Port of Sonic's speed-up path: PICOLA below 2x, partial pitch-period skipping from 2x up. Processes a whole clip at once |
| `ClipData.cs` | Reads and writes clip samples through Unity's native `AudioClip::GetData`/`SetData` (resolved with `IL2CPP.il2cpp_resolve_icall`) |
| `VoWatcher.cs` | Per-frame component: finishes deferred speed-ups, hotkeys, the message fade, cleanup of our clips, trace watcher. Each part is guarded separately |
| `Hotkey.cs` | Parses hotkey text like `Ctrl + Equals, Ctrl + KeypadPlus` (BepInEx's `KeyboardShortcut` isn't in its IL2CPP build) |
| `Overlay.cs` | The on-screen message: uGUI canvas plus TextMeshPro in the game's Dobra-Book font |
| `Compat.cs` | Startup check, by name, that the game still has what the speed-up hooks into. Also the game-version string for the log |
| `VoTrace.cs` | Optional `[VO]` trace patches (`Debug.Trace`) on the voice player, Pixel Crushers' `ConversationView` and `SunshineContinueButton` |

## Things worth knowing about the game

- **IL2CPP build, Unity 2020.3.12.** Voices go through Unity audio (no FMOD/Wwise) and are played by `VOTool.VoiceOverClipsPlayer`. The dialogue runs on Pixel Crushers Dialogue System.
- **Voice clips** come from per-conversation Addressables bundles in `StreamingAssets\aa`. They're all Vorbis with load type Decompress On Load (47,904 clips, 88 h), so their samples can be read.
- **BepInEx's first start needs internet:** BepInEx 6 IL2CPP downloads the Unity base libraries (unity.bepinex.dev) before generating the interop assemblies.
- **Clips load in the background.** When `PlayAudioClipDelegate` runs, the clip usually isn't loaded yet; that takes 6–20 ms, or about 1.4 s for the first line after loading a save. Until then, `GetData` returns false **without logging anything**.
- **The interop wrappers for `AudioClip.GetData`/`SetData` don't work here.** The instance one returns false, and the static one throws a `TypeLoadException`. Hence the native calls in `ClipData.cs`.
- **The dialogue never waits for the voice.** Each line's sequence is empty; it waits for Continue or the response menu, and clicking Continue mid-line stops the clip. So a shorter clip needs no other changes. The mod also sets `VoiceOverClipsPlayer.stopTime` (which the game sets to the clip length) to the new length; whether the game's `StopTime()` coroutine reads it again after that hasn't been verified, and nothing observable depends on it.
- **Barks** (ambient lines like the radio) use `PlayBarkVoiceOver` on another `AudioSource` and are deliberately not sped up.

## Debugging

- `Debug.Trace = true` (off by default; read at startup) logs every voice clip, subtitle, Continue click and button state as `[VO]` lines. Every sped-up line logs its length before and after and its timings, e.g.
  `sped up "Kim Kitsuragi-…-136" 11.10s -> 5.55s at 2.00x (waited 9 ms for load; read 21 ms, sonic 28 ms, …)`.
- **Simulating a game update:** build with `-p:DefineConstants=FAILSAFE_TEST`. The plugin then looks for a renamed hook method, and should log one "Voice speed-up is OFF" warning and keep the game running normally.
- `ClipData.SelfTest()` (not called by default) checks reading clips already in memory, plus a create/write/read round trip. It's handy if a game update breaks audio access.

## Furious Tare

[Furious Tare](https://github.com/laurence-myers/FuriousTare) also runs on BepInEx 6 (IL2CPP); it ships be.688, and this mod was tested alongside it on both be.688 and be.755. Its voice-over patches (`RemapVoiceOvers`, `VoiceOverFixAlternatives`, `SkipIncorrectVoiceOver`) act on `JanusNode` and on `VoiceOverClipsPlayer.PlayVoiceClip`, which run before our hook, so the two don't conflict. Its GitHub release (April 2026) predates the June 2026 game update; to test against current code, build it from source (its `global.json` pins SDK 6.0.428; add `"rollForward": "latestMajor"` to use a newer SDK).
