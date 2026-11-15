# Changelog

## 1.0.0 – 2026-11-14

First release.

- Plays voiced dialogue faster without changing the pitch: 1x–4x in 0.25 steps, 2x by default. Uses a C# port of the Sonic speech speed-up algorithm and takes a few tens of milliseconds per line.
- Hotkeys: Ctrl + = faster, Ctrl + - slower, Ctrl + 0 off/on (numpad keys too). They can be changed in the settings file.
- Short on-screen message in the game's font when the speed changes.
- Fail-safe: if a game update changes what the mod hooks into, it switches itself off and voices play at normal speed. A line that fails to speed up plays normally, and 3 failures in a row switch the mod off until the next start.
- Works alongside Furious Tare.
- Ambient voice lines ("barks", e.g. the radio) stay at normal speed.
- Optional detailed logging for bug reports (`Trace = true` in the settings file).

Tested on Disco Elysium – The Final Cut, Steam build 23980936, with BepInEx 6.0.0-be.755 (included in the "with BepInEx" download) and be.688 (Furious Tare's build).
