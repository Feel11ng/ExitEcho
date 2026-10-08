# Code signing policy

## Current status

ExitEcho is preparing an application to SignPath Foundation. Approval has not been granted, and **all current releases are unsigned**. This policy describes a proposed process; it does not claim that ExitEcho has received a certificate or that any published binary has a SignPath signature.

If the project is approved, the planned attribution is:

> Free code signing provided by SignPath.io, certificate by SignPath Foundation

The certificate would be issued to SignPath Foundation, not to ExitEcho or an individual maintainer. This attribution will apply only to releases actually signed through the approved SignPath process. Until then, release and download pages must describe artifacts as unsigned.

## Project roles

- **Committer:** [Feel11ng](https://github.com/Feel11ng), the current project maintainer, may commit changes to the source repository.
- **Reviewer:** [Feel11ng](https://github.com/Feel11ng) reviews proposed changes from contributors without commit access before they are merged, with particular attention to build scripts and release workflows.
- **Approver:** [Feel11ng](https://github.com/Feel11ng) is designated to manually approve each future SignPath signing request after checking the source revision, build provenance, and release artifacts.

These are ExitEcho's project responsibilities. SignPath account permissions and signing access will be configured only if the application is approved. No other person is assigned any of these roles by this policy. If the team changes, update this page before granting repository or signing access.

## Signing and release controls

- Sign only ExitEcho artifacts built from this repository's own source and build scripts through a verifiable release workflow. Do not submit locally modified binaries or third-party projects for signing.
- A committer prepares the release source revision and artifacts. A reviewer checks changes submitted by anyone without commit access before merge.
- The approver checks the exact release revision, build workflow, artifact identity, and release contents, then **manually approves each signing request**. Signing requests must never be approved automatically.
- Verify the signature and timestamp on each final signed artifact before publication, and identify signed and unsigned downloads accurately on release pages. Current unsigned releases remain unsigned.

These controls follow the [SignPath Foundation conditions for open-source projects](https://signpath.org/terms.html).

## Multi-factor authentication

Every project member with repository or SignPath access must enable multi-factor authentication for **both GitHub and SignPath** before receiving access. Do not share accounts or authentication factors. If an account or factor is compromised, suspend that member's signing and repository access until the incident is resolved.

## Privacy policy

ExitEcho monitors local process and window information to show possible leftover processes after an app closes. Settings, ignored-app rules, and exit history are stored only on the user's computer in `%LOCALAPPDATA%\ExitEcho` (`settings.json`, `ignored.json`, and `history.json`). Exit history may include application names, executable paths, detection times, process counts, and memory usage. ExitEcho has no account, telemetry, analytics, or automatic network requests. **ExitEcho does not transfer this information to other networked systems unless the user explicitly requests it.**

The portable app does not install a system service. Optional **Start with Windows** creates an `ExitEcho` value under `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`; disabling the option removes that value. ExitEcho does not change other system settings without an explicit user action. Third-party components used by the app are documented in [third-party notices](../THIRD_PARTY_NOTICES.md); no third-party network service is required at runtime.

To remove the portable app and, optionally, its local data, follow [Uninstall the portable app](../README.md#uninstall-the-portable-app).
