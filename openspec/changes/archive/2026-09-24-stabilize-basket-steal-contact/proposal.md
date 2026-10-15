## Why

When two opposing baskets remain overlapped, a successful steal can be reversed on the following simulation tick, causing possession to oscillate at 60 Hz and appear unreliable or ineffective. The visible basket can also overlap another basket without the carried marble entering the smaller collection well, so the player's visual expectation does not consistently match the authoritative steal rule.

## What Changes

- Treat a basket-steal contact as a single possession-transfer opportunity rather than evaluating a fresh transfer every tick while the same contact persists.
- Keep the new carrier's possession stable until the competing baskets separate; a later, distinct contact may transfer possession again.
- Give carried-marble steals a dedicated contact test that matches visible basket overlap more closely without broadening ordinary free-marble collection.
- Preserve deterministic seat ordering, rollback replay, event emission, and charge cancellation when a steal succeeds.
- Add multi-tick, separation/re-entry, directional symmetry, glancing-contact, and rollback determinism coverage.

## Capabilities

### New Capabilities

- None.

### Modified Capabilities

- `marble-basket`: Define stable, contact-scoped steal ownership and align the carried-marble steal region with visible basket contact.

## Impact

- `MatchSim` basket processing, carried-marble overlap geometry, and possession-transfer state.
- `MatchSimState` snapshots, cloning, initialization, and checksums for any new deterministic contact latch.
- PocketBump EditMode and PlayMode tests covering sustained basket contact and re-entry.
- LAN peers must run the same gameplay build because the deterministic simulation rule changes; no external package or service dependency is introduced.
