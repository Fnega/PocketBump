## ADDED Requirements

### Requirement: Production local diagnostics
The Release player SHALL retain local session reports containing version/build/rules identity, role, bound local and remote endpoints, tick/confirmed progress, lifecycle and send/receive/error counts. It SHALL capture terminal and initialization failures and normal leave, with bounded periodic writes during sessions.

#### Scenario: A connection freezes
- **WHEN** a LAN session encounters a terminal network reason
- **THEN** the existing frozen-screen behavior remains and a durable report records that reason and available transport evidence

#### Scenario: Port binding fails before connection
- **WHEN** the host cannot bind its socket
- **THEN** a report includes the attempted endpoint and initialization error even without an established session

### Requirement: Reporting cannot break gameplay
Report I/O SHALL be isolated from simulation and networking, handle unavailable storage without throwing into the match loop, and stay local unless the player explicitly shares files. Development fault injection and authority-changing fixtures SHALL remain excluded from Release.

#### Scenario: Storage is unavailable
- **WHEN** writing a report fails
- **THEN** the game keeps its existing match/failure state and exposes a useful reporting error without repeatedly crashing

### Requirement: Accessible report folder
The demo SHALL provide an action to open the report directory from its UI and explain in the bundled instructions how to return both peers' reports.

#### Scenario: Player sends a bug report
- **WHEN** the player uses the report-folder action
- **THEN** the local folder is opened or a visible error explains why it could not be opened, and no automatic upload occurs
