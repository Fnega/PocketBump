## MODIFIED Requirements

### Requirement: Simultaneous events resolve in a fixed order
If multiple scoring conditions become true in the same simulation tick, the system SHALL award exactly one point using this order: player-one dunk, player-two dunk, north marble pocket, south marble pocket.

#### Scenario: Dunk beats marble in the same frame
- **WHEN** player two falls into a dunk pocket AND a marble enters the south pocket in the same frame
- **THEN** only player one gains 1 point AND the marble goal is ignored

## ADDED Requirements

### Requirement: Match rules advance on a fixed tick
Kickoff countdown, live play, scoring freeze, and match-over SHALL advance on a fixed 60 Hz simulation tick. Scoring detection SHALL use that tick's simulated overlaps, not Unity Physics2D callbacks.

#### Scenario: Countdown is tick counted
- **WHEN** a kickoff begins
- **THEN** the gate stays closed for 3.0 seconds of simulation time (180 ticks at 60 Hz) before the round goes live
