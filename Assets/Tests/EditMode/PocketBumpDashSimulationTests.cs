using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    // Explicit mechanism fixtures, not evidence of a natural complete LAN match.
    public class PocketBumpDashSimulationTests
    {
        static ArenaRulesProfile rules;
        [OneTimeSetUp] public void Setup() => rules = ArenaRulesProfile.CreateGreybox();
        static DashArenaInput Input(Vector2 move, bool dash = false) => new DashArenaInput { Move = move, DashPressed = dash };
        static DashArenaState Live(ArenaRulesProfile profile = null)
        {
            var s = DashArenaSimulation.Create(profile ?? rules, 12345);
            DashArenaSimulation.StartMatch(s);
            s.Phase = MatchState.Live; s.GateClosed = false;
            s.Players[1].Position = new Vector2(10, -28);
            return s;
        }
        static void Ticks(DashArenaState s, int n)
        { for (int i = 0; i < n; i++) DashArenaSimulation.Step(s, default, default); }

        [Test] public void InputCodecRejectsAllReservedAndContradictoryBitsAndClearsPredictionEdge()
        {
            for (int value = 0; value < 256; value++)
            {
                byte bits = (byte)value;
                bool valid = (bits & 0xe0) == 0 && (bits & 3) != 3 && (bits & 12) != 12;
                Assert.AreEqual(valid, DashArenaInput.TryUnpack(bits, out var input), value.ToString());
                if (!valid) continue;
                Assert.AreEqual(bits, input.Pack());
                Assert.AreEqual(input.Move, input.Predict().Move);
                Assert.IsFalse(input.Predict().DashPressed);
            }
        }

        [Test] public void DashHasEighteenTicksAndOneHundredEightyTickCooldownWithLockedDirection()
        {
            var s = Live(); s.Players[0].Position = new Vector2(-18, -20); s.Players[0].Facing = Vector2.right;
            DashArenaSimulation.Step(s, Input(Vector2.zero, true), default);
            Assert.AreEqual(18, s.Players[0].DashTicks);
            Assert.AreEqual(Vector2.right, s.Players[0].DashDirection);
            for (int i = 1; i < 18; i++)
            {
                DashArenaSimulation.Step(s, Input(Vector2.up), default);
                Assert.Greater(s.Players[0].DashTicks, 0);
                Assert.AreEqual(Vector2.right, s.Players[0].DashDirection);
            }
            Assert.That(s.Players[0].Position.x, Is.EqualTo(-18 + 24 * .3f).Within(.001));
            DashArenaSimulation.Step(s, default, default);
            Assert.AreEqual(0, s.Players[0].DashTicks);
            Assert.AreEqual(180, s.Players[0].DashCooldownTicks);
            Ticks(s, 179);
            Assert.AreEqual(1, s.Players[0].DashCooldownTicks);
            var ev = DashArenaSimulation.Step(s, Input(Vector2.left, true), default);
            Assert.AreEqual(18, s.Players[0].DashTicks);
            Assert.AreEqual(1, ev.Count(e => e.Kind == DashArenaEventKind.DashStarted));
        }

        [Test] public void KickoffDiscardsDashAndDoesNotQueueItOnOpen()
        {
            var s = DashArenaSimulation.Create(rules); DashArenaSimulation.StartMatch(s);
            var ev = DashArenaSimulation.Step(s, Input(Vector2.right, true), default);
            Assert.IsFalse(ev.Any(e => e.Kind == DashArenaEventKind.DashStarted));
            Ticks(s, 179);
            Assert.AreEqual(MatchState.Live, s.Phase);
            DashArenaSimulation.Step(s, default, default);
            Assert.AreEqual(0, s.Players[0].DashTicks);
            Assert.IsTrue(s.Balls.All(b => b.Active));
        }

        static DashArenaState Shot(bool dash, bool green)
        {
            var s = Live(); s.Players[0].Position = new Vector2(-.95f, -18);
            s.Players[0].ControlVelocity = Vector2.right * rules.NormalSpeed;
            s.Players[0].GreenTicks = green ? 300 : 0;
            s.Balls[0] = new DashBallState { Active = true, Position = new Vector2(.25f, -18) };
            DashArenaSimulation.Step(s, Input(Vector2.right, dash), default);
            return s;
        }

        [Test] public void BodyContactShootsFreeBallAndGreenOnlyAddsOutgoingImpulse()
        {
            var plain = Shot(false, false); var green = Shot(false, true); var dash = Shot(true, false);
            Assert.Greater(plain.Balls[0].Velocity.x, 0);
            Assert.That(green.Balls[0].Velocity.x - plain.Balls[0].Velocity.x,
                Is.EqualTo(rules.NormalShotImpulse * .5f).Within(.0001));
            Assert.AreEqual(plain.Players[0].ControlVelocity, green.Players[0].ControlVelocity);
            Assert.Greater(dash.Balls[0].Velocity.x, plain.Balls[0].Velocity.x);
            Assert.IsTrue(dash.Balls[0].Active);
            TestContext.WriteLine($"fixture post-contact ball speed: ordinary={plain.Balls[0].Velocity.magnitude:F3}, green={green.Balls[0].Velocity.magnitude:F3}, Dash={dash.Balls[0].Velocity.magnitude:F3}; provisional units/s");
        }

        [Test] public void PlayerKnockbackIsSymmetricAndNotRemovedByNextMovementTick()
        {
            var s = Live(); s.Players[0].Position = new Vector2(-.9f, -18); s.Players[1].Position = new Vector2(.9f, -18);
            s.Players[0].ControlVelocity = Vector2.right * 12; s.Players[1].ControlVelocity = Vector2.left * 12;
            var ev = DashArenaSimulation.Step(s, Input(Vector2.right), Input(Vector2.left));
            Assert.AreEqual(1, ev.Count(e => e.Kind == DashArenaEventKind.Impact));
            Assert.That(s.Players[0].ExternalVelocity.x, Is.LessThan(-12));
            Assert.That(s.Players[0].ExternalVelocity.x, Is.EqualTo(-s.Players[1].ExternalVelocity.x).Within(.0001));
            DashArenaSimulation.Step(s, Input(Vector2.right), Input(Vector2.left));
            Assert.Less(s.Players[0].Velocity.x, 0, "Driving toward opponent must not erase the recoil.");
        }

        [Test] public void ContactLatchSuppressesRepeatedGameplayImpulse()
        {
            var initial = Shot(false, false);
            initial.Players[0].Position = new Vector2(-1.08f, -18); initial.Balls[0].Position = new Vector2(0, -18);
            initial.Players[0].ControlVelocity = Vector2.right * 12; initial.Players[0].ExternalVelocity = Vector2.zero;
            initial.Balls[0].Velocity = Vector2.zero; initial.PlayerBallContacts[0] = true;
            var unlatched = initial.Clone(); unlatched.PlayerBallContacts[0] = false;
            var ev = DashArenaSimulation.Step(initial, Input(Vector2.right), default);
            DashArenaSimulation.Step(unlatched, Input(Vector2.right), default);
            Assert.IsFalse(ev.Any(e => e.Kind == DashArenaEventKind.Impact));
            Assert.That(unlatched.Balls[0].Velocity.x - initial.Balls[0].Velocity.x, Is.EqualTo(rules.NormalShotImpulse).Within(.0001));
        }

        [Test] public void PillarInterruptsDashAndStunLastsExactlyThirtySixTicks()
        {
            var s = Live(); var pillar = rules.Pillars[0];
            s.Players[0].Position = pillar.Position - Vector2.right * (pillar.Radius + rules.PlayerRadius + .1f);
            var ev = DashArenaSimulation.Step(s, Input(Vector2.right, true), default);
            Assert.AreEqual(36, s.Players[0].StunTicks); Assert.AreEqual(0, s.Players[0].DashTicks);
            Assert.AreEqual(180, s.Players[0].DashCooldownTicks);
            Assert.AreEqual(1, ev.Count(e => e.Kind == DashArenaEventKind.Stunned));
            Ticks(s, 35); Assert.AreEqual(1, s.Players[0].StunTicks);
            DashArenaSimulation.Step(s, Input(Vector2.down, true), default);
            Assert.AreEqual(0, s.Players[0].StunTicks); Assert.AreEqual(0, s.Players[0].DashTicks);
        }

        [Test] public void ArmorContactLatchesWithoutStunAndExpiryDoesNotStunAgain()
        {
            var s = Live(); var pillar = rules.Pillars[0];
            s.Players[0].Position = pillar.Position - Vector2.right * (pillar.Radius + rules.PlayerRadius + .005f);
            s.Players[0].PurpleTicks = 3;
            DashArenaSimulation.Step(s, Input(Vector2.right), default);
            Assert.AreEqual(0, s.Players[0].StunTicks); Assert.IsTrue(s.CylinderContacts[0]);
            Ticks(s, 1);
            DashArenaSimulation.Step(s, Input(Vector2.right), default);
            Assert.AreEqual(0, s.Players[0].PurpleTicks); Assert.AreEqual(0, s.Players[0].StunTicks);
        }

        [Test] public void WallNeedsEffectiveDashAndRepairsOnlyAfterSixHundredTicks()
        {
            var s = Live(); var wall = rules.Walls[0];
            s.Players[0].Position = wall.Position - Vector2.up * (wall.HalfSize.y + rules.PlayerRadius + .1f);
            var ev = DashArenaSimulation.Step(s, Input(Vector2.up, true), default);
            Assert.IsTrue(s.Walls[0].Broken); Assert.AreEqual(600, s.Walls[0].RepairTicks);
            Assert.AreEqual(18, s.Players[0].DashTicks);
            Assert.AreEqual(1, ev.Count(e => e.Kind == DashArenaEventKind.WallBroken));
            s.Players[0].Position = rules.SpawnForSeat(0); s.Players[0].ControlVelocity = Vector2.zero;
            s.Players[0].DashTicks = 0;
            Ticks(s, 599); Assert.IsTrue(s.Walls[0].Broken); Assert.AreEqual(1, s.Walls[0].RepairTicks);
            ev = DashArenaSimulation.Step(s, default, default);
            Assert.IsFalse(s.Walls[0].Broken); Assert.AreEqual(1, ev.Count(e => e.Kind == DashArenaEventKind.WallRepaired));
        }

        [TestCase(false)] [TestCase(true)] public void WallRepairWaitsForWholePlayerOrBallFootprint(bool ball)
        {
            var s = Live(); var wall = rules.Walls[0];
            s.Walls[0] = new DashWallState { Broken = true, RepairTicks = 1 };
            if (ball) s.Balls[0] = new DashBallState { Active = true, Position = wall.Position };
            else s.Players[0].Position = wall.Position;
            var ev = DashArenaSimulation.Step(s, default, default);
            Assert.IsTrue(s.Walls[0].RepairPending); Assert.IsTrue(s.Walls[0].Broken);
            Assert.AreEqual(1, ev.Count(e => e.Kind == DashArenaEventKind.WallRepairPending));
            ev = DashArenaSimulation.Step(s, default, default);
            Assert.IsFalse(ev.Any(e => e.Kind == DashArenaEventKind.WallRepairPending));
            s.Balls[0].Active = false; s.Players[0].Position = rules.SpawnForSeat(0);
            ev = DashArenaSimulation.Step(s, default, default);
            Assert.IsFalse(s.Walls[0].Broken); Assert.AreEqual(1, ev.Count(e => e.Kind == DashArenaEventKind.WallRepaired));
        }

        [Test] public void GreenPlayerWithoutDashAndFastBallCannotBreakWall()
        {
            var s = Live(); var wall = rules.Walls[0];
            s.Players[0].Position = wall.Position - Vector2.up * (wall.HalfSize.y + rules.PlayerRadius + .01f);
            s.Players[0].ControlVelocity = Vector2.up * 24; s.Players[0].GreenTicks = 300;
            DashArenaSimulation.Step(s, Input(Vector2.up), default);
            Assert.IsFalse(s.Walls[0].Broken);
            s.Players[0].Position = rules.SpawnForSeat(0);
            s.Balls[0] = new DashBallState { Active = true, Position = wall.Position - Vector2.up * 1.1f, Velocity = Vector2.up * 48 };
            DashArenaSimulation.Step(s, default, default);
            Assert.IsFalse(s.Walls[0].Broken); Assert.Less(s.Balls[0].Velocity.y, 0);
        }

        [Test] public void FallingDoesNotScoreAndRespawnsAfterOneHundredEightyTicks()
        {
            var s = Live(); s.Players[0].Position = new Vector2(rules.HalfWidth + rules.PlayerRadius - .1f, 0);
            s.Players[0].ControlVelocity = Vector2.right * 12;
            s.Balls[0] = new DashBallState { Active = true, Position = new Vector2(0, -18), Velocity = Vector2.up * 2 };
            var ev = DashArenaSimulation.Step(s, Input(Vector2.right, true), default);
            Assert.IsFalse(s.Players[0].Alive); Assert.AreEqual(180, s.Players[0].RespawnTicks);
            Assert.AreEqual(180, s.Players[0].DashCooldownTicks); CollectionAssert.AreEqual(new[] { 0, 0 }, s.Scores);
            Assert.AreEqual(1, ev.Count(e => e.Kind == DashArenaEventKind.Fell));
            float y = s.Balls[0].Position.y;
            Ticks(s, 179); Assert.IsFalse(s.Players[0].Alive); Assert.Greater(s.Balls[0].Position.y, y);
            ev = DashArenaSimulation.Step(s, default, default);
            Assert.IsTrue(s.Players[0].Alive); Assert.AreEqual(Vector2.zero, s.Players[0].Velocity);
            Assert.AreEqual(rules.RespawnCandidates(0)[0], s.Players[0].Position);
            Assert.AreEqual(1, ev.Count(e => e.Kind == DashArenaEventKind.Respawned));
        }

        [Test] public void SimultaneousGoalsPreferNorthOnceThenResetDeadPlayersButPreserveWalls()
        {
            var s = Live(ArenaRulesProfile.CreateGreybox(8, 2));
            s.Balls[0] = new DashBallState { Active = true, Position = new Vector2(0, -36.25f), Velocity = Vector2.down * 48 };
            s.Balls[1] = new DashBallState { Active = true, Position = new Vector2(0, 36.25f), Velocity = Vector2.up * 48 };
            s.Players[0].Alive = false; s.Players[0].RespawnTicks = 170;
            s.Walls[0] = new DashWallState { Broken = true, RepairTicks = 500 };
            s.Players[1].GreenTicks = 300;
            var ev = DashArenaSimulation.Step(s, default, default);
            CollectionAssert.AreEqual(new[] { 0, 1 }, s.Scores);
            Assert.AreEqual(MatchState.Scored, s.Phase); Assert.AreEqual(0, s.Players[1].GreenTicks);
            Assert.AreEqual(1, ev.Count(e => e.Kind == DashArenaEventKind.Goal));
            Ticks(s, 60);
            Assert.AreEqual(MatchState.Kickoff, s.Phase); Assert.IsTrue(s.Players[0].Alive);
            Assert.AreEqual(0, s.Players[0].RespawnTicks); Assert.IsTrue(s.Walls[0].Broken);
            Assert.AreEqual(439, s.Walls[0].RepairTicks); Assert.IsFalse(s.Balls.Any(b => b.Active));
        }

        [Test] public void PurplePickupClearsStunButNotCooldownAndCoexistsWithGreen()
        {
            var s = Live(); s.Players[0].StunTicks = 20; s.Players[0].DashCooldownTicks = 100;
            for (int i = 0; i < 2; i++) s.Pickups[i] = new DashPickupState { Active = true, Id = i + 1,
                Position = s.Players[0].Position, Kind = i == 0 ? DashPickupKind.Green : DashPickupKind.Purple };
            var ev = DashArenaSimulation.Step(s, default, default);
            Assert.AreEqual(300, s.Players[0].GreenTicks); Assert.AreEqual(300, s.Players[0].PurpleTicks);
            Assert.AreEqual(0, s.Players[0].StunTicks); Assert.AreEqual(99, s.Players[0].DashCooldownTicks);
            Assert.AreEqual(2, ev.Count(e => e.Kind == DashArenaEventKind.Pickup));
            Assert.IsTrue(ev.Where(e => e.Kind == DashArenaEventKind.Pickup).All(e => e.PickupKind.HasValue));
        }

        [Test] public void SuccessfulBlueConsumesExactlyOneRandomAndStopsDashWithoutRecursivePickup()
        {
            var s = Live(); s.Players[0].Position = new Vector2(0, -18);
            s.Pickups[0] = new DashPickupState { Active = true, Id = 1, Kind = DashPickupKind.Blue, Position = s.Players[0].Position };
            uint expected = s.RandomState; uint draw = ArenaRandom.Next(ref expected);
            var destination = rules.TeleportCandidates[(int)(draw % (uint)rules.TeleportCandidates.Count)];
            s.Pickups[1] = new DashPickupState { Active = true, Id = 2, Kind = DashPickupKind.Green, Position = destination };
            var ev = DashArenaSimulation.Step(s, Input(Vector2.right, true), default);
            Assert.AreEqual(expected, s.RandomState); Assert.AreEqual(destination, s.Players[0].Position);
            Assert.AreEqual(Vector2.zero, s.Players[0].Velocity); Assert.AreEqual(0, s.Players[0].DashTicks);
            Assert.AreEqual(180, s.Players[0].DashCooldownTicks); Assert.AreEqual(0, s.Players[0].GreenTicks);
            Assert.IsTrue(s.Pickups[1].Active); Assert.AreEqual(1, ev.Count(e => e.Kind == DashArenaEventKind.Teleport));
        }

        [Test] public void GenerationUsesTwoDrawsLocationThenKindAndStopsAtCapacity()
        {
            var s = Live(); s.SpawnTicks = 1; uint expected = s.RandomState;
            ArenaRandom.TryChoosePickup(ref expected, rules.PickupCandidates.Count, out int location, out int kind);
            DashArenaSimulation.Step(s, default, default);
            Assert.AreEqual(expected, s.RandomState); Assert.AreEqual(2, s.NextPickupId);
            Assert.AreEqual(rules.PickupCandidates[location], s.Pickups[0].Position);
            Assert.AreEqual((DashPickupKind)kind, s.Pickups[0].Kind);
            for (int i = 0; i < s.Pickups.Length; i++) s.Pickups[i] = new DashPickupState
                { Active = true, Id = i + 1, Position = rules.PickupCandidates[i] };
            s.SpawnTicks = 1; DashArenaSimulation.Step(s, default, default);
            Assert.AreEqual(expected, s.RandomState); Assert.AreEqual(2, s.NextPickupId);
        }

        [Test] public void MatchOverDoesNotAdvanceAnything()
        {
            var s = Live(); s.Phase = MatchState.MatchOver; s.Players[0].GreenTicks = 200;
            s.Walls[0] = new DashWallState { Broken = true, RepairTicks = 1 };
            int checksum = s.Checksum();
            Ticks(s, 60);
            Assert.AreEqual(checksum, s.Checksum());
        }

        [Test] public void ProvisionalHandfeelMeasurementTableUsesSameContactFixtures()
        {
            foreach (bool dash in new[] { false, true })
            foreach (bool green in new[] { false, true })
            {
                var ballShot = Shot(dash, green);
                Vector2 ballStart = ballShot.Balls[0].Position;
                float ballSpeed = ballShot.Balls[0].Velocity.magnitude;
                // Remove the shooter after recording contact so this measures a free ball's travel.
                ballShot.Players[0].Alive = false; ballShot.Players[0].RespawnTicks = 180;
                Ticks(ballShot, 15);

                var pair = Live(); pair.Players[0].Position = new Vector2(-.9f, -18); pair.Players[1].Position = new Vector2(.9f, -18);
                pair.Players[0].ControlVelocity = Vector2.right * 12; pair.Players[1].ControlVelocity = Vector2.left * 12;
                pair.Players[0].GreenTicks = green ? 300 : 0;
                DashArenaSimulation.Step(pair, Input(Vector2.right, dash), Input(Vector2.left, dash));
                float firstRecoil = pair.Players[0].ExternalVelocity.x, secondRecoil = pair.Players[1].ExternalVelocity.x;
                Vector2 firstAtContact = pair.Players[0].Position, secondAtContact = pair.Players[1].Position;
                Ticks(pair, 30);
                TestContext.WriteLine($"PROVISIONAL fixture dash={dash} green(P1)={green}: ball speed={ballSpeed:F3} u/s; ball travel over next .25s={Vector2.Distance(ballStart, ballShot.Balls[0].Position):F3}u; P1/P2 initial external={firstRecoil:F3}/{secondRecoil:F3}; next .5s player displacements={Vector2.Distance(firstAtContact, pair.Players[0].Position):F3}/{Vector2.Distance(secondAtContact, pair.Players[1].Position):F3}u");
                Assert.Greater(ballSpeed, 0);
                Assert.Less(firstRecoil, 0); Assert.Greater(secondRecoil, 0);
                float closing = dash ? 48f : 24f;
                float baseOnlyExternal = (1 + rules.PlayerBounciness) * closing * .5f;
                float extra = dash ? rules.DashPlayerImpulse : rules.NormalPlayerImpulse;
                Assert.That(-firstRecoil, Is.EqualTo(baseOnlyExternal + extra).Within(.001));
                Assert.That(secondRecoil, Is.EqualTo(baseOnlyExternal + extra * (green ? rules.GreenImpactMultiplier : 1f)).Within(.001),
                    "The provisional safety cap must not erase the outgoing green enhancement in the matched Dash fixture.");
            }
        }

        [Test] public void SweptBallsCannotSwapSidesOrTunnelThroughThinWall()
        {
            var builder = rules.ToBuilder(); builder.BallCount = 2;
            builder.Walls = new[] { new ArenaWallDefinition(200, new Vector2(10, 8), new Vector2(2, .02f)) };
            var s = Live(builder.Freeze());
            s.Balls[0] = new DashBallState { Active = true, Position = new Vector2(-.5f, -18), Velocity = Vector2.right * 48 };
            s.Balls[1] = new DashBallState { Active = true, Position = new Vector2(.5f, -18), Velocity = Vector2.left * 48 };
            DashArenaSimulation.Step(s, default, default);
            Assert.Less(s.Balls[0].Position.x, s.Balls[1].Position.x);
            Assert.Less(s.Balls[0].Velocity.x, 0); Assert.Greater(s.Balls[1].Velocity.x, 0);
            s.Balls[0] = new DashBallState { Active = true, Position = new Vector2(10, 7.6f), Velocity = Vector2.up * 48 };
            s.Balls[1].Active = false;
            DashArenaSimulation.Step(s, default, default);
            Assert.Less(s.Balls[0].Position.y, 8 - .02f - rules.BallRadius);
            Assert.Less(s.Balls[0].Velocity.y, 0); Assert.IsFalse(s.Walls[0].Broken);
        }

        [Test] public void SameInstantPillarStunPrecedesWallAndCancelsBreakPermission()
        {
            var builder = rules.ToBuilder();
            builder.Pillars = new[] { new ArenaPillarDefinition(100, new Vector2(10, 8), 1) };
            builder.Walls = new[] { new ArenaWallDefinition(200, new Vector2(10, 6.5f), new Vector2(2, .5f)) };
            var s = Live(builder.Freeze());
            float y = 7 + rules.PlayerRadius;
            s.Players[0].Position = new Vector2(10 - Mathf.Sqrt(Mathf.Pow(1 + rules.PlayerRadius, 2) - Mathf.Pow(y - 8, 2)), y);
            var ev = DashArenaSimulation.Step(s, Input(new Vector2(.6f, -.8f), true), default);
            Assert.AreEqual(36, s.Players[0].StunTicks);
            Assert.AreEqual(0, s.Players[0].DashTicks); Assert.IsFalse(s.Walls[0].Broken);
            Assert.AreEqual(1, ev.Count(e => e.Kind == DashArenaEventKind.PillarHit));
            Assert.IsFalse(ev.Any(e => e.Kind == DashArenaEventKind.WallBroken));
        }
    }
}
