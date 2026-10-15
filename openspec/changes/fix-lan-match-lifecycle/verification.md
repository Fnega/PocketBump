# Implementation verification

## Delivery summary

- Accepted Windows Development build: `D:\uniry Assets\射球大战\PocketBump0.9` (protocol 6). Distribute the entire folder to both players, not only the executable.
- Final source suites: **218/218 EditMode + 15/15 PlayMode**. Final packaged build: **six paired acceptance scenarios passed**, including an intentional `PEER TIMEOUT` negative case; detailed evidence and the separately versioned 0.8 extended fault matrix are below.
- Same-rules bilateral rematch, confirmed results, validated heartbeats, safe initial/rematch startup, explicit departure/fresh join, and accurate staged diagnostics are implemented. Fixed-step catch-up now drains queued input before checking the existing hole boundary.
- **Real two-computer LAN testing has not been performed.** Loopback evidence does not establish arbitrary Wi-Fi reliability or cross-hardware determinism. Genuine input loss beyond the unchanged rollback window still pauses; automatic reconnect/resynchronization is not included.
- Original releases, failed-candidate reports, unrelated worktree changes and main specs are preserved. This change has not been archived.

## Baseline — 2026-09-30

- Evidence directory: `Tools/LanLifecycleResults/20260930-161733`.
- Unity 2022.3.62f3c1, hidden batch mode. No interactive Unity or packaged game process was running at the start.
- Full baseline suites: EditMode 144/144 passed; PlayMode 10/10 passed; both processes exited 0. Source was unchanged during both runs.
- Existing worktree changes include scenes, art/presentation, physics/netplay, menus/settings, existing tests, IDE/project settings, tools, main specs, earlier archives, and other active changes. They are preserved. No reset or unrelated scoring-spec synchronization is authorized.
- This change implements same-rules LAN rematch. Rule edits require leaving and hosting a fresh room; no automatic reconnect or host migration.

## Regression and implementation verification

- Added initial regressions for stopped-tick resampling, final-checksum reconciliation without a new tick, and the LAN rematch scene-reload guard. The navigation regression is not evidence of a completed socket game.
- `red-editmode.xml`: 2/2 expected failures; `red-playmode.xml`: 1/1 expected failure. Both test processes exited 2 before any runtime implementation changes.
- First compile checkpoint found the new PlayMode text assertions needed the explicit `Unity.TextMeshPro` test assembly reference. Added that reference; no runtime/physics assertion was weakened.
- `green2-editmode.xml`: 210/210 passed, exit 0. Includes real two-socket tests for lost handshake/lifecycle phases, startup samples/acks, duplicate/reordered packets, stale traffic, and three rematches, plus ordinary packed-input matches with 0/4/8-tick delayed delivery.
- `green2-playmode.xml`: 13/13 passed, exit 0. Preserves all ten baseline tests, adds LAN navigation guards and authored/generated result UI, and extends frozen-error reasons. UI state/clock injections prove presentation, not a genuine socket failure.
- Terminal-history correction tests explicitly use synthetic corrected snapshots to isolate early-finish invalidation, winner correction, and return-to-Live reconciliation. Natural scored-state freeze exceeds the rollback window; these fixtures do not claim ordinary gameplay triggered those rare history shapes.
- Source additionally retains bounded completed-epoch evidence across rematch and clears session references on explicit leave; subsequent regression/build results are recorded below.
- These initial unit/scene results preceded packaged acceptance. No real two-computer LAN test has been executed.

## Release audit hardening

- The first final regression/build pipeline was deliberately canceled after its EditMode pass and before any release folder was created, to address cross-review findings. Its partial PlayMode run is not counted as a pass.
- Epoch-zero startup now uses the same two-sample input/ack barrier as rematch. This prevents a joiner whose last handshake confirmation was lost from running 15 ticks ahead of a host still retrying at 0.25 seconds. Initial barrier progress has a separate 15-second `LAN HANDSHAKE TIMEOUT`; heartbeats cannot extend it. Handshake duplicate replies remain available until this initial barrier converges.
- Input codec rejects reserved sample bits before they can refresh liveness. Negative `peer-silence` reports require deliberate activation and actual post-activation drop evidence, not any incidental timeout.
- Diagnostic coordination files schedule the scripted players' result dwell and button choices; they never carry simulation state, final-result agreement, or rematch protocol messages. Both players must finish their eight-second hold before testing unilateral consent.
- After the first failure, the launcher allows a bounded interval for the other process's final report, then records any missing report explicitly. It only closes processes started by that invocation.

## First candidate regression and build — 0.7

- `release-EditMode.xml`: **217/217 passed**, exit 0. The seven added audit regressions cover reserved input bits, initial handshake/gameplay overlap (reproducing the old failure and proving the gated path), initial startup deadline, and active-game heartbeat/input-hole separation.
- `release-PlayMode.xml`: **13/13 passed**, exit 0.
- Coordinator real-socket tests use explicitly synthetic prior-match terminal tuples for rapid protocol testing. The normal-game controller's 0/4/8-tick delivery tests use in-memory delivery. Packaged-player results below are separate evidence.
- Windows x64 Development build: **Succeeded, 0 errors, 0 warnings** in `build-pocketbump0.7.log`.
- New output: `D:\uniry Assets\射球大战\PocketBump0.7`. The directory did not exist before building; 0.6 and earlier releases were preserved.
- SHA256 `PhysicscollGame.exe`: `FA01CCDBAA5F74C777609235B99BA8988285B2BF0754445E85BBA268B2E61EB7`.
- SHA256 `PhysicscollGame_Data\Managed\PocketBump.Runtime.dll`: `356F8550E076AC11DE9DBEE082E050BFB1D5BF68F3F0EB335CE5EAB18AAA8F0F`.
- Protocol version: **6**. Both players must use the complete same new build folder. Versions 5 and 4 are deliberately rejected; replacing only the executable is not an upgrade.
- Strict OpenSpec validation passed; focused diff checks passed. No other change was archived or synchronized and no main spec was modified by this implementation.

## First candidate packaged verification — 0.7

- Default smoke: both peers PASS, launcher exit 0, results `Tools/LanDiagnosticResults/20260930-165320-939-6d5494`.
- Handshake/control first-packet loss: both peers PASS, launcher exit 0, results `Tools/LanDiagnosticResults/20260930-165345-877-d6c35e`.
- First normal full-game run **FAILED** on both peers with `INPUT HOLE`: `Tools/LanDiagnosticResults/20260930-165410-358-5c3d2c`. Both had confirmed four collections, one actual shot, one dangerous collision and three score events; no injected faults or Socket send errors. Host tick 1635 / last remote 1622 / packet age 0.227s; join tick 1626 / last remote 1613 / age 1.184s. This is not a terminal/rematch failure and is not counted as a pass.
- Investigation: `FixedUpdate` catch-up can reach the 12-tick hole boundary before the frame's `Update` drains queued UDP. The real-queued-input regression and narrow receive-order correction are recorded below; the normal rollback window and genuine missing-input failure policy remain unchanged.
- **0.7 is a retained failed-validation candidate, not the accepted release.** Corrected builds use new sequential directories, preserving 0.7's failure evidence and prior releases.
- The later full-game, lifecycle and fault results below supersede this candidate's failed acceptance; no cross-machine LAN testing has been performed.

## Queued-input correction

- `queued-input-red.xml`: 1 expected failure / 1 pass, process exit 2 before the fix. The positive test confirmed data was physically queued in a real UDP socket, then the director incorrectly froze on its next fixed step. The negative test contained only a heartbeat and correctly retained `INPUT HOLE`.
- The director now drains its receive queue before **each** fixed simulation step as well as during rendered updates; after the pump it rechecks failure/handshake/state before advancing. Timeouts are still evaluated only after draining. No rollback-size, gameplay, scoring or input-hole threshold was changed.
- Corrected final suites: `release08-EditMode.xml` **217/217 passed** and `release08-PlayMode.xml` **15/15 passed**, both exit 0. Both queued-input regression cases are green.
- Corrected Windows Development build: **Succeeded, 0 errors, 0 warnings**, `build-pocketbump0.8.log`, output `D:\uniry Assets\射球大战\PocketBump0.8`. Neither 0.7 nor earlier folders were overwritten.
- 0.8 executable SHA256 remains `FA01CCDBAA5F74C777609235B99BA8988285B2BF0754445E85BBA268B2E61EB7` (Unity native launcher); the changed runtime assembly identity is `5BF3088724108932B89F04C801265D083E6994F016D4CBFEB2865EBCFAAA83E1` at `PhysicscollGame_Data\Managed\PocketBump.Runtime.dll`.

## Corrected 0.8 packaged acceptance

| Scenario / profile | Both players / launcher | Evidence under `Tools/LanDiagnosticResults` |
| --- | --- | --- |
| Ordinary full match + rematch, no faults | PASS / exit 0 | `20260930-170423-422-6e1ee6` |
| Default movement smoke | PASS / exit 0 | `20260930-170745-968-34558f` |
| Initial handshake/control first-packet loss | PASS / exit 0 | `20260930-170839-990-aec984` |
| Three rematches; drop Start; host leaves and fresh host/join | PASS / exit 0 | `20260930-170924-107-0ae8ad` |
| Three rematches; stale-epoch injection; joiner leaves and fresh host/join | PASS / exit 0 | `20260930-171250-121-7ac808` |
| Full match; first FinishAck lost | PASS / exit 0 | `20260930-171608-891-2f0f83` |
| Full match; first CommitAck lost | PASS / exit 0 | `20260930-171727-244-e20237` |
| Full match; first startup input lost | PASS / exit 0 | `20260930-171845-741-cca17e` |
| Full match; first startup acknowledgement lost | PASS / exit 0 | `20260930-172004-212-b19829` |
| Full match; duplicated/reordered lifecycle traffic | PASS / exit 0 | `20260930-172122-562-cdc800` |
| Deliberate silence during seeded results (negative case) | Expected PEER TIMEOUT / exit 0 | `20260930-172241-095-42ed9c` |

The normal full-match run recorded identical epoch-zero terminal tick **3318**, checksum **-218701961**, legal **8:0** result, **8 collections / 1 actual shot / 1 dangerous collision / 8 score events**, and **8.002s** result holds on both peers. Both then reached epoch 1, Live tick 301, with 300+ validated checksums and no terminal error. The earlier failed 0.7 run remains recorded above; this successful rerun does not erase its evidence.

All eleven 0.8 packaged runs in this table passed their declared conditions on both peers and the launcher; the negative case deliberately ended in `PEER TIMEOUT`. Per-case launcher stdout/stderr is saved as `package08-*.log/.err` in the main evidence directory (the first ordinary full-match run is recorded directly in its report directory).

The host-led lifecycle run completed all three rematches while dropping three Start messages. Its first, explicitly seeded epoch finished at tick 60 / checksum -2020856943; subsequent normal-input epochs 1 and 2 both finished at tick 3318 / checksum -218701961, each 8:0. Every finished epoch held results for at least eight seconds. After the third rematch reached Live, host observed `LOCAL LEAVE`, join observed `PEER LEFT`, and both joined a fresh connection (ID 426617180), epoch 0, ordinary zero-score initialization and Live progress.

The joiner-led lifecycle run also completed three rematches and fresh connection after injecting four retired-epoch datagrams per peer (input, heartbeat, finish and Goodbye). Host observed `PEER LEFT`; join observed `LOCAL LEAVE`. Both final reports passed; no old Goodbye terminated the new epoch.

## Final report-accuracy correction

The 0.8 negative case explicitly activated silence and actually suppressed 229/228 outgoing datagrams, producing the correct timeout on both peers. It also exposed a report-only defect: the top-level source was correctly `seeded_fixture`, but stopping around six seconds (before the normal eight-second summary enrichment) left a default `normal_gameplay` source and zero event counters in its basic completed-epoch summary. Those original reports remain unchanged and **must not be treated as ordinary-full-match or eight-second-hold evidence**.

The final correction enriches the current confirmed epoch from its actual tuple, source, reconciled event ledger and observed hold duration when exporting a report, including an early negative-case report. It does not change protocol, simulation, timers, or pause/rematch behavior. The launcher now also requires the current connection/epoch summary to match the top-level tuple, source, actual score count and observed hold; the deliberately early negative case is not required to last eight seconds. The stricter validator correctly rejected the unmodified old negative report.

## Final candidate regression and build — 0.9

- `release09-EditMode.xml`: **218/218 passed**, exit 0, including the early-negative-report provenance/event/short-hold regression.
- `release09-PlayMode.xml`: **15/15 passed**, exit 0, including queued-real-input-before-fixed-step and heartbeat-only genuine-hole coverage.
- Windows x64 Development build: **Succeeded, 0 errors, 0 warnings**, recorded in `build-pocketbump0.9.log`.
- New directory: `D:\uniry Assets\射球大战\PocketBump0.9`. Existing 0.8, failed-validation 0.7, and all earlier folders are preserved.
- SHA256 `PhysicscollGame_Data\Managed\PocketBump.Runtime.dll`: `1794ECC4E8E8536AADE948C85D3C5AF219ECA2F641EAE8D14BFE254396819A63`.
- The broader 0.8 fault matrix remains explicitly versioned evidence above; the final 0.9 packaged acceptance is recorded separately below, not inferred from that older build.

## Final 0.9 packaged acceptance

| Scenario / profile | Both players / launcher | Evidence under `Tools/LanDiagnosticResults` |
| --- | --- | --- |
| Deliberate peer silence, strict report-provenance validation | Expected PEER TIMEOUT / exit 0 | `20260930-173121-733-534b5d` |
| Default movement smoke | PASS / exit 0 | `20260930-173139-231-fa42e4` |
| Initial handshake/control first-packet loss | PASS / exit 0 | `20260930-173203-366-65e2e0` |
| Ordinary full match; first startup acknowledgement lost; rematch to Live | PASS / exit 0 | `20260930-173228-664-08b0c0` |
| Three rematches; drop Start; host leaves and fresh host/join | PASS / exit 0 | `20260930-173347-450-33871f` |
| Three rematches; stale-epoch injection; joiner leaves and fresh host/join | PASS / exit 0 | `20260930-173625-673-2f8496` |

The final negative case now correctly labels both top-level and completed-epoch source as `seeded_fixture`, with the same tick 60 / checksum -2020856943 / 8:0 tuple. Its actual event ledger records one score and zero collection/shot/dangerous-collision events; the observed holds are 6.003s / 6.001s, not eight seconds. Silence was explicitly activated and suppressed 229/228 datagrams; both peers froze with `PEER TIMEOUT`. This is timeout/report evidence, not ordinary-full-game evidence. Handshake-loss injected three/four real drops on host/join respectively.

The final ordinary full-match test recorded matching tick **3318**, checksum **-218701961**, **8:0**, and **8 collections / 1 actual shot / 1 dangerous collision / 8 score events** on both peers. Both held results for **8.002s**, then recovered from one deliberately dropped startup acknowledgement per peer and reached rematch epoch 1, Live tick 301. The completed-epoch summary records the finished game; the top-level score/finish fields correctly refer to the newly started match.

The final host-led lifecycle test completed three rematches despite three dropped Start packets, with source sequence `seeded_fixture` → `normal_gameplay` → `normal_gameplay`. The seeded tuple was tick 60 / checksum -2020856943; both later games reached tick 3318 / checksum -218701961 with the same ordinary-input event counts as the full-match case above. All three results were held for at least eight seconds. After the third rematch reached Live, `LOCAL LEAVE` / `PEER LEFT` preceded a fresh ordinary connection and Live progress.

The final joiner-led lifecycle test also completed three rematches, retaining the same fixture/ordinary-game provenance and event counts, with result holds of 8.002s / 8.000s / 8.000s. It injected four retired-epoch datagrams per peer (input, heartbeat, finish and Goodbye) without corrupting the new epoch or falsely departing. Join observed `LOCAL LEAVE`, host observed `PEER LEFT`, and fresh connection ID changed from 1674912312 to -104666460; both reached Live tick 301. The host-led variant changed ID from -541654753 to -1716494623. Fresh-connection reject counters are not evidence counts for packets injected on the retired connection.

All six final 0.9 paired runs passed their declared conditions, each with two final reports and launcher exit 0. Per-case launcher stdout/stderr is retained as `package09-*.log/.err` in the main evidence directory; all six stderr files are empty. No launched player process or UDP 7777 listener remained after cleanup. The final source and launcher were frozen throughout these six runs.

## Repeat commands

Run from any PowerShell working directory. Each invocation owns a separate timestamped report folder under the project's `Tools\LanDiagnosticResults`; full player logs, `.stages` trails and launcher summary stay there, including failures.

```powershell
$playerPath = 'D:\uniry Assets\射球大战\PocketBump0.9\PhysicscollGame.exe'
$diagnosticScript = 'D:\Unity project\PhysicscollGame\Tools\Run-LanDiagnostics.ps1'

# Default moving-player smoke; same default behavior as before.
powershell -ExecutionPolicy Bypass -File $diagnosticScript -PlayerPath $playerPath

# Lose initial handshake/control packets.
powershell -ExecutionPolicy Bypass -File $diagnosticScript -PlayerPath $playerPath -DropFirstControlPacket

# Normal starting state, actual collect/fire/collision/score/win, hold, rematch to Live.
powershell -ExecutionPolicy Bypass -File $diagnosticScript -PlayerPath $playerPath -Scenario full-match

# First epoch only is seeded; later finishes use ordinary input, three rematches, leave/rejoin.
powershell -ExecutionPolicy Bypass -File $diagnosticScript -PlayerPath $playerPath -Scenario lifecycle -LeaveRole host
powershell -ExecutionPolicy Bypass -File $diagnosticScript -PlayerPath $playerPath -Scenario lifecycle -LeaveRole join

# Example targeted loss and explicit negative test.
powershell -ExecutionPolicy Bypass -File $diagnosticScript -PlayerPath $playerPath -Scenario full-match -FaultProfile drop-start
powershell -ExecutionPolicy Bypass -File $diagnosticScript -PlayerPath $playerPath -Scenario lifecycle -FaultProfile peer-silence
```

Supported recoverable lifecycle profiles: `drop-finish`, `drop-finish-ack`, `drop-consent`, `drop-consent-ack`, `drop-offer`, `drop-prepared`, `drop-commit`, `drop-commit-ack`, `drop-start`, `drop-startup-input`, `drop-startup-ack`, `duplicate-reorder`, `stale-epoch`. A selected fault must actually be injected to count as tested. The deliberately negative `peer-silence` profile succeeds only when deliberate silence produces the expected `PEER TIMEOUT`; this does not mean gameplay continued.

The default whole-scenario allowance for lifecycle/full-match is 600 seconds (`-ScenarioTimeoutSeconds`), with independent stage deadlines printed in `launcher-summary.txt`. A missing event, timeout, missing report, inconsistent terminal tuple, or unexpected network pause is FAIL. The normal-game driver earns points through cooperative side-pocket dunks after demonstrating a real shot and dangerous impact; it is not evidence of shot-based goals or competitive AI quality.

## Two-computer LAN checklist — not yet executed

1. Copy the complete accepted version folder to both computers; do not mix protocol-5/4 builds or copy only the `.exe`. Use a trusted private LAN and allow the game through the private-network firewall; do not disable the firewall globally.
2. On one computer select Host LAN. On the other enter the host's active LAN IPv4 address and use matching rules. Avoid selecting a VPN/virtual-adapter address by mistake.
3. Play an ordinary match with collecting, shooting, collisions and scoring. Keep both result screens open for more than eight seconds; neither should pause.
4. Select Rematch on only one side and wait at least ten seconds. It must wait. Then consent on the other side: both must keep seats/rules, reset scores, and start the next match. Repeat for three rematches.
5. Test both host-led and joiner-led Main Menu exits. The remaining peer should display `PEER LEFT` if Goodbye arrives, or `PEER TIMEOUT` if it does not; it must stay in the frozen Arena until manually leaving. Host and join again from the menus.
6. To change rules, both leave; host a fresh room with the new settings. LAN Modify Rules must not silently change either side to local mode.
7. If a network problem occurs, record both screens' exact reason, the build folder/version, and which action was in progress. Preserve the relevant diagnostic reports and player logs before rerunning.

Loopback success cannot prove router/Wi-Fi behavior, firewall configuration, cross-hardware floating-point agreement, or tolerance of arbitrary stalls. Genuine missing input beyond the unchanged 12-tick rollback window still pauses; this change does not provide reconnect, state resynchronization, or host migration.
