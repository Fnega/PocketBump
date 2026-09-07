# Visual Presentation

## Purpose

Defines consistent visual identities, movement and danger cues, interface styling, and transition feedback.

## Requirements

### Requirement: Competitive elements have consistent visual identities
The presentation SHALL consistently identify player one with cyan, player two with orange, shared dunk danger with red, and the closed kickoff gate with yellow. Players, their owned pockets, score labels, and relevant effects SHALL preserve these identities.

#### Scenario: Player identities are readable
- **WHEN** the arena is visible
- **THEN** player one and the north pocket use the cyan identity AND player two and the south pocket use the orange identity

#### Scenario: Shared danger is distinct
- **WHEN** east and west dunk pockets are visible
- **THEN** they use a red danger treatment distinct from both player identities

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

### Requirement: Charge readings stay outside the player body
Direction and power readings SHALL be anchored at that player's basket and SHALL extend forward of the basket. The shortest power reading SHALL still be visible outside the player's body silhouette.

#### Scenario: Direction reading clears the body
- **WHEN** a carrying player is in the direction stage
- **THEN** the direction indicator is visible at the basket and is not contained inside the player mesh

#### Scenario: Minimum power reading clears the body
- **WHEN** a carrying player enters the power stage at the lowest power reading
- **THEN** the power indicator is still visible outside the player mesh

### Requirement: Collect and fire pulses
A successful basket collect (including a scoop steal) SHALL play a short world-space pulse at the new carrier's basket in that player's identity color. Firing a charged shot SHALL play a short world-space pulse at the launch point. Body bounce, a rejected second marble, and charge cancel SHALL NOT play those pulses. In a networked match those pulses SHALL play only for events that remain true after rollback (confirmed ticks), not for predictions that are later corrected.

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

#### Scenario: Rolled-back collect is silent
- **WHEN** a peer predicted a collect on tick 50 AND rollback determines tick 50 had no collect
- **THEN** no collect pulse remains from that prediction

### Requirement: Player trails communicate speed and boost
Each moving player SHALL emit an identity-colored trail whose length, intensity, and width increase during boost. Trail emission SHALL stop or fade when the player is nearly stationary.

#### Scenario: Normal movement trail
- **WHEN** a player moves above the normal trail threshold without boosting
- **THEN** a short dim trail in that player's identity color is visible

#### Scenario: Boost trail
- **WHEN** a player enters boosted movement
- **THEN** the trail becomes longer, brighter, and wider than the normal movement trail

#### Scenario: Stationary trail fades
- **WHEN** a player's speed falls below the trail threshold
- **THEN** new trail emission stops and the existing trail fades

### Requirement: Marble visuals communicate danger speed
Moving marbles SHALL display a speed-responsive trail. A marble above the dangerous speed threshold SHALL use a clearly stronger glow/trail treatment than a normal marble.

#### Scenario: Normal moving marble
- **WHEN** a marble moves above its trail threshold but below dangerous speed
- **THEN** it emits a short neutral-colored trail

#### Scenario: Dangerous marble warning
- **WHEN** a marble reaches dangerous speed
- **THEN** it displays a high-intensity trail or glow that is visibly distinct before it hits a player

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

### Requirement: Menu and HUD use a coherent tabletop presentation
The main menu, rule setup, match HUD, result screen, arena surface, pockets, and gate SHALL use a consistent neon tabletop visual language and readable TextMeshPro typography.

#### Scenario: Rule UI is legible
- **WHEN** rule setup is displayed at the reference resolution
- **THEN** labels, selected values, and navigation buttons are readable without overlapping the arena

#### Scenario: Match HUD preserves arena visibility
- **WHEN** a match is live
- **THEN** score and state feedback are readable without obscuring the center marble or pocket entrances

### Requirement: Important match transitions have feedback
The presentation SHALL provide visible feedback for the gate opening, a scoring event, a dangerous marble-player impact, and match victory.

#### Scenario: Gate opening feedback
- **WHEN** kickoff reaches zero and the center gate opens
- **THEN** a brief visual effect communicates that crossing and marble play are now active

#### Scenario: Scoring feedback
- **WHEN** a point is awarded
- **THEN** the scoring player's identity color and updated score are emphasized before the reset

#### Scenario: Dangerous impact feedback
- **WHEN** a dangerous marble applies knockback to a player
- **THEN** an impact effect appears at the collision point

### Requirement: Views follow the simulation
Player, marble, basket, and gate presentation SHALL take pose and charge readings from the match simulation (interpolating between ticks for display). Unity Physics2D bodies SHALL NOT independently move gameplay objects away from the sim.

#### Scenario: Presented pose matches sim
- **WHEN** the simulation places player one at a position after a tick
- **THEN** the visible player mesh is at that position (within render interpolation)
