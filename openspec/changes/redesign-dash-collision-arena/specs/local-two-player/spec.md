## MODIFIED Requirements

### Requirement: Two players share one machine
The game SHALL support a local match of exactly two human players on one machine in two equal left/right viewports of the same arena world, with no network session required. One deterministic match simulation SHALL drive both views; local mode SHALL NOT run a separate simulation per camera. Both seats SHALL be sampled on that machine using their corresponding local bindings.

#### Scenario: Both players present at match start
- **WHEN** a local match starts
- **THEN** two distinct player bodies exist in one arena world, both accept local input, and each has its own follow viewport

#### Scenario: Local match needs no session
- **WHEN** the user starts a local match from rule setup
- **THEN** the match runs without exchanging network packets and both seats are sampled on that machine

### Requirement: Separate movement controls
Player one SHALL move with WASD and player two SHALL move with the arrow keys. Player one SHALL trigger Dash with the current Dash binding, default K, and player two with the current Dash binding, default Keypad2 or numeric-keypad 2. Movement SHALL be applied through the shared tick simulation with acceleration, momentum, coasting, and braking so effective collisions can transfer impulse to scoring balls and the other player. Players SHALL NOT have a jump, basket, carry, charge-shot, or independent Shoot action. Dash, stun, and death SHALL use their explicit control-state rules rather than being bypassed by normal movement sampling.

#### Scenario: Player one input accelerates with inertia
- **WHEN** player one holds W from rest while normal movement is allowed
- **THEN** that player accelerates north over time rather than instantly reaching maximum speed

#### Scenario: Player two input accelerates with inertia
- **WHEN** player two holds the down arrow from rest while normal movement is allowed
- **THEN** that player accelerates south over time rather than instantly reaching maximum speed

#### Scenario: Releasing movement coasts then brakes
- **WHEN** a normally moving player releases all movement keys
- **THEN** the body retains momentum briefly and decelerates toward rest

#### Scenario: Reversing direction preserves momentum
- **WHEN** a normally moving player traveling north applies southward input
- **THEN** the player slows and reverses over time rather than changing velocity direction instantly

#### Scenario: Collision can cause a fall
- **WHEN** an eligible player collision transfers enough impulse toward a death zone during live play
- **THEN** the other player can fall through the normal movement and death rules without a special dunk button or a point being awarded for the fall

### Requirement: Players face their movement input
During normal controllable movement, each player's facing SHALL rotate toward nonzero movement input and SHALL keep the last facing when input is released. Facing SHALL NOT snap to a new 90-degree heading in a single physics step. During active Dash, the committed direction SHALL remain locked for that Dash as specified by `dash-combat`; stun and death SHALL NOT permit movement input to steer the player. The rendering camera SHALL NOT change the simulation facing or input interpretation.

#### Scenario: Player one turns toward east
- **WHEN** player one holds D while facing south and normal movement is allowed
- **THEN** facing rotates toward east and reaches east within 0.25 seconds

#### Scenario: Facing holds when input stops
- **WHEN** a normally controlled player was holding W and releases all movement keys
- **THEN** facing remains north

#### Scenario: Dash direction does not follow late steering
- **WHEN** a player changes directional input after Dash has started
- **THEN** the current Dash keeps its committed direction rather than curving toward the new input

### Requirement: Player turning matches forward acceleration
During normal movement, steering from an unaligned velocity SHALL use the same acceleration magnitude as aligned forward movement. The discrete Dash SHALL use its explicitly committed direction and Dash motion rules rather than the retired steerable hold-to-boost state. Returning to normal controllable movement SHALL restore normal steering behavior; a rendering camera's tilt, follow offset, or viewport size SHALL NOT alter steering acceleration.

#### Scenario: Perpendicular turn uses full acceleration
- **WHEN** a player at normal maximum speed applies perpendicular input while normal control is allowed
- **THEN** velocity changes toward that heading at the same acceleration used for aligned normal forward movement

#### Scenario: Normal steering returns after Dash
- **WHEN** Dash has ended and the player is alive and not stunned
- **THEN** ordinary movement input uses the normal steering rules even while Dash cooldown is active

### Requirement: HUD shows score, countdown, and match result
The HUD SHALL show both scores throughout the match, the remaining kickoff countdown during kickoff, and the confirmed winner when the match ends. Each player's viewport SHALL also show that player's Dash availability or cooldown, stun state, respawn countdown, and applicable timed Buffs. A dead player's personal countdown SHALL NOT replace or freeze the shared score or the other player's live view. LAN results SHALL respect corrected terminal agreement and the existing bilateral-rematch policy.

#### Scenario: Countdown visible
- **WHEN** kickoff countdown is running
- **THEN** the HUD displays the remaining seconds clearly without duplicating conflicting countdown values between local viewports

#### Scenario: Winner visible
- **WHEN** a player wins and the result is final for the current play mode
- **THEN** the HUD identifies that player as the winner and further rounds do not start until the appropriate new-match or bilateral-rematch flow completes

#### Scenario: Death countdown is personal
- **WHEN** one local player is waiting through the 180-tick respawn interval while live play continues
- **THEN** the player's viewport displays the respawn state and the other viewport continues showing the live world and unchanged shared score

### Requirement: Local and networked matches share one simulation
Whether the match is local or LAN, player and free-ball motion, Dash, collision effects, goals, deaths, respawns, obstacle contacts, wall damage and repair, pickups, Buffs, and gameplay random state SHALL advance only through the same deterministic match tick stepper. Unity Physics2D or Physics3D SHALL NOT be the source of truth for those facts. Local split-screen and LAN full-screen presentation SHALL only read that state and SHALL NOT change tick advancement or event ordering.

#### Scenario: Same input tape matches
- **WHEN** isolated local and LAN-style simulations start from the same approved kickoff state and consume the same pair of per-tick inputs
- **THEN** at the same tick their complete player, ball, obstacle, Buff, random, and score state checksums match

#### Scenario: Additional camera does not advance simulation
- **WHEN** local play renders both follow cameras in one rendered frame
- **THEN** the shared tick stepper advances according to its fixed-tick schedule rather than once per camera

### Requirement: Player-player contacts are stiffer than walls
The baseline player-player circle response SHALL retain its dedicated restitution of 0.90. Baseline static-wall, ball-ball, and player-ball contact response SHALL retain restitution 0.58. Explicit Dash, green impact-strength, and purple immunity responses SHALL be isolated gameplay modifiers governed by their own approved rules, rather than hidden changes to the base restitution solver. Retired basket-rim collision SHALL NOT remain active.

#### Scenario: Head-on bump separates faster than a wall hit
- **WHEN** unmodified equal-mass players approach along their contact normal at the relative speed used for an equivalent static-wall impact
- **THEN** their base post-contact separation uses restitution 0.90 and is stronger than the equivalent base wall response using 0.58

#### Scenario: Gameplay modifiers are distinguished from base response
- **WHEN** an effective Dash or Buff changes the final gameplay response
- **THEN** the modifier is applied through its explicit rules and isolated base-solver tests still verify the unchanged baseline restitution

### Requirement: Dynamic circle contacts resolve closing motion correctly
The shared simulation SHALL resolve player/player, player/ball, and ball/ball base circle contacts with a unit contact normal, inverse-mass penetration correction, and a base restitution impulse only for closing normal motion. For isolated base-circle resolution with positive finite masses, the impulse SHALL preserve pair momentum and tangential velocity, and the separating normal speed SHALL equal the closing normal speed multiplied by configured restitution within floating-point tolerance. Already separating or purely tangential contacts SHALL NOT receive another base restitution impulse. Existing baseline masses and restitution values SHALL remain unchanged. Additional effective-contact knockback, Dash, green strength, and purple immunity SHALL be applied as explicit gameplay rules outside the isolated mathematical base contract; those rules SHALL NOT remove solid obstacle separation, allow an immune player to pass through geometry, or silently repeat an extra hit for resting contact.

#### Scenario: Approaching players rebound
- **WHEN** two unmodified player circles approach along their contact normal and touch or overlap
- **THEN** their base contact response changes closing motion to separating motion using restitution 0.90 and the existing player masses

#### Scenario: Separating overlap is corrected without a second rebound
- **WHEN** two overlapping circles already have separating normal velocities and only the base circle solver is applied
- **THEN** their penetration is corrected in inverse-mass proportions while their velocities remain unchanged

#### Scenario: Tangential motion is not reflected
- **WHEN** two overlapping circles have zero relative normal velocity but nonzero tangential velocity and only the base circle solver is applied
- **THEN** their penetration is corrected without changing their tangential or normal velocities

#### Scenario: Unequal mass contact conserves base momentum
- **WHEN** a player circle and a free scoring ball undergo isolated closing base contact without gameplay modifiers
- **THEN** the impulse uses the existing unequal masses, preserves total momentum, and produces the normal separating speed specified by restitution 0.58

#### Scenario: Marble contacts use table restitution
- **WHEN** two free scoring balls undergo isolated closing base contact
- **THEN** they exchange impulse using their existing masses and restitution 0.58

#### Scenario: Exact touching can rebound without moving positions
- **WHEN** circle centers are exactly one radius sum apart and the circles have closing normal velocity
- **THEN** base contact applies the appropriate normal impulse without a penetration displacement

#### Scenario: Noncontact does not mutate the pair
- **WHEN** the circles are farther apart than their radius sum
- **THEN** base circle resolution does not change their positions or velocities

#### Scenario: Exchanging a nondegenerate pair preserves the physical result
- **WHEN** an isolated contact whose center separation is outside the degenerate fallback range is resolved with A and B exchanged together with positions, velocities, radii, and masses
- **THEN** the outputs assigned back to the original bodies match within floating-point tolerance

### Requirement: Degenerate circle contacts remain bounded and replayable
For supported finite simulation positions and velocities, coincident or near-coincident circle contacts SHALL use a deterministic unit fallback normal when center separation cannot reliably determine a normal. Penetration correction SHALL remain mass-weighted, SHALL NOT move either body by more than the pair's radius sum plus numerical tolerance in one resolution, and SHALL NOT produce NaN or infinite positions or velocities. Repeated isolated-pair resolution SHALL remove overlap within numerical tolerance. Resolving identical input state again SHALL produce identical output; no random or wall-clock input SHALL select the fallback. Replay validation SHALL exercise the new actual collision-and-Dash rules rather than retired charge-shot or carry events.

#### Scenario: Coincident circles do not fly across the arena
- **WHEN** two stationary circle centers coincide exactly and base contact is resolved
- **THEN** finite mass-weighted corrections separate them by their radius sum without either correction exceeding that sum plus numerical tolerance

#### Scenario: Very small offsets do not amplify the normal
- **WHEN** stationary circle centers differ by small positive or negative offsets around the normal-selection tolerance
- **THEN** their contact resolution stays finite and bounded and repeated isolated resolution removes overlap within numerical tolerance

#### Scenario: Coincident moving circles have a repeatable response
- **WHEN** the same supported coincident-center state with finite velocities is resolved repeatedly from identical initial state
- **THEN** the chosen separation direction, resulting positions, and resulting velocities are identical and finite

#### Scenario: Delayed input replays corrected collisions and Dash events
- **WHEN** a two-seat tape containing effective ball impacts, Dash contacts, pillar stun, wall breakage or repair, and Buff events is run with all inputs available and then with remote inputs delayed within the rollback window
- **THEN** corrected complete state and checksum match at the same confirmed tick for either local seat, with at least one rollback and each intended new-rule event explicitly exercised

## REMOVED Requirements

### Requirement: Each player has an independent hold-to-boost control
**Reason**: The redesign replaces continuous held Boost with an edge-triggered fixed-duration Dash and cooldown.
**Migration**: Migrate valid former Boost key preferences to Dash, preserve local seat isolation, and use the discrete Dash requirement and `dash-combat` state machine instead of the old windup, held acceleration, and release recovery.

### Requirement: Each player has a shoot key
**Reason**: Scoring balls are struck through body contact rather than held and launched by a Shoot action.
**Migration**: Remove Shoot sampling, UI, binding reservations, and charge-state transitions; retired Shoot preferences do not trigger gameplay.

### Requirement: Shared camera shows the whole table
**Reason**: The approved larger arena uses tilted local-space follow cameras, local left/right split-screen, and LAN full-screen assigned-seat views.
**Migration**: Replace the single full-table, untilted camera with mode-aware follow viewports while preserving screen-up as arena north and keeping all cameras outside authoritative simulation.

## ADDED Requirements

### Requirement: Each player has an independent discrete Dash action
Each eligible fresh press of the player's Dash binding SHALL request one Dash through the deterministic input stream. Dash SHALL commit to its chosen direction for 18 simulation ticks unless explicitly interrupted, then enter a 180-tick cooldown starting when the Dash ends or is interrupted. Holding the key SHALL NOT chain another Dash when cooldown expires; a fresh press SHALL be required. One player's input SHALL NOT activate the other player's Dash. Inputs while Dash is unavailable SHALL follow `dash-combat` eligibility rules rather than being silently queued to activate on recovery. The default local keys SHALL be K and numeric-keypad 2, with valid saved former Boost bindings migrated as specified by `menu-flow`.

#### Scenario: Player one Dash is independent
- **WHEN** eligible player one freshly presses K and player two does not press their Dash key
- **THEN** only player one begins the committed Dash

#### Scenario: Player two Dash is independent
- **WHEN** eligible player two freshly presses numeric-keypad 2 and player one does not press their Dash key
- **THEN** only player two begins the committed Dash

#### Scenario: Holding does not chain
- **WHEN** a player keeps the Dash key held through Dash completion and the entire cooldown
- **THEN** another Dash does not begin until the key is released and freshly pressed while eligible

#### Scenario: Interruption starts the cooldown
- **WHEN** a legal interrupt ends a Dash before its 18 active ticks complete
- **THEN** the 180-tick cooldown starts from that interruption and the remaining Dash motion is not resumed afterward

### Requirement: Follow viewports depend on play mode rather than simulation ownership
Local play SHALL render one shared world through a left player-one viewport and a right player-two viewport, each occupying half the available gameplay screen and following its assigned player. LAN play SHALL render one full-screen view following the handshake-assigned local seat, including player two on a joining machine. Cameras SHALL use the approved tilted top-down direction with fixed common orientation, and screen-up SHALL correspond to arena north. Neither camera SHALL rotate behind a robot or require both players and all arena boundaries to stay visible at once. Follow framing SHALL suit the approved larger layout and show readable local action rather than shrinking the entire arena into each half. Exact camera and layout tuning SHALL follow the reviewed profile and actual Unity sample approval rather than treating a candidate map size as approved.

#### Scenario: Local players have independent follow regions
- **WHEN** local players move far apart
- **THEN** the left view follows player one and the right view follows player two while both display the same authoritative world without forcing the two players into one frame

#### Scenario: LAN joiner follows the assigned seat
- **WHEN** a LAN joiner is assigned seat one, representing player two
- **THEN** its sole full-screen gameplay camera follows player two while host and joiner still simulate both players

#### Scenario: Screen up is north
- **WHEN** either player holds their north movement key while normal control is allowed
- **THEN** that player's character moves toward the top of the ground plane as displayed in its viewport without mirror or camera-relative reversal

#### Scenario: Resizing preserves viewport boundaries
- **WHEN** the window is resized or switched between supported windowed and fullscreen modes
- **THEN** local viewports remain left/right halves, LAN remains full screen, and personal HUD elements stay within their intended region without clipping shared scores

#### Scenario: Teleport and respawn reset follow history
- **WHEN** a followed player safely teleports or respawns far from its previous position
- **THEN** the view re-centers using the approved transition without sweeping its follow history through walls or tracking below-ground death animation
