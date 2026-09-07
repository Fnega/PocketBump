## MODIFIED Requirements

### Requirement: Two players share one machine
The game SHALL support a local match of exactly two human players on one machine in the same view, with no network session required. A local match SHALL step the same deterministic tick simulation used by networked matches. Local matches SHALL NOT require a LAN host or join.

#### Scenario: Both players present at match start
- **WHEN** a match starts
- **THEN** two distinct player bodies exist on the table and both accept local input

#### Scenario: Local match needs no session
- **WHEN** the user starts a local match from rule setup
- **THEN** the match runs without exchanging packets AND both seats are sampled on that machine

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

## ADDED Requirements

### Requirement: Local and networked matches share one simulation
Whether the match is local or LAN, player motion, marble motion, basket overlap, pockets, and scoring SHALL be advanced only by the deterministic match tick stepper. Unity Physics2D SHALL NOT be the source of truth for those gameplay facts.

#### Scenario: Same input tape matches
- **WHEN** two isolated simulations start from the same kickoff state AND consume the same pair of per-tick inputs
- **THEN** after N ticks their scores, carry state, and body poses checksum equal
