## Why

LAN failures currently return players to the menu with too little evidence to diagnose the cause, and reproducing a two-player session requires manual process setup.  The packaged game should also start in a convenient windowed mode while letting players choose fullscreen when desired.

## What Changes

- Add a development-build LAN pair runner that launches/coordinates host and join instances locally and writes machine-readable diagnostics for both peers.
- Record the disconnect reason and network/session counters in a persistent diagnostic report so a later investigation can use evidence from the user's test run.
- Add a menu settings surface for windowed and fullscreen display modes, persist the selected mode, and apply it before entering a match.
- Make packaged standalone players use a small windowed default unless a saved player preference overrides it.
- Pause an active LAN match on every terminal network problem and show the exact reason in a top-right in-game panel instead of immediately returning to the menu.
- Prevent false `DESYNC` failures by refreshing rollback history during replay and comparing checksums only after the referenced Tick has confirmed remote input.

## Capabilities

### New Capabilities

- `lan-diagnostic-runner`: Repeatable local two-player LAN smoke testing and persistent per-peer failure diagnostics.
- `display-mode-settings`: Persistent player-selectable windowed and fullscreen display modes.

### Modified Capabilities

- `rollback-netplay`: LAN session failures expose actionable diagnostic evidence and pause on an in-game problem panel rather than immediately returning to the menu.
- `menu-flow`: The main menu provides access to display settings.

## Impact

- `LanSmokeRunner`, LAN session/director code, development build command-line behavior, and a checked-in PowerShell helper.
- `GameSession` and `MainMenuController`, plus the authored MainMenu hierarchy.
- New EditMode tests for settings persistence and diagnostic-report formatting.
