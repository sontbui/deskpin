# Deskpin — Release Notes

All notable changes to Deskpin are documented here. The version numbers follow
[Semantic Versioning](https://semver.org): `MAJOR.MINOR.PATCH`.

The CI release workflow reads the section matching the pushed tag and uses it as the GitHub Release body.

## [1.5.1] - 2026-09-23

### Added
- **Find a file in the pane you are looking at.** Both panes gained a filter box: it narrows the current listing as you type, matches anywhere in the name, ignores case, and clears itself when you navigate elsewhere. Refresh, rename and delete keep the filter. No extra SFTP round trip - it only filters rows already listed.
- **Sortable columns.** NAME, SIZE and MODIFIED are now buttons: click to sort, click again to reverse, and an arrow marks the active column. Folders and files rank together; the column you clicked decides every row's position, with name as the tie-breaker.
- **A "No matches" state.** An empty pane now distinguishes an empty folder from a filter that matched nothing, and offers a Clear filter button.

### Changed
- **A machine that will not open now says so, and offers the fix.** Instead of only a line in the pane, a dialog names the failure - sign-in details missing, sign-in rejected, host unreachable - and carries the matching action: "Edit machine" or "Try again". The pane still keeps the reason after the dialog is dismissed.

### Fixed
- **Selecting a machine with a bad password no longer stalls the app.** A rejected sign-in is an everyday outcome of clicking a machine, not a fault, so connecting now returns it as a result. The connect path raises no exception at all, which also stops a debugger breaking on every failed selection.
- **Network failures were filed as unexpected errors.** A switched-off machine, a closed port and a stopped SSH service all mapped to "unexpected", making a routine condition indistinguishable from a defect. They are now reported as unreachable.
- **The helpful sign-in message was being thrown away.** The failure carries "check <user>'s password in Edit"; the error mapping replaced it with a generic "Access to the remote home directory was denied" before it reached the screen.
- **A rejected sign-in now logs the server's own reason**, which separates a wrong password from a server that refuses password authentication entirely - previously both surfaced identically.
- **The revealed password is now cleared** when the machine selection changes while a connection is still being established.

### Security
- **SSH.NET 2023.0.1 to 2026.0.0.** The old build carries the Terrapin prefix-truncation vulnerability (CVE-2023-48795, GHSA-mggc-4xg6-vcxf) in the SSH transport that every file transfer runs over.

## [1.3.2] - 2026-08-12

### Changed
- **New icon now used in the left nav rail too.** The "Machines & Files" rail item carried the old monitor glyph; it's now the Aperture-Pin mark, matching the title bar and app icon. It follows the nav's selected/rest colors instead of a hard-coded accent. Completes the icon rollout started in 1.3.1.

## [1.3.1] - 2026-08-12

### Changed
- **New app icon — "Aperture Pin".** A fresh, distinctive mark (a diamond aperture with a centered pin) on a blurple squircle, redrawn crisp at every size from 16 to 256px. It replaces the old monitor glyph across the taskbar, window, installer shortcuts and the in-app title bar.

## [1.3.0] - 2026-08-12

### Changed
- **File transfers now run over SFTP (SSH), WinSCP-style.** Deskpin connects as the machine's own user and lands in the server-reported home directory — replacing the SMB admin-share bridge. No admin rights, C$, `LocalAccountTokenFilterPolicy`, drive-redirection dependency, or profile-folder guessing. The remote pane uses POSIX paths; per-machine SFTP port (default 22) on Add/Edit. Windows targets need OpenSSH Server enabled once (the right-click "Enable file access" gives the command). Passwords are revealed from DPAPI only to open the SSH session.

## [1.2.3] - 2026-08-12

### Fixed
- **Remote pane opens the real profile folder.** It no longer assumes C:\Users\{login}; it verifies the folder over SMB, matches renamed profiles (name.DOMAIN), and falls back to C:\Users when the name differs — instead of a not-found error.

## [1.2.2] - 2026-08-12

### Fixed
- **User-profile folder no longer shows as empty.** Listings filtered out System-flagged entries, which on Windows includes the profile's own special folders (Desktop, Documents, Downloads…). Only Hidden items are skipped now, matching Explorer's default; drive-root junk ($Recycle.Bin, System Volume Information) stays hidden.

## [1.2.1] - 2026-08-10

### Changed
- **Machines & Files — one merged destination.** The Machines list and the Files console now share a single screen: a 232px machine master (search, status dot, live latency, right-click actions) drives the dual-pane commander and transfer dock on the right. The header toolbar carries Ping / Edit / Display / Drive redirection and the primary Remote action for the selected machine. The separate Machines and Files rail items are gone.
- **Nocturne design system.** All colors, type, radii and control styles now come from `Themes/Nocturne.xaml` (single blurple accent `#9184D9`, outlined primary buttons, compact density, semantic success/warning/error tokens). The caption bar carries the RDP Manager monitor mark.

### Fixed
- **Remote browsing actually reaches the remote machine.** The remote pane previously looked for `\\tsclient\C`, which only exists *inside* an RDP session (it's how the remote sees your local drives) — hence "C:\ was not found". Remote drive paths now map to the host's SMB admin share (`C:\Users` → `\\host\C$\Users`), with a clear message when the share is unreachable.

### Added
- **Per-machine operating system** (Windows / Linux / macOS) on the Add/Edit dialog. Remote is disabled for macOS (no RDP server); Linux still launches RDP (xrdp).
- **Automatic SMB sign-in.** Selecting a machine establishes an authenticated SMB session to the host using the machine's DPAPI-stored credential (revealed only transiently for the WNetAddConnection2 call), so the C$ bridge works without a manual `net use`. Unreachable-share errors now show the full explanation in the pane.
- **Remote pane lands in the user's home**: `C:\Users\{user}` on Windows (via C$), the Samba home-share (`\\host\{user}`) on Linux/macOS.
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
