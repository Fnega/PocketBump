using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using NUnit.Framework;

namespace PocketBump.Tests
{
    public sealed class PocketBumpLifecycleTransportTests
    {
        static readonly IPEndPoint Host = new IPEndPoint(IPAddress.Loopback, 7777);
        static readonly IPEndPoint Joiner = new IPEndPoint(IPAddress.Loopback, 17777);
        static readonly IPEndPoint Stranger = new IPEndPoint(IPAddress.Loopback, 27777);

        static NetLifecycleMessage Message(NetLifecycleKind kind)
        {
            return new NetLifecycleMessage
            {
                Kind = kind,
                MatchId = 904,
                Epoch = 3,
                Seat = 1,
                NextEpoch = 4,
                Finish = new NetFinishTuple
                {
                    FinishTick = 205,
                    Checksum = -12345,
                    Score0 = 8,
                    Score1 = 6,
                    Winner = 0,
                    SettingsHash = NetplayProtocol.SettingsHash(8, 1)
                }
            };
        }

        [Test]
        public void EveryLifecycleKindRoundTripsItsConnectionEpochAndTerminalTuple()
        {
            foreach (NetLifecycleKind kind in Enum.GetValues(typeof(NetLifecycleKind)))
            {
                NetLifecycleMessage sent = Message(kind);
                byte[] bytes = LanUdpPeer.EncodeLifecycle(sent);
                Assert.That(LanUdpPeer.TryDecodeLifecycle(bytes, out NetLifecycleMessage received), Is.True, kind.ToString());
                Assert.That(received.Kind, Is.EqualTo(kind));
                Assert.That(received.MatchId, Is.EqualTo(sent.MatchId));
                Assert.That(received.Epoch, Is.EqualTo(sent.Epoch));
                Assert.That(received.Seat, Is.EqualTo(sent.Seat));
                if (kind == NetLifecycleKind.Heartbeat)
                    Assert.That(bytes.Length, Is.EqualTo(19));
                else
                {
                    Assert.That(bytes.Length, Is.EqualTo(47));
                    Assert.That(received.NextEpoch, Is.EqualTo(sent.NextEpoch));
                    Assert.That(received.Finish, Is.EqualTo(sent.Finish));
                }
            }
        }

        [Test]
        public void LifecycleCodecRejectsEveryTruncationExtraBytesAndWrongType()
        {
            foreach (NetLifecycleKind kind in Enum.GetValues(typeof(NetLifecycleKind)))
            {
                byte[] bytes = LanUdpPeer.EncodeLifecycle(Message(kind));
                for (int length = 0; length < bytes.Length; length++)
                {
                    var shortPacket = new byte[length];
                    Array.Copy(bytes, shortPacket, length);
                    Assert.That(LanUdpPeer.TryDecodeLifecycle(shortPacket, out _), Is.False, kind + " length=" + length);
                }
                var longPacket = new byte[bytes.Length + 1];
                Array.Copy(bytes, longPacket, bytes.Length);
                Assert.That(LanUdpPeer.TryDecodeLifecycle(longPacket, out _), Is.False);
                bytes[4] = NetplayProtocol.Input;
                Assert.That(LanUdpPeer.TryDecodeLifecycle(bytes, out _), Is.False);
            }
        }

        [TestCase(5, 5)]
        [TestCase(9, 0)]
        [TestCase(13, -1)]
        [TestCase(19, -1)]
        [TestCase(23, -1)]
        [TestCase(31, -1)]
        [TestCase(35, -1)]
        [TestCase(39, 2)]
        public void LifecycleCodecRejectsInvalidBoundedFields(int offset, int value)
        {
            byte[] bytes = LanUdpPeer.EncodeLifecycle(Message(NetLifecycleKind.Offer));
            WriteInt(bytes, offset, value);
            Assert.That(LanUdpPeer.TryDecodeLifecycle(bytes, out _), Is.False);
        }

        [TestCase(17, 2)]
        [TestCase(18, 0)]
        [TestCase(18, 11)]
        public void LifecycleCodecRejectsInvalidSeatAndKind(int offset, byte value)
        {
            byte[] bytes = LanUdpPeer.EncodeLifecycle(Message(NetLifecycleKind.Finish));
            bytes[offset] = value;
            Assert.That(LanUdpPeer.TryDecodeLifecycle(bytes, out _), Is.False);
        }

        [Test]
        public void EstablishedCodecsRejectLegacyAndWrongVersionLayouts()
        {
            byte[] input = LanUdpPeer.EncodeInput(new NetInputPacket
            {
                MatchId = 91, Epoch = 2, NewestTick = 10, Seat = 1,
                HistoryCount = 1, InputBits = new byte[] { 0 }, AckTick = 8, ChecksumTick = 7
            });
            byte[] oldInput = new byte[input.Length - 8];
            Array.Copy(input, 0, oldInput, 0, 5);
            Array.Copy(input, 9, oldInput, 5, 4);
            Array.Copy(input, 17, oldInput, 9, input.Length - 17);
            Assert.That(LanUdpPeer.TryDecodeInput(oldInput, out _), Is.False);
            WriteInt(input, 5, 5);
            Assert.That(LanUdpPeer.TryDecodeInput(input, out _), Is.False);

            byte[] ready = LanUdpPeer.EncodeReady(91, ReadyPhase.HostReady, 2);
            WriteInt(ready, 5, 4);
            Assert.That(LanUdpPeer.TryDecodeReady(ready, out _), Is.False);
            byte[] goodbye = LanUdpPeer.EncodeGoodbye(91, 2);
            WriteInt(goodbye, 5, 5);
            Assert.That(LanUdpPeer.TryDecodeGoodbye(goodbye, out _), Is.False);
        }

        [TestCase(21, 2)]
        [TestCase(13, -1)]
        [TestCase(24, -2)]
        [TestCase(28, -2)]
        public void InputCodecRejectsInvalidSeatEpochAndNegativeTickFields(int offset, int value)
        {
            byte[] input = LanUdpPeer.EncodeInput(new NetInputPacket
            {
                MatchId = 91, Epoch = 2, NewestTick = 10, Seat = 1,
                HistoryCount = 1, InputBits = new byte[] { 0 }, AckTick = 8, ChecksumTick = 7
            });
            if (offset == 21) input[offset] = (byte)value;
            else WriteInt(input, offset, value);
            Assert.That(LanUdpPeer.TryDecodeInput(input, out _), Is.False);
        }

        [TestCase(0x40)]
        [TestCase(0x80)]
        [TestCase(0xFF)]
        public void InputCodecRejectsReservedBitsAnywhereInHistory(byte invalidBits)
        {
            byte[] input = LanUdpPeer.EncodeInput(new NetInputPacket
            {
                MatchId = 91, Epoch = 2, NewestTick = 10, Seat = 1,
                HistoryCount = 3, InputBits = new byte[] { 1, 4, 16 }, AckTick = 8, ChecksumTick = 7
            });
            for (int sample = 0; sample < 3; sample++)
            {
                byte[] malformed = (byte[])input.Clone();
                malformed[23 + sample] = invalidBits;
                Assert.That(LanUdpPeer.TryDecodeInput(malformed, out _), Is.False,
                    "Reserved bits must reject the whole packet before any history is applied.");
            }
            Assert.That(LanUdpPeer.TryDecodeInput(input, out _), Is.True);
        }

        [Test]
        public void ProbeAnswersWithoutBindingAndOnlyValidatedJoinBindsHost()
        {
            var host = new LanHandshake(LanHandshakeRole.Host, 8, 1, 991);
            Assert.That(host.Process(LanUdpPeer.EncodeReady(991, ReadyPhase.JoinerReady), Stranger, 0, out _), Is.False);
            Assert.That(host.RemoteEndpoint, Is.Null);
            Assert.That(host.Process(LanUdpPeer.EncodeJoin(8, 1), Stranger, 0, out byte[] capability), Is.True);
            Assert.That(LanUdpPeer.TryDecodeCapability(capability, out NetCapabilityMessage message), Is.True);
            Assert.That(message.Version, Is.EqualTo(6));
            Assert.That(host.RemoteEndpoint, Is.Null);
            Assert.That(host.JoinNonce, Is.Zero);
            Assert.That(host.State, Is.EqualTo(LanHandshakeState.HostWaitingJoin));
            Assert.That(host.Process(LanUdpPeer.EncodeJoin(8, 1, 404UL), Joiner, 0, out _), Is.True);
            Assert.That(host.RemoteEndpoint, Is.EqualTo(Joiner));
            Assert.That(host.JoinNonce, Is.EqualTo(404UL));
        }

        [Test]
        public void JoinRetryKeepsNonceButFreshAttemptCannotUseOldAcknowledgement()
        {
            var oldJoin = new LanHandshake(LanHandshakeRole.Joiner, 8, 1, 0, Host);
            var freshJoin = new LanHandshake(LanHandshakeRole.Joiner, 8, 1, 0, Host);
            Assert.That(oldJoin.JoinNonce, Is.Not.Zero);
            Assert.That(freshJoin.JoinNonce, Is.Not.EqualTo(oldJoin.JoinNonce));
            Assert.That(freshJoin.Process(LanUdpPeer.EncodeCapability(), Host, 0, out byte[] first), Is.True);
            Assert.That(freshJoin.TryBuildRetry(0.3f, out byte[] retry), Is.True);
            Assert.That(retry, Is.EqualTo(first));
            byte[] stale = LanUdpPeer.EncodeJoinAck(true, 1, 991, 8, 1, oldJoin.JoinNonce);
            Assert.That(freshJoin.Process(stale, Host, 0.4f, out _), Is.False);
            Assert.That(freshJoin.MatchId, Is.Zero);
            Assert.That(freshJoin.IsFailed, Is.False);
            Assert.That(freshJoin.State, Is.EqualTo(LanHandshakeState.JoinerWaitingAck));
            byte[] current = LanUdpPeer.EncodeJoinAck(true, 1, 992, 8, 1, freshJoin.JoinNonce);
            Assert.That(freshJoin.Process(current, Stranger, 0.5f, out _), Is.False);
            Assert.That(freshJoin.Process(current, Host, 0.5f, out _), Is.True);
            Assert.That(freshJoin.MatchId, Is.EqualTo(992));
        }

        [Test]
        public void StaleJoinAndFutureReadyCannotReplaceNegotiatedParticipant()
        {
            var host = new LanHandshake(LanHandshakeRole.Host, 8, 1, 991);
            Assert.That(host.Process(LanUdpPeer.EncodeJoin(8, 1, 404UL), Joiner, 0, out _), Is.True);
            Assert.That(host.Process(LanUdpPeer.EncodeJoin(8, 1, 405UL), Joiner, 0, out _), Is.False);
            Assert.That(host.Process(LanUdpPeer.EncodeReady(991, ReadyPhase.JoinerReady, 1), Joiner, 0, out _), Is.False);
            Assert.That(host.Process(LanUdpPeer.EncodeReady(991, ReadyPhase.JoinerReady), Stranger, 0, out _), Is.False);
            Assert.That(host.JoinNonce, Is.EqualTo(404UL));
            Assert.That(host.State, Is.EqualTo(LanHandshakeState.HostWaitingJoinerReady));
        }

        [Test]
        public void PositiveLegacyAcknowledgementCannotCompleteProtocolSixHandshake()
        {
            var join = new LanHandshake(LanHandshakeRole.Joiner, 8, 1, 0, Host);
            Assert.That(join.Process(LanUdpPeer.EncodeJoinAck(true, 1, 991, 8, 1), Host, 0, out _), Is.True);
            Assert.That(join.IsFailed, Is.True);
            Assert.That(join.MatchId, Is.Zero);
            Assert.That(join.FailureMessage, Is.EqualTo("SETTINGS MISMATCH"));
        }

        [TestCase(4)]
        [TestCase(5)]
        public void LegacyHostRecognizesExactProbeAndRejectsBeforeGameplay(int legacyVersion)
        {
            var join = new LanHandshake(LanHandshakeRole.Joiner, 8, 1, 0, Host);
            Assert.That(join.TryBuildRetry(0, out byte[] probe), Is.True);
            Assert.That(probe.Length, Is.EqualTo(21));
            Assert.That(BitConverter.ToInt32(probe, 0), Is.EqualTo(NetplayProtocol.Magic));
            Assert.That(probe[4], Is.EqualTo(NetplayProtocol.Join));
            Assert.That(BitConverter.ToInt32(probe, 5), Is.EqualTo(6));
            bool acceptedByLegacyHost = BitConverter.ToInt32(probe, 5) == legacyVersion;
            Assert.That(acceptedByLegacyHost, Is.False);
            byte[] legacyReply = LanUdpPeer.EncodeJoinAck(acceptedByLegacyHost, 1, 991, 8, 1);
            WriteInt(legacyReply, 11, legacyVersion);
            Assert.That(legacyReply.Length, Is.EqualTo(23));
            Assert.That(join.Process(legacyReply, Host, 0.01f, out byte[] response), Is.True);
            Assert.That(response, Is.Null);
            Assert.That(join.IsFailed, Is.True);
            Assert.That(join.IsComplete, Is.False);
        }

        [Test]
        public void NoncePacketsRejectZeroNonceAndEveryPartialNonce()
        {
            byte[] request = LanUdpPeer.EncodeJoin(8, 1, 77UL);
            for (int length = 22; length < request.Length; length++)
            {
                var truncated = new byte[length];
                Array.Copy(request, truncated, length);
                Assert.That(LanUdpPeer.TryDecodeJoin(truncated, out _), Is.False);
            }
            Array.Clear(request, 21, 8);
            Assert.That(LanUdpPeer.TryDecodeJoin(request, out _), Is.False);
            byte[] ack = LanUdpPeer.EncodeJoinAck(true, 1, 991, 8, 1, 77UL);
            Array.Clear(ack, 23, 8);
            Assert.That(LanUdpPeer.TryDecodeJoinAck(ack, out _), Is.False);
        }

        [Test]
        public void TwoSocketHandshakeRecoversEveryFirstPhaseLossWithoutPrebindingHost()
        {
            using (var hostPeer = new LanUdpPeer())
            using (var joinPeer = new LanUdpPeer())
            {
                Assert.That(hostPeer.BindEphemeral(), Is.True);
                Assert.That(joinPeer.BindEphemeral(), Is.True);
                var hostEnd = new IPEndPoint(IPAddress.Loopback, hostPeer.LocalEndPoint.Port);
                var host = new LanHandshake(LanHandshakeRole.Host, 8, 1, 903);
                var join = new LanHandshake(LanHandshakeRole.Joiner, 8, 1, 0, hostEnd, 45UL);
                joinPeer.SetRemote(hostEnd);
                var dropped = new HashSet<string>();
                bool observedUnboundCapability = false;
                for (int step = 0; step < 80 && (!host.IsComplete || !join.IsComplete); step++)
                {
                    float now = step * 0.3f;
                    if (host.TryBuildRetry(now, out byte[] hostRetry)) SendAfterFirstLoss(hostPeer, hostRetry, hostPeer.Remote, dropped);
                    if (join.TryBuildRetry(now, out byte[] joinRetry)) SendAfterFirstLoss(joinPeer, joinRetry, joinPeer.Remote, dropped);
                    Thread.Sleep(1);
                    Drain(hostPeer, host, now, dropped, ref observedUnboundCapability);
                    Drain(joinPeer, join, now, dropped, ref observedUnboundCapability);
                }
                Assert.That(observedUnboundCapability, Is.True);
                Assert.That(host.IsComplete && join.IsComplete, Is.True);
                Assert.That(host.JoinNonce, Is.EqualTo(join.JoinNonce));
                Assert.That(host.MatchId, Is.EqualTo(join.MatchId));
                Assert.That(dropped.Count, Is.EqualTo(7), "Probe, Capability, Join, JoinAck, and all three Ready phases were lost once.");
            }
        }

        static void Drain(LanUdpPeer peer, LanHandshake handshake, float now, HashSet<string> dropped,
            ref bool observedUnboundCapability)
        {
            while (peer.TryRecv(out byte[] data, out IPEndPoint from))
            {
                if (!handshake.Process(data, from, now, out byte[] response)) continue;
                if (response != null && LanUdpPeer.TryDecodeCapability(response, out _))
                {
                    Assert.That(handshake.RemoteEndpoint, Is.Null);
                    Assert.That(peer.Remote, Is.Null);
                    observedUnboundCapability = true;
                }
                if (handshake.RemoteEndpoint != null) peer.SetRemote(handshake.RemoteEndpoint);
                if (response != null) SendAfterFirstLoss(peer, response, from, dropped);
            }
        }

        static void SendAfterFirstLoss(LanUdpPeer peer, byte[] bytes, IPEndPoint endpoint, HashSet<string> dropped)
        {
            Assert.That(LanUdpPeer.TryReadHeader(bytes, out byte type, out _), Is.True);
            string key = type.ToString();
            if (LanUdpPeer.TryDecodeJoin(bytes, out NetJoinMessage join)) key += join.IsCompatibilityProbe ? "-probe" : "-nonce";
            if (LanUdpPeer.TryDecodeReady(bytes, out NetReadyMessage ready)) key += "-" + ready.Phase;
            if (dropped.Add(key)) return;
            Assert.That(peer.SendTo(bytes, endpoint), Is.True);
        }

        static void WriteInt(byte[] bytes, int offset, int value)
        {
            byte[] encoded = BitConverter.GetBytes(value);
            Array.Copy(encoded, 0, bytes, offset, 4);
        }
    }
}
