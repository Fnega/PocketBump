## ADDED Requirements

### Requirement: Obstacles have stable authoritative ground-plane geometry

The arena SHALL contain fixed cylinders and designated breakable walls with stable identifiers and authoritative ground-plane collision geometry. Their transforms, collision footprints, wall-break eligibility thresholds, and layout SHALL belong to the validated immutable match profile. Visual height and animation SHALL NOT change authoritative geometry. Obstacles SHALL respect the approved spawn areas, goal approaches, and playable routes; physical layout SHALL not be inferred from nondeterministic render animation or scene-callback ordering. Exact unconfirmed dimensions and thresholds SHALL remain subject to the design's parameter-approval gate.

#### Scenario: Cylinder visuals do not change collision bounds
- **WHEN** a cylinder plays its impact animation or gold-bell shield effect
- **THEN** its authoritative ground-plane collider stays at its configured position and size

#### Scenario: Both peers use identical obstacles
- **WHEN** a LAN match negotiates the arena profile
- **THEN** cylinder and wall identities, geometry, initial states, and interaction thresholds agree before gameplay starts

### Requirement: Cylinder entry produces one stun and feedback episode

A living player's new contact with a cylinder SHALL create one contact episode. For a nonimmune player this SHALL invoke the 36-tick stun and Dash-interruption rules in dash-combat, and emit a short cylinder-impact animation, gold-bell shield effect, and player-addressed head-star/“晕” indication. Repeated solver iterations or sustained contact SHALL not repeatedly emit the entry feedback or restart the stun. Purple armor SHALL prevent the stun and received knockback but SHALL NOT bypass solid cylinder geometry; armored entries SHALL still establish the contact latch. Contact release SHALL use the approved deterministic geometric tolerance rather than animation completion.

#### Scenario: Dash into a cylinder interrupts once
- **WHEN** a nonimmune dashing player begins a cylinder contact
- **THEN** one 36-tick stun and one cylinder-hit feedback episode occur, Dash ends, and its 180-tick cooldown begins

#### Scenario: A purple player cannot pass through a cylinder
- **WHEN** an armored player contacts a cylinder
- **THEN** the player is kept outside the cylinder's solid geometry without stun or received knockback and the contact episode is recorded

#### Scenario: Contact must separate before another entry effect
- **WHEN** a player remains against the same cylinder for several ticks or solver passes
- **THEN** the existing episode does not repeatedly create stun or entry effects, even if armor expires during that contact

### Requirement: Ordinary wall contact shakes without breaking

A currently solid breakable wall SHALL block ordinary player and ball motion with the approved collision response. A new wall-impact episode SHALL emit a brief shake feedback event without moving its authoritative collider. Wall shaking SHALL not be triggered once per internal solver iteration. Ordinary movement, a ball impact, and green enhancement alone SHALL NOT break the wall. Decorative cracks or animation SHALL NOT independently determine whether a wall is solid.

#### Scenario: Ordinary contact leaves the wall intact
- **WHEN** a player outside a qualifying high-speed Dash or a free ball hits a solid breakable wall
- **THEN** the wall remains solid, applies its ordinary blocking response, and emits only the appropriate contact feedback

#### Scenario: Green is not a substitute for Dash qualification
- **WHEN** a green-buffed player hits a wall without satisfying the high-speed active-Dash condition
- **THEN** the wall does not break solely because the player has increased outgoing impact strength

### Requirement: A qualifying high-speed Dash breaks a wall once

A designated breakable wall SHALL transition from solid to broken only when hit by a living player in an active Dash whose pre-response closing impact satisfies the approved profile's high-speed break condition. Qualification SHALL use authoritative motion and contact data, not trail brightness, keyboard hold time, or a post-bounce velocity. The broken wall SHALL cease blocking immediately for the resolved breaking impact and SHALL emit one destruction event. Breaking a wall SHALL not force an otherwise continuing Dash to stop; remaining Dash duration can carry the player through the opening unless another rule interrupts it. Repeated contacts with an already broken wall SHALL NOT restart its repair clock or emit another break event.

#### Scenario: Fast Dash opens the wall
- **WHEN** a living actively dashing player makes a qualifying high-speed closing impact against a solid breakable wall
- **THEN** the wall becomes nonblocking, one break event occurs, and the player can continue through the opening for any remaining Dash duration

#### Scenario: Low-speed or tangential Dash contact is insufficient
- **WHEN** an active Dash touches a wall without meeting the approved closing-impact threshold
- **THEN** the wall remains solid and the interaction follows ordinary wall-contact behavior

#### Scenario: Two same-tick hits do not duplicate breakage
- **WHEN** multiple contacts or solver iterations reference a wall that has already broken during the current tick
- **THEN** they observe the same broken state without resetting its repair deadline or duplicating the destruction event

### Requirement: Broken walls repair only after time and full clearance

Breaking a wall SHALL start a 600-simulation-tick repair delay. After that delay the wall SHALL become eligible for repair but SHALL stay nonblocking while its complete restored collision footprint, including the approved clearance tolerance, overlaps any living player or free scoring ball. It SHALL retry the clearance check deterministically on subsequent world ticks and restore its solid state once the area is clear. Fallen players without active collision SHALL NOT block repair. Repair SHALL NOT move, damage, teleport, trap, or kill an occupant to make space, and SHALL emit its restoration event only once.

#### Scenario: An empty wall repairs after ten seconds of simulation
- **WHEN** a broken wall has accumulated 600 eligible world ticks and its restored footprint is clear
- **THEN** it returns to its original solid state and emits one repair event

#### Scenario: A player delays repair safely
- **WHEN** repair becomes due while any part of a living player's full collider overlaps the restored wall footprint
- **THEN** the wall remains nonblocking in repair-pending state until a later clearance check succeeds

#### Scenario: A ball also delays repair
- **WHEN** a free scoring ball overlaps the restored wall footprint when repair is due
- **THEN** repair waits rather than trapping or pushing that ball

#### Scenario: A fallen player is not an invisible occupant
- **WHEN** a fallen player's last recorded pose overlaps a pending wall but that player's collision is inactive
- **THEN** that pose alone does not prevent repair, while any living player or free ball still does

### Requirement: Obstacle state survives goals and uses world simulation time

Cylinder identity and geometry, wall broken/solid/pending state, and remaining repair delay SHALL persist across scored-round freezes and subsequent kickoffs. Wall repair time SHALL advance by simulation ticks during Live, Kickoff, and the scored freeze; it SHALL stop at MatchOver and whenever the network-failure pause stops simulation. Goal reset SHALL NOT rebuild broken walls, restart their repair clocks, or reseed their identities. A genuinely new match or agreed rematch SHALL initialize obstacles from the approved layout. Player contact latches SHALL be cleared or recomputed consistently when those players are reset or respawned, without resetting the persistent obstacle itself.

#### Scenario: A goal does not erase a broken wall
- **WHEN** a goal resets players and scoring balls while a wall is broken or repair-pending
- **THEN** that wall keeps its identity and state and its repair schedule is not restarted by the goal

#### Scenario: Repair progresses through kickoff
- **WHEN** a broken wall exists during a scored freeze or subsequent kickoff
- **THEN** its repair delay advances with those world simulation ticks and any due restoration still checks current occupant clearance

#### Scenario: Pausing or finishing the match stops repair time
- **WHEN** the simulation enters network-failure pause or MatchOver
- **THEN** wall repair progress does not advance merely because real time passes

### Requirement: Obstacle transitions replay deterministically

Wall states, remaining repair ticks, repair-pending status, and per-player cylinder/wall contact latches SHALL be authoritative rollback state. Contact ordering for simultaneous obstacle interactions SHALL be stable and documented. Rollback SHALL restore these values and reproduce obstacle and player results from the same inputs, including safe repair checks against all relevant colliders. Presentation SHALL deduplicate committed break, repair, shake, and cylinder-hit events rather than changing simulation rules to suppress effects.

#### Scenario: Window-bounded rollbacks cover breakage and occupied repair boundaries
- **WHEN** one long input tape contains a wall break and a later occupied repair deadline, with separate delayed-input bursts causing rollbacks near each boundary that individually remain within the existing 12-tick rollback window
- **THEN** the corrected run matches an on-time run at the corresponding settled boundaries for wall state, timer, player and ball positions, and event identities without restoring a wall through the occupant or requiring one rollback to span the 600-tick repair delay
