## 1. Layout and camera

- [x] 1.1 Set `ArenaLayout.HalfWidth` to 16 and `HalfHeight` to 24; leave pocket sizes, wall thickness, and piece radii unchanged
- [x] 1.2 Drive Arena grid lines from `HalfWidth`/`HalfHeight` instead of hardcoded `-9..9` / `-6..6`
- [x] 1.3 Confirm `MatchDirector.FrameCamera` and the scene baker still use `HalfHeight + 2.4` so the full 32×48 table is visible

## 2. Player movement

- [x] 2.1 Scale player speed/accel in `ArenaLayout`: normal max 12, forward accel 36, turn accel 36, brake 21, boost max 19.5, boost accel 57
- [x] 2.2 Set `BoostSteeringMultiplier` to 1 so boost no longer cuts steering
- [x] 2.3 Add or adjust EditMode tests: perpendicular turn uses forward accel; boost turn is not slower than a normal turn

## 3. Marble rest

- [x] 3.1 Remove `MarbleMotor` per-tick speed add (`MarbleAccel`); leave knockback bands unchanged
- [x] 3.2 Set marble `Rigidbody2D.drag` to 2.0 in `Arena.CreateMarble` (and any baked marble prefab/pool objects)
- [x] 3.3 Add an EditMode or PlayMode test that a marble given a moderate speed with no collisions loses speed and does not script-accelerate

## 4. Authored scene and verify

- [x] 4.1 Bake authored scenes so `Arena.unity` walls, floor, gate, pockets, and camera match the new extents
- [x] 4.2 Play-mode check: walk the new perimeter, turn at speed, boost-turn, nudge a marble and wait until it fully stops
- [x] 4.3 Run existing EditMode and PlayMode PocketBump tests
