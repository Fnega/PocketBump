## ADDED Requirements

### Requirement: Shot power retains the full intended speed range through a simulation tick

Firing a carried marble SHALL map power from 0 through 1 linearly to launch speed from 8 through 22 units per second along the locked shot direction. The existing free-marble drag/limit stage SHALL use a shared speed limit equal to the maximum intended shot speed rather than a lower limit that flattens high-power shots. Natural drag SHALL remain active during the fire tick and subsequent ticks. This shared limit SHALL also apply to other free marbles at that stage; it SHALL NOT introduce a shot-only state flag or a new post-collision clamp. Existing charge controls, release of possession, and the recoil impulse range of 12–32 divided by player mass SHALL remain unchanged.

#### Scenario: Minimum and maximum launch speeds retain option A

- **WHEN** a carried marble is fired at power 0 or power 1
- **THEN** its initial launch speed before ordinary tick drag is respectively 8 or 22 units per second along the locked shot direction

#### Scenario: Distinct high powers produce distinct real tick speeds

- **WHEN** otherwise identical collision-free shots at power 0.75 and power 1 are fired through the ordinary simulation tick in open space
- **THEN** the full-power shot has strictly higher speed at the end of that tick, both shots have speeds above 16, and neither is flattened to the old 16-unit limit

#### Scenario: Full-power fire tick applies drag without premature clipping

- **WHEN** a full-power shot is fired through an ordinary 60 Hz tick with marble drag 2 and no intervening collision, collection, or scoring
- **THEN** its speed after that tick equals `22 / (1 + 2 / 60)` within floating-point tolerance, approximately 21.29 units per second

#### Scenario: Subsequent free flight keeps slowing

- **WHEN** a launched marble continues across collision-free space without another impulse
- **THEN** subsequent ticks reduce its speed under the existing drag and eventually permit it to rest rather than maintaining launch speed

#### Scenario: Shared free-marble limit uses the intended maximum

- **WHEN** a free marble whose velocity came from an earlier collision passes through the existing drag/limit stage
- **THEN** the stage uses the same maximum of 22 as for fired marbles, retains values between 16 and 22 after drag, and clamps values above 22 at that stage

#### Scenario: Firing still releases and recoils for either seat

- **WHEN** either player completes the unchanged direction and power stages and fires a carried marble
- **THEN** the marble becomes free, the shooter no longer carries it, and the opposite recoil impulse uses the unchanged 12–32 power mapping and player mass

#### Scenario: High-power shots preserve obstacle and basket interaction

- **WHEN** a full-power shot reaches a wall, blocking pocket edge, basket rim, or eligible empty basket interior in an isolated regression setup
- **THEN** it obeys the existing bounce, blocking, or collection rule for that surface and does not bypass the rule because the corrected shot can travel faster than 16
