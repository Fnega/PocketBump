## Context

Pocket Bump advances a custom 60 Hz authoritative simulation on both peers and exchanges one compact UDP input packet per tick. `RollbackSession` can correct late input for 12 ticks, but every input sample and handshake control message is currently sent once. `AckTick` records only the greatest received tick, not the greatest contiguous tick, and the most recently delivered datagram replaces the prediction baseline even when that datagram is older than input already received. These choices make ordinary datagram loss or reordering visible as incorrect prediction and eventually `InputHole`.

The change crosses the wire codec, handshake state machine, match driver, rollback input store, and automated tests. It must remain compatible with Unity 2022.3, use no external networking package, avoid blocking the Unity main thread, and keep simulation state synchronization input-based rather than sending transforms.

## Goals / Non-Goals

**Goals:**

- Complete a LAN handshake despite isolated loss, duplication, or reordering of control datagrams.
- Recover isolated missing input from redundant history carried by later input datagrams.
- Define acknowledgement as the highest contiguous remote input tick so a later isolated packet cannot hide an earlier hole.
- Make duplicate and out-of-order processing idempotent and prevent older input from replacing the prediction baseline.
- Prevent a predicted held input from repeating the one-shot shoot edge.
- Reject malformed, wrong-peer, wrong-seat, wrong-match, stale, and unreasonably future-dated datagrams without throwing from the frame loop.
- Preserve explicit failure when an input remains unrecoverable past the rollback window.
- Verify the behavior under deterministic loss, duplication, delay, and reordering.

**Non-Goals:**

- Internet matchmaking, NAT traversal, relays, encryption, authentication, or host migration.
- Reliable delivery of every UDP datagram; reliability is applied only to handshake state and the bounded input history needed by rollback.
- Increasing the rollback window or changing match physics, scoring, controls, or presentation.
- Solving cross-platform floating-point determinism or expanding the simulation checksum; those require a separate change.

## Decisions

### Version the wire format and use explicit bounded decoding

Increment `NetplayProtocol.Version` and define protocol codec methods that validate exact minimum lengths before reading fields. Input datagrams identify match, sender seat, newest input tick, history count, a fixed newest-to-oldest sequence of input bits, highest contiguous acknowledgement tick, and checksum data. Messages from an endpoint other than the negotiated peer, with the wrong match or seat, or outside the accepted tick window are ignored.

This is preferred over trying to decode both formats because the existing handshake already rejects protocol mismatches, and dual-format support would add ambiguity without a deployed compatibility requirement.

### Retry an idempotent handshake state machine

Handshake progress is represented explicitly rather than inferred from whether a message happened to arrive once. Each side retains the last control response and sends the message required by its current state every configured retry interval until it observes the next valid state or the existing overall handshake deadline expires. Duplicate `Join`, `JoinAck`, and `Ready` messages return the same negotiated match id, settings, seat, and readiness response and do not restart or duplicate the session.

Readiness messages include a small phase value so the joiner's ready declaration, the host's ready response, and the joiner's confirmation are distinguishable and safely repeatable. Both sides may continue answering late duplicate readiness messages for a short grace period after entering gameplay. No simulation tick is advanced before the local state machine has evidence that both peers declared readiness.

This is preferred over a generic reliable-channel layer because only a few control transitions require retransmission and the project does not need ordered delivery for all traffic.

### Carry eight input samples per gameplay datagram

Every gameplay datagram carries the newest input plus up to seven immediately preceding samples. Eight ticks cover 133 ms at 60 Hz and remain small enough for a single UDP datagram while making isolated and short burst loss recoverable. The receiver inserts every valid sample independently, so duplicates are harmless and reordered datagrams can still fill holes.

This is preferred over per-input retransmission because redundant history has no retransmission round trip, requires no send queue, and naturally matches a continuous 60 Hz stream. The history length is a named protocol constant so profiling or network testing can tune it without changing rollback semantics.

### Track contiguous receipt separately from newest receipt

`RollbackSession` records both the newest accepted remote tick and the highest tick for which all required remote inputs up to that point are present. `AckTick` reports only the contiguous value. Receiving tick N+2 before N+1 stores N+2 but does not advance the acknowledgement across the gap. When N+1 arrives through a later history block, acknowledgement advances across all newly contiguous samples.

Only an accepted input whose tick is newer than the current prediction-source tick updates the held prediction. The predicted value retains movement and boost but always clears `ShootPressed`, because shoot is an edge rather than a held state.

### Separate packet processing from socket timing for tests

Encoding/decoding and handshake transitions are deterministic methods operating on explicit messages and timestamps. `LanUdpPeer` remains the non-blocking socket adapter. Tests can therefore exercise most failure cases without wall-clock sleeps, while a two-peer loopback integration fixture uses two ephemeral UDP sockets and a controllable relay/queue to drop, duplicate, delay, and reorder selected datagrams.

This is preferred over tests that directly drive private `MatchDirector` methods because it validates the same serialized bytes and socket boundaries used by the game while keeping tests repeatable.

## Risks / Trade-offs

- [Input datagrams become larger] → Keep history fixed at eight one-byte inputs plus compact metadata and assert encoded size in protocol tests.
- [Both peers can briefly enter gameplay at different wall-clock times] → Continue accepting readiness duplicates and buffer valid future input within a strict bound; rollback input delay absorbs normal LAN skew.
- [A long burst loss still exhausts the rollback window] → Preserve `InputHole` as an explicit terminal failure and distinguish it from the five-second silent-peer timeout in diagnostics.
- [Retry timers can flood the network if configured too aggressively] → Use a named interval in the low hundreds of milliseconds and send only the message required by the current handshake state.
- [History insertion can overwrite ring-buffer data if future ticks are unbounded] → Reject samples newer than the configured future allowance before writing the ring buffer.
- [UDP source spoofing remains possible on a LAN] → Pin gameplay traffic to the negotiated endpoint and validate match id and seat; cryptographic authentication remains a non-goal.

## Migration Plan

1. Add versioned message models and codecs with exhaustive malformed-length tests.
2. Replace the one-shot handshake branches with the retryable state machine while retaining the existing 15-second deadline and menu error flow.
3. Add input history encoding, bounded batch insertion, contiguous acknowledgement tracking, and edge-safe prediction.
4. Update the match driver to send history and consume decoded batches, then add fault-injected two-peer tests.
5. Run all EditMode and PlayMode tests and perform a two-build LAN smoke test.

Because the wire version changes, old and new builds will fail version negotiation rather than interoperate incorrectly. Rollback consists of reverting the protocol version and the networking/rollback changes together; no saved data or scene migration is required.

## Open Questions

None. The initial implementation uses an eight-sample history and a named retry interval; both are internal tuning constants that can be adjusted from test and profiler evidence without changing the specified behavior.
