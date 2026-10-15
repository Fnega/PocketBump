## Why

During physical LAN play the user finds the transient input-wait panel distracting and has explicitly chosen to hide only that notice while retaining real fault feedback. The user also wants the second local player's default Dash key to be numeric-keypad 2 rather than the number-row 2.

## What Changes

- Hide the network panel during recoverable input pacing waits, without changing input pacing, recovery, timeouts, diagnostics or terminal fault feedback.
- Change the default P2 Dash key to Keypad2, displayed as Num2; preserve valid stored bindings and the existing read-only legacy preference migration.
- Cover hidden wait/recovery, persistent terminal fault UI, distinct keypad input and preserved custom bindings with regression tests.
- Deliver an isolated version 1.2 folder and ZIP after source tests and authorized ordinary-UI acceptance; preserve previous shipped packages and evidence unchanged.

## Capabilities

### New Capabilities

- `quiet-dash-input-feedback`: Nonintrusive transient waits and an unambiguous keypad default for local P2 in the Dash runtime.

### Modified Capabilities

None. The Dash gameplay and current production release capabilities remain in unarchived changes; the main legacy local-two-player specification is not replaced here.

## Impact

DashMatchHud, DashControlBindings, current controls guidance and related input/HUD tests. No changes to UDP protocol, deterministic gameplay, timeout policy, actual player preferences or existing releases.
