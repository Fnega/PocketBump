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
When player B's basket interior overlaps a marble carried by player A, AND player B is not already carrying a marble, the marble SHALL become carried by player B. Contact between player bodies SHALL NOT transfer or eject a carried marble.

#### Scenario: Scoop steal
- **WHEN** player one is carrying a marble AND player two's empty basket interior overlaps that marble
- **THEN** player two carries the marble AND player one does not

#### Scenario: Body check does not steal
- **WHEN** player one is carrying a marble AND player two's body collides with player one
- **THEN** player one still carries the marble

### Requirement: Held marble is launched only by a two-stage charge shot
A player SHALL start a charge only while carrying a marble, using a press of their shoot key sampled as a distinct press on a simulation tick (key down after release, latched into that tick). The first stage SHALL read a shot direction while the player may still move. The second stage SHALL read shot power while the player is held still (velocity cleared and movement input ignored). A second press SHALL lock direction; a third press SHALL fire. Firing SHALL release the marble along the locked direction at a speed taken from the power reading AND SHALL apply a recoil impulse to the shooter opposite that direction.

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
