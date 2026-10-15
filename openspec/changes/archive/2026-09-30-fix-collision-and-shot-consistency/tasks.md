## 1. Baseline and collision regression coverage

- [x] 1.1 Record the existing dirty-worktree scope and run the current EditMode and PlayMode suites; retain results and distinguish pre-existing failures before editing gameplay code.
- [x] 1.2 Add focused base-circle tests for approaching, separating-overlap, tangential, exact-touching, and noncontact cases; include equal/unequal masses, momentum, configured restitution, and ordinary pair-exchange symmetry, using only a narrow test seam if necessary.
- [x] 1.3 Add exact/near-coincident center tests on both sides of the normal-selection tolerance, including negative offsets, finite moving inputs, bounded inverse-mass separation, repeated resolution, and reproducible output; confirm the new regressions expose the current defects.

## 2. Correct the shared circle solver

- [x] 2.1 Fix the closing-velocity guard in `MatchSim.BounceCircles` while retaining the current normal/impulse convention, contact return semantics, masses, and restitution; make the normal-contact tests pass.
- [x] 2.2 Replace the degenerate normal calculation with the deterministic unit +X fallback and separate penetration accounting; make the finite, bounded, and replayable degenerate-contact tests pass.
- [x] 2.3 Exercise player/player, player/marble, and marble/marble contacts through the real simulation step, distinguishing base restitution from the existing additional knockback; verify separating base contacts are not reflected again.

## 3. Preserve option A through the real shot tick

- [x] 3.1 Add both-seat shot regressions that fire through `MatchSim.Step` at minimum, mid, 0.75, and full power, accounting for charge-time advancement; assert the 8–22 launch mapping, strictly increasing post-drag speed, and the current high-power clipping failure.
- [x] 3.2 Align the shared free-marble speed limit with `ShotMaxSpeed` without new per-ball state or tick-order changes; retain natural drag and verify full-power fire-tick speed near 21.29 plus subsequent coasting to rest in open space.
- [x] 3.3 Verify the shared drag/limit stage retains post-drag speeds between 16 and 22 and caps higher inputs at that stage; verify both-seat release, unchanged charge controls, and unchanged 12–32 recoil mapping.

## 4. Guard compatibility and replay

- [x] 4.1 Advance `NetplayProtocol.Version` from 4 to 5 and update version-specific protocol tests without changing packet layouts or handshake ordering.
- [x] 4.2 Add or update handshake tests for version-4 joiners against corrected hosts, pre-fix host acknowledgements against corrected joiners, and successful matching-version negotiation; assert rejection before playable ticks through the existing compatibility-failure path (`SETTINGS MISMATCH` is acceptable, with no archived-build UI changes).
- [x] 4.3 Add deterministic input tapes that actually exercise corrected circle contacts and high-power shots, then compare complete settled state and checksums against on-time baselines after delayed-input rollback for either seat; assert the intended gameplay events and at least one rollback occurred.

## 5. Gameplay and suite verification

- [x] 5.1 Run or strengthen focused regressions for basket collection, contact-steal latch, carried-no-score, existing scoring ownership, dangerous-impact cooldown and energy retention; preserve their gameplay contracts and document any physics-dependent fixture adjustment.
- [x] 5.2 Test full-power shots against walls, blocking pocket edges, basket rims, and empty basket interiors, including representative offsets; stop for user direction if supporting 22 requires an out-of-scope collision redesign or gameplay retuning.
- [x] 5.3 Run all EditMode and PlayMode tests after the fixes, retain XML/log evidence, and verify existing network-failure freeze, reason display, and manual menu behavior remain covered and unchanged.

## 6. Versioned build and packaged smoke checks

- [x] 6.1 Inspect existing releases and build a Windows development player in the next unused sequential `D:\uniry Assets\射球大战\PocketBump<version>` folder using the existing build helper (expected `PocketBump0.6` if `PocketBump0.5` is still latest); preserve all earlier folders and obtain filesystem approval if required.
- [x] 6.2 Run `Tools/Run-LanDiagnostics.ps1` against the new executable for a 3,600-tick normal run and a 900-tick dropped-first-control run; require both peers to pass each run and retain reports plus player logs.
- [x] 6.3 Record the final build path, compatibility version, automated results, and diagnostic directories in the implementation handoff; explain that both players must update and that real two-computer LAN acceptance remains separate from same-machine smoke evidence.
