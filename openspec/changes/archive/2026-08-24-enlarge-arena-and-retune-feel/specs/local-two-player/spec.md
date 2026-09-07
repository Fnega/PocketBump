## ADDED Requirements

### Requirement: Player turning matches forward acceleration
When a player's input is not aligned with current velocity, steering SHALL use the same acceleration magnitude as aligned forward acceleration. Boosted movement SHALL raise max speed and forward acceleration and SHALL NOT reduce steering acceleration relative to normal movement.

#### Scenario: Perpendicular turn uses full acceleration
- **WHEN** a player at normal max speed applies input perpendicular to current velocity
- **THEN** velocity changes toward that heading at the same acceleration used for aligned forward movement

#### Scenario: Boost does not slow steering
- **WHEN** a boosting player applies input perpendicular to current velocity
- **THEN** steering acceleration is not lower than the same turn during normal (non-boost) movement

## MODIFIED Requirements

### Requirement: Each player has an independent hold-to-boost control
Each player SHALL be able to hold their own boost key to enter a wind-up and then a boosted movement state. Boost SHALL increase acceleration and maximum speed and SHALL return to normal movement after release and a short recovery period. Boost SHALL NOT reduce steering acceleration. One player's boost input SHALL NOT activate the other player's boost.

#### Scenario: Player one boosts independently
- **WHEN** player one holds Left Shift through the boost wind-up while player two does not hold Right Shift
- **THEN** player one enters boosted movement AND player two remains in normal movement

#### Scenario: Player two boosts independently
- **WHEN** player two holds Right Shift through the boost wind-up while player one does not hold Left Shift
- **THEN** player two enters boosted movement AND player one remains in normal movement

#### Scenario: Boost keeps steering authority
- **WHEN** a boosting player applies input perpendicular to their current high-speed velocity
- **THEN** their steering acceleration is not reduced compared with the same turn during normal movement

#### Scenario: Releasing boost returns to normal limits
- **WHEN** a boosting player releases their boost key
- **THEN** the player exits boost, completes a short recovery period, and uses normal speed limits

### Requirement: Shared camera shows the whole table
The system SHALL keep a single camera that shows the full 32×48 table, both pockets on the vertical axis, both dunk pockets, both players, and the score.

#### Scenario: No split screen
- **WHEN** a match is running
- **THEN** both players are visible in one camera view

#### Scenario: Camera frames the enlarged table
- **WHEN** a match arena is loaded
- **THEN** north and south walls and east and west walls are all visible in the single camera view
