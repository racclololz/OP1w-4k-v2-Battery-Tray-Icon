# OP1w 4K v2 Battery Tray Icon

Windows tray icon that shows the battery percentage for the Endgame Gear OP1w 4K v2.

## Features

- Battery percentage shown directly in the tray icon
- Green at 50-100%, yellow at 20-49%, red at 0-19%
- Polls once per minute
- Right-click menu: Run at startup, Refresh now, Exit
- Left-click opens the official Endgame Gear config tool if it is placed beside the app

## Supported Device

- Endgame Gear OP1w 4K v2
- Dock/dongle: VID_3367&PID_1970
- Wired fallback: VID_3367&PID_1984

## Build

Requires Windows and the .NET 8 SDK.

```powershell
dotnet publish . -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

## Battery freshness (1.0.1)

Each poll sends two battery queries and discards the first reply. Only status
`0x01` is accepted as fresh. Status `0x08` means the mouse is unreachable and can
contain cached battery data; it must not renew the last successful reading.
See the [Endgame Gear protocol notes](https://github.com/stuffz/mouse-battery-monitor/blob/main/docs/ENDGAME.md).

When a fresh reading is unavailable, the previous percentage appears grey with
a "last known" tooltip. After five minutes without a fresh reading, the icon
shows `?`. Polling continues automatically; opening the vendor tool is not required.

Diagnostics are stored in `%LOCALAPPDATA%\OP1wBatteryTray\battery.log`, limited
to about 512 KiB plus one rotated file. They record response status, battery
percentage, display updates and refresh errors.

The informational version is `1.0.1-local-fresh-battery`, matching the installed
repair build.

## Regression checks

```powershell
dotnet run --project tests/BatteryTests.csproj -c Release
```

These checks need no mouse. They cover cached replies, the two-query sequence,
busy responses, invalid percentages, short reports and failed exchanges.
