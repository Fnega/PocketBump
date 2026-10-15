## 1. Confirmed behavior changes

- [x] 1.1 Hide the transient input-wait network panel while retaining terminal fault display, recovery, input buffering and diagnostics; update real-loopback HUD regressions.
- [x] 1.2 Default local P2 Dash to Keypad2, preserve valid persisted bindings, and update default/input/current-guidance regression coverage.

## 2. Verification and handoff

- [x] 2.1 Run affected EditMode and PlayMode tests, including terminal fault/menu regressions, and review the scoped changes without modifying shipped packages.
- [x] 2.2 Record actual verification results and explain saved-binding behavior in current guidance, without rewriting historical evidence.

## 3. Approved version 1.2 delivery

- [x] 3.1 Update current 1.2 identity, versioned build/package checks and new release guidance while retaining older-version validation.
- [x] 3.2 Build an isolated Release candidate and run authorized visible ordinary-UI dual-window acceptance with advance notice; retain screenshots/reports and exact candidate hashes.
- [x] 3.3 Package the accepted 1.2 files into the new folder and ZIP, independently verify all hashes and preserve earlier shipped versions.

Completed: source regressions passed (EditMode 63/63, PlayMode 158/158), packaging fixture 528/528 passed, and isolated Release candidate built with 0 errors / 0 warnings. First failed UI timing evidence is preserved. User-approved incremental report observation (1032 fixture assertions), prefilled join sequencing (22) and window geometry (35) passed. Same-candidate ordinary-UI retest Tools/ReleaseDemoResults/20261015-124337-607-893f61 passed all 6 checks, with 20 screenshots and 9 reports; subsequent independent visual review passed. Handshake starts now differ by 1.388 / 1.195 seconds, with no game timeout change. New PocketBump1.2 folder/ZIP contain 210 independently audited files (162 payload, 44 notices, 3 documents and hash manifest); six old 1.0/1.1 baselines are unchanged. Final receipt: Tools/ReleaseResults/20261015-1.2/delivery-receipt.json. Physical two-computer campus validation remains external, not claimed complete.
