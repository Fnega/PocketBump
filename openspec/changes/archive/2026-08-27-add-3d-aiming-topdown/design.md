## Context

Match play lives on the XY plane with `Rigidbody2D`. The camera is orthographic at `(0, 0, -10)` with size `HalfHeight + 2.4`, so it already looks straight at the table — but pieces are flat sprites, so there is no aiming-top-down *volume*. Basket facing, WASD, and “screen up = north” must stay aligned. Physics, pockets, and charge shot stay as they are.

## Goals / Non-Goals

**Goals:**

- Straight-down aiming camera: no pitch/yaw tilt. Screen up is table north; screen right is table east.
- One camera still shows the full 32×48 table plus pockets.
- Table, walls, pockets, gate, players, marbles, and baskets have 3D mesh volume and a light so height is readable from above.
- Overlay HUD/menu unchanged.

**Non-Goals:**

- 45° isometric or follow-cam.
- Switching to `Rigidbody` / 3D colliders.
- Pieces leaving the play plane (jumps, falling off the table).
- Reworking basket, steal, charge shot, or scoring rules.
- Authored art packs or a new render pipeline.

## Decisions

### 1. Keep the XY play plane; camera stays on −Z looking at origin

Do not rotate the world onto XZ. 2D physics, basket local facing, and existing tests keep working. The aiming top-down is the same axis as today, with perspective instead of ortho.

**Why:** User asked for 瞄准俯视 (aiming top-down), not a tilted cinematic camera. A 45° view would shear screen directions vs WASD.

**Alternative:** XZ ground + Y-up + `Rigidbody` — rejected for this change; that is a physics rewrite.

### 2. Perspective, not tilted ortho

`Camera.orthographic = false`. Place the camera on the −Z axis, rotation identity (looking toward +Z / the table). Choose FOV and distance so the vertical extent `HalfHeight + margin` fills the view (same framing intent as `HalfHeight + 2.4` ortho). Background stays `VisualPalette.Tabletop`.

**Why:** Ortho + flat sprites is what we have. Perspective from directly above gives sphere/cylinder shading without breaking aim.

**Alternative:** Keep ortho and only swap meshes — weaker “3D”; still acceptable if perspective framing fights the HUD, but try perspective first.

### 3. Built-in primitives for match meshes; 2D colliders stay on the same GameObjects

`CreatePrimitive` (sphere, cylinder, cube), strip the 3D collider, keep `CircleCollider2D` / `BoxCollider2D`. Cylinder axes aligned with Z (thickness toward camera). Neon identity colors on `MeshRenderer` materials. One directional light already baked in Arena; aim it so top faces read volume (not a second sun).

**Why:** Smallest visible 3D. No new packages.

**Alternative:** Keep SpriteRenderer and only change the camera — sprites will look like paper from a perspective camera.

### 4. Charge needles and trails stay world-space, slightly toward the camera

Charge bars remain XY-oriented quads/cubes just off the table toward −Z so they are not z-fought. Trails stay `TrailRenderer`.

**Why:** Spec already requires on-player direction/power readings.

## Risks / Trade-offs

- [Perspective squeezes the far north/south edge] → Frame with extra margin; if HUD or pockets clip, lower FOV and push the camera back, still untilted.
- [3D colliders accidentally added] → Strip primitive colliders in the factory so dunks/pockets stay 2D triggers.
- [Unlit sprites vs lit meshes] → Identity colors must still read cyan/orange/red/yellow under the directional light.
- [Authored scenes still ortho] → Rebake Arena (and any match camera on MainMenu) via the existing baker.

## Migration Plan

1. Add a mesh/material helper; swap match visuals off sprites.
2. Switch `FrameCamera` to untilted perspective that still frames the table.
3. Ensure a directional light on Arena.
4. Bake authored scenes.
5. Play: W = screen up, full table visible, baskets/charges still readable.
6. Rollback: restore ortho `FrameCamera` and sprite builders.

## Open Questions

- None. Camera is straight-down aiming top-down; physics stays 2D; no table-exit.
