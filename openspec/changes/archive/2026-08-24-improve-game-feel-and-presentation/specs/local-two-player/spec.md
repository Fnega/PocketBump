## MODIFIED Requirements

### Requirement: Separate movement controls
Player one SHALL move with WASD and SHALL boost with Left Shift. Player two SHALL move with the arrow keys and SHALL boost with Right Shift. Movement SHALL be applied through 2D physics bodies with acceleration, momentum, coasting, braking, and reduced reversal authority so collisions can transfer impulse to marbles and to the other player. Players SHALL NOT have a jump action.

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

## ADDED Requirements

### Requirement: Each player has an independent hold-to-boost control
Each player SHALL be able to hold their own boost key to enter a wind-up and then a boosted movement state. Boost SHALL increase acceleration and maximum speed, SHALL reduce steering authority, and SHALL return to normal movement after release and a short recovery period. One player's boost input SHALL NOT activate the other player's boost.

#### Scenario: Player one boosts independently
- **WHEN** player one holds Left Shift through the boost wind-up while player two does not hold Right Shift
- **THEN** player one enters boosted movement AND player two remains in normal movement

#### Scenario: Player two boosts independently
- **WHEN** player two holds Right Shift through the boost wind-up while player one does not hold Left Shift
- **THEN** player two enters boosted movement AND player one remains in normal movement

#### Scenario: Boost has a steering trade-off
- **WHEN** a boosting player applies input perpendicular to their current high-speed velocity
- **THEN** their velocity turns more slowly than it would during normal movement

#### Scenario: Releasing boost returns to normal limits
- **WHEN** a boosting player releases their boost key
- **THEN** the player exits boost, completes a short recovery period, and uses normal speed and steering limits
