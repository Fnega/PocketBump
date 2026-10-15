using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class ArenaRulesProfileTests
    {
        [Test]
        public void GreyboxRemainsProvisionalEvenWithApprovedProductionPreset()
        {
            ArenaRulesProfile profile = ArenaRulesProfile.CreateGreybox();
            Assert.That(profile.IsProvisional, Is.True, "A preview factory must not silently become a production preset.");
            Assert.That(profile.Width, Is.EqualTo(48f));
            Assert.That(profile.Height, Is.EqualTo(72f));
            Assert.That(profile.PlayerRadius, Is.EqualTo(.78f));
            Assert.That(profile.BallRadius, Is.EqualTo(.3f));
            Assert.That(profile.GoalWidth, Is.EqualTo(3.4f));
            Assert.That(profile.DeathOpening, Is.EqualTo(9.675f));
            Assert.That(profile.DashTicks, Is.EqualTo(18));
            Assert.That(profile.DashCooldownTicks, Is.EqualTo(180));
            Assert.That(profile.StunTicks, Is.EqualTo(36));
            Assert.That(profile.RespawnTicks, Is.EqualTo(180));
            Assert.That(profile.WallRepairTicks, Is.EqualTo(600));
            Assert.That(profile.GreenTicks, Is.EqualTo(300));
            Assert.That(profile.PurpleTicks, Is.EqualTo(300));
            Assert.That(profile.GreenImpactMultiplier, Is.EqualTo(1.5f));
            Assert.That(profile.BuffSpawnTicks, Is.EqualTo(600));
            Assert.That(profile.PickupLimit, Is.EqualTo(3));
            Assert.That(profile.MaxExternalSpeed, Is.EqualTo(72f));
            Assert.That(profile.DashSpeed * profile.DashTicks / profile.TickRate, Is.EqualTo(7.2f).Within(.0001f));
            Assert.That(profile.ContentHash.Length, Is.EqualTo(64));
            Assert.That(profile.LayoutHash.Length, Is.EqualTo(64));
        }

        [Test]
        public void FreezeOwnsAllArraysAndReadOnlyViewsCannotMutateThem()
        {
            ArenaRulesProfile.Builder builder = ArenaRulesProfile.Builder.CreateGreybox(ballCount: 8);
            ArenaRulesProfile profile = builder.Freeze();
            string originalHash = profile.ContentHash;
            builder.Pillars[0] = new ArenaPillarDefinition(999, Vector2.zero, 2f);
            builder.Walls[0] = new ArenaWallDefinition(998, Vector2.zero, Vector2.one);
            builder.Spawns[0] = Vector2.zero;
            builder.Respawn0[0] = Vector2.zero;
            builder.Respawn1[0] = Vector2.zero;
            builder.BallSpawns[0] = Vector2.one;
            builder.PickupCandidates[0] = Vector2.zero;
            builder.TeleportCandidates[0] = Vector2.zero;
            builder.DashSpeed = 99f;
            Assert.That(profile.Pillars[0].Id, Is.EqualTo(100));
            Assert.That(profile.Walls[0].Id, Is.EqualTo(200));
            Assert.That(profile.SpawnForSeat(0), Is.EqualTo(new Vector2(0f, 18f)));
            Assert.That(profile.RespawnCandidates(0)[0], Is.EqualTo(new Vector2(0f, 18f)));
            Assert.That(profile.RespawnCandidates(1)[0], Is.EqualTo(new Vector2(0f, -18f)));
            Assert.That(profile.BallSpawn(0), Is.EqualTo(Vector2.zero));
            Assert.That(profile.PickupCandidates[0], Is.EqualTo(new Vector2(0f, 8f)));
            Assert.That(profile.TeleportCandidates[0], Is.EqualTo(new Vector2(-10f, -22f)));
            Assert.That(profile.DashSpeed, Is.EqualTo(24f));
            Assert.Throws<NotSupportedException>(() => ((IList<Vector2>)profile.PickupCandidates)[0] = Vector2.zero);
            Assert.Throws<NotSupportedException>(() => ((IList<ArenaWallDefinition>)profile.Walls)[0] = builder.Walls[0]);
            ArenaRulesProfile.Builder editable = profile.ToBuilder();
            editable.Respawn0[0] = Vector2.zero;
            Assert.That(profile.RespawnCandidates(0)[0], Is.EqualTo(new Vector2(0f, 18f)));
            Assert.That(profile.ContentHash, Is.EqualTo(originalHash));
        }

        [Test]
        public void IdentityIsCultureIndependentAndCanonicalizesObstacleIdsOnly()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                ArenaRulesProfile first = ArenaRulesProfile.CreateGreybox();
                CultureInfo.CurrentCulture = new CultureInfo("en-US");
                ArenaRulesProfile.Builder builder = ArenaRulesProfile.Builder.CreateGreybox();
                Array.Reverse(builder.Pillars); Array.Reverse(builder.Walls);
                ArenaRulesProfile second = builder.Freeze();
                Assert.That(second.ContentHash, Is.EqualTo(first.ContentHash));
                Assert.That(second.LayoutHash, Is.EqualTo(first.LayoutHash));
                Vector2 temporary = builder.TeleportCandidates[0];
                builder.TeleportCandidates[0] = builder.TeleportCandidates[1];
                builder.TeleportCandidates[1] = temporary;
                Assert.That(builder.Freeze().ContentHash, Is.Not.EqualTo(first.ContentHash),
                    "Candidate ordering affects deterministic selection and cannot be sorted away.");
                Assert.That(ArenaRulesProfile.CreateGreybox(ballCount: 8).LayoutHash, Is.EqualTo(first.LayoutHash));
                Assert.That(ArenaRulesProfile.CreateGreybox(ballCount: 8).ContentHash, Is.Not.EqualTo(first.ContentHash));
            }
            finally { CultureInfo.CurrentCulture = original; }
        }

        [Test]
        public void EveryExposedScalarAuthoringFieldParticipatesInIdentity()
        {
            ArenaRulesProfile original = ArenaRulesProfile.CreateGreybox();
            foreach (FieldInfo field in typeof(ArenaRulesProfile.Builder).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.FieldType.IsArray) continue;
                ArenaRulesProfile.Builder builder = original.ToBuilder();
                if (field.FieldType == typeof(float)) field.SetValue(builder, (float)field.GetValue(builder) + .001f);
                else if (field.FieldType == typeof(int)) field.SetValue(builder, (int)field.GetValue(builder) + 1);
                else if (field.FieldType == typeof(bool)) field.SetValue(builder, !(bool)field.GetValue(builder));
                else Assert.Fail("Uncovered authoring field: " + field.Name);
                Assert.That(builder.Freeze().ContentHash, Is.Not.EqualTo(original.ContentHash), field.Name);
            }
        }

        [Test]
        public void GeometryIdentityIncludesDefinitionAndCandidateChanges()
        {
            ArenaRulesProfile original = ArenaRulesProfile.CreateGreybox();
            foreach (string fieldName in new[] { "Spawns", "Respawn0", "Respawn1", "BallSpawns", "PickupCandidates", "TeleportCandidates" })
            {
                ArenaRulesProfile.Builder builder = original.ToBuilder();
                var points = (Vector2[])typeof(ArenaRulesProfile.Builder).GetField(fieldName).GetValue(builder);
                points[0] += Vector2.right * .01f;
                Assert.That(builder.Freeze().LayoutHash, Is.Not.EqualTo(original.LayoutHash), fieldName);
            }
            ArenaRulesProfile.Builder changed = original.ToBuilder();
            ArenaPillarDefinition pillar = changed.Pillars[0];
            changed.Pillars[0] = new ArenaPillarDefinition(pillar.Id, pillar.Position, pillar.Radius + .01f);
            Assert.That(changed.Freeze().LayoutHash, Is.Not.EqualTo(original.LayoutHash));
            changed = original.ToBuilder();
            ArenaWallDefinition wall = changed.Walls[0];
            changed.Walls[0] = new ArenaWallDefinition(wall.Id + 10, wall.Position, wall.HalfSize);
            Assert.That(changed.Freeze().LayoutHash, Is.Not.EqualTo(original.LayoutHash));
        }

        [Test]
        public void EightBallLayoutHasFullClearFootprintsAndRoutesForEverySeatAndCandidate()
        {
            ArenaRulesProfile profile = ArenaRulesProfile.CreateGreybox(ballCount: 8);
            for (int seat = 0; seat < 2; seat++)
            {
                Assert.That(ArenaGeometry.IsFootprintClear(profile, profile.SpawnForSeat(seat), profile.PlayerRadius), Is.True);
                foreach (Vector2 point in profile.RespawnCandidates(seat))
                    Assert.That(ArenaGeometry.IsFootprintClear(profile, point, profile.PlayerRadius), Is.True);
                Assert.That(ArenaGeometry.IsRouteSegmentClear(profile, profile.SpawnForSeat(seat), Vector2.zero, profile.PlayerRadius), Is.True);
            }
            for (int i = 0; i < profile.BallCount; i++)
            {
                Assert.That(ArenaGeometry.IsFootprintClear(profile, profile.BallSpawn(i), profile.BallRadius), Is.True);
                for (int j = 0; j < i; j++)
                    Assert.That(ArenaGeometry.CirclesOverlap(profile.BallSpawn(i), profile.BallRadius,
                        profile.BallSpawn(j), profile.BallRadius), Is.False);
            }
            foreach (Vector2 point in profile.TeleportCandidates)
                Assert.That(ArenaGeometry.IsFootprintClear(profile, point, profile.PlayerRadius + profile.RepairClearance), Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => profile.SpawnForSeat(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => profile.BallSpawn(8));
        }

        [Test]
        public void InvalidDimensionsDurationsCountsAndNumbersFailBeforeMatch()
        {
            AssertInvalid(b => b.Width = float.NaN);
            AssertInvalid(b => b.DashSpeed = float.PositiveInfinity);
            AssertInvalid(b => b.PlayerRadius = 0f);
            AssertInvalid(b => b.Height = 1000f);
            AssertInvalid(b => b.GoalWidth = .5f);
            AssertInvalid(b => b.DeathOpening = 1f);
            AssertInvalid(b => b.BallCount = 9);
            AssertInvalid(b => b.PointsToWin = 0);
            AssertInvalid(b => b.PointsToWin = 22);
            AssertInvalid(b => b.DashTicks = 0);
            AssertInvalid(b => b.PickupLimit = 17);
            AssertInvalid(b => b.WallBreakSpeed = 100f);
            AssertInvalid(b => b.DangerousEnergyRetention = 1.1f);
            AssertInvalid(b => b.GreenImpactMultiplier = .5f);
            AssertInvalid(b => b.Acceleration = float.MaxValue);
            AssertInvalid(b => b.Spawns = null);
            AssertInvalid(b => b.PickupCandidates = Array.Empty<Vector2>());
            AssertInvalid(b => b.TeleportCandidates = Array.Empty<Vector2>());
        }

        [Test]
        public void InvalidIdsOverlapsAndOutOfBoundsFootprintsFailBeforeMatch()
        {
            AssertInvalid(b => b.Walls[0] = new ArenaWallDefinition(100, new Vector2(-12f, -4f), Vector2.one));
            AssertInvalid(b => b.Pillars[1] = b.Pillars[0]);
            AssertInvalid(b => b.Pillars[0] = new ArenaPillarDefinition(100, new Vector2(23f, 0f), 2f));
            AssertInvalid(b => b.Walls[0] = new ArenaWallDefinition(200, new Vector2(23f, 0f), new Vector2(2f, .5f)));
            AssertInvalid(b => b.TeleportCandidates[0] = new Vector2(23.5f, 0f));
            AssertInvalid(b => b.TeleportCandidates[0] = b.Pillars[0].Position);
            AssertInvalid(b => b.PickupCandidates[0] = b.Spawns[0]);
            AssertInvalid(b => b.Respawn1[0] = b.Spawns[0]);
            AssertInvalid(b => b.BallSpawns[1] = Vector2.zero);
            AssertInvalid(b => b.Walls[0] = new ArenaWallDefinition(200, Vector2.zero, Vector2.one));
            AssertInvalid(b => b.Walls[0] = new ArenaWallDefinition(200, new Vector2(0f, 35.98f), new Vector2(1.6f, .02f)));
            AssertInvalid(b => b.Walls[0] = new ArenaWallDefinition(200, new Vector2(23.98f, 0f), new Vector2(.02f, 4f)));
            AssertInvalid(b => b.Pillars[0] = new ArenaPillarDefinition(100, new Vector2(2f, 18f), .5f));
        }

        [Test]
        public void EndpointClearButSealedRouteIsRejected()
        {
            ArenaRulesProfile.Builder builder = ArenaRulesProfile.Builder.CreateGreybox();
            builder.Pillars = Array.Empty<ArenaPillarDefinition>();
            builder.Walls = new[] { new ArenaWallDefinition(200, new Vector2(0f, 10f), new Vector2(24f, .5f)) };
            ArgumentException failure = Assert.Throws<ArgumentException>(() => builder.Freeze());
            StringAssert.Contains("disconnected", failure.Message);
            StringAssert.Contains("wall:200", failure.Message);
        }

        static void AssertInvalid(Action<ArenaRulesProfile.Builder> mutate)
        {
            ArenaRulesProfile.Builder builder = ArenaRulesProfile.Builder.CreateGreybox();
            mutate(builder);
            Assert.Throws<ArgumentException>(() => builder.Freeze());
        }
    }

    public sealed class ArenaGeometryAndRandomTests
    {
        [Test]
        public void FullFootprintsAndCornerDistancesAreUsed()
        {
            Assert.That(ArenaGeometry.ContainsFootprint(new Vector2(23.5f, 0f), .78f, 24f, 36f), Is.False);
            Assert.That(ArenaGeometry.CircleAabbOverlap(new Vector2(1.5f, 1.5f), .6f, Vector2.zero, Vector2.one), Is.False);
            Assert.That(ArenaGeometry.CircleAabbOverlap(new Vector2(1.4f, 1.4f), .6f, Vector2.zero, Vector2.one), Is.True);
            Assert.That(ArenaGeometry.PointSegmentDistanceSquared(new Vector2(2f, 1f), Vector2.zero, new Vector2(4f, 0f)), Is.EqualTo(1f));
            Assert.That(ArenaGeometry.PointSegmentDistanceSquared(Vector2.one, Vector2.zero, Vector2.zero), Is.EqualTo(2f));
            Assert.That(ArenaGeometry.IsFinite(new Vector2(0f, float.NaN)), Is.False);
        }

        [Test]
        public void XorshiftKnownVectorAndSnapshotRestoreProduceSameFuture()
        {
            uint state = 1u;
            Assert.That(ArenaRandom.Next(ref state), Is.EqualTo(270369u));
            Assert.That(ArenaRandom.Next(ref state), Is.EqualTo(67634689u));
            Assert.That(ArenaRandom.Next(ref state), Is.EqualTo(2647435461u));
            uint snapshot = state;
            for (int i = 0; i < 100; i++)
                Assert.That(ArenaRandom.Next(ref state), Is.EqualTo(ArenaRandom.Next(ref snapshot)));
            uint zero = 0, normalized = ArenaRandom.DefaultSeed;
            Assert.That(ArenaRandom.Next(ref zero), Is.EqualTo(ArenaRandom.Next(ref normalized)));
            Assert.That(zero, Is.Not.EqualTo(0u));
        }

        [Test]
        public void EmptyGenerationSpendsNothingButEveryTeleportSpendsExactlyOneValue()
        {
            uint state = 17u;
            Assert.That(ArenaRandom.TryChoosePickup(ref state, 0, out int location, out int kind), Is.False);
            Assert.That(state, Is.EqualTo(17u));
            Assert.That(location, Is.EqualTo(-1)); Assert.That(kind, Is.EqualTo(-1));
            uint expected = state;
            ArenaRandom.Next(ref expected);
            Assert.That(ArenaRandom.TeleportStartIndex(ref state, 0), Is.EqualTo(-1));
            Assert.That(state, Is.EqualTo(expected));
            uint actualValue = ArenaRandom.Next(ref expected);
            Assert.That(ArenaRandom.TeleportStartIndex(ref state, 7), Is.EqualTo((int)(actualValue % 7u)));
            Assert.That(state, Is.EqualTo(expected));
            uint positionValue = ArenaRandom.Next(ref expected), typeValue = ArenaRandom.Next(ref expected);
            Assert.That(ArenaRandom.TryChoosePickup(ref state, 5, out location, out kind), Is.True);
            Assert.That(location, Is.EqualTo((int)(positionValue % 5u)));
            Assert.That(kind, Is.EqualTo((int)(typeValue % 3u)));
            Assert.That(state, Is.EqualTo(expected));
            uint unchanged = state;
            Assert.Throws<ArgumentOutOfRangeException>(() => ArenaRandom.NextIndex(ref state, 0));
            Assert.That(state, Is.EqualTo(unchanged));
        }
    }
}
