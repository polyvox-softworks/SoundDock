<div align="center">
  <img src="SoundDock/Assets/app.svg" alt="SoundDock icon" width="78" height="78">

  # SoundDock

  A lightweight Windows taskbar audio controller that keeps your current output and volume in view.

  **Windows 10 / 11** &nbsp;|&nbsp; **Portable** 

  [Website](https://polyvox.dev/)
</div>

## Why SoundDock?

Windows can make switching between speakers, headphones, and monitors take more clicks than necessary. SoundDock puts the essentials directly on the taskbar, just to the left of the notification area when the taskbar layout permits it.

- **At-a-glance status** - see the active output name and volume percentage.
- **Fast output switching** - choose a playback device from a compact menu.
- **Volume mixer** - adjust master volume and audio sessions individually.
- **Mouse-wheel control** - scroll on the taskbar indicator or volume rows to change levels.
- **Hide unused devices** - right-click an inactive output to hide it, and restore it later.
- **Personalization** - rename taskbar output labels and use your Windows accent color for sliders.
- **Start with Windows** - enabled on first launch; optional afterward.

## Installation

### Option 1: Use a release build

Check the [Releases page](https://github.com/polyvox-softworks/SoundDock/releases) for a Windows build. Download the appropriate portable package, extract the full folder to a permanent location, and run the installer.

### Option 2: Build from source

You need Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

1. Clone or download this repository.
2. Open the .sln and build from source code. 

## Controls

| Action | Result |
| --- | --- |
| Click taskbar indicator | Open or close the sound panel |
| Scroll over taskbar indicator | Change master volume (2% per notch) |
| Middle-click taskbar indicator | Mute or unmute the active output |
| Right-click taskbar indicator | Open the settings and customization menu |
| Click a playback output | Make it the Windows default output |
| Right-click an inactive output | Hide that device from the output list |
| Open **Hidden devices** | Restore individual hidden outputs |
| Drag or scroll over a volume row | Adjust master volume or an application's volume |
| Click a volume row's speaker | Mute or unmute that output or app |
| Click **More sound settings** | Open Windows sound settings |

The indicator responds to Windows volume-change notifications and also polls periodically as a fallback. SoundDock can use the Windows accent color for volume sliders.

## Startup and settings

SoundDock registers itself to start with Windows on its first launch, for the current user only. No administrator rights are required. To turn this off, right-click its taskbar indicator and uncheck **Start with Windows**. Your choice is remembered.

Keep the executable in a permanent location before the first run. If you move it later, launch the new copy once to update the startup path.

Settings and logs are saved locally:

```text
%LOCALAPPDATA%\SoundDock\settings.json
%LOCALAPPDATA%\SoundDock\errors.log
```

## Technical notes

- SoundDock uses Windows Core Audio to discover outputs, change the default playback endpoint, and manage audio sessions.
- Taskbar placement relies on Windows Explorer internals rather than a supported public widget API. A major Windows update or a nonstandard taskbar can affect positioning; SoundDock can fall back to a floating indicator.
- Applications explicitly assigned to a specific output in Windows may ignore a change to the system default output.
- SoundDock does not collect telemetry or make any network requests.
