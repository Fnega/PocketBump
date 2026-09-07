## 1. Bindings and menu

- [x] 1.1 Add a `ControlBindings` store with defaults P1 shoot J / boost K, P2 shoot Alpha1 / boost Alpha2; persist with PlayerPrefs; reject duplicate skill or movement keys
- [x] 1.2 Read boost (and later shoot) from bindings in `PlayerMotor` instead of hardcoded Left/Right Shift
- [x] 1.3 Show movement plus the four skill keys on rule setup; click-to-rebind with Escape cancelling listen without closing the panel
- [x] 1.4 Enlarge or reflow the rule-setup panel so bindings fit with points-to-win and marble count
- [x] 1.5 Point the pregame HUD banner at the current bindings (not L/R Shift)

## 2. Facing and basket

- [x] 2.1 Track facing from last non-zero movement input; spawn P1 facing south, P2 facing north
- [x] 2.2 Create a square basket child (identity-colored visual, U-shaped rim colliders, inner trigger) offset in front of each player
- [x] 2.3 Add `ArenaLayout` constants for basket size, offset, shot speed range, recoil scale, and needle period/angle

## 3. Carry, steal, scoring

- [x] 3.1 Collect on empty inner-trigger overlap with a free marble: kinematic parent into the well, at most one; body overlap does not collect
- [x] 3.2 Skip marble knockback and north/south scoring while `Carrier` is set; free marbles still score as today
- [x] 3.3 Transfer carry when the other player's empty basket trigger overlaps the held marble; body checks do not steal; cancel any charge on the victim
- [x] 3.4 Clear carry on round reset / marble despawn

## 4. Charge shot

- [x] 4.1 Direction stage: start on shoot `GetKeyDown` while carrying; allow movement; oscillating direction needle around live facing; next shoot-down after release locks world direction
- [x] 4.2 Power stage: zero velocity and ignore move/boost; oscillating power needle; movement input cancels and keeps the marble; next shoot-down fires
- [x] 4.3 On fire: unparent marble, apply locked-direction speed from the power reading, apply opposite recoil to the shooter
- [x] 4.4 Show direction and power indicators only during those stages; hide on fire or cancel

## 5. Tests and play

- [x] 5.1 EditMode: default bindings; duplicate rebind rejected; facing follows input; charge cannot start empty; power-stage cancel keeps carry
- [x] 5.2 PlayMode: basket collect vs body bounce; carried marble in north pocket does not score; free marble in pocket still scores; scoop steal; launch leaves the basket
- [x] 5.3 Run existing EditMode and PlayMode PocketBump tests and fix regressions (boost key tests, HUD string)
