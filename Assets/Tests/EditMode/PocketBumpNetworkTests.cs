using System;
using System.Net;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class PocketBumpNetworkTests
    {
        static MatchSimState Started()
        {
            MatchSimState state = MatchSim.Create(new MatchSettings());
            MatchSim.StartMatch(state);
            return state;
        }

        [Test]
        public void ProtocolV6RoundTripsEveryTypedMessage()
        {
            Assert.That(NetplayProtocol.Version, Is.EqualTo(6));
            Assert.That(NetplayProtocol.VersionsMatch(5), Is.False,
                "The previous release has no epoch-isolated match lifecycle.");
            Assert.That(NetplayProtocol.VersionsMatch(4), Is.False,
                "Pre-fix collision and shot rules must not join a corrected-physics match.");
            Assert.That(NetplayProtocol.VersionsMatch(3), Is.False,
                "Peers sending speculative v3 checksums must not join a confirmed-history match.");

            byte[] joinBytes = LanUdpPeer.EncodeJoin(8, 3, 0x0102030405060708UL);
            Assert.That(LanUdpPeer.TryDecodeJoin(joinBytes, out NetJoinMessage join), Is.True);
            Assert.That(join.Version, Is.EqualTo(NetplayProtocol.Version));
            Assert.That(join.Points, Is.EqualTo(8));
            Assert.That(join.Marbles, Is.EqualTo(3));
            Assert.That(join.Nonce, Is.EqualTo(0x0102030405060708UL));
            Assert.That(join.IsCompatibilityProbe, Is.False);

            byte[] ackBytes = LanUdpPeer.EncodeJoinAck(true, 1, 42, 8, 3, join.Nonce);
            Assert.That(LanUdpPeer.TryDecodeJoinAck(ackBytes, out NetJoinAckMessage ack), Is.True);
            Assert.That(ack.Accepted, Is.True);
            Assert.That(ack.Seat, Is.EqualTo(1));
            Assert.That(ack.MatchId, Is.EqualTo(42));
            Assert.That(ack.Nonce, Is.EqualTo(join.Nonce));

            foreach (ReadyPhase phase in Enum.GetValues(typeof(ReadyPhase)))
            {
                byte[] readyBytes = LanUdpPeer.EncodeReady(42, phase, 7);
                Assert.That(LanUdpPeer.TryDecodeReady(readyBytes, out NetReadyMessage ready), Is.True);
                Assert.That(ready.MatchId, Is.EqualTo(42));
                Assert.That(ready.Phase, Is.EqualTo(phase));
                Assert.That(ready.Epoch, Is.EqualTo(7));
            }

            byte[] history = { 31, 22, 13, 4 };
            var input = new NetInputPacket
            {
                MatchId = 42,
                Epoch = 7,
                NewestTick = 100,
                Seat = 1,
                HistoryCount = (byte)history.Length,
                InputBits = history,
                AckTick = 95,
                ChecksumTick = 94,
                Checksum = 123456
            };
            byte[] inputBytes = LanUdpPeer.EncodeInput(input);
            Assert.That(inputBytes.Length, Is.EqualTo(LanUdpPeer.EncodedInputSize(history.Length)));
            Assert.That(LanUdpPeer.TryDecodeInput(inputBytes, out NetInputPacket decoded), Is.True);
            Assert.That(decoded.MatchId, Is.EqualTo(input.MatchId));
            Assert.That(decoded.Epoch, Is.EqualTo(input.Epoch));
            Assert.That(decoded.NewestTick, Is.EqualTo(input.NewestTick));
            Assert.That(decoded.Seat, Is.EqualTo(input.Seat));
            Assert.That(decoded.InputBits, Is.EqualTo(history));
            Assert.That(decoded.AckTick, Is.EqualTo(input.AckTick));
            Assert.That(decoded.Checksum, Is.EqualTo(input.Checksum));

            byte[] goodbyeBytes = LanUdpPeer.EncodeGoodbye(42, 7);
            Assert.That(LanUdpPeer.TryDecodeGoodbye(goodbyeBytes, out NetGoodbyeMessage goodbye), Is.True);
            Assert.That(goodbye.MatchId, Is.EqualTo(42));
            Assert.That(goodbye.Epoch, Is.EqualTo(7));
        }

        [Test]
        public void TypedDecodersRejectTruncationWrongTypeInvalidHistoryAndOldVersion()
        {
            byte[] join = LanUdpPeer.EncodeJoin(8, 1);
            byte[] truncatedJoin = new byte[join.Length - 1];
            Array.Copy(join, truncatedJoin, truncatedJoin.Length);
            Assert.That(LanUdpPeer.TryDecodeJoin(truncatedJoin, out _), Is.False);

            // Version is the first int after the five-byte header.
            join[5] = 1;
            join[6] = join[7] = join[8] = 0;
            Assert.That(LanUdpPeer.TryDecodeJoin(join, out NetJoinMessage oldJoin), Is.True);
            Assert.That(NetplayProtocol.CanAcceptJoin(8, 1, oldJoin.Version, oldJoin.Points, oldJoin.Marbles, oldJoin.SettingsHash), Is.False);

            byte[] input = LanUdpPeer.EncodeInput(new NetInputPacket
            {
                MatchId = 1,
                NewestTick = 9,
                Seat = 0,
                HistoryCount = 1,
                InputBits = new byte[] { 0 },
                AckTick = 8,
                ChecksumTick = 7,
                Checksum = 6
            });
            input[22] = 0;
            Assert.That(LanUdpPeer.TryDecodeInput(input, out _), Is.False);
            input[22] = (byte)(NetplayProtocol.InputHistoryLength + 1);
            Assert.That(LanUdpPeer.TryDecodeInput(input, out _), Is.False);
            input[4] = NetplayProtocol.Join;
            Assert.That(LanUdpPeer.TryDecodeInput(input, out _), Is.False);

            Assert.That(LanUdpPeer.TryDecodeReady(new byte[4], out _), Is.False);
            Assert.That(LanUdpPeer.TryDecodeGoodbye(null, out _), Is.False);
        }

        [Test]
        public void RemoteHistoryTracksContiguousAckAndDoesNotRegressPrediction()
        {
            var session = new RollbackSession(Started(), 0);
            var newest = new PlayerInput { Move = Vector2.right, BoostHeld = true, ShootPressed = true };
            Assert.That(session.TryReceiveRemote(2, PlayerInput.Neutral), Is.True);
            Assert.That(session.ConfirmedTick, Is.EqualTo(2));
            Assert.That(session.TryReceiveRemote(4, newest), Is.True);
            Assert.That(session.ConfirmedTick, Is.EqualTo(2));
            Assert.That(session.TryReceiveRemote(3, new PlayerInput { Move = Vector2.up }), Is.True);
            Assert.That(session.ConfirmedTick, Is.EqualTo(4));

            PlayerInput predicted = session.PredictedRemoteFor(5);
            Assert.That(predicted.Move, Is.EqualTo(Vector2.right));
            Assert.That(predicted.BoostHeld, Is.True);
            Assert.That(predicted.ShootPressed, Is.False);

            Assert.That(session.TryReceiveRemote(3, new PlayerInput { Move = Vector2.left }), Is.False,
                "A conflicting duplicate must not replace the first accepted sample.");
            Assert.That(session.PredictedRemoteFor(5).Move, Is.EqualTo(Vector2.right));
            Assert.That(session.TryReceiveRemote(NetplayProtocol.FutureTickAllowance + 1, PlayerInput.Neutral), Is.False);
        }

        [Test]
        public void RedundantHistoryRecoversDroppedAndReorderedTicks()
        {
            var session = new RollbackSession(Started(), 0);
            PlayerInput t2 = new PlayerInput { Move = Vector2.left };
            PlayerInput t3 = new PlayerInput { Move = Vector2.up, BoostHeld = true };
            PlayerInput t4 = new PlayerInput { Move = Vector2.right };

            Assert.That(session.ReceiveRemoteHistory(2, new[] { t2.Pack() }, 1), Is.EqualTo(1));
            // Tick 3's own datagram is lost. Tick 4 carries 4, 3, 2 and repairs it.
            byte[] repair = { t4.Pack(), t3.Pack(), t2.Pack() };
            Assert.That(session.ReceiveRemoteHistory(4, repair, repair.Length), Is.EqualTo(3));
            Assert.That(session.ConfirmedTick, Is.EqualTo(4));

            PlayerInput t5 = new PlayerInput { Move = Vector2.down };
            PlayerInput t6 = new PlayerInput { Move = Vector2.down, BoostHeld = true };
            Assert.That(session.ReceiveRemoteHistory(6, new[] { t6.Pack() }, 1), Is.EqualTo(1));
            Assert.That(session.ConfirmedTick, Is.EqualTo(4));
            Assert.That(session.ReceiveRemoteHistory(5, new[] { t5.Pack(), t4.Pack() }, 2), Is.EqualTo(2));
            Assert.That(session.ConfirmedTick, Is.EqualTo(6));
            Assert.That(session.NewestRemoteTick, Is.EqualTo(6));
        }

        [Test]
        public void RetryableHandshakeSurvivesFirstLossOfEveryControlPhase()
        {
            IPEndPoint hostEnd = new IPEndPoint(IPAddress.Loopback, 7777);
            IPEndPoint joinEnd = new IPEndPoint(IPAddress.Loopback, 17777);
            var host = new LanHandshake(LanHandshakeRole.Host, 8, 1, 1234);
            var join = new LanHandshake(LanHandshakeRole.Joiner, 8, 1, 0, hostEnd);

            // Drop first Join.
            Assert.That(join.TryBuildRetry(0f, out _), Is.True);
            Assert.That(join.TryBuildRetry(0.3f, out byte[] joinRetry), Is.True);
            Assert.That(host.Process(joinRetry, joinEnd, 0.3f, out _), Is.True);
            Assert.That(host.RemoteEndpoint, Is.Null, "A dropped capability response must not bind a participant.");
            // Drop first Capability and first nonce-bearing Join; both retry within the same handshake.
            Assert.That(join.TryBuildRetry(0.6f, out byte[] probeRetry), Is.True);
            Assert.That(host.Process(probeRetry, joinEnd, 0.6f, out byte[] capability), Is.True);
            Assert.That(join.Process(capability, hostEnd, 0.6f, out _), Is.True);
            Assert.That(join.TryBuildRetry(0.9f, out byte[] realJoin), Is.True);
            Assert.That(host.Process(realJoin, joinEnd, 0.9f, out byte[] firstAck), Is.True);
            Assert.That(host.RemoteEndpoint, Is.EqualTo(joinEnd));

            // Drop first JoinAck; host retries the same negotiated values.
            Assert.That(host.TryBuildRetry(1.2f, out byte[] ackRetry), Is.True);
            Assert.That(LanUdpPeer.TryDecodeJoinAck(firstAck, out NetJoinAckMessage a), Is.True);
            Assert.That(LanUdpPeer.TryDecodeJoinAck(ackRetry, out NetJoinAckMessage b), Is.True);
            Assert.That(b.MatchId, Is.EqualTo(a.MatchId));
            Assert.That(join.Process(ackRetry, hostEnd, 1.2f, out _), Is.True);

            // Drop first JoinerReady, then deliver its retry.
            Assert.That(join.TryBuildRetry(1.5f, out byte[] joinerReady), Is.True);
            Assert.That(host.Process(joinerReady, joinEnd, 1.5f, out _), Is.True);

            // Drop first HostReady; deliver retry and drop first JoinerConfirm.
            Assert.That(host.TryBuildRetry(1.8f, out byte[] hostReady), Is.True);
            Assert.That(join.Process(hostReady, hostEnd, 1.8f, out _), Is.True);
            Assert.That(join.IsComplete, Is.True);

            Assert.That(host.TryBuildRetry(2.1f, out byte[] hostReadyRetry), Is.True);
            Assert.That(join.Process(hostReadyRetry, hostEnd, 2.1f, out byte[] confirmRetry), Is.True);
            Assert.That(host.Process(confirmRetry, joinEnd, 2.1f, out _), Is.True);
            Assert.That(host.IsComplete, Is.True);
            Assert.That(host.MatchId, Is.EqualTo(join.MatchId));
            Assert.That(host.LocalSeat, Is.EqualTo(0));
            Assert.That(join.LocalSeat, Is.EqualTo(1));
        }

        [Test]
        public void DuplicateAndReorderedHandshakeMessagesAreIdempotent()
        {
            IPEndPoint joinEnd = new IPEndPoint(IPAddress.Loopback, 17777);
            var host = new LanHandshake(LanHandshakeRole.Host, 5, 3, 991);
            byte[] join = LanUdpPeer.EncodeJoin(5, 3, 117UL);
            Assert.That(host.Process(join, joinEnd, 0f, out byte[] ackA), Is.True);
            Assert.That(host.Process(join, joinEnd, 0.1f, out byte[] ackB), Is.True);
            Assert.That(ackB, Is.EqualTo(ackA));

            byte[] prematureConfirm = LanUdpPeer.EncodeReady(991, ReadyPhase.JoinerConfirm);
            Assert.That(host.Process(prematureConfirm, joinEnd, 0.2f, out _), Is.False);
            Assert.That(host.State, Is.EqualTo(LanHandshakeState.HostWaitingJoinerReady));
        }

        [Test]
        public void HostRetriesNegativeJoinAckDuringFailureGrace()
        {
            IPEndPoint joinEnd = new IPEndPoint(IPAddress.Loopback, 17777);
            var host = new LanHandshake(LanHandshakeRole.Host, 8, 1, 404);
            byte[] oldJoin = LanUdpPeer.EncodeJoin(8, 1);
            oldJoin[5] = (byte)(NetplayProtocol.Version - 1);
            oldJoin[6] = oldJoin[7] = oldJoin[8] = 0;

            Assert.That(host.Process(oldJoin, joinEnd, 0f, out byte[] rejected), Is.True);
            Assert.That(host.IsFailed, Is.True);
            Assert.That(LanUdpPeer.TryDecodeJoinAck(rejected, out NetJoinAckMessage first), Is.True);
            Assert.That(first.Accepted, Is.False);
            Assert.That(host.TryBuildRetry(0.3f, out byte[] retry), Is.True);
            Assert.That(retry, Is.EqualTo(rejected));
        }

        [TestCase(4)]
        [TestCase(5)]
        public void LifecycleHostRejectsLegacyJoinBeforeEitherHandshakeCompletes(int version)
        {
            var hostEnd = new IPEndPoint(IPAddress.Loopback, 7777);
            var joinEnd = new IPEndPoint(IPAddress.Loopback, 17777);
            var host = new LanHandshake(LanHandshakeRole.Host, 8, 1, 404);
            var join = new LanHandshake(LanHandshakeRole.Joiner, 8, 1, 0, hostEnd);
            byte[] oldJoin = LanUdpPeer.EncodeJoin(8, 1);
            // Preserve the old exact layout and encode the older release's own settings hash.
            WritePacketInt(oldJoin, 5, version);
            WritePacketInt(oldJoin, 17, (8 * 397) ^ 1 ^ (version * 17));
            Assert.That(LanUdpPeer.TryDecodeJoin(oldJoin, out NetJoinMessage request), Is.True);
            Assert.That(request.Version, Is.EqualTo(version));

            Assert.That(host.Process(oldJoin, joinEnd, 0f, out byte[] rejected), Is.True);
            Assert.That(host.IsFailed, Is.True);
            Assert.That(host.IsComplete, Is.False);
            Assert.That(host.FailureMessage, Is.EqualTo("SETTINGS MISMATCH"));
            Assert.That(LanUdpPeer.TryDecodeJoinAck(rejected, out NetJoinAckMessage ack), Is.True);
            Assert.That(ack.Accepted, Is.False);
            Assert.That(ack.Version, Is.EqualTo(6));
            Assert.That(join.Process(rejected, hostEnd, 0f, out byte[] ready), Is.True);
            Assert.That(join.IsFailed, Is.True);
            Assert.That(join.IsComplete, Is.False);
            Assert.That(ready, Is.Null, "An incompatible request must not emit gameplay-ready confirmation.");
        }

        [TestCase(4, true)]
        [TestCase(4, false)]
        [TestCase(5, true)]
        [TestCase(5, false)]
        public void LifecycleJoinerRejectsLegacyHostAcknowledgementBeforeReady(int version, bool accepted)
        {
            var hostEnd = new IPEndPoint(IPAddress.Loopback, 7777);
            var join = new LanHandshake(LanHandshakeRole.Joiner, 8, 1, 0, hostEnd);
            Assert.That(join.TryBuildRetry(0f, out byte[] request), Is.True);
            Assert.That(LanUdpPeer.TryDecodeJoin(request, out NetJoinMessage newJoin), Is.True);
            Assert.That(newJoin.Version, Is.EqualTo(6));
            Assert.That(request.Length, Is.EqualTo(21), "Archived v4/v5 hosts require this exact Join length.");
            byte[] oldAck = LanUdpPeer.EncodeJoinAck(accepted, 1, 404, 8, 1);
            // JoinAck: five-byte header, accepted/seat bytes, match id, then version.
            WritePacketInt(oldAck, 11, version);
            Assert.That(LanUdpPeer.TryDecodeJoinAck(oldAck, out NetJoinAckMessage ack), Is.True);
            Assert.That(ack.Version, Is.EqualTo(version));

            Assert.That(join.Process(oldAck, hostEnd, 0.1f, out byte[] ready), Is.True);
            Assert.That(join.IsFailed, Is.True);
            Assert.That(join.IsComplete, Is.False);
            Assert.That(join.FailureMessage, Is.EqualTo("SETTINGS MISMATCH"));
            Assert.That(ready, Is.Null);
        }

        [Test]
        public void LifecyclePeersNegotiateVersion6WithoutChangingSeatsOrReadyPhases()
        {
            var hostEnd = new IPEndPoint(IPAddress.Loopback, 7777);
            var joinEnd = new IPEndPoint(IPAddress.Loopback, 17777);
            var host = new LanHandshake(LanHandshakeRole.Host, 8, 1, 505);
            var join = new LanHandshake(LanHandshakeRole.Joiner, 8, 1, 0, hostEnd);
            Assert.That(join.TryBuildRetry(0f, out byte[] request), Is.True);
            Assert.That(host.Process(request, joinEnd, 0f, out byte[] capability), Is.True);
            Assert.That(join.Process(capability, hostEnd, 0f, out byte[] nonceJoin), Is.True);
            Assert.That(host.Process(nonceJoin, joinEnd, 0f, out byte[] ack), Is.True);
            Assert.That(join.Process(ack, hostEnd, 0f, out byte[] joinReady), Is.True);
            Assert.That(host.Process(joinReady, joinEnd, 0f, out byte[] hostReady), Is.True);
            Assert.That(join.Process(hostReady, hostEnd, 0f, out byte[] confirmation), Is.True);
            Assert.That(host.Process(confirmation, joinEnd, 0f, out _), Is.True);
            Assert.That(host.IsComplete && join.IsComplete, Is.True);
            Assert.That(host.IsFailed || join.IsFailed, Is.False);
            Assert.That(host.LocalSeat, Is.EqualTo(0));
            Assert.That(join.LocalSeat, Is.EqualTo(1));
            Assert.That(host.MatchId, Is.EqualTo(join.MatchId));
            Assert.That(host.MatchId, Is.EqualTo(505));
        }

        [Test]
        public void TwoSocketLoopbackCompletesHandshakeWithFaultInjection()
        {
            using (var hostPeer = new LanUdpPeer())
            using (var joinPeer = new LanUdpPeer())
            {
                Assert.That(hostPeer.BindEphemeral(), Is.True);
                Assert.That(joinPeer.BindEphemeral(), Is.True);
                IPEndPoint hostEnd = Loopback(hostPeer.LocalEndPoint);
                IPEndPoint joinEnd = Loopback(joinPeer.LocalEndPoint);
                hostPeer.SetRemote(joinEnd);
                joinPeer.SetRemote(hostEnd);

                var host = new LanHandshake(LanHandshakeRole.Host, 8, 1, 7331);
                var join = new LanHandshake(LanHandshakeRole.Joiner, 8, 1, 0, hostEnd);
                var faults = new FirstControlFaults();

                for (int step = 0; step < 80 && (!host.IsComplete || !join.IsComplete); step++)
                {
                    float now = step * 0.3f;
                    if (host.TryBuildRetry(now, out byte[] hostRetry)) SendUnlessDropped(hostPeer, hostRetry, faults);
                    if (join.TryBuildRetry(now, out byte[] joinRetry)) SendUnlessDropped(joinPeer, joinRetry, faults);
                    Thread.Sleep(1);
                    DrainHandshake(hostPeer, host, now, faults);
                    DrainHandshake(joinPeer, join, now, faults);
                }

                Assert.That(host.IsComplete, Is.True);
                Assert.That(join.IsComplete, Is.True);
                Assert.That(host.MatchId, Is.EqualTo(join.MatchId));
                Assert.That(host.Points, Is.EqualTo(join.Points));
                Assert.That(host.Marbles, Is.EqualTo(join.Marbles));
            }
        }

        [Test]
        public void TwoSocketLoopbackRecoversLossDuplicationAndReordering()
        {
            using (var sender = new LanUdpPeer())
            using (var receiver = new LanUdpPeer())
            {
                Assert.That(sender.BindEphemeral(), Is.True);
                Assert.That(receiver.BindEphemeral(), Is.True);
                sender.SetRemote(Loopback(receiver.LocalEndPoint));
                receiver.SetRemote(Loopback(sender.LocalEndPoint));
                var session = new RollbackSession(Started(), 0);

                // Tick 3 packet is intentionally not sent. Tick 4 repairs it and duplicates tick 2.
                SendInput(sender, 2, new byte[] { new PlayerInput { Move = Vector2.left }.Pack() });
                SendInput(sender, 4, new byte[]
                {
                    new PlayerInput { Move = Vector2.right }.Pack(),
                    new PlayerInput { Move = Vector2.up }.Pack(),
                    new PlayerInput { Move = Vector2.left }.Pack()
                });
                Thread.Sleep(2);
                DrainInputs(receiver, session);
                Assert.That(session.ConfirmedTick, Is.EqualTo(4));

                // Send 6 before 5 to force a gap, then fill it with an older packet.
                SendInput(sender, 6, new byte[] { new PlayerInput { Move = Vector2.down }.Pack() });
                Thread.Sleep(1);
                DrainInputs(receiver, session);
                Assert.That(session.ConfirmedTick, Is.EqualTo(4));
                SendInput(sender, 5, new byte[] { new PlayerInput { BoostHeld = true }.Pack() });
                Thread.Sleep(1);
                DrainInputs(receiver, session);
                Assert.That(session.ConfirmedTick, Is.EqualTo(6));
            }
        }

        [Test]
        public void TwoSocketFaultedInputStreamsConvergeToEqualChecksums()
        {
            using (var peerA = new LanUdpPeer())
            using (var peerB = new LanUdpPeer())
            {
                Assert.That(peerA.BindEphemeral(), Is.True);
                Assert.That(peerB.BindEphemeral(), Is.True);
                peerA.SetRemote(Loopback(peerB.LocalEndPoint));
                peerB.SetRemote(Loopback(peerA.LocalEndPoint));
                var sessionA = new RollbackSession(Started(), 0);
                var sessionB = new RollbackSession(Started(), 1);
                const int frames = 48;

                for (int t = 0; t < frames + 10; t++)
                {
                    PlayerInput inputA = t < frames && t % 5 == 0
                        ? new PlayerInput { Move = Vector2.down, BoostHeld = t % 10 == 0 }
                        : PlayerInput.Neutral;
                    PlayerInput inputB = t < frames && t % 7 == 0
                        ? new PlayerInput { Move = Vector2.up, ShootPressed = t == 14 }
                        : PlayerInput.Neutral;
                    sessionA.Advance(inputA);
                    sessionB.Advance(inputB);
                    Assert.That(sessionA.Status, Is.EqualTo(RollbackStatus.Ok));
                    Assert.That(sessionB.Status, Is.EqualTo(RollbackStatus.Ok));

                    int newest = t + RollbackSession.DelayTicks;
                    // Drop isolated packets in both directions. Every following packet carries history.
                    if (t != 9 && t != 23) SendSessionHistory(peerA, sessionA, 0, newest);
                    if (t != 12 && t != 29) SendSessionHistory(peerB, sessionB, 1, newest);
                    // Duplicate selected datagrams; insertion must remain idempotent.
                    if (t == 18) SendSessionHistory(peerA, sessionA, 0, newest);
                    if (t == 21) SendSessionHistory(peerB, sessionB, 1, newest);

                    Thread.Sleep(1);
                    DrainInputs(peerA, sessionA);
                    DrainInputs(peerB, sessionB);
                }

                Assert.That(sessionA.Status, Is.EqualTo(RollbackStatus.Ok));
                Assert.That(sessionB.Status, Is.EqualTo(RollbackStatus.Ok));
                Assert.That(sessionA.State.Tick, Is.EqualTo(sessionB.State.Tick));
                Assert.That(sessionA.State.Checksum(), Is.EqualTo(sessionB.State.Checksum()));
            }
        }

        [Test]
        public void TwoSocketLoopbackStillReportsUnrecoverableInputHole()
        {
            using (var silentSender = new LanUdpPeer())
            using (var receiver = new LanUdpPeer())
            {
                Assert.That(silentSender.BindEphemeral(), Is.True);
                Assert.That(receiver.BindEphemeral(), Is.True);
                silentSender.SetRemote(Loopback(receiver.LocalEndPoint));
                receiver.SetRemote(Loopback(silentSender.LocalEndPoint));
                var session = new RollbackSession(Started(), 0);

                for (int i = 0; i < RollbackSession.BufferTicks + RollbackSession.DelayTicks + 2; i++)
                {
                    session.Advance(PlayerInput.Neutral);
                    if (session.Status == RollbackStatus.InputHole) break;
                }

                Assert.That(session.Status, Is.EqualTo(RollbackStatus.InputHole));
            }
        }

        static IPEndPoint Loopback(IPEndPoint endpoint)
        {
            return new IPEndPoint(IPAddress.Loopback, endpoint.Port);
        }

        static void WritePacketInt(byte[] packet, int offset, int value)
        {
            packet[offset] = (byte)value;
            packet[offset + 1] = (byte)(value >> 8);
            packet[offset + 2] = (byte)(value >> 16);
            packet[offset + 3] = (byte)(value >> 24);
        }

        static void SendInput(LanUdpPeer peer, int newestTick, byte[] history)
        {
            peer.Send(LanUdpPeer.EncodeInput(new NetInputPacket
            {
                MatchId = 1,
                NewestTick = newestTick,
                Seat = 1,
                HistoryCount = (byte)history.Length,
                InputBits = history,
                AckTick = 1,
                ChecksumTick = -1,
                Checksum = 0
            }));
        }

        static void SendSessionHistory(
            LanUdpPeer peer,
            RollbackSession session,
            byte seat,
            int newestTick)
        {
            byte[] history = new byte[NetplayProtocol.InputHistoryLength];
            int count = session.CopyLocalHistory(newestTick, history);
            peer.Send(LanUdpPeer.EncodeInput(new NetInputPacket
            {
                MatchId = 1,
                NewestTick = newestTick,
                Seat = seat,
                HistoryCount = (byte)count,
                InputBits = history,
                AckTick = session.ConfirmedTick,
                ChecksumTick = -1,
                Checksum = 0
            }));
        }

        static void DrainInputs(LanUdpPeer peer, RollbackSession session)
        {
            for (int spin = 0; spin < 20; spin++)
            {
                bool readAny = false;
                while (peer.TryRecv(out byte[] data, out _))
                {
                    readAny = true;
                    if (LanUdpPeer.TryDecodeInput(data, out NetInputPacket packet))
                        session.ReceiveRemoteHistory(packet.NewestTick, packet.InputBits, packet.HistoryCount);
                }
                if (readAny) continue;
                Thread.Sleep(1);
            }
        }

        static void DrainHandshake(LanUdpPeer peer, LanHandshake handshake, float now, FirstControlFaults faults)
        {
            while (peer.TryRecv(out byte[] data, out IPEndPoint from))
            {
                if (!handshake.Process(data, from, now, out byte[] response) || response == null) continue;
                SendUnlessDropped(peer, response, faults);
            }
        }

        static void SendUnlessDropped(LanUdpPeer peer, byte[] packet, FirstControlFaults faults)
        {
            if (!faults.Drop(packet)) peer.Send(packet);
        }

        sealed class FirstControlFaults
        {
            bool _join;
            bool _ack;
            bool _joinerReady;
            bool _hostReady;
            bool _confirm;

            public bool Drop(byte[] packet)
            {
                if (!LanUdpPeer.TryReadHeader(packet, out byte type, out _)) return false;
                if (type == NetplayProtocol.Join && !_join) { _join = true; return true; }
                if (type == NetplayProtocol.JoinAck && !_ack) { _ack = true; return true; }
                if (type != NetplayProtocol.Ready || !LanUdpPeer.TryDecodeReady(packet, out NetReadyMessage ready))
                    return false;
                if (ready.Phase == ReadyPhase.JoinerReady && !_joinerReady) { _joinerReady = true; return true; }
                if (ready.Phase == ReadyPhase.HostReady && !_hostReady) { _hostReady = true; return true; }
                if (ready.Phase == ReadyPhase.JoinerConfirm && !_confirm) { _confirm = true; return true; }
                return false;
            }
        }
    }
}
