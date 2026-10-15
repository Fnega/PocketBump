## MODIFIED Requirements

### Requirement: LAN session connects two peers before tick zero

The game SHALL support a two-peer LAN match over UDP. One peer SHALL host and listen; the other SHALL join with the host address. Peers SHALL agree on protocol version, match settings, immutable rules and arena-content identity, authoritative random initialization, and seats (player one north, player two south) before simulation tick 0. `Join`, `JoinAck`, and phased `Ready` messages SHALL be retried until the next handshake state is confirmed or the handshake deadline expires. Duplicate or out-of-order handshake messages SHALL be handled idempotently. A version, settings, rules, or layout mismatch SHALL abort without starting ticks. Initial gameplay SHALL retain a bounded two-way input/acknowledgement startup barrier so loss of the final readiness confirmation cannot let one peer exhaust the existing rollback history before the other starts.

#### Scenario: Host waits then starts
- **WHEN** a player hosts a LAN match with points-to-win 8 and scoring-ball count 1 and a second player joins with matching compatibility and content identities
- **THEN** both peers enter the same match with those settings and authoritative initialization, and tick 0 has not run before both peers have declared readiness

#### Scenario: Dropped join is retried
- **WHEN** the joiner's first `Join` datagram is dropped and the handshake deadline has not expired
- **THEN** the joiner sends `Join` again and the host can continue the same handshake when a retry arrives

#### Scenario: Dropped readiness response is retried
- **WHEN** both peers have declared readiness but a readiness response or confirmation is dropped
- **THEN** the appropriate readiness phase is sent again, neither peer creates a second match or changes its negotiated seat, and the initial startup barrier prevents advancement beyond recoverable initial history

#### Scenario: Duplicate handshake message is idempotent
- **WHEN** the host receives the same valid `Join` more than once from the negotiated joiner
- **THEN** each response advertises the same connection identity, settings, content identity, random initialization, and seat assignment

#### Scenario: Settings mismatch aborts
- **WHEN** the host's advertised settings or canonical rules/layout identity does not match the joiner's compatible local content
- **THEN** neither peer starts match ticks and both retain a paused Arena with the compatibility failure shown in the top-right problem panel and an explicit Main Menu action

#### Scenario: Heartbeats do not complete initial startup
- **WHEN** valid peer heartbeats continue but the initial two-way input/acknowledgement barrier does not complete within fifteen realtime seconds
- **THEN** the initial session reports `LAN HANDSHAKE TIMEOUT` without fabricating input progress or waiting indefinitely

### Requirement: Each peer drives only their seat

In a networked match, each peer SHALL sample input only for the seat they were assigned. That seat SHALL use the machine's local player-one movement and Dash bindings, including the approved binding migration, unless rebound on that machine. The remote seat SHALL be driven only by inputs received or predicted for that seat. The gameplay input vocabulary SHALL contain movement and the single `DashPressed` edge, not shooting, possession, charging, or a held Boost action. A fallen or stunned player SHALL NOT stop the network tick/input pipeline; simulation eligibility SHALL discard forbidden actions without queueing them for recovery.

#### Scenario: Host is player one
- **WHEN** the host is assigned seat 0 and holds W
- **THEN** only player one's simulated body accelerates north from that input and player two is not driven by the host's WASD

#### Scenario: Joiner uses local WASD for seat two
- **WHEN** the joiner is assigned seat 1 and holds W on their machine
- **THEN** player two's simulated body accelerates north and the joiner's arrow-key bindings are not required

#### Scenario: Disabled control does not disable transport
- **WHEN** the locally assigned player is stunned or fallen while the match continues
- **THEN** both peers continue exchanging the required tick input and acknowledgements, and a Dash press discarded during that state cannot activate automatically when the player recovers

### Requirement: Remote input is predicted then rolled back

Each peer SHALL advance the match simulation using confirmed local input for their seat and a prediction for the remote seat when the remote bits for that tick have not arrived. When later-arriving remote input for a past tick differs from the prediction, the peer SHALL restore simulation state at that tick and resimulate forward to the present. Prediction SHALL reuse movement from the newest accepted remote tick, or neutral input if none exists. Prediction SHALL always clear `DashPressed`; it SHALL NOT recreate the removed Shoot or held Boost semantics. An older out-of-order sample SHALL NOT replace the newest prediction source. Replay SHALL restore all authoritative dash-arena state, not only body positions.

#### Scenario: Late remote bits correct the table
- **WHEN** a peer predicted no Dash press for tick 40 and then receives a valid `DashPressed` for tick 40
- **THEN** that peer restores state at tick 40 and resimulates with that press, and the complete resulting state matches a baseline that received the press on time

#### Scenario: Matching prediction does not rewind
- **WHEN** received remote input for a tick equals the predicted input for that tick
- **THEN** the peer does not restore an earlier snapshot solely because that packet arrived

#### Scenario: Dash edge is not repeated by prediction
- **WHEN** the newest confirmed remote input contains `DashPressed` and the next remote tick has not arrived
- **THEN** the predicted input retains eligible movement but has `DashPressed` cleared

#### Scenario: Older input does not regress prediction
- **WHEN** tick 75 has already supplied the prediction source and a reordered datagram later supplies tick 72
- **THEN** future unconfirmed ticks continue predicting from tick 75

### Requirement: Gameplay datagrams are validated before state mutation

The receiver SHALL validate datagram length, explicit protocol version and message type, negotiated endpoint, connection identity, current or explicitly retained transition epoch, remote seat, input bit vocabulary, history bounds, and an allowed tick window before mutating rollback state. Acknowledgements used for history retirement or startup SHALL be bounded by input actually sent in the identified epoch. An older valid acknowledgement SHALL NOT regress acknowledgement state or prevent independent insertion of valid input history in the same datagram. Invalid datagrams SHALL be ignored without throwing from the Unity frame loop or extending the valid peer's disconnect timer. Reserved input bits and removed action encodings SHALL NOT be silently interpreted as Dash.

#### Scenario: Malformed datagram is ignored
- **WHEN** a datagram has valid magic and an input type but is shorter than the declared input payload
- **THEN** no input is stored, the peer timeout is not refreshed, and the frame loop continues

#### Scenario: Wrong peer cannot inject gameplay input
- **WHEN** an input datagram arrives from an endpoint other than the endpoint accepted during handshake
- **THEN** the datagram does not change remote input, acknowledgement, checksum, or connection liveness state

#### Scenario: Excessively future input is rejected
- **WHEN** a syntactically valid sample declares a tick beyond the configured future allowance
- **THEN** the sample is not written into the rollback ring buffer

#### Scenario: Reordered acknowledgement does not lose valid history
- **WHEN** a valid reordered datagram carries a backward acknowledgement and a missing input sample still inside the allowed history window
- **THEN** acknowledgement state remains monotonic and the addressed input sample can still be inserted and reconciled

#### Scenario: Removed or reserved actions are rejected
- **WHEN** an input datagram uses a prior-version packet layout or noncanonical action bits outside the negotiated movement and Dash vocabulary
- **THEN** it cannot supply a Dash press, alter rollback state, or refresh current-session liveness

### Requirement: Corrected physics sessions reject pre-fix simulation versions

The dash-collision arena SHALL advertise protocol/rules compatibility version 7 using the handshake and canonical settings/content identity mechanism. Implementation SHALL verify that version 7 is still unclaimed in the effective version-6 lifecycle baseline before committing the new codec; a conflicting reservation SHALL require an explicit planning update rather than silent reuse. A version-7 peer SHALL NOT begin a playable match with version-4, version-5, or version-6 peers, regardless of which side hosts. Legacy handshake probing SHALL retain bidirectional, pre-game compatibility rejection rather than converting a known incompatible version into a generic timeout. The existing `SETTINGS MISMATCH` reason SHALL remain acceptable and archived builds SHALL NOT require new wording. Same-version peers SHALL use matching version-7 packet layouts and lifecycle validation, with movement/Dash inputs, the new collision-goal rules, unchanged seat negotiation, 60 Hz simulation, a fixed two-tick input delay, and the existing twelve-tick rollback window.

#### Scenario: Corrected host rejects pre-fix joiner
- **WHEN** a version-4 joiner requests a match from a version-7 host
- **THEN** the corrected host rejects the incompatible request through the existing compatibility-failure path and neither peer starts a playable match for that request

#### Scenario: Corrected joiner rejects pre-fix host
- **WHEN** a version-7 joiner encounters a version-4 host or a pre-fix version-4 acknowledgement
- **THEN** the handshake does not complete as a playable match and the corrected joiner reports compatibility failure before gameplay ticks begin

#### Scenario: Corrected peers use the same physics rules
- **WHEN** two version-7 peers with matching canonical match/content identities complete the handshake and bounded startup exchange
- **THEN** both use the agreed seats and input delay, and corresponding confirmed states follow free-ball collision goals and Dash rather than basket shooting or held Boost

#### Scenario: Lifecycle host rejects the previous release
- **WHEN** a version-5 or version-6 joiner requests a match from a version-7 host
- **THEN** the version-7 host rejects the incompatible request before gameplay through the compatibility-failure path

#### Scenario: Lifecycle joiner rejects the previous release
- **WHEN** a version-7 joiner encounters a version-5 or version-6 host or its legacy acknowledgement
- **THEN** the version-7 joiner reports compatibility failure before gameplay rather than accepting the old rules or waiting for a generic handshake timeout

## ADDED Requirements

### Requirement: Dash arena content identity is canonical and immutable

Each match SHALL use a validated immutable rules and layout profile with an explicit schema identity and canonical serialization/hash. Its compatibility identity SHALL cover all gameplay-affecting values, including arena bounds, goal ownership and goal geometry, death zones, player and ball collision sizes, spawn regions and slots, ball count, stable obstacle identifiers and geometry, initial wall states, buff generation and destination candidates, action and impact parameters, phase/timer policies, tie ordering, and win settings. Presentation-only assets and camera positions SHALL NOT replace or alter this authority. Candidate tuning values awaiting approval SHALL NOT become an implicit production profile. Peers SHALL agree on the canonical identity before tick zero, retain it through finish agreement and same-connection rematches, and reject hot changes during a match.

#### Scenario: Layout mismatch fails before play
- **WHEN** two peers agree on points-to-win and ball count but differ in one wall footprint, death-zone boundary, or safe spawn slot
- **THEN** their canonical content identities differ and the handshake rejects the match before playable simulation

#### Scenario: Art changes do not create gameplay authority
- **WHEN** a render prefab or camera differs without changing the validated gameplay profile
- **THEN** simulation still uses the same agreed ground-plane geometry and no visual transform or animation supplies a new collision boundary

#### Scenario: Rematch retains the agreed profile
- **WHEN** both players consent to a new epoch on the retained connection
- **THEN** the fresh match uses the same approved rules/layout identity and match settings instead of rereading mutable editor or local menu values

### Requirement: Complete dash arena state survives rollback and reset boundaries

All future-affecting simulation state SHALL participate in snapshot clone/copy/restore and corrected-state checksum computation using stable canonical ordering. This SHALL include player and ball poses and every velocity channel; facing and locked Dash direction; active Dash, cooldown, stun, fall and respawn state; collision eligibility and contact-episode latches; scores, winner and phase clocks; active buffs and pickup identities; spawn clocks; wall solid/broken/repair-pending state and repair clocks; authoritative random-generator state and sequence counters; and any additional state introduced by the approved rules. Snapshot storage SHALL not alias mutable live arrays or omit fields merely because they are not currently visible. Removed basket, possession, charging, Shoot, and held Boost state SHALL have no remaining gameplay authority.

Timer and reset behavior SHALL follow the new gameplay capabilities: Dash lasts 18 ticks and starts its 180-tick cooldown on termination; cylinder stun lasts 36 ticks; individual fall recovery uses a 180-tick delay and the approved safe-spawn policy without pausing other bodies; wall repair becomes eligible after 600 world ticks and waits for full clearance. Wall clocks SHALL advance during Live, Kickoff, and scored freeze, but stop at MatchOver and network pause. Goal reset SHALL preserve obstacle state and repair progress while applying the documented player/ball/buff round reset. Individual respawn SHALL NOT reset the opponent, balls, or persistent world state. A new agreed epoch SHALL construct fresh match state exactly once and clear old input edges, contact latches, presentation reconciliation state, and rollback history while preserving prior-epoch diagnostic evidence.

#### Scenario: Replay restores timers and contact episodes
- **WHEN** delayed input within the rollback window changes a Dash impact followed by a cylinder contact or fall
- **THEN** corrected state matches an on-time baseline for velocities, Dash/cooldown, stun/fall timers, collision eligibility, contact latches, and ball motion rather than only matching positions

#### Scenario: Replay reproduces occupied wall repair
- **WHEN** rollback crosses a wall's repair eligibility tick and corrected input leaves a player or ball inside its restored footprint
- **THEN** the wall remains repair-pending on both peers until the same later clear tick and does not trap or displace an occupant

#### Scenario: Snapshot coverage detects an invisible difference
- **WHEN** a stored state's Dash cooldown, contact latch, pending-wall clock, pickup identity, or random state is changed without changing visible body positions
- **THEN** its corrected checksum changes and clone/copy/restore tests preserve the altered field without changing another independent snapshot

#### Scenario: Goal and individual respawn have different reset scopes
- **WHEN** a goal or a single player's fall recovery occurs while a wall is broken
- **THEN** the appropriate documented player/ball/buff reset applies, the wall keeps its repair progress, and individual respawn does not reset the rest of the world

### Requirement: Arena randomness is agreed and replayable

Gameplay randomness SHALL use an explicitly agreed seed and deterministic authoritative generator for each match epoch. Connection `MatchId` SHALL remain an identity boundary, not an implicit or machine-local seed. Both peers SHALL derive or receive the same seed through the negotiated initialization policy, and the selected seed and policy SHALL be diagnosable. Buff spawning, pickup arbitration, and blue safe-teleport candidate evaluation SHALL use documented stable ordering and bounded random consumption. Local random APIs, render timing, packet-arrival order, and effect particles SHALL NOT consume or reseed gameplay randomness. Random state SHALL be restored before replay, and new-epoch initialization SHALL use the agreed seed policy exactly once.

#### Scenario: Peers choose the same safe teleport
- **WHEN** both peers replay the same blue pickup from identical authoritative state and agreed random state
- **THEN** they choose the same safe destination or the same bounded no-destination outcome and finish with identical generator state

#### Scenario: Replaying a pickup does not consume extra randomness
- **WHEN** late input causes the simulation to execute an already predicted buff spawn or teleport again
- **THEN** restored random state reproduces the corrected sequence rather than consuming an additional live-stream draw

#### Scenario: Identity does not substitute for a seed agreement
- **WHEN** a fresh connection or epoch is initialized
- **THEN** both peers establish and report the agreed gameplay seed according to the profile without relying on independently generated connection identities or local randomness

### Requirement: Dash arena integration retains bounded lifecycle progress

The dash-arena runtime SHALL preserve the effective `fix-lan-match-lifecycle` guarantees: corrected terminal-history reconciliation, matching Finish/FinishAck terminal tuples, independent current-epoch liveness, bilateral idempotent rematches on one connection/socket, monotonic epoch isolation, nonce-bound fresh joins, and bounded initial and rematch startup. The finish tuple's settings/content identity SHALL bind the new rules profile. A provisional goal-based finish corrected by late input SHALL replace the terminal history or resume Live as required, without inventing ticks after MatchOver. Individual death, buff UI, split-screen rendering, and world repair timers SHALL NOT authorize results or bypass any lifecycle phase.

Already queued valid network traffic SHALL be drained and reconciled before each fixed-step simulation advance, including multiple catch-up steps in one render frame, and before realtime liveness/progress timeout decisions. This ordering SHALL NOT enlarge the twelve-tick prediction window or permit a genuinely missing input to age beyond it. Heartbeats SHALL NOT fill input holes or acknowledge unsent samples. Five-second silence and terminal-input-settlement deadlines and independent fifteen-second initial-startup, result-confirmation, and rematch-transition deadlines SHALL remain in force. Healthy confirmed results and unilateral rematch consent SHALL remain valid indefinitely while peer liveness is healthy.

#### Scenario: Queued gameplay protects a catch-up step
- **WHEN** an input hole is at the twelve-tick boundary and a valid recovery datagram is already queued before a catch-up fixed step
- **THEN** the runtime drains and reconciles that input before advancing and does not produce an avoidable `INPUT HOLE`

#### Scenario: Heartbeat-only traffic cannot weaken the input bound
- **WHEN** only valid heartbeats are queued and a genuinely missing interior gameplay sample, with later remote samples already accepted, reaches the twelve-tick rollback boundary
- **THEN** simulation stops with `INPUT HOLE` despite fresh liveness and no additional prediction allowance is introduced

#### Scenario: A final collision goal is corrected
- **WHEN** late retained input invalidates a predicted final goal or changes its winner
- **THEN** the peer reconciles the actual executed terminal history before advertising a result, discards superseded terminal records, and either agrees on the corrected result or resumes gameplay

#### Scenario: Live heartbeats do not extend progress deadlines
- **WHEN** heartbeats arrive while required final input, initial startup progress, terminal agreement, or an initiated rematch transition remains incomplete
- **THEN** the corresponding independent five- or fifteen-second deadline still produces its existing visible failure instead of hanging indefinitely

#### Scenario: Rematch loses its initial Dash-bearing input
- **WHEN** the new epoch loses Start or an initial real input/acknowledgement containing a Dash press
- **THEN** bounded startup retransmits the originally sampled data without resampling, duplicate initialization, duplicated Dash activation, or negotiation-induced exhaustion of rollback history

### Requirement: In-match input pacing bounds simulation lead

During active gameplay, the session SHALL check input lead before sampling or advancing a tick. When the remote contiguous-input frontier equals its newest accepted input and the local simulation reaches a safe lead below the twelve-tick rollback limit, it SHALL temporarily stop advancing rather than classify the remote peer's not-yet-generated next input as an expired hole. It SHALL continue receiving, acknowledging, retransmitting original input history, and running realtime lifecycle maintenance. Valid forward contiguous input progress SHALL permit automatic resumption. No additional prediction allowance, changed input delay, fabricated input, or global time-scale change SHALL be introduced. A genuine interior hole SHALL retain the existing rollback failure boundary.

Temporary waiting SHALL have an independent five-realtime-second input-progress deadline. Heartbeats, duplicate samples, stale epochs, rejected packets, or acknowledgements alone SHALL NOT refresh it. Queued valid input SHALL be handled before the deadline decision. Expiry SHALL produce terminal `INPUT PROGRESS TIMEOUT`, preserve the frozen Arena, and require an explicit Main Menu action; the existing peer-silence deadline may fail independently. Waiting SHALL not consume a pending Dash edge or sample the same simulation tick twice. New epochs and sessions SHALL clear stale waiting state.

#### Scenario: Fast peer waits for an ungenerated suffix
- **WHEN** one peer's fixed steps outpace the other's complete accepted input prefix near the safe lead limit
- **THEN** it waits with transport active, keeps its current simulation tick and timers, and resumes without a false input hole after sufficient valid input arrives

#### Scenario: Heartbeats cannot keep input waiting alive forever
- **WHEN** valid heartbeats continue but contiguous input makes no progress for five realtime seconds during pacing wait
- **THEN** the session reports `INPUT PROGRESS TIMEOUT` and does not automatically return to gameplay or the menu

#### Scenario: Waiting does not swallow a Dash edge
- **WHEN** a Dash press is latched while simulation is waiting and valid input later permits one step
- **THEN** that step samples the pending edge once and subsequent retransmission preserves the original sample without reactivating Dash

#### Scenario: Later samples expose an actual middle hole
- **WHEN** later accepted remote inputs demonstrate a missing interior sample rather than an ungenerated suffix
- **THEN** pacing does not suppress the original twelve-tick `INPUT HOLE` protection

### Requirement: Dash arena presentation follows reconciled event history

Authoritative gameplay events SHALL have stable epoch/tick and source/event identities sufficient to reconcile and deduplicate rollback. Committed impact, goal, pickup explanation, stun, fall/respawn, wall-break, and wall-repair effects and sounds SHALL correspond to confirmed or correctly reconciled history and SHALL NOT be emitted again merely because a tick is replayed. Responsiveness MAY use a predicted local Dash visual, but that visual SHALL be cancellable or reconcilable and SHALL not mutate physics, random state, cooldowns, or event evidence. Goal reset, individual respawn, rematch, disconnect, and return to menu SHALL clear or reconcile presentation at their documented scope; an old epoch's trail or notification SHALL not leak into a new epoch.

#### Scenario: One impact is replayed several times
- **WHEN** a body-ball impact or wall-break tick is replayed while late input settles
- **THEN** only the event remaining in corrected history becomes committed evidence and its sound or one-shot effect is committed at most once

#### Scenario: A predicted Dash is invalidated
- **WHEN** rollback shows that a predicted Dash was ineligible because the player was stunned, fallen, or cooling down
- **THEN** the speculative visual is removed or corrected, no committed Dash event is fabricated, and presentation does not alter the corrected simulation

#### Scenario: New epoch removes old effects
- **WHEN** a bilateral rematch creates fresh tick-zero state
- **THEN** old Dash trails, pickup messages, death/respawn effects, obstacle effects, and event-deduplication state are reset exactly once without discarding separately retained previous-match reports
