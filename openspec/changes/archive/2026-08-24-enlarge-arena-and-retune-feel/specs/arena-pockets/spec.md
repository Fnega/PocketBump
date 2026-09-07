## ADDED Requirements

### Requirement: Table extents are 32 by 48
The playable table SHALL measure 32 units wide by 48 units high. Player radius, marble radius, wall thickness, north/south marble-pocket opening width, and east/west dunk-pocket opening height SHALL remain at their previous sizes.

#### Scenario: Table is twice as long and twice as wide
- **WHEN** a match arena is loaded
- **THEN** the interior playable area is 32 units across the east-west axis and 48 units across the north-south axis

#### Scenario: Pocket openings are unchanged
- **WHEN** the north marble pocket and an east dunk pocket are measured
- **THEN** the north opening width is still 3.4 units AND the east opening height is still 2.15 units

### Requirement: Marbles coast to rest
Scoring marbles SHALL NOT gain speed from a per-tick scripted acceleration. After a marble is moving, if it is left without further collisions that add energy, it SHALL lose speed and come to rest.

#### Scenario: Isolated marble stops
- **WHEN** a marble is given a moderate initial speed and no other bodies touch it
- **THEN** its speed falls below a rest threshold within a few seconds and remains there

#### Scenario: No scripted runaway
- **WHEN** a marble is moving in open table with no collisions
- **THEN** its speed does not increase from physics tick to physics tick due to gameplay scripting
