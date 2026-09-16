## ADDED Requirements

### Requirement: Home menu separates actions into three regions
The home menu SHALL display Start, Server, and Exit as three independently titled and visually bounded UI regions on the same screen. Each region SHALL have its own UI container. Start SHALL contain the local Start action, Server SHALL contain Host LAN and Join LAN, and Exit SHALL contain the Exit action. All three regions SHALL be visible together when the home menu is active, without introducing a new server-selection page or exit-confirmation flow. Buttons, headings, and shared error feedback SHALL remain readable and non-overlapping at the Canvas reference resolution and at 16:9 and 4:3 aspect ratios.

#### Scenario: Home displays three groups
- **WHEN** the home menu is shown
- **THEN** the Start, Server, and Exit containers are visible together AND each action belongs to its intended container

#### Scenario: Returning restores all regions
- **WHEN** the user returns from local rule setup, host rule setup, or join-address entry
- **THEN** the complete three-region home menu is restored without starting a match

#### Scenario: Generated and authored menus agree
- **WHEN** the menu is created from an authored scene, runtime fallback, or repaired legacy flat hierarchy
- **THEN** the same three-region grouping is present AND repeated setup does not create duplicate regions or action buttons

#### Scenario: Layout fits supported aspect ratios
- **WHEN** the home menu is viewed at the reference resolution or a 16:9 or 4:3 aspect ratio
- **THEN** every region and action is visible AND headings, buttons, and shared error feedback do not overlap or clip

## MODIFIED Requirements

### Requirement: Main menu offers LAN host and join
The main menu SHALL offer Host LAN and Join LAN together in its Server region, separately from the local Start and Exit regions on the same home screen. Host LAN SHALL listen for a joiner after rule setup (or with the current rule values). Join LAN SHALL accept a host address and SHALL NOT load the arena until the handshake succeeds.

#### Scenario: Host is reachable from the menu
- **WHEN** the user selects Host LAN from the Server region and confirms rules
- **THEN** the application listens for a LAN joiner AND does not run a local two-keyboard match as the networked session

#### Scenario: Join needs an address
- **WHEN** the user selects Join LAN from the Server region
- **THEN** the user can enter a host address AND a failed handshake does not leave a live arena match running

#### Scenario: Local start still exists
- **WHEN** the user selects Start from the Start region and completes rule setup as a local match
- **THEN** the arena loads with both seats on that machine AND no LAN handshake is required
