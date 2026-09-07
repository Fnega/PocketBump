## Why

The first playable version proves the round and scoring loop, but player movement is overly direct, fast marbles do not feel dangerous, and the runtime-built UI and placeholder shapes do not yet present the game as a polished competitive experience. Improving feel and presentation now establishes a strong core before adding power-ups or networking.

## What Changes

- Replace near-instant player steering with momentum-based acceleration, braking, and directional inertia.
- Add independent hold-to-boost controls for both local players, with a short wind-up, increased top speed, reduced steering, and visible boost feedback.
- Make sufficiently fast marbles visibly dangerous and capable of imparting meaningful knockback to players, while transferring energy on impact.
- Add speed- and state-driven trails for players and marbles.
- Separate the experience into main menu, rule setup, gameplay, and result flows.
- Replace placeholder presentation with a coherent neon tabletop style, clearer pockets and center gate, improved player/marble visuals, and TextMeshPro-based UI.
- Preserve the existing scoring, kickoff, win-by-two, and configurable marble-count rules.
- Do not add arena power-ups in this change.

## Capabilities

### New Capabilities

- `menu-flow`: Main menu, rule setup, arena loading, result actions, and transfer of selected match settings.
- `visual-presentation`: Consistent arena art direction plus visual feedback for movement speed, boost state, dangerous marbles, scoring, and match state.

### Modified Capabilities

- `local-two-player`: Change player movement from highly responsive target velocity to momentum-based movement and add separate boost inputs with readable trade-offs.
- `arena-pockets`: Make high-speed marbles dangerous to players and define speed-based knockback and energy transfer behavior.

## Impact

- Player movement and collision tuning in `PlayerMotor`, `ArenaLayout`, and player construction.
- Marble motion/collision behavior in `MarbleMotor` and marble construction.
- Existing runtime UI in `MatchHud` and match startup flow in `MatchBootstrap` / `MatchDirector`.
- New menu and arena scene organization, shared runtime match configuration, TextMeshPro UI, trail effects, materials, sprites, and lightweight particles.
- No network package, power-up system, or scoring-rule migration is included.
