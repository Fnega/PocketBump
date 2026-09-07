## MODIFIED Requirements

### Requirement: Each player has a front basket that follows movement input
Each player SHALL have a square basket mounted in front of their body at an offset that stays outside the player radius (1.08 for radius 0.78). While movement input is non-zero, the basket SHALL rotate toward that input direction rather than snapping. While movement input is zero, the basket SHALL keep its last facing. Player one SHALL spawn facing south. Player two SHALL spawn facing north.

#### Scenario: Basket turns toward input
- **WHEN** player one holds D while facing south
- **THEN** player one's basket rotates toward east AND faces east within 0.25 seconds

#### Scenario: Facing holds after release
- **WHEN** a player was holding W AND then releases all movement keys
- **THEN** the basket continues to face north

#### Scenario: Spawn facings
- **WHEN** a kickoff places both players
- **THEN** player one's basket faces south AND player two's basket faces north
