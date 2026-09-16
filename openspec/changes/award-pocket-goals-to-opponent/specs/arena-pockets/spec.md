## MODIFIED Requirements

### Requirement: North and south pockets accept marbles only
North and south pockets SHALL detect scoring marbles that are not currently carried in a basket, SHALL identify the owning player as having conceded the goal, SHALL award the point to that owner's opponent, and SHALL physically block players from entering those pockets.

#### Scenario: Free marble scores against the north pocket owner
- **WHEN** a free scoring marble overlaps player one's north pocket well in the simulation
- **THEN** the pocket registers a marble goal conceded by player one AND player two is identified as the scorer

#### Scenario: Free marble scores against the south pocket owner
- **WHEN** a free scoring marble overlaps player two's south pocket well in the simulation
- **THEN** the pocket registers a marble goal conceded by player two AND player one is identified as the scorer

#### Scenario: Carried marble does not score
- **WHEN** a carried marble overlaps a north or south pocket well
- **THEN** that pocket does not register a marble-goal event

#### Scenario: Player bounces off north or south pocket
- **WHEN** a player body contacts a north or south pocket
- **THEN** the player SHALL NOT enter the pocket and SHALL remain on the table
