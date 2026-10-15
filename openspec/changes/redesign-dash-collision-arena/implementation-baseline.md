# Apply baseline — 2026-10-02

## Scope and current gate

This record captures the pre-redesign source, scenes, release identity and existing verification evidence for tasks 1.1–1.2. Reading a historical PASS is not a fresh test run and does not validate the redesigned game. Original evidence stays at its original timestamped path; nothing is overwritten or relabeled.

At initial capture, implementation was awaiting explicit G1/G2 answers. On 2026-10-02 the user replied “三个都推荐”, approving the map/Buff initial values, documented interaction choices, and provisional handfeel greybox trial. Tasks 1.3–1.4 may proceed; final handfeel approval and actual Unity art-sample approval remain separate later gates. Historical facts and evidence below describe the pre-implementation capture, not later source changes.

## Source, environment and scenes (task 1.1)

- Repository: `D:\Unity project\PhysicscollGame`.
- Branch: `main`; HEAD: `49d133caee606ac34a87373158297267ffcc52f0`.
- Initial `git status --short`: only `?? openspec/changes/redesign-dash-collision-arena/`; tracked source and scenes have no outstanding changes. Preserve this existing proposal directory.
- Editor: Unity `2022.3.62f3c1`, revision `1623fc0bbb97`, from `ProjectSettings/ProjectVersion.txt`.
- Relevant package manifest: URP `14.0.12`, Test Framework `1.1.33`, TextMeshPro `3.0.7`.
- Enabled build scenes, in order: `Assets/Scenes/MainMenu.unity`, `Assets/Scenes/Arena.unity`. Other sample/test scenes found on disk are not enabled release scenes and are not cleanup targets.
- Read-only process check found no `Unity` or `PhysicscollGame` process at capture time. This apply stage has not launched the editor, a test runner or a player.
- Current protocol: `NetplayProtocol.Version = 6` in `Assets/Scripts/PocketBump/LanUdpPeer.cs:20`. Settings identity currently mixes target score, ball count and protocol; new layout/profile/seed negotiation is future work, not already implemented.
- Input baseline: `PlayerInput` has `Move`, `BoostHeld`, `ShootPressed`; packed bit values 1/2/4/8 are movement, 16 is held Boost, 32 is Shoot. New Dash must not silently reinterpret old-version traffic as the new vocabulary.
- Current simulation: 60 Hz, two-tick input delay, twelve-tick rollback, five-second disconnect (`ArenaLayout`). Current arena is 32×48; side opening 6.45, goal width 3.4, player/ball radius 0.78/0.3. These are baseline facts, not newly chosen redesign values.
- Highest numbered release found under `D:\uniry Assets\射球大战`: `PocketBump0.9`. If unchanged at build time, the next unused integer-component version is `PocketBump0.10`. Recheck before building; do not overwrite any older folder or retained failed candidate.

### Source and release SHA-256 anchors

Paths below are repository-relative unless explicitly absolute. These hashes identify the files inspected at the start of apply; they do not prove future code matches an old binary.

| File | SHA-256 |
| --- | --- |
| `Assets/Scenes/MainMenu.unity` | `F887E8F402E86843E6EDF1761B2158836D332F74AD4956852C34D4E8FD3260F0` |
| `Assets/Scenes/Arena.unity` | `6DFAA836012FB4B830CC43AF29D80B12FCCEA57B3FD3F9B44A186CD566486A3C` |
| `Assets/Scripts/PocketBump/MatchSim.cs` | `2DCD5837B45CC4685359818FFBEF6FFD840B547C1A1F23A547D2B3A18D55FFC1` |
| `Assets/Scripts/PocketBump/MatchSimState.cs` | `F5B29576B4D5FCA019DAFACC2FBBEFCD74F2F5139D93A75AD35E586985913727` |
| `Assets/Scripts/PocketBump/PlayerInput.cs` | `1E8204241F95CE32F74695E04C9D6AAF4FD251DBAFBDD044E5B037300C4494AB` |
| `Assets/Scripts/PocketBump/LanUdpPeer.cs` | `12560E15F865F64FF8D27ADC6969B6A82DD4293509FD065DBC11CB7D594B3D7A` |
| `Tools/Run-LanDiagnostics.ps1` | `BD1FDF3C0636C591004DF8E4D9681E02DE3F980F0601CF5525EA450250E3BB34` |
| `D:\uniry Assets\射球大战\PocketBump0.9\PhysicscollGame.exe` | `FA01CCDBAA5F74C777609235B99BA8988285B2BF0754445E85BBA268B2E61EB7` |
| `D:\uniry Assets\射球大战\PocketBump0.9\PhysicscollGame_Data\Managed\PocketBump.Runtime.dll` | `1794ECC4E8E8536AADE948C85D3C5AF219ECA2F641EAE8D14BFE254396819A63` |

The current 0.9 runtime assembly hash matches the older lifecycle verification record. The native Unity launcher hash alone is not sufficient to identify a game-code revision. Preserve/distribute complete version folders.

## Existing change and specification dependencies

| Change | Observed state | Treatment in redesign |
| --- | --- | --- |
| `fix-lan-match-lifecycle` | 27/27 checked tasks; not archived | Preserve implemented v6 lifecycle protection. Its unsynced diagnostic delta adds `Full-match diagnostics prove actual gameplay completion`, requiring collection and actual shot release; the redesign explicitly removes/replaces this obsolete gameplay requirement. |
| `award-pocket-goals-to-opponent` | 9 checked / 1 unchecked | Preserve implemented opponent-of-goal-owner scoring. Do not mark its outstanding work complete; its retained fall-scoring/priority assumptions are superseded only by the approved redesign. |
| `catch-on-basket-contact` | Only `.openspec.yaml`; no proposal/design/tasks/specs | Empty historical scaffold, not an implemented or pending task list to execute. Do not populate or archive it automatically. |

Main specs lag effective code/unsynced deltas. Future synchronization requires separately authorized resolution of the old baseline first, then this redesign. In particular, the old lifecycle ADDED diagnostic requirement must exist before applying the redesign's corresponding REMOVED operation. Do not later sync old whole requirements over the redesigned rules, silently ignore missing baselines, or rewrite old evidence.

Retained network contracts include corrected terminal history and Finish/FinishAck agreement, current-epoch liveness, two-sided same-connection rematch, nonce/endpoint/seat/epoch validation, bounded initial/rematch input startup, and receive-queue draining before each fixed step and timeout evaluation. A heartbeat is not missing gameplay input. The existing twelve-tick input-hole limit and visible frozen-Arena/manual-menu error handling remain required, not promises of arbitrary Wi-Fi tolerance.

Concrete audit anchors: `MatchDirector.cs:293` pumps input before fixed advancement; `MatchDirector.cs:647` drains and reconciles before deadline decisions; `LanMatchLifecycle.cs:21` defines 0.25-second retry, 0.5-second heartbeat, five-second silence/final-input settlement and independent fifteen-second progress deadlines. `LanMatchLifecycle.cs:238` gates startup on actual local send/ack and continuous remote input. Existing input redundancy is eight samples per packet and future allowance is 32 ticks. The old main rollback spec still names version 5, whereas effective code is version 6; the old main scoring spec still names goal-owner scoring, whereas `MatchSim.cs:782` already awards the opponent. Current player falls at `MatchSim.cs:768` still score and take priority; removing that behavior is future redesign work.

## Retained historical test evidence (task 1.2)

No tests were run for this capture. The following existing XML files were parsed directly; their dates, counts and hashes are recorded so later results can be distinguished. Both suites belong to the old basket/Boost game.

| Original path | Result | Run interval (Asia/Shanghai, 2026-09-30) | SHA-256 |
| --- | --- | --- | --- |
| `Tools/LanLifecycleResults/20260930-161733/release09-EditMode.xml` | 218/218 passed, 0 failed, 0 skipped | 17:27:32–17:28:17 | `A06958E269DF9BBB28B3079D76896BE6655235FBBD36FCAADC447ADF91DE79BE` |
| `Tools/LanLifecycleResults/20260930-161733/release09-PlayMode.xml` | 15/15 passed, 0 failed, 0 skipped | 17:28:39–17:29:17 | `364825834490F22C378BF30582E6A3D59A721558A6E14E20CE67E85DAA3E2760` |

The retained `Tools/LanLifecycleResults/20260930-161733/build-pocketbump0.9.log` reports a successful Windows x64 Development build with zero errors/warnings and process exit 0. Environment is Unity 2022.3.62f3c1. Read the original `openspec/changes/fix-lan-match-lifecycle/verification.md` for the older red/green runs and separately versioned 0.7 failures / 0.8 extended fault matrix; do not relabel them as 0.9 or redesign tests.

All six original 0.9 result directories below contain `host.txt`, `join.txt`, and `launcher-summary.txt`; the audit read both peer results and launcher outcome. Every launcher identifies `D:\uniry Assets\射球大战\PocketBump0.9\PhysicscollGame.exe`, both final reports received and exit status 0.

| Directory under `Tools/LanDiagnosticResults` | Scenario | Historical result |
| --- | --- | --- |
| `20260930-173121-733-534b5d` | Lifecycle, deliberate `peer-silence` | Both PASS only because the expected `PEER TIMEOUT` occurred. Seeded fixture, not natural play; 229/228 actual suppressed datagrams, approximately six-second result holds, not eight-second hold proof. |
| `20260930-173139-231-fa42e4` | Default smoke | Both PASS; moving-player connectivity evidence, not full-match proof. |
| `20260930-173203-366-65e2e0` | Smoke with first handshake/control loss | Both PASS; drop-first-control enabled. |
| `20260930-173228-664-08b0c0` | Full match, `drop-startup-ack` | Both PASS; completed epoch 0 at tick 3318 / checksum -218701961 / 8:0, then epoch 1 Live tick 301. |
| `20260930-173347-450-33871f` | Three rematches, `drop-start`, host leave/fresh join | Both PASS; first epoch seeded, later finishes ordinary old-game input; all completed epochs held results at least eight seconds. |
| `20260930-173625-673-2f8496` | Three rematches, `stale-epoch`, joiner leave/fresh join | Both PASS; latest retained 0.9 run ends 2026-09-30 17:39:02 +08:00, with four stale datagrams injected per peer. |

The full-match report explicitly says `scoring_strategy=cooperative_side_pocket_dunks`, `smoke_input_pattern=collect_shoot_impact_cooperative_dunk`. Its completed epoch includes eight collections, one real shot, one dangerous collision and eight scores; result hold is 8.002 seconds on each peer. `normal_gameplay` means the old driver used ordinary packed input, **not** that it achieved eight ball goals. The new rules require an entirely new body-to-ball goal driver and confirmed evidence, not renamed fields.

No fresh redesign regression, new packaged build, actual Unity art sample or real two-computer LAN result exists at this checkpoint. Loopback cannot establish router/Wi-Fi reliability or cross-machine determinism.

## Test and diagnostic migration map

These are pre-change line anchors, not instructions to delete whole files. Preserve valid math, replay, transport, UI and evidence-integrity contracts while replacing obsolete action/scoring assertions.

| Existing source anchor | Retired dependency | Required replacement coverage |
| --- | --- | --- |
| `Assets/Tests/EditMode/PocketBumpCoreTests.cs:108`, `:132`, `:163`, `:202` | Held Boost/turning, four skill bindings, possession/charge state | 18-tick locked Dash, cooldown/fresh-edge behavior, binding migration and free-ball contact |
| `Assets/Tests/EditMode/PocketBumpSimTests.cs:50`, `:67`, `:88`, `:105`, `:126`, `:148`, `:171`, `:214`, `:258` | Basket collection/stealing and replay latches | Single-contact shot/knockback episodes with complete snapshot/restore/checksum tests |
| `Assets/Tests/EditMode/PocketBumpSimTests.cs:301`, `:355` | Charged-shot recoil; player fall before ball goal | Normal/Dash impact and stronger reciprocal knockback; no-point fall must not suppress a legitimate goal |
| `Assets/Tests/EditMode/PocketBumpShotConsistencyTests.cs:13` | Staged Shoot input and basket-rim contact | Body-contact shots; retain meaningful drag, speed-range, wall/goal crossing assertions |
| `Assets/Tests/EditMode/PocketBumpPhysicsReplayTests.cs:22`, `:128` | Old shot/Boost input tape | Both-seat Dash/obstacle/Buff tapes, complete-state equality and several window-bounded corrections |
| `Assets/Tests/PlayMode/PocketBumpFlowTests.cs:138`, `:195`, `:240` | Scene basket/possession/fall scoring | Free-ball body impacts, independent respawn and layered goal reset; keep valid opponent-goal scoring tests at `:268`, `:289` |
| `Assets/Tests/EditMode/PocketBumpNetworkTests.cs:120`, `:391`; `PocketBumpTerminalHistoryTests.cs:27` | Old predicted action vocabulary | Movement prediction with cleared Dash edges, retaining input-hole and terminal-history contracts |
| `Assets/Scripts/PocketBump/LanFullMatchDriver.cs:28`, `:56`, `:74` | Collect/fire then deliberately dunk player two to finish | Ordinary zero-score body-ball goal driver; no score injection or removed-action fallback |
| `Assets/Scripts/PocketBump/LanSmokeRunner.cs:101`, `:132`, `:280`, `:594`, `:604` | Smoke Boost/Shoot, fall-scoring fixture, collection/shot evidence and strategy labels | Movement/Dash smoke; legal explicitly named mechanic fixtures; real new confirmed milestones |
| `Assets/Scripts/PocketBump/LanDiagnostics.cs:224`; `Tools/Run-LanDiagnostics.ps1:153` | Required collection/shot counters | New event schema and actual effect/goal evidence, while retaining liveness/send errors/phase/epoch provenance |
| `Assets/Tests/EditMode/PocketBumpFullMatchDriverTests.cs:18`, `:93` | Old driver at 0/4/8-tick delay and old ledger events | New collision-goal tape and true confirmed events, retaining deduplication, provenance and completed-epoch retention |

`PocketBumpCollisionConsistencyTests`, queued-real-input tests, lifecycle fault transport, result navigation and frozen-error behavior remain regression protections. Replacing gameplay does not authorize weakening these checks. Tests after implementation must carry new output paths and be reported separately from every historical row above.

## Implemented retirement-to-replacement map — 2026-10-03

The formal `Arena` now owns one `DashMatchDirector` and the `DashArenaState` / `DashArenaSimulation` vocabulary. The previous generated v6 hierarchy is preserved inactive for recovery, not run alongside the new world. The v6 implementation and isolated legacy tests remain available as historical regression fixtures; their successful execution does **not** establish v7 gameplay correctness. In particular, a test that still instantiates `MatchSim`, `PlayerInput`, or the old `MatchDirector` is not relabeled as a Dash acceptance test.

The following concrete replacement cases exist and passed in `Tools/DashArenaIntegrationResults/20261002-formal-integration/editmode-1.xml` (471/471) or `playmode-1.xml` (62/62). Those totals include legacy coverage and must not be quoted as counts of exclusively new gameplay tests. Mechanism tests deliberately setting initial positions, counters or terminal state remain marked fixtures, not natural full-match evidence.

| Retired group from the baseline map | Landed replacement assertions | Retained boundary |
| --- | --- | --- |
| Held Boost, wind-up/steering, four skill actions and possession state in `PocketBumpCoreTests` | `PocketBumpDashSimulationTests.DashHasEighteenTicksAndOneHundredEightyTickCooldownWithLockedDirection`; `PocketBumpDashTickInputSamplerTests.HeldDashDoesNotRepeatAcrossDashAndCooldownLengths`, `SamplingConsumesAnUnavailablePressRatherThanSavingItForRecovery`; `PocketBumpDashControlBindingsTests.ValidLegacyBoostPairMigratesWithoutReadingOrReservingShoot`; `PocketBumpDashMenuTests.RulesExposeOnlyTwoDashBindingsAndExplainScoringAndLocalView` | Old core cases remain old-action regression only. Production menus/input expose movement and two Dash presses. |
| Catch, basket carry/weld, steal and possession replay latches in `PocketBumpSimTests` | `PocketBumpFlowTests.EitherPlayerBodyStrikesAFreeBallWithoutCollectingOrBasketGeometry`; `PocketBumpDashSimulationTests.ContactLatchSuppressesRepeatedGameplayImpulse`; `DashMechanicsAcceptanceTests.RepeatedSixPassContactEpisodeGrantsOnlyOneGameplayImpulse`; `PocketBumpDashStateTests.EveryAuthoritativeScalarAndArrayElementAffectsChecksumAndIsDeepCopied` | Basket geometry is not active in the formal scene. No new possession, catch, steal or charge event exists in the Dash simulation. |
| Charged-shot recoil and fall-scoring priority | `PocketBumpDashSimulationTests.PlayerKnockbackIsSymmetricAndNotRemovedByNextMovementTick`; `PocketBumpDashSimulationEdgeTests.TwoGoalsAndFallInOneTickSelectNorthGoalWithoutPickupOrFallPoint`; `DashMechanicsAcceptanceTests.BothPlayersCanFallTogetherWhileBallContinuesAndRespawnAfter180Ticks` | A fall gives no point and does not replace a valid ball goal. Fixture placement is explicit. |
| Staged Shoot and basket-rim assumptions in `PocketBumpShotConsistencyTests` | `PocketBumpFlowTests.SingleDashCollisionScoresWithoutChargeOrIndependentShootAction`, `IsolatedFreeBallCoastsToRestWithoutSpeedingUp`; `PocketBumpDashSimulationTests.BodyContactShootsFreeBallAndGreenOnlyAddsOutgoingImpulse`, `SweptBallsCannotSwapSidesOrTunnelThroughThinWall` | Old charged-shot tests are not new-ball evidence. Meaningful drag and swept collision/goal protections have new-body equivalents. |
| Old Boost/Shoot replay tape in `PocketBumpPhysicsReplayTests` | `DashRollbackSessionTests.LongReorderedTapeMatchesEveryStateFieldAfterRepeatedBoundedCorrections`, `DelayedDashReplaysEightBallStateTeleportRandomPickupAndWallRepairEvents`; `PocketBumpDashSimulationEdgeTests.LongTapeUsesSeparateTenTickCorrectionsAroundTeleportAndSixHundredTickRepair` | The new tape uses movement/Dash, eight free balls and complete world/event state, with each correction inside the existing rollback window. |
| Old scene possession/fall-score assertions in `PocketBumpFlowTests` | The formal-scene suite was migrated in place: `DangerousBallImpactsCanCausePersonalFallWithoutAwardingPoints`, `EitherPlayerBodyStrikesAFreeBallWithoutCollectingOrBasketGeometry`, `SingleDashCollisionScoresWithoutChargeOrIndependentShootAction`, both `FreeBallCrossingNorthGoalScoresForPlayerTwo` and `FreeBallCrossingSouthGoalScoresForPlayerOne` | Navigation remains covered by `MenuRulesArenaAndModifyRulesFlowTransfersSettings` and `ResultRematchAndMainMenuActionsPreserveAndLeaveMatch`. Formal scene lookup explicitly requires `DashMatchDirector`. |
| Old predicted action bits and terminal-history cases | `DashNetcodeTests.InputRoundTripsOnlyNewVocabularyAndRejectsV6`; `DashRollbackSessionTests.ReorderedInputDoesNotRegressPredictionOrRepeatDash`, `StoredLocalSampleIsNeverResampledAndHistoryIsNewestFirst`, `TerminalReconcileValidatesFinalChecksumWithoutExecutingAnotherTick`, all three `Synthetic*Finish` correction cases | Legacy packets are rejected before playable initialization; synthetic terminal tests are labeled algorithm fixtures. |
| Core circle mathematics and zero-distance stability | Retain `PocketBumpCollisionConsistencyTests.ApproachingConfiguredPairsConserveMomentumAndTangentsAndUseRestitution`, `SeparatingOverlapOnlyCorrectsPositions`, `DegenerateStationaryContactsRemainBoundedMassWeightedAndRemoveOverlap`, `CoincidentMovingContactsAreFiniteBoundedAndExactlyRepeatable`; add new-world contact, swept-wall and reciprocal-knockback assertions above | Shared solver math remains applicable. That file's old `RealStepRoutesAllCirclePairsAndDoesNotReboundSeparatingContacts` is specifically a legacy-step integration check, not evidence that v7 invokes every route. |
| Frozen network error and manual menu behavior | `PocketBumpNetworkPauseTests.TerminalNetworkReasonsFreezeInPlaceUntilManualMenuAction` now loads the formal Dash scene and injects explicitly marked v7 presentation failures; `DashLanSessionTests.HeartbeatCannotFillInputHoleAndFailureFreezesEveryMechanicClock` and `DashMatchDirectorTests.NetworkDirectorsUseOnlyAssignedSeatsAndShareTheRealFourHundredTickWorld` cover real-session progression/freeze | Injecting a classified failure verifies presentation, not the natural origin of that failure. Real socket classification, fault transport and deadline cases are distinct evidence. |

The `LanFullMatchDriver` / `LanSmokeRunner` / `LanDiagnostics` / launcher and old `PocketBumpFullMatchDriverTests` rows are **not** completed by this retirement map. Their v7 migration and natural zero-score, real-ball-goal proof belong to tasks 11–12 and require new evidence; the historical collect/fire/player-dunk strategy is not accepted. The 2026-10-03 supplemental formal presentation tests (camera corners/death height, bounded effect burst, epoch cleanup, assigned LAN HUD, nonmodal real input) also require their own subsequent Unity run and are not included in the earlier 471/62 results above.
