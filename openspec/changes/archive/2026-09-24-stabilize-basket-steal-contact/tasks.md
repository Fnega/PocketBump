## 1. Add Deterministic Steal-Contact State

- [x] 1.1 Add a named steal-release padding constant and a per-marble contact latch to the authoritative simulation state.
- [x] 1.2 Initialize and clear the latch whenever marbles spawn, release, deactivate, or reset between rounds.
- [x] 1.3 Include the latch in state checksums and verify cloned rollback snapshots preserve it.
- [x] 1.4 Increment the LAN protocol version so builds with different possession rules fail during handshake instead of entering a match.

## 2. Implement Contact-Scoped Basket Steals

- [x] 2.1 Add a deterministic oriented-box overlap helper derived from authoritative player position, facing, basket offset, width, and length.
- [x] 2.2 Split `ProcessBaskets` so free marbles retain inner-well collection while carried marbles use basket-to-basket steal contact.
- [x] 2.3 On a valid new contact, transfer possession once, cancel the previous carrier's charge, emit one collection event, and latch the marble.
- [x] 2.4 Keep the latch while padded basket regions remain overlapped and clear it only after separation, allowing a later re-entry to steal again.
- [x] 2.5 Ensure `DebugCollect` and legacy Physics2D well callbacks cannot bypass the authoritative contact latch during live simulation.

## 3. Verify Gameplay Semantics

- [x] 3.1 Add EditMode tests proving both seats steal immediately and symmetrically when their visible basket regions begin contact.
- [x] 3.2 Add an EditMode regression test that holds the baskets overlapped for at least 30 ticks and verifies ownership and event count remain stable.
- [x] 3.3 Add separation-and-re-entry tests proving a later counter-steal is allowed only after the original contact episode ends.
- [x] 3.4 Add geometry tests for front, side, glancing, non-overlap, and body-only contacts while confirming free-marble pickup dimensions are unchanged.
- [x] 3.5 Add reset, release, firing, and charge-cancellation tests proving stale latch state cannot block a future legitimate steal.

## 4. Verify Rollback and Presentation

- [x] 4.1 Add a rollback replay test that restores before a sustained-contact steal, resimulates identical inputs, and matches baseline ownership, events, and checksum.
- [x] 4.2 Add a PlayMode test using the rendered baskets for sustained overlap, visible transfer, separation, and re-entry without possession flicker.
- [x] 4.3 Run the complete PocketBump EditMode and PlayMode suites and confirm the Unity Console has no new errors or warnings.
