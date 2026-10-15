## REMOVED Requirements

### Requirement: Each player has a front basket that follows movement input
**Reason**: The approved robot collision game has no basket attachment.
**Migration**: Remove basket visuals, rims and authoritative basket geometry; robot facing remains movement-driven with the Dash direction-lock exception under `local-two-player` and `dash-combat`.

### Requirement: Basket contact collects one free marble
**Reason**: Scoring balls remain free and are never picked up or carried by a player.
**Migration**: Replace collection/possession with ordinary and enhanced body-ball contact defined by `dash-combat`; remove carrier state and do not confuse scoring balls with collectible Buff entities.

### Requirement: Basket mouth is wider than a marble
**Reason**: No basket mouth or collection well exists in the new gameplay.
**Migration**: Validate robot body/scoring-ball geometry and approved scene footprints instead of preserving obsolete rim widths or well tests.

### Requirement: Empty basket catches a flying marble and body bounces it
**Reason**: Catching is removed; every active scoring ball stays a freely simulated body after contacting a robot.
**Migration**: Use the new body-ball collision-shot and dangerous-impact requirements with no catch exception or carrier transition.

### Requirement: Opponent basket overlapping a held marble steals it
**Reason**: No player possesses a scoring ball, so stealing and basket-contact transfer episodes no longer exist.
**Migration**: Remove steal state/events and tests; add symmetric body-contact shot/knockback episode tests that retain replay and continuous-contact protections without possession.

### Requirement: Held marble is launched only by a two-stage charge shot
**Reason**: Independent shooting, direction/power charge stages and firing recoil are explicitly removed.
**Migration**: Move plus edge-triggered Dash is the control contract; body-ball impact generates a collision shot without a shoot key. Remove obsolete charge state, key labels, input bits, animation and firing helper dependencies.

### Requirement: Carried marble does not score in marble pockets
**Reason**: Carried scoring balls cannot exist in the redesigned state schema.
**Migration**: North/south goals evaluate only active free scoring-ball entities and award the opponent of the defended goal's owner; Buff pickups are not scoring balls.

### Requirement: Shot power retains the full intended speed range through a simulation tick
**Reason**: The old 8–22 charge-power mapping and 12–32 firing recoil range are not the new body-impact/Dash rules.
**Migration**: Retire charge-specific assertions with explicit replacement coverage for normal, Dash and approved green-strengthened body-ball shots, natural drag, validated speed limits, swept collisions and replay equality. Preserve valid base collision mathematics; do not silently keep an obsolete shot-speed clamp that erases new impact differences.
