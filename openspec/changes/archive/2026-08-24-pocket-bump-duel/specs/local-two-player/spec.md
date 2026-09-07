## ADDED Requirements

### Requirement: Two players share one machine
The game SHALL support exactly two human players on one machine in the same view, with no network session required for a match.

#### Scenario: Both players present at match start
- **WHEN** a match starts
- **THEN** two distinct player bodies exist on the table and both accept local input

### Requirement: Separate movement controls
Player one SHALL move with WASD. Player two SHALL move with the arrow keys. Movement SHALL be applied through 2D physics bodies so collisions can transfer impulse to marbles and to the other player. Players SHALL NOT have a jump action.

#### Scenario: Player one input
- **WHEN** player one holds W
- **THEN** player one's body accelerates toward the north side of the table

#### Scenario: Player two input
- **WHEN** player two holds the down arrow
- **THEN** player two's body accelerates toward the south side of the table

#### Scenario: Collision can dunk
- **WHEN** player one collides with player two with enough impulse toward an east or west pocket AND the round is live
- **THEN** player two can enter that dunk pocket as a result of physics (no special dunk button)

### Requirement: Shared camera shows the whole table
The system SHALL keep a single camera that shows the full table, both pockets on the vertical axis, both dunk pockets, both players, and the score.

#### Scenario: No split screen
- **WHEN** a match is running
- **THEN** both players are visible in one camera view

### Requirement: HUD shows score, countdown, and match result
The HUD SHALL show both scores at all times during a match, SHALL show the remaining kickoff countdown while the gate is closed, and SHALL show a winner when the match ends.

#### Scenario: Countdown visible
- **WHEN** kickoff countdown is running
- **THEN** the HUD displays the remaining seconds

#### Scenario: Winner visible
- **WHEN** a player wins the match
- **THEN** the HUD identifies that player as the winner and further rounds do not start until a new match is requested
