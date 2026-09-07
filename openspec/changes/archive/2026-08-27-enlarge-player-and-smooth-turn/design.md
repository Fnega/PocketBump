## Context

Player radius is `0.52` (mesh scale `PlayerRadius * 2`). The basket sits at `BasketOffset = 0.72` and `ApplyFacing` writes rotation every `FixedUpdate`. `PlayerChargeMath.StepFacing` returns `input.normalized` immediately, so WASD facing jumps 45°/90° per press. Velocity steering already uses the same acceleration as forward motion; the hitch is the basket, not `MoveTowards` on velocity. Player rigidbodies freeze rotation; only the basket communicates heading.

## Goals / Non-Goals

**Goals:**

- Player collision and mesh radius 1.5× current (`0.78`).
- Basket offset 1.5× (`1.08`) so the well stays in front of the sphere.
- Facing rotates toward non-zero movement input at a capped angular speed; hold last facing when input is zero.
- Spawn facings stay instant (P1 south, P2 north). `SetFacing` stays an instant set for tests and scripted placement.

**Non-Goals:**

- Changing marble radius, player mass, max speeds, or accelerations.
- Changing basket well / rim sizes (scoop window stays the same).
- Smoothing velocity steering (already full turn accel).
- Analog stick / Input System.

## Decisions

### 1. Scale radius and offset only

`PlayerRadius = 0.78`, `BasketOffset = 1.08`. Mass stays `2.8`. Dunk pocket height `2.15` still clears a `1.56` diameter.

**Why:** Visual and collider stay in sync; offset must grow or the well clips the sphere. Mass/speed retune is a later feel pass.

**Alternative:** Scale basket width/length 1.5× — easier scoop and steal; rejected until the larger body is playable.

### 2. Interpolate facing in `StepFacing` with `deltaTime`

`Vector2.RotateTowards(current, input.normalized, FacingTurnDegreesPerSecond * Deg2Rad * dt, 0)`. Call from `FixedUpdate` with `fixedDeltaTime`. Default **720°/s** (90° in 0.125s). Zero input still returns current.

**Why:** One function, logic and basket stay aligned (shot/scoop use the same facing as the well).

**Alternative:** Instant logic facing + visual-only slerp — shot direction would lead the basket. Rejected.

### 3. `SetFacing` does not go through the interpolator

Keep `SetFacing` as a snap to the given vector so PlayMode collect/steal tests do not wait for a turn.

**Why:** Tests place baskets; spawn already snaps via `SpawnFacing`.

### 4. Rebake Arena after radius change

`CreatePlayer` already scales from `PlayerRadius`. Authored scene players must be rebaked.

## Risks / Trade-offs

- [Same-size well on a larger body is harder to scoop] → Accept for this change; widen the well only if play complains.
- [720°/s still feels snappy on 8-way taps] → Lower toward 540°/s if needed; keep above ~360°/s so a 90° turn finishes in under 0.3s.
- [EditMode facing test assumes one-step east] → Drive `StepFacing` with enough `dt` (or several steps) until the heading is east.

## Migration Plan

1. Change layout constants; add `FacingTurnDegreesPerSecond`.
2. Give `StepFacing` a `deltaTime` argument; update `PlayerMotor` and EditMode tests.
3. Bake Arena.
4. Play: size readable, D from south rotates the basket instead of jumping, scoop/dunk still work.
5. Rollback: restore `0.52` / `0.72` and instant `StepFacing`.

## Open Questions

- None. Basket well size and mass stay as they are unless a follow-up change.
