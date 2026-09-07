# Local Two Player

## Purpose

Defines local two-player controls, shared camera behavior, match HUD feedback, and that local matches use the same deterministic tick simulation as networked matches.

## Requirements

### Requirement: Two players share one machine
The game SHALL support a local match of exactly two human players on one machine in the same view, with no network session required. A local match SHALL step the same deterministic tick simulation used by networked matches. Local matches SHALL NOT require a LAN host or join.

#### Scenario: Both players present at match start
- **WHEN** a match starts
- **THEN** two distinct player bodies exist on the table and both accept local input

#### Scenario: Local match needs no session
- **WHEN** the user starts a local match from rule setup
- **THEN** the match runs without exchanging packets AND both seats are sampled on that machine

### Requirement: Player body is larger on the table
Each player's 2D collision radius SHALL be 0.78 (1.5 times the previous radius of 0.52). The player mesh SHALL match that radius.

#### Scenario: Radius is enlarged
- **WHEN** a match arena is loaded
- **THEN** each player's circle collider world radius is 0.78

### Requirement: Separate movement controls
Player one SHALL move with WASD. Player two SHALL move with the arrow keys. Player one SHALL boost with the current boost binding (default K). Player two SHALL boost with the current boost binding (default Alpha2 / top-row 2). Movement SHALL be applied through the shared tick simulation with acceleration, momentum, coasting, braking, and reduced reversal authority so collisions can transfer impulse to marbles and to the other player. Players SHALL NOT have a jump action.

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

### Requirement: Players face their movement input
Each player's facing SHALL rotate toward that player's current movement input when input is non-zero, and SHALL keep the last facing when input is released. Facing SHALL NOT snap to a new 8-way heading in a single physics step when the heading change is 90 degrees.

#### Scenario: Player one turns toward east
- **WHEN** player one holds D while facing south
- **THEN** player one's facing rotates toward east AND is east within 0.25 seconds

#### Scenario: Facing holds when input stops
- **WHEN** a player was holding W AND then releases all movement keys
- **THEN** facing remains north

### Requirement: Player turning matches forward acceleration
When a player's input is not aligned with current velocity, steering SHALL use the same acceleration magnitude as aligned forward acceleration. Boosted movement SHALL raise max speed and forward acceleration and SHALL NOT reduce steering acceleration relative to normal movement.

#### Scenario: Perpendicular turn uses full acceleration
- **WHEN** a player at normal max speed applies input perpendicular to current velocity
- **THEN** velocity changes toward that heading at the same acceleration used for aligned forward movement

#### Scenario: Boost does not slow steering
- **WHEN** a boosting player applies input perpendicular to current velocity
- **THEN** steering acceleration is not lower than the same turn during normal (non-boost) movement

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

### Requirement: Each player has a shoot key
Player one's default shoot key SHALL be J. Player two's default shoot key SHALL be Alpha1 (top-row 1). Shoot SHALL be read from the current binding table so a rebound key is used in play. One player's shoot key SHALL NOT activate the other player's shot.

#### Scenario: Player one shoot is independent
- **WHEN** player one presses J while carrying a marble AND player two does not press their shoot key
- **THEN** only player one starts or advances a charge

#### Scenario: Rebound shoot key is used
- **WHEN** player one's shoot binding is Space AND player one presses Space while carrying a marble
- **THEN** player one starts or advances a charge

### Requirement: Shared camera shows the whole table
The system SHALL keep a single aiming top-down camera that looks straight at the table (no isometric tilt) and shows the full 32×48 table, both pockets on the vertical axis, both dunk pockets, both players, and the score. Screen-up SHALL correspond to table north so movement and basket facing match the view.

#### Scenario: No split screen
- **WHEN** a match is running
- **THEN** both players are visible in one camera view

#### Scenario: Camera frames the enlarged table
- **WHEN** a match arena is loaded
- **THEN** north and south walls and east and west walls are all visible in the single camera view

#### Scenario: Aiming top-down is untilted
- **WHEN** a match arena is loaded
- **THEN** the camera looks straight at the table center AND is not pitched or yawed into an isometric angle

#### Scenario: Screen up is north
- **WHEN** player one holds W from rest during a match
- **THEN** player one accelerates toward the top of the screen (north)

### Requirement: HUD shows score, countdown, and match result
The HUD SHALL show both scores at all times during a match, SHALL show the remaining kickoff countdown while the gate is closed, and SHALL show a winner when the match ends.

#### Scenario: Countdown visible
- **WHEN** kickoff countdown is running
- **THEN** the HUD displays the remaining seconds

#### Scenario: Winner visible
- **WHEN** a player wins the match
- **THEN** the HUD identifies that player as the winner and further rounds do not start until a new match is requested

### Requirement: Local and networked matches share one simulation
Whether the match is local or LAN, player motion, marble motion, basket overlap, pockets, and scoring SHALL be advanced only by the deterministic match tick stepper. Unity Physics2D SHALL NOT be the source of truth for those gameplay facts.

#### Scenario: Same input tape matches
- **WHEN** two isolated simulations start from the same kickoff state AND consume the same pair of per-tick inputs
- **THEN** after N ticks their scores, carry state, and body poses checksum equal
