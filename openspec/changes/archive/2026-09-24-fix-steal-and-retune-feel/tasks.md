## 1. Steal claim-once

- [x] 1.1 In `ProcessBaskets`, after a successful `TryCollect` for a marble, skip remaining seats for that marble on the same tick
- [x] 1.2 EditMode: P2 carrying, P1 well overlaps that marble through `Step` (not only `DebugCollect`) → P1 holds at end of tick; reverse still works

## 2. Recoil and player-player bounce

- [x] 2.1 Set `RecoilImpulseMin` 12 and `RecoilImpulseMax` 32
- [x] 2.2 Add `PlayerPlayerBounciness` 0.90 and pass it only to `ResolvePlayers`; leave `Bounciness` 0.58 for walls/marbles/rims
- [x] 2.3 EditMode: rest, full-power shot east → shooter westward speed ≥ 8 after the fire tick

## 3. Dunk openings 3×

- [x] 3.1 Set `DunkPocketHeight` to 6.45; confirm `MatchSim` wells/blockers/wall gaps and `Arena` dunk meshes/wall splits all read that constant (no leftover 2.15)
- [x] 3.2 Update any test or spec-asserted 2.15 dunk height to 6.45; north marble pocket stays 3.4

## 4. Suite

- [x] 4.1 Run PocketBump EditMode and PlayMode suites; fix regressions (especially marble-knock dunk timing with the wider well)
