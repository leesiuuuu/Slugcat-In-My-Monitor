# SlugcatInMyMonitor

<p align="center">
  <strong>English</strong> | <a href="README.ko.md">한국어</a>
</p>

<p align="center">
  <img src="docs/media/icon.png" alt="SlugcatInMyMonitor icon" width="96">
</p>

**A desktop pet that brings Rain World's Slugcats to your Windows desktop.**
Slugcats treat monitor and window edges as floors and walls, autonomously choosing
to walk, jump, fall, climb, and rest. You can pick them up and throw them with the
mouse, and run up to eight Slugcats at once.

![Several Slugcats roaming a desktop](docs/media/readme/example-preview.gif)

> [!IMPORTANT]
> **A purchased, locally installed PC copy of Rain World is required.**
> This app does not ship the original Slugcat artwork or skins. It reads the
> original atlases and installed mod information from your local Rain World
> installation. The app will not start without a valid `RainWorld.exe` and
> `RainWorld_Data` directory, and missing or incompatible game files may cause
> characters to render incorrectly.

Rain World, Steam, and Unity do not need to be running. The app does not load the
game executable or its DLLs; it only reads the assets it needs from your local
installation. Rain World and community skin assets are not included in this
repository or its releases.

## Features

- **Desktop terrain:** autonomous movement, jumping, wall climbing, and resting on windows and monitors
- **Multiple Slugcats:** create, select, and remove characters with individual movement stats and abilities
- **Direct interaction:** pick up and throw Slugcats with the mouse
- **Feeding:** drop a Blue Fruit or Eggbug Egg from the tray and observe appetite-driven eating or refusal
- **Original appearance:** graphics built from the original Rain World atlases
- **DMS skins:** per-part skin selection and color editing (experimental)
- **GPU rendering:** DirectComposition multi-surface composition with smoke and explosion effects
- **Smooth motion:** refresh-rate-aware rendering interpolation over a fixed 40 Hz simulation

Runtime audio is currently disabled for performance and stability.

## Supported Slugcats

These characters use individual movement stats and the currently implemented
special abilities, rather than simple color swaps. Interactions that require Rain
World's rooms, creatures, or item systems are reduced or omitted on the desktop.

| Slugcat | CLI name | Currently implemented traits |
|---|---|---|
| Survivor | `white` | Standard movement and baseline stats |
| Monk | `yellow` | Lighter body and gentler movement profile |
| Hunter | `red` | Faster movement and stronger physical stats |
| Gourmand | `gourmand` | Weight, stamina, rolling, and belly sliding |
| Artificer | `artificer` | Explosive jumps, shockwaves, and self-destruct effects |
| SpearMaster | `spearmaster` | Needle-spear creation and throwing |
| Rivulet | `rivulet` | Fast running, jumping, and climbing |
| Saint | `saint` | Tongue and rope traversal |

See the [Slugcat ability parity notes (Korean)](docs/SlugcatAbilityParity.md) for
the exact implementation scope and differences from the original game.

## Requirements

### Required

- **64-bit Windows 10 or Windows 11**
- **Microsoft .NET Framework 4.8 Runtime**
- **A purchased PC copy of Rain World installed locally**
  - The installation directory must contain `RainWorld.exe` and `RainWorld_Data`.
  - The game files must be intact so the app can read the original Slugcat atlases.
- **A Direct3D 11-capable GPU and driver**
  - DirectComposition and the required DirectX components are included with
    Windows 10 and 11, so no separate installation is normally needed.

Visual Studio, the .NET SDK, Unity, and BepInEx are not required to run a release.
The native renderer uses the static C++ runtime, so a separate Visual C++
Redistributable is not required either.

### Additional requirements for external DMS skins

- **Dress My Slugcat (DMS)** installed and enabled in Rain World's Remix menu
- A skin mod in DMS format
- That DMS skin mod enabled in Rain World's Remix menu
- The **Steam client** when installing skins from Steam Workshop

Steam is only needed for automatic Workshop discovery. If Rain World and the mods
were installed manually and you provide their paths, Steam does not need to stay
open while this app runs. Because mod activation is read from Rain World's config,
enable DMS and the skin mod, then exit Rain World normally at least once. The
Downpour DLC is not required by this app, but it is required if your chosen skin
depends on Downpour assets.

## Install and run

1. **Download from [GitHub Releases](https://github.com/leesiuuuu/Slugcat-In-My-Monitor/releases):** get the latest `win-x64.zip`.
2. **Extract the archive:** extract every file into the same folder.
3. **Launch the app:** run `SlugcatInMyMonitor.exe`.
4. **Select the Rain World directory:** if it is not detected automatically, choose the folder containing `RainWorld.exe`.

Do not delete or move `SlugcatInMyMonitor.DirectComposition.dll` away from the
executable; rendering cannot start without it.

You may also select the initial character or Rain World installation from the
command line.

```powershell
# Start as Gourmand
.\SlugcatInMyMonitor.exe --slugcat gourmand

# Available: white, yellow, red, gourmand, artificer,
#            spearmaster, rivulet, saint

# Provide the Rain World installation path
.\SlugcatInMyMonitor.exe `
  --rain-world "C:\Program Files (x86)\Steam\steamapps\common\Rain World"
```

The verified Rain World path is saved to
`%LOCALAPPDATA%\SlugcatInMyMonitor\rain-world-path.txt`.

## Settings and controls

![SlugcatInMyMonitor settings panel](docs/media/readme/settingPanel-rework.png)

Left-click the Slugcat system-tray icon to open the settings panel. From there you
can:

- add, select, or remove a Slugcat;
- change the character and ability;
- choose the UI language (한국어/English; applied after restart);
- open the skin editor;
- toggle debug visuals or pause all Slugcats;
- refresh Workshop mods;
- retry rendering or quit the app.

The basic controls are:

- **Left mouse button on a Slugcat:** pick it up
- **Move while holding, then release:** throw it
- **While holding a Slugcat:** block other drag input such as desktop selection
- **Click or pick up a Slugcat:** select it for configuration
- **Left-click the tray icon:** open the settings panel
- **Right-click the tray icon → Feed:** drop a Blue Fruit or Eggbug Egg near the selected Slugcat
- **Leave every monitor:** return to a safe floor after about one second

No global shortcuts are registered, avoiding conflicts with system shortcuts. The
food and Slugcat overlays remain click-through during normal desktop use; only
directly grabbing a Slugcat consumes left-drag input. The tray icon's context menu
is also a fallback when the settings window cannot open.

See the [food update report (Korean)](docs/FoodUpdateReport.ko.md) for the current
implementation scope and instructions for adding new food types.

## Skin editor (experimental)

> [!WARNING]
> The skin editor is experimental. Its UI, preset format, supported features, and
> output may change in later releases. External skins must use the **Dress My
> Slugcat (DMS) format**. SlugBase characters, regions, gameplay code, and mod DLLs
> are not executed.

![Experimental Slugcat skin panel](docs/media/readme/skinPanel-rework.png)

The skin editor lets you select and recolor the head, face, body, arms, hips, legs,
tail, and The Mark independently. Parts from different DMS skins can be combined,
and configurations can be saved and loaded as presets.

If a DMS skin does not appear, check all of the following:

1. **Game installation:** confirm that the app found the correct Rain World installation.
2. **Mod installation:** install Dress My Slugcat and the DMS skin mod in Rain World.
3. **Mod activation:** enable both DMS and the skin in Remix, then exit the game normally.
4. **Refresh:** use **Refresh Workshop mods** in this app or restart it.

The app scans `mods`, `mergedmods`, and discovered Steam Workshop folders for
`metadata.json` files and PNG/TXT atlas pairs. Damaged parts, parts missing required
frames, disabled skin mods, and non-DMS mods are excluded from the selection list.

## Troubleshooting

- **Rain World installation not found:** select the top-level folder containing `RainWorld.exe`, or provide it with `--rain-world`.
- **Broken default appearance or procedural fallback graphics:** verify the Rain World game files through Steam or your store, then restart the app.
- **DMS skin is missing:** verify the DMS installation, Remix activation, and PNG/TXT atlas pair, then refresh Workshop mods.
- **Frozen screen or missing rendering:** choose **Retry rendering** from the tray menu and update your graphics driver.
- **Finding errors:** inspect `%LOCALAPPDATA%\SlugcatInMyMonitor\errors.log` and `%LOCALAPPDATA%\SlugcatInMyMonitor\workshop.log`.

## Development

A development build requires:

- PowerShell 5.1 or later
- Visual Studio 2022 C++ desktop build tools (v143)
- Windows 10/11 SDK

The build script downloads the `.NET Framework 4.8` reference assemblies when
needed. Build a Release configuration and run the full test suite with:

```powershell
.\build.ps1 -Configuration Release
```

The DirectComposition bridge uses the Windows SDK's Direct3D 11, DXGI, and
DirectComposition libraries. Submit normal changes from `feature/*` or `fix/*` to
`develop`; create release pull requests from `develop` to `main`. See the
[contribution guide](CONTRIBUTING.md) for the complete workflow.

Implementation details are documented in:

- [Architecture](docs/Architecture.md)
- [Original behavior map (Korean)](docs/RainWorldBehaviorMap.md)
- [Slugcat ability parity (Korean)](docs/SlugcatAbilityParity.md)
- [Slugcat graphics profiles (Korean)](docs/SlugcatGraphicsProfiles.md)
- [Workshop and DMS compatibility](docs/WorkshopCompatibility.md)
- [Local asset findings (Korean)](docs/analysis/AssetFindings.md)
- [DLL findings (Korean)](docs/analysis/DllFindings.md)
- [Original-fidelity overhaul notes (Korean)](docs/analysis/RainWorldFidelityOverhaul.md)

## Assets, license, and trademarks

This repository does not distribute images or game assets from Rain World, Dress
My Slugcat, or community skins. Users must legitimately own Rain World and any
skins they use, and must follow the terms for those assets. See
[THIRD_PARTY_TEST_ASSETS.md](THIRD_PARTY_TEST_ASSETS.md) for details.

This is an unofficial fan project and is not affiliated with or endorsed by
Videocult or Akupara Games. Rain World and related names and assets belong to their
respective owners. Project code is distributed under the [MIT License](LICENSE);
that license does not apply to third-party assets.
