## ADDED Requirements

### Requirement: LAN session connects two peers before tick zero
The game SHALL support a two-peer LAN match over UDP. One peer SHALL host and listen; the other SHALL join with the host address. Peers SHALL exchange protocol version and match settings and SHALL agree on seats (player one north, player two south) before simulation tick 0. A version or settings mismatch SHALL abort without starting ticks.

#### Scenario: Host waits then starts
- **WHEN** a player hosts a LAN match with points-to-win 8 and marble count 1 AND a second player joins with a matching protocol version
- **THEN** both peers enter the same match with those settings AND tick 0 has not run before both are ready

#### Scenario: Settings mismatch aborts
- **WHEN** the host's settings hash does not match the joiner's advertised settings
- **THEN** neither peer starts match ticks AND both return to a menu error state

### Requirement: Each peer drives only their seat
In a networked match, each peer SHALL sample input only for the seat they were assigned. That seat SHALL use the machine's local movement and skill bindings for player one (defaults WASD, shoot J, boost K) unless rebound on that machine. The remote seat SHALL be driven only by inputs received (or predicted) for that seat.

#### Scenario: Host is player one
- **WHEN** the host is assigned seat 0 AND holds W
- **THEN** only player one's simulated body accelerates north from that input AND player two is not driven by the host's WASD

#### Scenario: Joiner uses local WASD for seat two
- **WHEN** the joiner is assigned seat 1 AND holds W on their machine
- **THEN** player two's simulated body accelerates north AND the joiner's arrow-key bindings are not required

### Requirement: Remote input is predicted then rolled back
Each peer SHALL advance the match simulation using confirmed local input for their seat and a prediction for the remote seat when the remote bits for that tick have not arrived. When later-arriving remote input for a past tick differs from the prediction, the peer SHALL restore simulation state at that tick and resimulate forward to the present. Prediction SHALL reuse the last confirmed remote input, or neutral input if none exists.

#### Scenario: Late remote bits correct the table
- **WHEN** a peer predicted remote boost-off for tick 40 AND then receives remote boost-on for tick 40
- **THEN** that peer restores state at tick 40 AND resimulates with boost-on AND the resulting state matches a baseline that had boost-on for tick 40 from the start

#### Scenario: Matching prediction does not rewind
- **WHEN** received remote input for a tick equals the predicted input for that tick
- **THEN** the peer does not restore an earlier snapshot solely because that packet arrived

### Requirement: Both seats share the same input delay
Networked matches SHALL apply the same fixed input delay (default 2 simulation ticks) to both seats so neither peer's inputs take effect earlier than the other's.

#### Scenario: Local press is delayed equally
- **WHEN** a networked player presses a movement key on tick N
- **THEN** that input is applied in the simulation at tick N plus the configured delay AND the remote seat uses the same delay

### Requirement: Checksum mismatch ends the session
Peers SHALL exchange a checksum of simulation state for corresponding ticks. If two peers disagree on the checksum for the same tick, both SHALL stop simulating that match and SHALL show a desync failure rather than continue with divergent tables.

#### Scenario: Divergent state aborts
- **WHEN** both peers have completed tick 100 AND their checksums for tick 100 differ
- **THEN** the match does not continue to tick 101 as a playable round AND a desync failure is shown

### Requirement: Disconnect returns to menu
If a peer sends a goodbye, or no packets arrive within the disconnect timeout, the remaining peer SHALL end the networked match and return to the main menu without host migration.

#### Scenario: Silent peer times out
- **WHEN** a networked match is running AND the other peer sends no packets for the disconnect timeout
- **THEN** the local peer leaves the match AND the main menu is shown

### Requirement: Missing input older than the rollback window aborts
If a required remote input is still missing after it is older than the rollback buffer, the session SHALL abort rather than invent inputs beyond the prediction window.

#### Scenario: Hole past buffer
- **WHEN** a peer cannot obtain remote input for a tick that has fallen out of the rollback buffer
- **THEN** the match ends AND the main menu or a disconnect failure is shown
