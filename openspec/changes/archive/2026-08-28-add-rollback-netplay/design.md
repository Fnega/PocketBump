## Context

Pocket Bump today is one `MatchDirector` driving two `PlayerMotor`s and up to eight `MarbleMotor`s on `Rigidbody2D`. Input is `Input.GetKey` in `FixedUpdate`, except shoot edges which are `GetKeyDown` in `Update`. Scoring, scoop, and dunk are Physics2D triggers flushed after each physics step. There is no networking package. First-version design explicitly deferred netplay so feel could be tuned without lag.

Prediction + rollback needs every gameplay fact to be a function of `(state, both players' inputs, tick)`. Unity Physics2D is neither bit-identical across processes nor rewindable without restoring the whole engine. `PlayerMovementMath` and `PlayerChargeMath` are already pure; contacts, baskets, and pockets are not.

Local two-player must keep working. The only way not to ship two games is one sim with two drivers: local keys, or networked delayed inputs.

```
Tick t
  inputs[P0], inputs[P1]  ──►  MatchSim.Step(state, inputs, dt=1/60)
                              │
                              ├─ players, marbles, gate, carry, charge
                              ├─ contacts, scoop/steal, pockets
                              └─ match machine (kickoff / live / scored / over)
                                         │
                         View interpolates transforms + HUD
                         FX consume events that survived rollback
```

## Goals / Non-Goals

**Goals:**

- One deterministic 60 Hz match sim that implements current rules (table size, masses as numbers in integration, scoop, two-stage charge, dunk/marble pockets, simultaneous-score order, win-by-two).
- Local 1v1 on that sim, same bindings as today.
- LAN 1v1: host/join, each peer predicts the remote seat, rolls back when real remote input arrives, presents corrected state.
- Own-seat input feels immediate (plus a small fixed input delay shared by both).
- Desync is detected (checksum) and the match stops rather than silently diverging.

**Non-Goals:**

- Rolling back or networking Unity Physics2D / Netcode for GameObjects / Mirror / Photon.
- Steam, Unity Relay, NAT punchthrough, dedicated servers, ranked, spectators, >2 players.
- Host migration, mid-match rejoin, anti-cheat.
- Bit-exact bounce parity with Box2D. Feel must still read as Pocket Bump; constants in `ArenaLayout` stay the starting point.
- Exact cross-architecture determinism (ARM vs x64, IL2CPP vs Mono). LAN assumes the same player build.
- Rewriting the 3D table presentation language (meshes, camera, identity colors stay).

## Decisions

### 1. Custom tick sim, not Physics2D rollback

- **Choice**: Gameplay state is a copyable struct (or sequential buffers) stepped by a pure `MatchSim.Step`. Scene colliders become views. `Rigidbody2D` is not the source of truth.
- **Why**: Rollback copies state, then resimulates a few ticks. PhysX/Box2D cannot do that cheaply or identically on two PCs.
- **Rejected**: Host-authoritative interpolation (client scoop/dunk always late). Client prediction of Physics2D (reconcile contacts is the hard problem). Dual path “local Box2D / net custom” (two feels).

### 2. Same sim for local and netplay

- **Choice**: Local Start constructs the sim and feeds both seats from this machine’s bindings every tick. Netplay feeds one seat from local sample and the other from the rollback session.
- **Why**: If local still uses Physics2D, every feel tweak has to be done twice and netplay will never match the “real” game.
- **Rejected**: Feature-flag the old motors for “offline quality.”

### 3. Circle / segment / OBB contacts, sequential impulses, fixed order

- **Choice**: Players and marbles are circles. Walls, gate, pocket blockers, and basket rims are segments or oriented boxes. Integrate with the existing accel/brake/boost formulas, then resolve overlaps in a **stable pair order** (sorted entity ids), using `ArenaLayout.Bounciness`, masses, marble impact bands, and recoil as impulse numbers (not `ForceMode2D`).
- **Why**: The table is circles vs boxes; we do not need a general engine. Determinism comes from order + no `Physics2D.Simulate`.
- **Rejected**: Unity.Physics ECS, third-party Box2D ports, raycast CCD as a first version. Use swept circle vs static segments only if tunneling shows up in tests (high marble shot speed 22 vs radius 0.3).

Basket well is an OBB (inner opening from `BasketWidth` minus rims). Collect/steal/catch: well overlaps marble circle, same rules as today’s triggers. Carried marble is excluded from free-body collision and welded to well center.

Pockets: analog of today’s layer filters — marble circle vs north/south well (not while carried); player circle vs east/west dunk well. Same-tick score order unchanged: P1 dunk, P2 dunk, north marble, south marble.

### 4. Tick input, including shoot edges

- **Choice**: Each tick samples a compact `PlayerInput` (move axes or 4 bits, boost held, shoot pressed this tick). `GetKeyDown` is latched between ticks and consumed once. Charge stages advance only in `Step`.
- **Why**: Today’s shoot in `Update` depends on rendering framerate; two machines and rollback cannot share that.
- **Rejected**: Sending analog stick floats (keyboard game; normalize on sample). Sending facing (derived in sim).

Default delay: **2 ticks** (~33 ms at 60 Hz) applied to **both** seats so neither side has an input-timing advantage. Tunable constant, not a menu yet.

### 5. GGPO-style session, written here, two players only

- **Choice**: Keep a ring of confirmed inputs and predicted remote inputs. Advance local tick with prediction. When a remote packet fills a past tick, if it differs from the prediction, restore snapshot at that tick and resimulate forward. Snapshot every tick (state is tiny: ≤2 players + 8 marbles + match machine).
- **Why**: Off-the-shelf Fusion/Photon is snapshot interpolation or a black box. A 2-seat 60 Hz loop is small enough to own. Prediction of remote: **hold last confirmed input** (neutral if none yet).
- **Rejected**: Lockstep with no prediction (always wait). Rollback.net / native GGPO as a first dependency.

Presentation: store previous and current sim poses; render interpolates. On rollback, snap or short-blend; do not keep playing one-shot FX (pulse, collect) from ticks that were unconfirmed. HUD score/state come from the latest presented tick.

### 6. LAN datagrams, not Netcode

- **Choice**: Thin UDP (`UdpClient` or Unity Transport as a datagram only). Payload: protocol id, match id, tick, seat, input bits, last-received remote tick (ack). Periodic state checksum (hash of positions, carry, scores, match state).
- **Why**: LAN 1v1 does not need a relay. NGO would fight the sim model.
- **Rejected**: Steam sockets (later). TCP (head-of-line). RPCs per scoop.

Host is **session authority for lobby only** (who is P1/north, settings). After the first gameplay tick, **neither peer is physics host** — both step the same inputs. Host does not simulate “for” the client.

Handshake: host listens; joiner sends settings hash + protocol version; host replies seat map + `MatchSettings`; both start tick 0 together after a short barrier (ready packets). Mismatch → error, back to menu.

Disconnect: no packets for N ms (e.g. 5 s) or peer goodbye → MatchOver-style abort, return to menu. No migration.

### 7. Checksum then halt

- **Choice**: Every tick (or every 4th) both sides hash sim state. If a checksum for an equal tick disagrees, freeze, log both hashes, show a desync message, end the session.
- **Why**: Silent desync is worse than a hard stop during development and LAN playtests.
- **Rejected**: Host snapshot correction (that is a different net model and hides sim bugs).

### 8. Tests drive the sim

- **Choice**: EditMode tests call `MatchSim.Step` with scripted inputs. Determinism test: two isolated states, same input tape, equal checksums. Rollback test: predict wrong remote input, inject the real bits, state equals the all-known-inputs baseline. PlayMode tests the view + menu host/join against loopback, not `Rigidbody2D.velocity` as truth.
- **Why**: The old PlayMode marble-roll tests are testing Box2D, which we are leaving.

## Risks / Trade-offs

- [Feel regresses vs Box2D] → Land the sim on **local** play first; do not open LAN until a local match is recognizably Pocket Bump. Keep `ArenaLayout` numbers; add swept tests if shots tunnel; retune only with a recorded “before” checklist (scoop, steal, dunk, charge recoil).
- [Tunneling on hard shots] → Swept circle vs walls/rims if a marble at `ShotMaxSpeed` skips a well or wall in one tick.
- [Rollback visual pops] → Interpolate render; suppress unconfirmed FX; 2-tick delay cuts misprediction rate.
- [Desync from float / `Mathf`] → Same player build; no `Time.time`, no `Random`, no dictionary iteration; sort contact pairs; checksum early.
- [Shoot edge missed or double-fired] → Latch `GetKeyDown` per tick; include shoot bit in the input tape tests.
- [UDP packet loss] → Inputs are idempotent per tick; request missing ticks via ack window; if the hole is older than the rollback buffer (e.g. 8–12 ticks), desync/abort rather than guess forever.
- [Scope explosion] → No Relay, no Steam, no old Physics2D fallback. Sim + local parity is the gate for netplay tasks.

## Migration Plan

1. Extract `MatchSim` + `MatchSimState` and step a local match with views glued on. Disable dynamic `Rigidbody2D` gameplay.
2. Move EditMode/PlayMode assertions onto the sim. Fix feel gaps.
3. Add rollback session in loopback (two sims, delayed/lossy fake packets) until checksums stay clean.
4. UDP host/join + menu entries. Local Start remains the default path.
5. If the change is reverted: restore Physics2D motors from version control; no saved-game format.

No player save data. Bindings persist as today.

## Open Questions

- Default input delay 2 vs 3 ticks after first LAN playtest (start at 2).
- Whether Unity Transport is worth a package vs `UdpClient` (default `UdpClient` unless editor firewall pain appears).
- Whether basket rims need full OBB collision or circles-plus-well is enough for steal fairness (well overlap is the rule; rims are anti-tunneling).
