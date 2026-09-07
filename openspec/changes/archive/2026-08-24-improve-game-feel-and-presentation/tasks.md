## 1. Player movement and boost

- [x] 1.1 Replace target-velocity movement with separate forward acceleration, turning/reversal, coasting, and braking behavior driven by Rigidbody2D velocity
- [x] 1.2 Add independent boost input/state for P1 Left Shift and P2 Right Shift, including wind-up, boosted limits, reduced steering, release recovery, and read-only state for feedback
- [x] 1.3 Add focused movement tests/debug checks for acceleration over time, non-instant reversal, independent boost activation, and return to normal limits

## 2. Dangerous marble collisions

- [x] 2.1 Add marble-player impact handling with normal, mild, and dangerous speed bands and relative-speed-scaled knockback
- [x] 2.2 Apply marble energy loss and per-player contact cooldown after dangerous knockback without changing low-speed dribbling behavior
- [x] 2.3 Verify slow contact, dangerous knockback, energy transfer, cooldown behavior, and marble-caused dunks in play mode

## 3. Speed and state feedback

- [x] 3.1 Add identity-colored player trails that fade while stationary and intensify during boost
- [x] 3.2 Add neutral and dangerous speed-responsive marble trail/glow states
- [x] 3.3 Add lightweight effects for center-gate opening, scoring, dangerous marble impact, and match victory

## 4. Menu, session, and result flow

- [x] 4.1 Add a persistent GameSession that carries selected MatchSettings safely between scenes and supports rematch
- [x] 4.2 Create MainMenu scene with TextMeshPro title/menu UI and a separate rule-setup panel for win score and marble count
- [x] 4.3 Create or migrate to an authored Arena scene that reads GameSession settings and no longer depends on automatic runtime bootstrap
- [x] 4.4 Add a result overlay with Rematch, Modify Rules, and Main Menu actions wired to the correct scene/session behavior
- [x] 4.5 Update build settings to start from MainMenu and validate scene transitions, back navigation, rematch, and custom-rule transfer

## 5. Arena and character presentation

- [x] 5.1 Create reusable neon-style materials/sprites for the dark tabletop, cyan P1, orange P2, neutral marble, red dunk pockets, and yellow gate
- [x] 5.2 Replace placeholder player/marble visuals with layered rim/core/highlight presentation while preserving collider sizes and gameplay readability
- [x] 5.3 Restyle arena floor, walls, pockets, gate, camera framing, match HUD, and result UI without obscuring center play or pocket entrances

## 6. Verification and tuning

- [x] 6.1 Run compilation and automated checks, fix errors, and validate both MainMenu and Arena scenes
- [x] 6.2 Playtest local two-player movement and tune acceleration, boost steering/recovery, trail thresholds, marble danger thresholds, knockback, and energy loss
- [x] 6.3 Confirm existing scoring, kickoff, configurable marble count, and win-by-two behavior remain unchanged
