## 1. Version and Harden the UDP Protocol

- [x] 1.1 Increment the LAN protocol version and add named constants for input-history length, handshake retry interval, readiness phases, and accepted future-tick allowance.
- [x] 1.2 Replace offset-based handshake parsing with typed, length-checked decode methods for Join, JoinAck, phased Ready, Goodbye, and Input messages.
- [x] 1.3 Extend the Input wire message to encode the newest tick, up to eight newest-to-oldest input samples, sender seat, contiguous AckTick, and checksum fields within one bounded datagram.
- [x] 1.4 Add EditMode codec tests covering round trips, exact encoded-size bounds, truncated payloads, invalid history counts, wrong message types, and protocol-version mismatch.

## 2. Make Rollback Input Receipt Loss- and Reorder-Tolerant

- [x] 2.1 Add a bounded batch API that inserts decoded remote input history idempotently and rejects stale or excessively future-dated samples before touching ring-buffer slots.
- [x] 2.2 Track the newest accepted remote tick separately from the highest contiguous received tick, and expose only the contiguous tick as AckTick.
- [x] 2.3 Update remote prediction so only a newer accepted tick changes the prediction source and predicted inputs always clear `ShootPressed` while retaining movement and boost state.
- [x] 2.4 Preserve rollback correction and InputHole failure semantics when redundant history fills a gap or when a gap remains unresolved past the buffer.
- [x] 2.5 Add deterministic EditMode tests for one- and multi-packet loss recovery, duplicate history, reordered batches, contiguous Ack gaps, older-packet prediction regression, cleared shoot edges, and an unrecoverable hole.

## 3. Implement the Retryable Handshake

- [x] 3.1 Introduce explicit host and joiner handshake states for Join, JoinAck, joiner-ready, host-ready, and joiner-confirm transitions.
- [x] 3.2 Send the control message required by the current state on the configured retry interval until the next valid state is observed or the existing handshake deadline expires.
- [x] 3.3 Make duplicate and reordered handshake messages idempotent so the host reuses the same match id/settings/seat assignment and both peers can answer late readiness messages during the gameplay grace period.
- [x] 3.4 Ensure neither peer advances tick 0 before it has evidence that both peers declared readiness, while preserving current mismatch and timeout menu errors.
- [x] 3.5 Add deterministic handshake-state tests that drop the first Join, JoinAck, each readiness phase, and deliver duplicates or reordered control messages.

## 4. Integrate Validated Gameplay Traffic

- [x] 4.1 Update `MatchDirector` to build each outgoing input history from stored local samples and insert every decoded remote sample before advancing rollback state.
- [x] 4.2 Pin gameplay traffic to the negotiated endpoint and require the expected match id and remote seat before accepting inputs, checksums, acknowledgements, or liveness updates.
- [x] 4.3 Ignore malformed, stale, wrong-peer, wrong-seat, wrong-match, and excessively future datagrams without throwing or refreshing `_lastPacketTime`.
- [x] 4.4 Continue responding to valid late readiness retries during the post-handshake grace period without creating another rollback session or restarting the match.
- [x] 4.5 Surface distinct diagnostics for handshake timeout, silent-peer timeout, desync, and unrecoverable input hole while retaining the existing menu return flow.

## 5. Verify End-to-End Reliability

- [x] 5.1 Build a two-peer loopback test fixture with two ephemeral UDP sockets and a controllable datagram queue that can drop, duplicate, delay, and reorder selected messages.
- [x] 5.2 Add loopback tests proving both peers complete the handshake after isolated control loss and retain identical negotiated match id, settings, and seats.
- [x] 5.3 Add loopback tests proving redundant input history survives isolated and short burst loss, duplication, and reordering without InputHole or divergent final checksums.
- [x] 5.4 Add a negative loopback test proving loss longer than the recoverable window terminates with InputHole rather than continuing on invented input.
- [x] 5.5 Run the complete EditMode and PlayMode suites, confirm the Unity Console has no errors or warnings, and perform a two-build LAN smoke test with protocol-version mismatch and transient packet loss checks.
