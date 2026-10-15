## 1. LAN diagnostic telemetry

- [x] 1.1 Add a bounded, serialisable LAN diagnostic snapshot and record session/packet evidence at the director boundaries.
- [x] 1.2 Extend the development smoke runner to write one structured terminal report per peer, including diagnostics on error and timeout.
- [x] 1.3 Add a checked-in Windows launcher that runs host and join development players, waits for results, and preserves a timestamped report directory.

## 2. Display settings

- [x] 2.1 Add a persisted display-mode settings service with safe 1280x720 windowed defaults for standalone players.
- [x] 2.2 Add a Settings view and Windowed/Fullscreen controls to the menu while preserving the existing home layout.

## 3. Verification

- [x] 3.1 Add EditMode tests for diagnostic report content and display preference persistence/default resolution.
- [x] 3.2 Compile in Unity, run focused EditMode tests, and verify the development LAN smoke command-line contract.

## 4. Handshake transport evidence

- [x] 4.1 Record UDP send attempts, successful sends, send failures, endpoint identity, and the final Socket error in peer reports.
- [x] 4.2 Preserve separate Host and Join player logs in each diagnostic result directory.
- [x] 4.3 Add and run focused EditMode tests for send-failure classification and extended report fields.
- [x] 4.4 Allow sufficient, configurable time for a standalone Host to load before the launcher starts Join.

## 5. Join address input stability

- [x] 5.1 Provide a valid TMP text viewport for the Join address field and prevent its text label from intercepting pointer input.
- [x] 5.2 Add and run an EditMode regression test for the Join address input hierarchy.

## 6. In-game network problem pause

- [x] 6.1 Replace immediate network-error scene changes with a frozen Arena state that retains the terminal diagnostics.
- [x] 6.2 Add a top-right HUD problem panel showing the exact reason and a Main Menu action that safely restores game time.
- [x] 6.3 Add focused regression tests, bake the authored Arena HUD, and rebuild the Windows development player.

## 7. Confirmed-state desync validation

- [x] 7.1 Refresh every replayed Tick snapshot and checksum during rollback.
- [x] 7.2 Queue received checksums and validate them only after rollback against contiguous confirmed input; send only confirmed checksums.
- [x] 7.3 Add regression coverage for late-input convergence and real mismatch detection, run LAN smoke tests, and package the next version without overwriting prior builds.

Verification (2026-09-30):
- EditMode: 64/64 passed (`Logs/Desync-EditMode-20260930.xml`), including a 1,600-Tick live two-seat loss/delay/reorder/duplicate test and the exact rollback-window boundary.
- PlayMode: 9/9 passed (`Logs/Desync-PlayMode-final-20260930.xml`); updated the existing home-menu assertion to include the previously added Settings button.
- Windows Development build: `D:/uniry Assets/射球大战/PocketBump0.5/PhysicscollGame.exe`, zero build errors/warnings; older version folders preserved.
- Packaged pair: both PASS at 3,600 Ticks (`Tools/LanDiagnosticResults/20260930-124520`) and both PASS at 900 Ticks with first-packet loss (`Tools/LanDiagnosticResults/20260930-124709`).
- Protocol version is now 4: both players must use the new build because older builds send speculative checksums. Actual two-computer Wi-Fi/LAN behavior remains a separate field check; loopback does not prove every network condition.
