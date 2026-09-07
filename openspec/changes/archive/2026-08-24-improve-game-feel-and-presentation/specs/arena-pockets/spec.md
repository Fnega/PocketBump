## ADDED Requirements

### Requirement: High-speed marbles can knock players back
A marble SHALL apply additional controlled knockback to a player when its impact speed reaches the dangerous threshold. Knockback SHALL increase with relative impact speed and SHALL follow the impact direction. Low-speed marble contact SHALL retain ordinary physics behavior without dangerous knockback.

#### Scenario: Slow marble contact remains ordinary
- **WHEN** a marble contacts a player below the dangerous speed threshold
- **THEN** the contact uses normal 2D physics and does not add dangerous knockback

#### Scenario: Dangerous marble knocks a player
- **WHEN** a marble contacts a player above the dangerous speed threshold
- **THEN** the player receives additional impulse in the impact direction proportional to relative impact speed

#### Scenario: Dangerous marble can cause a dunk
- **WHEN** a dangerous marble knocks a player toward an east or west pocket with sufficient impulse during a live round
- **THEN** the player can enter that dunk pocket as a result of the marble collision

### Requirement: Dangerous impacts transfer marble energy
After a marble applies dangerous knockback to a player, the marble SHALL lose a configured fraction of its speed. The same continuous contact SHALL NOT apply the additional knockback repeatedly before a short collision cooldown expires.

#### Scenario: Marble slows after transferring energy
- **WHEN** a dangerous marble applies knockback to a player
- **THEN** the marble's speed is reduced while preserving its post-collision travel direction

#### Scenario: One contact does not stack impulses
- **WHEN** a dangerous marble remains in contact with the same player across multiple physics callbacks within the collision cooldown
- **THEN** additional dangerous knockback is applied at most once during that cooldown
