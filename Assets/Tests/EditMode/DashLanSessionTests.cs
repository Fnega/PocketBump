using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    // Real local UDP sockets plus an injected protocol clock. Any state setup below is
    // explicitly a mechanism fixture, not evidence of a natural full match or two PCs.
    public sealed class DashLanSessionTests
    {
        [Test]
        public void NormalSocketInitializationAndFourHundredTicksAgreeAndCommitEventsOnce()
        {
            using (var pair = new Pair())
            {
                var hostEvents = new List<DashArenaEvent>(); var joinEvents = new List<DashArenaEvent>();
                int hostEpochs = 0, joinEpochs = 0;
                pair.Host.EpochInitialized += _ => hostEpochs++;
                pair.Join.EpochInitialized += _ => joinEpochs++;
                pair.Host.ConfirmedEvents += batch => hostEvents.AddRange(batch);
                pair.Join.ConfirmedEvents += batch => joinEvents.AddRange(batch);
                pair.DriveTo(400, (epoch, tick) => tick == 200 ? Input(Vector2.right, true) : default,
                    (epoch, tick) => tick == 200 ? Input(Vector2.left, true) : default);
                Assert.That(pair.Host.InitializationCount, Is.EqualTo(1));
                Assert.That(pair.Join.InitializationCount, Is.EqualTo(1));
                Assert.That(hostEpochs, Is.EqualTo(1)); Assert.That(joinEpochs, Is.EqualTo(1));
                Assert.That(pair.Host.MatchId, Is.EqualTo(pair.Join.MatchId));
                Assert.That(pair.Host.Seed, Is.EqualTo(123456u)); Assert.That(pair.Join.Seed, Is.EqualTo(123456u));
                Assert.That(pair.Host.LocalSeat, Is.Zero); Assert.That(pair.Join.LocalSeat, Is.EqualTo(1));
                Assert.That(pair.Host.State.Checksum(), Is.EqualTo(pair.Join.State.Checksum()));
                Assert.That(pair.Host.Rollback.ValidatedChecksumCount, Is.GreaterThan(0));
                Assert.That(pair.Join.Rollback.ValidatedChecksumCount, Is.GreaterThan(0));
                Assert.That(pair.Host.Lifecycle.Phase, Is.EqualTo(LanLifecyclePhase.Playing));
                Assert.That(pair.Join.Lifecycle.Phase, Is.EqualTo(LanLifecyclePhase.Playing));
                CollectionAssert.AreEqual(hostEvents, joinEvents);
                Assert.That(hostEvents.Count(e => e.Kind == DashArenaEventKind.GateOpened), Is.EqualTo(1));
                Assert.That(hostEvents.Count(e => e.Kind == DashArenaEventKind.DashStarted), Is.EqualTo(2));
                Assert.That(hostEvents.Select(e => e.Identity).Distinct().Count(), Is.EqualTo(hostEvents.Count));
                int count = hostEvents.Count; pair.Drain();
                Assert.That(hostEvents.Count, Is.EqualTo(count));
                Assert.That(pair.Host.RetainedResults, Is.Empty, "A 400-tick transport run is not a completed match.");
            }
        }

        [Test]
        public void NoncanonicalSamplesAreCapturedOnceAndStoredExactlyAsTheirWireBits()
        {
            using (var pair = new Pair())
            {
                int hostSamples = 0, joinSamples = 0;
                pair.DriveTo(400,
                    (epoch, tick) => { hostSamples++; return Input(new Vector2(.3f, 0)); },
                    (epoch, tick) => { joinSamples++; return Input(new Vector2(1, 1)); });
                Assert.That(hostSamples, Is.EqualTo(400)); Assert.That(joinSamples, Is.EqualTo(400));
                Assert.That(pair.Host.State.Checksum(), Is.EqualTo(pair.Join.State.Checksum()));
                var history = new byte[8];
                Assert.That(pair.Host.Rollback.CopyLocalHistory(401, history), Is.EqualTo(8));
                Assert.That(history.All(bits => bits == 0), Is.True);
                Assert.That(pair.Join.Rollback.CopyLocalHistory(401, history), Is.EqualTo(8));
                Assert.That(history.All(bits => bits == 5), Is.True);
            }
        }

        [Test]
        public void StartupWithoutPeersRealInputStopsAfterOneSampleAndRetriesThatSample()
        {
            using (var pair = new Pair())
            {
                pair.PumpUntil(() => pair.Host.Ready && pair.Join.Ready);
                int samples = 0;
                for (int i = 0; i < 20; i++)
                {
                    pair.Host.Advance((epoch, tick) => { samples++; return Input(Vector2.right, true); });
                    pair.Join.Pump(); pair.Now += .06f; Thread.Sleep(1);
                }
                Assert.That(samples, Is.EqualTo(1));
                Assert.That(pair.Host.State.Tick, Is.EqualTo(1));
                Assert.That(pair.Join.State.Tick, Is.Zero);
                Assert.That(pair.Host.Lifecycle.Phase, Is.EqualTo(LanLifecyclePhase.StartupBarrier));
                Assert.That(pair.Join.AcceptedInputDatagrams, Is.GreaterThan(1), "The same retained startup sample is retransmitted.");
                var history = new byte[8];
                Assert.That(pair.Host.Rollback.CopyLocalHistory(2, history), Is.EqualTo(1));
                Assert.That(history[0], Is.EqualTo(Input(Vector2.right, true).Pack()));
                pair.DriveTo(20);
                Assert.That(pair.Host.Frozen || pair.Join.Frozen, Is.False);
                Assert.That(pair.Host.InitializationCount, Is.EqualTo(1));
                Assert.That(pair.Join.InitializationCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void FastPeerWaitsBeforeSamplingAndResendsHistoryThenConsumesPendingDashExactlyOnce()
        {
            using (var pair = new Pair())
            {
                pair.DriveTo(220);
                int sampled = 0;
                for (int i = 0; i < 100; i++)
                    pair.Host.Advance((epoch, tick) => { sampled++; return default; });
                Assert.That(pair.Host.State.Tick, Is.EqualTo(231));
                Assert.That(sampled, Is.EqualTo(11));
                Assert.That(pair.Host.WaitingForRemoteInput, Is.True);
                Assert.That(pair.Host.InputLagTicks, Is.EqualTo(DashLanSession.MaximumInputLeadTicks));
                Assert.That(pair.Host.RemoteContiguousInputTick, Is.EqualTo(pair.Host.LatestRemoteInputTick));
                Assert.That(pair.Host.InputWaitCount, Is.EqualTo(1));
                int hash = pair.Host.State.Checksum(); int sent = pair.Host.SentDatagrams;
                int newest = pair.Host.LatestLocalInputTick;
                var original = new byte[8]; pair.Host.Rollback.CopyLocalHistory(newest, original);
                pair.Now += .3f;
                pair.Host.Advance((epoch, tick) => { Assert.Fail("Pacing wait consumed a pending edge."); return default; });
                pair.Join.Pump();
                Assert.That(pair.Host.State.Checksum(), Is.EqualTo(hash));
                Assert.That(pair.Host.LatestLocalInputTick, Is.EqualTo(newest));
                Assert.That(pair.Host.SentDatagrams, Is.GreaterThan(sent), "Waiting retransmits without sampling a new tick.");
                var repeated = new byte[8]; pair.Host.Rollback.CopyLocalHistory(newest, repeated);
                CollectionAssert.AreEqual(original, repeated);
                Assert.That(pair.Host.InputWaitCurrentSeconds, Is.EqualTo(.3f).Within(.001f));

                pair.Join.Advance(Neutral); Thread.Sleep(2);
                pair.Host.Pump();
                Assert.That(pair.Host.WaitingForRemoteInput, Is.False, "Pump itself resumes the HUD state after actual prefix progress.");
                Assert.That(pair.Host.State.Tick, Is.EqualTo(231), "A receive pump does not sample or advance the simulation.");
                int dashSamples = 0, dashes = 0;
                pair.Host.ConfirmedEvents += events => dashes += events.Count(e => e.Seat == 0 && e.Kind == DashArenaEventKind.DashStarted);
                pair.Host.Advance((epoch, tick) => { dashSamples++; return Input(Vector2.right, true); });
                pair.DriveTo(280);
                Assert.That(dashSamples, Is.EqualTo(1)); Assert.That(dashes, Is.EqualTo(1));
                Assert.That(pair.Host.State.Checksum(), Is.EqualTo(pair.Join.State.Checksum()));
                Assert.That(pair.Host.InputWaitSeconds, Is.GreaterThan(.299f));
                Assert.That(pair.Host.InputWaitMaxSeconds, Is.GreaterThan(.299f));
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AsymmetricFixedStepRatesKeepBothChecksumsGrowingBeforeSlowPeerCatchesUp(bool hostIsFast)
        {
            using (var pair = new Pair())
            {
                pair.DriveTo(220);
                DashLanSession fastPeer = hostIsFast ? pair.Host : pair.Join;
                DashLanSession slowPeer = hostIsFast ? pair.Join : pair.Host;
                int fastChecksums = fastPeer.Rollback.ValidatedChecksumCount;
                int slowChecksums = slowPeer.Rollback.ValidatedChecksumCount;
                int waitCount = fastPeer.InputWaitCount;
                for (int frame = 0; frame < 1200; frame++)
                {
                    // Four attempted fast fixed steps per one actual slow fixed step.
                    // Earlier tests paired one Advance on each peer and hid this drift.
                    for (int fast = 0; fast < 4; fast++) fastPeer.Advance(Neutral);
                    slowPeer.Advance(Neutral);
                    pair.Host.Pump(); pair.Join.Pump();
                    Assert.That(pair.Host.Frozen || pair.Join.Frozen, Is.False,
                        pair.Host.FailureReason + "/" + pair.Join.FailureReason);
                    Assert.That(fastPeer.State.Tick - fastPeer.Rollback.ConfirmedTick,
                        Is.LessThanOrEqualTo(DashLanSession.MaximumInputLeadTicks));
                    if ((frame + 1) % 120 == 0)
                    {
                        // Neither eventual catch-up nor confirmed input progress alone
                        // proves checksum validation survives a persistently fast peer.
                        Assert.That(fastPeer.Rollback.ValidatedChecksumCount - fastChecksums, Is.GreaterThanOrEqualTo(100),
                            "Fast peer stopped validating new slow-peer checksums during sustained 4:1 stepping.");
                        Assert.That(slowPeer.Rollback.ValidatedChecksumCount - slowChecksums, Is.GreaterThanOrEqualTo(100),
                            "Slow peer stopped validating checksums during sustained 4:1 stepping.");
                        Assert.That(fastPeer.InputWaitCount - waitCount, Is.GreaterThan(60),
                            "The regression must repeatedly exercise the active lead boundary.");
                        fastChecksums = fastPeer.Rollback.ValidatedChecksumCount;
                        slowChecksums = slowPeer.Rollback.ValidatedChecksumCount;
                        waitCount = fastPeer.InputWaitCount;
                    }
                    pair.Now += 1f / 60f; Thread.Sleep(1);
                }
                Assert.That(fastPeer.InputWaitCount, Is.GreaterThan(0));
                pair.DriveTo(1460);
                Assert.That(pair.Host.State.Checksum(), Is.EqualTo(pair.Join.State.Checksum()));
                Assert.That(pair.Host.WaitingForRemoteInput || pair.Join.WaitingForRemoteInput, Is.False);
                Assert.That(DashRollbackSession.BufferTicks, Is.EqualTo(12));
                Assert.That(DashRollbackSession.DelayTicks, Is.EqualTo(2));
                Assert.That(DashLanSession.MaximumInputLeadTicks, Is.EqualTo(10));
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RepeatedExactLeadBoundaryRetainsAndValidatesEachNewSlowPeerChecksum(bool hostIsFast)
        {
            using (var pair = new Pair())
            {
                pair.DriveTo(220);
                DashLanSession fastPeer = hostIsFast ? pair.Host : pair.Join;
                DashLanSession slowPeer = hostIsFast ? pair.Join : pair.Host;
                for (int cycle = 0; cycle < 360; cycle++)
                {
                    int fastChecksums = fastPeer.Rollback.ValidatedChecksumCount;
                    int slowChecksums = slowPeer.Rollback.ValidatedChecksumCount;
                    int waits = fastPeer.InputWaitCount;
                    // Saturate the real session gate without advancing the slow peer.
                    for (int attempt = 0; attempt < DashRollbackSession.BufferTicks + 3; attempt++)
                        fastPeer.Advance(Neutral);
                    pair.Drain();
                    Assert.That(fastPeer.WaitingForRemoteInput, Is.True);
                    Assert.That(fastPeer.InputWaitCount, Is.EqualTo(waits + 1));
                    Assert.That(fastPeer.State.Tick, Is.EqualTo(slowPeer.State.Tick + DashRollbackSession.BufferTicks - 1));
                    Assert.That(fastPeer.State.Tick - fastPeer.RemoteContiguousInputTick,
                        Is.EqualTo(DashLanSession.MaximumInputLeadTicks));
                    int slowChecksumTick = slowPeer.State.Tick - 1;
                    Assert.That(slowChecksumTick, Is.EqualTo(fastPeer.State.Tick - DashRollbackSession.BufferTicks),
                        "The slow peer's latest executed state must be included at the checksum receive boundary.");
                    Assert.That(fastPeer.Rollback.TryGetConfirmedChecksum(slowChecksumTick, out int retainedTick, out int retainedChecksum), Is.True);
                    Assert.That(retainedTick, Is.EqualTo(slowChecksumTick));
                    Assert.That(retainedChecksum, Is.EqualTo(slowPeer.State.Checksum()));

                    pair.Now += 1f / 60f;
                    slowPeer.Advance(Neutral); // Sends a genuinely new input and its actual completed-state checksum.
                    pair.Drain();
                    Assert.That(fastPeer.Frozen || slowPeer.Frozen, Is.False,
                        fastPeer.FailureReason + "/" + slowPeer.FailureReason);
                    Assert.That(fastPeer.WaitingForRemoteInput, Is.False);
                    Assert.That(fastPeer.Rollback.ValidatedChecksumCount, Is.GreaterThan(fastChecksums),
                        "Every boundary/resume cycle must validate the new slow-peer checksum, not only its input.");
                    Assert.That(slowPeer.Rollback.ValidatedChecksumCount, Is.GreaterThan(slowChecksums));
                }
                pair.DriveTo(620);
                Assert.That(pair.Host.State.Checksum(), Is.EqualTo(pair.Join.State.Checksum()));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HeartbeatsOrDuplicateInputsCannotRenewTheFiveSecondPacingDeadline(bool duplicates)
        {
            using (var pair = new Pair())
            {
                pair.DriveTo(220);
                for (int i = 0; i < 30; i++) pair.Host.Advance(Neutral);
                Assert.That(pair.Host.WaitingForRemoteInput, Is.True);
                int hash = pair.Host.State.Checksum();
                byte[] packet = duplicates ? LastInput(pair.Join) : DashNetcode.EncodeLifecycle(Heartbeat(pair.Join));
                for (int i = 0; i < 49; i++)
                {
                    pair.Now += .1f; Assert.That(Peer(pair.Join).Send(packet), Is.True);
                    Thread.Sleep(1); pair.Host.Pump();
                    Assert.That(pair.Host.Frozen, Is.False, pair.Host.FailureReason);
                    Assert.That(pair.Host.State.Checksum(), Is.EqualTo(hash));
                }
                pair.Now += .12f; Assert.That(Peer(pair.Join).Send(packet), Is.True);
                Thread.Sleep(2); pair.Host.Pump();
                Assert.That(pair.Host.FailureReason, Is.EqualTo("INPUT PROGRESS TIMEOUT"));
                Assert.That(pair.Host.Lifecycle.LastAcceptedPeerTime, Is.EqualTo(pair.Now), "Peer liveness remains healthy.");
                Assert.That(pair.Host.InputProgressAgeSeconds, Is.GreaterThanOrEqualTo(5f));
                Assert.That(pair.Host.WaitingForRemoteInput, Is.False);
                Assert.That(pair.Host.SocketBound, Is.False);
                float seconds = pair.Host.InputWaitSeconds, age = pair.Host.InputProgressAgeSeconds;
                pair.Now += 20f;
                pair.Host.Advance((epoch, tick) => { Assert.Fail("Terminal timeout sampled input."); return default; });
                Assert.That(pair.Host.State.Checksum(), Is.EqualTo(hash));
                Assert.That(pair.Host.InputWaitSeconds, Is.EqualTo(seconds));
                Assert.That(pair.Host.InputProgressAgeSeconds, Is.EqualTo(age));
            }
        }

        [Test]
        public void QueuedContiguousProgressAtExpiredDeadlineIsDrainedBeforeWaitingTimeout()
        {
            using (var pair = new Pair())
            {
                pair.DriveTo(220);
                for (int i = 0; i < 30; i++) pair.Host.Advance(Neutral);
                Assert.That(pair.Host.WaitingForRemoteInput, Is.True);
                pair.Now += 4.9f;
                pair.Join.Advance(Neutral); // Generates the genuinely next required input.
                pair.Now += .2f; Thread.Sleep(2);
                pair.Host.Pump(); // Old input-progress deadline has passed, but recovery is queued.
                Assert.That(pair.Host.Frozen, Is.False, pair.Host.FailureReason);
                Assert.That(pair.Host.WaitingForRemoteInput, Is.False);
                Assert.That(pair.Host.InputProgressAgeSeconds, Is.EqualTo(0f));
                Assert.That(pair.Host.RemoteContiguousInputTick, Is.EqualTo(222));
                pair.Host.Advance(Neutral);
                Assert.That(pair.Host.State.Tick, Is.EqualTo(232));
            }
        }

        [Test]
        public void WrongEndpointClaimingNewInputCannotRefreshTheIndependentProgressDeadline()
        {
            using (var pair = new Pair())
            using (var unrelated = new LanUdpPeer())
            {
                pair.DriveTo(220);
                Assert.That(unrelated.BindEphemeral(), Is.True);
                for (int i = 0; i < 30; i++) pair.Host.Advance(Neutral);
                Assert.That(pair.Host.WaitingForRemoteInput, Is.True);
                Assert.That(DashNetcode.TryDecodeInput(LastInput(pair.Join), out var forged), Is.True);
                forged.NewestTick++; forged.HistoryCount = 1;
                pair.Now += .5f;
                Assert.That(unrelated.SendTo(DashNetcode.EncodeInput(forged), Address(pair.Host)), Is.True);
                Assert.That(Peer(pair.Join).Send(DashNetcode.EncodeLifecycle(Heartbeat(pair.Join))), Is.True);
                Thread.Sleep(2); pair.Host.Pump();
                Assert.That(pair.Host.RemoteContiguousInputTick, Is.EqualTo(221));
                Assert.That(pair.Host.InputProgressAgeSeconds, Is.GreaterThanOrEqualTo(.5f));
                Assert.That(pair.Host.WaitingForRemoteInput, Is.True);
                Assert.That(pair.Host.RejectedDatagrams, Is.GreaterThan(0));
                pair.Now += 4.6f;
                Assert.That(unrelated.SendTo(DashNetcode.EncodeInput(forged), Address(pair.Host)), Is.True);
                Assert.That(Peer(pair.Join).Send(DashNetcode.EncodeLifecycle(Heartbeat(pair.Join))), Is.True);
                Thread.Sleep(2); pair.Host.Pump();
                Assert.That(pair.Host.FailureReason, Is.EqualTo("INPUT PROGRESS TIMEOUT"));
                Assert.That(unrelated.Bound, Is.True);
            }
        }

        [Test]
        public void LaterSampleExposesInteriorHoleAndCannotBeHiddenByExistingPacingWait()
        {
            using (var pair = new Pair())
            {
                pair.DriveTo(220);
                for (int i = 0; i < 30; i++) pair.Host.Advance(Neutral);
                Assert.That(pair.Host.WaitingForRemoteInput, Is.True);
                ExposeActualMiddleHole(pair);
                Assert.That(pair.Host.WaitingForRemoteInput, Is.False);
                for (int i = 0; i < DashRollbackSession.BufferTicks && !pair.Host.Frozen && pair.Host.State.Tick < 234; i++)
                    pair.Host.Advance(Neutral);
                Assert.That(pair.Host.State.Tick, Is.EqualTo(234));
                pair.Host.Advance(Neutral);
                Assert.That(pair.Host.FailureReason, Is.EqualTo("INPUT HOLE"));
                Assert.That(pair.Host.State.Tick, Is.EqualTo(234));
            }
        }

        [Test]
        public void LeaveClearsActivePacingWaitAndFreshConnectionHasNoOldMetrics()
        {
            using (var pair = new Pair())
            {
                pair.DriveTo(220);
                for (int i = 0; i < 30; i++) pair.Host.Advance(Neutral);
                Assert.That(pair.Host.WaitingForRemoteInput, Is.True);
                pair.Now += .25f; pair.Host.Leave();
                Assert.That(pair.Host.WaitingForRemoteInput, Is.False);
                Assert.That(pair.Host.InputWaitCurrentSeconds, Is.Zero);
                Assert.That(pair.Host.InputWaitSeconds, Is.EqualTo(.25f).Within(.001f));
                Assert.That(pair.Host.Left, Is.True);
            }
            using (var fresh = new Pair())
            {
                fresh.DriveTo(20);
                Assert.That(fresh.Host.InputWaitCount + fresh.Join.InputWaitCount, Is.Zero);
                Assert.That(fresh.Host.InputWaitSeconds + fresh.Join.InputWaitSeconds, Is.Zero);
            }
        }

        [Test]
        public void CatchUpDrainsQueuedMissingHistoryBeforeItsTwelveTickBoundary()
        {
            using (var pair = new Pair())
            {
                pair.DriveTo(220);
                // Fault fixture: the joiner really generates ticks 222..225, but only
                // 225 is delivered. This is an interior hole, not an ungenerated suffix.
                ExposeActualMiddleHole(pair);
                for (int i = 0; i < 14; i++) pair.Host.Advance(Neutral);
                Assert.That(pair.Host.State.Tick, Is.EqualTo(234));
                Assert.That(pair.Host.Frozen, Is.False);
                Assert.That(pair.Join.State.Tick, Is.EqualTo(224));
                // Retry the original retained suffix, including the actually missing
                // samples. Host drains it before its next boundary check.
                Assert.That(Peer(pair.Join).Send(LastInput(pair.Join)), Is.True);
                Thread.Sleep(2);
                pair.Host.Advance(Neutral);
                Assert.That(pair.Host.Frozen, Is.False, pair.Host.FailureReason);
                Assert.That(pair.Host.State.Tick, Is.EqualTo(235));
                pair.DriveTo(250);
                Assert.That(pair.Host.State.Checksum(), Is.EqualTo(pair.Join.State.Checksum()));
            }
        }

        [Test]
        public void HeartbeatCannotFillInputHoleAndFailureFreezesEveryMechanicClock()
        {
            using (var pair = new Pair())
            {
                pair.DriveTo(220);
                SeedDisabledMechanismFixture(pair.Host.State); SeedDisabledMechanismFixture(pair.Join.State);
                ExposeActualMiddleHole(pair);
                for (int i = 0; i < 14; i++) pair.Host.Advance(Neutral);
                Assert.That(pair.Host.Frozen, Is.False);
                int hash = pair.Host.State.Checksum();
                Assert.That(Peer(pair.Join).Send(DashNetcode.EncodeLifecycle(Heartbeat(pair.Join))), Is.True);
                Thread.Sleep(2);
                Assert.That(pair.Host.Advance(Neutral), Is.Empty);
                Assert.That(pair.Host.FailureReason, Is.EqualTo("INPUT HOLE"));
                Assert.That(pair.Host.WaitingForRemoteInput, Is.False, "A genuine middle hole is not an input-pacing wait.");
                Assert.That(pair.Host.State.Checksum(), Is.EqualTo(hash));
                Assert.That(pair.Host.State.Players[0].RespawnTicks, Is.GreaterThan(0));
                Assert.That(pair.Host.State.Players[1].StunTicks, Is.GreaterThan(0));
                Assert.That(pair.Host.State.Walls[0].RepairTicks, Is.GreaterThan(0));
                for (int i = 0; i < 40; i++)
                {
                    pair.Now += 1f;
                    pair.Host.Advance((epoch, tick) => { Assert.Fail("Frozen session sampled controls."); return default; });
                }
                Assert.That(pair.Host.State.Checksum(), Is.EqualTo(hash), "All timer, PRNG, phase and world state stays frozen.");
            }
        }

        [Test]
        public void QueuedValidDuplicateRefreshesPeerBeforeTimeoutEvaluationWithoutReplayingEffects()
        {
            using (var pair = new Pair())
            {
                pair.DriveTo(220);
                int before = pair.Host.AcceptedInputDatagrams;
                long events = pair.Host.CommittedEventCount;
                byte[] duplicate = LastInput(pair.Join);
                pair.Now += DashLanLifecycle.DisconnectSeconds + .01f;
                Assert.That(Peer(pair.Join).Send(duplicate), Is.True);
                Assert.That(Peer(pair.Join).Send(duplicate), Is.True);
                Thread.Sleep(2);
                pair.Host.Pump();
                Assert.That(pair.Host.Frozen, Is.False, pair.Host.FailureReason);
                Assert.That(pair.Host.AcceptedInputDatagrams, Is.EqualTo(before + 2));
                Assert.That(pair.Host.CommittedEventCount, Is.EqualTo(events));
                Assert.That(pair.Host.Lifecycle.LastAcceptedPeerTime, Is.EqualTo(pair.Now));
            }
        }

        [Test]
        public void WrongEndpointCannotKeepSessionAliveAndSilenceDoesNotAdvanceSimulation()
        {
            using (var pair = new Pair())
            using (var unrelated = new LanUdpPeer())
            {
                pair.DriveTo(220);
                Assert.That(unrelated.BindEphemeral(), Is.True);
                int hash = pair.Host.State.Checksum();
                pair.Now += DashLanLifecycle.DisconnectSeconds + .01f;
                Assert.That(unrelated.SendTo(DashNetcode.EncodeLifecycle(Heartbeat(pair.Join)), Address(pair.Host)), Is.True);
                Thread.Sleep(2);
                pair.Host.Advance(Neutral);
                Assert.That(pair.Host.RejectedDatagrams, Is.GreaterThan(0));
                Assert.That(pair.Host.FailureReason, Is.EqualTo("PEER TIMEOUT"));
                Assert.That(pair.Host.State.Checksum(), Is.EqualTo(hash));
                Assert.That(unrelated.Bound, Is.True, "A session must not close an unrelated socket.");
            }
        }

        [Test]
        public void FallenAndStunnedSeatsStillTransportInputsWithoutActivatingDash()
        {
            using (var pair = new Pair())
            {
                pair.DriveTo(220);
                SeedDisabledMechanismFixture(pair.Host.State); SeedDisabledMechanismFixture(pair.Join.State);
                int hostInputs = pair.Host.AcceptedInputDatagrams, joinInputs = pair.Join.AcceptedInputDatagrams;
                int dashes = 0;
                pair.Host.ConfirmedEvents += batch => dashes += batch.Count(e => e.Kind == DashArenaEventKind.DashStarted);
                pair.DriveTo(240, (epoch, tick) => Input(Vector2.right, true), (epoch, tick) => Input(Vector2.left, true));
                Assert.That(pair.Host.AcceptedInputDatagrams, Is.GreaterThan(hostInputs));
                Assert.That(pair.Join.AcceptedInputDatagrams, Is.GreaterThan(joinInputs));
                Assert.That(pair.Host.State.Players[0].Alive, Is.False);
                Assert.That(pair.Host.State.Players[1].StunTicks, Is.GreaterThan(0));
                Assert.That(dashes, Is.Zero);
                Assert.That(pair.Host.State.Checksum(), Is.EqualTo(pair.Join.State.Checksum()));
            }
        }

        [Test]
        public void LeaveNotifiesPeerAndClosesOnlyTheLeavingSessionsSocket()
        {
            using (var pair = new Pair())
            using (var unrelated = new LanUdpPeer())
            {
                pair.DriveTo(220); Assert.That(unrelated.BindEphemeral(), Is.True);
                int tick = pair.Host.State.Tick;
                pair.Join.Leave();
                Assert.That(pair.Join.Left, Is.True); Assert.That(pair.Join.SocketBound, Is.False);
                Thread.Sleep(2); pair.Host.Pump();
                Assert.That(pair.Host.FailureReason, Is.EqualTo("PEER LEFT"));
                Assert.That(pair.Host.FailureOrigin, Is.EqualTo("peer"));
                Assert.That(pair.Host.PeerFailureReason, Is.Empty);
                Assert.That(pair.Join.FailureOrigin, Is.EqualTo("none"));
                Assert.That(pair.Host.State.Tick, Is.EqualTo(tick));
                Assert.That(unrelated.Bound, Is.True);
                pair.Join.Leave(); // Idempotent cleanup.
            }
        }

        [Test]
        public void OneDirectionSilenceProducesLocalTimeoutAndPeerFaultWithFrozenFirstCause()
        {
            using (var pair = new Pair())
            {
                pair.DriveTo(220);
                float lastAccepted = pair.Join.Lifecycle.LastAcceptedPeerTime;
                int hostHash = pair.Host.State.Checksum(), joinHash = pair.Join.State.Checksum();
                // Host is no longer pumping/sending. Join continues to pump and send
                // its heartbeats: only the Host -> Join direction is silent.
                for (int i = 1; i <= 9; i++)
                {
                    pair.Now = lastAccepted + i * .5f;
                    pair.Join.Pump();
                    Assert.That(pair.Join.Frozen, Is.False, pair.Join.FailureReason);
                }
                pair.Now = lastAccepted + DashLanLifecycle.DisconnectSeconds + .01f;
                pair.Join.Pump();
                Assert.That(pair.Join.FailureReason, Is.EqualTo("PEER TIMEOUT"));
                Assert.That(pair.Join.FailureOrigin, Is.EqualTo("local"));
                Assert.That(pair.Join.PeerFailureReason, Is.Empty);
                Assert.That(pair.Join.AcceptedPeerAgeSeconds, Is.EqualTo(5.01f).Within(.001f));
                Thread.Sleep(2); pair.Host.Pump();
                Assert.That(pair.Host.FailureReason, Is.EqualTo("PEER FAULT"));
                Assert.That(pair.Host.FailureOrigin, Is.EqualTo("peer"));
                Assert.That(pair.Host.PeerFailureReason, Is.EqualTo("PEER TIMEOUT"));
                Assert.That(pair.Host.State.Checksum(), Is.EqualTo(hostHash));
                Assert.That(pair.Join.State.Checksum(), Is.EqualTo(joinHash));
                float rawAge = pair.Join.RawReceiveAgeSeconds, acceptedAge = pair.Join.AcceptedPeerAgeSeconds;
                int pumps = pair.Join.PumpCount, sends = pair.Join.SendAttempts;
                pair.Now += 120f;
                pair.Join.Pump(); pair.Join.Leave(); pair.Host.Leave();
                Assert.That(pair.Join.RawReceiveAgeSeconds, Is.EqualTo(rawAge));
                Assert.That(pair.Join.AcceptedPeerAgeSeconds, Is.EqualTo(acceptedAge));
                Assert.That(pair.Join.PumpCount, Is.EqualTo(pumps));
                Assert.That(pair.Join.SendAttempts, Is.EqualTo(sends), "Leave after fault must not replace its notification with Goodbye.");
                Assert.That(pair.Join.FailureReason, Is.EqualTo("PEER TIMEOUT"));
                Assert.That(pair.Host.PeerFailureReason, Is.EqualTo("PEER TIMEOUT"));
            }
        }

        [Test]
        public void AcceptedPeerFaultIsNeverEchoedAndRetainsTheFirstRemoteReason()
        {
            using (var pair = new Pair())
            {
                pair.DriveTo(220);
                int sent = pair.Host.SendAttempts;
                Assert.That(Peer(pair.Join).Send(Fault(pair.Join, DashPeerFaultReason.InputProgressTimeout)), Is.True);
                Assert.That(Peer(pair.Join).Send(Fault(pair.Join, DashPeerFaultReason.Desync)), Is.True);
                Thread.Sleep(2); pair.Host.Pump();
                Assert.That(pair.Host.FailureReason, Is.EqualTo("PEER FAULT"));
                Assert.That(pair.Host.PeerFailureReason, Is.EqualTo("INPUT PROGRESS TIMEOUT"));
                Assert.That(pair.Host.SendAttempts, Is.EqualTo(sent), "Incoming faults must not trigger any reply fault or Goodbye.");
                pair.Now += 60f; pair.Host.Leave();
                Assert.That(pair.Host.PeerFailureReason, Is.EqualTo("INPUT PROGRESS TIMEOUT"));
                Assert.That(pair.Host.SendAttempts, Is.EqualTo(sent));
            }
        }

        [Test]
        public void InvalidFaultsObserveRawTrafficButNeverRenewLivenessOrTerminateEarly()
        {
            using (var pair = new Pair())
            using (var foreign = new LanUdpPeer())
            {
                pair.DriveTo(220);
                Assert.That(foreign.BindEphemeral(), Is.True);
                float acceptedAt = pair.Host.Lifecycle.LastAcceptedPeerTime;
                int controls = pair.Host.RejectedControlDatagrams, endpoints = pair.Host.RejectedEndpointDatagrams;
                int received = pair.Host.ReceivedDatagrams;
                byte[] valid = Fault(pair.Join, DashPeerFaultReason.PeerTimeout);
                var invalid = new List<byte[]>();
                foreach (var change in new[] { (offset: 5, value: 7), (offset: 9, value: pair.Host.MatchId + 1), (offset: 13, value: 1) })
                {
                    byte[] bad = (byte[])valid.Clone();
                    Array.Copy(BitConverter.GetBytes(change.value), 0, bad, change.offset, 4); invalid.Add(bad);
                }
                byte[] wrongSeat = (byte[])valid.Clone(); wrongSeat[17] = 0; invalid.Add(wrongSeat);
                byte[] badReason = (byte[])valid.Clone(); badReason[18] = 255; invalid.Add(badReason);
                invalid.Add(valid.Take(valid.Length - 1).ToArray());
                invalid.Add(valid.Concat(new byte[1]).ToArray());
                invalid.Add(Array.Empty<byte>());
                pair.Now = acceptedAt + 4.9f;
                foreach (byte[] bad in invalid) Assert.That(Peer(pair.Join).Send(bad), Is.True);
                Assert.That(foreign.SendTo(valid, Address(pair.Host)), Is.True);
                Thread.Sleep(2); pair.Host.Pump();
                Assert.That(pair.Host.Frozen, Is.False, pair.Host.FailureReason);
                Assert.That(pair.Host.ReceivedDatagrams, Is.EqualTo(received + invalid.Count + 1));
                Assert.That(pair.Host.RejectedControlDatagrams, Is.EqualTo(controls + invalid.Count));
                Assert.That(pair.Host.RejectedEndpointDatagrams, Is.EqualTo(endpoints + 1));
                Assert.That(pair.Host.RawReceiveAgeSeconds, Is.Zero);
                Assert.That(pair.Host.Lifecycle.LastAcceptedPeerTime, Is.EqualTo(acceptedAt));
                Assert.That(pair.Host.AcceptedPeerAgeSeconds, Is.EqualTo(4.9f).Within(.001f));
                pair.Now = acceptedAt + 5.01f; pair.Host.Pump();
                Assert.That(pair.Host.FailureReason, Is.EqualTo("PEER TIMEOUT"));
            }
        }

        [Test]
        public void ShortReceiveStallRecordsPumpGapThenValidTrafficRecoversWithoutChangingTimeout()
        {
            using (var pair = new Pair())
            {
                Assert.That(pair.Host.RawReceiveAgeSeconds, Is.EqualTo(-1f));
                Assert.That(pair.Host.AcceptedPeerAgeSeconds, Is.EqualTo(-1f));
                pair.DriveTo(220);
                int pumpCount = pair.Host.PumpCount, received = pair.Host.ReceivedDatagrams;
                int empty = pair.Host.EmptyReceivePolls;
                float lastAccepted = pair.Host.Lifecycle.LastAcceptedPeerTime;
                pair.Now += 3f;
                pair.Host.Pump();
                Assert.That(pair.Host.Frozen, Is.False);
                Assert.That(pair.Host.PumpCount, Is.EqualTo(pumpCount + 1));
                Assert.That(pair.Host.LastPumpGapSeconds, Is.EqualTo(3f).Within(.001f));
                Assert.That(pair.Host.MaxPumpGapSeconds, Is.EqualTo(3f).Within(.001f));
                Assert.That(pair.Host.EmptyReceivePolls, Is.GreaterThan(empty));
                Assert.That(pair.Host.AcceptedPeerAgeSeconds, Is.EqualTo(pair.Now - lastAccepted).Within(.001f));
                Assert.That(Peer(pair.Join).Send(DashNetcode.EncodeLifecycle(Heartbeat(pair.Join))), Is.True);
                Thread.Sleep(2); pair.Host.Pump();
                Assert.That(pair.Host.RawReceiveAgeSeconds, Is.Zero);
                Assert.That(pair.Host.AcceptedPeerAgeSeconds, Is.Zero);
                Assert.That(pair.Host.ReceivedDatagrams, Is.EqualTo(received + 1));
                Assert.That(pair.Host.ReceiveFailures, Is.Zero);
                pair.DriveTo(240);
                Assert.That(pair.Host.State.Checksum(), Is.EqualTo(pair.Join.State.Checksum()));
                Assert.That(DashLanLifecycle.DisconnectSeconds, Is.EqualTo(5f));
            }
        }

        [Test]
        public void SessionRetainsReceiveFailureAndRecoversBeforeFiveSecondDeadline()
        {
            using (var pair = new Pair())
            using (var invalidReceiveSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            using (var pendingConnection = new TcpClient(AddressFamily.InterNetwork))
            {
                pair.DriveTo(220);
                LanUdpPeer peer = Peer(pair.Host);
                var client = (UdpClient)typeof(LanUdpPeer).GetField("_udp", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(peer);
                Socket original = client.Client;
                invalidReceiveSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                invalidReceiveSocket.Listen(1);
                pendingConnection.Connect((IPEndPoint)invalidReceiveSocket.LocalEndPoint);
                invalidReceiveSocket.Blocking = false;
                Assert.That(invalidReceiveSocket.Poll(1000000, SelectMode.SelectRead), Is.True);
                float acceptedAt = pair.Host.Lifecycle.LastAcceptedPeerTime;
                try
                {
                    client.Client = invalidReceiveSocket;
                    pair.Host.Pump();
                    Assert.That(pair.Host.Frozen, Is.False);
                    Assert.That(pair.Host.ReceiveFailures, Is.EqualTo(1));
                    Assert.That(pair.Host.LastReceiveError, Is.Not.Empty);
                    Assert.That(pair.Host.SendFailures, Is.Zero);
                }
                finally { client.Client = original; }
                string error = pair.Host.LastReceiveError;
                Assert.That(pair.Host.Lifecycle.LastAcceptedPeerTime, Is.EqualTo(acceptedAt));
                pair.Now += 1f;
                Assert.That(Peer(pair.Join).Send(DashNetcode.EncodeLifecycle(Heartbeat(pair.Join))), Is.True);
                Thread.Sleep(2); pair.Host.Pump();
                Assert.That(pair.Host.Frozen, Is.False);
                Assert.That(pair.Host.AcceptedPeerAgeSeconds, Is.Zero);
                Assert.That(pair.Host.ReceiveFailures, Is.EqualTo(1));
                Assert.That(pair.Host.LastReceiveError, Is.EqualTo(error));
                Assert.That(pair.Host.LastSocketError, Is.Empty);
                pair.DriveTo(240);
                Assert.That(pair.Host.State.Checksum(), Is.EqualTo(pair.Join.State.Checksum()));
            }
        }

        [Test]
        public void ContentMismatchFailsBothPeersBeforeStateInitialization()
        {
            using (var pair = new Pair(ArenaRulesProfile.CreateProductionV1(), ArenaRulesProfile.CreateProductionV1(ballCount: 2)))
            {
                pair.PumpUntil(() => pair.Host.Frozen && pair.Join.Frozen);
                Assert.That(pair.Host.FailureReason, Is.EqualTo("SETTINGS MISMATCH"));
                Assert.That(pair.Join.FailureReason, Is.EqualTo("SETTINGS MISMATCH"));
                Assert.That(pair.Host.State, Is.Null); Assert.That(pair.Join.State, Is.Null);
                Assert.That(pair.Host.InitializationCount + pair.Join.InitializationCount, Is.Zero);
                pair.Now += NetplayProtocol.HandshakeGraceSeconds + .01f; pair.Host.Pump();
                Assert.That(pair.Host.SocketBound, Is.False);
            }
        }

        [Test]
        public void AbsentJoinerHasBoundedHandshakeWithoutCreatingGameTicks()
        {
            float now = 0;
            using (var host = DashLanSession.Host(ArenaRulesProfile.CreateProductionV1(), 987, 45, () => now))
            {
                now = DashLanSession.HandshakeSeconds + .01f;
                host.Pump();
                Assert.That(host.FailureReason, Is.EqualTo("LAN HANDSHAKE TIMEOUT"));
                Assert.That(host.State, Is.Null); Assert.That(host.InitializationCount, Is.Zero);
                Assert.That(host.SocketBound, Is.False);
            }
        }

        [Test]
        public void PresentationSubscriberExceptionIsNotReclassifiedAsNetworkFailure()
        {
            using (var pair = new Pair())
            {
                pair.DriveTo(179);
                Action<IReadOnlyList<DashArenaEvent>> brokenView = _ => throw new InvalidOperationException("test presentation failure");
                pair.Host.ConfirmedEvents += brokenView;
                Assert.DoesNotThrow(() => pair.Host.Advance(Neutral));
                pair.Host.ConfirmedEvents -= brokenView;
                Assert.That(pair.Host.Frozen, Is.False);
                Assert.That(pair.Host.FailureReason, Is.Empty);
                Assert.That(pair.Host.State.Tick, Is.EqualTo(180));
                Assert.That(pair.Host.PresentationErrorCount, Is.EqualTo(1));
                Assert.That(pair.Host.LastPresentationError,
                    Is.EqualTo("ConfirmedEvents: InvalidOperationException: test presentation failure"));
                pair.DriveTo(200);
                Assert.That(pair.Host.State.Checksum(), Is.EqualTo(pair.Join.State.Checksum()));
            }
        }

        [Test]
        public void BrokenConfirmedSubscriberDoesNotSkipLaterSubscriberOrRepeatCommittedBatch()
        {
            using (var pair = new Pair())
            {
                int brokenCalls = 0, healthyCalls = 0;
                var delivered = new List<DashArenaEvent>();
                pair.Host.ConfirmedEvents += batch =>
                {
                    brokenCalls++;
                    throw new InvalidOperationException("first subscriber failed");
                };
                pair.Host.ConfirmedEvents += batch => { healthyCalls++; delivered.AddRange(batch); };
                pair.DriveTo(180);
                Assert.That(brokenCalls, Is.EqualTo(1));
                Assert.That(healthyCalls, Is.EqualTo(1));
                Assert.That(delivered.Count, Is.EqualTo(1));
                Assert.That(delivered[0].Kind, Is.EqualTo(DashArenaEventKind.GateOpened));
                Assert.That(pair.Host.CommittedEventCount, Is.EqualTo(1));
                Assert.That(pair.Host.PresentationErrorCount, Is.EqualTo(1));
                Assert.That(pair.Host.FailureReason, Is.Empty);

                Assert.That(Peer(pair.Join).Send(LastInput(pair.Join)), Is.True);
                Thread.Sleep(2);
                pair.Drain();
                pair.DriveTo(220);
                Assert.That(brokenCalls, Is.EqualTo(1));
                Assert.That(healthyCalls, Is.EqualTo(1));
                Assert.That(pair.Host.CommittedEventCount, Is.EqualTo(1), "Committed batches are not rolled back or redelivered after callback failure.");
                Assert.That(pair.Host.State.Checksum(), Is.EqualTo(pair.Join.State.Checksum()));
            }
        }

        [Test]
        public void BrokenEpochSubscriberDoesNotSkipLaterInitializationOrInterruptStartup()
        {
            using (var pair = new Pair())
            {
                int brokenCalls = 0;
                var epochs = new List<int>();
                pair.Host.EpochInitialized += epoch =>
                {
                    brokenCalls++;
                    throw new InvalidOperationException("epoch view failed");
                };
                pair.Host.EpochInitialized += epochs.Add;
                pair.DriveTo(20);
                Assert.That(brokenCalls, Is.EqualTo(1));
                CollectionAssert.AreEqual(new[] { 0 }, epochs);
                Assert.That(pair.Host.InitializationCount, Is.EqualTo(1));
                Assert.That(pair.Host.Lifecycle.Phase, Is.EqualTo(LanLifecyclePhase.Playing));
                Assert.That(pair.Host.PresentationErrorCount, Is.EqualTo(1));
                Assert.That(pair.Host.LastPresentationError,
                    Is.EqualTo("EpochInitialized: InvalidOperationException: epoch view failed"));
                Assert.That(pair.Host.FailureReason, Is.Empty);
                pair.Drain();
                pair.DriveTo(40);
                Assert.That(brokenCalls, Is.EqualTo(1));
                CollectionAssert.AreEqual(new[] { 0 }, epochs);
                Assert.That(pair.Host.State.Checksum(), Is.EqualTo(pair.Join.State.Checksum()));
            }
        }

        [Test]
        public void ExplicitGoalFixtureConfirmsResultsAndRematchesThreeTimesOnTheSameSockets()
        {
            // This deliberately supplies identical pre-winning-goal worlds to both peers.
            // The ball then crosses the goal through Step, followed by the real score freeze,
            // Finish/FinishAck and rematch protocol. It is NOT a natural full-match driver.
            int seedCalls = 0;
            using (var pair = new Pair(nextSeedFactory: () => (uint)(9000 + ++seedCalls)))
            {
                LanUdpPeer hostSocket = Peer(pair.Host), joinSocket = Peer(pair.Join);
                IPEndPoint hostAddress = Address(pair.Host), joinAddress = Address(pair.Join);
                var hostStarts = new List<DashArenaState>(); var joinStarts = new List<DashArenaState>();
                var hostSessions = new List<DashRollbackSession>(); var joinSessions = new List<DashRollbackSession>();
                var cleanHistories = new List<bool>();
                var hostEvents = new List<DashArenaEvent>(); var joinEvents = new List<DashArenaEvent>();
                pair.Host.EpochInitialized += epoch => CaptureFreshEpoch(pair.Host, hostStarts, hostSessions, cleanHistories);
                pair.Join.EpochInitialized += epoch => CaptureFreshEpoch(pair.Join, joinStarts, joinSessions, cleanHistories);
                pair.Host.ConfirmedEvents += batch => hostEvents.AddRange(batch);
                pair.Join.ConfirmedEvents += batch => joinEvents.AddRange(batch);
                var finishes = new List<NetFinishTuple>(); var seeds = new List<uint>();
                pair.DriveTo(180);
                for (int i = 0; i < 30; i++) pair.Host.Advance(Neutral);
                Assert.That(pair.Host.WaitingForRemoteInput, Is.True);
                pair.DriveTo(200);
                Assert.That(pair.Host.InputWaitCount, Is.GreaterThan(0), "Exercise real prior-epoch pacing before checking reset.");

                for (int epoch = 0; epoch < 3; epoch++)
                {
                    pair.DriveTo(220, (e, tick) => tick == 200 ? Input(Vector2.right, true) : default,
                        (e, tick) => tick == 200 ? Input(Vector2.left, true) : default);
                    SeedPreWinningGoalFixture(pair.Host.State);
                    SeedPreWinningGoalFixture(pair.Join.State);
                    pair.DriveTo(221 + pair.Host.Rules.FreezeTicks);
                    pair.PumpUntil(() => pair.Host.Lifecycle.ResultConfirmed && pair.Join.Lifecycle.ResultConfirmed);
                    Assert.That(pair.Host.State.Phase, Is.EqualTo(MatchState.MatchOver));
                    Assert.That(pair.Join.State.Phase, Is.EqualTo(MatchState.MatchOver));
                    NetFinishTuple finish = pair.Host.Lifecycle.ConfirmedFinish;
                    Assert.That(finish.Equals(pair.Join.Lifecycle.ConfirmedFinish), Is.True);
                    Assert.That(finish.FinishTick, Is.EqualTo(pair.Host.State.Tick - 1));
                    Assert.That(finish.Checksum, Is.EqualTo(pair.Host.State.Checksum()));
                    Assert.That(finish.Score0, Is.EqualTo(pair.Host.Rules.PointsToWin));
                    Assert.That(finish.Score1, Is.Zero); Assert.That(finish.Winner, Is.Zero);
                    Assert.That(hostEvents.Count(e => e.Epoch == epoch && e.Kind == DashArenaEventKind.Goal), Is.EqualTo(1));
                    Assert.That(hostEvents.Count(e => e.Epoch == epoch && e.Kind == DashArenaEventKind.MatchOver), Is.EqualTo(1));
                    Assert.That(pair.Host.State.Walls[0].Broken, Is.True);
                    Assert.That(pair.Host.State.NextPickupId, Is.EqualTo(99));
                    finishes.Add(finish); seeds.Add(pair.Host.Seed);
                    int terminalHash = pair.Host.State.Checksum();
                    int eventCount = hostEvents.Count;
                    if (epoch == 0) pair.Hold(8f); // Healthy results do not expire after five seconds.

                    DashLanSession first = epoch % 2 == 0 ? pair.Host : pair.Join;
                    DashLanSession second = epoch % 2 == 0 ? pair.Join : pair.Host;
                    Assert.That(first.RequestRematch(), Is.True);
                    pair.Hold(8f); // Either seat's unilateral intent must remain a waiting choice.
                    Assert.That(first.Lifecycle.LocalConsent, Is.True);
                    Assert.That(second.Lifecycle.RemoteConsent, Is.True);
                    Assert.That(second.Lifecycle.LocalConsent, Is.False);
                    Assert.That(pair.Host.Epoch, Is.EqualTo(epoch)); Assert.That(pair.Join.Epoch, Is.EqualTo(epoch));
                    Assert.That(pair.Host.State.Checksum(), Is.EqualTo(terminalHash));
                    Assert.That(pair.Join.State.Checksum(), Is.EqualTo(terminalHash));
                    Assert.That(hostEvents.Count, Is.EqualTo(eventCount));
                    Assert.That(seedCalls, Is.EqualTo(epoch), "A unilateral request must not sample a proposed seed.");

                    Assert.That(second.RequestRematch(), Is.True);
                    pair.PumpUntil(() => pair.Host.Epoch == epoch + 1 && pair.Join.Epoch == epoch + 1);
                    Assert.That(seedCalls, Is.EqualTo(epoch + 1));
                    Assert.That(pair.Host.Seed, Is.EqualTo((uint)(9001 + epoch)));
                    Assert.That(pair.Join.Seed, Is.EqualTo(pair.Host.Seed));
                    Assert.That(pair.Host.InitializationCount, Is.EqualTo(epoch + 2));
                    Assert.That(pair.Join.InitializationCount, Is.EqualTo(epoch + 2));
                    Assert.That(Peer(pair.Host), Is.SameAs(hostSocket)); Assert.That(Peer(pair.Join), Is.SameAs(joinSocket));
                    Assert.That(Address(pair.Host), Is.EqualTo(hostAddress)); Assert.That(Address(pair.Join), Is.EqualTo(joinAddress));
                    Assert.That(pair.Host.MatchId, Is.EqualTo(771)); Assert.That(pair.Join.MatchId, Is.EqualTo(771));
                    Assert.That(pair.Host.SocketBound && pair.Join.SocketBound, Is.True);
                    Assert.That(pair.Host.InputWaitCount + pair.Join.InputWaitCount, Is.Zero);
                    Assert.That(pair.Host.InputWaitSeconds + pair.Join.InputWaitSeconds, Is.Zero);
                    Assert.That(pair.Host.WaitingForRemoteInput || pair.Join.WaitingForRemoteInput, Is.False);
                    pair.DriveTo(20); // Both actual-input/ack barriers complete in the new epoch.
                    Assert.That(pair.Host.Lifecycle.Phase, Is.EqualTo(LanLifecyclePhase.Playing));
                    Assert.That(pair.Join.Lifecycle.Phase, Is.EqualTo(LanLifecyclePhase.Playing));
                    Assert.That(hostEvents.Count, Is.EqualTo(eventCount), "No old event or Dash edge may leak into the new kickoff.");
                    Assert.That(pair.Host.State.Checksum(), Is.EqualTo(pair.Join.State.Checksum()));

                    Assert.That(pair.Host.RetainedResults.Count, Is.EqualTo(epoch + 1));
                    Assert.That(pair.Join.RetainedResults.Count, Is.EqualTo(epoch + 1));
                    for (int previous = 0; previous <= epoch; previous++)
                    {
                        foreach (DashLanSession session in new[] { pair.Host, pair.Join })
                        {
                            DashLanEpochEvidence evidence = session.RetainedResults[previous];
                            Assert.That(evidence.Epoch, Is.EqualTo(previous));
                            Assert.That(evidence.Seed, Is.EqualTo(seeds[previous]));
                            Assert.That(evidence.ContentHash, Is.EqualTo(session.Rules.ContentHash));
                            Assert.That(evidence.Finish.Equals(finishes[previous]), Is.True);
                        }
                    }
                }

                Assert.That(hostStarts.Count, Is.EqualTo(4)); Assert.That(joinStarts.Count, Is.EqualTo(4));
                Assert.That(hostSessions.Distinct().Count(), Is.EqualTo(4)); Assert.That(joinSessions.Distinct().Count(), Is.EqualTo(4));
                Assert.That(cleanHistories.Count, Is.EqualTo(8)); Assert.That(cleanHistories.All(clean => clean), Is.True);
                for (int epoch = 0; epoch < 4; epoch++)
                {
                    uint seed = epoch == 0 ? 123456u : (uint)(9000 + epoch);
                    var expected = DashArenaSimulation.Create(pair.Host.Rules, seed, epoch);
                    DashArenaSimulation.StartMatch(expected);
                    Assert.That(hostStarts[epoch].Checksum(), Is.EqualTo(expected.Checksum()), "Fresh complete authoritative state, including buffs/walls/PRNG/contact latches.");
                    Assert.That(joinStarts[epoch].Checksum(), Is.EqualTo(expected.Checksum()));
                }
                CollectionAssert.AreEqual(hostEvents, joinEvents);
                Assert.That(hostEvents.Select(e => e.Identity).Distinct().Count(), Is.EqualTo(hostEvents.Count));
                Assert.That(pair.Host.PresentationErrorCount + pair.Join.PresentationErrorCount, Is.Zero);
            }
        }

        static void CaptureFreshEpoch(DashLanSession session, List<DashArenaState> states,
            List<DashRollbackSession> sessions, List<bool> cleanHistories)
        {
            states.Add(session.State.Clone()); sessions.Add(session.Rollback);
            cleanHistories.Add(session.ConfirmedStateTick == -1 && session.Rollback.NewestRemoteTick == -1 &&
                session.Rollback.CopyLocalHistory(2, new byte[8]) == 0 &&
                !session.Rollback.TryGetConfirmedEvents(0, out _) && !session.WaitingForRemoteInput &&
                session.InputWaitCount == 0 && session.InputWaitSeconds == 0f);
        }

        static void SeedPreWinningGoalFixture(DashArenaState state)
        {
            state.Scores[0] = state.Rules.PointsToWin - 1;
            state.Scores[1] = 0;
            state.Balls[0] = new DashBallState { Active = true,
                Position = Vector2.down * (state.Rules.HalfHeight + .2f), Velocity = Vector2.down * 30f };
            state.Walls[0] = new DashWallState { Broken = true, RepairTicks = state.Rules.WallRepairTicks };
            state.Players[0].DashTicks = state.Players[1].DashTicks = 0;
            state.Players[0].DashCooldownTicks = state.Players[1].DashCooldownTicks = 150;
            state.Players[0].GreenTicks = state.Players[1].PurpleTicks = 240;
            state.NextPickupId = 99;
            ArenaRandom.Next(ref state.RandomState);
        }

        // Deliberate post-initialization, pre-next-tick mechanism fixture applied identically
        // to both peers. Not a natural fall/stun/buff/wall-break achievement claim.
        static void SeedDisabledMechanismFixture(DashArenaState state)
        {
            state.Players[0].Alive = false; state.Players[0].RespawnTicks = 40;
            state.Players[1].StunTicks = 50; state.Players[1].DashCooldownTicks = 180;
            state.Players[1].GreenTicks = 100; state.Players[1].PurpleTicks = 110;
            state.Walls[0] = new DashWallState { Broken = true, RepairTicks = 600 };
            state.SpawnTicks = 50;
        }

        static DashArenaInput Neutral(int epoch, int tick) => default;
        static DashArenaInput Input(Vector2 move, bool dash = false) => new DashArenaInput { Move = move, DashPressed = dash };
        static IPEndPoint Address(DashLanSession session) => new IPEndPoint(IPAddress.Loopback, session.LocalEndpoint.Port);
        static DashLifecycleMessage Heartbeat(DashLanSession sender) => new DashLifecycleMessage
        {
            Kind = NetLifecycleKind.Heartbeat, MatchId = sender.MatchId, Epoch = sender.Epoch,
            Seat = (byte)sender.LocalSeat, Seed = sender.Seed, ContentHash = sender.Rules.ContentHash
        };
        static byte[] Fault(DashLanSession sender, DashPeerFaultReason reason) => DashNetcode.EncodeFault(new DashPeerFaultMessage
        { MatchId = sender.MatchId, Epoch = sender.Epoch, Seat = (byte)sender.LocalSeat, Reason = reason });

        // Tests inject actual duplicate/control datagrams through the session's own bound
        // socket. This preserves endpoint checks without adding a production bypass API.
        static LanUdpPeer Peer(DashLanSession session) => (LanUdpPeer)typeof(DashLanSession)
            .GetField("_peer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);

        static byte[] LastInput(DashLanSession sender)
        {
            int newest = sender.Lifecycle.HighestSentTick;
            var history = new byte[DashNetcode.HistoryLength];
            int count = sender.Rollback.CopyLocalHistory(newest, history);
            Assert.That(count, Is.GreaterThan(0));
            return DashNetcode.EncodeInput(new NetInputPacket
            {
                MatchId = sender.MatchId, Epoch = sender.Epoch, Seat = (byte)sender.LocalSeat,
                NewestTick = newest, HistoryCount = (byte)count, InputBits = history,
                AckTick = sender.Rollback.ConfirmedTick, ChecksumTick = -1, Checksum = 0
            });
        }

        static void ExposeActualMiddleHole(Pair pair)
        {
            for (int i = 0; i < 4; i++) pair.Join.Advance(Neutral);
            Thread.Sleep(2);
            // Drop generated input datagrams before the host's receive path sees them.
            while (Peer(pair.Host).TryRecv(out _, out _)) { }
            Assert.That(DashNetcode.TryDecodeInput(LastInput(pair.Join), out NetInputPacket generated), Is.True);
            generated.HistoryCount = 1; // Only deliver the actual newest sample 225.
            Assert.That(Peer(pair.Join).Send(DashNetcode.EncodeInput(generated)), Is.True);
            Thread.Sleep(2); pair.Host.Pump();
            Assert.That(pair.Host.Rollback.ConfirmedTick, Is.EqualTo(221));
            Assert.That(pair.Host.Rollback.NewestRemoteTick, Is.EqualTo(225));
        }

        sealed class Pair : IDisposable
        {
            public float Now;
            public readonly DashLanSession Host, Join;
            public Pair(ArenaRulesProfile hostRules = null, ArenaRulesProfile joinRules = null, Func<uint> nextSeedFactory = null)
            {
                Host = DashLanSession.Host(hostRules ?? ArenaRulesProfile.CreateProductionV1(), 771, 123456, () => Now,
                    nextSeedFactory: nextSeedFactory);
                Assert.That(Host.SocketBound, Is.True, Host.LastSocketError);
                Join = DashLanSession.Join(joinRules ?? hostRules ?? ArenaRulesProfile.CreateProductionV1(), Address(Host), () => Now, 654321);
                Assert.That(Join.SocketBound, Is.True, Join.LastSocketError);
            }

            public void DriveTo(int tick, Func<int, int, DashArenaInput> hostInput = null, Func<int, int, DashArenaInput> joinInput = null)
            {
                int guard = 0;
                while ((Host.State == null || Host.State.Tick < tick || Join.State == null || Join.State.Tick < tick) && guard++ < tick * 5 + 200)
                {
                    if (Host.State == null || Host.State.Tick < tick) Host.Advance(hostInput ?? Neutral);
                    if (Join.State == null || Join.State.Tick < tick) Join.Advance(joinInput ?? Neutral);
                    Host.Pump(); Join.Pump();
                    Assert.That(Host.Frozen, Is.False, "Host " + Host.FailureReason);
                    Assert.That(Join.Frozen, Is.False, "Join " + Join.FailureReason);
                    Now += 1f / 60f;
                    Thread.Sleep(1);
                }
                Assert.That(Host.State?.Tick, Is.EqualTo(tick)); Assert.That(Join.State?.Tick, Is.EqualTo(tick));
                Drain();
            }

            public void PumpUntil(Func<bool> complete)
            {
                for (int i = 0; i < 200 && !complete(); i++)
                { Host.Pump(); Join.Pump(); Now += .02f; Thread.Sleep(1); }
                Assert.That(complete(), Is.True, "Host=" + Host.FailureReason + ", Join=" + Join.FailureReason);
            }

            public void Drain()
            {
                for (int i = 0; i < 4; i++) { Host.Pump(); Join.Pump(); Thread.Sleep(1); }
            }

            public void Hold(float seconds)
            {
                int steps = (int)Math.Ceiling(seconds / .1f);
                for (int i = 0; i < steps; i++)
                {
                    Now += .1f; Host.Pump(); Join.Pump(); Thread.Sleep(1);
                    Assert.That(Host.Frozen, Is.False, Host.FailureReason);
                    Assert.That(Join.Frozen, Is.False, Join.FailureReason);
                }
                Drain();
            }

            public void Dispose() { Join.Dispose(); Host.Dispose(); }
        }
    }
}
