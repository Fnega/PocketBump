## MODIFIED Requirements

### Requirement: Development builds produce peer diagnostic reports

When launched in LAN diagnostic mode, a development build SHALL write a terminal report for its host or join role. The report SHALL state pass/fail, terminal reason, simulation tick, last accepted remote tick, last acknowledgement, packet receive/rejection counters, and a bounded recent-event trail. Lifecycle scenarios SHALL retain scenario/stage identity, connection identity and epoch, confirmed terminal tick/checksum/scores/winner, result-hold duration, rematch count, liveness/control counters, and expected versus observed departure. Completed-epoch evidence SHALL survive per-match counter resets.

Reports for the dash arena SHALL additionally identify the report schema, protocol version, immutable rules/profile and layout identity, agreed random seed or reproducible seed policy, supported and exercised movement/Dash action vocabulary, per-seat confirmed mechanic milestones, and actual fault-injection counts. Each epoch SHALL identify whether its initialization was ordinary production state or a labeled fixture. Old collection, shot-button, held Boost, or fall/dunk-score evidence SHALL NOT be relabeled as new collision-goal proof. PASS SHALL mean that every required stage and evidence requirement of the selected scenario passed, not merely that a tick threshold, one match, a requested input, or an expected error name was observed.

#### Scenario: Match starts successfully
- **WHEN** host and join smoke processes complete the default local LAN handshake and satisfy movement, live-tick, and confirmed-checksum thresholds
- **THEN** each writes a passing default-smoke report containing its role, profile/protocol identity, and network snapshot without claiming a completed natural match or all new mechanics

#### Scenario: Session aborts
- **WHEN** a diagnostic process observes an unexpected session error or its scenario deadline expires
- **THEN** it writes a failing report before exiting with the terminal reason, last reached stage, missing required milestones, and available network snapshot

#### Scenario: No handshake packet arrives
- **WHEN** a host or join diagnostic peer reaches its handshake deadline without receiving a datagram
- **THEN** its report distinguishes send attempts, send failures, local endpoint, remote endpoint, and the last socket error from receive counters

#### Scenario: Expected departure is observed
- **WHEN** a scenario has explicitly entered its planned leave stage and the remaining peer receives the departing peer's valid Goodbye
- **THEN** the report records `PEER LEFT` as the expected observation for that stage, still requires subsequent prescribed stages, and does not classify an earlier unexpected departure as success

#### Scenario: A first rematch does not finish a multi-match scenario
- **WHEN** a lifecycle scenario completes its first rematch but further rematches or rejoin stages are required
- **THEN** neither peer emits a final whole-scenario PASS yet and the completed epoch's evidence remains available

#### Scenario: An old fixture is not a new gameplay proof
- **WHEN** historical or newly rerun reports use a seeded terminal fixture or the removed collection/shooting rules
- **THEN** their labels and schema preserve that origin and they do not satisfy natural dash-arena gameplay acceptance

### Requirement: Local pair launcher preserves test evidence

The repository SHALL provide a Windows development helper that launches host and join standalone players on loopback, waits for their reports, and retains a timestamped result directory containing both reports and launcher status. It SHALL preserve the existing default smoke invocation and the documented meaning of existing fault profiles while supporting explicit dash-arena, lifecycle, natural full-match, and bounded mechanic scenarios with stage deadlines. It SHALL wait for both final whole-scenario reports rather than stopping players on an intermediate success marker. A failure or early exit by one process SHALL allow a bounded report-collection grace period for its peer; absent required final evidence SHALL remain a failure, not inferred success. Logs, invocation parameters, profile/layout identity, effective deadlines, actual injected faults, and launcher failures SHALL be retained on success or failure.

PowerShell launches SHALL use hidden process windows unless an explicitly selected interactive run requires otherwise. Cleanup SHALL affect only processes started and tracked by that invocation. Automated loopback evidence SHALL be labeled as same-machine two-process evidence, not a two-computer network validation. Verification builds SHALL use a new, previously absent `PocketBump<version>` directory under the approved build root without overwriting earlier releases; sequential dotted version components SHALL be treated as integers, so an unchanged latest `0.9` sequence advances to `0.10`, not a decimal-equivalent `0.1`.

#### Scenario: User runs a local diagnostic pair
- **WHEN** a developer supplies a Development build executable without selecting a new scenario
- **THEN** the helper starts host before join, preserves default movement-smoke behavior, and prints the created results directory after both roles finish or the helper deadline expires

#### Scenario: User runs a lifecycle scenario
- **WHEN** the developer selects lifecycle diagnostics
- **THEN** the helper retains both players until they complete or fail the whole scenario and then preserves both final reports where available, full logs, and launcher status

#### Scenario: Player startup fails
- **WHEN** the host or join player cannot become ready or exits before writing its report
- **THEN** the helper reports failure, retains logs and launcher-stage evidence, gives the other peer its bounded final-report opportunity, and cleans up only its own surviving launched process

#### Scenario: Existing versions are preserved
- **WHEN** verification is ready to create a new build and earlier `PocketBump0.9` and other version directories already exist
- **THEN** the selected next integer-component version is checked for absence before building and no existing version directory is overwritten or deleted

#### Scenario: Loopback success is reported with its limit
- **WHEN** both local standalone processes complete all selected stages
- **THEN** the report may claim same-machine UDP coverage but identifies physical two-computer validation as separate unless it was actually performed and recorded

## REMOVED Requirements

### Requirement: Full-match diagnostics prove actual gameplay completion

**Reason**: The effective lifecycle baseline added this requirement with mandatory collection and shot-release milestones. The redesign removes possession, independent shooting, and fall-based scoring, so retaining that block would require proof of obsolete gameplay alongside the new collision-goal game.

**Migration**: Preserve the older change and its evidence without editing or archiving it automatically. When specification synchronization is authorized, synchronize the older lifecycle delta before this redesign, then replace this requirement with `Collision-goal diagnostics prove natural dash arena completion` and `Confirmed mechanic diagnostics cover both dash arena seats`. Historical collection/shooting reports remain historical evidence only; the new natural driver and event schema SHALL not fabricate equivalent milestones.

## ADDED Requirements

### Requirement: Collision-goal diagnostics prove natural dash arena completion

The natural full-match scenario SHALL start both peers from ordinary production initialization with the same approved profile and seed policy. Its driver SHALL act through ordinary packed movement and `DashPressed` input, normal UDP transport, rollback, and gameplay simulation. It SHALL produce confirmed body-to-free-ball impacts, actual balls entering defended goals, opponent-of-pocket point attribution, a legal winner under the configured win-by-two rules, mutually confirmed terminal state, and a bilateral rematch that reaches Live with real new-epoch input/checksum progress. Both seats SHALL demonstrate actual eligible Dash execution and actual collision-goal participation in the natural gameplay evidence, not merely requested key presses.

The driver SHALL NOT alter scores, move authoritative bodies outside ordinary simulation, create collection or shot-release events, use player falls as scores, force MatchOver, call result UI as a substitute for agreement, or fall back to a near-finish fixture to obtain PASS. Goals and a legal full-match finish SHALL not be delegated solely to a bounded mechanic fixture. If the driver cannot finish or satisfy a required natural milestone within its documented bounded deadline, the scenario SHALL fail with the missing stage identified.

#### Scenario: A Dash request does not prove Dash execution
- **WHEN** the driver presses Dash while the player is stunned, fallen, or cooling down
- **THEN** that input does not satisfy the successful-Dash milestone unless confirmed simulation actually starts an eligible Dash

#### Scenario: Actual body contact produces a goal
- **WHEN** a controlled player makes a confirmed eligible impact on a free ball and ordinary simulation carries that ball into a defended goal
- **THEN** the ledger records the impact and actual goal with correct scorer attribution and no possession, shoot action, or score injection is required

#### Scenario: Falling does not manufacture progress
- **WHEN** the driver sends a player into a death zone
- **THEN** the fall may satisfy its separately identified fall milestone but neither score nor collision-goal count increases merely because the player fell

#### Scenario: Full match and rematch succeed
- **WHEN** ordinary input-driven collision-goal play reaches the required natural milestones, legal winner, matching final tuple, and a bilateral rematch into confirmed live play
- **THEN** each report identifies `finish_source=normal_gameplay`, retains the gameplay and terminal evidence, and permits natural full-match PASS

#### Scenario: Scripted gameplay fails to finish
- **WHEN** the scenario deadline arrives before the legal winner or another required natural milestone
- **THEN** the run fails and reports the missing evidence without changing the rules, seeding scores, or substituting a fixture

### Requirement: Confirmed mechanic diagnostics cover both dash arena seats

The new-game acceptance suite SHALL retain per-seat confirmed evidence of actual Dash execution, collision-goal participation, cylinder stun and recovery, green pickup and enhanced eligible impact, purple pickup and an observed immunity interaction, blue safe-teleport outcome, fall without score followed by safe respawn, qualifying wall break, and occupancy-delayed wall repair followed by restoration. It SHALL demonstrate both player and free-ball occupancy blocking a repair and the same per-seat rules when role assignments are exchanged. A report SHALL distinguish successful teleport from the bounded no-safe-destination outcome; both outcomes SHALL have deterministic automated coverage, and only actual relocation SHALL satisfy a successful-teleport milestone.

These milestones MAY be distributed across explicitly named bounded mechanic scenarios rather than forcing every mechanic into one natural match. Such scenarios SHALL still exercise their declared ordinary input/simulation/network path and SHALL label any deterministic pre-baseline fixture or selected seed. Fixtures SHALL be initialized consistently before rollback baseline construction and SHALL not mutate live authoritative state to manufacture an event. Each accepted milestone SHALL cite the confirmed epoch/tick, relevant seat/object, outcome, and profile identity. A confirmed event ledger SHALL reconcile invalidated predictions and deduplicate replayed events rather than counting input attempts, animation starts, or repeatedly executed ticks. Fixture evidence SHALL remain distinct from natural full-match and repeated-rematch evidence.

#### Scenario: A confirmed pickup and effect are separate evidence
- **WHEN** a player acquires green or purple but never performs the corresponding enhanced impact or immunity interaction
- **THEN** the pickup alone does not complete that buff's behavioral milestone

#### Scenario: Repair is delayed by a real occupant
- **WHEN** a broken wall becomes eligible after 600 advancing world ticks while a living player's full collider or a free ball occupies its restored footprint
- **THEN** the wall stays nonblocking and repair-pending until actual clearance, and the ledger records both the delay and subsequent one-time restoration

#### Scenario: Replay counts one corrected event
- **WHEN** rollback re-executes a Dash, pickup, impact, goal, fall, wall break, or repair tick
- **THEN** only events remaining in corrected confirmed history contribute to totals and each stable event identity is counted once

#### Scenario: A bounded fixture cannot replace a natural finish
- **WHEN** a mechanic scenario starts from a labeled deterministic obstacle or pickup fixture and passes
- **THEN** its report proves only that scenario's declared mechanics and the separate natural collision-goal full-match requirement remains required

#### Scenario: Both seats are exercised
- **WHEN** the new-game acceptance suite completes
- **THEN** retained evidence identifies successful mechanic coverage for seat 0 and seat 1 rather than inferring remote-seat correctness from one local player's animations

### Requirement: Dash arena verification retains lifecycle and fault acceptance

The dash-arena verification suite SHALL retain the effective lifecycle scenario: confirmed terminal agreement, a healthy result hold of at least eight realtime seconds for each completed epoch, unilateral consent without a start, at least three successive bilateral rematches on one connection, active new-epoch input and confirmed-checksum progress, and separate host-led and joiner-led leave/menu/fresh-join variants. Rematches and later fresh connections SHALL use normal production initialization, with required subsequent finishes reached through actual collision-goal play. A first-connection epoch-zero terminal fixture MAY remain for narrow lifecycle isolation, but SHALL be legal for the new rules and explicitly labeled; old falling/collection/shot fixtures SHALL NOT prove the redesigned game.

Existing documented handshake-loss, terminal-control-loss, rematch-control-loss, duplication/reordering, stale-epoch, and negative fault-profile meanings SHALL remain available rather than being silently weakened to accommodate the new driver. Acceptance SHALL cover the first Join/Ready confirmations, initial startup input/ack progress, Finish, FinishAck, consent, offer, prepared, commit, commit acknowledgement, Start, and initial new-epoch input. Stale old-epoch input, result, heartbeat, and Goodbye SHALL not affect the new game. Expected-failure PASS SHALL require a profile explicitly armed for that fault, evidence that the intended fault was actually injected, the expected reason in the expected phase, and every other required stage; an accidental unrelated error SHALL remain FAIL.

#### Scenario: Three rematches retain new gameplay state isolation
- **WHEN** both peers finish three successive bilateral rematches after healthy results and unilateral-consent checks
- **THEN** each next epoch starts once with the same seats/profile and fresh player, ball, buff, wall, input, and event state, prior-epoch evidence remains available, and the final epoch reaches confirmed Live progress

#### Scenario: Each role can leave and reconnect
- **WHEN** the host-led and joiner-led leave variants run with valid Goodbye delivery enabled
- **THEN** the other peer observes `PEER LEFT`, uses the actual Main Menu recovery action, and both establish a fresh nonce-bound connection without stale results, input, pause, or socket state

#### Scenario: Lost controls recover without a startup hole
- **WHEN** the selected recoverable profile drops an initial-handshake, finish, or rematch phase's first datagram and later delivery resumes inside its deadline
- **THEN** retry and bounded startup complete the required stage without duplicate initialization, false desync, or negotiation-induced input-hole failure

#### Scenario: A stale Goodbye cannot pass as an expected departure
- **WHEN** a captured old-epoch Goodbye is injected after a rematch starts
- **THEN** it is rejected without ending the current match and cannot satisfy the later planned current-epoch leave stage

#### Scenario: Negative fault must actually be exercised
- **WHEN** a timeout or input-hole reason appears but the configured negative fault was never injected or the error occurs before its declared stage
- **THEN** the run fails rather than treating the matching error text as expected success

### Requirement: New simulation verification protects rollback and identity boundaries

Automated verification SHALL cover packed movement/Dash encoding, cleared predicted Dash edges, complete clone/copy/checksum coverage, stable event identities, deterministic seed consumption, and replay equivalence for new mechanics at the retained two-tick delay and twelve-tick rollback window. Corrected on-time and delayed/reordered-input runs SHALL compare complete state and confirmed event history, including delayed Dash, stun/fall transitions, buff pickup/teleport, wall break, and occupied repair. Tests SHALL verify independent five- and fifteen-second lifecycle deadlines with heartbeat-only traffic, retained terminal reconciliation, initial/rematch startup, and already queued real input processed before fixed-step catch-up or timeout decisions.

Compatibility verification SHALL cover version 7 against archived versions 4, 5, and 6 with each side hosting, rejection of malformed/reserved action encodings, mismatched canonical rules/layout identities before play, nonce and epoch isolation, and acknowledgement bounds. A synthetic corrected snapshot used to isolate history bookkeeping SHALL be explicitly identified as an algorithm fixture, not a naturally reachable gameplay sequence. Published acceptance claims SHALL identify which evidence came from deterministic unit tests, two-socket integration, Unity tests, same-machine standalone players, and any separately performed two-computer run.

#### Scenario: Queued recovery is tested separately from missing input
- **WHEN** otherwise equivalent catch-up tests with a genuine interior hole and later accepted samples queue a valid missing input in one case and only a heartbeat in the other
- **THEN** the former reconciles before its next fixed step while the latter still reaches `INPUT HOLE` beyond the unchanged prediction bound

#### Scenario: Complete state agrees after delayed teleport and wall interaction
- **WHEN** a deterministic tape is executed on time and with recoverable late input across teleport, contact latches, and wall state changes
- **THEN** corrected authoritative state, random-generator state, checksums, and committed event identities agree without duplicated notifications

#### Scenario: Each legacy version is incompatible in either role
- **WHEN** a version-7 build attempts a connection with a version-4, version-5, or version-6 peer, once as host and once as joiner
- **THEN** each pairing rejects compatibility before playable ticks and does not rely on a later desync or generic handshake timeout as the proof

#### Scenario: Synthetic history evidence is scoped honestly
- **WHEN** a test supplies an explicitly constructed corrected snapshot to exercise an otherwise difficult terminal-history branch
- **THEN** the result is reported as focused reconciliation coverage and cannot substitute for a natural collision-goal match or a real runtime event sequence

### Requirement: Pacing diagnostics distinguish waiting from lost input

Reports SHALL expose the local simulation tick, newest remote tick, contiguous remote input frontier, current waiting state, wait episodes/duration, and input-progress age. A separately named pacing scenario SHALL introduce a bounded real standalone-player simulation-step stall while continuing transport, retain the injected role/count/duration, prove the other peer actually waits and resumes with confirmed input/checksum progress, and reject unrelated terminal failures. Existing fault-profile meanings SHALL remain unchanged. Automated socket tests SHALL separately prove heartbeat-only input-progress timeout and unchanged failure for a genuine interior hole. All evidence SHALL keep its actual same-machine or physical-LAN scope.

#### Scenario: Bounded stall resumes in actual players
- **WHEN** one standalone diagnostic peer temporarily stops simulation steps while continuing network polling and the peer reaches the safe lead limit
- **THEN** both reports retain actual stall/wait evidence followed by successful confirmed gameplay progress, rather than treating an injection request alone as PASS

#### Scenario: Waiting cannot relabel a real hole
- **WHEN** a test with a missing interior sample receives later inputs and heartbeats
- **THEN** it still verifies the original `INPUT HOLE` boundary instead of declaring pacing recovery successful
