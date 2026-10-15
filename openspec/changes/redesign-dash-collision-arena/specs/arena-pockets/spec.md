## MODIFIED Requirements

### Requirement: Table has four pockets and a center gate
The arena SHALL retain a north defended scoring-ball goal, a south defended scoring-ball goal, east and west player-only death regions, and a center-line kickoff gate on the deterministic gameplay plane. Its visible 3D ground and obstacle layout SHALL be larger than the legacy arena under a user-approved layout profile and SHALL be independent of camera framing. Each mode SHALL use the same authored and validated gameplay geometry rather than changing the world to fit a viewport.

#### Scenario: Larger arena keeps readable roles
- **WHEN** the new arena loads
- **THEN** the north/south ball goals, lateral fall regions and central gate have distinct roles and matching simulated and visible boundaries even when only part of the arena is on screen

### Requirement: North and south pockets accept marbles only
North and south goals SHALL accept active free scoring balls and SHALL block players. The north goal SHALL be defended by player one and award player two one point; the south goal SHALL be defended by player two and award player one one point. There SHALL be no carried-ball eligibility state. Buff pickups, cosmetic fragments and inactive objects SHALL NOT count as scoring balls. Fast valid crossings SHALL not be lost merely because both end-of-tick centers lie beyond a thin well.

#### Scenario: North goal awards its owner's opponent
- **WHEN** a free scoring ball validly enters the north goal during Live play
- **THEN** the goal identifies player one as conceding and player two as scoring

#### Scenario: South goal awards its owner's opponent
- **WHEN** a free scoring ball validly enters the south goal during Live play
- **THEN** the goal identifies player two as conceding and player one as scoring

#### Scenario: Player cannot walk or Dash into a goal
- **WHEN** a player approaches a north or south goal, including during Dash or purple armor
- **THEN** its full body is blocked from entering that goal and no player-goal score is produced

#### Scenario: Buffs are not scoring balls
- **WHEN** a Buff or visual fragment is near a goal
- **THEN** it cannot produce a scoring-ball goal or change either score

### Requirement: East and west pockets accept players only
East and west death regions SHALL accept active players during Live play and SHALL physically block free scoring balls. A valid player crossing SHALL begin the independent no-point respawn flow without ending Live play. Fast movement and Dash SHALL not tunnel over the region without detection. Neither purple armor nor a cosmetic shield SHALL provide fall immunity.

#### Scenario: Player falls without awarding a point
- **WHEN** an active player validly enters a lateral death region
- **THEN** that player becomes inactive for the respawn flow, neither score changes, and the other player and scoring balls continue

#### Scenario: Ball stays on the arena
- **WHEN** a scoring ball contacts the east or west death opening
- **THEN** the simulated blocker keeps the ball on the playable side rather than deleting it or awarding a point

#### Scenario: Armor does not prevent falling
- **WHEN** a purple-armored player moves into a death region
- **THEN** the ordinary no-point fall and respawn rules still apply

### Requirement: Marbles spawn at rest after the gate opens
Scoring balls SHALL spawn only after the round's center gate opens and SHALL have zero initial velocity. The default one ball SHALL spawn in the center spawn region; a configured count up to eight SHALL use a deterministic, non-overlapping cluster there. Layout validation SHALL reserve enough collision-free center space for every supported ball count and SHALL reject pillar/wall layouts obstructing it. Buff spawns SHALL not consume scoring-ball slots. Goal restarts SHALL clear old scoring balls before spawning the new set; independent player respawns SHALL not respawn or move the balls.

#### Scenario: Single ball kickoff
- **WHEN** the round becomes Live with the default ball count of one
- **THEN** exactly one free scoring ball appears at rest in the reserved center location

#### Scenario: Maximum ball count remains valid
- **WHEN** the round becomes Live with eight scoring balls
- **THEN** all eight spawn at deterministic clear non-overlapping cluster locations without pillars, intact walls or pickups replacing their slots

#### Scenario: No scoring balls during countdown
- **WHEN** kickoff is still counting down
- **THEN** no active scoring balls remain from the previous round or spawn early

### Requirement: High-speed marbles can knock players back
A scoring ball SHALL use ordinary base contact below the approved dangerous-impact threshold and SHALL add controlled directional gameplay knockback on a qualifying dangerous contact with a non-armored player. The additional impulse SHALL scale with closing relative impact speed under the validated rules profile and SHALL be applied at most once for an eligible contact episode. Purple armor SHALL suppress the player's incoming dynamic knockback without disabling nonpenetration or the ball's collision response. A collision-caused fall SHALL use the new no-point respawn flow.

#### Scenario: Slow contact remains ordinary
- **WHEN** a slow ball contacts a non-armored player below the dangerous threshold
- **THEN** the ordinary base collision occurs without the extra dangerous-impact impulse

#### Scenario: Dangerous ball knocks a player toward a fall
- **WHEN** a dangerous ball strikes a non-armored player toward a death region
- **THEN** the player receives the configured directional knockback and any resulting fall awards no point

#### Scenario: Armored player still blocks the ball
- **WHEN** a fast ball strikes a purple-armored player
- **THEN** the player is not dynamically knocked back, but the ball and player do not pass through one another

### Requirement: Dangerous impacts transfer marble energy
When a scoring ball actually applies the additional dangerous knockback, it SHALL lose the profile-defined fraction of its speed while preserving the resolved outgoing direction. The extra effect SHALL not repeat across solver iterations or continued contact until that episode has ended and any configured rearm conditions are satisfied. Armor-suppressed extra knockback SHALL not be reported as an applied dangerous impulse. Natural free-ball drag SHALL remain active between contacts.

#### Scenario: Energy transfer slows the ball
- **WHEN** a ball applies the extra dangerous knockback to a non-armored player
- **THEN** the ball loses the configured speed fraction and one actual impact event is emitted

#### Scenario: Sustained overlap does not add repeated impulses
- **WHEN** the same ball/player contact persists across multiple solver iterations and ticks
- **THEN** it does not stack extra dangerous knockback or duplicate impact events solely because it is reevaluated

## REMOVED Requirements

### Requirement: Table extents are 32 by 48
**Reason**: The user requires a larger arena and larger lateral death regions with follow cameras rather than the legacy fixed full-table view.
**Migration**: Use the approved layout profile and the new layout-validation requirement. The 48×72 interior and 9.675-unit lateral openings passed the initial-value approval gate on 2026-10-02; the user's subsequent “可以就这样” accepted the presented greybox layout and tuning as the first production baseline. Freezing and verifying that production profile remains implementation work, and this acceptance does not approve the actual robot art sample or formal scene replacement. Changing art alone must not silently change collider geometry.

## ADDED Requirements

### Requirement: Expanded arena geometry comes from one approved layout
The simulation, visible scene, collision queries, camera bounds, goals, death regions, spawn regions and obstacle definitions SHALL derive from the same validated immutable layout profile. The first production profile SHALL enlarge the arena and lateral death openings relative to the legacy 32×48 / 6.45 baseline after user confirmation, without tying world dimensions to screen size. Existing player and scoring-ball radii and goal openings SHALL be preserved unless a separately approved profile change explicitly replaces them. Every active layout SHALL contain clear paths between spawn regions, the scoring-ball area and goals, and safe candidates for respawn, Buff generation and teleport. Its canonical gameplay content SHALL be part of LAN rule compatibility.

#### Scenario: Pending dimensions are not silently finalized
- **WHEN** the design's proposed map dimensions have not received user confirmation
- **THEN** they remain clearly labeled candidate parameters and cannot be represented as the approved production layout

#### Scenario: Visual and simulated openings agree
- **WHEN** the approved layout is loaded in the editor and in either play mode
- **THEN** the rendered goal/death boundaries match the same dimensions used by swept collision, scoring and fall queries

#### Scenario: Invalid obstacle placement is rejected
- **WHEN** an authored pillar or wall intersects a mandatory kickoff footprint or blocks all required routes
- **THEN** layout validation identifies the invalid stable object ID before beginning a playable match rather than spawning objects inside it

#### Scenario: Screen size does not change the world
- **WHEN** the game changes window size or switches between local split-screen and LAN full-screen presentation
- **THEN** the same approved world geometry and rule identity are retained while only viewport framing changes
