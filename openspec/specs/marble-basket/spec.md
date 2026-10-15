# Marble Basket

## Purpose

Defines the front-facing basket, single-marble carry, scoop steal, and two-stage charge shot.

## Requirements

### Requirement: Each player has a front basket that follows movement input
Each player SHALL have a square basket mounted in front of their body at an offset that stays outside the player radius (1.08 for radius 0.78). While movement input is non-zero, the basket SHALL rotate toward that input direction rather than snapping. While movement input is zero, the basket SHALL keep its last facing. Player one SHALL spawn facing south. Player two SHALL spawn facing north.

#### Scenario: Basket turns toward input
- **WHEN** player one holds D while facing south
- **THEN** player one's basket rotates toward east AND faces east within 0.25 seconds

#### Scenario: Facing holds after release
- **WHEN** a player was holding W AND then releases all movement keys
- **THEN** the basket continues to face north

#### Scenario: Spawn facings
- **WHEN** a kickoff places both players
- **THEN** player one's basket faces south AND player two's basket faces north

### Requirement: Basket contact collects one free marble
A player SHALL collect a free scoring marble only when that player's basket interior overlaps the marble AND the player is not already carrying a marble. Body contact with a marble SHALL NOT collect it. A player SHALL carry at most one marble. A carried marble SHALL stay at rest in the basket until shot, stolen, or the round clears it.

#### Scenario: Basket scoops a free marble
- **WHEN** an empty basket interior overlaps a free marble
- **THEN** that player carries the marble AND the marble remains still in the basket

#### Scenario: Body does not collect
- **WHEN** a player's body overlaps a free marble AND the basket interior does not
- **THEN** the player does not start carrying that marble AND the marble uses ordinary simulated collision against the body

#### Scenario: Occupied basket does not collect a second marble
- **WHEN** a player is already carrying a marble AND their basket overlaps a different free marble
- **THEN** the player still carries only the first marble AND the second marble is not collected

### Requirement: Basket mouth is wider than a marble
The empty basket's open face (inner distance between the side rims) SHALL be wider than a scoring marble's diameter so a marble approaching that open face can overlap the interior well. Body contact SHALL still not collect. Rim thickness SHALL NOT be used as the collect surface.

#### Scenario: Front-on marble reaches the well
- **WHEN** a free marble approaches an empty basket along that basket's facing
- **THEN** the marble can overlap the interior well without being blocked by the side-rim opening being narrower than the marble

#### Scenario: Body still does not collect
- **WHEN** a player's body overlaps a free marble AND the basket interior does not
- **THEN** the player does not start carrying that marble

### Requirement: Empty basket catches a flying marble and body bounces it
When a marble not currently carried overlaps an empty basket interior, the owner of that basket SHALL collect it. When a marble overlaps a player body and not that player's empty basket interior, the marble SHALL bounce using ordinary simulated collision (including existing dangerous-knockback rules).

#### Scenario: Shot marble hits an empty basket
- **WHEN** a launched marble overlaps player two's empty basket interior
- **THEN** player two carries that marble

#### Scenario: Shot marble hits a body
- **WHEN** a launched marble overlaps player two's body AND does not overlap player two's empty basket interior
- **THEN** player two does not collect it AND the marble continues as a simulated free body

### Requirement: Opponent basket overlapping a held marble steals it
When player B's empty basket steal region overlaps player A's basket steal region while player A carries a marble, the marble SHALL become carried by player B immediately on that simulation tick. Either seat SHALL be able to steal from the other. A continuous basket-contact episode SHALL transfer that marble at most once: after a successful steal, the new carrier SHALL retain the marble while the baskets remain in contact, and another transfer SHALL become eligible only after the baskets separate and begin a new contact episode. The carried-marble steal region SHALL correspond to the visible basket footprint and SHALL be independent of the narrower free-marble collection well. Contact between player bodies alone SHALL NOT transfer or eject a carried marble.

#### Scenario: Scoop steal
- **WHEN** player one is carrying a marble AND player two's empty basket steal region begins overlapping player one's basket steal region
- **THEN** player two carries the marble on that simulation tick AND player one does not

#### Scenario: Player one can steal from player two
- **WHEN** player two is carrying a marble AND player one's empty basket steal region begins overlapping player two's basket steal region
- **THEN** player one carries the marble on that simulation tick AND player two does not

#### Scenario: Sustained contact does not reverse possession
- **WHEN** player two steals a marble from player one AND the two basket steal regions remain continuously overlapped for subsequent simulation ticks
- **THEN** player two retains the marble AND no additional possession-transfer event is emitted during that contact episode

#### Scenario: Separation permits a later counter-steal
- **WHEN** player two has stolen a marble, the basket steal regions fully separate, and player one's empty basket begins a new overlap with player two's basket
- **THEN** player one may steal the marble during the new contact episode

#### Scenario: Visible basket contact uses the steal region
- **WHEN** the two visible basket footprints overlap in a valid steal contact while the carried marble would be outside the opponent's narrower free-marble well
- **THEN** the empty basket steals the carried marble AND the dimensions of free-marble collection remain unchanged

#### Scenario: Body check does not steal
- **WHEN** player one is carrying a marble AND player two's body collides with player one without their basket steal regions overlapping
- **THEN** player one still carries the marble

#### Scenario: Rollback replay preserves the contact episode
- **WHEN** a steal occurs during sustained basket contact and the simulation restores an earlier snapshot then replays the same inputs
- **THEN** the same player owns the marble on every replayed tick AND the replayed state checksum matches the baseline

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

### Requirement: Carried marble does not score in marble pockets
While a marble is carried, overlap between that marble and a north or south pocket SHALL NOT register a scoring event.

#### Scenario: Walk a held marble into a pocket
- **WHEN** the round is live AND a carried marble overlaps the north pocket
- **THEN** no marble-goal event is registered AND the marble remains carried

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
