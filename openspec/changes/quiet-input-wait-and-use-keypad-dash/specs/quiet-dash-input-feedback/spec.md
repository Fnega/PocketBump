## ADDED Requirements

### Requirement: Quiet recoverable input waits
The Dash HUD SHALL keep the bottom-right network panel hidden during recoverable input pacing waits. It SHALL preserve existing waiting, transport, input buffering, automatic recovery and diagnostic behavior.

#### Scenario: Remote input is briefly behind
- **WHEN** a healthy session enters WaitingForRemoteInput
- **THEN** the network panel remains hidden, simulation respects the existing input lead bound and transport continues

#### Scenario: Input arrives after a wait
- **WHEN** valid queued input permits progression again
- **THEN** play resumes automatically and a Dash edge captured during the wait is consumed once without requiring a panel action

### Requirement: Terminal network faults remain visible
The existing lower-right terminal fault panel SHALL remain visible for disconnection, input-progress timeout, desynchronization and peer-reported faults, with existing reason, frozen-screen, report access and return-menu behavior.

#### Scenario: A silent wait becomes a terminal fault
- **WHEN** the existing input-progress deadline expires
- **THEN** the terminal fault panel appears and remains visible; hiding transient waits does not extend or disable the timeout

### Requirement: Numeric keypad default for local player two
Local P2 SHALL default to Keypad2, labeled Num2, while local P1 and each LAN player's local mapping remain unchanged. Valid persisted Dash or legacy Boost bindings SHALL remain authoritative; loading SHALL not rewrite preferences.

#### Scenario: Fresh default controls
- **WHEN** no applicable saved binding exists or defaults are explicitly restored
- **THEN** P2 uses Keypad2, the current-controls UI shows Num2, and number-row Alpha2 does not trigger the default P2 Dash

#### Scenario: Existing number-row preference
- **WHEN** a valid saved P2 binding is Alpha2
- **THEN** it remains Alpha2 and is displayed as 2 rather than being silently reset to Num2

#### Scenario: LAN local input
- **WHEN** either seat is controlled on its own computer in LAN mode
- **THEN** the local P1 binding is used and the changed local P2 default does not alter LAN input mapping

### Requirement: Non-overwriting verified version 1.2 delivery
The approved new PocketBump1.2 directory and ZIP SHALL contain the exact accepted non-Development candidate, notices, current instructions and hashes. Previous releases SHALL remain unchanged.

#### Scenario: Package confirmed changes
- **WHEN** source regression and authorized visible ordinary-UI checks pass for an isolated 1.2 candidate
- **THEN** that candidate is packaged to the new destination with file and ZIP verification, without claiming physical two-computer campus validation
