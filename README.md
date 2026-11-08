# Screen Dimmer

A tiny Windows tray app that dims your whole screen with a hotkey, similar to the dark layer you see while taking a screenshot with the Snipping Tool. Useful for reading at night, or when your monitor's lowest brightness is still too bright.

- One hotkey to turn it on or off, two hotkeys to make it darker or lighter
- Dims everything evenly: all windows, the taskbar, the Start menu, Alt+Tab and notifications
- Doesn't get in the way: you keep clicking and typing as usual
- Remembers your darkness level
- Optional start with Windows
- A single 13 KB `.exe` with no installer, no dependencies and no network access

## Download

Get `ScreenDimmer.exe` from the [latest release](../../releases/latest), put it anywhere (for example `C:\Tools\ScreenDimmer\`) and run it. A crescent moon icon appears in the system tray. If you don't see it, click the **^** arrow next to the clock.

Windows SmartScreen may warn you the first time because the file isn't code-signed. Click **More info → Run anyway**, or [build it yourself](#build-from-source) from the source in this repository.

## Usage

| Action | Shortcut |
|---|---|
| Dim / undim the screen | `Ctrl+Alt+D` or left-click the tray icon |
| Darker | `Ctrl+Alt+Down` |
| Lighter | `Ctrl+Alt+Up` |
| Darkness level, start with Windows, exit | Right-click the tray icon |

Darkness goes from 10% to 90% in steps of 5%. The default is 50%.

## How it works

Screen Dimmer applies a full-screen color filter that scales red, green and blue down by the chosen amount. Windows' own Color Filters work the same way, through the [Magnification API](https://learn.microsoft.com/windows/win32/api/magnification/nf-magnification-magsetfullscreencoloreffect). The filter is applied by the desktop compositor, so it isn't a window that other windows can appear above, and it costs essentially nothing in performance. When you exit the app, the screen returns to normal.

## Notes

- **Windows Color Filters and Magnifier** use the same mechanism, so only one of them can be active at a time. If Screen Dimmer shows a warning that the filter was refused, turn those off first.
- **Exclusive fullscreen games** bypass the compositor and are not dimmed. Borderless or windowed games are.
- **The screen keeps changing brightness on its own while dimmed?** That is usually your monitor's *Dynamic Contrast* (also called DCR, ECO or Smart Contrast) reacting to the darker picture. Turn it off in the monitor's on-screen menu. On G-SYNC or FreeSync monitors, variable refresh rate can also flicker on dark content. Setting it to fullscreen apps only fixes that on the desktop.
- Settings are stored in `%APPDATA%\ScreenDimmer\level.txt`. *Start with Windows* adds an entry under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Requirements

Windows 10 or 11, 64-bit. The .NET Framework 4.x it uses is already part of Windows.

## Build from source

You don't need Visual Studio or any SDK. The C# compiler that ships with Windows is enough:

```bat
git clone https://github.com/aminebaccar/screen-dimmer.git
cd screen-dimmer
build.cmd
```

This produces `ScreenDimmer.exe` next to the source. The whole app is one file, [`ScreenDimmer.cs`](ScreenDimmer.cs).

## Uninstall

1. Right-click the tray icon, untick **Start with Windows**, then click **Exit**.
2. Delete `ScreenDimmer.exe` and the `%APPDATA%\ScreenDimmer` folder.

## License

[MIT](LICENSE)
