## ADDED Requirements

### Requirement: Gameplay input transport recovers bounded datagram loss
Each gameplay input datagram SHALL carry the sender's newest input and a bounded contiguous history of preceding input samples. The receiver SHALL insert every valid sample independently and SHALL tolerate duplicate and out-of-order delivery without applying an input to the wrong tick. A later datagram SHALL be able to recover an earlier missing input while that input remains inside the rollback window.

#### Scenario: Later history recovers a dropped input
- **WHEN** the datagram whose newest sample is tick 40 is dropped AND the tick 41 datagram includes tick 40 in its history
- **THEN** the receiver stores the original tick 40 input before it leaves the rollback window AND the match does not abort for an input hole at tick 40

#### Scenario: Duplicate history is idempotent
- **WHEN** two valid datagrams contain the same remote input sample for tick 52
- **THEN** processing the duplicate does not advance the simulation twice, corrupt another ring-buffer slot, or trigger a rollback when the input bits are identical

#### Scenario: Reordered history fills only the addressed ticks
- **WHEN** a datagram containing ticks 61 through 68 arrives before a datagram containing ticks 57 through 64
- **THEN** every sample is stored under its declared tick AND older delivery does not replace the prediction source selected from tick 68

### Requirement: Remote input acknowledgement is contiguous
Each peer SHALL acknowledge the highest remote input tick for which all required input samples through that tick have been received. Receipt of a newer tick SHALL NOT acknowledge across an earlier missing tick. The acknowledgement SHALL advance across the stored range when the gap is later filled.

#### Scenario: Acknowledgement stops at a gap
- **WHEN** inputs through tick 80 are contiguous, tick 81 is missing, and tick 82 is received
- **THEN** the reported acknowledgement remains tick 80

#### Scenario: Filling a gap advances acknowledgement
- **WHEN** tick 81 later arrives and tick 82 is already stored
- **THEN** the reported acknowledgement advances through tick 82

### Requirement: Gameplay datagrams are validated before state mutation
The receiver SHALL validate datagram length, protocol message type, negotiated endpoint, match id, remote seat, history bounds, and an allowed tick window before mutating rollback state. Invalid datagrams SHALL be ignored without throwing from the Unity frame loop or extending the valid peer's disconnect timer.

#### Scenario: Malformed datagram is ignored
- **WHEN** a datagram has a valid magic and input type but is shorter than the declared input payload
- **THEN** no input is stored, the peer timeout is not refreshed, and the frame loop continues

#### Scenario: Wrong peer cannot inject gameplay input
- **WHEN** an input datagram arrives from an endpoint other than the endpoint accepted during handshake
- **THEN** the datagram does not change remote input, acknowledgement, checksum, or connection liveness state

#### Scenario: Excessively future input is rejected
- **WHEN** a syntactically valid sample declares a tick beyond the configured future allowance
- **THEN** the sample is not written into the rollback ring buffer

## MODIFIED Requirements

### Requirement: LAN session connects two peers before tick zero
The game SHALL support a two-peer LAN match over UDP. One peer SHALL host and listen; the other SHALL join with the host address. Peers SHALL exchange protocol version and match settings and SHALL agree on seats (player one north, player two south) before simulation tick 0. `Join`, `JoinAck`, and phased `Ready` messages SHALL be retried until the next handshake state is confirmed or the handshake deadline expires. Duplicate or out-of-order handshake messages SHALL be handled idempotently. A version or settings mismatch SHALL abort without starting ticks.

#### Scenario: Host waits then starts
- **WHEN** a player hosts a LAN match with points-to-win 8 and marble count 1 AND a second player joins with a matching protocol version
- **THEN** both peers enter the same match with those settings AND tick 0 has not run before both peers have declared readiness

#### Scenario: Dropped join is retried
- **WHEN** the joiner's first `Join` datagram is dropped AND the handshake deadline has not expired
- **THEN** the joiner sends `Join` again AND the host can continue the same handshake when a retry arrives

#### Scenario: Dropped readiness response is retried
- **WHEN** both peers have declared readiness but a readiness response or confirmation is dropped
- **THEN** the appropriate readiness phase is sent again AND neither peer creates a second match or changes its negotiated seat

#### Scenario: Duplicate handshake message is idempotent
- **WHEN** the host receives the same valid `Join` more than once from the negotiated joiner
- **THEN** each response advertises the same match id, settings, and seat assignment

#### Scenario: Settings mismatch aborts
- **WHEN** the host's settings hash does not match the joiner's advertised settings
- **THEN** neither peer starts match ticks AND both return to a menu error state

### Requirement: Remote input is predicted then rolled back
Each peer SHALL advance the match simulation using confirmed local input for their seat and a prediction for the remote seat when the remote bits for that tick have not arrived. When later-arriving remote input for a past tick differs from the prediction, the peer SHALL restore simulation state at that tick and resimulate forward to the present. Prediction SHALL reuse movement and held-action state from the newest accepted remote tick, or neutral input if none exists. Prediction SHALL clear one-shot input edges such as `ShootPressed`, and an older out-of-order sample SHALL NOT replace the newest prediction source.

#### Scenario: Late remote bits correct the table
- **WHEN** a peer predicted remote boost-off for tick 40 AND then receives remote boost-on for tick 40
- **THEN** that peer restores state at tick 40 AND resimulates with boost-on AND the resulting state matches a baseline that had boost-on for tick 40 from the start

#### Scenario: Matching prediction does not rewind
- **WHEN** received remote input for a tick equals the predicted input for that tick
- **THEN** the peer does not restore an earlier snapshot solely because that packet arrived

#### Scenario: Shoot edge is not repeated by prediction
- **WHEN** the newest confirmed remote input contains `ShootPressed` AND the next remote tick has not arrived
- **THEN** the predicted input retains eligible held movement and boost state but has `ShootPressed` cleared

#### Scenario: Older input does not regress prediction
- **WHEN** tick 75 has already supplied the prediction source AND a reordered datagram later supplies tick 72
- **THEN** future unconfirmed ticks continue predicting from tick 75

### Requirement: Missing input older than the rollback window aborts
If a required remote input is still missing after redundant input history and handshake-valid gameplay traffic have had the opportunity to recover it, and the input becomes older than the rollback buffer, the session SHALL abort rather than invent inputs beyond the prediction window. Receipt of newer ticks or acknowledgement values SHALL NOT conceal the unresolved hole.

#### Scenario: Recoverable hole does not abort
- **WHEN** an input datagram is lost but the missing sample is recovered from a later history block before leaving the rollback buffer
- **THEN** the session continues without an input-hole failure

#### Scenario: Hole past buffer
- **WHEN** a peer cannot obtain remote input for a tick that has fallen out of the rollback buffer despite later valid gameplay traffic
- **THEN** the match ends AND the main menu or a disconnect failure is shown

#### Scenario: Newer isolated input does not conceal a hole
- **WHEN** a newer remote tick is received while an earlier required tick remains missing
- **THEN** contiguous acknowledgement does not cross the missing tick AND the unresolved hole still causes failure if it leaves the rollback window
