## Why

Players read small on the 32×48 table, and the front basket snaps between eight keyboard headings in a single physics step, which feels like a hitch rather than a turn. Both should change together: a larger body and facing that rotates toward input instead of jumping.

## What Changes

- Scale player collision and mesh radius to **1.5×** the current `PlayerRadius` (0.52 → 0.78).
- Scale **basket offset** by the same 1.5× so the well stays in front of the sphere and does not clip into the body.
- Rotate basket / logic facing toward movement input at a capped angular speed instead of snapping to the 8-way heading.
- Leave marble size, player mass, max speeds, accelerations, and basket well dimensions unchanged unless play shows a dunk or scoop problem.
- **BREAKING** for tests and specs that assert facing is instantly east on the frame D is held.

## Capabilities

### New Capabilities

- (none)

### Modified Capabilities

- `local-two-player`: Players are larger; facing rotates toward movement input instead of snapping.
- `marble-basket`: The basket stays mounted in front of the larger body and turns smoothly with facing.

## Impact

- `ArenaLayout.PlayerRadius` and `BasketOffset`.
- `PlayerChargeMath.StepFacing` (and `PlayerBasket.ApplyFacing` if visual rotation is applied there).
- Player spawn scale in `MatchDirector.CreatePlayer` / baker (scale follows radius).
- EditMode tests that assume instant facing (`Player one faces east`) and turn-acceleration tests if they stay as-is.
- Rebake authored Arena so player meshes match the new radius.
- Dunk pocket height (2.15) still fits a 1.56-diameter body; scoop/steal feel may change slightly because the well size stays the same.
