using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using NUnit.Framework;

namespace PocketBump.Tests
{
    public sealed class DashLanLifecycleTests
    {
        const int MatchId = 1977;
        const uint InitialSeed = 0x01020304u;
        static readonly ArenaRulesProfile Rules = ArenaRulesProfile.CreateProductionV1();
        static readonly int SettingsHash = DashNetcode.ContentTag(Rules.ContentHash);

        static NetFinishTuple Finish(int checksum = 12345)
        {
            return new NetFinishTuple
            {
                FinishTick = 92, Checksum = checksum, Score0 = 8, Score1 = 5,
                Winner = 0, SettingsHash = SettingsHash
            };
        }

        static DashLifecycleMessage Message(NetLifecycleKind kind, int seat = 1, int epoch = 0)
        {
            return new DashLifecycleMessage
            {
                Kind = kind, MatchId = MatchId, Epoch = epoch,
                NextEpoch = kind >= NetLifecycleKind.Offer ? epoch + 1 : epoch,
                Seat = (byte)seat, Finish = Finish(), ContentHash = Rules.ContentHash,
                Seed = InitialSeed, NextSeed = kind >= NetLifecycleKind.Offer ? 9001u : 0u
            };
        }

        [Test]
        public void ResultNeedsMatchingPeerFinishAndAcknowledgementOfOurExactFinalTuple()
        {
            float now = 0f;
            var host = new DashLanLifecycle(true, MatchId, Rules, InitialSeed, () => now);
            host.UpdateTerminal(true, false, Finish());
            Assert.That(host.Phase, Is.EqualTo(LanLifecyclePhase.SettlingResult));
            Assert.That(host.Receive(Message(NetLifecycleKind.FinishAck)), Is.False);
            Assert.That(host.RequestRematch(), Is.False);
            Assert.That(host.Receive(Message(NetLifecycleKind.Finish)), Is.True);
            Assert.That(host.ResultConfirmed, Is.False);
            host.UpdateTerminal(true, true, Finish());
            Assert.That(host.ResultConfirmed, Is.False, "Remote input completeness is not terminal agreement.");
            var wrongAck = Message(NetLifecycleKind.FinishAck);
            wrongAck.Finish.FinishTick--;
            Assert.That(host.Receive(wrongAck), Is.False);
            Assert.That(host.Receive(Message(NetLifecycleKind.FinishAck)), Is.True);
            Assert.That(host.ResultConfirmed, Is.True);
            Assert.That(host.ConfirmedFinish.FinishTick, Is.EqualTo(92));
            Assert.That(host.ConfirmedFinish.Checksum, Is.EqualTo(12345));
        }

        [Test]
        public void PredictedFinishCanReturnToPlayWithoutPublishingAResult()
        {
            float now = 0f;
            var host = new DashLanLifecycle(true, MatchId, Rules, InitialSeed, () => now);
            host.UpdateTerminal(true, false, Finish());
            now = 3f;
            host.UpdateTerminal(false, false, default);
            Assert.That(host.Phase, Is.EqualTo(LanLifecyclePhase.Playing));
            Assert.That(host.CanAdvance(93), Is.True);
            Assert.That(host.ResultConfirmed, Is.False);
            Assert.That(host.RequestRematch(), Is.False);
            Assert.That(host.FailureReason, Is.Empty);
        }

        [Test]
        public void AheadPeerFinishWaitsForCorrespondingCorrectedLocalTerminalHistory()
        {
            float now = 0f;
            var host = new DashLanLifecycle(true, MatchId, Rules, InitialSeed, () => now);
            Assert.That(host.Receive(Message(NetLifecycleKind.Finish)), Is.True);
            Assert.That(host.Phase, Is.EqualTo(LanLifecyclePhase.Playing));
            host.UpdateTerminal(true, false, Finish(789));
            Assert.That(host.FailureReason, Is.Empty);
            host.UpdateTerminal(true, true, Finish(789));
            Assert.That(host.FailureReason, Is.EqualTo("DESYNC"));
        }

        [TestCase(false, "INPUT HOLE", 5f)]
        [TestCase(true, "RESULT CONFIRMATION TIMEOUT", 15f)]
        public void HeartbeatsCannotRenewTerminalProgressDeadline(bool complete, string reason, float deadline)
        {
            float now = 0f;
            var host = new DashLanLifecycle(true, MatchId, Rules, InitialSeed, () => now);
            host.UpdateTerminal(true, complete, Finish());
            for (int second = 0; second < deadline; second++)
            {
                now = second;
                Assert.That(host.Receive(Message(NetLifecycleKind.Heartbeat)), Is.True);
                host.Tick();
                Assert.That(host.FailureReason, Is.Empty);
            }
            now = deadline;
            host.Receive(Message(NetLifecycleKind.Heartbeat));
            host.Tick();
            Assert.That(host.FailureReason, Is.EqualTo(reason));
        }

        [Test]
        public void ConfirmedResultsDwellForEightSecondsAndSingleConsentWaitsIndefinitely()
        {
            using (var pair = new Pair(false))
            {
                pair.ConfirmResults();
                pair.Run(9f);
                Assert.That(pair.Host.ResultConfirmed && pair.Join.ResultConfirmed, Is.True);
                Assert.That(pair.Host.RequestRematch(), Is.True);
                Assert.That(pair.Host.RequestRematch(), Is.True);
                pair.Run(20f);
                Assert.That(pair.Host.Phase, Is.EqualTo(LanLifecyclePhase.WaitingForPeerChoice));
                Assert.That(pair.Join.Phase, Is.EqualTo(LanLifecyclePhase.ResultsReady));
                Assert.That(pair.Host.Epoch, Is.Zero);
                Assert.That(pair.Join.Epoch, Is.Zero);
                Assert.That(pair.Join.RemoteConsent, Is.True);
                pair.AssertHealthy();
            }
        }

        [Test]
        public void ReceiveDrainBeforeTimeoutKeepsAnActuallyLivePeerConnected()
        {
            float now = 0f;
            var host = new DashLanLifecycle(true, MatchId, Rules, InitialSeed, () => now);
            now = 5.1f;
            Assert.That(host.Receive(Message(NetLifecycleKind.Heartbeat)), Is.True);
            host.Tick();
            Assert.That(host.FailureReason, Is.Empty);
            now = 10.2f;
            host.Tick();
            Assert.That(host.FailureReason, Is.EqualTo("PEER TIMEOUT"));
        }

        [Test]
        public void InvalidIdentitySeatEpochAndOutOfPhaseControlsDoNotRefreshLiveness()
        {
            float now = 0f;
            var host = new DashLanLifecycle(true, MatchId, Rules, InitialSeed, () => now);
            now = 4f;
            var wrong = Message(NetLifecycleKind.Heartbeat);
            wrong.MatchId++;
            Assert.That(host.Receive(wrong), Is.False);
            Assert.That(host.Receive(Message(NetLifecycleKind.Heartbeat, 0)), Is.False);
            Assert.That(host.Receive(Message(NetLifecycleKind.Heartbeat, 1, 1)), Is.False);
            Assert.That(host.Receive(Message(NetLifecycleKind.RematchIntent)), Is.False);
            var future = Message(NetLifecycleKind.Commit);
            future.NextEpoch = 1;
            Assert.That(host.Receive(future), Is.False);
            Assert.That(host.LastAcceptedPeerTime, Is.Zero);
            now = 5f;
            host.Tick();
            Assert.That(host.FailureReason, Is.EqualTo("PEER TIMEOUT"));
        }

        [Test]
        public void StartupNeedsDistinctSecondInputAndAckNotRepeatedFirstSample()
        {
            using (var pair = new Pair(false))
            {
                pair.ConfirmResults();
                pair.RequestBoth();
                pair.RunUntil(() => pair.Host.Epoch == 1 && pair.Join.Epoch == 1 &&
                    pair.Host.CanAdvance(0) && pair.Join.CanAdvance(0), 3f, false);
                var host = pair.Host;
                Assert.That(host.CanAdvance(0), Is.True);
                Assert.That(host.CanAdvance(1), Is.False);
                host.RecordSentInput(2);
                Assert.That(host.CanAcceptInput(MatchId, 1, 1, 3), Is.False,
                    "A future acknowledgement cannot unlock the barrier.");
                for (int i = 0; i < 30; i++)
                    Assert.That(host.AcceptInput(MatchId, 1, 1, 2, 2, 2), Is.True);
                Assert.That(host.CanAdvance(1), Is.True);
                Assert.That(host.CanAdvance(2), Is.False);
                Assert.That(host.Phase, Is.EqualTo(LanLifecyclePhase.StartupBarrier));
                host.RecordSentInput(3);
                Assert.That(host.AcceptInput(MatchId, 1, 1, 3, 2, 3), Is.True);
                Assert.That(host.CanAdvance(2), Is.False);
                Assert.That(host.AcceptInput(MatchId, 1, 1, 2, 1, 3), Is.True,
                    "Out-of-order acknowledgement must not discard useful input history.");
                Assert.That(host.LastPeerAck, Is.EqualTo(2));
                Assert.That(host.AcceptInput(MatchId, 1, 1, 3, 3, 3), Is.True);
                Assert.That(host.Phase, Is.EqualTo(LanLifecyclePhase.Playing));
                Assert.That(host.CanAdvance(2), Is.True);
            }
        }

        [Test]
        public void LiveHeartbeatsCannotRenewAStalledRematchDeadline()
        {
            using (var pair = new Pair(false))
            {
                pair.ConfirmResults();
                pair.SuppressPrepared = true;
                pair.RequestBoth();
                pair.Run(16f, false, false);
                Assert.That(pair.Host.FailureReason, Is.EqualTo("REMATCH TIMEOUT"));
                Assert.That(pair.Join.FailureReason, Is.EqualTo("REMATCH TIMEOUT"));
                Assert.That(pair.Host.Epoch, Is.Zero);
            }
        }

        [Test]
        public void OldEpochTrafficCannotChangeOrKeepNewEpochAlive()
        {
            using (var pair = new Pair(false))
            {
                pair.ConfirmResults();
                pair.RequestBoth();
                pair.RunUntil(() => pair.BothPlayingIn(1), 4f);
                float accepted = pair.Host.LastAcceptedPeerTime;
                pair.Now += 1f;
                foreach (NetLifecycleKind kind in new[]
                {
                    NetLifecycleKind.Heartbeat, NetLifecycleKind.Finish, NetLifecycleKind.FinishAck,
                    NetLifecycleKind.RematchIntent, NetLifecycleKind.CommitAck
                })
                    Assert.That(pair.Host.Receive(Message(kind)), Is.False, kind.ToString());
                Assert.That(pair.Host.ReceiveGoodbye(MatchId, 0), Is.False);
                Assert.That(pair.Host.CanAcceptInput(MatchId, 0, 1, 1), Is.False);
                Assert.That(pair.Host.CanAcceptInput(MatchId, 2, 1, 1), Is.False);
                Assert.That(pair.Host.LastAcceptedPeerTime, Is.EqualTo(accepted));
                Assert.That(pair.Host.Epoch, Is.EqualTo(1));
                Assert.That(pair.Host.FailureReason, Is.Empty);
            }
        }

        [Test]
        public void CurrentGoodbyeTerminatesWaitingButFreshConnectionRejectsIt()
        {
            using (var pair = new Pair(false))
            {
                pair.ConfirmResults();
                pair.Host.RequestRematch();
                Assert.That(pair.Host.ReceiveGoodbye(MatchId, 0), Is.True);
                Assert.That(pair.Host.FailureReason, Is.EqualTo("PEER LEFT"));
                var fresh = new DashLanLifecycle(true, MatchId + 1, Rules, InitialSeed, () => pair.Now);
                Assert.That(fresh.ReceiveGoodbye(MatchId, 0), Is.False);
                Assert.That(fresh.Phase, Is.EqualTo(LanLifecyclePhase.Playing));
            }
        }

        [Test]
        public void CommittedTransitionDuplicatesReplyWithoutResetOrCurrentEpochLiveness()
        {
            using (var pair = new Pair(false))
            {
                pair.ConfirmResults();
                pair.RequestBoth();
                pair.RunUntil(() => pair.Host.Epoch == 1 && pair.Join.Epoch == 1 &&
                    pair.Host.CanAdvance(0) && pair.Join.CanAdvance(0), 3f, false);
                float accepted = pair.Host.LastAcceptedPeerTime;
                pair.Now += 1f;
                var prepared = Message(NetLifecycleKind.Prepared);
                prepared.NextEpoch = 1;
                Assert.That(pair.Host.Receive(prepared), Is.True);
                Assert.That(pair.Host.TryTakePreparedEpoch(out _, out _), Is.False);
                Assert.That(pair.Host.Epoch, Is.EqualTo(1));
                Assert.That(pair.Host.LastAcceptedPeerTime, Is.EqualTo(accepted));
                bool replied = false;
                while (pair.Host.TryDequeue(out DashLifecycleMessage response))
                    replied |= response.Kind == NetLifecycleKind.Commit;
                Assert.That(replied, Is.True);
                prepared.NextEpoch = 2;
                Assert.That(pair.Host.Receive(prepared), Is.False);
                Assert.That(pair.Host.Epoch, Is.EqualTo(1));
            }
        }

        [TestCase(NetLifecycleKind.Finish)]
        [TestCase(NetLifecycleKind.FinishAck)]
        [TestCase(NetLifecycleKind.RematchIntent)]
        [TestCase(NetLifecycleKind.RematchIntentAck)]
        [TestCase(NetLifecycleKind.Offer)]
        [TestCase(NetLifecycleKind.Prepared)]
        [TestCase(NetLifecycleKind.Commit)]
        [TestCase(NetLifecycleKind.CommitAck)]
        [TestCase(NetLifecycleKind.Start)]
        public void TwoSocketsRecoverFirstLossOfEachLifecyclePhase(NetLifecycleKind droppedKind)
        {
            using (var pair = new Pair(true) { DropFirstKind = droppedKind, DuplicateControls = true })
            {
                pair.ConfirmResults();
                pair.RequestBoth();
                pair.RunUntil(() => pair.BothPlayingIn(1), 8f);
                Assert.That(pair.DroppedControls, Is.GreaterThan(0));
                Assert.That(pair.HostResets, Is.EqualTo(1));
                Assert.That(pair.JoinResets, Is.EqualTo(1));
                pair.AssertStartupEvidence();
            }
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void TwoSocketsRecoverLostFirstInputOrAcknowledgement(bool dropInput, bool dropAck)
        {
            using (var pair = new Pair(true) { DropFirstInput = dropInput, DropFirstAck = dropAck })
            {
                pair.ConfirmResults();
                pair.RequestBoth();
                pair.RunUntil(() => pair.BothPlayingIn(1), 8f);
                Assert.That(pair.HostResets, Is.EqualTo(1));
                Assert.That(pair.JoinResets, Is.EqualTo(1));
                if (dropInput) Assert.That(pair.DroppedInputs, Is.GreaterThan(0));
                if (dropAck) Assert.That(pair.DroppedAcks, Is.GreaterThan(0));
                pair.AssertStartupEvidence();
            }
        }

        [Test]
        public void TwoSocketsRepeatThreeRematchesWithFreshStateAndConfirmedChecksums()
        {
            using (var pair = new Pair(true) { DuplicateControls = true, ReorderControls = true })
            {
                for (int epoch = 1; epoch <= 3; epoch++)
                {
                    pair.ConfirmResults();
                    pair.RequestBoth();
                    int expected = epoch;
                    pair.RunUntil(() => pair.BothPlayingIn(expected), 8f);
                    pair.AssertStartupEvidence();
                    Assert.That(pair.HostResets, Is.EqualTo(epoch));
                    Assert.That(pair.JoinResets, Is.EqualTo(epoch));
                    Assert.That(pair.HostSeedCalls, Is.EqualTo(epoch), "Retries must not draw another gameplay seed.");
                    Assert.That(pair.JoinSeedCalls, Is.Zero, "Only the host proposes seeds.");
                    Assert.That(pair.Host.Seed, Is.EqualTo(9000u + (uint)epoch));
                    Assert.That(pair.Join.Seed, Is.EqualTo(pair.Host.Seed));
                }
                Assert.That(pair.Host.Epoch, Is.EqualTo(3));
                Assert.That(pair.Join.Epoch, Is.EqualTo(3));
            }
        }

        [Test]
        public void InitialStartupHeartbeatCannotRenewTheHandshakeProgressDeadline()
        {
            float now = 0f;
            var host = new DashLanLifecycle(true, MatchId, Rules, InitialSeed, () => now, initialStartup: true);
            Assert.That(host.Phase, Is.EqualTo(LanLifecyclePhase.StartupBarrier));
            Assert.That(host.CanAdvance(0), Is.True);
            Assert.That(host.CanAdvance(1), Is.False);
            for (int second = 0; second < 15; second++)
            {
                now = second;
                host.Receive(Message(NetLifecycleKind.Heartbeat));
                host.Tick();
                Assert.That(host.FailureReason, Is.Empty);
            }
            now = 15f;
            host.Receive(Message(NetLifecycleKind.Heartbeat));
            host.Tick();
            Assert.That(host.FailureReason, Is.EqualTo("LAN HANDSHAKE TIMEOUT"));
        }

        [Test]
        public void ActiveGameplayHeartbeatsCannotSupplyMissingRollbackInput()
        {
            float now = 0f;
            var lifecycle = new DashLanLifecycle(true, MatchId, Rules, InitialSeed, () => now);
            DashArenaState state = DashArenaSimulation.Create(Rules, InitialSeed);
            DashArenaSimulation.StartMatch(state);
            var rollback = new DashRollbackSession(state, 0);
            for (int frame = 0; frame <= DashRollbackSession.DelayTicks + DashRollbackSession.BufferTicks; frame++)
            {
                now = frame / 60f;
                Assert.That(lifecycle.Receive(Message(NetLifecycleKind.Heartbeat)), Is.True);
                lifecycle.Tick();
                Assert.That(lifecycle.CanAdvance(state.Tick), Is.True);
                rollback.Advance(DashArenaInput.Neutral);
                if (rollback.Status == RollbackStatus.InputHole) break;
            }
            Assert.That(lifecycle.LastAcceptedPeerTime, Is.EqualTo(now));
            Assert.That(lifecycle.FailureReason, Is.Empty, "The peer is live, but its required gameplay history is missing.");
            Assert.That(rollback.ConfirmedTick, Is.EqualTo(DashRollbackSession.DelayTicks - 1));
            Assert.That(rollback.Status, Is.EqualTo(RollbackStatus.InputHole));
        }

        [TestCase(NetLifecycleKind.Offer)]
        [TestCase(NetLifecycleKind.Prepared)]
        [TestCase(NetLifecycleKind.Commit)]
        [TestCase(NetLifecycleKind.CommitAck)]
        [TestCase(NetLifecycleKind.Start)]
        public void EveryTransitionBindsOldSeedNewSeedFullHashAndFinishWithoutRenewingLiveness(NetLifecycleKind kind)
        {
            using (var pair = new Pair(false) { SuppressPrepared = true })
            {
                pair.ConfirmResults();
                pair.RequestBoth();
                pair.RunUntil(() => pair.Host.Phase == LanLifecyclePhase.PreparingNextEpoch &&
                    pair.Join.Phase == LanLifecyclePhase.PreparingNextEpoch, 3f, false);
                if (kind >= NetLifecycleKind.Commit)
                    Assert.That(pair.Host.Receive(Message(NetLifecycleKind.Prepared)), Is.True);
                if (kind >= NetLifecycleKind.CommitAck)
                    Assert.That(pair.Join.Receive(Message(NetLifecycleKind.Commit, seat: 0)), Is.True);
                bool toHost = kind == NetLifecycleKind.Prepared || kind == NetLifecycleKind.CommitAck;
                DashLanLifecycle receiver = toHost ? pair.Host : pair.Join;
                DashLifecycleMessage correct = Message(kind, seat: toHost ? 1 : 0);
                float accepted = receiver.LastAcceptedPeerTime;
                int epoch = receiver.Epoch;
                uint seed = receiver.Seed;
                pair.Now += 1f;

                var wrong = correct;
                wrong.Seed++;
                Assert.That(receiver.Receive(RoundTrip(wrong)), Is.False, "Old seed must remain bound after commit.");
                wrong = correct; wrong.NextSeed++;
                Assert.That(receiver.Receive(RoundTrip(wrong)), Is.False, "A retry cannot change the proposed seed.");
                wrong = correct; wrong.Finish.Checksum++;
                Assert.That(receiver.Receive(RoundTrip(wrong)), Is.False, "Transition must retain the agreed result.");
                wrong = correct; wrong.Epoch++; wrong.NextEpoch++;
                Assert.That(receiver.Receive(RoundTrip(wrong)), Is.False, "Future epochs cannot skip the transaction.");
                wrong = correct;
                wrong.ContentHash = SameTagDifferentHash();
                Assert.That(DashNetcode.ContentTag(wrong.ContentHash), Is.EqualTo(SettingsHash));
                Assert.That(receiver.Receive(RoundTrip(wrong)), Is.False,
                    "The legacy 32-bit finish tag cannot substitute for full content identity.");
                wrong = correct; wrong.NextSeed = 0;
                Assert.That(receiver.Receive(wrong), Is.False, "Direct callers cannot introduce a zero proposal either.");

                Assert.That(receiver.LastAcceptedPeerTime, Is.EqualTo(accepted));
                Assert.That(receiver.Epoch, Is.EqualTo(epoch));
                Assert.That(receiver.Seed, Is.EqualTo(seed));
                Assert.That(receiver.FailureReason, Is.Empty);
                Assert.That(receiver.Receive(RoundTrip(correct)), Is.True, "Original immutable proposal must remain acceptable.");
                Assert.That(pair.HostSeedCalls, Is.EqualTo(1));
                Assert.That(pair.JoinSeedCalls, Is.Zero);
            }
        }

        [TestCase(NetLifecycleKind.Heartbeat)]
        [TestCase(NetLifecycleKind.Finish)]
        [TestCase(NetLifecycleKind.FinishAck)]
        [TestCase(NetLifecycleKind.RematchIntent)]
        [TestCase(NetLifecycleKind.RematchIntentAck)]
        public void CurrentControlsRequireCurrentSeedAndCompleteContentHash(NetLifecycleKind kind)
        {
            float now = 0f;
            var host = new DashLanLifecycle(true, MatchId, Rules, InitialSeed, () => now);
            host.UpdateTerminal(true, true, Finish());
            Assert.That(host.Receive(Message(NetLifecycleKind.Finish)), Is.True);
            Assert.That(host.Receive(Message(NetLifecycleKind.FinishAck)), Is.True);
            Assert.That(host.RequestRematch(), Is.True);
            now = 4f;
            var wrong = Message(kind); wrong.Seed++;
            Assert.That(host.Receive(RoundTrip(wrong)), Is.False);
            wrong = Message(kind); wrong.ContentHash = SameTagDifferentHash();
            Assert.That(host.Receive(RoundTrip(wrong)), Is.False);
            Assert.That(host.LastAcceptedPeerTime, Is.Zero);
            Assert.That(host.Seed, Is.EqualTo(InitialSeed));
            now = 5f; host.Tick();
            Assert.That(host.FailureReason, Is.EqualTo("PEER TIMEOUT"));
        }

        [Test]
        public void OfferRetriesReuseExactlyOneHostSeedAndPreparedEpochReturnsSeedExactlyOnce()
        {
            using (var pair = new Pair(false) { SuppressPrepared = true })
            {
                pair.ConfirmResults();
                Assert.That(pair.HostSeedCalls, Is.Zero);
                Assert.That(pair.Host.RequestRematch(), Is.True);
                pair.Run(1f, false);
                Assert.That(pair.HostSeedCalls, Is.Zero, "One user's consent cannot sample a next-match seed.");
                Assert.That(pair.Join.RequestRematch(), Is.True);
                pair.Run(2f, false);
                Assert.That(pair.HostSeedCalls, Is.EqualTo(1));
                Assert.That(pair.JoinSeedCalls, Is.Zero);
                Assert.That(pair.Host.Seed, Is.EqualTo(InitialSeed));
                Assert.That(pair.Join.Seed, Is.EqualTo(InitialSeed));
                int offers = 0;
                foreach (DashLifecycleMessage message in pair.SentControls)
                {
                    if (message.Kind != NetLifecycleKind.Offer) continue;
                    offers++;
                    Assert.That(message.Seed, Is.EqualTo(InitialSeed));
                    Assert.That(message.NextSeed, Is.EqualTo(9001u));
                    Assert.That(message.ContentHash, Is.EqualTo(Rules.ContentHash));
                }
                Assert.That(offers, Is.GreaterThan(1), "This fixture must actually exercise retries.");
                Assert.That(pair.Host.Receive(Message(NetLifecycleKind.Prepared)), Is.True);
                Assert.That(pair.Host.TryTakePreparedEpoch(out int epoch, out uint seed), Is.True);
                Assert.That(epoch, Is.EqualTo(1)); Assert.That(seed, Is.EqualTo(9001u));
                Assert.That(pair.Host.Seed, Is.EqualTo(seed));
                Assert.That(pair.Host.TryTakePreparedEpoch(out epoch, out seed), Is.False);
                Assert.That(epoch, Is.EqualTo(-1)); Assert.That(seed, Is.Zero);
                Assert.That(pair.Host.Receive(Message(NetLifecycleKind.Prepared)), Is.True);
                Assert.That(pair.Host.TryTakePreparedEpoch(out _, out _), Is.False);
                Assert.That(pair.HostSeedCalls, Is.EqualTo(1));
            }
        }

        [TestCase(false, "INVALID REMATCH SEED")]
        [TestCase(true, "REMATCH SEED ERROR")]
        public void InvalidSeedFactoryFailsWithoutRerollingOrPreparingAnyEpoch(bool throws, string reason)
        {
            float now = 0f;
            int calls = 0;
            var host = new DashLanLifecycle(true, MatchId, Rules, InitialSeed, () => now,
                () => { calls++; if (throws) throw new InvalidOperationException("fixture"); return 0; });
            host.UpdateTerminal(true, true, Finish());
            host.Receive(Message(NetLifecycleKind.Finish));
            host.Receive(Message(NetLifecycleKind.FinishAck));
            host.RequestRematch();
            host.Receive(Message(NetLifecycleKind.RematchIntent));
            Assert.That(host.FailureReason, Is.EqualTo(reason));
            Assert.That(host.Epoch, Is.Zero);
            Assert.That(host.Seed, Is.EqualTo(InitialSeed));
            Assert.That(host.TryTakePreparedEpoch(out _, out _), Is.False);
            now = 1f; host.Tick();
            host.RequestRematch();
            host.Receive(Message(NetLifecycleKind.RematchIntent));
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void InitializationRequiresExplicitNonzeroSeedAndDefaultOfferSuppliesOne()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DashLanLifecycle(true, MatchId, Rules, 0, () => 0f));
            Assert.Throws<ArgumentNullException>(() => new DashLanLifecycle(true, MatchId, null, InitialSeed, () => 0f));
            var host = new DashLanLifecycle(true, MatchId, Rules, InitialSeed, () => 0f);
            host.UpdateTerminal(true, true, Finish());
            host.Receive(Message(NetLifecycleKind.Finish));
            host.Receive(Message(NetLifecycleKind.FinishAck));
            host.RequestRematch();
            host.Receive(Message(NetLifecycleKind.RematchIntent));
            bool offered = false;
            while (host.TryDequeue(out DashLifecycleMessage message))
                if (message.Kind == NetLifecycleKind.Offer)
                {
                    offered = true;
                    Assert.That(message.NextSeed, Is.Not.Zero);
                    Assert.That(message.Seed, Is.EqualTo(InitialSeed));
                    Assert.That(RoundTrip(message).NextSeed, Is.EqualTo(message.NextSeed));
                }
            Assert.That(offered, Is.True);
        }

        static DashLifecycleMessage RoundTrip(DashLifecycleMessage message)
        {
            Assert.That(DashNetcode.TryDecodeLifecycle(DashNetcode.EncodeLifecycle(message), out DashLifecycleMessage decoded), Is.True);
            return decoded;
        }

        static string SameTagDifferentHash()
        {
            string hash = Rules.ContentHash;
            return hash.Substring(0, 63) + (hash[63] == '0' ? "1" : "0");
        }

        // These tests drive real UDP codecs and real freshly initialized rollback sessions.
        // The old match's terminal tuple is synthetic coordinator evidence, not a natural win.
        sealed class Pair : IDisposable
        {
            public float Now;
            public readonly DashLanLifecycle Host;
            public readonly DashLanLifecycle Join;
            readonly LanUdpPeer _hostSocket;
            readonly LanUdpPeer _joinSocket;
            readonly HashSet<string> _dropped = new HashSet<string>();
            readonly List<DashLifecycleMessage> _heldHost = new List<DashLifecycleMessage>();
            readonly List<DashLifecycleMessage> _heldJoin = new List<DashLifecycleMessage>();
            DashRollbackSession _hostRollback;
            DashRollbackSession _joinRollback;
            public NetLifecycleKind DropFirstKind;
            public bool DropFirstInput;
            public bool DropFirstAck;
            public bool DuplicateControls;
            public bool ReorderControls;
            public bool SuppressPrepared;
            public int DroppedControls;
            public int DroppedInputs;
            public int DroppedAcks;
            public int HostResets;
            public int JoinResets;
            public int HostSeedCalls;
            public int JoinSeedCalls;
            public readonly List<DashLifecycleMessage> SentControls = new List<DashLifecycleMessage>();

            public Pair(bool sockets)
            {
                Host = new DashLanLifecycle(true, MatchId, Rules, InitialSeed, () => Now,
                    () => 9000u + (uint)++HostSeedCalls);
                Join = new DashLanLifecycle(false, MatchId, Rules, InitialSeed, () => Now,
                    () => { JoinSeedCalls++; return 0xdeadbeefu; });
                if (!sockets) return;
                _hostSocket = new LanUdpPeer();
                _joinSocket = new LanUdpPeer();
                Assert.That(_hostSocket.BindEphemeral(), Is.True);
                Assert.That(_joinSocket.BindEphemeral(), Is.True);
                _hostSocket.SetRemote(new IPEndPoint(IPAddress.Loopback, _joinSocket.LocalEndPoint.Port));
                _joinSocket.SetRemote(new IPEndPoint(IPAddress.Loopback, _hostSocket.LocalEndPoint.Port));
            }

            public void ConfirmResults()
            {
                Host.UpdateTerminal(true, true, Finish());
                Join.UpdateTerminal(true, true, Finish());
                RunUntil(() => Host.ResultConfirmed && Join.ResultConfirmed, 5f, false);
            }

            public void RequestBoth()
            {
                Assert.That(Host.RequestRematch(), Is.True);
                Assert.That(Join.RequestRematch(), Is.True);
            }

            public bool BothPlayingIn(int epoch)
            {
                return Host.Epoch == epoch && Join.Epoch == epoch &&
                       Host.Phase == LanLifecyclePhase.Playing && Join.Phase == LanLifecyclePhase.Playing;
            }

            public void Run(float seconds, bool input = true, bool healthy = true)
            {
                float until = Now + seconds;
                while (Now < until) Step(input, healthy);
            }

            public void RunUntil(Func<bool> done, float seconds, bool input = true)
            {
                float until = Now + seconds;
                while (!done() && Now < until) Step(input, true);
                Assert.That(done(), Is.True, "Timed out at " + Host.Phase + " / " + Join.Phase);
            }

            void Step(bool input, bool healthy)
            {
                // Drain before evaluating timeout, matching the production integration contract.
                Drain(_hostSocket, Host, ref _hostRollback, ref HostResets);
                Drain(_joinSocket, Join, ref _joinRollback, ref JoinResets);
                Host.Tick();
                Join.Tick();
                Flush(Host, Join, _hostSocket, _heldHost);
                Flush(Join, Host, _joinSocket, _heldJoin);
                Prepare(Host, ref _hostRollback, ref HostResets);
                Prepare(Join, ref _joinRollback, ref JoinResets);
                if (input)
                {
                    Advance(Host, _hostRollback);
                    Advance(Join, _joinRollback);
                    SendInput(Host, Join, _hostRollback, _joinRollback, _hostSocket);
                    SendInput(Join, Host, _joinRollback, _hostRollback, _joinSocket);
                }
                if (_hostSocket != null) Thread.Sleep(1);
                Drain(_hostSocket, Host, ref _hostRollback, ref HostResets);
                Drain(_joinSocket, Join, ref _joinRollback, ref JoinResets);
                if (healthy) AssertHealthy();
                Now += 0.05f;
            }

            void Flush(DashLanLifecycle source, DashLanLifecycle target, LanUdpPeer socket,
                List<DashLifecycleMessage> held)
            {
                var messages = new List<DashLifecycleMessage>();
                while (source.TryDequeue(out DashLifecycleMessage message)) messages.Add(message);
                if (ReorderControls) messages.Reverse();
                messages.AddRange(held);
                held.Clear();
                foreach (DashLifecycleMessage message in messages)
                {
                    SentControls.Add(message);
                    if (SuppressPrepared && message.Kind == NetLifecycleKind.Prepared) continue;
                    if (message.Kind == DropFirstKind && _dropped.Add("control:" + message.Seat + ":" + message.Kind))
                    {
                        DroppedControls++;
                        continue;
                    }
                    if (ReorderControls && message.Kind != NetLifecycleKind.Heartbeat &&
                        _dropped.Add("hold:" + message.Seat + ":" + message.Epoch + ":" + message.Kind))
                    {
                        held.Add(message);
                        continue;
                    }
                    byte[] bytes = DashNetcode.EncodeLifecycle(message);
                    if (socket != null)
                    {
                        Assert.That(socket.Send(bytes), Is.True);
                        if (DuplicateControls) Assert.That(socket.Send(bytes), Is.True);
                    }
                    else
                    {
                        Assert.That(DashNetcode.TryDecodeLifecycle(bytes, out DashLifecycleMessage decoded), Is.True);
                        target.Receive(decoded);
                        if (DuplicateControls) target.Receive(decoded);
                    }
                }
            }

            static void Prepare(DashLanLifecycle lifecycle, ref DashRollbackSession rollback, ref int resets)
            {
                if (!lifecycle.TryTakePreparedEpoch(out int epoch, out uint seed)) return;
                Assert.That(epoch, Is.EqualTo(lifecycle.Epoch));
                Assert.That(seed, Is.EqualTo(lifecycle.Seed));
                DashArenaState state = DashArenaSimulation.Create(Rules, seed, epoch);
                Assert.That(state.InitialSeed, Is.EqualTo(seed));
                Assert.That(state.RandomState, Is.EqualTo(seed));
                Assert.That(state.Epoch, Is.EqualTo(epoch));
                DashArenaSimulation.StartMatch(state);
                Assert.That(state.Tick, Is.Zero);
                Assert.That(state.Scores[0], Is.Zero);
                Assert.That(state.Scores[1], Is.Zero);
                rollback = new DashRollbackSession(state, lifecycle.LocalSeat);
                resets++;
                Assert.That(lifecycle.TryTakePreparedEpoch(out _, out _), Is.False);
            }

            static void Advance(DashLanLifecycle lifecycle, DashRollbackSession rollback)
            {
                if (rollback == null || !lifecycle.CanAdvance(rollback.State.Tick)) return;
                rollback.Advance(DashArenaInput.Neutral);
                Assert.That(rollback.Status, Is.EqualTo(RollbackStatus.Ok));
                if (lifecycle.Phase == LanLifecyclePhase.StartupBarrier)
                    Assert.That(rollback.State.Tick, Is.LessThanOrEqualTo(2));
            }

            void SendInput(DashLanLifecycle source, DashLanLifecycle target, DashRollbackSession rollback,
                DashRollbackSession receiver, LanUdpPeer socket)
            {
                if (rollback == null || rollback.State.Tick == 0) return;
                int newest = rollback.State.Tick - 1 + DashRollbackSession.DelayTicks;
                var history = new byte[DashNetcode.HistoryLength];
                int count = rollback.CopyLocalHistory(newest, history);
                if (count == 0) return;
                rollback.TryGetConfirmedChecksum(rollback.State.Tick - 1, out int checksumTick, out int checksum);
                var packet = new NetInputPacket
                {
                    MatchId = MatchId, Epoch = source.Epoch, Seat = (byte)source.LocalSeat,
                    NewestTick = newest, HistoryCount = (byte)count, InputBits = history,
                    AckTick = rollback.ConfirmedTick, ChecksumTick = checksumTick, Checksum = checksum
                };
                source.RecordSentInput(newest);
                if (DropFirstInput && _dropped.Add("input:" + source.LocalSeat)) { DroppedInputs++; return; }
                if (DropFirstAck && packet.AckTick >= DashRollbackSession.DelayTicks &&
                    _dropped.Add("ack:" + source.LocalSeat)) { DroppedAcks++; return; }
                byte[] bytes = DashNetcode.EncodeInput(packet);
                if (socket != null) Assert.That(socket.Send(bytes), Is.True);
                else if (DashNetcode.TryDecodeInput(bytes, out NetInputPacket decoded))
                    ReceiveInput(target, receiver, decoded);
            }

            static void ReceiveInput(DashLanLifecycle lifecycle, DashRollbackSession rollback, NetInputPacket packet)
            {
                if (rollback == null || !lifecycle.CanAcceptInput(packet.MatchId, packet.Epoch, packet.Seat, packet.AckTick))
                    return;
                if (rollback.ReceiveRemoteHistory(packet.NewestTick, packet.InputBits, packet.HistoryCount) <= 0) return;
                lifecycle.AcceptInput(packet.MatchId, packet.Epoch, packet.Seat, packet.NewestTick,
                    packet.AckTick, rollback.ConfirmedTick);
                if (packet.ChecksumTick >= 0 && packet.ChecksumTick <= packet.AckTick)
                    rollback.QueueRemoteChecksum(packet.ChecksumTick, packet.Checksum);
            }

            static void Drain(LanUdpPeer socket, DashLanLifecycle lifecycle,
                ref DashRollbackSession rollback, ref int resets)
            {
                if (socket == null) return;
                while (socket.TryRecv(out byte[] bytes, out IPEndPoint from))
                {
                    if (!socket.IsRemote(from)) continue;
                    if (DashNetcode.TryDecodeLifecycle(bytes, out DashLifecycleMessage control))
                    {
                        lifecycle.Receive(control);
                        Prepare(lifecycle, ref rollback, ref resets);
                    }
                    else if (DashNetcode.TryDecodeInput(bytes, out NetInputPacket packet))
                        ReceiveInput(lifecycle, rollback, packet);
                }
            }

            public void AssertStartupEvidence()
            {
                // Run beyond the barrier so queued real checksums are consumed by normal Advance.
                Run(0.5f);
                Assert.That(_hostRollback.State.Scores[0], Is.Zero);
                Assert.That(_hostRollback.State.Scores[1], Is.Zero);
                Assert.That(_joinRollback.State.Scores[0], Is.Zero);
                Assert.That(_joinRollback.State.Scores[1], Is.Zero);
                Assert.That(_hostRollback.State.InitialSeed, Is.EqualTo(Host.Seed));
                Assert.That(_joinRollback.State.InitialSeed, Is.EqualTo(Join.Seed));
                Assert.That(_hostRollback.State.Epoch, Is.EqualTo(Host.Epoch));
                Assert.That(_joinRollback.State.Epoch, Is.EqualTo(Join.Epoch));
                Assert.That(Host.LastPeerAck, Is.GreaterThan(3));
                Assert.That(Join.LastPeerAck, Is.GreaterThan(3));
                Assert.That(_hostRollback.ValidatedChecksumCount, Is.GreaterThan(0));
                Assert.That(_joinRollback.ValidatedChecksumCount, Is.GreaterThan(0));
                int common = Math.Min(_hostRollback.State.Tick, _joinRollback.State.Tick) - 2;
                Assert.That(_hostRollback.ChecksumAt(common), Is.EqualTo(_joinRollback.ChecksumAt(common)));
                AssertHealthy();
            }

            public void AssertHealthy()
            {
                Assert.That(Host.FailureReason, Is.Empty, "Host failed at " + Now);
                Assert.That(Join.FailureReason, Is.Empty, "Join failed at " + Now);
            }

            public void Dispose()
            {
                _hostSocket?.Dispose();
                _joinSocket?.Dispose();
            }
        }
    }
}
