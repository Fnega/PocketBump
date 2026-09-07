## 1. Tick state and input

- [x] 1.1 Add copyable `MatchSimState` (players, marbles, gate, carry, charge, boost, scores, match phase, tick) and `PlayerInput` (move, boost held, shoot pressed this tick)
- [x] 1.2 Latch Unity key-downs between ticks into `PlayerInput` so shoot edges are tick-sampled, not `Update`-sampled
- [x] 1.3 Implement `MatchSim.Step` with fixed `dt = 1/60` calling existing `PlayerMovementMath` / `PlayerChargeMath` for player control; no `Time.deltaTime` or Physics2D inside Step
- [x] 1.4 Add a state checksum used by tests and later by netplay

## 2. Simulated table

- [x] 2.1 Resolve circle-circle and circle-vs-static (walls, gate, pocket blockers, basket rims) in a sorted pair order using `ArenaLayout` masses, bounciness, drag, and marble impact bands
- [x] 2.2 Implement basket well OBB overlap: collect, steal, catch; weld carried marble to well; body overlap does not collect
- [x] 2.3 Implement pocket wells: free marble vs north/south, player vs east/west; same-tick score order P1 dunk → P2 dunk → north → south
- [x] 2.4 Port kickoff (3s closed gate, then open, then spawn marbles at rest), freeze, reset, and win-by-two into `MatchSim.Step`
- [x] 2.5 If a marble at `ShotMaxSpeed` tunnels a wall or well in one tick, add swept circle vs those statics

## 3. Local match on the sim

- [x] 3.1 Drive a local match from `MatchSim` only: both seats sampled on this machine (WASD vs arrows)
- [x] 3.2 Turn player/marble/gate/basket scene objects into views that follow sim poses; stop using dynamic `Rigidbody2D` as gameplay truth
- [x] 3.3 Keep HUD, camera, identity colors, and charge needles; needles read charge state from the sim
- [x] 3.4 Play collect/fire pulses from sim events (local ticks are confirmed immediately)

## 4. Local tests and feel gate

- [x] 4.1 EditMode: same input tape on two isolated sims → equal checksums; scoop vs body; steal; carried marble does not score; simultaneous dunk beats marble; charge stages and recoil
- [x] 4.2 EditMode: marble coasts to rest; dangerous knockback + cooldown; kickoff 180 ticks then spawn
- [x] 4.3 Rewrite PlayMode flow tests to assert sim/view, not `Rigidbody2D.velocity` as source of truth
- [x] 4.4 Play a local match and retune contact constants only if scoop/dunk/charge no longer read as Pocket Bump; do not start LAN until this passes

## 5. Rollback session (loopback)

- [x] 5.1 Snapshot every tick; restore + resimulate when a past remote input disagrees with prediction (last confirmed remote, else neutral)
- [x] 5.2 Apply the same input delay (default 2 ticks) to both seats
- [x] 5.3 Loopback test: two sessions, delayed/lossy fake packets, final checksum equals an all-known-inputs baseline
- [x] 5.4 Abort when a required input is older than the rollback buffer

## 6. LAN transport and menu

- [x] 6.1 UDP datagram: protocol id, match id, tick, seat, input bits, ack, checksum; host listen / join address
- [x] 6.2 Handshake: version + settings hash, seat map, barrier before tick 0; mismatch returns to menu without ticks
- [x] 6.3 Main menu: Host LAN (rules then listen) and Join LAN (address); keep local Start unchanged aside from using the sim
- [x] 6.4 Disconnect timeout or goodbye returns to main menu; checksum mismatch shows desync and ends the match

## 7. Netplay presentation and tests

- [x] 7.1 Interpolate views between ticks; on rollback, do not leave collect/fire pulses from unconfirmed events
- [x] 7.2 PlayMode or EditMode: loopback host/join handshake; joiner WASD drives seat 1; settings mismatch aborts
- [x] 7.3 Run the full PocketBump EditMode and PlayMode suites and fix regressions
