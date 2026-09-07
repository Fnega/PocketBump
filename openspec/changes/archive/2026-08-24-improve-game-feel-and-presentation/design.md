## Context

The current game is built almost entirely at runtime from `MatchBootstrap`, `MatchDirector`, `Arena`, and `MatchHud`. Players use `Rigidbody2D`, but `PlayerMotor` moves velocity rapidly toward a target (`Accel = 55`, drag `= 4`), so movement reads as direct input rather than momentum. Marbles already accelerate while moving and can collide with players, but their low mass makes the collision weak. Presentation uses generated circles/boxes and legacy uGUI text in the same scene as gameplay.

This change improves the core competitive feel and presentation before power-ups or networking are introduced.

## Goals / Non-Goals

**Goals:**

- Make normal movement responsive but visibly inertial, with acceleration, coasting, braking, and slower reversal.
- Add independent hold-to-boost controls with a wind-up and a steering trade-off.
- Make fast marbles a readable physical hazard that can knock players toward dunk pockets.
- Separate main menu, rule setup, gameplay, and result states.
- Establish a consistent neon tabletop visual language using lightweight 2D assets and effects.
- Preserve current scoring, kickoff, marble-count, and win-by-two behavior.

**Non-Goals:**

- Arena power-ups or player status effects.
- Online multiplayer, gamepad rebinding, accessibility remapping, or input-system migration.
- New scoring rules, arena geometry, or additional maps.
- Heavy shader pipelines, 3D models, or large external art packages.

## Decisions

### 1. Use momentum-based velocity steering

`PlayerMotor` will keep `Rigidbody2D` as the source of truth. Input selects a desired movement direction, but velocity will approach that direction using separate acceleration, turning, and braking rates.

Initial tuning targets:

- Normal maximum speed: 8 units/s.
- Forward acceleration: 24 units/s².
- Braking with no input: 14 units/s².
- Reversal/turn acceleration: lower than forward acceleration when velocity opposes input.
- Rigidbody drag: low enough to allow coasting; active braking performs most deceleration.

Rationale: direct `transform` movement would undermine collision impulse, while the current high acceleration/drag pair removes useful inertia.

### 2. Boost is always available but has handling cost

Player one uses Left Shift and player two uses Right Shift. A shared Space key is rejected because either player could activate both boosts on the same keyboard.

Boost behavior:

1. Holding the boost key starts a 0.35-second wind-up.
2. After wind-up, maximum speed rises to 13 units/s and forward acceleration rises.
3. Steering authority while boosting is approximately 45% of normal.
4. Releasing boost returns to normal limits and leaves a short 0.4-second recovery/coast period.
5. There is no stamina meter in this change.

The handling penalty makes permanent boost suboptimal without adding another HUD resource.

### 3. Separate input, movement state, and feedback

`PlayerMotor` owns physical movement and exposes read-only speed/boost state. A small presentation component observes that state to control trail emission, color, and intensity. Match code only enables/disables input and does not tune physics directly.

### 4. Fast marbles use explicit collision knockback

Normal Unity contact remains active, but a marble-player collision handler adds controlled impulse when marble speed crosses the dangerous threshold.

Initial behavior:

- Below 6 units/s: normal physical contact only.
- 6–10 units/s: mild additional knockback.
- Above 10 units/s: strong knockback with dangerous visual state.
- Knockback scales with relative impact speed and impact direction.
- After applying dangerous knockback, the marble loses 25% of its speed to represent energy transfer.
- A short per-player collision cooldown prevents multiple callbacks from stacking impulse during one contact.
- Existing marble maximum speed remains capped at 16 units/s.

Rationale: increasing marble mass globally would also distort ordinary low-speed dribbling and wall rebounds.

### 5. Use two authored scenes and persistent session settings

Create:

- `MainMenu.unity`: title screen and rule-setup panel.
- `Arena.unity`: gameplay scene, HUD, result overlay, and authored arena references.

A small `GameSession` object survives scene loading with `DontDestroyOnLoad` and carries the selected `MatchSettings`. It also supports rematch and returning to the rule screen without persisting editor asset mutations.

Flow:

```
Main Menu -> Rule Setup -> Arena -> Result
                                |-> Rematch
                                |-> Modify Rules
                                `-> Main Menu
```

The current runtime-building path can remain temporarily during migration, but the final arena and UI should use scene/prefab references so art can be edited visually.

### 6. Use TextMeshPro and a small visual token set

TextMeshPro is already available in the project and becomes the UI text system. The presentation uses:

- P1 cyan and P2 orange as persistent identities.
- Red for shared dunk pockets/danger.
- Yellow for the closed center gate/countdown.
- A dark tabletop with subtle grid/edge accents.
- Layered sprites for player rim/core and marble highlight.
- Lightweight particles for score, gate opening, and dangerous impacts.

No external art dependency is required; imported sprites may be substituted later without changing behavior.

### 7. Trails communicate state

Each player and marble has a `TrailRenderer` (or equivalent lightweight pooled trail):

- Normal player movement: short, dim identity-colored trail.
- Boost: longer, brighter trail with increased width.
- Slow/stationary: emission disabled.
- Marble: short white trail above a speed threshold.
- Dangerous marble: longer high-intensity trail and glow.

Only two players and at most eight marbles exist, so individual trail components are simpler than a pooled particle-only system.

### 8. Result flow owns navigation, match flow owns scoring

`MatchDirector` continues to determine scores and the winner. It emits state/result events; menu/result controllers decide scene navigation. This prevents UI buttons from duplicating scoring/reset logic.

## Risks / Trade-offs

- [Boost is still optimal to hold] → Tune steering loss, wind-up, and recovery before adding stamina.
- [Movement becomes sluggish] → Keep normal acceleration responsive and reduce only reversal/turn authority; tune in a two-player playtest.
- [Dangerous marbles cause unavoidable chain knockback] → Use speed thresholds, energy loss, impact cooldown, and the existing max-speed cap.
- [Scene migration breaks the current auto-bootstrap] → Migrate in stages and disable auto-boot only after authored scenes contain all required references.
- [Visual effects obscure the ball] → Trails fade quickly, render below characters, and scale intensity by speed.
- [Two keyboard boost keys conflict with OS or keyboard rollover] → Use Left Shift and Right Shift initially and document them; gamepad/rebinding is a later change.

## Migration Plan

1. Introduce movement/boost state and dangerous-marble collision behavior while the existing runtime scene still works.
2. Add presentation components and verify effects in the existing arena.
3. Create persistent `GameSession`, `MainMenu.unity`, and rule setup.
4. Create/authenticate the authored `Arena.unity`, then transfer arena, HUD, and result references.
5. Update build settings to start from `MainMenu.unity`.
6. Disable the legacy automatic runtime bootstrap after scene validation.

Rollback is possible by restoring `SampleScene` as the first build scene and re-enabling the runtime bootstrap.

## Open Questions

- Final tuning values for normal acceleration, boost steering, and marble knockback require local two-player playtesting.
- Final sprite/material assets may be generated in-project or replaced with user-provided art, but the visual token rules remain fixed.
