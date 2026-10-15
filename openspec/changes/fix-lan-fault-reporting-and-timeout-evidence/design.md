## Context

Paired 1.0 reports for match -1146607340 show the joiner timing out after roughly five seconds without accepted current-session traffic, followed by the host observing PEER LEFT. No DESYNC or send failure was recorded. The shared UDP receive path suppresses SocketException; the Dash session sends Goodbye for both faults and normal departure. The user confirmed campus networking without window switching or obvious stalls. These facts establish the shutdown chain, not the physical cause of lost traffic.

The user approved a new PocketBump1.1 folder/zip, retaining 1.0, and visible dual-window acceptance with a reminder before starting. All work must preserve existing dirty files and authored art.

## Goals / Non-Goals

**Goals:** Fix inaccurate peer-fault attribution, retain receive-path and pre-failure evidence, test the observed timeout chain and transient recovery without changing simulation, and deliver the verified 1.1 package.

**Non-Goals:** Claiming campus-network reliability, opening firewall rules without separate approval, background networking threads, automatic reconnection, changing the five-second policy or rollback window, retuning gameplay, and modifying prior reports/packages.

## Decisions

1. **Explicit protocol-8 fault messages.** Keep voluntary Goodbye distinct from a fixed-size fault message containing only a validated reason code and session identity. Validate endpoint, match, epoch and sender seat before acting. Unknown/malformed/stale messages cannot terminate a match or renew liveness. An inbound failure must not be echoed as a new fault. Preserve the first cause and record whether it originated locally or at the peer. Old/new protocols must fail visibly during handshake. A reason-coded message is preferable to unbounded remote strings or overloading normal departure.
2. **Observe receive errors without changing transport authority.** Count polls, empty polls, successful datagrams, failures and unexpected disposed-socket errors separately from sends. Retain last receive error/code. Expected nonblocking WouldBlock is not a terminal fault. Expose raw receive age, accepted-peer age, pump count/gap, and bounded rejection categories from the session. Use the injected monotonic protocol clock; observation does not refresh liveness or alter input acceptance. Socket failures remain nonthrowing and existing timeout behavior remains authoritative.
3. **Bounded flight recorder in production reporting.** Sample a fixed-capacity in-memory timeline at approximately 0.25-second intervals, retaining about 20 seconds. Include relative monotonic observation time, epoch/ticks, cumulative transport/accept/reject counters, wait state, receive/accept/input-progress ages and pump/main-frame timing. On the first terminal observation force a final sample and freeze the timeline so later fault-screen writes and normal leave cannot erase pre-failure evidence. Copy timeline data into reports; do not put it into simulation state/checksums. Existing report IDs and old files remain untouched. Additive versioned evidence is readable without pretending legacy fields contain it.
4. **Clear existing HUD feedback.** Voluntary departure remains PEER LEFT. A received fault is clearly peer-originated and displays a mapped, bounded cause; keep the bottom-right frozen-screen panel and report-directory action. No automatic return to menu, glyph/style rebuild or camera change is required.
5. **Separate evidence from root-cause claims.** Tests simulate receive errors, one-way silence, healthy heartbeats with stalled inputs, short recoverable interruptions and explicit leave. They must establish correct local/peer reasons and retained evidence. Real two-PC campus testing remains a user gate; counters without packet timing cannot prove a network device caused loss.
6. **Versioned delivery, unchanged accepted binaries.** Update release identity/tooling coherently for 1.1 while preserving validation for 1.0. Build isolated Release/Development candidates, run source/regression tests, exercise the actual Release with ordinary UI, and package exact accepted bits into a previously absent PocketBump1.1 folder and zip. Do not mutate old packages or silently change firewall/network settings.

## Risks / Trade-offs

- [Fault UDP packet may itself be lost] → Best-effort notification; the other peer may then independently time out. Never promise both sides always receive the same remote reason.
- [Sampling misses sub-interval detail] → Retain cumulative counters, pump gap maxima and a forced terminal sample; clearly state the timeline is not a packet capture.
- [Diagnostics introduce overhead] → Fixed capacity, bounded cadence, no per-packet JSON/disk writes; serialization stays at existing report intervals and terminal transitions.
- [Protocol 8 cannot play protocol 7] → Visible version rejection and instructions requiring both players to use the same complete new package.
- [Receive counters expose more errors than before] → Separate observations from terminal policy and test normal idle polling; no spurious failure on an empty socket.
- [Physical network interruption remains unexplained] → Keep the physical release gate pending and collect paired 1.1 evidence before claiming resolution.

## Migration Plan

Read the existing change/report/test context; implement transport/fault and reporting changes with regression tests; update 1.1 identity/build/docs; run batch tests and isolated candidate builds without an active project editor; remind the user before visible acceptance; deliver exact tested files and verified ZIP without overwriting existing targets. Keep original 1.0 and supplied reports for comparison.

## Open Questions

The campus-network interruption's underlying cause remains unresolved by the 1.0 evidence. No blocking product choice remains for this scoped repair; if implementation reveals a need to change timeout/recovery semantics or system permissions, ask before doing so.
