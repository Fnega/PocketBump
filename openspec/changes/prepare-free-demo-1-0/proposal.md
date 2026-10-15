## Why

The approved Dash game is ready for a Windows free-demo release candidate, but the current Development packages lack production fault reports, clear first-play instructions, an in-match menu, and basic audio feedback. The user approved completing this release scope and delivering version 1.0 in a new PocketBump1.0 folder.

## What Changes

- Add durable, release-safe LAN reports and an accessible report-directory action, without exposing development fault-injection controls.
- Add Chinese quick-start instructions, attack-direction cues, a local pause/LAN personal Esc menu, and a laptop-friendly P2 Dash default while preserving saved bindings.
- Add original synthesized Dash, impact, wall-break, pickup and goal sounds with persistent volume control; improve the scoring ball's contrast.
- Add a Windows Release build and package manifest, identify the game as 射球大战 / PocketBump 1.0 by Fnega, use PocketBump.exe, and include an authored icon, instructions and existing third-party notices.
- Validate source/tests and the actual Release candidate; distinguish local two-process evidence from the user's pending physical two-computer acceptance.
- Deliver the complete package at D:\uniry Assets\射球大战\PocketBump1.0 and a matching zip, without overwriting older versions or publishing externally.

## Capabilities

### New Capabilities

- `free-demo-release`: Versioned non-Development Windows delivery, release identity, packaging and evidence.
- `demo-player-experience`: Chinese onboarding, accessible controls, safe in-match menu, readable ball and event-safe audio.
- `release-lan-reports`: Production-safe local diagnostic reports and report-folder access.

### Modified Capabilities

None. Existing simulation, protocol, rollback rules, map, score rules, approved art and LAN failure semantics remain authoritative.

## Impact

Dash orchestration/presentation/HUD, menu and bindings; new report/audio/UI components; Editor incremental scene integration and build pipeline; release packaging and acceptance tools; EditMode/PlayMode tests. Existing dirty worktree and authored scenes require incremental edits and exact pre-migration backups. No new downloaded assets, online matchmaking, accounts, external publishing or music are included. Physical two-PC acceptance remains an explicit external release gate and must not be reported as automatically passed.
