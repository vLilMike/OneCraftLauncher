# One Craft Launcher

Download: [OneCraftLauncher.exe](https://raw.githubusercontent.com/vLilMike/OneCraftLauncher/main/download/OneCraftLauncher.exe)

Press **PLAY** and the launcher:

1. Installs NeoForge 21.1.265 for Minecraft 1.21.1 (once). It downloads Java 21 for this if the PC doesn't have it yet.
2. Downloads or updates One Craft, RP Essentials and GeckoLib into its own game folder, `%APPDATA%\.onecraft`.
3. Adds a **One Craft** profile to the official Minecraft Launcher and opens it. Sign in there, pick One Craft and press Play.

Other Minecraft installs, worlds and mods are never touched. Mods you add to `.onecraft\mods` yourself are kept.

## Releasing a new One Craft version

```
powershell -ExecutionPolicy Bypass -File tools\publish.ps1 -Note "What changed", "Something else that changed"
git add -A
git commit -m "One Craft <version>"
git push
```

`publish.ps1` takes the newest JAR from `OnePieceMod\releases` and `RPEssentials\build\libs`, copies it into `mods/` and updates `manifest.json`. Every launcher picks it up the next time someone presses PLAY.

## Updating the launcher itself

Raise `Version` in `src/LauncherForm.cs` and `launcher.version` in `manifest.json`, run `build.ps1`, then commit and push. Open launchers show **Restart to update**.

## Files

| Path | What it is |
|---|---|
| `manifest.json` | What every launcher reads: versions, mod download links with SHA-1, patch notes, and optional `servers` (`{"name","ip"}`) added to a new player's multiplayer list |
| `mods/` | One Craft and RP Essentials JARs. GeckoLib downloads from Modrinth |
| `download/OneCraftLauncher.exe` | The launcher players download |
| `src/` | Launcher source (C#, built by `build.ps1` with the compiler that comes with Windows) |
| `assets/` | Banner screenshots and icon |
| `tools/` | `publish.ps1`, and `make-icon.ps1` for the icon |

Troubleshooting: the gear button has **Open launcher log** (`%APPDATA%\.onecraft\launcher\launcher.log`).
