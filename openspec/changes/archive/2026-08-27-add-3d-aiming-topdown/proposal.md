## Why

The match is a flat neon sprite table viewed with an orthographic camera, so aiming is readable but the table has no volume. The camera should stay a straight-down aiming top-down (screen directions match WASD / arrows and basket facing) while the table, pieces, baskets, and pockets read as 3D objects.

## What Changes

- Replace the match camera with a **straight-down aiming top-down**: looking at table center along the view axis, not a 45° isometric tilt, so push-where / face-where still maps 1:1 to the screen.
- Keep the **whole 32×48 table** (walls, four pockets, both players, HUD) in one view.
- Give the table, walls, pockets, gate, players, marbles, and baskets **3D volume** (primitives or equivalent meshes) and a directional light so height is visible from above.
- Keep **2D physics and all current rules**: pockets, dunks, basket scoop/steal, two-stage charge shot, recoil, bindings. Pieces do not leave the play plane. **BREAKING** for authored Arena/MainMenu cameras and sprite-only match visuals.

## Capabilities

### New Capabilities

- (none)

### Modified Capabilities

- `local-two-player`: The shared camera is a straight-down aiming top-down that still shows the full table; screen up remains table north.
- `visual-presentation`: Match pieces have 3D volume and lighting while keeping cyan / orange / red / yellow identities.

## Impact

- `MatchDirector.FrameCamera` and the scene baker camera (currently ortho size `HalfHeight + 2.4` at `(0,0,-10)`).
- Arena / player / marble / basket / FX builders that currently use `SpriteRenderer` for match objects (menu/HUD canvases stay overlay UI).
- Lighting in authored Arena (and Main Menu only if the menu shows a table preview).
- PlayMode/visual checks that the full table remains framed and that W still means screen-up / north.
- No change to `Rigidbody2D` movement, pocket scoring, or `ControlBindings`.
