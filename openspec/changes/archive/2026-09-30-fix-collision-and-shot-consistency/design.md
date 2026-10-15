## Context

Local and LAN matches use the same fixed 60 Hz `MatchSim`. The working tree already contains gameplay, presentation, network diagnostics, and confirmed-history desync fixes; this change must preserve them. There are also unrelated active OpenSpec changes, including scoring work whose main specification is not yet synchronized. This proposal does not reconcile those changes.

The defects are visible in the current implementation:

- `MatchSim.BounceCircles` defines the normal from A to B and computes `dot(va - vb, normal)`, but skips the impulse when that value is positive: the approaching case. All three dynamic circle pair types use this helper.
- Its coincident-center branch replaces the displacement with `(1, 0)` while setting squared distance to `1e-8`, yielding a normal of length 10,000 instead of one.
- `Fire` computes an 8–22 launch speed, then `StepMarbles` applies drag and a 16-unit limit during the same tick. At 60 Hz, shots above approximately 61% power therefore saturate at 16 despite increasing recoil. Existing direct `DebugShoot` assertions do not exercise that whole path.
- LAN compatibility currently uses protocol version 4, also included in the settings hash. Corrected physics must not run against the old rules.

The user selected **A: retain the 8–22 shot design**, not B: reduce the design to 8–16.

## Goals / Non-Goals

**Goals:**

- Correct the shared circle solver's base impulse sign and degenerate-position handling.
- Preserve an increasing shot-power/speed relationship through actual collision-free simulation ticks.
- Retain drag, recoil, mass, restitution, charge controls, and existing special impact rules.
- Prove physical behavior independently, then prove confirmed-input rollback reproduces it.
- Keep old rules out of a new LAN match and deliver a separately versioned Windows build after apply.

**Non-Goals:**

- No authoritative Unity Physics2D migration, fixed-point conversion, collision pipeline reorder, or general continuous-collision rewrite.
- No Boost, charge timing, recoil, dangerous-impact threshold/cooldown, scoring, stealing, or arena balance redesign.
- No reconnect/resynchronization, match-over/rematch lifecycle repair, tutorial, audio, art, map, or mode work.
- No claim that same-machine diagnostics establish reliability on every network or cross-platform floating-point determinism.

## Decisions

### 1. Retain the solver's convention and correct its approaching test

Use a unit normal from A to B and relative normal speed `vn = dot(va - vb, n)`. For an isolated overlapping/touching pair with positive finite masses:

- Correct penetration in inverse-mass proportions independently of whether bodies approach or separate.
- Apply the base normal impulse only when `vn > 0`.
- Keep the signed impulse formulation `j = -(1 + restitution) * vn / (inverseMassA + inverseMassB)`, with A receiving `n*j*inverseMassA` and B receiving its opposite.
- Preserve tangential velocity and pair momentum for this base solver; the outgoing normal relative speed is `-restitution * vn` within floating-point tolerance.
- Keep the helper's contact result distinct from whether it applied a normal impulse, so collection and special-impact callers are not silently redefined.

Do not change the configured 0.90 player/player or 0.58 other restitution values. Tests of this base impulse must isolate it from input acceleration, drag, walls, and the intentionally additional player/marble knockback. Use existing debug/test access patterns or a narrow non-stateful test seam if necessary, plus real `MatchSim.Step` integration cases for the callers.

Alternative: switch to `vb - va` and reverse all impulse signs. Rejected because a consistent existing convention needs a smaller correction. Merely increasing restitution would mask the sign defect.

### 2. Give degenerate contacts a deterministic unit normal

For reliably separated centers, normalize their actual displacement. When center separation is too small to reliably normalize, use the fixed unit +X axis in the existing deterministic pair order; do not divide a fabricated unit displacement by a tiny distance. Keep true separation/penetration accounting separate from normal selection and bound each correction by the pair's radius sum plus the numerical tolerance. Repeated isolated-pair resolution must remove overlap within tolerance without NaN, infinity, or a macroscopic position jump.

Use a named small-distance tolerance and test exact zero, offsets on both sides of that tolerance, and both positive and negative axes. Preserve the inverse-mass split. The fixed fallback deliberately does not promise rotation or seat-swap symmetry for the fully coincident, otherwise indistinguishable case; ordinary nondegenerate contacts must remain symmetric under exchanging the pair.

Alternative: choose a random axis or use render time. Rejected because replay must reproduce the result. A velocity-derived fallback is unnecessary behavior change for this narrow fix; retain the existing intended +X fallback, correctly normalized.

### 3. Align the shared free-marble limit with the shot maximum

Use `ShotMaxSpeed` as the source of truth for the existing free-marble speed limit, currently `MarbleMaxSpeed`. Preserve the 8–22 interpolation in `PlayerChargeMath`, the existing per-tick drag factor, and tick ordering. No per-marble shot exemption flag or new snapshot field is needed.

At the current drag and tick rate, a full-power shot in collision-free space has speed `22 / (1 + 2 / 60)`, approximately 21.29, at the end of its fire tick. That is expected drag, not a failure to preserve the 22-unit launch speed. Subsequent collision-free ticks continue to slow the marble; this change must not maintain a constant flight speed.

The shared cap applies to all free marbles at the existing drag/limit stage, so a collision-driven marble can also retain speeds above the previous limit of 16. It is not a new hard post-collision velocity clamp. Keep this consequence explicit and cover dangerous impacts and walls in regressions; do not silently compensate by retuning other constants.

Alternatives: reduce `ShotMaxSpeed` to 16 (rejected by the user); exempt newly fired marbles using extra replicated state (more state and two competing speed policies); move all drag/limits to another stage (unnecessary simulation-order change).

### 4. Version the changed simulation rules with the existing compatibility mechanism

Advance `NetplayProtocol.Version` from 4 to 5, update version-dependent tests, and retain packet layouts. Verify the existing handshake rejects a version-4 peer before either side begins a playable match and reports the existing compatibility failure. `LanHandshake` currently uses `SETTINGS MISMATCH` for version or settings rejection; retain that wording rather than adding a new version-specific UI in this change. The settings hash already incorporates the protocol version; a new configurable physics hash is deferred.

Test both corrected peers together and both old/new handshake role combinations using encoded old-version messages or existing development injection. Do not modify archived builds to make a test pass. Keep current network failure freeze, top-right reason, and manual menu action unchanged.

Alternative: leave version 4 because packet fields did not change. Rejected: peers could accept each other and later desync because their simulation rules differ.

### 5. Verify correctness first, then deterministic replay and integration

Verification layers:

1. Base contacts: approaching/separating/tangential motion, noncontact, equal/unequal mass, restitution, ordinary pair exchange, exact/near-coincident centers, and repeated resolution.
2. Actual tick shots for both seats: minimum, mid, and multiple high power readings (including full power), increasing resulting speed, first-tick drag, subsequent coasting, release of possession, and preserved recoil. Account for the tick's charge-time advance when choosing the fire input; a direct `DebugShoot` result alone is insufficient.
3. Integration regressions: all dynamic circle callers, high-speed wall/rim behavior, collection/steal latch, carried-no-score, current scoring, and dangerous-impact cooldown/energy retention. Record any changed expectations caused by corrected physics rather than deleting assertions.
4. Baseline versus delayed-input replay: construct tapes that demonstrably produce collisions and high-power shots, deliver remote inputs late within the supported window, and compare complete settled state and checksums at equal confirmed ticks for both seats. Assert the intended events and at least one rollback actually occurred.
5. Full existing EditMode/PlayMode suites and two same-version packaged players: a 3,600-tick normal diagnostic and a 900-tick dropped-first-control diagnostic with both reports passing. Retain logs; these smoke runs complement, not replace, gameplay-event tests or a later real two-computer LAN playtest.

## Risks / Trade-offs

- [Correct bounce impulses change familiar handfeel] -> Treat the math correction separately from balance; preserve coefficients and report visible differences for later tuning.
- [Faster high-power marbles amplify dangerous impacts or expose tunneling] -> Exercise shots against bodies, walls, rims, and collection areas. Keep thresholds fixed; pause for a scoped design decision if the existing collision pipeline cannot support the intended speed.
- [The fixed degenerate axis has a directional bias] -> Restrict it to unreliable center separation; require bounded replayable results and preserve normal-case symmetry.
- [Regression fixtures relied on incorrect physics] -> Compare their intended gameplay contract before updating positions or expectations; never weaken checks just to get green tests.
- [Main specs lag some existing changes] -> Add narrowly scoped delta requirements only; do not restore stale disconnect-to-menu or scoring behavior while applying this change.
- [Two peers can agree on an incorrect result] -> Keep analytical momentum/restitution assertions alongside checksum tests.
- [Mixed-build players cannot join] -> Explain that both peers need the new version and prove pre-game rejection instead of an in-match desync.

## Migration Plan

1. During apply, preserve the current worktree and establish the existing test baseline. Add focused regressions, implement the scoped fixes, and increment compatibility together.
2. Run and retain the focused and full suite results. Stop and report unrelated failures separately; do not mark unexecuted verification complete.
3. Inspect release directories and choose the next unused sequential version under `D:\uniry Assets\射球大战\`. At proposal time the latest known build is `PocketBump0.5`, so `PocketBump0.6` is the expected destination, subject to rechecking. Never overwrite an earlier release.
4. Build with the existing development-build workflow, run both packaged diagnostic cases, and record the executable path, rules version, test results, and diagnostic directories.
5. Ask both human players to use the same new build for real LAN acceptance. Keep network failures paused and visible as currently implemented.

No saved-data migration is expected. To roll back a release, both peers use the same preserved earlier build; do not reset the user's worktree or mix simulation versions.

## Open Questions

The shot-range choice is resolved: option A. No further product decision blocks this proposal. If implementation requires changing gameplay coefficients, introducing new replicated state, or expanding collision detection beyond this scope, pause and ask the user before doing so.
