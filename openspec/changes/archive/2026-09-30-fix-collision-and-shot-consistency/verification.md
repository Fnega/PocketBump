# Implementation verification

## Baseline — 2026-09-30

- Evidence directory: `Tools/PhysicsConsistencyResults/20260930-151024/`.
- Unity: 2022.3.62f3c1, Windows, batch mode. No interactive Unity or game process was running when verification began.
- Before source edits: EditMode **64/64 passed**, PlayMode **9/9 passed**, both Unity processes exited 0. See `baseline-editmode.xml` / `.log` and `baseline-playmode.xml` / `.log`.
- Existing worktree was already dirty: both scenes, art/presentation, simulation/netplay, game/session/menu/HUD scripts, tests, project settings, `.gitignore`, IDE state, main specs, unarchived changes, and earlier archived-change moves. New art/diagnostic/build/test files and generated test scenes were also present. These are retained; no reset, cleanup, or unrelated spec synchronization is authorized by this change.
- Planned runtime edit scope: the circle-contact helper in `MatchSim.cs`, the marble speed-limit constant in `ArenaLayout.cs`, and the protocol version in `LanUdpPeer.cs`. Add focused test files, narrowly update existing version-dependent tests, and document any necessary existing test-fixture adjustment.

## Red regression run

- `red-editmode.xml` / `.log`: 144 cases, 77 passed, 67 failed, Unity exit 2 (expected). Runtime collision and speed-limit fixes had not yet been applied; the independent protocol bump was already present.
- Failures were exclusively new coverage: 20 collision cases, 43 shot cases, 4 physics replay cases. Existing tests and the newly added compatibility cases passed.
- Examples: approaching relative normal velocity stayed +5 instead of rebounding to -4.5 at restitution 0.90; coincident positions moved about 1290 units instead of a radius-scale correction; high-power shots were clipped to 16 rather than the expected post-drag speed.

## Verification status

- `green-editmode.xml` / `.log`: **144/144 passed**, no skips, Unity exit 0. The 67 expected red failures now pass without weakening their assertions.
- Added coverage: 29 circle-contact cases, 43 real-tick shot/obstacle cases, 4 active-physics rollback combinations, and 4 compatibility cases. The original 64 cases also pass; no existing physics fixture needed adjustment.
- Runtime changes remain limited to the circle solver, speed-limit constant, and compatibility version. No mass, restitution, recoil, drag, cooldown, scoring, or stealing parameter was retuned.
- `green-playmode.xml` / `.log`: **10/10 passed**, no skips, Unity exit 0. All original 9 scene-flow tests pass, including dangerous knockback/dunk, collection/steal, coasting, and scoring.
- The added PlayMode case checks nine terminal reasons through the shared `Abort` path: freeze in Arena, unchanged simulation and rendered player position, top-right reason, original failure retention, and manual menu/clock recovery. It intentionally does not claim to induce each underlying socket fault.
- Physics replay fixtures all fired at speed 21.29032 after fire-tick drag and each produced one dangerous body contact. `(local seat, shooter)` combinations `(0,0)`, `(0,1)`, `(1,0)`, `(1,1)` required 2, 3, 3, and 2 rollbacks respectively; final confirmed tick 180 matched the baseline checksum and complete JSON state.

## Build

- Windows development build: `D:\uniry Assets\射球大战\PocketBump0.6\PhysicscollGame.exe`.
- Compatibility version: **5**. Both human players must use this version; protocol-4 builds are intentionally rejected during handshake.
- Prior releases through `PocketBump0.5` were present and preserved. The new output directory was verified absent before building, and the existing build helper refuses overwrites.
- `build-pocketbump0.6.log`: **Succeeded**, errors **0**, warnings **0**, Unity exit **0**.
- `PhysicscollGame_Data/Managed/PocketBump.Runtime.dll` SHA-256: `295D38E98E7127E12D926FA6CC6FE35C3E8626CA1E965FEEED7CCD26CB7831E3`.

## Packaged dual-player diagnostics

Both runs used the new `PocketBump0.6` executable on loopback, reported protocol 5, and exited successfully. Each directory contains `host.txt`, `join.txt`, both full player logs, and `launcher-summary.txt`.

| Run | Evidence directory | Host / join | Confirmed checksums host / join | Result |
| --- | --- | --- | --- | --- |
| Normal, 3,600 ticks | `Tools/LanDiagnosticResults/20260930-152803/` | Both reached 3,600 | 3,598 / 3,599 | Both PASS |
| Dropped first control packets, 900 ticks | `Tools/LanDiagnosticResults/20260930-152938/` | Both reached 900 | 898 / 899 | Both PASS |

- All four reports have an empty terminal reason, `send_failures=0`, and `desync_tick=-1`.
- The fault-injection run records `last_socket_error=FAULT_INJECTED_DROP` as expected: this is an intentional test drop, not an actual Socket send failure.
- These low-latency packaged runs needed no gameplay rollback. Delayed/ reordered input correction with actual shots and impacts is separately exercised by the four EditMode physics replay cases above, and the existing network stress tests also passed in the full suite.
- No unexpected exceptions were found in the packaged logs. Existing failure freeze and menu behavior remain unchanged and have dedicated scene-level regression coverage.

## Handoff and limits

- All 18 implementation tasks are complete. The change has not been archived.
- Give both players the complete `PocketBump0.6` folder, not just the executable; do not connect a 0.5 peer to 0.6. No saved-data migration is needed.
- Option A is implemented: initial shot speed remains 8–22, natural drag and 12–32 recoil remain unchanged, and the shared free-marble limit is now 22. Corrected circle restitution affects all three dynamic pair types without retuning their masses or coefficients.
- The tests establish correctness for the covered simulation cases and same-machine packaged LAN scenarios. They are not proof of all-network or cross-hardware reliability. A real two-computer LAN playtest with both players on 0.6 is still recommended.
- Reconnect/resynchronization and the match-over/rematch LAN lifecycle remain outside this change, as specified in the proposal.

To repeat the packaged checks:

```powershell
powershell -ExecutionPolicy Bypass -File "D:\Unity project\PhysicscollGame\Tools\Run-LanDiagnostics.ps1" -PlayerPath "D:\uniry Assets\射球大战\PocketBump0.6\PhysicscollGame.exe" -SimulationTicks 3600
powershell -ExecutionPolicy Bypass -File "D:\Unity project\PhysicscollGame\Tools\Run-LanDiagnostics.ps1" -PlayerPath "D:\uniry Assets\射球大战\PocketBump0.6\PhysicscollGame.exe" -SimulationTicks 900 -DropFirstControlPacket
```
