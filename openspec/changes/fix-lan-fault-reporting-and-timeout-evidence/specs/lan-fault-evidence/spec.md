## ADDED Requirements

### Requirement: Explicit peer failure attribution
Dash protocol 8 SHALL distinguish a voluntary departure from a terminal peer-reported fault using a bounded validated reason code. The receiver SHALL validate sender endpoint and current session identity, retain the first terminal cause and identify peer-originated faults without presenting them as voluntary departure.

#### Scenario: Joiner times out
- **WHEN** the joiner has no accepted current-session traffic for the existing five-second deadline and its fault notification reaches the host
- **THEN** the joiner records its local timeout and the host records a peer-originated timeout rather than PEER LEFT; both retain the existing frozen-screen behavior

#### Scenario: Player leaves normally
- **WHEN** a healthy player explicitly returns to the menu
- **THEN** the other player still sees PEER LEFT, with no invented fault reason

#### Scenario: Invalid or stale fault packet
- **WHEN** a fault packet has an unknown reason, malformed length, foreign endpoint, wrong seat, wrong match or stale epoch
- **THEN** it neither terminates the match nor renews current-session liveness

#### Scenario: Mixed player protocols
- **WHEN** a protocol-7 player attempts to connect to a protocol-8 player
- **THEN** the incompatible connection is rejected visibly and cannot enter live play

### Requirement: Observable nonthrowing receive path
The transport SHALL retain receive poll/error counters and the last receive error separately from send errors. Expected idle/nonblocking polls SHALL not count as failures. Session diagnostics SHALL expose raw and accepted-message ages, pump timing and rejection categories without altering liveness, deterministic state or timeout thresholds.

#### Scenario: Receive operation fails
- **WHEN** the receive path encounters a SocketException other than an expected nonblocking-empty condition
- **THEN** its counter and error code appear in diagnostic evidence without throwing through gameplay or being mislabeled as a send failure

#### Scenario: Socket becomes healthy again
- **WHEN** a subsequent valid packet arrives before the existing timeout deadline
- **THEN** normal progression can resume while the earlier receive failure remains visible as historical evidence

### Requirement: Bounded frozen pre-failure timeline
Production reporting SHALL retain a fixed-capacity timeline spanning approximately the most recent twenty seconds at bounded sampling cadence, with counters, input progression, receive/accepted ages and main-loop timing. It SHALL force and freeze a terminal sample on the first failure and preserve that evidence across later fault-screen updates and leave operations.

#### Scenario: Player stays on a failure screen
- **WHEN** a terminal network fault occurs and the player leaves the screen open longer than the timeline duration
- **THEN** the retained failure timeline still includes the pre-failure observations rather than only repeated frozen-state samples

#### Scenario: Reports cannot be written
- **WHEN** report storage is unavailable
- **THEN** reporting remains bounded and nonthrowing, gameplay rules are unchanged, and a later successful write can preserve the retained terminal timeline

#### Scenario: Legacy reports remain present
- **WHEN** the new player creates diagnostic evidence
- **THEN** it does not rewrite or delete previous players' or 1.0 report files and the new fields clearly identify their evidence format

### Requirement: Verified non-overwriting 1.1 handoff
The repair SHALL deliver a non-Development version 1.1 under the approved new PocketBump1.1 directory and matching ZIP, preserving 1.0. Source tests, companion diagnostics and actual Release acceptance SHALL be distinguished, and the campus two-PC gate SHALL remain unpassed until new paired evidence is reviewed.

#### Scenario: Package the repair
- **WHEN** the isolated 1.1 candidate passes automated and authorized ordinary-UI checks
- **THEN** its exact verified bytes, notices, instructions and hashes are packaged without overwriting prior targets or claiming the physical network interruption is proven fixed
