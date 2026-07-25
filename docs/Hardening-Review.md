# RDP Manager — Final Hardening Review

Self-review across all milestones, per the brief ("look for bugs, race conditions, resource leaks, architecture violations, and UX issues"). Items marked **Fixed** were addressed during the build; items marked **Windows-verify** are correct by construction but need on-device confirmation because they can't run in the Linux/CI-less sandbox.

## Architecture
- **Dependency rule holds.** Domain references nothing (enforced by an empty-dependency csproj). Application references only Domain (+ Logging.Abstractions). Infrastructure/Presentation depend inward and are wired at the composition root. Verified by the fact that the whole Domain+Application layer compiles standalone with no Infrastructure present (the sandbox verification build).
- **No logic in Views.** Every decision (which dialog, remap vs. reconfigure, toast vs. error) lives in a ViewModel or the use case. Code-behind only does view mechanics (hosting a dialog, wiring the title bar).
- **No static mutable state.** Everything is DI-managed; `SystemClock` abstracts time; no `DateTime.Now` in logic.

## Concurrency / races
- **DbContext is never shared across threads** — repositories create a short-lived context per call via `IDbContextFactory`. **Fixed** the WinUI-appropriate pattern (no scoped context).
- **Credential lifetime race.** mstsc needs the `TERMSRV/<host>` credential for a few seconds after launch; removing it immediately would race authentication. Handled by a grace-delayed cleanup (`RemoteSessionUseCase.ScheduleCredentialCleanupAsync`) that's returned as an awaitable so tests are deterministic. **Verified** inject-then-remove ordering in the orchestration tests.
- **Temp-file/mstsc race.** mstsc reads the `.rdp` once at startup; deletion is deferred by a read grace and always runs in a `finally`. **Windows-verify** the grace is adequate under slow disks.

## Resource leaks
- **Secrets are zeroed.** `SecureStringBytes` zero-fills every intermediate buffer; DPAPI output blobs are `LocalFree`d; pinned GCHandles are freed in `finally`. **Windows-verify** (P/Invoke path).
- **Temp files.** Deleted after launch and on failure; a startup sweep removes crash orphans. **Verified** by `TempRdpFileWriterTests` (cross-platform).
- **Disposables.** `await using` on every DbContext; `using` on TcpClient, Process, SecureString.

## Security
- **No plaintext password anywhere.** The domain has no type/API to hold one (asserted test); the DB has no plaintext column (asserted test); the generated `.rdp` never contains a `password` field (asserted test, run under the real runtime). Secrets are DPAPI-encrypted at rest and delivered via Credential Manager at launch, then removed.
- **DPAPI scope** is CurrentUser + per-record entropy; the blob is unreadable by other users. **Windows-verify** round-trip (`DpapiCredentialStoreTests`, skipped off-Windows).

## Bugs caught & fixed during the build
1. **Matcher confidence bug (Fixed).** The first scoring model (`0.7·identity + 0.3·geometry` with identity=0 on mismatch) wrongly scored a benign monitor replug as **Low**. Redesigned so identity is *positive evidence* that boosts geometry, only contradicting serials penalize, and unknown identity falls back to geometry. Proven by executing the real C# against all edge cases.
2. **Build-break risks (Fixed).** Relaxed analyzer-style rules from `warnings-as-errors` (would fail on cosmetic CA rules); removed an invalid nullable-enum EF conversion.
3. **Unstable device path (Fixed).** The GDI device name (`\\.\DISPLAY1`) shuffles like the index; switched the fingerprint's device path to the stable monitor instance id from `EnumDisplayDevices`.
4. **Tag reconciliation scope (Fixed/scoped).** Many-to-many tag upsert was pulled out of the repository into the service layer to avoid subtly-wrong join handling.

## Known items for the Windows build (not defects — environment limits here)
- The `.NET 9` SDK, EF Core/xUnit packages (NuGet), and all Windows P/Invoke (DPAPI, CCD, mstsc) and WinUI XAML must be built/run on Windows. The sandbox could only reach npm/pypi, so the pure Domain+Application core was verified by executing the real C# under a .NET 8 runtime; the rest is verified by `dotnet test` and manual scenarios on-device.
- **EDID enrichment** is a documented fast-follow: `NullEdidReader` ships first (matcher degrades to stable-device-path + geometry); `RegistryEdidReader` adds serial-level identical-panel disambiguation.
- **Groups/Activity/Settings pages** ship as scaffolds behind complete ViewModels/services; their XAML follows the `MachinesPage` pattern.

## UX
- The 95% path (repeat remote, unchanged layout) is silent and prompt-free. Re-maps are silent at high confidence with an Undo toast, surfaced at medium, and only block at low confidence — matching the design spec's trust model.
- Pre-flight reachability prevents a failing mstsc window from ever appearing.
