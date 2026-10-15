## ADDED Requirements

### Requirement: Network failures pause with visible diagnostic evidence
When a LAN session aborts for an input hole, peer timeout, desync, invalid setup, or goodbye, the game SHALL retain a bounded local diagnostic snapshot, freeze the Arena, and show a top-right problem panel containing the exact terminal reason and an explicit action to return to the main menu.

#### Scenario: Input hole pauses the match
- **WHEN** a missing remote input leaves the rollback buffer and the session aborts
- **THEN** the retained snapshot identifies `INPUT HOLE` and includes the current tick and last accepted remote input state AND the Arena remains visible and paused with `INPUT HOLE` shown in the top-right problem panel

#### Scenario: Player leaves a paused network match
- **WHEN** a network problem panel is visible and the player selects Main Menu
- **THEN** normal game time is restored AND the main menu is loaded

### Requirement: Desync checks use corrected confirmed history
The rollback session SHALL refresh snapshots and checksums for every replayed Tick.  A received checksum SHALL be compared only after local rollback processing is complete and remote input is contiguous through the referenced Tick.

#### Scenario: Late input corrects a predicted Tick
- **WHEN** a peer receives a checksum and late input for a Tick that was previously predicted
- **THEN** it replays and records the corrected Tick before comparison AND matching corrected states do not report `DESYNC`

#### Scenario: Confirmed corrected states differ
- **WHEN** both peers have confirmed input through the checksum Tick and their corrected checksums differ
- **THEN** the session reports `DESYNC`
