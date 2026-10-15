## Why

The shared simulation currently skips the normal collision impulse for approaching circles, applies it to separating circles, and can produce an oversized position correction for coincident centers. It also launches shots at an intended 8–22 units/second but clamps free marbles to 16 in the same tick, flattening high-power shots while recoil still increases; these correctness defects should be fixed before further gameplay balancing.

## What Changes

- Correct approaching/separating contact handling for player/player, player/marble, and marble/marble circles while retaining configured masses and restitution.
- Make coincident and near-coincident circle separation bounded, finite, and deterministic.
- Adopt the user's selected **option A**: preserve the 8–22 launch-speed range, the current charge controls, recoil, and natural drag; align the free-marble speed limit with the intended maximum shot speed instead of reducing shots to 16. The shared limit also applies to collision-driven free marbles; no shot-only state is introduced.
- Add physics assertions, real-tick shot tests, and rollback regressions that distinguish physical correctness from merely obtaining matching checksums.
- **BREAKING**: advance the LAN protocol/rules compatibility version so old and corrected simulations cannot start a match together. Both peers must update; packet layouts remain unchanged.
- After implementation and verification, publish the next unused sequential `PocketBump<version>` Windows build folder under `D:\uniry Assets\射球大战\`, preserving existing builds and retaining diagnostic results.

This is phase 1 of the improvement roadmap. LAN lifecycle/reconnect work, tutorials, audio, presentation, new modes, maps, and balance redesign are not part of this change.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `local-two-player`: Define correct deterministic circle contact resolution, including approaching, separating, and degenerate contacts, for the simulation shared by local and LAN play.
- `marble-basket`: Specify the preserved 8–22 shot-speed curve and observable power differences after an ordinary simulation tick, without changing charge controls or recoil.
- `rollback-netplay`: Require a compatibility-version change when these simulation rules change and reject pre-fix peers before gameplay begins.

## Impact

- Runtime areas: `MatchSim.BounceCircles`, `MatchSim.StepMarbles`, `ArenaLayout`, and `NetplayProtocol` in `LanUdpPeer.cs`.
- Verification: EditMode simulation/protocol/rollback tests, PlayMode regression tests, and existing two-player Windows LAN diagnostics.
- No new dependency, authoritative Unity Physics2D integration, or additional rollback state is planned.
- Preserve basket contact stealing, dangerous-impact cooldown/energy transfer, current scoring behavior, and network-failure freeze/reason display. Existing unrelated changes and unsynced specifications are not rewritten by this proposal.
- This proposal creates planning artifacts only; code changes and packaging require a subsequent apply request.
