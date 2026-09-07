# Menu Flow

## Purpose

Defines navigation from the main menu through rule setup, gameplay, and post-match results.

## Requirements

### Requirement: Main menu is separate from gameplay
The game SHALL open on a main menu that does not display or run the active arena simulation. The main menu SHALL offer starting a game and exiting the application.

#### Scenario: Application opens to main menu
- **WHEN** the application starts
- **THEN** the main menu is visible AND no match round is running

#### Scenario: Exit action
- **WHEN** the user selects Exit from the main menu
- **THEN** the application requests a clean quit

### Requirement: Start opens rule setup before gameplay
Selecting Start SHALL open a rule-setup screen before loading the arena. The rule setup SHALL allow selecting points-to-win and marbles-per-round within their supported ranges AND SHALL show both players' movement and skill key bindings.

#### Scenario: Start opens settings
- **WHEN** the user selects Start on the main menu
- **THEN** the rule-setup screen is shown before the arena loads

#### Scenario: Rule values can be adjusted
- **WHEN** the rule-setup screen is open
- **THEN** the user can change points-to-win and marbles-per-round within their valid ranges

#### Scenario: Bindings are visible on rule setup
- **WHEN** the rule-setup screen is open
- **THEN** player one and player two shoot and boost keys are shown

#### Scenario: Back returns to main menu
- **WHEN** the user selects Back from rule setup
- **THEN** the main menu is shown without starting a match

### Requirement: Rule setup shows and rebinds skill keys
The rule-setup screen SHALL display player one's WASD movement, player two's arrow-key movement, and the current shoot and boost keys for both players. The user SHALL be able to rebind each of the four skill keys (player-one shoot, player-one boost, player-two shoot, player-two boost) by selecting that binding and pressing a new key. Movement keys SHALL NOT be remappable from this screen. A new skill key SHALL be rejected if it is already used by another skill binding or by either player's movement keys. Escape SHALL cancel an in-progress rebind without leaving rule setup. Bindings SHALL persist on the machine and SHALL NOT be locked as match rules.

#### Scenario: Defaults are visible
- **WHEN** the rule-setup screen is open with factory bindings
- **THEN** the screen shows player one shoot as J, player one boost as K, player two shoot as 1, and player two boost as 2

#### Scenario: Rebind player two shoot
- **WHEN** the user selects player two's shoot binding AND then presses Keypad1
- **THEN** player two's shoot key becomes Keypad1 AND later matches use Keypad1 for player two's shot

#### Scenario: Duplicate skill key is rejected
- **WHEN** player one's shoot is J AND the user tries to bind player one's boost to J
- **THEN** the boost binding remains unchanged

#### Scenario: Escape cancels listen
- **WHEN** the user is listening for a new key AND presses Escape
- **THEN** the binding is unchanged AND the rule-setup screen remains open

### Requirement: Selected rules transfer to the arena
Starting from rule setup SHALL load the arena with the selected match settings. The selected settings SHALL remain fixed for that match.

#### Scenario: Custom rules reach gameplay
- **WHEN** the user selects 5 points-to-win and 3 marbles and then starts gameplay
- **THEN** the arena match uses 5 points-to-win and spawns 3 marbles each round

### Requirement: Match result provides navigation choices
After a match ends, a result screen SHALL identify the winner and SHALL offer Rematch, Modify Rules, and Main Menu actions.

#### Scenario: Rematch preserves rules
- **WHEN** the user selects Rematch
- **THEN** a new match starts with the same points-to-win and marble-count settings

#### Scenario: Modify Rules returns to setup
- **WHEN** the user selects Modify Rules
- **THEN** the rule-setup screen opens with the previous values available for editing

#### Scenario: Main Menu leaves gameplay
- **WHEN** the user selects Main Menu
- **THEN** the main menu loads and the arena simulation stops

### Requirement: Main menu offers LAN host and join
The main menu SHALL offer Host LAN and Join LAN in addition to starting a local game and exiting. Host LAN SHALL listen for a joiner after rule setup (or with the current rule values). Join LAN SHALL accept a host address and SHALL NOT load the arena until the handshake succeeds.

#### Scenario: Host is reachable from the menu
- **WHEN** the user selects Host LAN from the main menu and confirms rules
- **THEN** the application listens for a LAN joiner AND does not run a local two-keyboard match as the networked session

#### Scenario: Join needs an address
- **WHEN** the user selects Join LAN
- **THEN** the user can enter a host address AND a failed handshake does not leave a live arena match running

#### Scenario: Local start still exists
- **WHEN** the user selects Start and completes rule setup as a local match
- **THEN** the arena loads with both seats on that machine AND no LAN handshake is required
