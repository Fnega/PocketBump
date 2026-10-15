## ADDED Requirements

### Requirement: North-up whole-arena minimap belongs to each local viewport
The HUD SHALL show a noninteractive, aspect-preserving whole-arena minimap at the top-right of each local split-screen viewport and at the top-right of the assigned local LAN viewport. World north SHALL remain up regardless of player facing. Both defended goals and pillar/wall outlines SHALL be identifiable, and the map SHALL remain distinct from the score, status, pickup explanations, and bottom-right network panel at supported sizes.

#### Scenario: Local split views
- **WHEN** a local match is displayed at 1280×720, 1920×1080 or 1024×768
- **THEN** each half has its own contained top-right minimap showing the same arena without crossing the divider or overlapping its status/pickup text

#### Scenario: LAN assigned seat
- **WHEN** either host or joiner enters a LAN match
- **THEN** exactly one minimap is shown, north-up, with the local player identified correctly for that negotiated seat

### Requirement: Minimap markers represent current simulation only
The minimap SHALL show both living players' ground positions and facings using cyan/orange arrows, and every active scoring ball using distinct white markers. Dead players and inactive balls SHALL NOT appear as active markers. Broken walls SHALL NOT be depicted as solid, and pending repair SHALL be distinct from an intact wall. Marker rendering SHALL read the current reconciled simulation and SHALL NOT alter ticks, input, physics, PRNG, checksums or networking.

#### Scenario: Multiple balls and opposing directions
- **WHEN** up to eight scoring balls are active and the two players face different directions
- **THEN** all active balls and both correctly oriented player arrows map to their current ground coordinates with uniform map scale

#### Scenario: Discontinuities and corrected walls
- **WHEN** rollback, teleport, death, respawn or a new match epoch changes entity state
- **THEN** markers and wall appearance update to that state without stale entities or changes to the simulation checksum

#### Scenario: Paused match
- **WHEN** a network waiting or terminal fault indication appears
- **THEN** the minimap remains at top-right, the network indication appears bottom-right, and entity positions stop advancing with the paused simulation

### Requirement: Minimap is an authored bounded overlay
The formal Arena scene SHALL contain inspectable minimap UI with editable presentation references. Incremental addition SHALL preserve unrelated authored models/materials/UI. The map SHALL use bounded drawing based on existing entity capacities and SHALL NOT add a gameplay camera, AudioListener, collider or match simulation.

#### Scenario: Saved presentation survives
- **WHEN** the author saves supported minimap styling and reopens the scene or enters/exits Play
- **THEN** the serialized choice persists and initialization does not recreate the entire HUD
