using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class ProductionProfileTests
    {
        // Observed in the final saved greybox capture and retained in design/verification.
        // This historical value is not regenerated from the implementation under test.
        const string ApprovedGreyboxHash = "9de4c55f0cc30454befa6a010afb489c90093605dd692bfe100944ad167d21a2";
        const string ApprovedProductionHash = "7012a8bc484f3d522ace17ccc550c9572659c5d0d2f96fa122a83fa27831abb8";
        const string ApprovedLayoutHash = "13a8a46bb386b63ff20e427546d184a5eb932274c753ea8d6397e69c9dcaa500";

        [TestCase(1, 1)]
        [TestCase(8, 1)]
        [TestCase(8, 8)]
        [TestCase(21, 1)]
        [TestCase(21, 8)]
        public void ProductionV1MatchesEveryApprovedScalarAndArrayExceptProvisionalFlag(int pointsToWin, int ballCount)
        {
            ArenaRulesProfile production = ArenaRulesProfile.CreateProductionV1(pointsToWin, ballCount);
            ArenaRulesProfile greybox = ArenaRulesProfile.CreateGreybox(pointsToWin, ballCount);
            Assert.That(production.IsProvisional, Is.False);
            Assert.That(greybox.IsProvisional, Is.True);
            Assert.That(production.PointsToWin, Is.EqualTo(pointsToWin));
            Assert.That(production.BallCount, Is.EqualTo(ballCount));
            Assert.That(production.LayoutHash, Is.EqualTo(greybox.LayoutHash));
            Assert.That(production.ContentHash, Is.Not.EqualTo(greybox.ContentHash),
                "Approval identity must remain part of compatibility rather than be removed from hashing.");

            ArenaRulesProfile.Builder approvedData = production.ToBuilder();
            ArenaRulesProfile.Builder previewData = greybox.ToBuilder();
            foreach (FieldInfo field in typeof(ArenaRulesProfile.Builder).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.Name == nameof(ArenaRulesProfile.Builder.IsProvisional)) continue;
                Assert.That(field.GetValue(approvedData), Is.EqualTo(field.GetValue(previewData)), field.Name);
                if (field.FieldType.IsArray)
                    Assert.That(field.GetValue(approvedData), Is.Not.SameAs(field.GetValue(previewData)), field.Name);
            }
            approvedData.IsProvisional = true;
            Assert.That(approvedData.Freeze().ContentHash, Is.EqualTo(greybox.ContentHash),
                "Normalization compares the full canonical raw-bit content, not approximate float equality.");
            Assert.That(production.KickoffHalfDepth, Is.EqualTo(greybox.KickoffHalfDepth));
        }

        [Test]
        public void ProductionV1LocksThePreviouslyObservedApprovedGoldenSnapshot()
        {
            ArenaRulesProfile production = ArenaRulesProfile.CreateProductionV1();
            ArenaRulesProfile greybox = ArenaRulesProfile.CreateGreybox();
            Assert.That(production.ContentHash, Is.EqualTo(ApprovedProductionHash));
            Assert.That(production.LayoutHash, Is.EqualTo(ApprovedLayoutHash));
            Assert.That(greybox.ContentHash, Is.EqualTo(ApprovedGreyboxHash),
                "Introducing a production entry point must not rewrite historical greybox content.");
            ArenaRulesProfile.Builder normalized = production.ToBuilder();
            normalized.IsProvisional = true;
            Assert.That(normalized.Freeze().ContentHash, Is.EqualTo(ApprovedGreyboxHash),
                "The complete production snapshot is pinned to approved content, with only its marker changed.");
            Assert.That(production.MaxExternalSpeed, Is.EqualTo(72f));
            Assert.That(production.NormalSpeed, Is.EqualTo(12f));
            Assert.That(production.DashSpeed, Is.EqualTo(24f));
            Assert.That(production.DashSpeed * production.DashTicks / production.TickRate, Is.EqualTo(7.2f).Within(.0001f));
            Assert.That(production.BallMaxSpeed, Is.EqualTo(48f));
            Assert.That(production.KickoffHalfDepth, Is.EqualTo(1.85f).Within(.0001f));
            // Golden values were measured in the retained 20261002-2002/editmode-1.xml run.
            TestContext.Out.WriteLine("ProductionV1.ContentHash=" + production.ContentHash);
            TestContext.Out.WriteLine("ProductionV1.LayoutHash=" + production.LayoutHash);
            TestContext.Out.WriteLine("Greybox.ContentHash=" + greybox.ContentHash);
        }

        [Test]
        public void ProductionV1IsStableAcrossInstancesAndCultures()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("en-US");
                ArenaRulesProfile first = ArenaRulesProfile.CreateProductionV1();
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                ArenaRulesProfile second = ArenaRulesProfile.CreateProductionV1();
                Assert.That(second.ContentHash, Is.EqualTo(first.ContentHash));
                Assert.That(second.LayoutHash, Is.EqualTo(first.LayoutHash));
                Assert.That(second.Pillars, Is.Not.SameAs(first.Pillars));
                Assert.That(second.Walls, Is.Not.SameAs(first.Walls));
                Assert.That(second.PickupCandidates, Is.Not.SameAs(first.PickupCandidates));
                Assert.That(second.TeleportCandidates, Is.Not.SameAs(first.TeleportCandidates));
            }
            finally { CultureInfo.CurrentCulture = original; }
        }

        [TestCase(0, 1)]
        [TestCase(22, 1)]
        [TestCase(8, 0)]
        [TestCase(8, 9)]
        public void ProductionFactoryPreservesStrictSupportedMatchSettingBounds(int pointsToWin, int ballCount)
        {
            Assert.Throws<ArgumentException>(() => ArenaRulesProfile.CreateProductionV1(pointsToWin, ballCount));
        }

        [Test]
        public void DifferentSupportedSettingsChangeContentButNeverMoveTheFrozenLayout()
        {
            ArenaRulesProfile original = ArenaRulesProfile.CreateProductionV1();
            ArenaRulesProfile otherScore = ArenaRulesProfile.CreateProductionV1(pointsToWin: 21);
            ArenaRulesProfile otherBallCount = ArenaRulesProfile.CreateProductionV1(ballCount: 8);
            Assert.That(otherScore.ContentHash, Is.Not.EqualTo(original.ContentHash));
            Assert.That(otherBallCount.ContentHash, Is.Not.EqualTo(original.ContentHash));
            Assert.That(otherScore.LayoutHash, Is.EqualTo(original.LayoutHash));
            Assert.That(otherBallCount.LayoutHash, Is.EqualTo(original.LayoutHash));
            for (int i = 0; i < otherBallCount.BallCount; i++)
                Assert.That(ArenaGeometry.IsFootprintClear(otherBallCount, otherBallCount.BallSpawn(i), otherBallCount.BallRadius), Is.True);
        }

        [Test]
        public void ProductionViewsAndBuilderCopiesCannotMutateTheFrozenV1OrFutureInstances()
        {
            ArenaRulesProfile production = ArenaRulesProfile.CreateProductionV1();
            string originalHash = production.ContentHash;
            ArenaRulesProfile.Builder edit = production.ToBuilder();
            edit.MaxExternalSpeed = 36f;
            edit.IsProvisional = true;
            edit.Pillars[0] = new ArenaPillarDefinition(999, Vector2.zero, 1f);
            edit.Walls[0] = new ArenaWallDefinition(998, Vector2.zero, Vector2.one);
            edit.Spawns[0] = Vector2.zero;
            edit.Respawn0[0] = Vector2.zero;
            edit.Respawn1[0] = Vector2.zero;
            edit.BallSpawns[0] = Vector2.one;
            edit.PickupCandidates[0] = Vector2.zero;
            edit.TeleportCandidates[0] = Vector2.zero;
            Assert.Throws<NotSupportedException>(() => ((IList<Vector2>)production.PickupCandidates)[0] = Vector2.zero);
            Assert.Throws<NotSupportedException>(() => ((IList<ArenaWallDefinition>)production.Walls)[0] = edit.Walls[0]);
            Assert.That(production.ContentHash, Is.EqualTo(originalHash));
            Assert.That(production.ToBuilder().Freeze().ContentHash, Is.EqualTo(originalHash),
                "Recomputing from the live data must agree, not just the cached digest.");
            Assert.That(production.IsProvisional, Is.False);
            Assert.That(production.MaxExternalSpeed, Is.EqualTo(72f));
            Assert.That(ArenaRulesProfile.CreateProductionV1().ContentHash, Is.EqualTo(originalHash));
            Assert.That(ArenaRulesProfile.CreateGreybox().ContentHash, Is.EqualTo(ApprovedGreyboxHash));
        }

        [Test]
        public void ProductionDerivedAuthoringStillUsesExistingGeometryAndScalarValidation()
        {
            ArenaRulesProfile production = ArenaRulesProfile.CreateProductionV1();
            ArenaRulesProfile.Builder badNumber = production.ToBuilder();
            badNumber.MaxExternalSpeed = float.NaN;
            Assert.Throws<ArgumentException>(() => badNumber.Freeze());
            ArenaRulesProfile.Builder blockedCenter = production.ToBuilder();
            blockedCenter.Walls[0] = new ArenaWallDefinition(200, Vector2.zero, Vector2.one);
            ArgumentException failure = Assert.Throws<ArgumentException>(() => blockedCenter.Freeze());
            StringAssert.Contains("200", failure.Message);
            ArenaRulesProfile.Builder missingCandidates = production.ToBuilder();
            missingCandidates.TeleportCandidates = Array.Empty<Vector2>();
            Assert.Throws<ArgumentException>(() => missingCandidates.Freeze());
        }
    }
}
