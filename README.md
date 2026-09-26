<img src="assets/icon-source.png" alt="ExitEcho logo" width="96">

# ExitEcho

**See what keeps running after an app exits.**

ExitEcho is a lightweight utility for **Windows 10/11 x64** that detects processes an application leaves running after its visible windows are closed.

> **Download for Windows 10/11 x64:** [GitHub Releases](https://github.com/Feel11ng/ExitEcho/releases)

## Why

Closing an app's window does not always end its processes. ExitEcho shows which related processes remain so you can decide whether to leave them running. Some apps intentionally continue working in the background.

## How it works

ExitEcho watches visible top-level windows and related process starts and stops. It identifies processes by PID and creation time. After an app's last visible window disappears, it waits eight seconds before reporting any related processes still running.

## Features

- Passive monitoring and leftover notifications
- Process name, PID, and RAM details
- Manual termination with confirmation; per-app ignore list
- Tray pause control and single-instance GUI
- CLI `run` and `watch` modes

## Installation

Download the portable ZIP from Releases, extract it, and run `ExitEcho.exe`. No installer or account is needed.

## Usage

ExitEcho starts in the system tray. Use its menu to open the window, pause monitoring, or exit. Select **Details** on a notification to inspect processes; **End leftovers** always asks for confirmation.

The source build also provides a CLI:

```text
exitecho watch
exitecho run "<path-to-exe>" [args]
```

## Privacy

ExitEcho runs fully locally. It requires no account, collects no telemetry, and makes no network requests.

## Limitations

Only applications observed with visible top-level windows are tracked. Very short-lived or inaccessible processes may be missed. A remaining process is not necessarily a problem; many applications run background tasks by design.

## Building from source

On Windows 10/11 x64 with the .NET 8 SDK:

```text
dotnet build ExitEcho.sln -c Release
dotnet publish ExitEcho.App/ExitEcho.App.csproj -c Release -r win-x64 --self-contained true
```

<!-- Future media: assets/demo.gif and assets/screenshot.png -->
