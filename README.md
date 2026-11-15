# DiscoVoiceSpeed

A mod for **Disco Elysium – The Final Cut** that plays voiced dialogue faster, without the chipmunk effect, like the playback speed setting on YouTube. Pick any speed from 1x to 4x, and change it in game with a hotkey.

Disco Elysium never waits for a voice line to finish, only for you to click Continue or pick a reply. So a faster voice simply finishes sooner, and you can keep reading at your own pace.

Source code, updates and bug reports: **https://github.com/StDannyDan/disco-voice-speed**

## Requirements

- Disco Elysium – The Final Cut on **Windows**. Tested on the Steam version, build 23980936. The GOG version and Steam Deck / Linux (Proton) should work but are untested.
- [BepInEx 6 (IL2CPP, x64)](https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755%2B3fab71a.zip), the mod loader. The "with BepInEx" download already includes it (build be.755). Tested on be.755 and be.688.

## Install

There are two downloads. Pick one:

- **`DiscoVoiceSpeed-<version>-with-BepInEx.zip`**: if you don't have BepInEx yet. Don't extract it over an existing BepInEx install (e.g. from another mod): it would replace BepInEx's files and settings. Use the plugin-only file instead.
- **`DiscoVoiceSpeed-<version>-plugin-only.zip`**: if you already have BepInEx 6 (be.688 or newer), e.g. from [Furious Tare](https://www.nexusmods.com/discoelysium/mods/62).
- **Vortex users:** install the plugin-only file (not yet tested with Vortex; reports welcome). The [Disco Elysium Vortex extension](https://www.nexusmods.com/site/mods/1643) installs BepInEx for you.

1. Find the game folder: in Steam, right-click Disco Elysium → *Manage* → *Browse local files*. It's the folder with `disco.exe`.
2. Extract the zip **into that folder**. Afterwards `BepInEx` should be next to `disco.exe`, and the mod at `BepInEx\plugins\DiscoVoiceSpeed\DiscoVoiceSpeed.dll`.
3. Start the game. **The first start with BepInEx takes a minute or two longer** while it prepares, and **needs an internet connection** (BepInEx downloads some Unity files once). Later starts are normal and work offline.

Voiced lines now play at 2x.

### Steam Deck / Linux (Proton)

Untested, but BepInEx normally needs one launch option there. In Steam, open the game's *Properties* → *Launch Options* and enter:

```
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

## Using it

| Hotkey | Does |
|---|---|
| **Ctrl + =** | Faster (+0.25x, up to 4x) |
| **Ctrl + -** | Slower (−0.25x, down to 1x) |
| **Ctrl + 0** | Turn the speed-up off / on |

A short message near the top of the screen shows the new speed. Changes apply from the next line and are remembered between sessions. The numpad +, − and 0 keys work too.

**Non-US keyboard?** The `=` and `-` keys sit elsewhere on some layouts (e.g. German), so use the numpad keys, or pick other hotkeys in the settings.

## Settings

All settings are in `BepInEx\config\stdannydan.discovoicespeed.cfg`, which appears after the first start. Edit it with any text editor while the game is closed. The relevant lines look like this:

```ini
[General]
Enabled = true
Speed = 2
ShowMessages = true

[Hotkeys]
Faster = Ctrl + Equals, Ctrl + KeypadPlus
Slower = Ctrl + Minus, Ctrl + KeypadMinus
Toggle = Ctrl + Alpha0, Ctrl + Keypad0

[Debug]
Trace = false
```

| Setting | Default | Meaning |
|---|---|---|
| `Speed` | `2` | Voice speed, 1.0–4.0 |
| `Enabled` | `true` | Turn the speed-up on or off |
| `ShowMessages` | `true` | Show the on-screen message when a hotkey changes the speed |
| `Faster` / `Slower` / `Toggle` | see above | The hotkeys |
| `Trace` | `false` | Write detailed `[VO]` lines about every voice line to the BepInEx log (for bug reports) |

**Changing hotkeys:** write modifiers and one key joined with `+`, and separate alternatives with `,`. Modifiers are `Ctrl`, `Shift` and `Alt`. A single digit means the number row (`Ctrl + 0`), and `=` and `-` work as written. Other keys use [Unity's key names](https://docs.unity3d.com/ScriptReference/KeyCode.html), for example `F6`, `PageUp` or `Keypad1`. Leave a hotkey empty to turn it off. Example: `Faster = Shift + F6`. If a hotkey can't be understood, the log says so.

## Compatibility

- **[Furious Tare](https://www.nexusmods.com/discoelysium/mods/62)** (unofficial patch): works alongside it. Install either one first and add the other's plugin-only download.
- **Game updates:** if an update changes what this mod hooks into, the mod switches itself off and voices play at normal speed. Nothing breaks; check for a mod update. If a single line fails to speed up, it plays at normal speed. After 3 failures in a row, the mod switches off until the next start.

## Troubleshooting

- **Nothing changed?** Open `BepInEx\LogOutput.log` and search for `DiscoVoiceSpeed`. It should say `speed-up available`. If there's no BepInEx folder or no log, BepInEx isn't installed in the right folder (see Install, step 2). On Steam Deck/Linux, check the launch option.
- **"Voice speed mod inactive" when pressing a hotkey:** the game version isn't supported (see Compatibility), or the mod switched itself off after repeated errors. The log says which.
- **The first start takes a long time:** that's normal once; BepInEx is preparing. If the log has no `speed-up available` line after the first start, make sure that start had internet access (BepInEx downloads some Unity files once), then start the game again.
- **Antivirus complains about `winhttp.dll`:** that's BepInEx's loader, which loads mods into the game; it's a known false positive.
- **Reporting a bug:** set `Trace = true` in the settings file, start the game, reproduce the problem, then open an issue at https://github.com/StDannyDan/disco-voice-speed/issues (or comment on the Nexus page) and attach `BepInEx\LogOutput.log`. It includes your game version. The log can also contain folder paths from your PC (e.g. your Windows user name); look it over before posting if that matters to you.

## Uninstall

- **Just this mod:** delete the folder `BepInEx\plugins\DiscoVoiceSpeed` (and `BepInEx\config\stdannydan.discovoicespeed.cfg` if you like).
- **BepInEx as well**, only if no other BepInEx mods are installed: from the game folder, delete `BepInEx`, `dotnet`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and `changelog.txt`.

## How it works

When the game starts a voice line, the mod reads the clip's audio and speeds it up with a C# port of [Sonic](https://github.com/waywardgeek/sonic), a speech speed-up algorithm made for screen readers (Android's ExoPlayer media player uses it for playback speed too). The mod then plays the shorter copy instead; that takes a few tens of milliseconds per line. No game files are changed. To build it yourself, see [DEVELOPMENT.md](https://github.com/StDannyDan/disco-voice-speed/blob/main/DEVELOPMENT.md).

## License

Licensed under the [Apache License 2.0](LICENSE). See [NOTICE](NOTICE) for attributions.

`Sonic.cs` is a port of part of the [Sonic](https://github.com/waywardgeek/sonic) library, Copyright 2010 Bill Cox, also Apache 2.0.

Disco Elysium is a trademark of ZA/UM. This mod is not affiliated with or endorsed by ZA/UM, and contains no game files.
