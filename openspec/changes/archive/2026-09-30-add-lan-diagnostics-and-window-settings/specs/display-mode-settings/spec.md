## ADDED Requirements

### Requirement: Standalone defaults to a compact window
On first standalone launch, the game SHALL start in a 1280x720 windowed mode unless the player has a saved display-mode preference.

#### Scenario: Fresh standalone launch
- **WHEN** a Windows standalone player launches without a saved display preference
- **THEN** it uses a 1280x720 windowed display

### Requirement: Players can select and persist display mode
The main menu SHALL provide a Settings view with Windowed and Fullscreen choices.  Selecting either choice SHALL apply it immediately and persist it for subsequent launches.

#### Scenario: Player selects fullscreen
- **WHEN** the player selects Fullscreen in Settings
- **THEN** the game enters fullscreen AND the next launch restores fullscreen

#### Scenario: Player returns to windowed mode
- **WHEN** the player selects Windowed in Settings
- **THEN** the game uses a 1280x720 window AND the next launch restores windowed mode
