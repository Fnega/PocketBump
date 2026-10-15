## ADDED Requirements

### Requirement: Player-player contacts are stiffer than walls
Player-player circle collisions SHALL use a dedicated restitution of 0.90. Wall, marble, basket-rim, and player-marble collisions SHALL keep the existing table bounciness of 0.58.

#### Scenario: Head-on bump separates faster than a wall hit
- **WHEN** two players of equal mass approach each other along the line between centers at the same relative speed they would have against a static wall
- **THEN** their post-contact separating speed along that line is higher than the rebound speed from an equivalent wall contact using 0.58 restitution
