## 1. Transport and terminal cause

- [x] 1.1 Add receive polling/error diagnostics and session timing/rejection observations without changing timeout or simulation behavior; cover idle/error/recovery cases.
- [x] 1.2 Add protocol-8 reason-coded peer fault notification, first-cause retention and voluntary-leave separation; test invalid packets, timeout propagation and old/new rejection.

## 2. Production evidence and feedback

- [x] 2.1 Add a bounded sampled pre-failure timeline, terminal freeze and additive production report fields with serialization/retention/I/O-isolation tests.
- [x] 2.2 Update the existing fault HUD to distinguish peer faults and local timeouts, preserving frozen-screen/report-folder behavior; add presentation regressions.

## 3. Verification and versioned delivery

- [x] 3.1 Update approved 1.1 identity/build/package tooling and player guidance while preserving 1.0 package support and existing shipped files.
- [x] 3.2 Run EditMode/PlayMode regression and separately identified Development network checks, including directional silence, transient recovery and normal leave/rematch.
- [x] 3.3 Build and exercise the exact non-Development 1.1 candidate using authorized ordinary UI, inspect reports/screenshots and retain exact build hashes.
- [x] 3.4 Deliver verified PocketBump1.1 folder/ZIP with notices/docs/hashes and explain remaining campus-network uncertainty; preserve 1.0 and supplied reports.
- [ ] 3.5 User and friend retest the same complete 1.1 package on physical campus-connected computers and return both timelines before claiming the original interruption resolved. (External user-owned gate.)
