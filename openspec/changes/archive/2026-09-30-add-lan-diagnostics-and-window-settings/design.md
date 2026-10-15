## Context

The current development-only `LanSmokeRunner` starts one role per player and writes a single success/failure string.  It does not coordinate two player processes, retain counters, or retain the last accepted packets, so a LAN failure cannot be diagnosed after the player has returned to the menu.  The main menu has no display-mode control and standalone window defaults are controlled only by player settings.

## Goals / Non-Goals

**Goals:**

- Make a development build testable as a host/join pair on one computer through a checked-in launcher.
- Persist bounded, human-readable peer diagnostics on normal completion, timeout, and abort.
- Make diagnostic artifacts discoverable and safe to share with a later debugging session.
- Provide a persisted menu choice between a small resizable window and fullscreen.

**Non-Goals:**

- Simulating arbitrary packet loss in production clients or fixing rollback reliability in this change.
- Changing resolution, monitor selection, graphics quality, or the active network protocol.
- Starting a second process from inside a shipped player.

## Decisions

### Development launcher coordinates independently built players

A PowerShell script will receive the path of a Development standalone executable, create a timestamped results directory, start the host and join with existing command-line smoke flags, then wait for both result files.  It will be kept outside `Assets` so it is not included in a player build.  This is preferred to adding editor-only orchestration because it exercises the actual packaged executable and its UDP socket binding.

### Runtime telemetry is snapshot-based and bounded

`MatchDirector` will maintain a compact diagnostic snapshot: role, local simulation tick, last remote tick/ack, receive/reject counters, last accepted packet age, and terminal reason.  It will be supplied to the smoke runner and serialised as text/JSON-compatible key-value output with a bounded recent-event list.  Counters are preferable to packet payload logs because they identify timeout, filtering, and rollback symptoms without collecting unnecessary user data.

The snapshot will additionally record attempted/successful/failed sends, the last `SocketError`, and the local/remote UDP endpoints.  The launcher will direct each standalone player's Unity log to its own result file.  This separates "join did not send" from "send failed" and "packet never arrived" without relying on a shared Player.log.

### Every smoke role emits a report in a finally-style terminal path

The runner will write one structured result report on pass, game error, or its own deadline.  A marker file communicates readiness to the launcher before joining, avoiding a race where the joiner begins before host socket setup.  Existing command-line flags remain compatible.

### Display mode is owned by a focused preference class

`DisplayModeSettings` will encapsulate PlayerPrefs keys, defaults, validation, and the `Screen.SetResolution` calls.  It will default standalone players to a compact 1280x720 window; tests and editor mode will avoid forcing a disruptive resolution.  `GameSession.Ensure` applies the saved setting once per process, while the main menu exposes a Settings panel that immediately applies and saves selection.

### Terminal network problems pause in place

Every network abort path will record and close the failed session as before, but it will keep the Arena scene loaded, freeze game time, and open a top-right HUD panel containing the exact terminal reason plus a short explanation.  The panel provides an explicit Main Menu action that restores normal time before changing scenes.  Terminal rollback problems are not auto-resumed because an input hole or desync can no longer be repaired safely.

### Checksums are validated only after rollback convergence

Input packets may carry a checksum for a Tick whose late input has just arrived.  The receiver will queue that checksum, perform any required rollback first, and compare it only when remote input is contiguous through that Tick.  Every replayed Tick rewrites its snapshot and checksum so later validation and later rollback restore the corrected history rather than stale predicted history.  Senders advertise only checksums at or below their confirmed remote-input Tick.

## Risks / Trade-offs

- [Two processes can collide on a stale port or quit before report generation] → launch script waits for a host-ready marker, has explicit deadline, and preserves each process's log/report.
- [Logs grow or contain too much detail] → only bounded counters and a fixed-size event trail are recorded, one folder per explicit test invocation.
- [Fullscreen/window transitions differ by platform] → use Unity `FullScreenMode` APIs and scope acceptance to Windows standalone; no platform-specific native calls.
- [A report proves symptoms but not packet-level root cause] → include baseline counters and retain Unity Player.log paths; later changes can add targeted fault injection.
- [Paused time leaks into a later scene] → both the panel action and `MatchDirector.OnDestroy` restore `Time.timeScale` before leaving the Arena.
- [Checksum comparison races a pending rollback] → stage bounded checksum evidence in the rollback session and validate after replay against confirmed history.

## Migration Plan

1. Add diagnostics and tests without changing LAN gameplay behavior.
2. Add the launcher and document its output directory/arguments.
3. Add display preference with the windowed default; existing users get the default on first launch.
4. Verify with EditMode tests and a two-process development build smoke run.  Removing the preference key restores the default if rollback is needed.
