## MODIFIED Requirements

### Requirement: Match result provides navigation choices
After a local match ends, a result screen SHALL identify the winner and SHALL offer Rematch, Modify Rules, and Main Menu actions. A LAN result SHALL identify a final winner and enable Rematch only after corrected terminal agreement with the peer; before agreement it SHALL show a result-confirmation state. LAN Rematch SHALL request a same-rules bilateral rematch rather than reload the local scene. LAN results SHALL offer Main Menu and explain that rule changes require leaving the connection and creating a new Host LAN room from the main menu; they SHALL NOT expose an actionable in-session Modify Rules button or silently switch the participant to local play. Authored, generated, and repaired result UI SHALL follow the same mode-aware action policy.

#### Scenario: Rematch preserves rules
- **WHEN** the user selects Rematch after a local match
- **THEN** a new local match starts with the same points-to-win and marble-count settings

#### Scenario: Modify Rules returns to setup
- **WHEN** the user selects Modify Rules after a local match
- **THEN** the rule-setup screen opens with the previous values available for editing

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
- **THEN** the UI shows a starting/waiting state and does not misrepresent it as a new confirmed result or a network failure

#### Scenario: LAN players want different rules
- **WHEN** LAN results are shown and a player wants to change points-to-win or marble count
- **THEN** the UI instructs the player to use Main Menu, have the host create a new Host LAN room with the desired rules, and join that new room; the current connection's rules remain unchanged

#### Scenario: Authored and fallback result UI agree
- **WHEN** the Arena uses an authored result panel, a generated fallback, or repaired legacy buttons
- **THEN** local actions retain their existing behavior and LAN buttons use the same confirmation, bilateral rematch, and leave-to-change-rules policy without duplicate listeners
