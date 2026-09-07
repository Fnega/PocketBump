## ADDED Requirements

### Requirement: Collect and fire pulses
A successful basket collect (including a scoop steal) SHALL play a short world-space pulse at the new carrier's basket in that player's identity color. Firing a charged shot SHALL play a short world-space pulse at the launch point. Body bounce, a rejected second marble, and charge cancel SHALL NOT play those pulses.

#### Scenario: Collect pulse
- **WHEN** an empty basket interior overlaps a free marble and that player starts carrying it
- **THEN** a pulse is visible at that player's basket in that player's identity color

#### Scenario: Steal pulse
- **WHEN** player two's empty basket steals the marble from player one
- **THEN** a pulse is visible at player two's basket in player two's identity color

#### Scenario: Fire pulse
- **WHEN** a player completes the power stage and launches the marble
- **THEN** a pulse is visible at the launch point

#### Scenario: Bounce is silent
- **WHEN** a marble hits a player body and is not collected
- **THEN** no collect pulse plays

### Requirement: Charge readings stay outside the player body
Direction and power readings SHALL be anchored at that player's basket and SHALL extend forward of the basket. The shortest power reading SHALL still be visible outside the player's body silhouette.

#### Scenario: Direction reading clears the body
- **WHEN** a carrying player is in the direction stage
- **THEN** the direction indicator is visible at the basket and is not contained inside the player mesh

#### Scenario: Minimum power reading clears the body
- **WHEN** a carrying player enters the power stage at the lowest power reading
- **THEN** the power indicator is still visible outside the player mesh

## MODIFIED Requirements

### Requirement: Basket and charge readings are visible
Each player's basket SHALL use that player's identity color. While a player is in the direction stage of a charge, a direction reading SHALL be visible on that player at the basket, using that player's identity color. While a player is in the power stage, a power reading SHALL be visible using a high-contrast treatment distinct from identity cyan/orange, gate yellow, and dunk red, AND the locked direction reading SHALL remain visible. Those readings SHALL not remain after the charge ends.

#### Scenario: Basket identity
- **WHEN** both players are on the table
- **THEN** player one's basket uses the cyan identity AND player two's basket uses the orange identity

#### Scenario: Direction reading appears
- **WHEN** a carrying player is in the direction stage
- **THEN** a direction indicator in that player's identity color is visible at that player's basket

#### Scenario: Power reading appears with locked direction
- **WHEN** a carrying player is in the power stage
- **THEN** a high-contrast power indicator is visible at that player's basket AND the locked direction indicator remains visible

#### Scenario: Readings clear after fire or cancel
- **WHEN** a charge fires or is cancelled
- **THEN** the direction and power indicators for that player are no longer shown
