## Why

Players currently "shoot" only by ramming marbles with their circular bodies, so possession, aiming, and scoring are the same collision. The table needs a front-facing basket that can hold one marble, a two-stage charge shot as the only way to launch from the basket, and steal-by-scoop so the opponent takes the held marble with their basket rather than by body-checking.

## What Changes

- Add a square basket in front of each player. The basket faces the player's current movement input (last non-zero input when idle).
- Collect a free marble only when the **basket** overlaps it. Body contact still uses ordinary physics (bounce, existing knockback bands). One marble in a basket at a time.
- A marble sitting in a basket SHALL NOT score if it overlaps a north or south pocket. Free marbles that roll or bounce into those pockets still score as today.
- Replace "launch by colliding" as the held-marble shot: the holder MUST charge-shoot. Charge has two sequential readings on the shoot key: **direction** (player may move) then **power** (player must be still). Firing applies recoil opposite the locked shot direction.
- Steal: when the opponent's basket overlaps the marble currently held in a basket, ownership transfers to the opponent. Body contact with the holder does not eject the marble.
- Incoming flying marbles: basket (empty) catches; body bounces.
- Default keys: P1 shoot `J`, boost `K`; P2 shoot `Alpha1`, boost `Alpha2`. WASD / arrows stay as movement. Rule-setup shows these bindings and allows rebinding the four shoot/boost keys. **BREAKING** for anyone used to Left/Right Shift boost.

## Capabilities

### New Capabilities

- `marble-basket`: Front basket, carry one marble, basket-only collect, scoop steal, empty-basket catch vs body bounce, two-stage charge shot with recoil, carried marble ignored by marble pockets.

### Modified Capabilities

- `arena-pockets`: North/south pocket scoring ignores a marble that is currently carried in a basket.
- `local-two-player`: Players have a facing used by the basket; boost keys change from Shift to K / 2; each player has a shoot key; input is read from the current binding table.
- `menu-flow`: Rule setup displays movement plus shoot/boost bindings and lets the user rebind the four skill keys.
- `visual-presentation`: Basket is readable per player identity; charge direction/power readings have on-table feedback.

## Impact

- New basket child (visual + trigger) on each player; carry state parented/kinematic marble; charge state machine; recoil impulse on the player body after a successful shot.
- `PlayerMotor` input (boost no longer hardcoded Left/Right Shift; shoot `GetKeyDown`); facing from movement input.
- `MatchDirector` scoring must skip carried marbles; HUD control banner must show live bindings.
- `MainMenuController` / `MatchSettings` or a small bindings store: display and rebind; persist locally (not locked as match rules).
- `MarbleMotor` / layers: carried marble must not apply dangerous knockback; steal and catch use basket overlap.
- EditMode/PlayMode tests for collect, steal, no score while carried, charge phases, recoil, default keys, and rebind conflicts.
- Kickoff, dunk pockets, win-by-two, marble count, and table extents stay the same.
