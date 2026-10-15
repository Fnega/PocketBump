## Context

The current Dash HUD uses the same bottom-right panel for WaitingForRemoteInput and NetworkPaused. The former pauses simulation safely while transport and input sampling continue; the latter represents a terminal fault. The user confirmed hiding only the recoverable notice. P2 currently defaults to Alpha2, while binding persistence and labels already distinguish Keypad2 as Num2.

## Goals / Non-Goals

**Goals:** Quiet transient waits, unchanged actionable terminal feedback, Keypad2 as the new local P2 default, regression coverage and clear current-binding guidance.

**Non-Goals:** Disabling input pacing, extending timeouts, changing protocol/simulation, deleting fault/report controls, resetting saved preferences, rebaking authored scenes/art, modifying shipped 1.0/1.1 files or asserting the campus-network root cause is resolved.

## Decisions

1. Gate the network panel only on NetworkPaused. Remove its transient-wait rendering branch; retain observability properties if diagnostics still consume them. No networking/director state is changed. Keeping the panel object avoids breaking authored references, and terminal faults can still activate it.
2. Change only DefaultP2Dash to Keypad2 and update tests that actually depend on defaults. Valid saved Alpha2 or other bindings remain authoritative. Loading remains read-only; resetting to defaults uses the new value. Automatic migration cannot distinguish an old saved default from a deliberate custom binding and is therefore rejected.
3. Keep guidance driven by the active binding, with Num2 explicitly distinguished from 2. Do not hard-code the default over custom settings. Verify local keyboard sampling accepts Keypad2 and does not interpret Alpha2 as the same key; LAN keeps the P1 mapping on both computers.
4. Update actual-loopback HUD tests to assert no transient panel while preserving automatic recovery, buffered Dash edges, terminal timeout panel, lower-right layout and report access. Run relevant EditMode and PlayMode suites before handoff.

## Risks / Trade-offs

- [A short simulation pause is now silent] → User explicitly chose this; retain timing evidence and terminal fault feedback.
- [Existing stored P2 key remains 2] → Preserve user choices, test the behavior and explain rebinding rather than erasing preferences.
- [No keypad on some keyboards] → Existing pregame key rebinding remains available.
- [Historical evidence describes the previous UI] → Keep old releases/reports untouched and record new test evidence separately.

## Delivery

The user approved a new D:\uniry Assets\射球大战\PocketBump1.2 folder and ZIP, preserving 1.1, and visible ordinary-UI dual-window verification with a reminder before starting. Update current identity/build/package support to 1.2 without dropping 1.0/1.1 validation; protocol and simulation rules remain unchanged. Run regression tests, build an isolated Release candidate without rebaking scenes/art, verify that exact candidate through ordinary UI, then package and independently hash-check the exact accepted files. Existing 1.1 evidence is not proof for 1.2 and must not be relabeled.

After the first candidate's UI attempt missed the host handshake window, the user approved optimizing only the acceptance tooling and retesting the exact same binary. Snapshot historical report names before Player launch, observe new reports incrementally while retaining PID/start-time identity checks and newest-session/sequence ordering, and prepare the join address before starting the host. Preserve every existing desktop safety guard, report and failed attempt; add report-read and connection-timing evidence. Do not extend game deadlines, change firewall settings or rebuild the accepted candidate to work around test timing.
