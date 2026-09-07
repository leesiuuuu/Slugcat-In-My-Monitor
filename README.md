# SlugcatInMyMonitor

<p align="center">
  <strong>English</strong> | <a href="README.ko.md">한국어</a>
</p>

<p align="center">
  <img src="docs/media/readme/example-preview.gif" alt="Several Slugcats roaming across the Windows desktop" width="100%">
</p>

**Bring Rain World's Slugcats onto your Windows desktop.**

SlugcatInMyMonitor is an independent desktop pet for 64-bit Windows. Slugcats use
monitor boundaries and real application windows as terrain, then decide for themselves
when to walk, jump, climb, rest, watch the cursor, or visit another ledge.

> [!IMPORTANT]
> **A purchased, locally installed PC copy of Rain World is required.**
> The app reads the original Slugcat artwork and supported sounds from your own
> installation. Rain World, community skins, game executables, and game DLLs are not
> included in this repository or its releases.

Rain World, Steam, Unity, and BepInEx do not need to be running with the pet. The app
uses the required files from the local installation in read-only form.

## Highlights

- **Desktop terrain:** monitor work areas, screen edges, and visible window tops and sides
  become floors, ledges, and walls.
- **Autonomous behavior:** each pet has its own personality, attention, rest cycle, route
  choices, and character-specific movement.
- **Direct commands:** right-click a Slugcat to open a radial command
  wheel with Stop, Move, and Follow Me.
- **Up to eight Slugcats:** add, select, resize, change, or remove pets independently.
- **Mouse interaction:** pick up and throw Slugcats, move food, and let pets react to the
  cursor without blocking ordinary desktop clicks.
- **Food and appetite:** place Blue Fruit or Eggbug Eggs and observe eating, refusal,
  fullness, and digestion.
- **Original appearance:** use the Slugcat artwork from the locally installed Rain World copy.
- **Optional mod integration:** use Dress My Slugcat skins and Push To Meow voices when
  those mods are installed and enabled.

## Commands and controls

Right-click a Slugcat to open the command wheel. The selected Slugcat pauses while the
wheel is waiting for a choice. Move the pointer over a segment and left-click its icon.

| Icon | Command | Behavior |
| --- | --- | --- |
| Pause bars | **Stop** | Stops locomotion at the current position. Blinking, looking around, facial motion, idle posture, and available meows continue. |
| Play triangle | **Move** | Returns to the normal autonomous AI. |
| Arrow toward pointer | **Follow Me** | Tries to reach the mouse using walking and variable-height jumps, while occasionally pausing, crouching, glancing, or taking a short detour. |

Other controls:

| Input | Action |
| --- | --- |
| Left-drag a Slugcat | Pick it up; release with mouse momentum to place or throw it. |
| Right-click a Slugcat | Open its command wheel. |
| Left-click the tray icon | Open Settings. |
| Right-click the tray icon | Open quick actions, character selection, feeding, and exit controls. |
| Choose food, then left-click the desktop | Place the selected food. |
| Left-drag placed food | Move or toss the food. |

No global keyboard shortcut is registered. Empty overlay pixels remain click-through, so
normal desktop and application interaction continues behind the pets.

## Requirements

### Required

- **64-bit Windows 10 or Windows 11**
- **Microsoft .NET Framework 4.8 Runtime**
- **A purchased PC copy of Rain World installed locally**
  - The selected directory must contain `RainWorld.exe` and `RainWorld_Data`.
  - The game files must be intact so the original graphics and supported sounds can be read.
- **A Direct3D 11-capable GPU and current graphics driver**

Visual Studio, the .NET SDK, Unity, BepInEx, and the Visual C++ Redistributable are not
required to run a release build.

### Optional integrations

- **More Slugcats Expansion:** required for the Slugpup appearance toggle and for content
  that depends on expansion assets.
- **Dress My Slugcat (DMS):** required only for external DMS skins. Enable DMS and the
  desired skin in Rain World's Remix menu, then exit the game normally once.
- **Push To Meow:** required only for automatic meow audio and its matching closed-eye,
  upward-look, or SpearMaster tail animation. Without the mod, those meow-specific events
  do not run; ordinary face and idle animations still do.
- **Steam:** needed only when the app must discover Workshop-installed mods. It does not
  need to stay open after the files are available locally.

## Install and run

1. Download the latest Windows archive from
   [GitHub Releases](https://github.com/leesiuuuu/Slugcat-In-My-Monitor/releases).
2. Extract **every file** from the archive into one folder.
3. Run `SlugcatInMyMonitor.exe`.
4. If Rain World is not detected automatically, select the folder containing
   `RainWorld.exe`.

Keep every extracted file beside the executable. Moving or deleting individual files may
prevent the app from starting or rendering. The verified Rain World path is saved to
`%LOCALAPPDATA%\SlugcatInMyMonitor\rain-world-path.txt`.

Optional command-line arguments:

```powershell
# Start as Gourmand
.\SlugcatInMyMonitor.exe --slugcat gourmand

# Available: white, yellow, red, gourmand, artificer,
#            spearmaster, rivulet, saint

# Select the Rain World installation explicitly
.\SlugcatInMyMonitor.exe `
  --rain-world "C:\Program Files (x86)\Steam\steamapps\common\Rain World"

# Start with diagnostics visible
.\SlugcatInMyMonitor.exe --debug
```

## Settings

![SlugcatInMyMonitor Settings window](docs/media/readme/settingPanel-rework.png)

Left-click the tray icon to open Settings. The window provides one place to:

- add, select, and remove up to eight Slugcats;
- change the selected character and its implemented ability set;
- choose Small, Normal, or Large size;
- enable Slugpup appearance when More Slugcats Expansion is available;
- open the experimental skin editor or refresh Workshop data;
- pause every Slugcat or enable the debug overlay;
- mute audio and set master volume from 0% to 200%;
- retry rendering after a graphics failure;
- switch between Korean and English UI (restart required after changing language).

Audio starts muted on a new installation and remembers the last mute and volume settings.

## Skin editor (Experimental)

![Experimental Slugcat Skin Editor](docs/media/readme/skinPanel-rework.png)

> [!WARNING]
> The skin editor is experimental. Its UI, preset format, compatibility, and output may
> change. It reads DMS skin files, but does not execute SlugBase characters, regions,
> gameplay code, or mod DLLs.

The editor can mix and recolor individual parts from Vanilla and installed DMS skins:
head, face, body, arms, hips, legs, tail, Artificer face scar, Rivulet gills,
SpearMaster tail speckles, Saint ascension graphics, and The Mark. Character-only
structures appear only when the selected Slugcat supports them. An incomplete DMS part
falls back to the current Vanilla Slugcat instead of leaving the character broken.

Use **Copy/Paste** to transfer a setup, **Save/Load Preset** to keep it, **Reset** to
return to defaults, and **Reload Sprites** after changing installed files.

If a DMS skin does not appear:

1. Confirm that the app found the correct Rain World installation.
2. Install Dress My Slugcat and the desired DMS skin.
3. Enable both mods in Remix and exit Rain World normally.
4. Select **Refresh Workshop** in Settings or restart the app.

Only enabled DMS skins with complete, compatible sprite files appear in the editor.

## Supported Slugcats

These are behavior profiles, not simple color swaps. Systems that require Rain World's
rooms, creatures, campaigns, or full item simulation are adapted or omitted for the
desktop environment.

| Slugcat | CLI name | Implemented desktop traits |
| --- | --- | --- |
| Survivor | `white` | Standard movement and abilities |
| Monk | `yellow` | Gentler movement characteristics |
| Hunter | `red` | Stronger, faster physical profile |
| Gourmand | `gourmand` | Heavy body type |
| Artificer | `artificer` | Explosive jumps, smoke, shockwaves, and self-destruct effects |
| SpearMaster | `spearmaster` | Needle-spear creation, aiming, and throwing |
| Rivulet | `rivulet` | Fast running and higher, more agile jumps |
| Saint | `saint` | Tongue firing, attachment, rope motion, and traversal |

## Audio and Push To Meow

The app plays supported movement, impact, and implemented ability sounds from the local
Rain World installation.

When Push To Meow is installed and enabled, a successful meow triggers both its sound and
the matching face/tail animation. If the mod is absent, disabled, muted, or cannot supply
a compatible voice, no meow-specific animation is triggered.

No Rain World or mod audio is copied into the repository or release archive.

## Troubleshooting

- **Rain World installation not found:** select the top-level directory containing
  `RainWorld.exe`, or pass it with `--rain-world`.
- **Broken appearance or procedural fallback:** verify Rain World's installed files, then
  restart the app.
- **Slugpup option unavailable:** install/enable More Slugcats Expansion and make sure the
  correct Rain World installation was selected.
- **DMS skin missing:** verify DMS and skin activation and confirm that the skin files are
  compatible, then refresh Workshop data.
- **No sound:** clear **Mute Audio**, raise the volume, and check the selected Rain World
  installation. The first run is muted by default.
- **No meows:** Push To Meow must be installed, enabled, and contain a compatible voice.
- **Rendering paused or missing:** use **Retry Rendering** and update the GPU driver.
- **Logs:** inspect `%LOCALAPPDATA%\SlugcatInMyMonitor\errors.log` and
  `%LOCALAPPDATA%\SlugcatInMyMonitor\workshop.log`.

## Development

A development build requires PowerShell 5.1 or later, Visual Studio 2022 C++ desktop
build tools (v143), and a Windows 10/11 SDK.

```powershell
.\build.ps1 -Configuration Release
```

This builds the application, runs the complete test suite, and writes output to
`artifacts\Release`.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the workflow. Further implementation details:

- [Architecture](docs/Architecture.md)
- [Behavior compatibility and source boundary](docs/BehaviorCompatibility.md)
- [Workshop and DMS compatibility](docs/WorkshopCompatibility.md)
- [Food update report (Korean)](docs/FoodUpdateReport.ko.md)

## Assets, license, and trademarks

This repository does not distribute Rain World, Dress My Slugcat, Push To Meow, or
community skin/audio assets. Users must legitimately own Rain World and follow the terms
for every third-party asset they use. See
[THIRD_PARTY_TEST_ASSETS.md](THIRD_PARTY_TEST_ASSETS.md) for the test-asset boundary.

This is an unofficial fan project and is not affiliated with or endorsed by
Videocult or Akupara Games. Rain World and related names and assets belong to their
respective owners.

The [MIT License](LICENSE) applies only to code and other material independently
authored by this project's contributors and for which they hold the necessary
licensing rights. It does not apply to Rain World source or object code,
characters, artwork, audio, game assets, names, trademarks, or any other
third-party material. Nothing in this repository grants a license to use those
materials. Third-party components remain subject to their respective licenses and
terms.
