## ADDED Requirements

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

## MODIFIED Requirements

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
