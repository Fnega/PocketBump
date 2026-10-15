using System;
using System.Collections.Generic;
using System.Net;
using NUnit.Framework;

namespace PocketBump.Tests
{
    public sealed class DashNetcodeTests
    {
        static readonly IPEndPoint HostAddress = new IPEndPoint(IPAddress.Loopback, 18001);
        static readonly IPEndPoint JoinAddress = new IPEndPoint(IPAddress.Loopback, 18002);
        static ArenaRulesProfile Rules => ArenaRulesProfile.CreateProductionV1();
        static DashLanHandshake Host(ArenaRulesProfile rules = null) =>
            new DashLanHandshake(LanHandshakeRole.Host, rules ?? Rules, 1701, 912345);
        static DashLanHandshake Join(ArenaRulesProfile rules = null) =>
            new DashLanHandshake(LanHandshakeRole.Joiner, rules ?? Rules, 0, 0, HostAddress, 721);

        static void Capability(DashLanHandshake host, DashLanHandshake join)
        {
            Assert.That(join.TryBuildRetry(0, out var probe), Is.True);
            Assert.That(host.Process(probe, JoinAddress, 0, out var capability), Is.True);
            Assert.That(host.RemoteEndpoint, Is.Null, "Capability probing cannot claim a participant.");
            Assert.That(join.Process(capability, HostAddress, 0, out _), Is.True);
        }

        [Test]
        public void HandshakeAgreesFullContentSeedNonceAndSeatsIdempotently()
        {
            var host = Host(); var join = Join(); Capability(host, join);
            byte[] request = DashNetcode.EncodeJoin(Rules, 721);
            Assert.That(host.Process(request, JoinAddress, 1, out var ack), Is.True);
            Assert.That(join.Process(ack, HostAddress, 1, out var ready), Is.True);
            Assert.That(host.Process(ready, JoinAddress, 1, out var hostReady), Is.True);
            Assert.That(join.Process(hostReady, HostAddress, 1, out var confirm), Is.True);
            Assert.That(host.Process(confirm, JoinAddress, 1, out _), Is.True);
            Assert.That(host.IsComplete && join.IsComplete, Is.True);
            Assert.That(join.Seed, Is.EqualTo(host.Seed)); Assert.That(join.Seed, Is.EqualTo(912345));
            Assert.That(join.LocalSeat, Is.EqualTo(1)); Assert.That(host.LocalSeat, Is.Zero);
            Assert.That(join.Rules.ContentHash, Is.EqualTo(host.Rules.ContentHash));
            Assert.That(host.Process(request, JoinAddress, 2, out var retryAck), Is.True);
            CollectionAssert.AreEqual(ack, retryAck);
            Assert.That(join.Process(retryAck, HostAddress, 2, out var retryConfirm), Is.True);
            CollectionAssert.AreEqual(confirm, retryConfirm);
        }

        [Test]
        public void RetryRecoversEachDroppedInitialPhaseWithoutChangingInitialization()
        {
            for (int drop = 0; drop < 7; drop++)
            {
                var host = Host(); var join = Join();
                var queue = new Queue<(bool toHost, byte[] bytes)>();
                int sequence = 0, dropped = 0;
                for (int i = 0; i < 100 && !(host.IsComplete && join.IsComplete); i++)
                {
                    float now = i * .1f;
                    if (join.TryBuildRetry(now, out var request)) queue.Enqueue((true, request));
                    if (host.TryBuildRetry(now, out var retry)) queue.Enqueue((false, retry));
                    int budget = 20;
                    while (queue.Count > 0 && budget-- > 0)
                    {
                        var item = queue.Dequeue();
                        if (sequence++ == drop) { dropped++; continue; }
                        bool accepted = item.toHost
                            ? host.Process(item.bytes, JoinAddress, now, out var response)
                            : join.Process(item.bytes, HostAddress, now, out response);
                        if (accepted && response != null) queue.Enqueue((!item.toHost, response));
                    }
                }
                Assert.That(dropped, Is.EqualTo(1), "phase " + drop);
                Assert.That(host.IsComplete && join.IsComplete, Is.True, "phase " + drop);
                Assert.That(join.Seed, Is.EqualTo(912345));
                Assert.That(queue, Is.Empty, "Completed peers must not echo HostReady/Confirm forever.");
            }
        }

        [Test]
        public void ChangedLayoutOrTuningFailsBeforeAnyPlayableState()
        {
            var changed = Rules.ToBuilder(); changed.MaxExternalSpeed += 1;
            var host = Host(); var join = Join(changed.Freeze()); Capability(host, join);
            Assert.That(host.Process(DashNetcode.EncodeJoin(join.Rules, 721), JoinAddress, 1, out var rejection), Is.True);
            Assert.That(join.Process(rejection, HostAddress, 1, out _), Is.True);
            Assert.That(host.IsFailed && join.IsFailed, Is.True);
            Assert.That(join.MatchId, Is.Zero); Assert.That(join.Seed, Is.Zero);
        }

        [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
        public void LegacyJoinerGetsParsableExplicitRejection(int legacyVersion)
        {
            byte[] probe = LanUdpPeer.EncodeJoin(8, 1); SetInt(probe, 5, legacyVersion);
            var host = Host();
            Assert.That(host.Process(probe, JoinAddress, 0, out var rejection), Is.True);
            Assert.That(LanUdpPeer.TryDecodeJoinAck(rejection, out var ack), Is.True);
            Assert.That(ack.Accepted, Is.False); Assert.That(ack.Version, Is.EqualTo(DashNetcode.Version));
            Assert.That(host.IsFailed, Is.True);
        }

        [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
        public void NewJoinerRejectsLegacyHostBeforeInitialization(int legacyVersion)
        {
            var join = Join();
            byte[] ack = LanUdpPeer.EncodeJoinAck(true, 1, 71, 8, 1); SetInt(ack, 11, legacyVersion);
            Assert.That(join.Process(ack, HostAddress, 0, out _), Is.True);
            Assert.That(join.IsFailed, Is.True); Assert.That(join.MatchId, Is.Zero);
            Assert.That(join.FailureMessage, Is.EqualTo("SETTINGS MISMATCH"));
        }

        [Test]
        public void ProtocolSevenCapabilityIsRejectedBeforeModernJoinOrPlayableState()
        {
            var join = Join();
            byte[] capability = DashNetcode.EncodeCapability(); SetInt(capability, 5, 7);
            Assert.That(join.Process(capability, HostAddress, 0, out byte[] response), Is.True);
            Assert.That(response, Is.Null);
            Assert.That(join.IsFailed, Is.True);
            Assert.That(join.FailureMessage, Is.EqualTo("SETTINGS MISMATCH"));
            Assert.That(join.MatchId, Is.Zero); Assert.That(join.Seed, Is.Zero);
            byte[] oldJoin = DashNetcode.EncodeJoin(Rules, 721); SetInt(oldJoin, 5, 7);
            Assert.That(DashNetcode.TryDecodeJoin(oldJoin, out _), Is.False);
            byte[] oldInput = DashNetcode.EncodeInput(Input()); SetInt(oldInput, 5, 7);
            Assert.That(DashNetcode.TryDecodeInput(oldInput, out _), Is.False);
        }

        [Test]
        public void FaultCodecHasBoundedReasonSeatAndIdentityAndDoesNotChangeLegacyCodec()
        {
            foreach (DashPeerFaultReason reason in Enum.GetValues(typeof(DashPeerFaultReason)))
            {
                var message = new DashPeerFaultMessage { MatchId = 17, Epoch = 2, Seat = 1, Reason = reason };
                byte[] bytes = DashNetcode.EncodeFault(message);
                Assert.That(bytes.Length, Is.EqualTo(19));
                Assert.That(DashNetcode.TryDecodeFault(bytes, out var decoded), Is.True);
                Assert.That(decoded.MatchId, Is.EqualTo(17)); Assert.That(decoded.Epoch, Is.EqualTo(2));
                Assert.That(decoded.Seat, Is.EqualTo(1)); Assert.That(decoded.Reason, Is.EqualTo(reason));
                Assert.That(DashPeerFault.FromFailureReason(DashPeerFault.ToFailureReason(reason)), Is.EqualTo(reason));
                Assert.That(LanUdpPeer.TryReadHeader(bytes, out _, out _), Is.False);
                Assert.That(DashNetcode.TryDecodeGoodbye(bytes, out _), Is.False);
                CheckLengths(bytes, candidate => DashNetcode.TryDecodeFault(candidate, out _));
            }
            byte[] valid = DashNetcode.EncodeFault(new DashPeerFaultMessage
            { MatchId = 17, Epoch = 2, Seat = 1, Reason = DashPeerFaultReason.PeerTimeout });
            foreach (int reason in new[] { 0, 16, 255 })
            {
                byte[] bad = (byte[])valid.Clone(); bad[18] = (byte)reason;
                Assert.That(DashNetcode.TryDecodeFault(bad, out _), Is.False);
            }
            byte[] wrongSeat = (byte[])valid.Clone(); wrongSeat[17] = 2;
            Assert.That(DashNetcode.TryDecodeFault(wrongSeat, out _), Is.False);
            foreach (var invalid in new[] { (offset: 5, value: 7), (offset: 9, value: 0), (offset: 13, value: -1) })
            {
                byte[] bad = (byte[])valid.Clone(); SetInt(bad, invalid.offset, invalid.value);
                Assert.That(DashNetcode.TryDecodeFault(bad, out _), Is.False);
            }
            Assert.Throws<ArgumentException>(() => DashNetcode.EncodeFault(new DashPeerFaultMessage
            { MatchId = 17, Epoch = 2, Seat = 1, Reason = (DashPeerFaultReason)255 }));
            Assert.That(NetplayProtocol.Version, Is.EqualTo(6)); Assert.That(DashNetcode.Version, Is.EqualTo(8));
        }

        [Test]
        public void FreshNonceWrongEndpointAndChangedSeedCannotReplaceAcceptedInitialization()
        {
            var host = Host(); var join = Join(); Capability(host, join);
            Assert.That(host.Process(DashNetcode.EncodeJoin(Rules, 721), JoinAddress, 1, out var ack), Is.True);
            Assert.That(join.Process(ack, HostAddress, 1, out _), Is.True);
            Assert.That(host.Process(DashNetcode.EncodeJoin(Rules, 722), JoinAddress, 2, out _), Is.False);
            Assert.That(host.Process(DashNetcode.EncodeJoin(Rules, 721), HostAddress, 2, out _), Is.False);
            Assert.That(join.Process(DashNetcode.EncodeJoinAck(true, 1701, Rules, 721, 345), HostAddress, 2, out _), Is.False);
            Assert.That(host.Process(DashNetcode.EncodeReady(1701, 722, ReadyPhase.JoinerReady), JoinAddress, 2, out _), Is.False);
            Assert.That(join.Seed, Is.EqualTo(912345));
        }

        static NetInputPacket Input() => new NetInputPacket
        {
            MatchId = 17, Epoch = 2, Seat = 1, NewestTick = 9, HistoryCount = 3,
            InputBits = new byte[] { 0x11, 0x09, 0 }, AckTick = 8, ChecksumTick = 7, Checksum = 99
        };

        [Test]
        public void InputRoundTripsOnlyNewVocabularyAndRejectsV6()
        {
            byte[] bytes = DashNetcode.EncodeInput(Input());
            Assert.That(DashNetcode.TryDecodeInput(bytes, out var decoded), Is.True);
            CollectionAssert.AreEqual(decoded.InputBits, Input().InputBits);
            Assert.That(decoded.Epoch, Is.EqualTo(2)); Assert.That(decoded.Checksum, Is.EqualTo(99));
            Assert.That(LanUdpPeer.TryDecodeInput(bytes, out _), Is.False);
            Assert.That(DashNetcode.TryDecodeInput(LanUdpPeer.EncodeInput(Input()), out _), Is.False);
        }

        [TestCase(0x20)] [TestCase(0x40)] [TestCase(0x80)] [TestCase(0x03)] [TestCase(0x0c)]
        public void MalformedInputBitsAreRejected(int bits)
        {
            byte[] bytes = DashNetcode.EncodeInput(Input()); bytes[23] = (byte)bits;
            Assert.That(DashNetcode.TryDecodeInput(bytes, out _), Is.False);
        }

        [Test]
        public void CodecRejectsEveryTruncationAndTrailingByteWithoutThrowing()
        {
            var rules = Rules;
            var controls = new DashLifecycleMessage
            {
                Kind = NetLifecycleKind.Offer, MatchId = 11, Epoch = 0, NextEpoch = 1, Seat = 0,
                ContentHash = rules.ContentHash, Seed = 21, NextSeed = 22,
                Finish = new NetFinishTuple { FinishTick = 400, Checksum = 41, Score0 = 8, Score1 = 5, Winner = 0, SettingsHash = DashNetcode.ContentTag(rules.ContentHash) }
            };
            CheckLengths(DashNetcode.EncodeJoin(rules, 711), b => DashNetcode.TryDecodeJoin(b, out _));
            CheckLengths(DashNetcode.EncodeJoinAck(true, 11, rules, 711, 21), b => DashNetcode.TryDecodeJoinAck(b, out _));
            CheckLengths(DashNetcode.EncodeReady(11, 711, ReadyPhase.JoinerReady), b => DashNetcode.TryDecodeReady(b, out _));
            CheckLengths(DashNetcode.EncodeInput(Input()), b => DashNetcode.TryDecodeInput(b, out _));
            CheckLengths(DashNetcode.EncodeGoodbye(11, 1), b => DashNetcode.TryDecodeGoodbye(b, out _));
            CheckLengths(DashNetcode.EncodeLifecycle(controls), b => DashNetcode.TryDecodeLifecycle(b, out _));
            Assert.That(DashNetcode.TryDecodeLifecycle(DashNetcode.EncodeLifecycle(controls), out var decoded), Is.True);
            Assert.That(decoded.ContentHash, Is.EqualTo(rules.ContentHash));
            Assert.That(decoded.Seed, Is.EqualTo(21)); Assert.That(decoded.NextSeed, Is.EqualTo(22));
            controls.Kind = NetLifecycleKind.Heartbeat;
            CheckLengths(DashNetcode.EncodeLifecycle(controls), b => DashNetcode.TryDecodeLifecycle(b, out _));
        }

        [Test]
        public void SessionRangesAndMalformedLengthsNeverMutateState()
        {
            byte[] bytes = DashNetcode.EncodeInput(Input());
            SetInt(bytes, 17, int.MaxValue);
            Assert.That(DashNetcode.TryDecodeInput(bytes, out _), Is.False);
            bytes = DashNetcode.EncodeInput(Input()); SetInt(bytes, 13, -1);
            Assert.That(DashNetcode.TryDecodeInput(bytes, out _), Is.False);
            bytes = DashNetcode.EncodeJoinAck(true, 17, Rules, 721, 123); SetInt(bytes, 63, 0);
            Assert.That(DashNetcode.TryDecodeJoinAck(bytes, out _), Is.False);
        }

        static void CheckLengths(byte[] valid, Func<byte[], bool> decode)
        {
            Assert.That(decode(valid), Is.True); Assert.That(decode(null), Is.False);
            for (int length = 0; length < valid.Length; length++)
            {
                byte[] truncated = new byte[length]; Array.Copy(valid, truncated, length);
                Assert.That(decode(truncated), Is.False, "length " + length);
            }
            byte[] extended = new byte[valid.Length + 1]; Array.Copy(valid, extended, valid.Length);
            Assert.That(decode(extended), Is.False);
        }

        static void SetInt(byte[] bytes, int offset, int value)
        { Array.Copy(BitConverter.GetBytes(value), 0, bytes, offset, 4); }
    }
}
