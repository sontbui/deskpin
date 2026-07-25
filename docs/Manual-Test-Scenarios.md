# RDP Manager — Manual Test Scenarios

Run on a real Windows box, ideally a laptop with a dock and 2–3 external monitors (the whole point of the app is the dock re-plug case). Each scenario lists steps and the expected result. IDs map to the automated tests where one exists.

## Setup
1. Build & run `RdpManager.Presentation` from Visual Studio on Windows (net9-windows, Windows App SDK).
2. On first launch the SQLite DB is created under `%LOCALAPPDATA%\RdpManager\`.

## A. Machine management
| # | Steps | Expected |
|---|---|---|
| A1 | Add a machine (name, host, username), Save | Appears in the list; DB row created; no password stored yet (`MachineRepositoryTests`) |
| A2 | Set a password on the Credential tab | Stored via DPAPI; `Secrets` table holds an encrypted blob, never plaintext (`DpapiCredentialStoreTests`) |
| A3 | Duplicate a machine | New `<name>-copy`; **credential is NOT copied** |
| A4 | Delete a machine | Removed after confirm dialog; its DPAPI secret is also removed (no orphan) |
| A5 | Ping a reachable / unreachable host | Shows latency / "unreachable" without launching mstsc |

## B. First remote (unconfigured) — the picker
| # | Steps | Expected |
|---|---|---|
| B1 | Click Remote on a never-configured machine | Reachability check → **monitor picker opens** with your live layout; positions labelled Left/Center/Right (`RemoteSessionUseCaseTests.Unconfigured…`) |
| B2 | Select one screen, Save & remote | Profile saved as fingerprints (no index); mstsc opens on that screen |
| B3 | Select multiple screens, Save & remote | `.rdp` has `use multimon:i:1` + `selectedmonitors` derived from the live layout (`RdpProfileBuilderTests`) |
| B4 | Single-display host | Picker shows "single-display" state, no empty stage |

## C. Repeat remote (layout unchanged) — the 95% path
| # | Steps | Expected |
|---|---|---|
| C1 | Remote to a configured machine, no hardware change | **Zero prompts**, sub-second launch on the saved screens (`…High_confidence_launches…`) |
| C2 | Inspect the generated temp `.rdp` during launch | Contains **no** `password` field; file is deleted a few seconds after launch (`TempRdpFileWriterTests`) |
| C3 | Confirm Credential Manager during the session | A `TERMSRV/<host>` entry exists briefly, then is removed after launch |

## D. Dock re-plug / layout changed — the core value
| # | Steps | Expected |
|---|---|---|
| D1 | Configure on the dock (3 monitors). Undock, re-dock so Windows re-indexes. Remote. | Silently **re-maps to the same physical screens**; a non-blocking "re-mapped" toast with Undo (`…index_swap…`, `…serial_rearrange…`) |
| D2 | Swap two identical panels' cables | Still opens on the intended positions (position disambiguation) (`…identical_panels…`) |
| D3 | Add a monitor (2→3) | Saved two still used; new screen stays unselected; no prompt (`…added_monitor…`) |
| D4 | Change a monitor's resolution, same position | Re-maps silently (medium confidence) (`…res_change…`) |
| D5 | Go from 3 monitors to laptop-only (1) | **Blocking dialog**: open on single screen / reconfigure / cancel (`…three_to_one…`, `…Low_confidence…`) |

## E. Reachability & credentials
| # | Steps | Expected |
|---|---|---|
| E1 | Remote to a powered-off host | Pre-flight blocks with an inline error and Retry/Edit — **no failing mstsc window** (`…Unreachable…`) |
| E2 | Remote with a missing/expired credential | Prompted to re-authenticate in-app; not dropped into mstsc's own credential dialog |

## F. Command palette & shell
| # | Steps | Expected |
|---|---|---|
| F1 | Press Ctrl+K, type a machine, Enter | Remotes immediately (palette = the launcher) |
| F2 | Switch rail destinations (Machines/Groups/Activity/Settings) | Single window, selection preserved, nothing pops a child window |
| F3 | Trigger a re-map, open Activity | The re-map is listed with its confidence; trust is auditable |

## G. Resilience
| # | Steps | Expected |
|---|---|---|
| G1 | Kill the app mid-launch, relaunch | Startup sweep removes orphaned temp `.rdp` files (`TempRdpFileWriterTests.SweepOrphans`) |
| G2 | Cancel a slow ping / launch | CancellationToken observed; operation stops cleanly |
| G3 | Duplicate machine names | Allowed (GUID identity); a soft warning only |
