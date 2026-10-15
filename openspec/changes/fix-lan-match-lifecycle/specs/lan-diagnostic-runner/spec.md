## MODIFIED Requirements

### Requirement: Development builds produce peer diagnostic reports
When launched in LAN smoke mode, a development build SHALL write a terminal report for its host or join role. The report SHALL state pass/fail, terminal reason, simulation tick, last accepted remote tick, last acknowledgement, packet receive/rejection counters, and a bounded recent-event trail. Lifecycle scenarios SHALL additionally retain scenario/stage identity, connection identity and match epoch, confirmed terminal tick/checksum/scores/winner, result-hold duration, rematch count, liveness/control counters, and expected versus observed departure. Completed-epoch evidence SHALL survive per-match counter resets. A report SHALL distinguish a seeded lifecycle fixture from a match completed from ordinary initial state. PASS SHALL mean that every required stage of the selected scenario passed, not merely that a tick threshold or one match was reached.

#### Scenario: Match starts successfully
- **WHEN** host and join smoke processes complete the default local LAN handshake and satisfy the existing movement, live-tick, and confirmed-checksum thresholds
- **THEN** each process writes a passing default-smoke report containing its role and network snapshot without claiming a completed natural match

#### Scenario: Session aborts
- **WHEN** a smoke process observes an unexpected session error or its scenario deadline expires
- **THEN** it writes a failing report before exiting and the report contains the terminal reason, last reached stage, and available network snapshot

#### Scenario: No handshake packet arrives
- **WHEN** a host or join smoke peer reaches its handshake deadline without receiving a datagram
- **THEN** its report distinguishes send attempts, send failures, local endpoint, remote endpoint, and the last socket error from receive counters

#### Scenario: Expected departure is observed
- **WHEN** a scenario has explicitly entered its planned leave stage and the remaining peer receives the departing peer's valid Goodbye
- **THEN** the report records `PEER LEFT` as the expected observation for that stage, still requires subsequent prescribed stages, and does not classify an earlier unexpected departure as success

#### Scenario: A first rematch does not finish a multi-match scenario
- **WHEN** a lifecycle scenario completes its first rematch but further rematches or rejoin stages are required
- **THEN** neither peer emits a final whole-scenario PASS yet and the completed epoch's evidence remains available

### Requirement: Local pair launcher preserves test evidence
The repository SHALL provide a Windows development helper that launches host and join standalone players on loopback, waits for their reports, and retains a timestamped result directory containing both reports and launcher status. It SHALL preserve the existing default smoke invocation and support explicit lifecycle and full-match scenarios with appropriate stage deadlines and selectable fault profiles. It SHALL wait for both final whole-scenario reports rather than stopping players on an intermediate success marker. Logs, scenario parameters, stage failures, and effective deadlines SHALL be retained on success or failure. Cleanup SHALL affect only processes launched by that invocation.

#### Scenario: User runs a local diagnostic pair
- **WHEN** a developer supplies a Development build executable to the helper without selecting a new scenario
- **THEN** the helper starts host before join, preserves the default movement-smoke behavior, and prints the created results directory after both roles finish or the helper deadline expires

#### Scenario: User runs a lifecycle scenario
- **WHEN** the developer selects lifecycle diagnostics
- **THEN** the helper retains both players until they complete or fail the specified whole scenario, then preserves both reports, full logs, and launcher status in the result directory

#### Scenario: Player startup fails
- **WHEN** the host or join player cannot become ready or exits before its report
- **THEN** the helper reports failure, retains available logs and launcher-stage evidence, and does not leave its surviving launched peer running

## ADDED Requirements

### Requirement: Lifecycle diagnostics exercise results rematches and fresh sessions
The lifecycle scenario SHALL exercise confirmed terminal agreement, at least eight realtime seconds on healthy results, unilateral consent without starting, bilateral rematch, at least three successive rematches, and active new-epoch input/checksum progress. Separate variants SHALL exercise a host-initiated and a joiner-initiated departure, manual menu recovery on the remaining peer, and successful fresh Host LAN/Join LAN. A development-only near-finish fixture SHALL be labeled `finish_source=seeded_fixture` and SHALL initialize both peers consistently before constructing their rollback baselines only for epoch zero of the first connection. Every rematch and the later fresh connection SHALL use normal production initialization. Subsequent finishes needed for repeated rematches SHALL be reached through ordinary input, and reports SHALL identify the actual finish source per epoch. Direct result UI calls, repeated score seeding, or unsynchronized mid-match state edits SHALL NOT count as lifecycle completion.

#### Scenario: Seeded lifecycle fixture reaches a real simulated finish
- **WHEN** both peers start with the agreed development-only near-finish fixture
- **THEN** normal simulation produces match-over, both peers confirm the same terminal tuple, and the report labels the fixture origin rather than claiming a natural full match

#### Scenario: Results remain healthy across repeated matches
- **WHEN** the lifecycle scenario performs result holds and three bilateral rematches
- **THEN** neither healthy hold triggers a timeout, each next epoch is initialized exactly once from normal zero-score state with unchanged seats/rules, and both peers record resumed input and checksum progress; after the third rematch, reaching Live with confirmed progress satisfies the repeated-rematch milestone

#### Scenario: Each role can leave and establish a new session
- **WHEN** the host-led and joiner-led leave variants run with Goodbye delivery enabled
- **THEN** the remaining peer observes `PEER LEFT`, returns through the real menu action, and both peers successfully establish a fresh connection without stale state or port-binding failure

### Requirement: Full-match diagnostics prove actual gameplay completion
The full-match scenario SHALL begin from ordinary game state and use packed local input through normal simulation, UDP transport, and rollback. It SHALL require confirmed evidence of collection, actual shot release, dynamic collision, scoring, a legal match winner, mutually confirmed terminal state, and a subsequent rematch reaching live play. Event evidence SHALL be committed only for confirmed history or correctly reconciled and deduplicated by epoch/tick. The scenario SHALL NOT alter scores, force MatchOver, or switch to a near-finish fixture to obtain PASS. A deadline without required milestones SHALL produce FAIL and identify what was not reached.

#### Scenario: Shot button presses do not prove shots
- **WHEN** scripted inputs include ShootPressed but no marble is actually released
- **THEN** the shot milestone remains incomplete and those button presses cannot satisfy full-match success

#### Scenario: Replayed events are counted once
- **WHEN** rollback re-executes a tick containing a shot, collision, or score
- **THEN** only events remaining in confirmed history contribute to scenario totals and each such event is counted once

#### Scenario: Full match and rematch succeed
- **WHEN** normal input-driven play reaches all gameplay milestones, the legal win condition, matching final state, and a bilateral rematch into live play
- **THEN** the report identifies `finish_source=normal_gameplay`, includes the milestone evidence and terminal tuple, and permits full-match PASS

#### Scenario: Scripted gameplay fails to finish
- **WHEN** the scenario deadline arrives before the legal winner or another required milestone
- **THEN** both available reports identify the incomplete stage and the run fails without artificially completing the match

### Requirement: Lifecycle fault profiles verify control recovery and stale traffic rejection
Development diagnostics and automated socket tests SHALL support reproducible targeted loss, duplication, reordering, and stale-epoch delivery for terminal and rematch control messages. Profiles SHALL cover the first Finish, FinishAck, rematch intent, offer, preparation, commit, commit acknowledgement, Start, and initial new-epoch input. Profiles SHALL distinguish recoverable faults from deliberately terminal failures. The existing first-handshake-control-drop coverage SHALL remain available.

#### Scenario: A control phase loses its first packet
- **WHEN** one selected terminal or rematch phase loses its first datagram and subsequent delivery resumes within the deadline
- **THEN** retries complete the expected stage without duplicate initialization, false DESYNC, or a startup-induced input hole

#### Scenario: Old Goodbye arrives in a new epoch
- **WHEN** a fault profile delivers a captured old-epoch Goodbye after the next match has started
- **THEN** the connection remains active and the report records rejection without changing current-epoch state

#### Scenario: Peer vanishes instead of sending Goodbye
- **WHEN** a negative test suppresses all valid peer traffic beyond the disconnect deadline
- **THEN** the remaining peer reports `PEER TIMEOUT`, not successful intentional departure, and retains the diagnostic evidence
