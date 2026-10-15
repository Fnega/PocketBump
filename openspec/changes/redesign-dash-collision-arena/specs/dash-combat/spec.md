## ADDED Requirements

### Requirement: Body contact replaces basket shooting

The simulation SHALL retain free scoring balls and the defended north and south goals. An eligible closing player-body/ball impact SHALL act as the player's shot, using the contact geometry, relative motion, and the approved immutable match rules profile. Players SHALL NOT collect, carry, steal, or charge a ball, and there SHALL be no independent shoot action. Ordinary movement and Dash SHALL both be capable of striking a free ball; repeated solver iterations or sustained contact SHALL NOT manufacture repeated additional shot impulses.

#### Scenario: Ordinary movement strikes a free ball
- **WHEN** a living player moves into a free ball with closing normal motion
- **THEN** the collision imparts the configured shot response while the ball remains free and no shoot key or possession state is required

#### Scenario: Dash strikes a free ball
- **WHEN** a player in an active Dash has an eligible closing contact with a free ball
- **THEN** the approved Dash-impact response applies once for that contact episode and the ball can subsequently enter a defended goal

#### Scenario: Contact does not become possession
- **WHEN** a ball touches any side of a player or remains adjacent to that player
- **THEN** it remains an independently simulated free ball and no basket, carry, steal, or charge transition occurs

### Requirement: Base collision correctness is separate from gameplay impulses

The implementation SHALL preserve correct deterministic base collision resolution, including closing-only normal impulses, finite bounded separation, and the configured mass/restitution behavior for ordinary unmodified pairs. Enhanced shot and player-knockback impulses SHALL be explicit gameplay responses separate from that base solver. Strength, speed limits, impact qualification, and contact-release tolerances SHALL come from the approved immutable match rules profile, not unapproved candidate values or render-frame timing. A gameplay impulse SHALL apply at most once per eligible contact episode, independent of the number of solver iterations; positional nonpenetration resolution SHALL continue as needed. Impact responses SHALL remain bounded and SHALL NOT create NaN, infinity, or unlimited contact-driven acceleration.

#### Scenario: Repeated solver passes do not multiply a shot
- **WHEN** one player-ball contact is processed by multiple collision-solver iterations within a simulation tick
- **THEN** base nonpenetration can be solved repeatedly but the contact's additional gameplay shot impulse and impact event occur at most once

#### Scenario: Separating contact does not fire again
- **WHEN** bodies remain in contact or begin separating after an impact without beginning a new eligible contact episode
- **THEN** they do not receive another additional shot or knockback impulse merely because another tick is simulated

#### Scenario: Gameplay modifiers do not silently alter the base solver
- **WHEN** the isolated base solver is tested without Dash, buffs, or additional knockback
- **THEN** its ordinary mass, restitution, momentum, tangential-motion, and bounded-degenerate-contact contracts still hold

### Requirement: Player collisions produce stronger knockback without erasing external motion

Eligible player-player impacts SHALL add the approved profile's stronger gameplay knockback on top of the ordinary base contact response, for either seat. Ordinary-movement and Dash impact strength SHALL be validated separately at the G1 handfeel-approval gate; matched unobstructed tests SHALL demonstrate the configured enhancement relative to the same base-only contact rather than relying only on brighter effects. Active movement velocity and external impact velocity SHALL be represented and advanced separately so ordinary input handling or its speed cap does not immediately discard the received knockback on the following tick. External motion SHALL decay and respond to collisions according to the approved profile, without granting permanent loss of control or unlimited acceleration. The same contact-episode limit SHALL apply to these additional impulses. Green SHALL modify only eligible outgoing gameplay impact, and purple SHALL suppress received dynamic knockback while preserving solid nonpenetration, as defined in arena-buffs.

#### Scenario: Either seat can deliver the enhanced body collision
- **WHEN** mirrored ordinary or Dash player-player impact cases are run with identical approved parameters and no armor
- **THEN** the corresponding bodies receive equivalent seat-independent gameplay responses and the configured separating motion exceeds the matched unmodified base-only response

#### Scenario: Holding movement does not immediately cancel knockback
- **WHEN** an unarmored player receives an eligible impact while continuing to hold a movement direction
- **THEN** subsequent ticks retain and advance the external motion according to its approved decay instead of replacing it with the normal movement velocity or clamping it away at the normal movement speed limit

#### Scenario: Green and purple modify the explicit gameplay response
- **WHEN** a green-enhanced player's valid body impact reaches a purple-armored opponent
- **THEN** the enhanced outgoing response does not bypass the opponent's received-knockback immunity, both bodies still obey geometric nonpenetration, and the ordinary base-solver mathematical contract is not rewritten

### Requirement: Dash is a single-edge locked-direction action

Each player SHALL have an independent Dash press action using their current Dash binding. A valid fresh press SHALL start a Dash lasting exactly 18 simulation ticks, with no legacy Boost wind-up. Its direction SHALL be captured from nonzero movement input at activation, or from the player's last valid facing when movement input is zero. Steering input SHALL NOT change that captured direction during the Dash. Releasing the button SHALL NOT shorten the Dash, and holding it SHALL NOT extend or repeat the action. The Dash speed and acceleration response SHALL use the approved immutable match rules profile. Direction locking SHALL NOT grant immunity to geometry, falls, or permitted external collision effects.

#### Scenario: A fresh press produces one short Dash
- **WHEN** a living unstunned player whose Dash is ready makes a fresh Dash press
- **THEN** the action captures one direction and remains active for 18 ticks unless interrupted by a rule-defined event

#### Scenario: Steering cannot redirect an active Dash
- **WHEN** movement input changes while a Dash is active
- **THEN** the stored Dash direction stays unchanged and normal steering resumes only after the Dash ends or is interrupted

#### Scenario: Stationary Dash uses facing
- **WHEN** a ready player presses Dash without movement input
- **THEN** the Dash uses that player's last valid facing and does not select a random or camera-dependent direction

### Requirement: Dash cooldown starts on completion or interruption

A Dash that naturally completes or is interrupted SHALL immediately enter a 180-simulation-tick cooldown measured from that termination, not from activation. A player SHALL be able to move normally during cooldown unless another state forbids it. Dash presses received while dashing, cooling down, stunned, fallen, or otherwise unable to act SHALL be discarded rather than queued. A held button SHALL NOT cause automatic activation when eligibility returns; another physical press edge SHALL be required. Goal-reset and player-respawn policies SHALL follow the match-flow requirements and SHALL NOT replay discarded inputs.

#### Scenario: Cooldown follows the full Dash duration
- **WHEN** an uninterrupted 18-tick Dash ends
- **THEN** its 180-tick cooldown begins and no Dash can start during that cooldown

#### Scenario: Activation and readiness have exact tick boundaries
- **WHEN** a valid Dash starts at tick N and is not interrupted or reset
- **THEN** Dash movement is active for ticks N through N+17, cooldown starts at N+18 and blocks activation through N+197, and a fresh eligible press can start the next Dash at N+198 but a previously held or discarded press cannot

#### Scenario: Cylinder interruption starts cooldown immediately
- **WHEN** a nonimmune player hits a cylinder during an active Dash and becomes stunned
- **THEN** Dash ends on the contact tick and its 180-tick cooldown starts on that termination rather than waiting for the unused Dash duration

#### Scenario: Presses during cooldown do not fire later
- **WHEN** a player presses or holds Dash while it is cooling down and does not make a new press after readiness
- **THEN** no Dash automatically starts when the cooldown expires

### Requirement: Cylinder stun suppresses control without pausing the world

A living nonimmune player entering an eligible cylinder-contact episode SHALL be stunned for exactly 36 simulation ticks. The stun SHALL prevent movement-driven acceleration and Dash activation, interrupt an active Dash, and leave the player subject to ordinary solid constraints and nonimmune external collision effects. It SHALL NOT pause the ball, the other player, or world timers. Continued contact with the same cylinder SHALL NOT retrigger or refresh the stun until separation ends that contact episode. Purple armor SHALL exempt the player from this stun according to arena-buffs requirements.

#### Scenario: A cylinder hit briefly disables one player
- **WHEN** a living nonimmune player begins an eligible cylinder contact
- **THEN** that player loses movement and Dash control for 36 ticks while the rest of the match continues

#### Scenario: Remaining against a cylinder does not chain-stun
- **WHEN** a player remains in one continuous cylinder-contact episode after the stun was issued
- **THEN** that cylinder does not restart the player's stun every tick

### Requirement: High-speed movement resolves crossed contacts in a deterministic order

At every speed admitted by the approved profile, player and free-ball movement SHALL use deterministic swept collision queries or bounded substeps sufficient to resolve crossed dynamic circles, solid cylinders, wall segments, goal regions, and death zones. Checking only the final position of a tick SHALL NOT allow a Dash or ball to tunnel past an otherwise valid contact or region crossing. Substep bounds and contact ordering SHALL depend on authoritative simulation state and the approved profile, not render frame rate. Contacts SHALL be resolved by hit time; ties SHALL use the design's cylinder-before-breakable-wall type order and then stable entity identifiers. Later responses in a tick SHALL use the action state after earlier contacts, not a stale pre-contact Dash flag. These rules SHALL preserve the once-per-contact gameplay-impulse and feedback limits.

#### Scenario: Dash cannot skip a narrow obstacle or free ball
- **WHEN** an admitted-speed Dash would cross a solid cylinder, intact wall, or free scoring ball between the beginning and end of a simulation tick
- **THEN** the crossed contact is processed even if the final uncorrected pose would no longer overlap the object, with blocking, stun, a qualifying break, or a ball impact applied according to that object's rules

#### Scenario: Goal and death-zone crossings cannot be skipped
- **WHEN** an admitted-speed free ball crosses a valid goal region or a player crosses a valid death zone within a tick
- **THEN** the applicable goal or fall candidate is detected even when endpoint overlap alone would miss the crossing, and the match-flow event-order rules decide its outcome

#### Scenario: Same-time cylinder interruption invalidates a later wall-break attempt
- **WHEN** a nonimmune dashing player has equal-time contacts with a cylinder and a breakable wall
- **THEN** the cylinder is processed first, its stun terminates Dash and starts cooldown, and the wall cannot break using the Dash flag that was valid before that interruption

#### Scenario: Replay preserves a sequence of high-speed contacts
- **WHEN** the same admitted-speed input sequence is run with different rendering frame rates or replayed after window-bounded delayed input
- **THEN** the same contact order, action transitions, ball and player poses, and deduplicated impact identities result

### Requirement: Combat state is replayable and independent of presentation

Dash direction, remaining active ticks, cooldown, stun state, impact-response state, and contact-episode latches SHALL be authoritative simulation state restored and replayed with the match. Their timers SHALL use simulation ticks, not animation duration, wall-clock time, or particle lifetime. Combat effects and input presentation SHALL NOT mutate these values. Replayed combat SHALL reproduce the same physical state without duplicating committed audiovisual notifications.

#### Scenario: Late input replays a Dash impact and stun
- **WHEN** delayed remote input causes rollback across Dash activation, an impact, and cylinder interruption
- **THEN** the settled Dash, cooldown, stun, contact latches, body poses, and ball state match an on-time run and no duplicate committed impact or stun notification is emitted

#### Scenario: Network pause freezes combat timers
- **WHEN** the established network-failure pause stops simulation advancement
- **THEN** Dash, cooldown, and stun do not expire based only on elapsed real time
