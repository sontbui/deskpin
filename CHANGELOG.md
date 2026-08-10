# Deskpin — Release Notes

All notable changes to Deskpin are documented here. The version numbers follow
[Semantic Versioning](https://semver.org): `MAJOR.MINOR.PATCH`.

The CI release workflow reads the section matching the pushed tag and uses it as the GitHub Release body.

## [1.2.0] - 2026-08-10

### Changed
- **Machines & Files — one merged destination.** The Machines list and the Files console now share a single screen: a 232px machine master (search, status dot, live latency, right-click actions) drives the dual-pane commander and transfer dock on the right. The header toolbar carries Ping / Edit / Display / Drive redirection and the primary Remote action for the selected machine. The separate Machines and Files rail items are gone.
- **Nocturne design system.** All colors, type, radii and control styles now come from `Themes/Nocturne.xaml` (single blurple accent `#9184D9`, outlined primary buttons, compact density, semantic success/warning/error tokens). The caption bar carries the RDP Manager monitor mark.

### Added
- **File logging.** ILogger output now lands on disk at `%LOCALAPPDATA%\RdpManager\logs\deskpin-yyyyMMdd.log` (Info and above, 14-day retention) — previously it only went to the invisible console/debugger. Transfer queue events (queued, completed/failed with reason, skipped, cancel, retry) and app startup are logged. "Open logs folder" button in the Info tab.
- **Report a bug (Info tab).** A small form that opens a pre-filled GitHub issue on the Deskpin repository (title, description, optional diagnostics: app version, OS, recent error-log tail). Token-free by design — the user submits from their own GitHub account in the browser.
- **Files — a dual-pane transfer console** (fifth rail destination beside Machines, Groups, Activity and Settings). Browse This PC and the remote host side by side over RDP drive redirection, with a breadcrumb + editable "go to directory" address bar (accepts `\` or `/`, validated before navigating), multi-select, drag-drop across the divider or center Send/Get arrows, and a right-click menu (Upload/Download, Rename, New folder, Properties, Delete).
- **Transfer dock** with Queue and History tabs: live per-file progress with throughput and ETA, cancel on active items, one-click Retry on failures, "Clear finished", and a receipt of every past transfer — including skipped ones.
- **Overwrite conflicts** are resolved per file (Replace / Keep both / Skip) with an "apply to all conflicts in this transfer" shortcut; Keep both writes `name (copy).ext`.
- **Drive redirection panel** governing which local drives the remote session can reach, persisted per machine and mapped to the `.rdp` `drivestoredirect:s:` field the profile builder emits (`*`, specific drives like `C:;D:;`, or `DynamicDrives`). Turning redirection off disables the Files console for that machine.
- Friendly pane states for empty folders and permission-denied directories (no crashes, a Back affordance instead).
- Architecture: new `TransferJob` value object + status state machine in Domain; `IFileTransferService`/`FileTransferUseCase`, `ILocalFileSystem`/`IRemoteFileSystem` abstractions and pure path validation in Application (fully unit-tested); `\\tsclient`-bridge filesystem, chunked streaming copies with progress/cancellation, and the drive-redirection settings store in Infrastructure. No plaintext credentials anywhere — the DPAPI/Credential Manager model is untouched.

## [1.0.0] - 2026-07-25

First release.

### Added
- **Position-based monitor matching.** Deskpin stores each monitor's hardware fingerprint + position, never its Windows index, so remote sessions still open on the right physical screens after a dock re-plug or re-index.
- **Machine management** — add, edit, duplicate, delete, ping; search by name/host/tag.
- **First-run monitor picker** — pick the screens a session opens on; you only do it once.
- **Smart re-map** — silent when confident, with a re-configure prompt when it can't map reliably.
- **Quick actions** — double-click a machine to remote; ⋯ menu for Remote / Configure display / Ping / Edit / Delete.
- **Active-session dot** — a green dot marks machines with a live remote session.
- **Security** — passwords are encrypted at rest with Windows DPAPI and delivered to mstsc via Credential Manager; the generated `.rdp` never contains a password.
- **Automatic update check** against GitHub Releases on startup.

### Known limitations
- EDID serial enrichment (to disambiguate two identical panels) is a fast-follow; matching currently uses device path + geometry.
- Tags/gateway/notes are not yet editable from the Edit dialog.
- Groups / Activity pages are scaffolds; their ViewModels exist.

<!--
## [1.1.0] - unreleased
### Added
- ...
### Fixed
- ...
-->
