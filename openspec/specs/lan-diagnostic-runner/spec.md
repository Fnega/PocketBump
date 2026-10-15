# LAN Diagnostic Runner

## Purpose

Defines development-player LAN reports and the Windows loopback launcher that preserves evidence from both peers.

## Requirements

### Requirement: Development builds produce peer diagnostic reports
When launched in LAN smoke mode, a development build SHALL write a terminal report for its host or join role.  The report SHALL state pass/fail, terminal reason, simulation tick, last accepted remote tick, last acknowledgement, packet receive/rejection counters, and a bounded recent-event trail.

#### Scenario: Match starts successfully
- **WHEN** host and join smoke processes complete a local LAN handshake and advance past the smoke threshold
- **THEN** each process writes a passing report containing its role and network snapshot

#### Scenario: Session aborts
- **WHEN** a smoke process observes a session error or its smoke deadline expires
- **THEN** it writes a failing report before exiting AND the report contains the terminal reason and available network snapshot

#### Scenario: No handshake packet arrives
- **WHEN** a host or join smoke peer reaches its handshake deadline without receiving a datagram
- **THEN** its report distinguishes send attempts, send failures, local endpoint, remote endpoint, and the last socket error from receive counters

### Requirement: Local pair launcher preserves test evidence
The repository SHALL provide a Windows development helper that launches host and join standalone players on loopback, waits for their reports, and retains a timestamped result directory containing both reports and launcher status.

#### Scenario: User runs a local diagnostic pair
- **WHEN** a developer supplies a Development build executable to the helper
- **THEN** the helper starts host before join AND prints the created results directory after both roles finish or the helper deadline expires
