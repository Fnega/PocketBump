## 1. Capture Baseline and Failing Regressions

- [x] 1.1 Record the existing dirty-worktree scope and run the full PocketBump EditMode and PlayMode baseline suites, retaining logs/results without resetting unrelated work or synchronizing the unfinished scoring change.
- [x] 1.2 Add focused failing regressions for terminal receive/reconcile after MatchOver, healthy result-screen dwell beyond five seconds, and LAN Rematch incorrectly reloading/disconnecting; distinguish deterministic/unit and scene evidence from a real packaged connection.

## 2. Version and Validate Session Traffic

- [x] 2.1 Define protocol-6 lifecycle message codecs and connection/epoch fields for established traffic, with round-trip, malformed-length, wrong-seat, wrong-endpoint, retired-epoch, future-epoch, and acknowledgement-bound tests.
- [x] 2.2 Implement the legacy-length compatibility probe and nonce-bound version-6 handshake; verify v6/v6 retry success and v6 host/join rejection of both v5 and v4 without accepting legacy packets as a new playable session.
- [x] 2.3 Add fresh connection identity and monotonic epoch ownership, idempotent transition tuple validation, and stale nonce/Ready/Input/Finish/heartbeat/Goodbye rejection; preserve only the responses needed for an in-flight transition.

## 3. Confirm Results and Keep Healthy Connections Alive

- [x] 3.1 Extract no-new-tick rollback reconciliation/checksum validation shared with Advance, covering idempotent polling, earlier terminal ticks, invalidated later records, corrected winners, and a predicted finish that returns to live play.
- [x] 3.2 Retransmit immutable recoverable terminal input across the entire retained suffix in bounded history blocks, validate received acknowledgements, and expose the exact corrected finishTick checksum without resampling stopped ticks.
- [x] 3.3 Implement clock-injected lifecycle result settlement with Finish/FinishAck retries, matching terminal tuple checks, provisional versus confirmed result state, five-second missing-input deadline, and fifteen-second result-agreement deadline.
- [x] 3.4 Add realtime validated heartbeat/control pumping and drain-before-timeout behavior independent of FixedUpdate; verify healthy result dwell, true peer silence, invalid-packet liveness rejection, and heartbeat traffic that cannot conceal an input hole or renew progress deadlines.

## 4. Implement Bilateral Rematch and Safe Startup

- [x] 4.1 Implement per-epoch consent with automatic retry/acknowledgement, indefinite healthy waiting for the other player's choice, repeated-click idempotence, and host-only offer creation after both consents.
- [x] 4.2 Implement offer/preparation/commit/ack/start negotiation bound to the final result, next epoch, and unchanged rule hash; preserve duplicate responses through convergence and enforce the fifteen-second rematch/start deadline.
- [x] 4.3 Add the bounded startup-input barrier with sample-once initial input, realtime history/ack retries, safe progress beyond the first waiting point, and delayed release of gap protection; prove loss of Start, CommitAck, first input, or first ack does not exhaust history or produce a negotiation-induced InputHole.
- [x] 4.4 Initialize each committed rematch exactly once using fresh MatchSimState/RollbackSession and zero scores while retaining socket, endpoints, seats, and rules; reset sampler edges, snapshots, checksums, interpolation, FX, HUD, and per-match diagnostics without sending Goodbye or losing completed-epoch evidence.

## 5. Integrate Result Navigation and Explicit Departure

- [x] 5.1 Route authored/generated/repaired result UI through the same mode-aware actions: confirming result, confirmed winner, waiting for peer consent, starting rematch, and Main Menu; disable actionable LAN Modify Rules and explain the user's leave-and-rehost policy while preserving local navigation.
- [x] 5.2 Centralize explicit leave/disposal so Main Menu closes the connection once, signals current-session departure, restores time, and clears startup/terminal flags; keep remote PEER LEFT or genuine timeout in the frozen-Arena panel and support a subsequent fresh host/join.
- [x] 5.3 Extend PlayMode coverage for local result actions, both LAN seat perspectives, predicted-result withdrawal, duplicate button binding, new lifecycle timeout reasons, and menu/time recovery; do not claim injected presentation tests reproduce underlying socket faults.

## 6. Extend Development Diagnostics

- [x] 6.1 Add bounded lifecycle/epoch diagnostic summaries and fields for stage, terminal tuple, liveness, result dwell, rematch count, and expected/actual departure; implement confirmed or rollback-reconciled event accounting so input presses and replay duplicates do not masquerade as actual gameplay milestones.
- [x] 6.2 Develop and verify a normal-initial-state packed-input full-match driver that actually collects, fires, collides, scores, reaches a legal winner, and rematches into live play; report missing milestones/timeouts as FAIL without score edits or forced finish fallback.
- [x] 6.3 Using the normal-input driver from 6.2, add a lifecycle scenario seeded only before the first connection's epoch-zero baseline, with eight-second result holds, unilateral waiting, three rematches, and host-led/joiner-led leave-and-fresh-join variants; require normal zero-score initialization thereafter, input-driven later finishes, per-epoch finish-source labels, and Live/confirmed progress after the third rematch.
- [x] 6.4 Add reproducible targeted terminal/rematch control loss, duplicate/reorder, startup input/ack loss, and stale-epoch injection profiles; keep the existing handshake-drop profile and distinguish recoverable faults from explicit negative tests.
- [x] 6.5 Extend Run-LanDiagnostics.ps1 with scenario/profile selection, stage-aware deadlines and whole-scenario final reports; retain default invocation behavior, full logs and failure evidence, and cleanup restricted to its own hidden launched processes, including startup/error paths.

## 7. Verify and Hand Off the Release

- [x] 7.1 Run the full EditMode suite and real two-socket lifecycle/fault tests, including each dropped control phase, terminal correction, old traffic rejection, bounded startup, repeated rematch, genuine failures, and bidirectional version mismatch; retain green evidence without weakening existing physics/rollback assertions.
- [x] 7.2 Run the full PlayMode suite and verify authored/fallback UI parity, the preserved local flow, LAN wait/result/error states, and manual menu recovery; retain results and relevant logs/screenshots.
- [x] 7.3 Strictly validate this OpenSpec change and check its implementation against all three delta specs, leaving unrelated active changes and main-spec synchronization untouched.
- [x] 7.4 Build a Windows Development player into the next absent sequential PocketBump version directory under D:\uniry Assets\射球大战 (expected PocketBump0.7; recheck first), preserve prior releases, and record build outcome plus runtime artifact identity.
- [x] 7.5 Run the new packaged pair through default smoke, existing handshake loss, both lifecycle leave-role variants, targeted recoverable control faults, and normal-gameplay full-match/rematch; require both final reports and retain per-stage evidence, including expected negative-test reasons.
- [x] 7.6 Record verification results, exact repeat commands, new-version distribution requirements, and known limits in the change handoff; provide a two-computer LAN checklist and explicitly state whether real two-computer testing was performed rather than inferring it from loopback success.
