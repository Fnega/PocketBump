## MODIFIED Requirements

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
