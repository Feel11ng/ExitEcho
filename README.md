<img src="assets/icon-source.png" alt="ExitEcho logo" width="96">

# ExitEcho

**See what keeps running after an app exits.**

ExitEcho is a local Windows utility that shows related processes still running after an application's visible windows close. It lets you inspect them and decide what to do.

![ExitEcho demonstration: a demo app closes and ExitEcho detects remaining processes](assets/demo.gif)

**Windows 10/11 x64** · [Download the portable ZIP from GitHub Releases](https://github.com/Feel11ng/ExitEcho/releases)

## Features

- Watches apps you open normally and reports related processes left running after the last window closes.
- Shows process names, PIDs, and RAM; optional **End leftovers** always asks for confirmation.
- Keeps local exit history and a per-app ignore list.
- Runs in the system tray with pause and resume controls; only one GUI instance runs at a time.
- Provides light and dark themes, plus settings for language, optional Windows startup, notifications, and the detection delay.
- Offers English and 26 other UI languages, including right-to-left Arabic and Hebrew.
- Includes `exitecho watch` and `exitecho run` CLI modes for development and debugging.

## Screenshots

| Main window | Notification |
| --- | --- |
| ![ExitEcho main window in dark mode](assets/screenshots/main.png) | ![ExitEcho leftover notification in dark mode](assets/screenshots/notification.png) |
| Details | Settings |
| ![ExitEcho process details in dark mode](assets/screenshots/details.png) | ![ExitEcho settings in dark mode](assets/screenshots/settings.png) |

[![Watch the full ExitEcho demo](assets/demo-cover.png)](assets/demo.mp4)

The demo uses real ExitEcho windows with illustrative process data.

## Installation and use

Download `ExitEcho-v0.1.0-win-x64.zip` from [Releases](https://github.com/Feel11ng/ExitEcho/releases), extract it, and run `ExitEcho.exe`. The portable build needs no installer or administrator rights.

ExitEcho starts in the system tray and monitors in the background. Open it from the tray icon. When it finds related processes after an app closes, choose **Details** to inspect them, **Ignore** to suppress future alerts for that app, or **End leftovers** to terminate the listed processes after confirmation. Monitoring can be paused from the tray or main window. Settings let you adjust the notification delay (3–60 seconds; eight by default), theme, language, notifications, and optional Windows startup.

The CLI is built separately from source:

```text
exitecho watch
exitecho run "<path-to-exe>" [args]
```

## Languages

The UI supports 27 languages: English, Russian, German, French, Spanish, Brazilian and European Portuguese, Italian, Polish, Ukrainian, Turkish, Dutch, Czech, Swedish, Norwegian, Danish, Finnish, Simplified and Traditional Chinese, Japanese, Korean, Arabic, Hebrew, Hindi, Indonesian, Vietnamese, and Thai. Select a language in Settings or use the Windows system language. The CLI is in English.

## Privacy

All settings and history stay local. No account, telemetry, or network requests.

## Limitations

ExitEcho tracks apps it observes with visible top-level windows. Inaccessible, short-lived, or detached processes may be missed. A process left running does not necessarily indicate a problem; some apps intentionally continue in the background. Ending processes can interrupt their work, so ExitEcho never does it automatically.

## Build from source

On Windows 10/11 x64 with the .NET 8 SDK:

```powershell
dotnet build ExitEcho.sln -c Release
dotnet publish ExitEcho.App/ExitEcho.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

See the [changelog](CHANGELOG.md), [third-party notices](THIRD_PARTY_NOTICES.md), and [MIT license](LICENSE). Created by **Feel11ng**.
