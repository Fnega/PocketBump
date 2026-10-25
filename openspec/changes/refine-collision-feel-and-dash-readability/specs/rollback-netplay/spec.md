## ADDED Requirements

### Requirement: Handfeel content and checksum schemas are agreed before play

The handfeel policy and decay parameters SHALL participate in the immutable match content identity while unchanged geometry retains its layout identity. V1, handfeel candidate and subsequently approved production snapshots SHALL be distinguishable wherever their simulation or provisional identity differs. The existing full-content-hash handshake SHALL reject mismatched rules before gameplay. The existing packet format and protocol 8 SHALL remain sufficient for this change by versioning the handfeel-specific checksum encoding together with the content identity; the system SHALL NOT silently use different checksum schemas under the same identity. Legacy V1 behavior and checksum encoding SHALL remain paired, with new recoil-stage state inactive and zero.

#### Scenario: Old and new handfeel peers are rejected before tick zero
- **WHEN** one peer requests V1 and the other uses the new handfeel candidate or approved new production profile
- **THEN** the existing content-identity check rejects the mismatch before gameplay rather than discovering it through an in-match DESYNC

#### Scenario: Equal geometry does not imply equal simulation rules
- **WHEN** two profiles use the same arena geometry but different ordinary-assistance or recoil settings
- **THEN** their layout hashes remain equal while their content identities differ

#### Scenario: A decay parameter cannot change under an old hash
- **WHEN** an authoring copy changes the initial-stage duration, initial decay, tail multiplier or handfeel policy version
- **THEN** freezing that new profile produces a different relevant content identity without changing the released V1 golden identity

### Requirement: Handfeel recoil stage is fully rollback authoritative

Every new value capable of influencing later handfeel simulation, including the remaining initial-recoil stage, SHALL be copied, restored, compared and incorporated into the appropriate versioned checksum along with existing velocities and contact latches. Late-input replay SHALL use pre-contact approach sampling and the same collision order, reset rules and tick-stage boundaries as on-time execution. Local and LAN gameplay SHALL share these rules, and marker rendering SHALL NOT mutate them or require extra gameplay packets.

#### Scenario: A stage-only difference is detectable
- **WHEN** two otherwise equal candidate snapshots differ only in their remaining recoil initial-stage counter
- **THEN** their candidate checksums differ and copying/restoring either snapshot preserves that exact counter

#### Scenario: Rollback crosses both decay stages and another hit
- **WHEN** delayed or reordered input causes replay across a gentle ball touch, a dynamic hit, the sixth/seventh decay boundary and a genuine second contact
- **THEN** the resulting full candidate state matches an on-time reference, including velocities, stage counters, contact latches and existing scores/buffs/world state

#### Scenario: Presentation remains read only
- **WHEN** local or LAN marker refreshes run repeatedly, including same-tick corrections
- **THEN** no authoritative state field or checksum changes solely because the markers were rendered

### Requirement: Handfeel tuning preserves existing network pacing and fault behavior

The change SHALL preserve 60 Hz simulation, the existing two-tick input delay, twelve-tick rollback window, packet recovery, timeouts, input-pacing coordination, lifecycle handling and terminal fault UI. Recoil and readiness timers SHALL not consume wall-clock time while simulation waits or pauses. Recovery SHALL neither duplicate contact assistance nor synthesize, queue or repeat Dash edges. No global timeScale pause, additional network stall, checksum relaxation or window expansion SHALL be introduced for handfeel.

#### Scenario: A recoverable wait freezes the new state without a popup
- **WHEN** a real two-peer session enters existing recoverable input pacing during the initial recoil stage and an active cooldown
- **THEN** those simulation values and corresponding marker progress stay fixed until simulation resumes, normal transport continues, and no recoverable-wait panel is restored

#### Scenario: Existing terminal failures remain actionable
- **WHEN** a genuine existing timeout, input hole or confirmed desynchronization occurs with the new profile
- **THEN** the established failure handling and visible terminal reason remain in effect rather than being hidden by the new indicators or tuning
