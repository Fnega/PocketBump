## ADDED Requirements

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
