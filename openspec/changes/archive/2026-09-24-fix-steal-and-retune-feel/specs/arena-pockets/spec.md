## MODIFIED Requirements

### Requirement: Table extents are 32 by 48
The playable table SHALL measure 32 units wide by 48 units high. Player radius SHALL remain 0.78, marble radius 0.3, wall thickness 0.45, and north/south marble-pocket opening width 3.4. East/west dunk-pocket opening height SHALL be 6.45 (three times the previous height of 2.15). Simulated dunk wells, marble blockers, wall gaps, and the visible dunk meshes SHALL use that same height.

#### Scenario: Table is twice as long and twice as wide
- **WHEN** a match arena is loaded
- **THEN** the interior playable area is 32 units across the east-west axis and 48 units across the north-south axis

#### Scenario: Pocket openings
- **WHEN** the north marble pocket and an east dunk pocket are measured
- **THEN** the north opening width is still 3.4 units AND the east opening height is 6.45 units
