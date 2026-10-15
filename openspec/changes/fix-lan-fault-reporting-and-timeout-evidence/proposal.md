## Why

The user's physical campus-network test of Release 1.0 failed: the joiner timed out while the host displayed PEER LEFT. Inspection confirmed that receive exceptions are silently discarded, automatic fault shutdown is indistinguishable from voluntary departure on the wire, and the retained snapshots cannot explain the final seconds of communication loss.

## What Changes

- Record receive polling/errors separately from sends, preserving usable evidence without throwing into gameplay.
- Distinguish voluntary departure from peer-reported failure; carry a bounded validated reason and preserve the first local/remote terminal cause.
- **BREAKING**: advance the Dash wire protocol for the new fault notification contract; reject old/new peers clearly rather than silently mixing semantics.
- Preserve a bounded pre-failure timeline of network counters, input progress, receive/accept ages and main-loop timing in production reports; freeze first-failure evidence even when the player stays on the fault screen.
- Keep the five-second timeout, deterministic simulation, rollback limits and local/LAN gameplay unchanged. Do not claim the underlying campus-network interruption has been repaired without further evidence.
- Validate receive failure/recovery, one-way traffic interruption, heartbeat-only waits, genuine departure and peer-fault propagation. If packaging is approved, deliver the next version into a new folder and retain 1.0 and all user reports.

## Capabilities

### New Capabilities

- `lan-fault-evidence`: Receive-path diagnostics, bounded pre-fault timeline and explicit peer-failure notification for the Dash runtime.

### Modified Capabilities

None. This extends the currently implemented but unarchived release-reporting capability; it does not replace the main legacy rollback requirements or mark the failed physical-network release gate as passed.

## Impact

Dash protocol/session/reporting/HUD, shared UDP receive diagnostics, relevant automated tests, and versioned Release tooling/docs if approved. No new assets, art/physics changes, firewall/network-policy changes, automatic reconnect or timeout relaxation. Existing reports and previously shipped binaries remain untouched.
