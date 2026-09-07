## Why

Playtesting showed three feel problems on the shared tick sim: P2 can scoop-steal from P1 but P1 cannot keep a steal from P2 (same-tick steal-back), charged shots barely push the shooter, and player-player bumps are too soft to dunk reliably. East/west dunk openings are also only 2.15 units (~1.4 player diameters), so knockback rarely finishes a dunk.

## What Changes

- Fix scoop steal so either seat can take a carried marble when their empty well overlaps it; a marble is claimed at most once per simulation tick (no immediate steal-back because the marble has not welded yet).
- Increase charged-shot recoil to about 2× current impulse (`RecoilImpulseMin`/`Max` 6/16 → 12/32).
- Make player-player collisions stiffer with a dedicated restitution, without raising wall/marble/basket-rim bounciness.
- Widen east and west dunk openings to **3×** current height: `DunkPocketHeight` 2.15 → **6.45**. Wall gaps, sim wells/blockers, and the red dunk meshes stay in lockstep.
- Keep local Start and LAN on the same `MatchSim`; no protocol/version bump unless a constant is hashed (it is not).

## Capabilities

### New Capabilities

- None

### Modified Capabilities

- `marble-basket`: Steal is symmetric; one collect/steal per marble per tick. Charge fire recoil is strong enough to push the shooter backward.
- `local-two-player`: Player-player contacts use a higher restitution than table walls so bumps transfer more separating speed.
- `arena-pockets`: East/west dunk opening height is 6.45 (three times 2.15). Marble-only north/south pockets stay 3.4 wide.

## Impact

- `ArenaLayout` constants, `MatchSim` basket processing and `BounceCircles` restitution argument, `Arena` dunk meshes and east/west wall splits (must match sim AABBs).
- EditMode steal test must drive well overlap through `Step`, not only `DebugCollect`. Recoil and dunk-opening assertions if they pin old numbers.
- Views remain kinematic; no Physics2D gameplay truth. LAN checksums stay valid as long as both peers use the same build.
