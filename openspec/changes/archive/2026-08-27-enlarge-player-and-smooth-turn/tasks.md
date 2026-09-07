## 1. Size

- [x] 1.1 Set `PlayerRadius` to 0.78 and `BasketOffset` to 1.08
- [x] 1.2 Confirm player mesh scale still follows `PlayerRadius * 2` and the basket well stays outside the sphere

## 2. Facing

- [x] 2.1 Add `FacingTurnDegreesPerSecond` (720) and interpolate `StepFacing` with `deltaTime` via `Vector2.RotateTowards`
- [x] 2.2 Pass `Time.fixedDeltaTime` from `PlayerMotor`; keep `SetFacing` and spawn facings as instant snaps
- [x] 2.3 Update `FacingFollowsInputAndHoldsWhenReleased` so a 90° turn reaches east over several steps, not one

## 3. Verify

- [x] 3.1 Bake authored Arena so spawned players use the new radius
- [x] 3.2 Play: players read larger; holding D from south rotates the basket instead of jumping; scoop and dunk still work
- [x] 3.3 Run existing EditMode and PlayMode PocketBump tests
