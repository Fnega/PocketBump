## ADDED Requirements

### Requirement: Players face their movement input
Each player's basket facing SHALL follow that player's current movement input when input is non-zero, and SHALL keep the last facing when input is released.

#### Scenario: Player one faces east
- **WHEN** player one holds D
- **THEN** player one's facing is east

### Requirement: Each player has a shoot key
Player one's default shoot key SHALL be J. Player two's default shoot key SHALL be Alpha1 (top-row 1). Shoot SHALL be read from the current binding table so a rebound key is used in play. One player's shoot key SHALL NOT activate the other player's shot.

#### Scenario: Player one shoot is independent
- **WHEN** player one presses J while carrying a marble AND player two does not press their shoot key
- **THEN** only player one starts or advances a charge

#### Scenario: Rebound shoot key is used
- **WHEN** player one's shoot binding is Space AND player one presses Space while carrying a marble
- **THEN** player one starts or advances a charge

## MODIFIED Requirements

### Requirement: Separate movement controls
Player one SHALL move with WASD. Player two SHALL move with the arrow keys. Player one SHALL boost with the current boost binding (default K). Player two SHALL boost with the current boost binding (default Alpha2 / top-row 2). Movement SHALL be applied through 2D physics bodies with acceleration, momentum, coasting, braking, and reduced reversal authority so collisions can transfer impulse to marbles and to the other player. Players SHALL NOT have a jump action.

#### Scenario: Player one input accelerates with inertia
- **WHEN** player one holds W from rest
- **THEN** player one's body accelerates toward the north side over time rather than instantly reaching maximum speed

#### Scenario: Player two input accelerates with inertia
- **WHEN** player two holds the down arrow from rest
- **THEN** player two's body accelerates toward the south side over time rather than instantly reaching maximum speed

#### Scenario: Releasing movement coasts then brakes
- **WHEN** a moving player releases all movement keys
- **THEN** the body retains momentum briefly and decelerates toward rest

#### Scenario: Reversing direction preserves momentum
- **WHEN** a player moving north applies southward input
- **THEN** the player slows and reverses over time rather than changing velocity direction instantly

#### Scenario: Collision can dunk
- **WHEN** player one collides with player two with enough impulse toward an east or west pocket AND the round is live
- **THEN** player two can enter that dunk pocket as a result of physics (no special dunk button)

### Requirement: Each player has an independent hold-to-boost control
Each player SHALL be able to hold their own boost key to enter a wind-up and then a boosted movement state. Default boost keys SHALL be K for player one and Alpha2 for player two. Boost SHALL increase acceleration and maximum speed and SHALL return to normal movement after release and a short recovery period. Boost SHALL NOT reduce steering acceleration. One player's boost input SHALL NOT activate the other player's boost. Boost SHALL be read from the current binding table.

#### Scenario: Player one boosts independently
- **WHEN** player one holds K through the boost wind-up while player two does not hold Alpha2
- **THEN** player one enters boosted movement AND player two remains in normal movement

#### Scenario: Player two boosts independently
- **WHEN** player two holds Alpha2 through the boost wind-up while player one does not hold K
- **THEN** player two enters boosted movement AND player one remains in normal movement

#### Scenario: Boost keeps steering authority
- **WHEN** a boosting player applies input perpendicular to their current high-speed velocity
- **THEN** their steering acceleration is not reduced compared with the same turn during normal movement

#### Scenario: Releasing boost returns to normal limits
- **WHEN** a boosting player releases their boost key
- **THEN** the player exits boost, completes a short recovery period, and uses normal speed limits
