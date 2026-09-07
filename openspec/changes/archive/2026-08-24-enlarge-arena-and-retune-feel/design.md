## Context

Pocket Bump is a local 2D physics duel. Layout, speeds, and marble behavior all live in `ArenaLayout` plus a few motors. The authored Arena scene is baked from `Arena.Build`, so wall positions are not derived at play time unless we rebake.

Today the table is 16×24. Players use `PlayerMovementMath.StepVelocity`: aligned input uses `NormalAcceleration` (24), misaligned input uses `TurnAcceleration` (13), and boost multiplies that by `BoostSteeringMultiplier` (0.45). Marbles add `MarbleAccel` (3.2) every physics tick once faster than 0.35, so they never settle.

## Goals / Non-Goals

**Goals:**

- Playable table 32×48 with the same piece radii and pocket opening sizes.
- Players feel faster (speed and accel ×1.5) and can turn at the same authority as they accelerate.
- Boost still raises max speed and accel, but does not punish steering.
- Marbles lose the scripted speed-up and stop through drag after they are left alone.
- Authored Arena and camera show the whole new table.

**Non-Goals:**

- Changing pocket rules, scoring, kickoff, or controls.
- Scaling pocket openings, wall thickness, or piece sizes.
- Art/visual restyle.
- Changing dangerous-knockback bands except as a side effect of marbles no longer self-accelerating.
- Network play.

## Decisions

### 1. Linear table scale, not area scale

`HalfWidth` 8→16, `HalfHeight` 12→24. Pocket widths/heights, `Wall`, `PlayerRadius`, and `MarbleRadius` stay.

**Why:** The user locked “边长 ×2” and “口袋大小不变”. Linear ×2 is 4× area; that emptiness is intended.

**Alternative:** Scale pockets with the table — rejected; dunks and marble goals would become too easy.

### 2. Rebuild authored geometry from the same builder

Keep using `Arena.Build` / `Tools/Pocket Bump/Bake Authored Scenes`. Camera already uses `HalfHeight + 2.4`. Drive grid lines from `HalfWidth`/`HalfHeight` instead of hardcoded `-9..9` / `-6..6`.

**Why:** Walls in `Arena.unity` are baked transforms. Changing constants without a rebake leaves old walls around a new camera.

**Alternative:** Runtime rebuild every play — unnecessary while the baker already exists.

### 3. Scale player accel with max speed; turn accel equals forward accel

| Constant | From | To |
|---|---|---|
| NormalMaxSpeed | 8 | 12 |
| NormalAcceleration | 24 | 36 |
| TurnAcceleration | 13 | 36 |
| BrakeAcceleration | 14 | 21 |
| BoostMaxSpeed | 13 | 19.5 |
| BoostAcceleration | 38 | 57 |
| BoostSteeringMultiplier | 0.45 | 1.0 |

**Why:** Speed without accel feels sluggish to top speed. Turn 13 vs forward 24 is why steering feels sticky; boost ×0.45 made it worse. Brake ×1.5 keeps coast/stop time in the same ballpark as the new top speed.

**Alternative:** Rotate heading while preserving speed (ice-skate). Rejected for this change — keep the existing MoveTowards velocity model, just retune it.

### 4. Delete marble self-acceleration; raise drag so they fully stop

Remove the `MarbleMotor.FixedUpdate` speed-add. Set marble `Rigidbody2D.drag` from 0.15 to **2.0** (angular drag can stay ~0.25). Keep `BumpBounce` bounciness. Dangerous knockback stays speed-threshold based; without scripted accel, danger only happens after a hard hit, then the marble should slow and stop.

**Why:** User asked for 珠子完全停. Drag 2.0 brings a typical post-hit speed under the rest threshold in about 1–2 seconds, which reads as “friction” rather than “ice forever”.

**Alternative:** Keep a tiny accel so marbles slowly wander — rejected. Zero scripted accel.

### 5. Leave dangerous speed thresholds at 6 / 10

Players can still create a dangerous marble by hitting it hard (boost max 19.5). They will not become dangerous by waiting.

**Why:** The complaint was runaway, not “danger should disappear”.

## Risks / Trade-offs

- [Larger table + only 1.5× speed] → End-to-end travel is ~4s vs ~3s today. Accept as more room to play.
- [Drag 2.0 too sticky or too icy] → Tune one constant; do not bring scripted accel back.
- [Forgetting to rebake] → Play camera zooms out around old walls. Mitigation: baker is a required task; verify wall extents in Play Mode.
- [Boost without steering penalty] → Boost is strictly better. Accept; user asked to remove the 0.45 cut.
- [Dangerous marbles rarer] → Skill shot instead of timer. Accept.

## Migration Plan

1. Change constants and motors in scripts.
2. Fix grid generation to follow table size.
3. Bake authored Arena (and confirm MainMenu camera unchanged).
4. Play: walk the new perimeter, turn at speed, boost-turn, nudge a marble and wait for rest.
5. Rollback: revert `ArenaLayout` and marble drag, rebake.

## Open Questions

- None locked by the user. If drag 2.0 feels wrong in play, only that number should move.
