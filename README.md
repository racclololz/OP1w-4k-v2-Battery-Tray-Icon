# OP1w 4K v2 Battery Tray Icon

Lightweight Windows tray app for showing the battery percentage of the Endgame Gear OP1w 4K v2 mouse.

## Features

- Shows the current battery percentage directly in the Windows tray icon.
- Green at `50-100%`, yellow at `20-49%`, red at `0-19%`.
- Polls the mouse/dock once per minute.
- Right-click menu with `Run at startup` and `Exit`.
- Left-click opens the official Endgame Gear configuration tool if it is placed beside the tray app.

## Supported Hardware

- Endgame Gear OP1w 4K v2
- Wireless dock/dongle: `VID_3367&PID_1970`
- Wired mouse fallback: `VID_3367&PID_1984`

Other mice are not supported unless their battery protocol is added separately.

## Requirements

To build from source:

- Windows
- .NET 8 SDK

To run a framework-dependent build:

- .NET 8 Desktop Runtime

## Build

From this folder:

```powershell
dotnet publish . -c Release -r win-x64 --self-contained false -o publish
```

The app will be created in:

```text
publish\OP1wBatteryTray.exe
```

For a release build that does not require users to install .NET separately:

```powershell
dotnet publish . -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

## Optional Configuration Tool Launch

The official Endgame Gear configuration tool is not included in this repository.

If you want left-clicking the tray icon to open it, place this file beside `OP1wBatteryTray.exe`:

```text
Endgame_Gear_OP1w_4k_v2_Configuration_Tool_v1_02.exe
```

Do not redistribute that EXE unless Endgame Gear's license allows it.

## Diagnostics

Read the battery once:

```powershell
.\OP1wBatteryTray.exe --once
```

Print HID diagnostic details:

```powershell
.\OP1wBatteryTray.exe --diag
```

## Notes

This app reads the mouse battery through the device's HID feature report. It does not use the official configuration tool for normal tray updates.
