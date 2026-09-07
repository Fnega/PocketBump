## ADDED Requirements

### Requirement: Competitive elements have consistent visual identities
The presentation SHALL consistently identify player one with cyan, player two with orange, shared dunk danger with red, and the closed kickoff gate with yellow. Players, their owned pockets, score labels, and relevant effects SHALL preserve these identities.

#### Scenario: Player identities are readable
- **WHEN** the arena is visible
- **THEN** player one and the north pocket use the cyan identity AND player two and the south pocket use the orange identity

#### Scenario: Shared danger is distinct
- **WHEN** east and west dunk pockets are visible
- **THEN** they use a red danger treatment distinct from both player identities

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
