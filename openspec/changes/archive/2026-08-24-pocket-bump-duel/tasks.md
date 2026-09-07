## 1. Arena

- [x] 1.1 Create the match scene with table bounds, north/south marble pockets, east/west dunk pockets, and a disable-able center gate collider
- [x] 1.2 Add physics layers and colliders so marbles enter only N/S pockets and players enter only E/W pockets
- [x] 1.3 Mark north as P1 pocket, south as P2 pocket, and expose half-center spawn points plus table-center marble spawn

## 2. Local players

- [x] 2.1 Add two Rigidbody2D players with WASD (P1) and arrow keys (P2) force/velocity movement and no jump
- [x] 2.2 Frame the whole table with one camera so both players, all four pockets, and future HUD fit on screen

## 3. Match flow

- [x] 3.1 Implement MatchSettings with points-to-win (default 8, range 1–21) and marble count (default 1, range 1–8)
- [x] 3.2 Implement MatchDirector states: 3s kickoff (gate closed, players can move in-half, no marbles) → open gate → spawn marbles at rest → live
- [x] 3.3 Award exactly one point per live round: pocket owner for marbles; opponent for dunks; same-frame order dunks then north then south; then freeze and reset
- [x] 3.4 End the match when a player reaches points-to-win and leads by 2; otherwise start another kickoff from half-center spawns

## 4. HUD and settings

- [x] 4.1 Show both scores, kickoff countdown, and a winner banner that stops further rounds
- [x] 4.2 Add a pre-match settings panel to edit points-to-win and marble count, locked after the first kickoff

## 5. Playable pass

- [x] 5.1 Verify defaults (1 marble, first to 8 win-by-2), own-goals, self-dunks, and multi-marble spawn do not overlap or start with velocity
- [x] 5.2 Tune pocket widths, player mass, and bounce so camping a goal is leaky and a live bump can dunk without a special button
