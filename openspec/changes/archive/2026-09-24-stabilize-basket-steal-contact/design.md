## Context

The deterministic simulation welds a carried marble to its carrier's basket before `ProcessBaskets` runs. `ProcessBaskets` then tests both players every tick and transfers the marble when the non-carrier's well overlaps it. Breaking the player loop after a successful transfer prevents a same-tick reversal, but the next tick welds the marble to the new carrier while the baskets may still overlap, allowing the former carrier to steal it back. Sustained contact therefore alternates possession at simulation frequency.

The current carried-marble steal uses the same narrow well test as free-marble collection. The rendered basket uses the larger `BasketWidth` by `BasketLength` footprint, so visible basket contact is not always a steal contact. The fix must remain deterministic under rollback and cannot depend on Physics2D trigger ordering or presentation transforms.

## Goals / Non-Goals

**Goals:**

- Transfer a carried marble at most once during one continuous basket-contact episode.
- Keep possession stable while the same two baskets remain in contact, then permit a new steal after separation and re-entry.
- Make carried-marble steal contact correspond to the visible basket footprint while leaving free-marble pickup unchanged.
- Preserve seat symmetry, charge cancellation, deterministic rollback replay, and checksum-based desync detection.

**Non-Goals:**

- Changing player-body collisions, free-marble collection, basket rim physics, shooting, or scoring.
- Adding an input button, random contest resolution, or time-based possession advantage.
- Using Unity trigger callbacks as gameplay authority.

## Decisions

### Model steals as contact episodes

Each active marble will carry a deterministic steal-contact latch. A successful steal sets the latch. While the two basket steal regions remain in contact, a latched marble cannot transfer again. The latch clears only after the basket regions separate, or when the marble is released, deactivated, or reset for a new round.

This is preferred over a short cooldown because a cooldown merely postpones the same oscillation when baskets remain overlapped. It is also preferred over seat priority because fixed priority would make otherwise symmetric gameplay favor one player.

The project has exactly two player seats, so the latch does not need to encode an arbitrary player pair. Keeping it on `MarbleSim` scopes the state to the possession being protected and avoids blocking unrelated free marbles.

### Use a dedicated deterministic steal region

Free-marble collection will continue using the existing circle-versus-inner-well test. Carried-marble steals will instead use deterministic OBB contact derived from both players' authoritative position, facing, `BasketOffset`, `BasketWidth`, and `BasketLength`, matching the visible basket footprint rather than Physics2D colliders.

The contact episode will use hysteresis: normal basket dimensions start contact, while a small release padding keeps the latch active until the baskets are clearly separated. This prevents numerical boundary chatter from creating several steals during one apparent contact.

Using full basket contact is preferred over globally enlarging `WellOverlaps`, which would also make ordinary free-marble catches more permissive. It is preferred over trigger callbacks because callback timing is not rollback-replayable.

### Separate free collection from carried possession transfer

`ProcessBaskets` will branch on `MarbleSim.Carrier`:

- A free marble follows the existing per-seat inner-well collection path.
- A carried marble identifies the only opposing seat, verifies that seat is empty and eligible, evaluates the steal-contact episode, and transfers once when the episode is not latched.

On transfer, the previous carrier loses the marble and any charge state, the new carrier gains it, velocity is cleared, and exactly one collection event is emitted. The marble remains welded normally on the following tick.

### Include latch state in rollback truth

The contact latch will be initialized and cleared with marble lifecycle state, copied by snapshots, and mixed into `MatchSimState.Checksum()`. Tests will compare baseline and restored/resimulated states through sustained contact. Because the gameplay rule differs from previous builds, the LAN protocol version will be incremented so mixed versions fail during handshake rather than desynchronizing during play.

## Risks / Trade-offs

- **[Risk] Full basket OBB contact may make corner touches steal too easily.** → Cover front, side, and corner contacts in tests and tune a dedicated steal inset if playtesting shows accidental steals, without changing free collection.
- **[Risk] Release hysteresis may feel like temporary immunity after baskets visually separate.** → Keep padding small, deterministic, and expressed as a named gameplay constant.
- **[Risk] A latch omitted from initialization or checksum could survive resets or hide divergence.** → Add lifecycle, clone, checksum, and rollback regression assertions together with the state field.
- **[Risk] Legacy Physics2D well callbacks can still request `DebugCollect`.** → Ensure the authoritative collection path applies the same carried-marble latch rule, or prevent compatibility callbacks from bypassing contact-episode arbitration during a live simulated match.

## Migration Plan

No saved-game or data migration is required. Ship the simulation, tests, and protocol-version increment together. Rollback consists of reverting the contact latch, steal geometry, tests, and protocol version as one gameplay change.

## Open Questions

- The initial proposal assumes any visible basket-footprint overlap is a valid steal. Playtesting may justify a small inset so rim-tip corner touches do not transfer possession; the region must remain visibly understandable and symmetric.
