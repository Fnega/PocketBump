## Context

Pocket Bump already runs a 60 Hz `MatchSim` for local and LAN. Playtesting found a steal-order bug and three feel knobs that are too timid: shot recoil, player-player restitution, and east/west dunk opening height.

Steal today: `WeldCarried` then `ProcessBaskets` loops P1 then P2. The marble stays at the previous carrier’s well until the next weld. P1 stealing from P2 is undone in the same tick when P2’s well still overlaps. Tests used `DebugCollect`, so they never hit this path.

Recoil is `RecoilImpulse (6–16) / PlayerMass (2.8)` → about 2–6 u/s vs shot speed 8–22. Player-player `BounceCircles` uses the same `Bounciness` 0.58 as walls and marbles. Dunk opening is `DunkPocketHeight` 2.15 vs player diameter ~1.56.

## Goals / Non-Goals

**Goals:**

- Either seat can scoop-steal; a marble is claimed at most once per tick.
- Charged-shot recoil about 2× current impulse.
- Player-player contacts stiffer than wall/marble contacts.
- East/west dunk openings 3× current height (6.45), with sim AABBs and arena meshes aligned.

**Non-Goals:**

- Changing north/south marble pocket width, table 32×48, or player radius.
- Raising global wall/marble bounciness.
- Protocol/version bump, Steam/Relay, or retuning dangerous-marble knockback (unless a test forces it).
- Visual restyle beyond the dunk lip scaling with the new opening.

## Decisions

### 1. Steal: stop after the first successful collect/steal on that marble this tick

In `ProcessBaskets`, after `TryCollect` succeeds for a marble, do not offer that marble to the other seat in the same tick.

- Alternative: reverse seat order — still asymmetric.
- Alternative: weld immediately after steal — extra coupling; claim-once is enough.
- Alternative: `CollectLock` on the victim — would also block legitimate re-steal next ticks; too coarse.

### 2. Recoil: double impulse constants, keep mass divide

Set `RecoilImpulseMin` 12 and `RecoilImpulseMax` 32. Keep `velocity += impulse / PlayerMass` so mass stays the single inertia knob.

- Alternative: stop dividing by mass — Δv 12–32 would rival max speed and often self-dunk into 6.45 wells.

### 3. Player-player restitution is its own constant

Add `PlayerPlayerBounciness` 0.90. Pass it only into `ResolvePlayers`. Leave `Bounciness` 0.58 for walls, marbles, basket rims, and player-marble.

- Alternative: raise global `Bounciness` — marbles and walls get snappier too; not requested.

### 4. Dunk height is one constant, 3×

`DunkPocketHeight = 2.15f * 3f` → **6.45f**. `MatchSim` static walls/wells/blockers and `Arena` wall splits plus dunk meshes already read this constant; changing it once should keep them in lockstep. Do not hardcode 2.15 in tests or specs.

East well height 6.45 on a 48-tall table still leaves ~21 units of wall above and below the gap.

## Risks / Trade-offs

- [Accidental dunks] → 6.45 wells + 2× recoil + 0.9 bumps will dunk more. Accepted; user asked for 3× openings.
- [Steal of a free marble both wells overlap] → P1 still wins (loop order). Unchanged; not reported as a problem.
- [LAN desync] → constants are compile-time; mixed builds would desync. Same as any `ArenaLayout` change.
- [PlayMode marble-dunk test] → wider wells may change knock-in timing; fix the test pose if needed, not the well.

## Migration Plan

Ship as a local/LAN feel patch. No save data. Rollback is revert the constants and the `ProcessBaskets` break.

## Open Questions

- None locked for implementation. If 6.45 + 0.90 feels too lethal after play, only the two floats change.
