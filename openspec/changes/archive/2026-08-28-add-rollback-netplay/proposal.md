## Why

Pocket Bump is a same-machine physics duel. Two people who are not at one keyboard cannot play, and host-authoritative interpolation would make scoop, charge, and dunk late on the client — the moves that *are* the game. Prediction plus rollback is the model that keeps both players' own inputs immediate, but Unity Physics2D cannot be rolled back or run bit-identically on two machines, so the play loop has to become a deterministic tick simulation first.

## What Changes

- Replace live `Rigidbody2D` gameplay with a **deterministic 60 Hz tick sim** (circles, walls, baskets, pockets, charge, scoop, dunk). Presentation follows the sim; Unity Physics2D is no longer the rules engine.
- **BREAKING** for feel: bounce, CCD, and trigger timing will not be identical to Box2D. Constants stay the same where they already live in `ArenaLayout`; remaining contact behavior is retuned until local play still reads as Pocket Bump.
- Local two-player keeps working on **the same sim** (WASD vs arrows, one machine, no session). No second physics path.
- Add LAN 1v1: host and join by address, each machine uses that seat's local bindings (defaults: WASD / J / K), seats and match settings lock before the first tick.
- Netplay uses **input delay + prediction + rollback**: each peer predicts the remote player's input, resimulates from the last confirmed tick when the real bits arrive, then presents the corrected state.
- Shoot, boost, and movement are sampled **per tick**, not `Update` `GetKeyDown` (today's charge presses are frame-rate dependent and cannot rollback).
- Disconnect, refuse mismatched settings, and return to menu. No host migration in this change.

## Capabilities

### New Capabilities

- `rollback-netplay`: LAN session, seat assignment, delayed input exchange, remote-input prediction, rollback resimulation, disconnect.

### Modified Capabilities

- `local-two-player`: Local matches still need no network; movement and collisions come from the shared tick sim; online each peer drives only their seat with local bindings.
- `menu-flow`: Main menu offers Host LAN and Join LAN in addition to local Start.
- `match-flow`: Scoring, kickoff, and simultaneous-event order run on sim ticks (same order as today).
- `marble-basket`: Collect, steal, catch, and charge fire from sim overlaps and tick input, not Unity triggers / `Update` key-downs.
- `arena-pockets`: Pocket entry is tested in the sim, not Physics2D triggers.
- `visual-presentation`: Bodies interpolate from confirmed/predicted ticks; one-shot FX play only for events that survive rollback.

## Impact

- Core loop: `MatchDirector`, `PlayerMotor`, `MarbleMotor`, `PlayerBasket`, `Pocket` stop owning rules; a new tick state + stepper owns them. Scene objects become views (kinematic or transform-only).
- `PlayerMovementMath` / `PlayerChargeMath` stay the player-control kernels and must run inside the tick with a fixed `dt`.
- Menu: `MainMenuController` / `GameSession` gain host/join and a session object; local Start path unchanged aside from loading the sim.
- Tests: EditMode must step the sim without Play Mode physics. PlayMode keeps flow tests but against views + sim, not `Rigidbody2D` velocities as source of truth.
- Transport: small UDP (Unity Transport or equivalent). No Steam, Relay, Netcode for GameObjects, or dedicated server in this change.
- Existing EditMode/PlayMode suites that assume Unity Physics2D contacts need rewriting against the sim.
