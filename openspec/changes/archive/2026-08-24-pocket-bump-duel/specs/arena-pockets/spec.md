## ADDED Requirements

### Requirement: Table has four pockets and a center gate
The arena SHALL be a 2D table with a north marble pocket, a south marble pocket, an east dunk pocket, a west dunk pocket, and a horizontal center-line gate that can block crossing between the north and south halves.

#### Scenario: Layout is readable
- **WHEN** a match arena is loaded
- **THEN** north and south pockets sit on the vertical ends, east and west pockets sit on the horizontal sides, and the gate lies on the horizontal midline

### Requirement: North and south pockets accept marbles only
North and south pockets SHALL detect scoring marbles and SHALL physically block players from entering those pockets.

#### Scenario: Marble can score in a north or south pocket
- **WHEN** a scoring marble's collider overlaps a north or south pocket trigger
- **THEN** that pocket registers a marble-goal event for the pocket's owner

#### Scenario: Player bounces off north or south pocket
- **WHEN** a player body contacts a north or south pocket
- **THEN** the player SHALL NOT enter the pocket and SHALL remain on the table

### Requirement: East and west pockets accept players only
East and west pockets SHALL detect players and SHALL physically block scoring marbles from entering those pockets.

#### Scenario: Player can fall in an east or west pocket
- **WHEN** a player body overlaps an east or west pocket trigger
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
