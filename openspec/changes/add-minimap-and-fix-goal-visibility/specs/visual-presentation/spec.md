## ADDED Requirements

### Requirement: Nearby goals remain visible in tilted follow views
When a living followed player is in the near-goal approach corridor, within eight ground units of either end and near the goal mouth laterally, the complete goal frame and entrance SHALL be inside the camera's usable viewport without being hidden by status/pickup/minimap UI. The followed robot SHALL remain visible. This SHALL hold for both goals, both local seats, both LAN assignments, and supported 16:9 and 4:3 sizes. Camera correction SHALL preserve north-up tilt and gameplay geometry, rather than moving goals or showing the entire arena at reduced scale.

#### Scenario: Blue goal approached
- **WHEN** either followed player approaches the blue goal in local split-screen or LAN
- **THEN** the blue entrance, posts and lintel fit in the HUD-safe visible area instead of being clipped beyond the top edge

#### Scenario: Orange goal approached
- **WHEN** either followed player approaches the orange goal
- **THEN** its complete entrance and frame remain visible along with the player, without bottom-edge clipping

#### Scenario: Main view remains local
- **WHEN** a player moves away from a goal
- **THEN** the main camera keeps its follow scale and the minimap supplies off-screen goal/entity orientation

## MODIFIED Requirements

### Requirement: Input pacing wait is visibly distinct from terminal failure
The bottom-right network panel SHALL show a temporary waiting-for-peer-input state whenever input pacing stops simulation. It SHALL explain that valid input progress can resume play automatically, and disappear when waiting ends. A five-second input-progress timeout SHALL instead display the terminal failure reason with an explicit Main Menu action and SHALL NOT automatically resume. Local split-screen gameplay SHALL not display a LAN pacing warning. Supported resolutions and Chinese glyph coverage SHALL remain valid. The bottom-right panel SHALL remain inside the window and separate from the top-right minimap and bottom banner without hiding the minimap.

#### Scenario: A brief slower-peer wait clears automatically
- **WHEN** pacing waits near the lead limit and valid input subsequently restores progress
- **THEN** the bottom-right waiting indication appears during the wait and clears on automatic resumption without showing a terminal failure

#### Scenario: No progress becomes a persistent fault
- **WHEN** the independent five-second input-progress deadline expires
- **THEN** the frozen view shows `INPUT PROGRESS TIMEOUT` and Main Menu remains a manual action

#### Scenario: Other network faults retain their treatment
- **WHEN** DESYNC, input loss, peer departure or another existing terminal network fault occurs
- **THEN** the existing pause and reason behavior is preserved in the bottom-right panel without automatically returning to the menu
