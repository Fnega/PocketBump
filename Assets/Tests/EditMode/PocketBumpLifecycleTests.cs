using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using NUnit.Framework;

namespace PocketBump.Tests
{
    public sealed class PocketBumpLifecycleTests
    {
        const int MatchId = 1977;
        static readonly int SettingsHash = NetplayProtocol.SettingsHash(8, 1);

        static NetFinishTuple Finish(int checksum = 12345)
        {
            return new NetFinishTuple
            {
                FinishTick = 92, Checksum = checksum, Score0 = 8, Score1 = 5,
                Winner = 0, SettingsHash = SettingsHash
            };
        }

        static NetLifecycleMessage Message(NetLifecycleKind kind, int seat = 1, int epoch = 0)
        {
            return new NetLifecycleMessage
            {
                Kind = kind, MatchId = MatchId, Epoch = epoch, NextEpoch = epoch,
                Seat = (byte)seat, Finish = Finish()
            };
        }

        [Test]
        public void ResultNeedsMatchingPeerFinishAndAcknowledgementOfOurExactFinalTuple()
        {
            float now = 0f;
            var host = new LanMatchLifecycle(true, MatchId, SettingsHash, () => now);
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
            var host = new LanMatchLifecycle(true, MatchId, SettingsHash, () => now);
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
            var host = new LanMatchLifecycle(true, MatchId, SettingsHash, () => now);
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
            var host = new LanMatchLifecycle(true, MatchId, SettingsHash, () => now);
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
            var host = new LanMatchLifecycle(true, MatchId, SettingsHash, () => now);
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
            var host = new LanMatchLifecycle(true, MatchId, SettingsHash, () => now);
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
                var fresh = new LanMatchLifecycle(true, MatchId + 1, SettingsHash, () => pair.Now);
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
                Assert.That(pair.Host.TryTakePreparedEpoch(out _), Is.False);
                Assert.That(pair.Host.Epoch, Is.EqualTo(1));
                Assert.That(pair.Host.LastAcceptedPeerTime, Is.EqualTo(accepted));
                bool replied = false;
                while (pair.Host.TryDequeue(out NetLifecycleMessage response))
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
                }
                Assert.That(pair.Host.Epoch, Is.EqualTo(3));
                Assert.That(pair.Join.Epoch, Is.EqualTo(3));
            }
        }

        [Test]
        public void InitialStartupHeartbeatCannotRenewTheHandshakeProgressDeadline()
        {
            float now = 0f;
            var host = new LanMatchLifecycle(true, MatchId, SettingsHash, () => now, initialStartup: true);
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
            var lifecycle = new LanMatchLifecycle(true, MatchId, SettingsHash, () => now);
            MatchSimState state = MatchSim.Create(new MatchSettings());
            MatchSim.StartMatch(state);
            var rollback = new RollbackSession(state, 0);
            for (int frame = 0; frame <= RollbackSession.DelayTicks + RollbackSession.BufferTicks; frame++)
            {
                now = frame / 60f;
                Assert.That(lifecycle.Receive(Message(NetLifecycleKind.Heartbeat)), Is.True);
                lifecycle.Tick();
                Assert.That(lifecycle.CanAdvance(state.Tick), Is.True);
                rollback.Advance(PlayerInput.Neutral);
                if (rollback.Status == RollbackStatus.InputHole) break;
            }
            Assert.That(lifecycle.LastAcceptedPeerTime, Is.EqualTo(now));
            Assert.That(lifecycle.FailureReason, Is.Empty, "The peer is live, but its required gameplay history is missing.");
            Assert.That(rollback.ConfirmedTick, Is.EqualTo(RollbackSession.DelayTicks - 1));
            Assert.That(rollback.Status, Is.EqualTo(RollbackStatus.InputHole));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TwoSocketHandshakeAndGameplayOverlapFinalConfirmationLoss(bool protectStartup)
        {
            using (var host = new BootstrapPeer(true, protectStartup))
            using (var join = new BootstrapPeer(false, protectStartup))
            {
                host.Connect(join);
                join.Connect(host);
                int joinTicksBeforeHostReady = 0;
                for (int step = 0; step < 180; step++)
                {
                    host.Now = join.Now = step / 60f;
                    host.RetryHandshake();
                    join.RetryHandshake();
                    host.Drain();
                    join.Drain();
                    host.StepAndSend();
                    join.StepAndSend();
                    if (host.Lifecycle == null && join.Rollback != null)
                        joinTicksBeforeHostReady = Math.Max(joinTicksBeforeHostReady, join.Rollback.State.Tick);
                    if (join.Rollback != null && join.Rollback.Status != RollbackStatus.Ok) break;
                    if (host.Rollback != null && join.Rollback != null &&
                        host.Rollback.State.Tick >= 40 && join.Rollback.State.Tick >= 40) break;
                    Thread.Sleep(1);
                }
                Assert.That(join.DroppedConfirmation, Is.True, "The final handshake acknowledgement must actually be lost.");
                if (!protectStartup)
                {
                    Assert.That(join.Rollback.Status, Is.EqualTo(RollbackStatus.InputHole),
                        "This control case reproduces the old 0.25s retry / 12-tick rollback race.");
                    Assert.That(joinTicksBeforeHostReady, Is.GreaterThanOrEqualTo(14));
                    return;
                }
                Assert.That(joinTicksBeforeHostReady, Is.LessThanOrEqualTo(1));
                foreach (BootstrapPeer peer in new[] { host, join })
                {
                    Assert.That(peer.Handshake.IsComplete, Is.True);
                    Assert.That(peer.Lifecycle.Phase, Is.EqualTo(LanLifecyclePhase.Playing));
                    Assert.That(peer.Lifecycle.FailureReason, Is.Empty);
                    Assert.That(peer.Rollback.Status, Is.EqualTo(RollbackStatus.Ok));
                    Assert.That(peer.Rollback.State.Tick, Is.GreaterThanOrEqualTo(40));
                    Assert.That(peer.Rollback.ValidatedChecksumCount, Is.GreaterThan(0));
                    Assert.That(peer.Lifecycle.LastPeerAck, Is.GreaterThan(3));
                }
                int commonTick = Math.Min(host.Rollback.State.Tick, join.Rollback.State.Tick) - 2;
                Assert.That(host.Rollback.ChecksumAt(commonTick), Is.EqualTo(join.Rollback.ChecksumAt(commonTick)));
            }
        }

        // Runs handshake and playable fixed ticks concurrently, unlike tests which finish the
        // complete handshake first. The disabled-barrier case is an explicit old-behavior control.
        sealed class BootstrapPeer : IDisposable
        {
            readonly bool _host;
            readonly bool _protectStartup;
            readonly LanUdpPeer _socket = new LanUdpPeer();
            public float Now;
            public LanHandshake Handshake;
            public LanMatchLifecycle Lifecycle;
            public RollbackSession Rollback;
            public bool DroppedConfirmation;

            public BootstrapPeer(bool host, bool protectStartup)
            {
                _host = host;
                _protectStartup = protectStartup;
                Assert.That(_socket.BindEphemeral(), Is.True);
            }

            public void Connect(BootstrapPeer other)
            {
                var remote = new IPEndPoint(IPAddress.Loopback, other._socket.LocalEndPoint.Port);
                _socket.SetRemote(remote);
                Handshake = new LanHandshake(_host ? LanHandshakeRole.Host : LanHandshakeRole.Joiner,
                    8, 1, _host ? MatchId : 0, _host ? null : remote);
            }

            public void RetryHandshake()
            {
                if (Handshake.TryBuildRetry(Now, out byte[] packet)) SendControl(packet);
            }

            void SendControl(byte[] packet)
            {
                if (packet == null) return;
                if (!_host && !DroppedConfirmation &&
                    LanUdpPeer.TryDecodeReady(packet, out NetReadyMessage ready) &&
                    ready.Phase == ReadyPhase.JoinerConfirm)
                {
                    DroppedConfirmation = true;
                    return;
                }
                Assert.That(_socket.Send(packet), Is.True);
            }

            public void Drain()
            {
                while (_socket.TryRecv(out byte[] bytes, out IPEndPoint from))
                {
                    if (!_socket.IsRemote(from)) continue;
                    if (Handshake.Process(bytes, from, Now, out byte[] reply))
                    {
                        SendControl(reply);
                        if (Handshake.IsComplete && Lifecycle == null)
                        {
                            Lifecycle = new LanMatchLifecycle(_host, MatchId, SettingsHash, () => Now,
                                initialStartup: _protectStartup);
                            var state = MatchSim.Create(new MatchSettings());
                            MatchSim.StartMatch(state);
                            Rollback = new RollbackSession(state, _host ? 0 : 1);
                        }
                        continue;
                    }
                    if (Lifecycle == null) continue;
                    if (LanUdpPeer.TryDecodeLifecycle(bytes, out NetLifecycleMessage control))
                        Lifecycle.Receive(control);
                    else if (LanUdpPeer.TryDecodeInput(bytes, out NetInputPacket input) &&
                        Lifecycle.CanAcceptInput(input.MatchId, input.Epoch, input.Seat, input.AckTick) &&
                        Rollback.ReceiveRemoteHistory(input.NewestTick, input.InputBits, input.HistoryCount) > 0)
                    {
                        Lifecycle.AcceptInput(input.MatchId, input.Epoch, input.Seat, input.NewestTick,
                            input.AckTick, Rollback.ConfirmedTick);
                        if (input.ChecksumTick >= 0 && input.ChecksumTick <= input.AckTick)
                            Rollback.QueueRemoteChecksum(input.ChecksumTick, input.Checksum);
                    }
                }
            }

            public void StepAndSend()
            {
                if (Lifecycle == null) return;
                Rollback.Reconcile();
                Lifecycle.Tick();
                while (Lifecycle.TryDequeue(out NetLifecycleMessage control))
                    Assert.That(_socket.Send(LanUdpPeer.EncodeLifecycle(control)), Is.True);
                if (Lifecycle.CanAdvance(Rollback.State.Tick)) Rollback.Advance(PlayerInput.Neutral);
                if (Rollback.State.Tick == 0) return;
                // Even before unrestricted play, immutable initial samples retry every poll.
                int newest = Rollback.State.Tick - 1 + RollbackSession.DelayTicks;
                var history = new byte[NetplayProtocol.InputHistoryLength];
                int count = Rollback.CopyLocalHistory(newest, history);
                Rollback.TryGetConfirmedChecksum(Rollback.State.Tick - 1, out int checksumTick, out int checksum);
                Assert.That(_socket.Send(LanUdpPeer.EncodeInput(new NetInputPacket
                {
                    MatchId = MatchId, Epoch = 0, Seat = (byte)(_host ? 0 : 1), NewestTick = newest,
                    InputBits = history, HistoryCount = (byte)count, AckTick = Rollback.ConfirmedTick,
                    ChecksumTick = checksumTick, Checksum = checksum
                })), Is.True);
                Lifecycle.RecordSentInput(newest);
            }

            public void Dispose() { _socket.Dispose(); }
        }

        // These tests drive real UDP codecs and real freshly initialized rollback sessions.
        // The old match's terminal tuple is synthetic coordinator evidence, not a natural win.
        sealed class Pair : IDisposable
        {
            public float Now;
            public readonly LanMatchLifecycle Host;
            public readonly LanMatchLifecycle Join;
            readonly LanUdpPeer _hostSocket;
            readonly LanUdpPeer _joinSocket;
            readonly HashSet<string> _dropped = new HashSet<string>();
            readonly List<NetLifecycleMessage> _heldHost = new List<NetLifecycleMessage>();
            readonly List<NetLifecycleMessage> _heldJoin = new List<NetLifecycleMessage>();
            RollbackSession _hostRollback;
            RollbackSession _joinRollback;
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

            public Pair(bool sockets)
            {
                Host = new LanMatchLifecycle(true, MatchId, SettingsHash, () => Now);
                Join = new LanMatchLifecycle(false, MatchId, SettingsHash, () => Now);
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

            void Flush(LanMatchLifecycle source, LanMatchLifecycle target, LanUdpPeer socket,
                List<NetLifecycleMessage> held)
            {
                var messages = new List<NetLifecycleMessage>();
                while (source.TryDequeue(out NetLifecycleMessage message)) messages.Add(message);
                if (ReorderControls) messages.Reverse();
                messages.AddRange(held);
                held.Clear();
                foreach (NetLifecycleMessage message in messages)
                {
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
                    byte[] bytes = LanUdpPeer.EncodeLifecycle(message);
                    if (socket != null)
                    {
                        Assert.That(socket.Send(bytes), Is.True);
                        if (DuplicateControls) Assert.That(socket.Send(bytes), Is.True);
                    }
                    else
                    {
                        Assert.That(LanUdpPeer.TryDecodeLifecycle(bytes, out NetLifecycleMessage decoded), Is.True);
                        target.Receive(decoded);
                        if (DuplicateControls) target.Receive(decoded);
                    }
                }
            }

            static void Prepare(LanMatchLifecycle lifecycle, ref RollbackSession rollback, ref int resets)
            {
                if (!lifecycle.TryTakePreparedEpoch(out int epoch)) return;
                Assert.That(epoch, Is.EqualTo(lifecycle.Epoch));
                MatchSimState state = MatchSim.Create(new MatchSettings());
                MatchSim.StartMatch(state);
                Assert.That(state.Tick, Is.Zero);
                Assert.That(state.ScoreP1, Is.Zero);
                Assert.That(state.ScoreP2, Is.Zero);
                rollback = new RollbackSession(state, lifecycle.LocalSeat);
                resets++;
                Assert.That(lifecycle.TryTakePreparedEpoch(out _), Is.False);
            }

            static void Advance(LanMatchLifecycle lifecycle, RollbackSession rollback)
            {
                if (rollback == null || !lifecycle.CanAdvance(rollback.State.Tick)) return;
                rollback.Advance(PlayerInput.Neutral);
                Assert.That(rollback.Status, Is.EqualTo(RollbackStatus.Ok));
                if (lifecycle.Phase == LanLifecyclePhase.StartupBarrier)
                    Assert.That(rollback.State.Tick, Is.LessThanOrEqualTo(2));
            }

            void SendInput(LanMatchLifecycle source, LanMatchLifecycle target, RollbackSession rollback,
                RollbackSession receiver, LanUdpPeer socket)
            {
                if (rollback == null || rollback.State.Tick == 0) return;
                int newest = rollback.State.Tick - 1 + RollbackSession.DelayTicks;
                var history = new byte[NetplayProtocol.InputHistoryLength];
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
                if (DropFirstAck && packet.AckTick >= RollbackSession.DelayTicks &&
                    _dropped.Add("ack:" + source.LocalSeat)) { DroppedAcks++; return; }
                byte[] bytes = LanUdpPeer.EncodeInput(packet);
                if (socket != null) Assert.That(socket.Send(bytes), Is.True);
                else if (LanUdpPeer.TryDecodeInput(bytes, out NetInputPacket decoded))
                    ReceiveInput(target, receiver, decoded);
            }

            static void ReceiveInput(LanMatchLifecycle lifecycle, RollbackSession rollback, NetInputPacket packet)
            {
                if (rollback == null || !lifecycle.CanAcceptInput(packet.MatchId, packet.Epoch, packet.Seat, packet.AckTick))
                    return;
                if (rollback.ReceiveRemoteHistory(packet.NewestTick, packet.InputBits, packet.HistoryCount) <= 0) return;
                lifecycle.AcceptInput(packet.MatchId, packet.Epoch, packet.Seat, packet.NewestTick,
                    packet.AckTick, rollback.ConfirmedTick);
                if (packet.ChecksumTick >= 0 && packet.ChecksumTick <= packet.AckTick)
                    rollback.QueueRemoteChecksum(packet.ChecksumTick, packet.Checksum);
            }

            static void Drain(LanUdpPeer socket, LanMatchLifecycle lifecycle,
                ref RollbackSession rollback, ref int resets)
            {
                if (socket == null) return;
                while (socket.TryRecv(out byte[] bytes, out IPEndPoint from))
                {
                    if (!socket.IsRemote(from)) continue;
                    if (LanUdpPeer.TryDecodeLifecycle(bytes, out NetLifecycleMessage control))
                    {
                        lifecycle.Receive(control);
                        Prepare(lifecycle, ref rollback, ref resets);
                    }
                    else if (LanUdpPeer.TryDecodeInput(bytes, out NetInputPacket packet))
                        ReceiveInput(lifecycle, rollback, packet);
                }
            }

            public void AssertStartupEvidence()
            {
                // Run beyond the barrier so queued real checksums are consumed by normal Advance.
                Run(0.5f);
                Assert.That(_hostRollback.State.ScoreP1, Is.Zero);
                Assert.That(_hostRollback.State.ScoreP2, Is.Zero);
                Assert.That(_joinRollback.State.ScoreP1, Is.Zero);
                Assert.That(_joinRollback.State.ScoreP2, Is.Zero);
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
