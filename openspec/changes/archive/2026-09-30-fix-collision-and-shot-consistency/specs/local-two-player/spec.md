## ADDED Requirements

### Requirement: Dynamic circle contacts resolve closing motion correctly

The shared simulation SHALL resolve player/player, player/marble, and marble/marble circle contacts with a unit contact normal, inverse-mass penetration correction, and a base restitution impulse only for closing normal motion. For isolated base-circle resolution with positive finite masses, the impulse SHALL preserve pair momentum and tangential velocity, and the separating normal speed SHALL equal the closing normal speed multiplied by the configured restitution within floating-point tolerance. Already separating or purely tangential contacts SHALL NOT receive another base restitution impulse. This requirement SHALL NOT remove the separately configured gameplay knockback for player/marble contacts. Existing masses and restitution values SHALL remain unchanged.

#### Scenario: Approaching players rebound

- **WHEN** two player circles approach along their contact normal and touch or overlap
- **THEN** their base contact response changes closing motion to separating motion using restitution 0.90 and the existing player masses

#### Scenario: Separating overlap is corrected without a second rebound

- **WHEN** two overlapping circles already have separating normal velocities and only the base circle solver is applied
- **THEN** their penetration is corrected in inverse-mass proportions while their velocities remain unchanged

#### Scenario: Tangential motion is not reflected

- **WHEN** two overlapping circles have zero relative normal velocity but nonzero tangential velocity and only the base circle solver is applied
- **THEN** their penetration is corrected without changing their tangential or normal velocities

#### Scenario: Unequal mass contact conserves base momentum

- **WHEN** a player circle and a free marble circle undergo an isolated closing base contact without additional gameplay knockback
- **THEN** the impulse uses their existing unequal masses, preserves total momentum, and produces the normal separating speed specified by restitution 0.58

#### Scenario: Marble contacts use table restitution

- **WHEN** two free marble circles undergo an isolated closing base contact
- **THEN** they exchange impulse using their existing masses and restitution 0.58

#### Scenario: Exact touching can rebound without moving positions

- **WHEN** circle centers are exactly one radius sum apart and the circles have closing normal velocity
- **THEN** base contact applies the appropriate normal impulse without a penetration displacement

#### Scenario: Noncontact does not mutate the pair

- **WHEN** the circles are farther apart than their radius sum
- **THEN** base circle resolution does not change their positions or velocities

#### Scenario: Exchanging a nondegenerate pair preserves the physical result

- **WHEN** an isolated contact whose center separation is outside the degenerate fallback range is resolved with A and B exchanged together with their positions, velocities, radii, and masses
- **THEN** the outputs assigned back to the original bodies match within floating-point tolerance

### Requirement: Degenerate circle contacts remain bounded and replayable

For supported finite simulation positions and velocities, coincident or near-coincident circle contacts SHALL use a deterministic unit fallback normal when center separation cannot reliably determine a normal. Penetration correction SHALL remain mass-weighted, SHALL NOT move either body by more than the pair's radius sum plus numerical tolerance in one resolution, and SHALL NOT produce NaN or infinite positions or velocities. Repeated isolated-pair resolution SHALL remove overlap within numerical tolerance. Resolving identical input state again SHALL produce identical output; no random or wall-clock input SHALL select the fallback.

#### Scenario: Coincident circles do not fly across the arena

- **WHEN** two stationary circle centers coincide exactly and base contact is resolved
- **THEN** finite mass-weighted corrections separate them by their radius sum without either correction exceeding that sum plus numerical tolerance

#### Scenario: Very small offsets do not amplify the normal

- **WHEN** stationary circle centers differ by small positive or negative offsets around the normal-selection tolerance
- **THEN** their contact resolution stays finite and bounded and repeated isolated resolution removes the overlap within numerical tolerance

#### Scenario: Coincident moving circles have a repeatable response

- **WHEN** the same supported coincident-center state with finite velocities is resolved multiple times from an identical initial state
- **THEN** the chosen separation direction, resulting positions, and resulting velocities are identical and finite

#### Scenario: Delayed input replays corrected collisions and shots

- **WHEN** a two-seat input tape containing actual circle collisions and high-power shots is run once with all inputs available and once with remote inputs arriving late within the rollback window
- **THEN** after corrections settle at the same confirmed tick the complete simulation state and checksum match the baseline for either local seat, with at least one rollback and the intended collision/shot events exercised
