## ADDED Requirements

### Requirement: Identifiable Windows release package
The demo SHALL ship as a non-Development Windows x64 player named PocketBump.exe with version 1.0, game identity 射球大战 / PocketBump, developer Fnega and a project-authored icon. It SHALL retain the default windowed experience and the existing display choice.

#### Scenario: New player launches the demo
- **WHEN** the packaged executable starts
- **THEN** the menu identifies version 1.0 and the free-demo/local-LAN scope, and the runtime reports a non-Development build

### Requirement: Non-overwriting complete delivery
The package SHALL include the full player, Chinese instructions, existing license notices and a SHA256 manifest. It SHALL be delivered to D:\uniry Assets\射球大战\PocketBump1.0 with a matching zip, and MUST reject existing output targets instead of overwriting them.

#### Scenario: Package is prepared
- **WHEN** a verified candidate is packaged into a previously absent target
- **THEN** executable and data identities match the tested candidate, notices and instructions are included, and debug-only DoNotShip folders are excluded

### Requirement: Honest final-build acceptance
Verification SHALL distinguish source tests, Development diagnostics, actual Release UI/transport evidence and physical two-computer evidence. A pending physical two-PC test MUST remain pending and be documented for the user.

#### Scenario: Only local automation is available
- **WHEN** source tests and same-machine Release checks pass
- **THEN** the evidence records their exact build identity and does not claim Wi-Fi or physical two-PC acceptance
