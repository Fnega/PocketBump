## ADDED Requirements

### Requirement: Match pieces have readable 3D volume from above
The table, walls, pockets, gate, players, marbles, and baskets SHALL be rendered as 3D meshes with height toward the camera, lit so their volume is visible in the aiming top-down view. Player, pocket, dunk, and gate identity colors SHALL remain cyan, orange, red, and yellow respectively. Overlay menu and HUD SHALL stay screen-space UI.

#### Scenario: Players and marbles have volume
- **WHEN** a match arena is loaded
- **THEN** both players and at least one scoring marble are 3D meshes rather than flat sprites

#### Scenario: Identities survive lighting
- **WHEN** the arena is visible
- **THEN** player one and the north pocket still read as the cyan identity AND player two and the south pocket still read as the orange identity

#### Scenario: HUD remains overlay
- **WHEN** a match is running
- **THEN** score and countdown remain screen-space UI and are not placed as world meshes on the table
