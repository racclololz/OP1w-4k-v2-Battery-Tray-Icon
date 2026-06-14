# OP1w 4K v2 Battery Tray Icon

Windows tray icon that shows the battery percentage for the Endgame Gear OP1w 4K v2.

## Features

- Battery percentage shown directly in the tray icon
- Green at 50-100%, yellow at 20-49%, red at 0-19%
- Polls once per minute
- Right-click menu: Run at startup, Exit
- Left-click opens the official Endgame Gear config tool if it is placed beside the app

## Supported Device

- Endgame Gear OP1w 4K v2
- Dock/dongle: VID_3367&PID_1970
- Wired fallback: VID_3367&PID_1984

## Build

Requires Windows and the .NET 8 SDK.

```powershell
dotnet publish . -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
