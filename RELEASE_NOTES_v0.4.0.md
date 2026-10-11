# ExitEcho v0.4.0

ExitEcho v0.4.0 updates the Windows utility without changing how leftover processes are detected or ended.

## Changes since v0.3.0

- Migrated the app, Core, and CLI to .NET 10 and aligned their version numbers.
- Added OLED Black, Graphite, and Midnight, bringing the theme selector to six choices including System default.
- Smoothed Pause/Resume and other UI transitions, including rapid repeated actions and Reduced Motion behavior.
- Added a compact recent-activity view and event status steps in History and Details.
- Fixed MainWindow layout when Ignored apps expands, and refreshed the four README screenshots.
- Expanded CI checks for ignore rules, legacy JSON, portable publish, and assembly versions.

## Download and verification

The portable x64 archive is `ExitEcho-v0.4.0-win-x64.zip`. Extract it and run `ExitEcho.exe`; no installer is required.

SHA-256: `FB021AE9EEF71A1EBD43B7D079957B94723947272A5D109B997D31E803B1150D`

The EXE is **unsigned**. The SignPath Foundation application was not approved. This build was tested on Windows 11 x64; ExitEcho has **not** been tested on Windows 10. Microsoft's .NET 10 support for Windows 10 is limited to supported LTSC/Enterprise editions and does not by itself verify ExitEcho compatibility.

ExitEcho cannot determine whether background activity is intentional. It never ends processes automatically; **End leftovers** requires confirmation.
