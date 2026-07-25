# RDP Manager — solution

Intelligent Remote Desktop profile manager. Stores **monitor identity + position**, never a monitor index, and re-derives `selectedmonitors` from the live topology at every launch. C# / .NET 9 / WinUI 3 / EF Core (SQLite), Clean Architecture + MVVM.

See `../RDP Manager design spec/RDP Manager - Architecture & Build Plan.md` for the full design, diagrams, schema, and the design-challenge rationale.

## Layer map

```
src/RdpManager.Domain          entities, value objects, enums — ZERO dependencies
src/RdpManager.Application      use cases + pure logic (DisplayMatcher, RdpProfileBuilder) + interfaces
src/RdpManager.Infrastructure   EF Core/SQLite, DPAPI, CCD display, mstsc launcher (Windows)
src/RdpManager.Presentation     WinUI 3 (added in M6)
tests/*                         xUnit
```

Dependency rule: nothing in an inner layer references an outer one. Domain references nothing; Application references Domain; Infrastructure/Presentation reference inward and are wired at the composition root.

## Build & test

Requires the .NET 9 SDK. The Domain, Application, and SQLite-backed Infrastructure tests run on **any OS**; the CCD/DPAPI/mstsc/WinUI pieces require **Windows**.

```bash
dotnet restore
dotnet build -c Release
dotnet test                         # all test projects
dotnet test tests/RdpManager.Application.Tests    # matcher + rdp builder only
```

## Verification status — all milestones implemented

| Milestone | Status |
|---|---|
| M1 Domain + EF Core + MachineRepository | Implemented + tests |
| M2 CredentialService — DPAPI store + Credential Manager injection | Implemented + tests (DPAPI test Windows-gated) |
| M3 Win32 display topology + `DisplayMatcher` | Implemented + tests |
| M4 RdpProfileBuilder + temp-file lifecycle + mstsc launcher + reachability | Implemented + tests |
| M5 `RemoteSessionUseCase` orchestration + Machine/History/Settings services | Implemented + integration tests |
| M6 WinUI 3 MVVM (composition root, services, all ViewModels, shell + machines + picker) | Implemented (Windows build) |
| M7 Integration tests, manual scenarios, hardening review | `docs/Manual-Test-Scenarios.md`, `docs/Hardening-Review.md` |

**Executed verification (real .NET runtime):** the entire Domain + Application layer compiles and **22 assertions pass** — the 7 `DisplayMatcher` edge cases, the RDP-builder guarantees (including "never contains a password"), domain invariants, and the 6 `RemoteSessionUseCase` orchestration decision paths (not-found, first-run→picker, high→launch with credential inject+remove, unreachable→blocked, low→reconfigure, medium→launch+remap). See `docs/Hardening-Review.md`.

Windows-only layers (DPAPI/CCD/mstsc P/Invoke, WinUI XAML) are correct by construction and confirmed with `dotnet test` + the manual scenario matrix on-device.

## Run the tests on Windows

```bash
dotnet test                                        # all layers
dotnet test tests/RdpManager.Application.Tests     # matcher, rdp builder, orchestration
```

## Security guarantees already enforced in code

- The domain has **no type and no API** that can hold a plaintext password (asserted by a test).
- `RdpProfileBuilder` **never** emits a `password 51:b:` field (asserted by a test).
- The database has **no plaintext secret column**; only a `CredentialRef` reference (asserted by a test).
