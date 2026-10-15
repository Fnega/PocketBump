## ADDED Requirements

### Requirement: Chinese first-play guidance
The game SHALL expose a Chinese quick-start guide from the main menu and match menu explaining collision scoring against the opponent's goal, Dash duration/cooldown, falling/respawn, pillar stun, breakable walls and the three pickup effects. Controls SHALL reflect valid saved bindings and use top-row 2 as the new-install P2 Dash default.

#### Scenario: Laptop player opens instructions
- **WHEN** a player without saved bindings opens the guide
- **THEN** the displayed controls work without a numeric keypad and explain local split-screen versus same-LAN play

#### Scenario: Existing player upgrades
- **WHEN** valid saved Dash bindings exist in the active profile
- **THEN** loading the demo preserves them and distinguishes Num2 from top-row 2 in labels

### Requirement: Safe in-match menu
Esc SHALL toggle an in-match menu with continue, help, volume, report-directory access and main-menu exit. Local mode SHALL pause simulation; LAN mode SHALL keep networking and simulation active while suppressing local gameplay input. Opening or closing the menu SHALL clear queued input edges.

#### Scenario: Local player pauses
- **WHEN** the local match menu is open
- **THEN** simulation ticks do not advance and continue resumes without a stale Dash press

#### Scenario: LAN player opens menu
- **WHEN** one LAN player opens the menu
- **THEN** both peers continue network progression, the menu explains that the game continues, local intent is neutral, and leaving uses normal peer notification

### Requirement: Readable gameplay cues
Each active viewport SHALL identify the opponent goal/attack direction. Scoring balls SHALL have a clear contrasting visual cue against the existing pale ground without changing physical geometry or minimap/camera behavior.

#### Scenario: Player tracks the ball
- **WHEN** a scoring ball rolls on the light arena surface
- **THEN** its visible marker remains distinct and both local viewports retain their minimaps and directional context

### Requirement: Confirmed one-shot audio feedback
The game SHALL provide original synthesized effects for Dash, impact, wall break, pickup and goal, with persistent adjustable volume. Audio SHALL consume confirmed events, avoid duplicate playback across rollback, and never mutate simulation or random state.

#### Scenario: Event repeats during reconciliation
- **WHEN** the same confirmed event identity is observed again
- **THEN** its sound is not played again and the simulation checksum is unchanged by audio presentation

#### Scenario: Player mutes sound
- **WHEN** the volume is set to zero and the game restarts
- **THEN** the saved mute choice is restored
