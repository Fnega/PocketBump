using System;
using System.Net;
using System.Threading;
using NUnit.Framework;

namespace PocketBump.Tests
{
    // Actual loopback UDP and protocol-clock fixtures, not two-computer LAN evidence.
    public sealed class HandfeelNetIdentityTests
    {
        [Test]
        public void CandidateRequiresExplicitDevelopmentGateAndGenericGreyboxRemainsRejected()
        {
            ArenaRulesProfile candidate = ArenaRulesProfile.CreateHandfeelCandidate();
            Assert.That(candidate.IsProvisional, Is.True);
            Assert.Throws<ArgumentException>(() => new DashLanHandshake(LanHandshakeRole.Host, candidate, 401, 91));
            Assert.DoesNotThrow(() => new DashLanHandshake(LanHandshakeRole.Host, candidate, 401, 91,
                allowHandfeelCandidate: true));
            Assert.Throws<ArgumentException>(() => new DashLanHandshake(LanHandshakeRole.Host,
                ArenaRulesProfile.CreateGreybox(), 401, 91, allowHandfeelCandidate: true));
            Assert.That(DashNetcode.Version, Is.EqualTo(8));
            Assert.That(DashRollbackSession.DelayTicks, Is.EqualTo(2));
            Assert.That(DashRollbackSession.BufferTicks, Is.EqualTo(12));
        }

        [Test]
        public void MatchingCandidateSocketsAgreeBeforeFirstTickAndUseUnchangedPacketSizes()
        {
            ArenaRulesProfile rules = ArenaRulesProfile.CreateHandfeelCandidate();
            ArenaRulesProfile old = ArenaRulesProfile.CreateProductionV1();
            Assert.That(rules.ContentHash, Is.Not.EqualTo(old.ContentHash));
            Assert.That(rules.LayoutHash, Is.EqualTo(old.LayoutHash));
            Assert.That(DashNetcode.EncodeJoin(rules, 801).Length, Is.EqualTo(DashNetcode.EncodeJoin(old, 801).Length));
            Assert.That(DashNetcode.EncodeJoinAck(true, 401, rules, 801, 91).Length,
                Is.EqualTo(DashNetcode.EncodeJoinAck(true, 401, old, 801, 91).Length));
            using (var pair = new Pair(rules, rules))
            {
                pair.PumpUntil(() => pair.Host.Ready && pair.Join.Ready);
                Assert.That(pair.Host.State.Tick, Is.Zero);
                Assert.That(pair.Join.State.Tick, Is.Zero);
                Assert.That(pair.Host.Rules.ContentHash, Is.EqualTo(pair.Join.Rules.ContentHash));
                Assert.That(pair.Host.InitializationCount, Is.EqualTo(1));
                Assert.That(pair.Join.InitializationCount, Is.EqualTo(1));
                for (int i = 0; i < 100 && (pair.Host.State.Tick < 20 || pair.Join.State.Tick < 20); i++)
                {
                    if (pair.Host.State.Tick < 20) pair.Host.Advance((_, __) => default);
                    if (pair.Join.State.Tick < 20) pair.Join.Advance((_, __) => default);
                    pair.Pump();
                }
                pair.Pump();
                Assert.That(pair.Host.Frozen || pair.Join.Frozen, Is.False);
                Assert.That(pair.Host.State.Tick, Is.EqualTo(20));
                Assert.That(pair.Join.State.Tick, Is.EqualTo(20));
                Assert.That(pair.Host.State.Checksum(), Is.EqualTo(pair.Join.State.Checksum()));
                Assert.That(pair.Host.AcceptedInputDatagrams, Is.GreaterThan(0));
                Assert.That(pair.Join.AcceptedInputDatagrams, Is.GreaterThan(0));
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void V1AndCandidateSocketsRejectBothDirectionsBeforeStateInitialization(bool candidateHost)
        {
            ArenaRulesProfile old = ArenaRulesProfile.CreateProductionV1();
            ArenaRulesProfile candidate = ArenaRulesProfile.CreateHandfeelCandidate();
            AssertRejected(candidateHost ? candidate : old, candidateHost ? old : candidate);
        }

        [TestCase("grace")] [TestCase("decay")] [TestCase("tail")]
        public void ChangedCandidateParameterRejectsBeforeTickZeroWithoutChangingGeometry(string parameter)
        {
            ArenaRulesProfile original = ArenaRulesProfile.CreateHandfeelCandidate();
            var changed = original.ToBuilder();
            if (parameter == "grace") changed.RecoilGraceTicks++;
            else if (parameter == "decay") changed.ExternalDecay += 1f;
            else changed.RecoilTailMultiplier = 2f;
            ArenaRulesProfile other = changed.Freeze();
            Assert.That(other.LayoutHash, Is.EqualTo(original.LayoutHash));
            Assert.That(other.ContentHash, Is.Not.EqualTo(original.ContentHash));
            AssertRejected(original, other);
        }

        static void AssertRejected(ArenaRulesProfile host, ArenaRulesProfile join)
        {
            using (var pair = new Pair(host, join))
            {
                pair.PumpUntil(() => pair.Host.Frozen && pair.Join.Frozen);
                Assert.That(pair.Host.FailureReason, Is.EqualTo("SETTINGS MISMATCH"));
                Assert.That(pair.Join.FailureReason, Is.EqualTo("SETTINGS MISMATCH"));
                Assert.That(pair.Host.State, Is.Null);
                Assert.That(pair.Join.State, Is.Null);
                Assert.That(pair.Host.Rollback, Is.Null);
                Assert.That(pair.Join.Rollback, Is.Null);
                Assert.That(pair.Host.InitializationCount + pair.Join.InitializationCount, Is.Zero);
                Assert.That(pair.Host.AcceptedInputDatagrams + pair.Join.AcceptedInputDatagrams, Is.Zero);
            }
        }

        sealed class Pair : IDisposable
        {
            public readonly DashLanSession Host, Join;
            float now;
            public Pair(ArenaRulesProfile hostRules, ArenaRulesProfile joinRules)
            {
                Host = DashLanSession.Host(hostRules, 401, 91u, () => now, allowHandfeelCandidate: true);
                try
                {
                    Assert.That(Host.SocketBound, Is.True, Host.LastSocketError);
                    Join = DashLanSession.Join(joinRules, new IPEndPoint(IPAddress.Loopback, Host.LocalEndpoint.Port),
                        () => now, 801u, allowHandfeelCandidate: true);
                    Assert.That(Join.SocketBound, Is.True, Join.LastSocketError);
                }
                catch { Host.Dispose(); Join?.Dispose(); throw; }
            }
            public void Pump()
            {
                Host.Pump(); Join.Pump(); now += .005f; Thread.Sleep(1);
            }
            public void PumpUntil(Func<bool> ready)
            {
                for (int i = 0; i < 500 && !ready(); i++) Pump();
                Assert.That(ready(), Is.True, Host.FailureReason + "/" + Join.FailureReason);
            }
            public void Dispose() { Host.Dispose(); Join.Dispose(); }
        }
    }
}
