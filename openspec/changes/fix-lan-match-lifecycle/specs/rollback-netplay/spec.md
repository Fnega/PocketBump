## ADDED Requirements

### Requirement: LAN results are based on reconciled terminal history
The LAN session SHALL treat a locally predicted match-over as provisional. It SHALL continue receiving, retransmitting recoverable stored input, reconciling rollback history, and validating confirmed checksums without advancing a stopped simulation or resampling input already assigned to a tick. It SHALL advertise a terminal result only after contiguous real remote input covers the actual final simulated tick and all applicable reconciliation has completed. The terminal tuple SHALL identify the connection, match epoch, final tick, corrected final checksum, scores, winner, and negotiated settings. A peer SHALL enable authoritative result actions only after validating the other peer's identical terminal tuple and acknowledgement of its own tuple. Terminal exchanges SHALL be retried and duplicates SHALL be idempotent.

#### Scenario: Last input changes the predicted winner
- **WHEN** a local prediction produces match-over but a retained late input corrects the terminal history
- **THEN** the peer reconciles the history before confirming a result, discards the provisional winner, and presents only the corrected mutually confirmed result or resumes playable simulation if the finish disappeared

#### Scenario: Reconciliation finishes at an earlier tick
- **WHEN** replay of late input reaches the true terminal state before the previously predicted last tick
- **THEN** only actually executed ticks contribute snapshots and checksums, superseded later records are invalidated, and the advertised terminal tick identifies the corrected finish

#### Scenario: Exact final state is checked
- **WHEN** the simulation stopped with its next-tick counter equal to N
- **THEN** the finish agreement compares corrected state after tick N minus 1 rather than substituting the ordinary N minus 2 checksum or a received input acknowledgement

#### Scenario: Tail input or finish acknowledgement is lost
- **WHEN** recoverable final input or a Finish/FinishAck message is dropped and later delivery succeeds within the terminal deadlines
- **THEN** retransmission covers the required retained suffix, duplicate data does not rewrite sampled input or advance simulation, and both peers can confirm the same result

#### Scenario: Terminal input never recovers
- **WHEN** required terminal input remains missing for five realtime seconds after entering terminal settlement even though valid heartbeats arrive
- **THEN** the peer reports `INPUT HOLE` through the frozen-Arena problem panel and does not confirm a winner or permit rematch

#### Scenario: Locally complete result cannot be agreed
- **WHEN** locally input-complete terminal history has waited fifteen realtime seconds without completing terminal agreement
- **THEN** the session reports `RESULT CONFIRMATION TIMEOUT` through the frozen-Arena problem panel rather than displaying an unconfirmed final result indefinitely

#### Scenario: Confirmed terminal states differ
- **WHEN** corresponding terminal history is corrected and input-complete but the peers' final checksums differ
- **THEN** the session follows the existing `DESYNC` pause behavior and does not permit rematch from the divergent result

### Requirement: Connection liveness is independent of gameplay ticks
Connected LAN peers SHALL exchange validated current-epoch liveness traffic independently of simulation ticks, including during terminal settlement, results, and rematch negotiation. Valid peer traffic SHALL be processed before declaring a receive timeout. A healthy result screen SHALL NOT time out solely because gameplay input production has stopped. Heartbeats SHALL NOT supply missing gameplay input, advance contiguous input acknowledgement, confirm results, or extend a result/transition progress deadline. Genuine peer silence SHALL retain the existing five-second disconnect timeout and frozen-Arena error behavior.

#### Scenario: Both players remain on results
- **WHEN** both peers have confirmed the result and exchange valid liveness traffic while staying on the result screen for at least eight realtime seconds
- **THEN** the confirmed game state remains unchanged and neither peer reports a network failure

#### Scenario: Peer disappears on the result screen
- **WHEN** no valid peer traffic arrives for five seconds while results are displayed
- **THEN** the remaining peer freezes with `PEER TIMEOUT` and waits for an explicit Main Menu action

#### Scenario: Heartbeats do not hide a gameplay hole
- **WHEN** current-epoch heartbeats arrive during active gameplay but a required remote input ages beyond the normal rollback window
- **THEN** the existing `INPUT HOLE` failure still occurs

#### Scenario: Invalid traffic does not keep a connection alive
- **WHEN** malformed packets, packets from another endpoint, or retired-epoch gameplay packets arrive
- **THEN** those packets do not refresh current-epoch liveness or alter gameplay, terminal agreement, or rematch consent

### Requirement: LAN rematch requires both players and starts once
After a mutually confirmed result, the session SHALL retain each player's explicit rematch consent for that epoch until a rematch succeeds or the player leaves. Consent SHALL automatically retry until the remote peer acknowledges it or incorporates it in a valid offer; recovery SHALL NOT require another button press. A single consent SHALL NOT start a match. The host SHALL initiate exactly one next-epoch transition after both consents, preserving negotiated seats and rule values. Offer, preparation, commit, acknowledgement, and start processing SHALL be retried, phase-validated, and idempotent. The game SHALL retain its connection while preparing a fresh tick-zero simulation and rollback session; LAN rematch SHALL NOT independently reload a scene or send Goodbye. Per-match gameplay, input edges, presentation, and diagnostic state SHALL reset once, while previous-match evidence SHALL remain available separately.

#### Scenario: Only one player requests rematch
- **WHEN** one player selects Rematch and the other player has not done so
- **THEN** both remain in the completed match, the requesting player sees a waiting state, and healthy waiting alone does not expire the session

#### Scenario: Both players request rematch
- **WHEN** both players consent after confirming the same result and the transition completes
- **THEN** both enter the same next epoch with the original seats and rules, zero scores, fresh tick-zero state, and no old input edges, checksum history, or result overlays

#### Scenario: First consent is lost
- **WHEN** a player's first rematch-consent datagram is dropped but later delivery succeeds
- **THEN** automatic retries deliver the same consent without requiring the player to click again and without creating an additional rematch

#### Scenario: Rematch control packets are dropped or duplicated
- **WHEN** a transition message or acknowledgement is lost, reordered, or repeated within the negotiation deadline
- **THEN** retries converge on the same transition, neither peer initializes it more than once, and both can start the next game without a negotiation-induced input hole

#### Scenario: Last start packet is lost
- **WHEN** both peers have committed the next epoch but a final Start, first input, or first acknowledgement packet is lost
- **THEN** a bounded startup barrier retains and retransmits initial real input, prevents advancement from exhausting recoverable history, and releases only after validated two-way new-epoch input/ack progress beyond the initial exchange; repeated copies of the first sample alone do not release it

#### Scenario: Negotiation stalls despite a live peer
- **WHEN** a rematch offer has started but the next-epoch transition and startup do not converge within fifteen realtime seconds
- **THEN** the session reports `REMATCH TIMEOUT` through the frozen-Arena problem panel and does not silently fall back to local play

#### Scenario: Repeated rematches preserve behavior
- **WHEN** the peers complete at least three successive bilateral rematches
- **THEN** each advances to one distinct epoch with unchanged rules and seats, and every new match resumes actual input processing and confirmed checksum validation

### Requirement: Session traffic is isolated across rematches and fresh joins
Every established-session datagram SHALL be bound to the negotiated endpoint, connection identity, and match epoch. A fresh join attempt SHALL carry a fresh nonce, reuse it only for retries of that attempt, and accept only the matching acknowledgement. Rematches SHALL advance the epoch monotonically within the existing connection without reusing an epoch. Only the current epoch and an explicitly retained transition to its committed successor SHALL affect lifecycle processing. Old input, result, heartbeat, or Goodbye traffic SHALL NOT mutate or terminate the new epoch. Transition duplicates SHALL be answered only as required for convergence and SHALL NOT recreate state. Input acknowledgements used for startup or history retirement SHALL be monotonic and bounded by input actually sent in that epoch.

#### Scenario: Old match packets arrive after rematch
- **WHEN** delayed input, terminal, heartbeat, and Goodbye packets from the previous epoch arrive during the new game
- **THEN** the new simulation, consent state, result, and liveness remain unaffected and the connection is not terminated by the old Goodbye

#### Scenario: Unexpected future epoch is received
- **WHEN** a packet names a future epoch for which the peer has not accepted and committed a valid transition
- **THEN** it does not start a match, supply input, or reset existing state

#### Scenario: Old join acknowledgement arrives after returning to menu
- **WHEN** the player starts a fresh Join LAN attempt and receives an acknowledgement from an earlier attempt
- **THEN** the nonce mismatch prevents that acknowledgement from initializing the new session

#### Scenario: Leave and host again
- **WHEN** either peer explicitly leaves, both return to the main menu, and a fresh host/join handshake is performed
- **THEN** the new connection uses fresh identity and handshake state with no leaked socket, stale pause, result, or consent state

#### Scenario: Peer leaves while rematch is pending
- **WHEN** a peer's current-epoch Goodbye arrives before the new match is committed
- **THEN** the pending rematch is discarded and the remaining peer sees `PEER LEFT` in the existing frozen-Arena panel until choosing Main Menu

#### Scenario: Future acknowledgement attempts to release startup
- **WHEN** an input acknowledgement exceeds the highest actually sent local sample for that epoch
- **THEN** it cannot retire input history or release the startup barrier

## MODIFIED Requirements

### Requirement: Corrected physics sessions reject pre-fix simulation versions
The corrected collision and shot rules with LAN lifecycle support SHALL advertise protocol/rules compatibility version 6 using the handshake and settings-hash mechanism. A version-6 peer SHALL NOT begin a playable match with a version-5 lifecycle-incompatible peer or a pre-fix version-4 peer, regardless of which side hosts. The implementation SHALL reject version incompatibility through the existing compatibility-failure path before gameplay, without waiting for a state-checksum mismatch or a generic handshake timeout; the existing `SETTINGS MISMATCH` reason SHALL remain acceptable and no new wording is required from archived builds. Same-version peers SHALL use matching version-6 message layouts and lifecycle validation while preserving the corrected physics, seat negotiation, fixed input delay, and reliable handshake behavior.

#### Scenario: Corrected host rejects pre-fix joiner
- **WHEN** a version-4 joiner requests a match from a version-6 host
- **THEN** the corrected host rejects the incompatible request through its existing compatibility-failure path and neither peer starts a playable match for that request

#### Scenario: Corrected joiner rejects pre-fix host
- **WHEN** a version-6 joiner encounters a version-4 host or a pre-fix version-4 join acknowledgement
- **THEN** the handshake does not complete as a playable match and the corrected joiner reports the existing compatibility failure before gameplay ticks begin

#### Scenario: Corrected peers use the same physics rules
- **WHEN** two version-6 peers with matching match settings complete the handshake
- **THEN** both enter the corrected simulation with the agreed seats and input delay, and corresponding confirmed states use the corrected collision and full-range shot behavior

#### Scenario: Lifecycle host rejects the previous release
- **WHEN** a version-5 joiner requests a match from a version-6 host
- **THEN** the version-6 host rejects the incompatible request before gameplay through `SETTINGS MISMATCH`

#### Scenario: Lifecycle joiner rejects the previous release
- **WHEN** a version-6 joiner encounters a version-5 host or its legacy acknowledgement
- **THEN** the version-6 joiner reports `SETTINGS MISMATCH` before gameplay rather than accepting a peer without lifecycle support
