## ADDED Requirements

### Requirement: Player body is larger on the table
Each player's 2D collision radius SHALL be 0.78 (1.5 times the previous radius of 0.52). The player mesh SHALL match that radius.

#### Scenario: Radius is enlarged
- **WHEN** a match arena is loaded
- **THEN** each player's circle collider world radius is 0.78

## MODIFIED Requirements

### Requirement: Players face their movement input
Each player's facing SHALL rotate toward that player's current movement input when input is non-zero, and SHALL keep the last facing when input is released. Facing SHALL NOT snap to a new 8-way heading in a single physics step when the heading change is 90 degrees.

#### Scenario: Player one turns toward east
- **WHEN** player one holds D while facing south
- **THEN** player one's facing rotates toward east AND is east within 0.25 seconds

#### Scenario: Facing holds when input stops
- **WHEN** a player was holding W AND then releases all movement keys
- **THEN** facing remains north
