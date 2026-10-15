## MODIFIED Requirements

### Requirement: Start opens rule setup before gameplay
Selecting Start SHALL open rule setup before loading the arena. The screen SHALL allow points-to-win and free-scoring-balls-per-round within their existing supported ranges: 1 through 21 points with default 8, and 1 through 8 balls with default 1. It SHALL explain that victory still requires reaching the selected target and leading by at least two points. The screen SHALL show both players' movement and Dash bindings and SHALL NOT present retired basket, charge-shot, Shoot, or hold-to-Boost controls. Choosing local play SHALL clearly identify left/right split-screen mode.

#### Scenario: Start opens settings
- **WHEN** the user selects Start on the main menu
- **THEN** rule setup appears before the arena loads and identifies local left/right two-player play

#### Scenario: Rule values can be adjusted
- **WHEN** rule setup is open
- **THEN** the user can change points-to-win and free-scoring-ball count within their valid ranges while the win-by-two rule remains visible

#### Scenario: Bindings are visible on rule setup
- **WHEN** rule setup is open
- **THEN** player one and player two movement and Dash bindings are shown without retired shooting or continuous-Boost instructions

#### Scenario: Back returns to main menu
- **WHEN** the user selects Back from rule setup
- **THEN** the main menu is shown without starting a match

### Requirement: Rule setup shows and rebinds skill keys
The rule-setup screen SHALL display player one's WASD movement, player two's arrow-key movement, and each player's current Dash binding. Factory Dash bindings SHALL be K for player one and Keypad2, the numeric-keypad 2, for player two. The user SHALL be able to rebind the two Dash actions by selecting a binding and pressing a valid new key. Movement keys SHALL NOT be remappable from this screen. A new Dash binding SHALL be rejected if it conflicts with the other Dash action or either player's movement keys. Escape SHALL cancel listening without leaving rule setup. Bindings SHALL persist locally and SHALL NOT be locked as network match rules. The game SHALL migrate valid persisted former Boost bindings to Dash, validate the resulting two-key mapping, and fall back to a valid nonconflicting default mapping when required. Retired Shoot bindings SHALL NOT trigger gameplay, appear as editable actions, or reserve otherwise valid keys.

#### Scenario: Defaults are visible
- **WHEN** rule setup is opened with factory settings
- **THEN** it shows player-one Dash as K and player-two Dash as numeric-keypad 2 and does not display four legacy skill bindings

#### Scenario: Rebind player two Dash
- **WHEN** the user selects player two's Dash binding and presses Keypad1 without a conflict
- **THEN** player two's Dash becomes Keypad1 and later matches use Keypad1 for that player's discrete Dash

#### Scenario: Duplicate skill key is rejected
- **WHEN** player one's Dash is K and the user tries to bind player two's Dash to K
- **THEN** player two's binding remains unchanged and the UI does not falsely report success

#### Scenario: Escape cancels listen
- **WHEN** the user is listening for a new key and presses Escape
- **THEN** the binding is unchanged and rule setup remains open

#### Scenario: Existing Boost preference migrates
- **WHEN** saved former Boost bindings form a valid nonconflicting pair
- **THEN** those keys become the corresponding Dash bindings without requiring the user to reconfigure them

#### Scenario: Retired Shoot binding does not reserve a key
- **WHEN** a former Shoot preference is J and J is otherwise a valid unused Dash key
- **THEN** J does not fire a shot and can be explicitly rebound to a Dash action

### Requirement: Selected rules transfer to the arena
Starting from rule setup SHALL load the arena with the selected target score and free-scoring-ball count and the approved arena gameplay profile. Those settings SHALL remain fixed for the match and SHALL be included in LAN compatibility validation where they affect simulation. Display mode, per-machine bindings, and local camera viewport layout SHALL NOT change the shared gameplay rules. Candidate geometry, Buff strengths, durations, or spawn frequencies not yet approved SHALL NOT be silently exposed as finalized gameplay settings.

#### Scenario: Custom rules reach gameplay
- **WHEN** the user selects 5 points-to-win and 3 balls and starts gameplay
- **THEN** the match uses target 5 with a two-point winning lead and spawns 3 free scoring balls per round under the approved gameplay profile

#### Scenario: Presentation mode does not change rules
- **WHEN** the same selected rules are used for local split-screen and LAN full-screen play
- **THEN** the simulation uses the same arena and gameplay profile despite the different camera layouts

### Requirement: Match result provides navigation choices
After a local match ends, a result screen SHALL identify the winner and SHALL offer Rematch, Modify Rules, and Main Menu actions. A LAN result SHALL identify a final winner and enable Rematch only after corrected terminal agreement with the peer; before agreement it SHALL show a result-confirmation state. LAN Rematch SHALL request a same-rules bilateral rematch rather than reload the local scene. LAN results SHALL offer Main Menu and explain that rule changes require leaving the connection and creating a new Host LAN room from the main menu; they SHALL NOT expose an actionable in-session Modify Rules button or silently switch the participant to local play. Authored, generated, and repaired result UI SHALL follow the same mode-aware action policy and SHALL use the approved collision-and-Dash terminology.

#### Scenario: Rematch preserves rules
- **WHEN** the user selects Rematch after a local match
- **THEN** a new local match starts with the same target score, free-ball count, and approved gameplay profile

#### Scenario: Modify Rules returns to setup
- **WHEN** the user selects Modify Rules after a local match
- **THEN** rule setup opens with the previous values available for editing

#### Scenario: Main Menu leaves gameplay
- **WHEN** the user selects Main Menu from the result or rematch-waiting screen
- **THEN** the local main menu loads, normal game time is restored, and the arena simulation stops; for LAN play the local connection is disposed and departure is signaled to the peer

#### Scenario: Predicted finish is still being confirmed
- **WHEN** local simulation has reached match-over but terminal agreement has not completed
- **THEN** the UI shows a confirmation state without an authoritative final winner or enabled rematch action, and Main Menu remains available

#### Scenario: LAN player waits for the other consent
- **WHEN** a player selects Rematch after a confirmed LAN result and the other player has not consented
- **THEN** the result UI shows that it is waiting for the other player, repeat clicks do not create new matches, and Main Menu remains available

#### Scenario: Both peers prepare the next game
- **WHEN** both players consent but the new-epoch start barrier is still pending
- **THEN** the UI shows a starting or waiting state and does not misrepresent it as a new confirmed result or a network failure

#### Scenario: LAN players want different rules
- **WHEN** LAN results are shown and a player wants to change the target score or free-ball count
- **THEN** the UI instructs the player to use Main Menu, have the host create a new Host LAN room with the desired rules, and join that room; the current connection's rules remain unchanged

#### Scenario: Authored and fallback result UI agree
- **WHEN** the Arena uses an authored result panel, a generated fallback, or repaired legacy buttons
- **THEN** local actions retain their existing behavior and LAN buttons use the same confirmation, bilateral rematch, and leave-to-change-rules policy without duplicate listeners

## ADDED Requirements

### Requirement: Mode and network-failure navigation remain explicit
The UI SHALL distinguish local two-player left/right split-screen from LAN play with one full-screen local-seat view. Changing windowed or full-screen display settings SHALL NOT change the play mode, assigned seat, session, or rules. A network fault SHALL preserve the existing frozen-match and visible-reason behavior until the user explicitly chooses Main Menu; a camera rebuild, death, or popup SHALL NOT automatically return to the menu or convert a LAN session to local play.

#### Scenario: Fullscreen preserves the session
- **WHEN** a supported display-mode change is applied during a match
- **THEN** viewport and HUD layout adapt while the play mode, logical seat, connection, and gameplay rules remain unchanged

#### Scenario: Network failure does not leave silently
- **WHEN** a network fault stops a LAN match
- **THEN** the current gameplay view remains frozen with a visible reason and an explicit Main Menu action instead of silently loading the menu

#### Scenario: Respawn is not navigation
- **WHEN** the local player dies and later respawns
- **THEN** the existing match, camera mode, and controls continue without opening rule setup or leaving the LAN connection
