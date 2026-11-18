# SoundDock Windows installer

This repository builds a self-contained x64 Windows installer with [Inno Setup](https://jrsoftware.org/inno-setup/downloads). End users do **not** need the .NET SDK or .NET runtime.

## Build on Windows

Install:

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Inno Setup 6 or 7](https://jrsoftware.org/inno-setup/downloads)

Double-click **`BUILD_INSTALLER.cmd`** in the repository root. It publishes the app and creates:

`dist\SoundDock-Setup-1.4.0-win-x64.exe`

The version is read from `SoundDock/SoundDock.csproj`. You can also run `./build-installer.ps1` in PowerShell. If the app has already been published, run `./build-installer.ps1 -SkipPublish` to compile only the setup file.

## Build on GitHub (no local SDK needed)

1. Upload the repository contents, including `.github/workflows/build-installer.yml`, to GitHub.
2. Open **Actions > Build SoundDock installer > Run workflow** (select your main branch).
3. When the run completes, open the workflow run and download **SoundDock-Windows-Installer** from **Artifacts**.
4. Extract the artifact ZIP; it contains the `SoundDock-Setup-...exe` installer.

A push of a version tag such as `v1.4.0` also builds and attaches the installer to a corresponding GitHub Release. **Make the tag match the `<Version>` in SoundDock.csproj.** The workflow checks this before publishing.

## What the installer does

- Installs for the current Windows account at `%LOCALAPPDATA%\Programs\SoundDock`. It does **not** prompt for admin rights.
- Adds SoundDock to the Start Menu and **Installed apps** (including uninstall support).
- Offers an optional desktop shortcut.
- Offers **Launch SoundDock** at the end. Its normal first-launch behavior registers **Start with Windows** unless the user had explicitly disabled it previously.
- Uses a stable installation ID, so running a new SoundDock setup upgrades the existing installation in place.
- Detects SoundDock, BarTone, and Taskbar Audio if they are still running and asks you to close them before installation/uninstallation.
- Retains `%LOCALAPPDATA%\SoundDock\settings.json` when updating or uninstalling, preserving hidden outputs, labels, and preferences for later reinstalls.
- On uninstall, removes the **SoundDock** startup registry value **only if it points to the executable being removed**, avoiding damage to another copy elsewhere.

**Existing portable installation:** A setup installer does not delete old portable files automatically. Exit the old app before installing; once installed, run SoundDock from the Start Menu. On first run it updates the existing SoundDock startup entry to the installed executable. Delete the old portable folder afterward if no longer needed. There may also be legacy Taskbar Audio/BarTone folders, which are not deleted by the installer.

## Notes for distributing

- This installer is **unsigned**. Windows SmartScreen may display an unrecognized publisher warning for new downloads. For public distribution, sign the installer and application with a trusted code-signing certificate. Never tell users to disable SmartScreen globally.
- The installer currently targets **Windows x64**. ARM64 needs its own publish and installer configuration.
- Test on a real Windows 10/11 PC before distributing publicly: install, startup after sign-in, upgrade, uninstall, and per-user data retention.
- Publishing directly from a GitHub Release allows the website's Releases link to offer a setup executable instead of requiring visitors to build from source.
