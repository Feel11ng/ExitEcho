# Changelog

## v0.4.0

- Moved the app, Core, CLI, and test projects to .NET 10 and unified their version metadata.
- Added OLED Black, Graphite, and Midnight alongside the existing system, light, and dark theme choices.
- Smoothed interruptible Pause/Resume, status, notification, and section transitions, with Reduced Motion support.
- Added a compact recent-activity card and truthful event status steps in History and Details.
- Fixed MainWindow spacing and ignored-app expansion so content does not overlap the Pause control.
- Strengthened CI with functional ignore/legacy JSON tests, portable publish, and version checks.

## v0.3.0

- Deduplicated notifications for the same app and updated open notifications with fresh process data.
- Limited visible notifications to three, queued additional notifications, and smoothly repositioned cards when space opens.
- Added Reduced Motion support and refined native UI animations.
- Fixed opening History and cleaned up the Main, Notification, Details, and Settings UI.

## v0.2.0

- Added app-scoped process ignore rules alongside existing whole-app ignore rules.
- Added per-process Ignore actions in Details and management of both rule types under Ignored apps.
- Remaining processes stay visible after a process is ignored; End leftovers acts only on visible processes.
- Preserved existing ignored-app settings when loading older `ignored.json` files.
- Clarified when an app-name-only rule is less precise because its executable path is unavailable.

## v0.1.0

- Passive monitoring of visible Windows apps and related processes, with a configurable delay after the last window closes
- Leftover notifications with local app icons, process names, PIDs, and RAM details
- Optional manual termination with confirmation and a completion result
- Local exit history (up to 200 events) and a per-app ignore list
- Settings for language, theme, Windows startup, notifications, and notification delay
- Light and dark themes with the updated ExitEcho interface and logo
- UI localization in 27 languages, including Arabic and Hebrew right-to-left layouts
- System tray controls, pause and resume, and single-instance protection
- CLI `run` and `watch` modes
