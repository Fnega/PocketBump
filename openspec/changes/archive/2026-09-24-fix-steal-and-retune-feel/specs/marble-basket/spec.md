## MODIFIED Requirements

### Requirement: Opponent basket overlapping a held marble steals it
When player B's basket interior overlaps a marble carried by player A, AND player B is not already carrying a marble, the marble SHALL become carried by player B. Either seat SHALL be able to steal from the other. A given marble SHALL be collected or stolen at most once per simulation tick. Contact between player bodies SHALL NOT transfer or eject a carried marble.

#### Scenario: Scoop steal
- **WHEN** player one is carrying a marble AND player two's empty basket interior overlaps that marble
- **THEN** player two carries the marble AND player one does not

#### Scenario: Player one can steal from player two
- **WHEN** player two is carrying a marble AND player one's empty basket interior overlaps that marble
- **THEN** player one carries the marble AND player two does not

#### Scenario: Same-tick steal is not reversed
- **WHEN** player two is carrying a marble AND player one's empty well overlaps it on a tick AND player two's well still overlaps the marble's pre-weld position
- **THEN** player one keeps the marble at the end of that tick

#### Scenario: Body check does not steal
- **WHEN** player one is carrying a marble AND player two's body collides with player one
- **THEN** player one still carries the marble

### Requirement: Held marble is launched only by a two-stage charge shot
A player SHALL start a charge only while carrying a marble, using a press of their shoot key sampled as a distinct press on a simulation tick (key down after release, latched into that tick). The first stage SHALL read a shot direction while the player may still move. The second stage SHALL read shot power while the player is held still (velocity cleared and movement input ignored). A second press SHALL lock direction; a third press SHALL fire. Firing SHALL release the marble along the locked direction at a speed taken from the power reading AND SHALL apply a recoil impulse to the shooter opposite that direction. Recoil impulse magnitude SHALL interpolate from 12 to 32 with charge power (about twice the previous 6–16 range) and SHALL be applied as velocity change divided by player mass.

#### Scenario: Direction stage allows movement
- **WHEN** a carrying player starts a charge AND is in the direction stage AND holds a movement key
- **THEN** the player still accelerates from that input AND the marble remains carried

#### Scenario: Power stage holds the player still
- **WHEN** a carrying player has locked direction AND is in the power stage
- **THEN** the player's velocity is treated as zero for movement AND movement keys do not accelerate the body

#### Scenario: Movement during power cancels
- **WHEN** a player is in the power stage AND applies movement input
- **THEN** the charge ends without firing AND the player still carries the marble

#### Scenario: Fire launches and recoils
- **WHEN** a player completes the power stage with a shoot press
- **THEN** the marble leaves the basket with velocity along the locked direction AND the player is pushed opposite that direction AND the player is no longer carrying

#### Scenario: Full-power recoil is clearly backward
- **WHEN** a player at rest fires a full-power shot due east
- **THEN** the shooter's velocity has a westward component of at least 8 units per second immediately after the fire tick

#### Scenario: Empty basket cannot charge
- **WHEN** a player is not carrying a marble AND presses shoot
- **THEN** no charge starts

#### Scenario: Steal cancels charge
- **WHEN** a player is charging AND the opponent steals the carried marble
- **THEN** the charge ends without firing
