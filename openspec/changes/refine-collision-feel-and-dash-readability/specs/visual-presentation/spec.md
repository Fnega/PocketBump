## ADDED Requirements

### Requirement: Dash readiness markers are owned by each viewing seat

In local split-screen, both active gameplay cameras SHALL display the two players' complete cooldown progress as thick world-ground rings following their robots. This local sharing is explicitly approved and replaces the earlier own-detailed/opponent-coarse policy in local mode only. LAN SHALL retain its existing foot-anchored own-progress and coarse visible-opponent markers; its opponent markers SHALL NOT encode remaining seconds or fractional progress through fill, text or animation. Dash readiness presentation SHALL preserve other HUD and combat controls. The separately approved local-minimap-controls requirements MAY toggle both local maps and provide a configurable map key without changing the ground rings or other match HUD.

#### Scenario: Local players see both ground rings
- **WHEN** both players are cooling down in local split-screen
- **THEN** both viewports display both players' thick ground-ring progress where visible, and the four old overlay readiness markers are hidden

#### Scenario: Ground rings do not enter LAN or replace the released baseline
- **WHEN** the candidate presentation uses LAN, or the released V1 profile is selected
- **THEN** the local ground rings are hidden, temporarily hidden identity-footprint renderers regain their previous enabled values, and the previous presentation behavior remains intact

#### Scenario: LAN seat two is not mistaken for camera two
- **WHEN** the client is assigned seat two and LAN uses GameplayCameras[0]
- **THEN** seat two is treated as the local detailed marker owner and both projected markers use that active camera

### Requirement: Dash markers respect eligibility rather than cooldown alone

Markers SHALL apply the existing status priority: dead, non-Live or kickoff locked, stunned, currently dashing, cooling down, then ready. Dead-player markers SHALL be hidden. Non-Live, stun and active-Dash states SHALL never display ready merely because their cooldown is zero. Both local rings and the LAN owner's cooling marker SHALL derive progress from 1 minus remaining cooldown divided by the approved cooldown duration. Local non-ready rings SHALL NOT fully close their bright arc; a minimum visible final gap MAY be retained until readiness to prevent rasterization from suggesting an early recharge. The LAN opponent not-ready marker SHALL retain its fixed coarse appearance. The new markers SHALL NOT alter Dash's 18 active ticks, 180-tick cooldown beginning at termination, locked direction, input-edge rules or illegal-press discard behavior.

#### Scenario: Active Dash does not look recharged
- **WHEN** a player is still within the 18 active Dash ticks and has zero remaining cooldown because cooldown has not started
- **THEN** local views show an empty/disabled bright ground ring and LAN keeps its existing active-Dash/not-ready presentation rather than a full ready ring

#### Scenario: Stun and kickoff override a completed cooldown
- **WHEN** cooldown is zero but the player is stunned or the match is not Live
- **THEN** markers use disabled/not-ready presentation and the existing stun and kickoff UI remains intact

#### Scenario: End of cooldown matches simulation eligibility
- **WHEN** a living unstunned Live player reaches zero cooldown outside an active Dash
- **THEN** both local views see that player's fully bright closed ring, or LAN shows the existing owner-ready ring/opponent-coarse symbol, without triggering or buffering any action

#### Scenario: Last cooldown tick is visibly not ready
- **WHEN** a living local player has one cooldown tick left
- **THEN** its bright ground-ring arc retains a visible gap until the simulation reaches actual Dash eligibility

### Requirement: Dash marker timing is reversible and simulation derived

Marker state and progress SHALL read the currently presented simulation state rather than an independent wall-clock countdown. Same-tick corrections, rollback, successful teleport, respawn, goal reset and new epochs SHALL update or clear stale presentation without mutating gameplay. The candidate SHALL use persistent readiness styling without new one-shot completion sounds or flashes. Existing pauses and recoverable pacing SHALL freeze progress with simulation; silent input waiting SHALL remain silent and terminal-fault UI SHALL remain functional.

#### Scenario: Same-tick correction restores cooldown
- **WHEN** a corrected state at the same tick changes a player from ready to cooling down
- **THEN** the next marker refresh reflects the corrected state, including both viewport roles, without requiring tick advancement or replaying a readiness effect

#### Scenario: Waiting cannot visually finish a cooldown
- **WHEN** recoverable input pacing stops simulation progression while real time continues
- **THEN** marker progress remains fixed, no waiting panel or new fault notification is introduced, and progress resumes from simulation state after recovery

### Requirement: Dash markers preserve visibility boundaries and authored presentation

Local rings SHALL lie on the world XZ ground plane, follow interpolated robot-root positions without following facing rotation or animated body bob/squash, and use ordinary depth testing and camera clipping. LAN overlays SHALL retain their existing viewport projection/clipping behavior. No marker SHALL create screen-edge hints or broaden obstacle-fade rules. Local ground rings SHALL replace, not stack on, the old readiness overlays and original identity footprints while active; unrelated models and authored styles SHALL be preserved. Markers SHALL use the existing cyan/orange identities with strong contrast, not substitute green/purple buff auras. They SHALL NOT intercept UI input or draw on top of ball, stun, minimap or fault controls. Their objects, style and references SHALL be saved in the authored Arena and editable without runtime regeneration overwriting user choices.

#### Scenario: Ground rings follow the ground rather than the body animation
- **WHEN** a local robot moves, turns, teleports or animates its upper body
- **THEN** its ring follows its ground position, keeps a fixed world orientation and remains a thick ground-plane band, without mutating authoritative state

#### Scenario: Offscreen and occluded opponents do not reveal extra information
- **WHEN** local ground-ring geometry is outside a camera's view or hidden by solid depth geometry, or a LAN opponent meets its existing offscreen/fully-occluded condition
- **THEN** that camera does not draw the clipped/occluded marker geometry, and no edge arrow, minimap cooldown or new obstacle transparency is added

#### Scenario: Marker visibility is per camera
- **WHEN** one split-screen camera sees a ground ring but the other camera cannot see that same ground geometry
- **THEN** each camera independently applies its clipping and depth tests; sharing ring progress does not force geometry to render through obstacles or outside either view

#### Scenario: Authored styles survive ordinary use
- **WHEN** an editor-authored marker style is saved, reloaded, played, exited and subjected to the explicit integration tool a second time
- **THEN** the style and intended references remain valid, no duplicate markers appear and unrelated scene objects or material edits are not replaced
