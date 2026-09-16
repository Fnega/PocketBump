## 1. Correct Deterministic Score Attribution

- [x] 1.1 Update `MatchSim.TryScore` so a north-pocket goal awards P2 and a south-pocket goal awards P1 through an explicit opponent-of-pocket-owner mapping, while preserving carried-marble filtering and the existing simultaneous-event priority.
- [x] 1.2 Make marble-goal reason strings identify the scorer first and the conceding pocket second, without introducing last-touch state.
- [x] 1.3 Add explicit transient score-origin data to `SimEvents` and populate it for north/south marble goals and east/west player dunks without adding it to rollback snapshots or checksums.

## 2. Align Score Presentation

- [x] 2.1 Update `MatchDirector.PlayEvents` to place score effects at the event's explicit score origin rather than deriving a north/south position from the scorer.
- [x] 2.2 Verify the HUD banner uses the corrected scorer identity color and unambiguous scorer-first reason text for both marble goals.

## 3. Add Regression Coverage

- [x] 3.1 Add EditMode simulation tests proving a free marble in P1's north pocket awards only P2 and a free marble in P2's south pocket awards only P1.
- [x] 3.2 Retain or extend EditMode coverage proving dunk attribution and the fixed simultaneous-event priority are unchanged.
- [x] 3.3 Update PlayMode pocket-scoring expectations and add symmetric scene-level coverage for both north and south defended goals, including round transition behavior.

## 4. Validate the Change

- [ ] 4.1 Run the PocketBump EditMode and PlayMode test suites and resolve any scoring, presentation, or rollback regressions.
- [x] 4.2 Run OpenSpec validation for `award-pocket-goals-to-opponent` and confirm all artifacts and delta requirements are valid.
