## Why

The current 16×24 table feels cramped once players have momentum, turning is weaker than forward acceleration (and even weaker during boost), and marbles script-accelerate forever after any nudge. The table needs more room, players need to keep up without becoming tanks, and marbles need to stop like physical objects.

## What Changes

- Scale the playable table to **32×48** (linear size ×2). Player, marble, wall thickness, and pocket opening sizes stay as they are.
- Scale player **max speed and accelerations ×1.5**: normal max 8→12, forward accel 24→36, boost max 13→19.5, boost accel 38→57. Brake accel scales with speed (14→21) so stopping still feels intentional.
- Make **turn acceleration equal to forward acceleration** (36). Boost **no longer multiplies steering by 0.45**.
- **Remove marble self-acceleration** (`MarbleAccel` per-frame speed add). Raise marble linear drag so a moving marble comes to rest on its own.
- Rebuild/rebake authored Arena geometry, camera framing, and grid lines so they match the new table extents. **BREAKING** for any saved scene that still uses the old 16×24 wall positions.

## Capabilities

### New Capabilities

- (none)

### Modified Capabilities

- `arena-pockets`: Table extents become 32×48; pocket openings stay the previous sizes; marbles no longer self-accelerate and SHALL come to rest through drag.
- `local-two-player`: Player speed/acceleration rise; turning uses the same acceleration as forward movement; boost no longer reduces steering authority; the shared camera still shows the whole (now larger) table.

## Impact

- `ArenaLayout` constants, `PlayerMovementMath` turn/boost steering, `MarbleMotor` FixedUpdate, marble `Rigidbody2D.drag` in `Arena.CreateMarble`.
- Authored `Arena.unity` (and baker `PocketBumpSceneBaker`) because walls, floor, gate, pockets, and camera are currently baked to 16×24.
- Grid drawing in `Arena.BuildWalls` uses hardcoded ranges that will miss the new table unless they follow `HalfWidth`/`HalfHeight`.
- EditMode movement tests stay valid if they compare against `ArenaLayout` constants; add coverage that marbles do not self-accelerate and that boost turning is not weaker than normal turning.
- Kickoff, scoring, controls, and HUD flow stay the same.
