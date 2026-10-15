using NUnit.Framework;

namespace PocketBump.Tests
{
    public sealed class LanFaultPacketInfoTests
    {
        [TestCase(6)] [TestCase(DashNetcode.Version)]
        public void InputFaultRoutingReadsTheActualWireVersion(int protocol)
        {
            var packet = new NetInputPacket { MatchId = 72, Epoch = 3, Seat = 1,
                NewestTick = 8, HistoryCount = 1, InputBits = new byte[] { 0x11 }, AckTick = 5, ChecksumTick = -1 };
            byte[] bytes = protocol == DashNetcode.Version ? DashNetcode.EncodeInput(packet) : LanUdpPeer.EncodeInput(packet);
            LanFaultPacketInfo info = LanFaultPacketInfo.Read(bytes);
            Assert.That(info.Protocol, Is.EqualTo(protocol));
            Assert.That(info.IsInput && !info.IsControl, Is.True);
            Assert.That(info.Epoch, Is.EqualTo(3));
            Assert.That(info.NewestTick, Is.EqualTo(8));
            Assert.That(info.AckTick, Is.EqualTo(5));
            Assert.That(info.MatchId, Is.EqualTo(72));
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)] [TestCase(10)]
        public void EveryDashControlKindCanBeTargetedWithoutLegacyDecoding(int kind)
        {
            var rules = ArenaRulesProfile.CreateProductionV1();
            var message = new DashLifecycleMessage { Kind = (NetLifecycleKind)kind,
                MatchId = 73, Epoch = 2, NextEpoch = kind >= 6 ? 3 : 2,
                Seed = 41, NextSeed = kind >= 6 ? 42u : 0, ContentHash = rules.ContentHash,
                Finish = new NetFinishTuple { FinishTick = 200, Checksum = 18, Score0 = 8, Score1 = 1,
                    Winner = 0, SettingsHash = DashNetcode.ContentTag(rules.ContentHash) } };
            LanFaultPacketInfo info = LanFaultPacketInfo.Read(DashNetcode.EncodeLifecycle(message));
            Assert.That(info.IsControl && !info.IsInput, Is.True);
            Assert.That(info.Protocol, Is.EqualTo(DashNetcode.Version));
            Assert.That(info.Kind, Is.EqualTo((NetLifecycleKind)kind));
            Assert.That(info.MatchId, Is.EqualTo(73)); Assert.That(info.Epoch, Is.EqualTo(2));
        }

        [Test]
        public void UnknownAndTruncatedPayloadsAreNotMisclassifiedAsFaultTargets()
        {
            Assert.That(LanFaultPacketInfo.Read(null).Protocol, Is.Zero);
            Assert.That(LanFaultPacketInfo.Read(new byte[5]).Protocol, Is.Zero);
            Assert.That(LanFaultPacketInfo.Read(DashNetcode.EncodeGoodbye(3, 0)).IsInput, Is.False);
        }
    }
}
