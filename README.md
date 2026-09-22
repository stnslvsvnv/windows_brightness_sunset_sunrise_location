# Windows Auto Brightness by Sunrise/Sunset

A Windows app that automatically adjusts laptop screen brightness by time of day.

It can work by:
- sunrise/sunset (based on your location), or
- manual day/night schedule.

## What It Does

- Runs in tray and applies brightness automatically every 15 seconds.
- Uses separate `Day brightness` and `Night brightness` values.
- Supports smooth transitions with cosine easing:
  - **Dawn**: gradually increases brightness before sunrise/day start.
  - **Twilight**: gradually decreases brightness after sunset/night start.
- Transition duration is configurable: **0 to 20 minutes** (step 1 minute).
  - `0` means instant switch.
- **Auto-switch Windows theme**: dark theme at night, light theme during the day.
- Can start with Windows.
- Can detect location via Windows location services (Wi-Fi/GNSS), IP geolocation, a fallback city and the last known location — in that order, and a location from an earlier time zone is treated as stale after a move.

## Quick Start (Ready Installer)

Download and run:
- `NotebookAutoBrightnessSetup.exe` (in this repository root)

## Build From Source

Requirements:
- Windows 10/11
- .NET 8 SDK

Build app:

```powershell
cd notebook-auto-brightness
dotnet build src/NotebookAutoBrightness/NotebookAutoBrightness.csproj
```

Run app from source:

```powershell
cd notebook-auto-brightness
dotnet run --project src/NotebookAutoBrightness/NotebookAutoBrightness.csproj
```

Build installer:

```powershell
cd notebook-auto-brightness
./build-installer.ps1
```

## Main Settings

- `Enabled` - turns automation on/off.
- `Use sunrise/sunset schedule` - uses astronomy times (if location is available).
- `Use geolocation (IP-based)` - auto-detects coordinates.
- `City` - fallback location if geolocation is off or unavailable.
- `Day brightness` / `Night brightness` - brightness limits (0-100).
- `Transition duration` - transition window in minutes (0-20).
- `Auto-switch Windows theme (dark at night)` - automatically switches Windows theme:
  - **Light theme** during the day (at sunrise/day start).
  - **Dark theme** at night (at sunset/night start).
- `Start with Windows` - autorun.

## How Transitions Work

If transition duration is `N` minutes:
- Morning: from `sunrise - N` to `sunrise` (night -> day brightness).
- Evening: from `sunset` to `sunset + N` (day -> night brightness).

In manual schedule mode:
- Morning: from `day start - N` to `day start`.
- Evening: from `night start` to `night start + N`.

## Project Structure

- `notebook-auto-brightness/src/NotebookAutoBrightness` - main WinForms app.
- `notebook-auto-brightness/src/Installer` - installer project.
- `notebook-auto-brightness/build-installer.ps1` - installer build script.
- `NotebookAutoBrightnessSetup.exe` - prebuilt installer artifact.

## Notes

- Brightness control uses WMI and depends on hardware/driver support.
- External monitors may not support this method.
- Windows theme switching updates both theme registry values and broadcasts the change to running apps.
