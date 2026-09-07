## ADDED Requirements

### Requirement: Main menu is separate from gameplay
The game SHALL open on a main menu that does not display or run the active arena simulation. The main menu SHALL offer starting a game and exiting the application.

#### Scenario: Application opens to main menu
- **WHEN** the application starts
- **THEN** the main menu is visible AND no match round is running

#### Scenario: Exit action
- **WHEN** the user selects Exit from the main menu
- **THEN** the application requests a clean quit

### Requirement: Start opens rule setup before gameplay
Selecting Start SHALL open a rule-setup screen before loading the arena. The rule setup SHALL allow selecting points-to-win and marbles-per-round within their supported ranges.

#### Scenario: Start opens settings
- **WHEN** the user selects Start on the main menu
- **THEN** the rule-setup screen is shown before the arena loads

#### Scenario: Rule values can be adjusted
- **WHEN** the rule-setup screen is open
- **THEN** the user can change points-to-win and marbles-per-round within their valid ranges

#### Scenario: Back returns to main menu
- **WHEN** the user selects Back from rule setup
- **THEN** the main menu is shown without starting a match

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
