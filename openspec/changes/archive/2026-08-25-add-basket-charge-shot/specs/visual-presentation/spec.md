## ADDED Requirements

### Requirement: Basket and charge readings are visible
Each player's basket SHALL use that player's identity color. While a player is in the direction stage of a charge, a direction reading SHALL be visible on that player. While a player is in the power stage, a power reading SHALL be visible on that player. Those readings SHALL not remain after the charge ends.

#### Scenario: Basket identity
- **WHEN** both players are on the table
- **THEN** player one's basket uses the cyan identity AND player two's basket uses the orange identity

#### Scenario: Direction reading appears
- **WHEN** a carrying player is in the direction stage
- **THEN** a direction indicator is visible at that player

#### Scenario: Power reading appears
- **WHEN** a carrying player is in the power stage
- **THEN** a power indicator is visible at that player

#### Scenario: Readings clear after fire or cancel
- **WHEN** a charge fires or is cancelled
- **THEN** the direction and power indicators for that player are no longer shown
