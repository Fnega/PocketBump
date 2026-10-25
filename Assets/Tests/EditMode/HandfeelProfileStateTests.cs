using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class HandfeelProfileStateTests
    {
        const string V1Hash = "7012a8bc484f3d522ace17ccc550c9572659c5d0d2f96fa122a83fa27831abb8";
        const string LayoutHash = "13a8a46bb386b63ff20e427546d184a5eb932274c753ea8d6397e69c9dcaa500";

        [TestCase(8, 1)]
        [TestCase(1, 8)]
        [TestCase(21, 3)]
        public void CandidatePreservesAllOtherAuthoringFieldsAndLayout(int points, int balls)
        {
            var legacy = ArenaRulesProfile.CreateProductionV1(points, balls);
            var candidate = ArenaRulesProfile.CreateHandfeelCandidate(points, balls);
            Assert.That(candidate.IsProvisional, Is.True);
            Assert.That(candidate.UsesRefinedHandfeel, Is.True);
            Assert.That(candidate.HandfeelPolicyVersion, Is.EqualTo(1));
            Assert.That(candidate.RecoilGraceTicks, Is.EqualTo(6));
            Assert.That(candidate.ExternalDecay, Is.EqualTo(8f));
            Assert.That(candidate.RecoilTailMultiplier, Is.EqualTo(6f));
            Assert.That(candidate.NormalPlayerImpulse, Is.EqualTo(4.5f));
            Assert.That(candidate.DashPlayerImpulse, Is.EqualTo(10.5f));
            Assert.That(candidate.LayoutHash, Is.EqualTo(legacy.LayoutHash));
            Assert.That(candidate.LayoutHash, Is.EqualTo(LayoutHash));
            Assert.That(candidate.ContentHash, Is.Not.EqualTo(legacy.ContentHash));
            var allowed = new HashSet<string> { "IsProvisional", "HandfeelPolicyVersion", "RecoilGraceTicks", "RecoilTailMultiplier", "NormalPlayerImpulse", "DashPlayerImpulse" };
            var before = legacy.ToBuilder(); var after = candidate.ToBuilder();
            foreach (var field in typeof(ArenaRulesProfile.Builder).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (allowed.Contains(field.Name)) continue;
                Assert.That(field.GetValue(after), Is.EqualTo(field.GetValue(before)), field.Name);
            }
            Assert.That(ArenaRulesProfile.CreateProductionV1().ContentHash, Is.EqualTo(V1Hash));
            Assert.That(legacy.UsesRefinedHandfeel, Is.False);
            Assert.That(legacy.RecoilGraceTicks, Is.Zero);
            Assert.That(legacy.RecoilTailMultiplier, Is.EqualTo(1f));
            Assert.That(legacy.NormalPlayerImpulse, Is.EqualTo(6f));
            Assert.That(legacy.DashPlayerImpulse, Is.EqualTo(14f));
        }

        [Test]
        public void HistoricalCandidatesKeepTheirIdentitiesAndCurrentChangesOnlyPlayerExtras()
        {
            var current = ArenaRulesProfile.CreateHandfeelCandidate();
            var previousBuilder = ArenaRulesProfile.CreateProductionV1().ToBuilder();
            previousBuilder.IsProvisional = true;
            previousBuilder.HandfeelPolicyVersion = 1;
            previousBuilder.RecoilGraceTicks = 6;
            previousBuilder.RecoilTailMultiplier = 3f;
            var previous = previousBuilder.Freeze();
            Assert.That(previous.ContentHash, Is.EqualTo("e5572f9304a694833c2a33eca5603a1cb2e0ee294aa482da6edac93733145c84"));
            previousBuilder.RecoilTailMultiplier = 6f;
            var previousSix = previousBuilder.Freeze();
            Assert.That(previousSix.ContentHash, Is.EqualTo("b8ebd0915032cb3d4171e1cdcc1fe2e45f24a4d63a9478fb47b5c06f87bc28c0"));
            Assert.That(current.ContentHash, Is.Not.EqualTo(previous.ContentHash));
            Assert.That(current.ContentHash, Is.Not.EqualTo(previousSix.ContentHash));
            Assert.That(current.ContentHash, Is.Not.EqualTo(V1Hash));
            Assert.That(current.LayoutHash, Is.EqualTo(previous.LayoutHash).And.EqualTo(LayoutHash));
            Assert.That(current.LayoutHash, Is.EqualTo(previousSix.LayoutHash));
            Assert.That(ArenaRulesProfile.CreateProductionV1().ContentHash, Is.EqualTo(V1Hash));
            var currentBuilder = current.ToBuilder();
            foreach (var field in typeof(ArenaRulesProfile.Builder).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.Name == "NormalPlayerImpulse" || field.Name == "DashPlayerImpulse") continue;
                Assert.That(field.GetValue(currentBuilder), Is.EqualTo(field.GetValue(previousBuilder)), field.Name);
            }
            TestContext.Out.WriteLine("PREVIOUS 3x candidate ContentHash=" + previous.ContentHash);
            TestContext.Out.WriteLine("PREVIOUS 6x candidate, player extras 6/14, ContentHash=" + previousSix.ContentHash);
            TestContext.Out.WriteLine("CURRENT 6x candidate, player extras 4.5/10.5, ContentHash=" + current.ContentHash);
            TestContext.Out.WriteLine("UNCHANGED LayoutHash=" + current.LayoutHash + "; V1 ContentHash=" + V1Hash);
        }

        [Test]
        public void EachCandidateParameterAndApprovalMarkerHasSeparateContentOnlyIdentity()
        {
            var candidate = ArenaRulesProfile.CreateHandfeelCandidate();
            Action<ArenaRulesProfile.Builder>[] changes =
            {
                b => b.HandfeelPolicyVersion = 0,
                b => b.RecoilGraceTicks = 7,
                b => b.ExternalDecay = 9f,
                b => b.RecoilTailMultiplier = 2f,
                b => b.NormalPlayerImpulse = 5f,
                b => b.DashPlayerImpulse = 11f,
                b => b.IsProvisional = false
            };
            var hashes = new HashSet<string> { candidate.ContentHash };
            foreach (var change in changes)
            {
                var builder = candidate.ToBuilder(); change(builder);
                var altered = builder.Freeze();
                Assert.That(hashes.Add(altered.ContentHash), Is.True);
                Assert.That(altered.LayoutHash, Is.EqualTo(candidate.LayoutHash));
            }
            Assert.That(candidate.ToBuilder().Freeze().ContentHash, Is.EqualTo(candidate.ContentHash));
            Assert.That(ArenaRulesProfile.CreateHandfeelCandidate().ContentHash, Is.EqualTo(candidate.ContentHash));
            TestContext.Out.WriteLine("HandfeelCandidate.ContentHash=" + candidate.ContentHash);
            TestContext.Out.WriteLine("HandfeelCandidate.LayoutHash=" + candidate.LayoutHash);
        }

        [Test]
        public void InvalidPolicyAndRecoilParametersAreRejectedBeforePlay()
        {
            Action<ArenaRulesProfile.Builder>[] invalid =
            {
                b => b.HandfeelPolicyVersion = -1, b => b.HandfeelPolicyVersion = 2,
                b => b.RecoilGraceTicks = -1, b => b.RecoilGraceTicks = 360001,
                b => b.RecoilTailMultiplier = .99f, b => b.RecoilTailMultiplier = float.NaN,
                b => b.RecoilTailMultiplier = float.PositiveInfinity
            };
            foreach (var change in invalid)
            {
                var builder = ArenaRulesProfile.Builder.CreateHandfeelCandidate(); change(builder);
                Assert.Throws<ArgumentException>(() => builder.Freeze());
            }
        }

        [Test]
        public void CandidateStageAloneIsChecksummedClonedAndRestoredForBothSeats()
        {
            var state = DashArenaSimulation.Create(ArenaRulesProfile.CreateHandfeelCandidate(), 12345);
            int before = state.Checksum();
            for (int seat = 0; seat < 2; seat++)
            {
                var changed = state.Clone();
                changed.Players[seat].RecoilGraceTicks = 6;
                Assert.That(changed.Checksum(), Is.Not.EqualTo(before));
                var clone = changed.Clone();
                Assert.That(clone.Players[seat].RecoilGraceTicks, Is.EqualTo(6));
                Assert.That(clone.Checksum(), Is.EqualTo(changed.Checksum()));
                var restored = DashArenaSimulation.Create(ArenaRulesProfile.CreateProductionV1(), 999);
                restored.CopyFrom(changed);
                Assert.That(restored.Rules, Is.SameAs(changed.Rules));
                Assert.That(restored.Players[seat].RecoilGraceTicks, Is.EqualTo(6));
                Assert.That(restored.Checksum(), Is.EqualTo(changed.Checksum()));
                clone.Players[seat].RecoilGraceTicks = 1;
                Assert.That(changed.Players[seat].RecoilGraceTicks, Is.EqualTo(6));
            }
        }

        [Test]
        public void LegacySchemaExplicitlyOmitsInactiveExtensionAndSimulationKeepsItZero()
        {
            var state = DashArenaSimulation.Create(ArenaRulesProfile.CreateProductionV1(), 12345);
            int original = state.Checksum();
            // An artificial inactive-field mutation cannot rewrite schema 1's bytes.
            state.Players[0].RecoilGraceTicks = 6;
            Assert.That(state.Checksum(), Is.EqualTo(original));
            state.Phase = MatchState.Live; state.GateClosed = false;
            state.Players[0].Position = new Vector2(0, -18);
            state.Players[1].Position = new Vector2(10, 26); // Keep the decay fixture clear of seat one's spawn.
            state.Players[0].ExternalVelocity = Vector2.right * 12;
            DashArenaSimulation.Step(state, default, default);
            Assert.That(state.Players[0].RecoilGraceTicks, Is.Zero);
            Assert.That(state.Players[0].ExternalVelocity.x, Is.EqualTo(12f - 8f / 60f).Within(.00001f));
            for (int i = 0; i < 6; i++) DashArenaSimulation.Step(state, default, default);
            Assert.That(state.Players[0].RecoilGraceTicks, Is.Zero);
            Assert.That(state.Players[0].ExternalVelocity.x, Is.EqualTo(12f - 7f * 8f / 60f).Within(.00001f));
        }

        [Test]
        public void LegacyChecksumMatchesIndependentPreChangeSchemaOneReference()
        {
            // Reference encoding transcribed from the pre-change schema, not a newly
            // observed checksum presented as a historical runtime golden measurement.
            var state = DashArenaSimulation.Create(ArenaRulesProfile.CreateProductionV1(ballCount: 3), 0x12345678u, 2);
            Assert.That(state.Checksum(), Is.EqualTo(LegacySchemaOneReference(state)));
            DashArenaSimulation.StartMatch(state);
            for (int tick = 0; tick < 20; tick++)
            {
                DashArenaSimulation.Step(state, new DashArenaInput { Move = Vector2.right }, default);
                Assert.That(state.Checksum(), Is.EqualTo(LegacySchemaOneReference(state)));
            }
            state.Players[0].ExternalVelocity = new Vector2(-5.25f, 6.5f);
            state.Players[1].RecoilGraceTicks = 6; // Inactive extension remains absent.
            state.Balls[0] = new DashBallState { Active = true, Position = Vector2.one,
                Velocity = new Vector2(2, -3), DangerousImpactCount = 7 };
            state.Walls[0] = new DashWallState { Broken = true, RepairPending = true, RepairTicks = 5 };
            state.Pickups[0] = new DashPickupState { Active = true, Id = 19, Kind = DashPickupKind.Purple, Position = Vector2.down };
            state.PlayerBallContacts[2] = state.CylinderContacts[3] = true;
            Assert.That(state.Checksum(), Is.EqualTo(LegacySchemaOneReference(state)));
        }

        static int LegacySchemaOneReference(DashArenaState s)
        {
            var bytes = new List<byte>();
            void Word(uint value) { for (int shift = 0; shift < 32; shift += 8) bytes.Add((byte)(value >> shift)); }
            void Integer(int value) => Word(unchecked((uint)value));
            void Flag(bool value) => Integer(value ? 1 : 0);
            void Vector(Vector2 value)
            {
                Word(BitConverter.ToUInt32(BitConverter.GetBytes(value.x), 0));
                Word(BitConverter.ToUInt32(BitConverter.GetBytes(value.y), 0));
            }
            void Flags(bool[] values) { Integer(values.Length); foreach (bool value in values) Flag(value); }
            Integer(1); Integer(s.Rules.ContentHash.Length);
            foreach (char c in s.Rules.ContentHash) { bytes.Add((byte)c); bytes.Add((byte)(c >> 8)); }
            Integer(s.Tick); Integer(s.Epoch); Integer((int)s.Phase); Integer(s.PhaseTicks);
            Flag(s.GateClosed); Integer(s.LastScorer); Word(s.InitialSeed); Word(s.RandomState);
            Integer(s.NextPickupId); Integer(s.SpawnTicks);
            Integer(s.Scores.Length); foreach (int score in s.Scores) Integer(score);
            Integer(s.Players.Length);
            foreach (var p in s.Players)
            {
                Vector(p.Position); Vector(p.ControlVelocity); Vector(p.ExternalVelocity);
                Vector(p.Facing); Vector(p.DashDirection); Flag(p.Alive);
                Integer(p.DashTicks); Integer(p.DashCooldownTicks); Integer(p.StunTicks);
                Integer(p.RespawnTicks); Integer(p.GreenTicks); Integer(p.PurpleTicks);
            }
            Integer(s.Balls.Length);
            foreach (var b in s.Balls) { Flag(b.Active); Vector(b.Position); Vector(b.Velocity); Integer(b.DangerousImpactCount); }
            Integer(s.Walls.Length);
            foreach (var wall in s.Walls) { Flag(wall.Broken); Flag(wall.RepairPending); Integer(wall.RepairTicks); }
            Integer(s.Pickups.Length);
            foreach (var item in s.Pickups) { Flag(item.Active); Integer(item.Id); Integer((int)item.Kind); Vector(item.Position); }
            Flags(s.PlayerContacts); Flags(s.PlayerBallContacts); Flags(s.CylinderContacts); Flags(s.WallContacts); Flags(s.BallWallContacts);
            uint hash = 2166136261u;
            foreach (byte value in bytes) hash = unchecked((hash ^ value) * 16777619u);
            return unchecked((int)hash);
        }
    }
}
