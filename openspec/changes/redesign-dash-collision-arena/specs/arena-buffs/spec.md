## ADDED Requirements

### Requirement: Buff tuning is approved and immutable during a match

The arena SHALL support green impact enhancement, blue random safe teleport, and purple armor. Their strengths, durations, spawn intervals, active-pickup limits, locations, and stacking policy SHALL be supplied by a validated immutable match rules profile whose implementation values have passed the parameter-approval gate described in the design. Candidate values awaiting confirmation SHALL NOT be treated as approved gameplay constants. Both LAN peers SHALL agree on that profile before gameplay. Buff generation and pickup processing SHALL occur only during Live play.

#### Scenario: Pending candidate values do not become implementation authority
- **WHEN** a buff parameter still has an unanswered confirmation item in the design
- **THEN** implementation of that parameter remains gated rather than silently treating the candidate value as user-approved

#### Scenario: Both peers use the same buff rules
- **WHEN** a LAN match starts with buffs enabled
- **THEN** both peers use the same approved generation, duration, strength, and stacking profile throughout that match

### Requirement: Pickup resolution is deterministic and emits one personal explanation

Only living eligible players SHALL collect pickups. Each pickup SHALL have a stable simulation identity and SHALL be consumed at most once. Simultaneous pickup contenders SHALL be resolved by a documented deterministic rule from the match design, not scene-callback or packet-arrival order. A successful consumption SHALL emit one player-addressed explanation event, including the buff identity and its effect or outcome. The event SHALL not pause gameplay or trigger a modal interaction; presentation SHALL use the player's applicable viewport and include a name or symbol in addition to color. Rollback replay SHALL NOT duplicate committed explanation events.

#### Scenario: Both players touch one pickup together
- **WHEN** both eligible player bodies reach the same pickup during one simulation tick
- **THEN** the documented deterministic arbitration grants it to exactly one player and consumes that pickup once on both peers

#### Scenario: The pickup explanation does not stop the opponent
- **WHEN** a player consumes a buff
- **THEN** that player's viewport receives a nonmodal effect explanation while the ball, opponent, and simulation continue

### Requirement: Green enhances outgoing impact rather than movement

Green SHALL increase the recipient's eligible outgoing player-body and free-ball gameplay impact response using the approved profile. It SHALL NOT increase ordinary movement speed, change Dash duration or cooldown, bypass the impact contact latch, or independently make an ordinary collision qualify as a Dash wall break. The base circle solver SHALL remain separate from the added gameplay response. When the target has purple armor, the target's specified immunity SHALL take precedence over received green-enhanced knockback while solid nonpenetration remains enforced.

#### Scenario: Green strengthens a valid ball impact
- **WHEN** a green-buffed player produces an eligible body-ball shot contact
- **THEN** the outgoing gameplay impact uses the approved enhanced strength once for that contact episode without changing the player's ordinary movement speed

#### Scenario: Green does not bypass armor or wall-break eligibility
- **WHEN** a green-buffed player collides with an armored opponent or touches a wall outside a qualifying high-speed Dash
- **THEN** the opponent retains its specified knockback immunity and the wall does not break solely because of green

### Requirement: Purple prevents received dynamic knockback and cylinder stun

Purple armor SHALL prevent received external dynamic knockback from player and free-ball impacts and SHALL prevent cylinder stun for its approved duration. Acquiring purple SHALL clear an already active cylinder stun under the interaction policy proposed in the design. Armor SHALL NOT remove geometric nonpenetration, make players intangible, prevent entering a death zone, or prevent the resulting fall/respawn transition. Armored player contacts SHALL still be able to strike balls and interact with solid obstacles. Cylinder contact episodes SHALL still be latched while immune so remaining against a cylinder when armor expires does not manufacture a new entry event.

#### Scenario: Armor blocks incoming knockback but not overlap correction
- **WHEN** an armored player is struck by another player or a free ball
- **THEN** received dynamic knockback is suppressed but the applicable solid overlap constraints are still resolved

#### Scenario: Armor prevents a cylinder stun
- **WHEN** an armored player begins contact with a solid cylinder
- **THEN** the cylinder remains solid, the contact is recorded, and no stun is applied

#### Scenario: Armor expires during sustained cylinder contact
- **WHEN** purple expires while the same cylinder-contact episode is still ongoing
- **THEN** expiration alone does not trigger a stun, and another eligible entry requires separation first

#### Scenario: Acquiring armor clears current cylinder stun
- **WHEN** an otherwise eligible pickup acquisition grants purple to a currently cylinder-stunned player
- **THEN** the existing cylinder stun is cleared without restarting a previously interrupted Dash or canceling its cooldown

#### Scenario: Armor does not prevent falling
- **WHEN** an armored player's body enters a valid death zone
- **THEN** the ordinary no-point fall and respawn rules apply

### Requirement: Blue selects a bounded deterministic safe destination

Consuming blue SHALL attempt one immediate random teleport using authoritative deterministic random state and a bounded stable set of destination candidates. Destination validation SHALL account for the full player collider, arena bounds and death zones, solid cylinders, currently solid or repair-pending walls, living opponents, and free scoring balls. The destination SHALL be in an approved reachable safe region, not merely a visually empty point. Candidate enumeration, random consumption, and fallback policy SHALL be identical under replay and on both peers; no unbounded random retry loop SHALL be permitted. Successful teleport SHALL clear velocity and any active Dash, starting its 180-tick cooldown if that Dash was interrupted, without resetting an already running cooldown. It SHALL not grant unspecified invulnerability.

#### Scenario: Blue avoids unsafe and occupied positions
- **WHEN** blue selects among candidate destinations
- **THEN** it rejects any destination whose full player collider conflicts with a death zone, solid or repair-pending obstacle, living opponent, or free scoring ball

#### Scenario: Teleport interrupts Dash cleanly
- **WHEN** a player successfully teleports while dashing
- **THEN** the player arrives with zero velocity, no active Dash, and a cooldown started at that interruption

#### Scenario: No safe destination is available
- **WHEN** bounded candidate evaluation finds no valid destination
- **THEN** the pickup is still consumed once, the player remains at the original pose without a partial teleport, one no-safe-destination outcome event is emitted, and the documented bounded random-state transition is preserved

#### Scenario: Delayed input reproduces the teleport
- **WHEN** rollback replays a blue pickup from the same authoritative random state and world state
- **THEN** the same destination or no-safe-destination outcome and random-state advancement result

### Requirement: Buff lifecycle respects Live play and goal resets

Buff durations and spawn clocks SHALL be measured in authoritative simulation ticks using the approved lifecycle profile. Generation and collection SHALL be disabled outside Live play and all buff clocks SHALL stop when simulation is network-paused or MatchOver. Under the goal-reset policy proposed in the design, a goal SHALL clear player buffs and existing pickup instances and restart the spawn schedule for the next Live round without resetting persistent obstacle state. Falling SHALL clear the fallen player's buffs under that profile without deleting other players' buffs or restarting the world spawn schedule. Buff state, pickup identities, spawn-clock state, and random state SHALL be restored with rollback snapshots and included in consistency verification.

#### Scenario: A goal begins a fresh buff round but preserves obstacles
- **WHEN** a valid goal causes a round reset
- **THEN** player buffs and active pickups are cleared and the next Live spawn schedule restarts while wall breakage and repair progress remain intact

#### Scenario: One player falls without resetting the arena buffs
- **WHEN** a player falls during Live play
- **THEN** the fallen player's buffs are cleared while the opponent's buffs, existing unconsumed pickups, and world spawn schedule continue according to their simulation ticks

#### Scenario: Presentation time does not expire a paused buff
- **WHEN** the network-failure screen freezes the simulation
- **THEN** an active buff does not expire merely because real time or an effect animation has advanced
