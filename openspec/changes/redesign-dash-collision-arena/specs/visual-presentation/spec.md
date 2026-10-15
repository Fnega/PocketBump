## MODIFIED Requirements

### Requirement: Competitive elements have consistent visual identities
The presentation SHALL identify player one and that player's defended goal with cyan, and player two and that player's defended goal with orange. Player identity SHALL remain recognizable on rounded robot bodies, score labels, goal markings, and player-specific effects under the approved cream-and-mint lighting and materials. The closed kickoff gate SHALL retain its yellow identity during the three-second kickoff, and death-zone boundaries SHALL use a distinct red or coral-red danger treatment. Green, blue, and purple pickups SHALL combine their color with distinct symbols. Colors alone SHALL NOT be the only way to identify pickup effects or dangerous boundaries.

#### Scenario: Player identities are readable
- **WHEN** either player's view shows players or their defended goals
- **THEN** the cyan and orange identities are recognizable on the respective robot bodies and goals without relying on the other player being on screen

#### Scenario: Shared danger is distinct
- **WHEN** a death-zone boundary is visible
- **THEN** its red or coral-red warning markings distinguish it from walkable cream-and-mint ground, goal entrances, the yellow kickoff gate, and decorative edges

#### Scenario: Closed kickoff gate is recognizable
- **WHEN** the three-second kickoff is running and the gate is closed
- **THEN** the gate retains a readable yellow identity distinct from player identities and death-zone warnings

#### Scenario: Pickup types remain distinct without color alone
- **WHEN** green, blue, and purple pickups are presented
- **THEN** each type has a distinct symbol as well as its assigned color and the symbol agrees with its effect description

### Requirement: Player trails communicate speed and boost
Each moving player SHALL emit a restrained identity-colored movement trail. During the discrete Dash, the presentation SHALL use a longer, stronger, saturated or deeper identity-colored trail that is visibly distinct from normal movement. Trail emission SHALL stop or fade when nearly stationary, dead, or no longer dashing, and SHALL NOT imply the retired hold-to-boost mechanic. Teleports, respawns, and corrected discontinuities SHALL clear or reposition trail history rather than drawing a line across the arena.

#### Scenario: Normal movement trail
- **WHEN** a player moves above the normal trail threshold without dashing
- **THEN** a short restrained trail in that player's identity color is visible

#### Scenario: Dash trail
- **WHEN** a player enters the active Dash phase
- **THEN** the trail becomes visibly longer and stronger than normal movement and uses the approved deeper identity treatment

#### Scenario: Stationary trail fades
- **WHEN** a player's speed falls below the trail threshold
- **THEN** new movement-trail emission stops and the existing trail fades

#### Scenario: Teleport does not draw a false path
- **WHEN** a player teleports or respawns at a distant position
- **THEN** the old trail is cleared and no continuous trail connects the old and new positions

### Requirement: Marble visuals communicate danger speed
Free scoring balls SHALL display a speed-responsive trail, and balls above the approved gameplay danger threshold SHALL have a stronger readable warning than slower balls. The visual warning SHALL read the simulation's velocity and applicable approved tuning profile rather than deciding collision damage, scoring, or knockback. A ball's visibility SHALL remain distinct from the blue teleport pickup and the cyan player.

#### Scenario: Normal moving marble
- **WHEN** a free ball moves above its trail threshold but below the danger threshold
- **THEN** it emits a short neutral trail and remains identifiable as the scoring ball

#### Scenario: Dangerous marble warning
- **WHEN** a free ball reaches the approved dangerous-speed threshold
- **THEN** it displays a stronger trail or glow that is recognizable before contact without obscuring its position or nearby death boundaries

### Requirement: Match pieces have readable 3D volume from above
The arena ground, goals, kickoff gate, players, free scoring balls, fixed pillars, destructible walls, and pickups SHALL use readable 3D forms in the approved tilted top-down presentation. Players SHALL use the approved rounded cyan and orange robot direction, and the arena SHALL use the approved cream-and-mint material direction. Gameplay SHALL remain motion on one ground plane with no jump or airborne wall traversal. Visual height, lighting, animation, or imported mesh bounds SHALL NOT implicitly change authoritative collision geometry. Overlay menu and HUD SHALL remain screen-space UI, partitioned by viewport where appropriate.

#### Scenario: Players and marbles have volume
- **WHEN** the approved arena is loaded and free scoring balls are active
- **THEN** both players and the balls have visibly lit 3D volume rather than appearing only as flat sprites

#### Scenario: Identities survive lighting
- **WHEN** either viewport shows a robot or defended goal
- **THEN** cyan and orange remain recognizable under the cream-and-mint scene lighting

#### Scenario: HUD remains overlay
- **WHEN** a match is running
- **THEN** score, countdown, and personal status remain screen-space UI rather than physical objects that can collide with or hide behind scenery

#### Scenario: Robot art does not enlarge collision implicitly
- **WHEN** an authored robot visual model or decorative part is replaced or resized
- **THEN** the validated simulation collision shape remains unchanged unless an explicit gameplay-layout change is separately approved

### Requirement: Menu and HUD use a coherent tabletop presentation
The main menu, rule setup, match HUD, results, arena materials, goals, pickups, and status feedback SHALL use the approved fresh cream-and-mint 3D arena style rather than the retired dark neon tabletop requirement. TextMeshPro typography SHALL be readable at supported resolutions and in both half-width local viewports. The Chinese character `晕` and any approved Chinese pickup descriptions SHALL render with complete glyph coverage. Global score, countdown, and match-result UI SHALL remain distinct from player-specific Dash, stun, respawn, and Buff status.

#### Scenario: Rule UI is legible
- **WHEN** rule setup is displayed at 1280 by 720, 1920 by 1080, or a supported 4:3 resolution
- **THEN** rule values, movement and Dash bindings, and navigation controls remain visible without clipping or overlap

#### Scenario: Match HUD preserves arena visibility
- **WHEN** a local split-screen or LAN full-screen match is live
- **THEN** score and state feedback remain readable without covering the followed player, nearby ball, or immediately relevant danger boundary

#### Scenario: Stun glyph is available in a packaged build
- **WHEN** the player is stunned in the built game
- **THEN** the head marker displays `晕` with the intended font rather than a missing-character box

### Requirement: Important match transitions have feedback
The presentation SHALL communicate kickoff completion, a confirmed scoring event, an effective Dash impact, pillar-triggered stun, death, respawn, and match victory. A point SHALL emphasize the scoring player's identity and updated score before the next kickoff. A death without a goal SHALL NOT play goal or winner feedback. Pillar-triggered stun SHALL show the affected player's head marker `晕`, orbiting stars, and the contacted pillar's brief golden bell-shaped glow. Any accompanying bell sound SHALL require a separately approved audio-resource plan; the approved golden bell visual SHALL NOT be interpreted as approval to add mandatory audio. Purple super-armor immunity SHALL NOT show an applied-stun marker. Transient effects and any approved audio SHALL be deduplicated by match epoch, simulation tick, and event identity; irreversible score and pickup announcements SHALL be presented only for confirmed events. Presentation SHALL NOT pause or modify simulation time to create impact emphasis.

#### Scenario: Kickoff completion feedback
- **WHEN** the three-second kickoff reaches zero, the kickoff gate opens, and live play begins
- **THEN** a brief visible gate-opening cue communicates that gameplay is active without displaying the retired basket mechanic

#### Scenario: Scoring feedback
- **WHEN** a point is confirmed
- **THEN** the scorer's identity and updated score are emphasized before the round reset

#### Scenario: Dash impact feedback
- **WHEN** an effective Dash contact is confirmed against an eligible target
- **THEN** a brief impact effect appears at the contact and does not misrepresent repeated resting contact as repeated hits

#### Scenario: Pillar stun feedback
- **WHEN** an eligible pillar contact applies the 36-tick stun
- **THEN** the affected robot displays `晕` and orbiting stars for its simulated stun state and the pillar displays the golden bell visual effect; accompanying audio is present only if included in a separately approved audio-resource plan

#### Scenario: Immunity is not displayed as stun
- **WHEN** a player with active purple super armor contacts a pillar without receiving stun
- **THEN** the player does not display `晕` and the feedback distinguishes protected contact from an applied stun

#### Scenario: Falling does not announce a point
- **WHEN** a player falls into a death zone without a simultaneous scoring goal
- **THEN** death and the 180-tick respawn wait are communicated while scores and goal announcements remain unchanged

#### Scenario: Replay does not duplicate approved bell audio
- **WHEN** an approved audio-resource plan includes a bell sound and the same pillar-stun event is encountered again while replaying corrected history
- **THEN** audio deduplication prevents a second bell for that event and any stateful stun marker agrees with the corrected simulation

### Requirement: Views follow the simulation
Player, free-ball, kickoff-gate, obstacle, pickup, Buff, Dash, stun, death, respawn, and wall-repair presentation SHALL derive gameplay state from the deterministic match simulation. Positions MAY be interpolated between ticks for display, and purely decorative animation MAY use rendering time. Unity physics, visual debris, model animation, cameras, popups, and audio SHALL NOT independently change authoritative gameplay state. Corrected history SHALL reconcile visible wall state, status indicators, and effects rather than leaving a predicted destroyed wall or expired Buff visible as current state.

#### Scenario: Presented pose matches sim
- **WHEN** the simulation places player one at a ground position after a tick
- **THEN** the visible robot follows that ground position within render interpolation

#### Scenario: Rollback restores wall and status presentation
- **WHEN** corrected history changes a wall from broken to intact or removes a predicted status change
- **THEN** the corresponding wall and status presentation return to the corrected state without adding an independent visual collider to the simulation

## REMOVED Requirements

### Requirement: Basket and charge readings are visible
**Reason**: The approved collision-and-Dash rules remove baskets, carrying, and staged shooting.
**Migration**: Remove basket identity and charge-stage indicators; show movement, Dash availability, stun, respawn, and Buff status instead.

### Requirement: Charge readings stay outside the player body
**Reason**: No direction or power charge stage remains in the redesigned game.
**Migration**: Remove charge needles and their placement logic; ensure new status indicators remain readable around the approved robot silhouette.

### Requirement: Collect and fire pulses
**Reason**: Collecting a scoring ball, stealing a carried ball, and firing a charged shot are retired actions.
**Migration**: Remove collect/fire pulses for those actions and use confirmed collision, Dash, pickup, and transition feedback with rollback-aware event identity.

## ADDED Requirements

### Requirement: Input pacing wait is visibly distinct from terminal failure
The existing top-right network panel SHALL show a temporary waiting-for-peer-input state whenever input pacing stops simulation. It SHALL explain that valid input progress can resume play automatically, and disappear when waiting ends. A five-second input-progress timeout SHALL instead display the terminal failure reason with an explicit Main Menu action and SHALL NOT automatically resume. Local split-screen gameplay SHALL not display a LAN pacing warning. Supported resolutions and Chinese glyph coverage SHALL remain valid.

#### Scenario: A brief slower-peer wait clears automatically
- **WHEN** pacing waits near the lead limit and valid input subsequently restores progress
- **THEN** the top-right waiting indication appears during the wait and clears on automatic resumption without showing a terminal failure

#### Scenario: No progress becomes a persistent fault
- **WHEN** the independent five-second input-progress deadline expires
- **THEN** the frozen view shows `INPUT PROGRESS TIMEOUT` and Main Menu remains a manual action

### Requirement: Actual Unity art sample approval precedes formal theme replacement
The image at this change's `references/approved-art-concept.png` SHALL be treated as the approved visual-direction reference, not as a finished asset or proof of in-engine quality. Before replacing the visual theme in the formal MainMenu or Arena scene, implementation SHALL present an isolated actual Unity sample with cyan and orange robots, cream-and-mint ground, a pillar, a destructible wall, the three pickup identities, Dash trails, and stun feedback for explicit user approval. Newly sourced third-party asset choices SHALL be presented for approval before adoption, and source and license records SHALL be retained. The sample SHALL NOT overwrite the existing playable scenes or silently substitute unrelated art. An unresolved art approval SHALL stop formal theme replacement while allowing unrelated approved work to continue.

#### Scenario: Concept approval alone is insufficient
- **WHEN** the concept image is approved but the actual Unity sample has not been approved
- **THEN** the implementation may prepare the isolated sample but does not replace the formal scenes' visual theme

#### Scenario: Actual sample is reviewed
- **WHEN** the Unity sample is ready
- **THEN** an in-engine view or recording of its actual models, lighting, effects, and camera is supplied for user review and the formal replacement waits for explicit approval

#### Scenario: New assets retain provenance
- **WHEN** an approved external asset is adopted
- **THEN** its source, license, and attribution obligations are recorded alongside the retained original source package without deleting existing licensed sources

### Requirement: Authored art remains inspectable and editable
The approved visual implementation SHALL provide authored scene objects and reusable visual prefabs, serialized material and model references, and an explicit theme configuration that can be inspected and edited outside Play Mode. Loading a scene, entering or leaving Play Mode, or running the approved incremental art update SHALL preserve intentional authored model, material, and palette choices. Runtime status effects MAY temporarily layer state-driven tint or animation, but SHALL restore the authored appearance and SHALL NOT permanently overwrite it with hard-coded palette defaults. Generated content SHALL be distinguished from hand-authored content, and a broad scene-recreation operation SHALL NOT be used to overwrite manual art changes without explicit user authorization.

#### Scenario: Art is visible before running
- **WHEN** the formal scene is opened in Edit Mode after approved art integration
- **THEN** its ground, robot visuals, pillars, walls, pickups or their authoring previews, and representative materials are inspectable without starting the game

#### Scenario: Palette and model edits survive runtime
- **WHEN** an author saves a supported palette, material, or visual-prefab replacement and then enters and exits Play Mode
- **THEN** the saved choice remains in the scene and no initialization path restores the retired palette or model

#### Scenario: Incremental art updates preserve hand-authored work
- **WHEN** the approved art-update tool is run on a scene containing hand-authored objects
- **THEN** the update changes only its managed content and preserves those objects and their saved overrides

### Requirement: Viewport status and pickup explanations are nonblocking
Each player view SHALL show its player's Dash active or cooldown state, stun, death or respawn countdown, and active timed Buffs. A confirmed pickup SHALL display a short nonblocking explanation with its icon, name, and actual approved effect in the recipient's local viewport; a LAN machine SHALL show personal explanations for its assigned local seat. The popup SHALL NOT pause gameplay, consume movement or Dash input, or require dismissal. Repeat pickups SHALL update or coalesce bounded notification content. Popup duration and effect text SHALL derive from the reviewed presentation and gameplay profiles rather than unapproved assumed Buff durations.

#### Scenario: Local pickup belongs to one half
- **WHEN** player two receives a confirmed pickup in a local match
- **THEN** its explanation is confined to player two's right viewport and neither player's movement or Dash input is interrupted

#### Scenario: LAN joiner sees its own status
- **WHEN** the LAN joiner is assigned player two and receives a pickup
- **THEN** its full-screen view displays player-two status and pickup explanation rather than player-one status

#### Scenario: Repeat pickup does not fill the screen
- **WHEN** multiple confirmed pickups arrive while a notification is visible
- **THEN** the bounded notification area updates or coalesces messages without covering gameplay or duplicating an event after rollback

### Requirement: Obstacle presentation distinguishes damage and restoration
Fixed pillars and destructible walls SHALL have distinct readable silhouettes. Nonbreaking wall impacts SHALL visibly shake the wall without altering the authoritative shape. A valid breaking Dash SHALL display breakage and non-authoritative debris. A broken wall SHALL remain visibly passable while its simulated repair timer has not completed or while a player or ball prevents restoration. Repair eligibility after 600 ticks SHALL NOT be misrepresented as an already solid wall when the area is occupied. Scoring a goal SHALL NOT visually reset persistent wall damage or repair progress.

#### Scenario: Ordinary collision shakes but does not break
- **WHEN** an ordinary impact does not meet the approved wall-breaking criteria
- **THEN** the wall displays a brief shake and remains visually and authoritatively intact

#### Scenario: Occupied repair region stays open
- **WHEN** a broken wall reaches repair eligibility but a player or ball occupies its restoration region
- **THEN** it remains visibly broken and passable until the simulation allows safe restoration

#### Scenario: Goal does not restore broken scenery
- **WHEN** a goal starts the next kickoff while a wall is broken
- **THEN** the visible wall state continues to match the persistent simulated wall and its repair timing

### Requirement: Camera visibility is preserved without changing collision
The tilted follow views SHALL keep the followed active robot and immediately relevant ground hazards readable around pillars, walls, and decorative objects. This SHALL be achieved through approved art proportions or rendering-only occlusion handling rather than by removing authoritative colliders. Death animations SHALL NOT pull the camera below the ground; teleports and respawns SHALL reset or snap follow history safely rather than sweeping through unseen obstacles. View-specific occlusion changes SHALL NOT hide scenery for the other local camera incorrectly, and the gameplay scene SHALL have one active AudioListener rather than one per viewport.

#### Scenario: Pillar cannot hide the followed player indefinitely
- **WHEN** a pillar lies between a follow camera and its active player
- **THEN** the approved visual treatment keeps the player and relevant danger cues readable while the pillar's gameplay collision remains intact

#### Scenario: Death camera stays above the arena
- **WHEN** the followed robot plays its falling presentation and waits for respawn
- **THEN** the camera remains in the intended above-ground view and follows the authoritative safe position on respawn without a below-ground excursion

#### Scenario: Two local views do not duplicate listeners
- **WHEN** both local cameras are active
- **THEN** there is exactly one active gameplay AudioListener and player-specific viewport rendering does not corrupt the other view
