# Arena Pockets

## Purpose

Defines the physical arena layout, pocket filtering, center gate, and marble spawning behavior for the pocket-bump duel.

## Requirements

### Requirement: Table has four pockets and a center gate
The arena SHALL be a 2D table with a north marble pocket, a south marble pocket, an east dunk pocket, a west dunk pocket, and a horizontal center-line gate that can block crossing between the north and south halves.

#### Scenario: Layout is readable
- **WHEN** a match arena is loaded
- **THEN** north and south pockets sit on the vertical ends, east and west pockets sit on the horizontal sides, and the gate lies on the horizontal midline

### Requirement: North and south pockets accept marbles only
North and south pockets SHALL detect scoring marbles that are not currently carried in a basket and SHALL physically block players from entering those pockets.

#### Scenario: Marble can score in a north or south pocket
- **WHEN** a free scoring marble overlaps a north or south pocket well in the simulation
- **THEN** that pocket registers a marble-goal event for the pocket's owner

#### Scenario: Carried marble does not score
- **WHEN** a carried marble overlaps a north or south pocket well
- **THEN** that pocket does not register a marble-goal event

#### Scenario: Player bounces off north or south pocket
- **WHEN** a player body contacts a north or south pocket
- **THEN** the player SHALL NOT enter the pocket and SHALL remain on the table

### Requirement: East and west pockets accept players only
East and west pockets SHALL detect players and SHALL physically block scoring marbles from entering those pockets.

#### Scenario: Player can fall in an east or west pocket
- **WHEN** a player body overlaps an east or west pocket well in the simulation
- **THEN** that pocket registers a dunk event for that player

#### Scenario: Marble bounces off east or west pocket
- **WHEN** a scoring marble contacts an east or west pocket
- **THEN** the marble SHALL NOT enter the pocket and SHALL remain on the table

### Requirement: Pocket ownership is fixed per match
The north pocket SHALL belong to player one. The south pocket SHALL belong to player two. East and west pockets SHALL be shared dunk pockets with no owner.

#### Scenario: Owners do not swap mid-match
- **WHEN** a match is in progress
- **THEN** north remains player one's pocket and south remains player two's pocket

### Requirement: Center gate blocks crossing until opened
While the center gate is closed, player colliders SHALL NOT cross from one half into the other. While the gate is open, players and marbles SHALL be able to cross the midline.

#### Scenario: Closed gate keeps players in their half
- **WHEN** the center gate is closed AND a player tries to cross the midline
- **THEN** the player remains in their current half

#### Scenario: Open gate allows crossing
- **WHEN** the center gate is open
- **THEN** players and marbles can move across the midline

### Requirement: Marbles spawn at rest after the gate opens
Scoring marbles SHALL spawn only after the center gate has opened for that round. Spawned marbles SHALL have zero initial velocity. A single marble SHALL spawn at the table center. Additional marbles SHALL spawn in a non-overlapping cluster around the center.

#### Scenario: Default single marble
- **WHEN** the round goes live with marble count 1
- **THEN** one marble appears at the table center with zero velocity

#### Scenario: Cluster for multiple marbles
- **WHEN** the round goes live with marble count greater than 1
- **THEN** that many marbles appear around the center without overlapping colliders and with zero velocity

#### Scenario: No marbles during countdown
- **WHEN** the kickoff countdown is running
- **THEN** no scoring marbles exist on the table

### Requirement: High-speed marbles can knock players back
A marble SHALL apply additional controlled knockback to a player when its impact speed reaches the dangerous threshold. Knockback SHALL increase with relative impact speed and SHALL follow the impact direction. Low-speed marble contact SHALL retain ordinary simulated collision without dangerous knockback.

#### Scenario: Slow marble contact remains ordinary
- **WHEN** a marble contacts a player below the dangerous speed threshold
- **THEN** the contact uses normal simulated collision and does not add dangerous knockback

#### Scenario: Dangerous marble knocks a player
- **WHEN** a marble contacts a player above the dangerous speed threshold
- **THEN** the player receives additional impulse in the impact direction proportional to relative impact speed

#### Scenario: Dangerous marble can cause a dunk
- **WHEN** a dangerous marble knocks a player toward an east or west pocket with sufficient impulse during a live round
- **THEN** the player can enter that dunk pocket as a result of the marble collision

### Requirement: Table extents are 32 by 48
The playable table SHALL measure 32 units wide by 48 units high. Player radius SHALL remain 0.78, marble radius 0.3, wall thickness 0.45, and north/south marble-pocket opening width 3.4. East/west dunk-pocket opening height SHALL be 6.45 (three times the previous height of 2.15). Simulated dunk wells, marble blockers, wall gaps, and the visible dunk meshes SHALL use that same height.

#### Scenario: Table is twice as long and twice as wide
- **WHEN** a match arena is loaded
- **THEN** the interior playable area is 32 units across the east-west axis and 48 units across the north-south axis

#### Scenario: Pocket openings
- **WHEN** the north marble pocket and an east dunk pocket are measured
- **THEN** the north opening width is still 3.4 units AND the east opening height is 6.45 units

### Requirement: Marbles coast to rest
Scoring marbles SHALL NOT gain speed from a per-tick scripted acceleration. After a marble is moving, if it is left without further collisions that add energy, it SHALL lose speed and come to rest.

#### Scenario: Isolated marble stops
- **WHEN** a marble is given a moderate initial speed and no other bodies touch it
- **THEN** its speed falls below a rest threshold within a few seconds and remains there

#### Scenario: No scripted runaway
- **WHEN** a marble is moving in open table with no collisions
- **THEN** its speed does not increase from simulation tick to simulation tick due to gameplay scripting

### Requirement: Dangerous impacts transfer marble energy
After a marble applies dangerous knockback to a player, the marble SHALL lose a configured fraction of its speed. The same continuous contact SHALL NOT apply the additional knockback repeatedly before a short collision cooldown expires.

#### Scenario: Marble slows after transferring energy
- **WHEN** a dangerous marble applies knockback to a player
- **THEN** the marble's speed is reduced while preserving its post-collision travel direction

#### Scenario: One contact does not stack impulses
- **WHEN** a dangerous marble remains in contact with the same player across multiple simulation ticks within the collision cooldown
- **THEN** additional dangerous knockback is applied at most once during that cooldown
