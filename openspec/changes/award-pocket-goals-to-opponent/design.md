## Context

Player one is the cyan north-side player and owns the north marble pocket; player two is the orange south-side player and owns the south pocket. `MatchSim.TryScore` currently treats the pocket owner as the scorer (`north -> P1`, `south -> P2`), while dunk scoring already awards the point to the player who did not fall in. The simulation intentionally does not track last touch, so marble-goal attribution must be derived solely from the defended pocket that was entered.

Score presentation also assumes the scorer identifies the goal location: `MatchDirector.PlayEvents` maps P1 to north and P2 to south. That assumption becomes visibly wrong once the opponent of the pocket owner receives the point.

## Goals / Non-Goals

**Goals:**

- Treat north and south pockets as the owning players' defended goals.
- Award a free-marble goal to the opponent of the pocket owner, including an own goal.
- Keep deterministic local and rollback simulation behavior independent of scene triggers and last-touch history.
- Make score presentation use the event's actual location rather than inferring it from the scorer.
- Cover both pocket directions and protect unchanged dunk/priority behavior with tests.

**Non-Goals:**

- Adding assists, last-touch attribution, shot ownership, or statistics.
- Changing pocket ownership, player spawn sides, pocket geometry, carried-marble filtering, dunk scoring, scoring priority, kickoff/reset flow, or win conditions.
- Changing network messages or persisted match data.

## Decisions

### Derive the scorer from the conceding pocket owner

The scoring branch will retain the fixed north/south overlap checks and fixed event priority, but will express the mapping as `opponent of pocket owner`: north owner P1 yields scorer P2, and south owner P2 yields scorer P1. A small centralized opponent mapping is preferred over treating the side itself as the scorer because it makes the defended-goal rule explicit and reduces the chance of another inversion.

Alternative considered: track the last player to shoot or touch each marble. This is rejected because the intended rule awards the opponent of the conceding goal regardless of who last touched the marble, and new mutable attribution state would unnecessarily expand rollback state.

### Carry the scoring location in the semantic event

`SimEvents` will expose the actual score position (or equivalent explicit score-origin data), populated from the pocket or dunk that ended the round. `MatchDirector` will place its pulse at that event location and continue coloring it with the scorer's identity. The event remains transient presentation data and is not added to the simulation snapshot checksum.

Alternative considered: reverse the existing scorer-to-Y lookup. This would encode another fragile assumption and still would not represent east/west dunk locations correctly.

### Make banner text scorer-first

Marble-goal reasons will identify the awarded player first and may also name the conceding pocket, for example `P2 SCORES - P1 POCKET`. The HUD color continues to come from `events.Scorer`. This prevents `P1 POCKET` from being mistaken for “P1 scored” after attribution is corrected.

### Test the rule at the deterministic boundary and through scene integration

EditMode simulation tests will assert the full symmetric truth table: north/P1 pocket awards only P2 and south/P2 pocket awards only P1. The existing PlayMode north-pocket assertion will be corrected, and integration coverage will verify the emitted score state and feedback path. Existing simultaneous-event tests remain the guard for the unchanged priority order.

## Risks / Trade-offs

- [Risk] Existing tests and main specs currently encode owner-scoring behavior, so changing only `TryScore` would leave the repository internally contradictory. → Mitigation: update both affected capability specs and symmetric test expectations in the same change.
- [Risk] Presentation code may display an effect at the scorer's home side instead of the conceded goal. → Mitigation: carry and consume an explicit score location.
- [Risk] Ambiguous reason strings can make a correct score appear wrong. → Mitigation: use scorer-first banner wording and scorer identity color.
- [Trade-off] Adding transient score-origin data slightly expands `SimEvents`. → It avoids coupling score presentation to player-side assumptions and also represents dunk locations correctly without changing rollback state.
