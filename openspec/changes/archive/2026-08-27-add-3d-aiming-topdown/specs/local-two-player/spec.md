## MODIFIED Requirements

### Requirement: Shared camera shows the whole table
The system SHALL keep a single aiming top-down camera that looks straight at the table (no isometric tilt) and shows the full 32×48 table, both pockets on the vertical axis, both dunk pockets, both players, and the score. Screen-up SHALL correspond to table north so movement and basket facing match the view.

#### Scenario: No split screen
- **WHEN** a match is running
- **THEN** both players are visible in one camera view

#### Scenario: Camera frames the enlarged table
- **WHEN** a match arena is loaded
- **THEN** north and south walls and east and west walls are all visible in the single camera view

#### Scenario: Aiming top-down is untilted
- **WHEN** a match arena is loaded
- **THEN** the camera looks straight at the table center AND is not pitched or yawed into an isometric angle

#### Scenario: Screen up is north
- **WHEN** player one holds W from rest during a match
- **THEN** player one accelerates toward the top of the screen (north)
