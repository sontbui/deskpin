# Deskpin — Release Notes

All notable changes to Deskpin are documented here. The version numbers follow
[Semantic Versioning](https://semver.org): `MAJOR.MINOR.PATCH`.

The CI release workflow reads the section matching the pushed tag and uses it as the GitHub Release body.

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
