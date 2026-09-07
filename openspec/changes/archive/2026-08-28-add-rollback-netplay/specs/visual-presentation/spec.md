## MODIFIED Requirements

### Requirement: Collect and fire pulses
A successful basket collect (including a scoop steal) SHALL play a short world-space pulse at the new carrier's basket in that player's identity color. Firing a charged shot SHALL play a short world-space pulse at the launch point. Body bounce, a rejected second marble, and charge cancel SHALL NOT play those pulses. In a networked match those pulses SHALL play only for events that remain true after rollback (confirmed ticks), not for predictions that are later corrected.

#### Scenario: Collect pulse
- **WHEN** an empty basket interior overlaps a free marble and that player starts carrying it
- **THEN** a pulse is visible at that player's basket in that player's identity color

#### Scenario: Steal pulse
- **WHEN** player two's empty basket steals the marble from player one
- **THEN** a pulse is visible at player two's basket in player two's identity color

#### Scenario: Fire pulse
- **WHEN** a player completes the power stage and launches the marble
- **THEN** a pulse is visible at the launch point

#### Scenario: Bounce is silent
- **WHEN** a marble hits a player body and is not collected
- **THEN** no collect pulse plays

#### Scenario: Rolled-back collect is silent
- **WHEN** a peer predicted a collect on tick 50 AND rollback determines tick 50 had no collect
- **THEN** no collect pulse remains from that prediction

## ADDED Requirements

### Requirement: Views follow the simulation
Player, marble, basket, and gate presentation SHALL take pose and charge readings from the match simulation (interpolating between ticks for display). Unity Physics2D bodies SHALL NOT independently move gameplay objects away from the sim.

#### Scenario: Presented pose matches sim
- **WHEN** the simulation places player one at a position after a tick
- **THEN** the visible player mesh is at that position (within render interpolation)
