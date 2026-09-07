## MODIFIED Requirements

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
