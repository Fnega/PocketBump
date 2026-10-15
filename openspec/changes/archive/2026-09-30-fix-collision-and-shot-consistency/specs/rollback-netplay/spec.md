## ADDED Requirements

### Requirement: Corrected physics sessions reject pre-fix simulation versions

The corrected collision and shot rules SHALL advertise protocol/rules compatibility version 5 rather than pre-fix version 4 using the existing handshake and settings-hash mechanism. A version-4 peer and a version-5 peer SHALL NOT begin a playable match together regardless of which side hosts. The corrected implementation SHALL reject version incompatibility through the existing compatibility-failure path before gameplay, without waiting for a state-checksum mismatch; the existing `SETTINGS MISMATCH` reason SHALL remain acceptable and no new wording is required from archived builds. Same-version peers SHALL continue using the existing packet layouts, seat negotiation, input delay, and reliable handshake behavior.

#### Scenario: Corrected host rejects pre-fix joiner

- **WHEN** a version-4 joiner requests a match from a version-5 host
- **THEN** the corrected host rejects the incompatible request through its existing compatibility-failure path and neither peer starts a playable match for that request

#### Scenario: Corrected joiner rejects pre-fix host

- **WHEN** a version-5 joiner encounters a version-4 host or a pre-fix version-4 join acknowledgement
- **THEN** the handshake does not complete as a playable match and the corrected joiner reports the existing compatibility failure before gameplay ticks begin

#### Scenario: Corrected peers use the same physics rules

- **WHEN** two version-5 peers with matching match settings complete the existing handshake
- **THEN** both enter the corrected simulation with the agreed seats and input delay, and corresponding confirmed states use the corrected collision and full-range shot behavior
