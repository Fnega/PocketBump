## 1. Camera

- [x] 1.1 Change `MatchDirector.FrameCamera` (and the scene baker match camera) to an untilted perspective camera on the −Z axis looking at table center
- [x] 1.2 Size FOV and distance so the full 32×48 table plus pocket walls stay in view (same framing intent as `HalfHeight + 2.4`)
- [x] 1.3 Confirm a directional light exists in Arena so mesh tops read volume

## 2. Match meshes

- [x] 2.1 Add a small mesh/material helper: sphere, Z-aligned cylinder, cube; strip 3D colliders; apply identity-colored lit materials
- [x] 2.2 Swap table, walls, pockets, and gate visuals from sprites to meshes; keep existing 2D colliders and pocket triggers
- [x] 2.3 Swap player, marble, and basket visuals to meshes; keep `Rigidbody2D` / 2D colliders; lift charge needles slightly toward the camera to avoid z-fight
- [x] 2.4 Leave menu and match HUD as overlay Canvas UI

## 3. Bake and verify

- [x] 3.1 Bake authored scenes so Arena camera, light, and mesh-built pieces persist
- [x] 3.2 Play-mode check: W moves toward screen top; north/south and east/west walls visible; cyan/orange identities still readable
- [x] 3.3 Run existing EditMode and PlayMode PocketBump tests (physics and flow should be unchanged)
