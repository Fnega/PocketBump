## Context

`MatchDirector.FixedUpdate` returns immediately on `MatchOver`, while `Update` continues polling `PumpNet` and its five-second peer timeout. This stops both input transmission and `RollbackSession.Advance`, which currently owns historical reconciliation and checksum validation. `MatchSim.Step` itself does not increment the tick in `MatchOver`, so merely removing the director's early return is not a safe fix. The normal sender also selects `Sim.Tick - 2` for checksums, omitting the last simulated tick when progression stops.

`GameSession.Rematch` reloads the local Arena. Destruction of its director sends Goodbye and disposes the socket. Result buttons use this path for local and LAN play alike; Modify Rules currently changes the play kind to Local. Existing loopback smoke success proves moving peers and checksum exchange, not a naturally completed match or repeated rematches.

The user selected option 1: rematches preserve the negotiated rules; changing rules leaves the connection and requires a new room from the main menu. Local play keeps its existing navigation. This change must preserve the existing frozen-Arena/top-right error behavior and the corrected physics in release 0.6. It must not synchronize unrelated unfinished changes or retune scoring, shots, or stealing.

## Goals / Non-Goals

**Goals:**

- Establish an input-complete, corrected, mutually acknowledged result without treating healthy results as a timeout.
- Start one fresh match only after both players explicitly consent, with the same seats and negotiated rules.
- Recover bounded control-packet loss, duplication, and reordering; reject traffic from an old connection or match generation.
- Provide clear result/waiting/exit UI in authored and generated scenes, with unchanged local navigation.
- Produce repeatable unit, socket, scene, and packaged-player evidence for completion, repeated rematches, and leave/rejoin.

**Non-Goals:**

- In-session rule editing, a new lobby, reconnect/resync, host migration, internet services, or anti-cheat authentication.
- Changing gameplay physics, score attribution, win-by-two rules, fixed input delay, the normal rollback window, or normal match input-hole policy.
- New art, audio, balance changes, automatic menu return after a network fault, or claims that loopback tests prove cross-machine reliability.

## Decisions

### 1. Separate connection lifecycle from simulation phase

Introduce a plain, clock-injected lifecycle coordinator, integrated by `MatchDirector`. It owns control state, retries, results, and rematch consent; `MatchSimState.Phase` continues owning game rules. Poll, send control messages, and reconcile queued input from `Update` using unscaled/realtime time. Only the existing fixed-tick path advances playable simulation.

```text
Playing -> SettlingResult -> ResultsReady -> WaitingForPeerChoice
    ^            |                                |
    +------------+  (late input removes finish)    v
                                      PreparingNextEpoch -> StartupBarrier -> Playing

Any connected phase -> existing frozen error panel on genuine network failure
Explicit local leave -> dispose connection -> main menu
```

`SettlingResult` shows a neutral confirmation message, not an authoritative winner. `ResultsReady` enables Rematch. Waiting for a human choice has no short timeout while validated peer traffic continues. Normal results do not set `Time.timeScale` to zero or reuse the network-error panel.

Alternative rejected: disable timeouts whenever `MatchOver` is visible. It hides real departures and does not repair final-state reconciliation or rematch coordination.

### 2. Reconcile the terminal history without inventing new ticks

Extract the existing restore/replay/confirmed-checksum work into a reconciliation entry point that can run without sampling new local input or executing a new tick. `Advance` and terminal processing share it. Repeated calls with no new input are idempotent. When replay reaches an earlier terminal state, stop recording at the actual last executed tick and invalidate superseded snapshots/events; do not stamp multiple no-op steps as different ticks. If replay removes the predicted finish, restore the corrected playable phase, clear the provisional result UI, and resume normal advancement.

Record `finishTick = Sim.Tick - 1`, final scores, winner, settings hash, and the corrected final checksum. A finish is eligible to advertise only when required remote input is contiguous through that tick and reconciliation has completed. A peer's input `AckTick` alone is not proof of a matching result. Exchange retried Finish and FinishAck messages carrying the same terminal tuple. Enable local result actions only after validating the peer's matching final tuple and acknowledgment of the local tuple. Continue answering duplicates while the peer catches up. A comparison must refer to corresponding corrected ticks; do not report DESYNC merely because a still-playing peer has not reached the other peer's claimed finish yet.

While settling, resend immutable stored input covering the peer's missing suffix, in history blocks bounded by the existing packet limit. Cover the entire retained recoverable suffix rather than only the last eight samples. This matters because the rollback window is twelve ticks. Do not overwrite queued input for a tick by resampling it. Continue normal checksum validation, and publish the exact final checksum rather than the usual `Tick - 2` preference.

Use two bounded terminal deadlines: unresolved required input fails as `INPUT HOLE` after five realtime seconds spent settling; once locally input-complete, lack of terminal agreement fails as `RESULT CONFIRMATION TIMEOUT` after fifteen seconds. A corrected confirmed mismatch still fails as `DESYNC`. Control traffic does not renew these progress deadlines. Once both results are agreed, stop the terminal deadlines and keep the result connection alive indefinitely while healthy.

Alternative rejected: keep calling `Advance` on `MatchOver`. The simulation does not advance its tick there and repeated sampling would overwrite tick-indexed history.

### 3. Scope all control and input traffic to one connection and one epoch

Advance `NetplayProtocol.Version` from 5 to 6. Preserve enough legacy handshake decoding to reject version 5 and version 4 explicitly through `SETTINGS MISMATCH` before gameplay; do not make old peers merely wait for a malformed-packet timeout. Keep the corrected collision/shot rules and existing seat/input-delay semantics. Because legacy hosts require the old exact Join length, start a version-6 join with a retried legacy-length compatibility probe advertising version 6. An old host can decode and reject it with its existing version-mismatch response. A version-6 host treats that same-version probe only as a capability query, sends a version-6 capability response, and does not allocate a playable session or bind its participant from the probe. The joiner then performs the nonce-bearing version-6 Join exchange. A legacy acknowledgement can trigger rejection, never complete a version-6 handshake. Both stages share the bounded handshake deadline, and both host/join version directions must be tested.

Use the existing nonzero negotiated `MatchId` as the connection identity and add a monotonically increasing round epoch, initially zero. Every established-session packet, including input, readiness, terminal messages, heartbeat, and Goodbye, must identify its epoch. Each rematch advances the epoch exactly once; do not wrap it into a reused identity. A fresh host/join connection negotiates a fresh identity. Add a stable nonce per join attempt, echoed by JoinAck and bound to that attempt, so an old acknowledgement cannot initialize a new join attempt. Retries retain the original nonce.

Validate endpoint, version/layout, identity, epoch, seat, bounded fields, and allowed phase before state mutation or liveness updates. The initial host must not bind a peer from an unvalidated out-of-phase packet. While a rematch transition is active, accept only its explicitly retained old-to-new transition tuple; arbitrary future epochs cannot start a match. Old gameplay and Goodbye packets never apply to a new epoch. Duplicate old transition requests can receive the cached response while convergence is pending, but cannot reset the game again or extend current-epoch liveness indefinitely. Retire the previous transition after acknowledged new-epoch progress.

Validate received input acknowledgements monotonically and against locally sent samples before using them to retire history or release startup gates. This is not a substitute for final checksum agreement.

Alternative rejected: reuse only the endpoint or reset tick numbers under the same match identity. Delayed old packets would then become valid new-match traffic.

### 4. Keep liveness and progress separate

Send a small current-epoch heartbeat approximately every 0.5 realtime seconds while connected, including results and transition phases. Valid input or phase-valid control traffic also establishes liveness. Drain queued validated traffic before deciding that the peer has timed out, avoiding a frame-order false timeout. Preserve the five-second disconnect threshold.

Heartbeat receipt never advances input acknowledgement, supplies a missing input, confirms a result, or counts as rematch progress. Keep independent timestamps for last accepted peer traffic, result/input progress, and rematch transition progress. Waiting for the other player's choice is not a protocol stall. Once both choices have been received and an offer begins, enforce a fifteen-second negotiation/start deadline; expiry reports `REMATCH TIMEOUT` through the same error panel. Receipt of unrelated heartbeats must not extend that deadline.

### 5. Negotiate rematch on the existing socket and reset once

Each confirmed-result player can latch one consent for the current epoch. Consent is automatically retransmitted until an explicit acknowledgement or the valid rematch offer confirms that it was received; retrying does not require a second click. Repeated button presses retain the same intent. The host is the only proposer of the next epoch, and only after both consents. Negotiate an offer containing the current/next epoch, confirmed terminal tuple, and unchanged settings hash, joiner Prepared, host Commit, joiner CommitAck, then Start. These are named phases of one idempotent transaction, retried using injected realtime timers. Retain the committed tuple and responses until the other peer has demonstrated new-epoch progress.

Commit prepares a fresh `MatchSimState` and `RollbackSession` at tick zero without playable advancement; do not mutate the old snapshot into a new match. Preserve socket, endpoints, seat, and rules. Reset score, transient input edges, rollback/checksum rings, per-match diagnostics, interpolation, result/error overlays, banners, and tick-tagged FX exactly once. Retain a bounded completed-match summary separately from reset counters. LAN rematch must not reload the Arena or send Goodbye. Local rematch retains its existing behavior.

Start requires a committed target epoch. A valid target-epoch host input can serve as an implicit Start only for a joiner already prepared and committed to that exact transition. It must not authorize an arbitrary future epoch.

Add a startup barrier that exchanges and retains initial real input before unrestricted simulation advancement. Initial sampling is performed once per addressed tick; only the existing two delay ticks are implicit neutral input. Each committed peer can execute one safe initial tick to produce its first real delayed sample, then send that sample and its acknowledgement through `Update` while otherwise waiting. Once the first exchange is acknowledged, allow a small bounded next step on each side so both can demonstrate progress beyond the first waiting point without depending on the other already leaving the barrier. A duplicate of the first sample is not progress. Keep outstanding startup samples recoverable and block further advancement before either the retained history or rollback budget could be exhausted. Release the barrier only after validated two-way new-epoch input/ack progress beyond that initial point establishes that both peers are active; a first-sample acknowledgement alone cannot remove gap protection. Continue answering pending Start/Commit retries during that convergence. This prevents the existing 0.25-second control retry interval (15 simulation ticks) from outrunning the twelve-tick rollback window when a final control packet is lost. A lost Start, CommitAck, first input, or first acknowledgement must recover without a duplicate reset or a negotiation-induced `INPUT HOLE`; prolonged failure hits the bounded rematch deadline. After startup convergence, normal gameplay input-hole rules are unchanged.

Alternatives rejected: independent scene reloads, a single unreliable restart packet, or advancing ordinary gameplay immediately after sending the final control message. None proves that the remote peer is ready or keeps the first input suffix recoverable.

### 6. Apply the user's navigation policy without a hidden mode change

Local results keep Rematch, Modify Rules, and Main Menu. LAN results show Rematch, a clear same-rules notice, and Main Menu; the existing Modify Rules control is disabled or replaced with a non-actionable instruction to leave and create a new host room. Do not invoke the local Modify Rules handler from LAN results. The chosen text must explain that changing rules requires returning to the main menu and using Host LAN again; neither peer is silently moved into local play.

Rematch status distinguishes confirming result, waiting for the other player's choice, and starting the next match. Clicking Main Menu remains available while waiting. It closes the connection once and restores normal time on the initiating peer. A valid Goodbye freezes the remaining peer with `PEER LEFT` and manual Main Menu; if Goodbye is lost or the process vanishes, the existing `PEER TIMEOUT` path is the truthful fallback. Do not infer successful Goodbye delivery from a local send call.

Use one mode-aware button-binding/presentation path for authored Arena UI, runtime fallback, and repaired legacy UI. No broad art rebake is needed. After both peers leave, a fresh Host LAN/Join LAN must work with new identity, cleared terminal flags, and no leaked bound socket.

### 7. Verify lifecycle behavior without overstating gameplay coverage

Keep the existing default movement smoke test and its pass conditions. Add explicit scenarios:

- `lifecycle`: a development-only, mutually selected initial fixture can place both simulations in the same legitimate near-finish state before rollback baselines are created; normal simulation produces the finish. Apply this fixture only to epoch zero of the first connection in the scenario and record `finish_source=seeded_fixture` for that epoch. Every rematch must use the production zero-score, tick-zero initial state, pass the startup barrier, and record real input/checksum progress. Use the same normal-input driver as `full-match` to complete the subsequent matches needed for at least three rematches; reaching Live with confirmed progress after the third rematch is sufficient. Record the actual finish source per epoch. Confirm results, hold at least eight realtime seconds, test unilateral waiting, and exercise host-led and joiner-led leave/rejoin in separate runs. The fresh connection after leaving must also use normal production initialization, not repeat the seed.
- `full-match`: use ordinary starting rules/state and packed local inputs through the real UDP/rollback path. Require actual collection, shot, collision, score, and match completion; no debug score/position injection or forced phase transition. Failure to finish by the explicit deadline is FAIL, not a switch to the seeded fixture. Continue through a confirmed result and a rematch into live play.
- Fault profiles: selectively lose terminal and rematch phase messages, duplicate/reorder them, and delay old-epoch traffic into the new match. Retain the existing initial-handshake fault profile. Classify expected departures only in the designated leave stage; an unexpected `PEER LEFT`, timeout, or desync still fails the run.

Extend diagnostics with scenario/stage, connection identity/epoch, terminal tuple, hold duration, consent/transition state, heartbeat/control counters, successful rematches, and expected/observed departure. Count actual gameplay events only after their ticks are confirmed, or retract/deduplicate provisional events by epoch and tick before committing totals. A press of Shoot is not evidence that a marble fired. Keep bounded per-epoch summaries so resetting the new match does not erase previous-stage evidence.

The launcher waits for final whole-scenario reports from both peers, not a first-stage PASS. Retain both logs, stage reports, effective scenario deadlines, and launcher status. Do not terminate both processes just because the first match completed. Preserve Windows PowerShell compatibility, paths with spaces/non-ASCII characters, reserved-variable safety, hidden helper processes, and cleanup limited to processes launched by that invocation.

## Risks / Trade-offs

- [Risk] Prediction produces a finish that later disappears. → Do not publish a confirmed winner or accept consent before reconciliation and terminal agreement; test corrected terminal-tick changes as well as a return to live play.
- [Risk] Control liveness hides lost gameplay input or stalled negotiation. → Track independent progress deadlines and keep normal input-hole detection unchanged.
- [Risk] Final rematch control loss leaves one peer advancing alone. → Stage initialization, retain initial real inputs, and enforce a bounded startup barrier rather than widening the normal rollback window.
- [Risk] A valid old Goodbye terminates the next game. → Validate connection identity and epoch before interpreting message meaning; replay old traffic in tests.
- [Trade-off] No online rule editing. → Clearly explain the leave-and-rehost path selected by the user; avoid a new lobby or rule-negotiation UI.
- [Risk] Scripted full-match play cannot reach the win condition reliably. → Develop and verify a deterministic input strategy/fixture-independent controller before claiming packaged coverage; keep the lifecycle fixture separately labeled and report blockers honestly.
- [Risk] Existing scoring main specs lag the separate unfinished `award-pocket-goals-to-opponent` change. → Preserve actual current scoring and its regression tests; do not fold scoring/spec synchronization into this lifecycle change.
- [Risk] Same-machine checks miss cross-hardware/network behavior. → Publish the exact scope of automated evidence and a separate two-computer checklist; never mark manual LAN testing as executed without evidence.

## Migration Plan

1. Run baseline EditMode/PlayMode suites and retain current source/build evidence; add failing regressions for the present lifecycle defects before implementing fixes.
2. Implement and validate protocol 6, terminal reconciliation, lifecycle/UI, then diagnostic scenarios. Update version-specific tests without weakening physics or pause assertions.
3. Build into the next absent sequential directory under `D:\uniry Assets\射球大战`. The latest inspected release is `PocketBump0.6`, so the expected next directory is `PocketBump0.7`; recheck at implementation time and never overwrite an existing release.
4. Run both packaged peers from the complete same-version folder. Retain normal smoke, control-fault, lifecycle, and full-match reports with test/build logs. Provide a two-computer test checklist and state whether it was actually run.
5. If release validation fails, keep the previous complete release available for both players; do not mix old and new protocol versions or undo unrelated workspace changes.

## Open Questions

No product decision remains blocking this plan. Option 1 (same-rules rematch; leave and rehost to edit rules) is explicitly confirmed. Exact new private type names, packet byte layouts, and input-controller details are implementation choices constrained by the behaviors and tests above. If implementation requires a new gameplay rule, in-session editing, or reconnect behavior, stop and ask before expanding scope.
