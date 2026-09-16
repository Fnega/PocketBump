## Why

Marble goals are currently awarded to the owner of the pocket, so a player can shoot a marble into their own colored goal and gain a point. A player's north/south pocket is their defended goal, so conceding a marble there should award the point to the opponent instead.

## What Changes

- Award a north-pocket marble goal to player two because the north pocket belongs to player one.
- Award a south-pocket marble goal to player one because the south pocket belongs to player two.
- Keep marble scoring independent of last-touch attribution: any free marble entering a defended goal awards the opponent, including an own goal.
- Preserve carried-marble filtering, dunk scoring, simultaneous-event priority, round freeze/reset, and win rules.
- Make score feedback identify both the scoring player and the actual scoring location without deriving one from the other.
- Add symmetric automated coverage for both owned pockets and regression coverage for own-goal behavior.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `match-flow`: Change marble-goal attribution from the pocket owner to that owner's opponent while retaining last-touch independence and round-resolution rules.
- `arena-pockets`: Define north and south pockets as defended goals whose marble-goal events identify the conceding owner and award the opponent.

## Impact

- Deterministic scoring logic and score-event data in `MatchSim` / `SimEvents`.
- Score-location effects and score banners in `MatchDirector` / `MatchHud`.
- EditMode and PlayMode scoring tests, including the existing north-pocket expectation.
- Main OpenSpec requirements for `match-flow` and `arena-pockets`; no dependency, save-data, protocol, or control changes are expected.
