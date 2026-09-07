## MODIFIED Requirements

### Requirement: North and south pockets accept marbles only
North and south pockets SHALL detect scoring marbles that are not currently carried in a basket and SHALL physically block players from entering those pockets.

#### Scenario: Marble can score in a north or south pocket
- **WHEN** a free scoring marble's collider overlaps a north or south pocket trigger
- **THEN** that pocket registers a marble-goal event for the pocket's owner

#### Scenario: Carried marble does not score
- **WHEN** a carried marble overlaps a north or south pocket trigger
- **THEN** that pocket does not register a marble-goal event

#### Scenario: Player bounces off north or south pocket
- **WHEN** a player body contacts a north or south pocket
- **THEN** the player SHALL NOT enter the pocket and SHALL remain on the table
