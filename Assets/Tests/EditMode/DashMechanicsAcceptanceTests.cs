using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    // Explicit core-mechanic fixtures. Direct resolver/reset calls deliberately isolate a
    // boundary; none of these cases claims natural-match, transport, or LAN acceptance.
    public sealed class DashMechanicsAcceptanceTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void RepeatedSixPassContactEpisodeGrantsOnlyOneGameplayImpulse(bool ball)
        {
            DashArenaState state = Live();
            var events = new List<DashArenaEvent>();
            int other = ball ? 2 : 1;
            for (int batch = 0; batch < 12; batch++)
            for (int pass = 0; pass < 6; pass++)
            {
                // Model an iterative positional constraint presenting the same closing pair
                // again, without ending its contact episode. This is not 72 natural ticks.
                state.Players[0].Position = new Vector2(0, 8);
                state.Players[0].ControlVelocity = Vector2.right * 12;
                state.Players[0].ExternalVelocity = Vector2.zero;
                if (ball)
                    state.Balls[0] = new DashBallState { Active = true,
                        Position = new Vector2(state.Rules.PlayerRadius + state.Rules.BallRadius, 8) };
                else
                {
                    state.Players[1].Position = new Vector2(2 * state.Rules.PlayerRadius, 8);
                    state.Players[1].ControlVelocity = state.Players[1].ExternalVelocity = Vector2.zero;
                }
                DashArenaState baseCollision = state.Clone();
                if (ball) baseCollision.PlayerBallContacts[0] = true;
                else baseCollision.PlayerContacts[0] = true;
                Invoke("ResolveBodies", baseCollision, 0, other, new List<DashArenaEvent>());
                Invoke("ResolveBodies", state, 0, other, events);

                float actual = ball ? state.Balls[0].Velocity.x : state.Players[1].ExternalVelocity.x;
                float basic = ball ? baseCollision.Balls[0].Velocity.x : baseCollision.Players[1].ExternalVelocity.x;
                float extra = batch == 0 && pass == 0
                    ? (ball ? state.Rules.NormalShotImpulse : state.Rules.NormalPlayerImpulse) : 0;
                Assert.That(actual - basic, Is.EqualTo(extra).Within(.0001f), $"batch {batch}, pass {pass}");
                Assert.That(Count(events, DashArenaEventKind.Impact), Is.EqualTo(1));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        public void ReachingTargetWithOnePointLeadRequiresAnotherActualGoal(int scorer)
        {
            var builder = Empty(); builder.PointsToWin = 3;
            DashArenaState state = Live(builder);
            state.Scores[0] = state.Scores[1] = 2; // Explicit deuce fixture, not a full match.
            CrossGoal(state, scorer);
            Assert.That(state.Scores[scorer], Is.EqualTo(3));
            IReadOnlyList<DashArenaEvent> firstFreeze = Run(state, state.Rules.FreezeTicks);
            Assert.That(state.Phase, Is.EqualTo(MatchState.Kickoff));
            Assert.That(Count(firstFreeze, DashArenaEventKind.MatchOver), Is.Zero);
            Run(state, state.Rules.KickoffTicks);
            Assert.That(state.Phase, Is.EqualTo(MatchState.Live));
            CrossGoal(state, scorer);
            IReadOnlyList<DashArenaEvent> terminal = Run(state, state.Rules.FreezeTicks);
            Assert.That(state.Scores[scorer], Is.EqualTo(4));
            Assert.That(state.Scores[1 - scorer], Is.EqualTo(2));
            Assert.That(state.Phase, Is.EqualTo(MatchState.MatchOver));
            Assert.That(terminal.Single(e => e.Kind == DashArenaEventKind.MatchOver).Seat, Is.EqualTo(scorer));
            int checksum = state.Checksum();
            Run(state, 10);
            Assert.That(state.Checksum(), Is.EqualTo(checksum));
        }

        [TestCase(0)]
        [TestCase(1)]
        public void DefenderBodySendingBallIntoOwnGoalAwardsOnlyOpponent(int defender)
        {
            DashArenaState state = Live();
            float sign = defender == 0 ? 1 : -1;
            Vector2 toward = Vector2.up * sign;
            state.Players[defender].Position = toward * (state.Rules.HalfHeight - state.Rules.PlayerRadius - .03f);
            state.Players[defender].ControlVelocity = toward * state.Rules.NormalSpeed;
            state.Balls[0] = new DashBallState { Active = true,
                Position = toward * (state.Rules.HalfHeight + state.Rules.BallRadius - .1f) };
            IReadOnlyList<DashArenaEvent> events = defender == 0
                ? Step(state, Input(toward, true)) : Step(state, default, Input(toward, true));
            Assert.That(Count(events, DashArenaEventKind.Impact), Is.EqualTo(1));
            Assert.That(events.Single(e => e.Kind == DashArenaEventKind.Goal).Seat, Is.EqualTo(1 - defender));
            Assert.That(state.Scores[defender], Is.Zero);
            Assert.That(state.Scores[1 - defender], Is.EqualTo(1));
        }

        [Test]
        public void FreeBallReboundingFromEnvironmentCanScoreWithoutPlayerOwnership()
        {
            var builder = Empty();
            builder.Walls = new[] { new ArenaWallDefinition(200, new Vector2(0, 30), new Vector2(1, .3f)) };
            DashArenaState state = Live(builder);
            state.Players[0].Position = new Vector2(-10, 18);
            state.Players[1].Position = new Vector2(10, -18);
            state.Balls[0] = new DashBallState { Active = true, Position = new Vector2(0, 29.35f), Velocity = Vector2.up * 40 };
            var events = new List<DashArenaEvent>();
            for (int i = 0; i < 360 && state.Phase == MatchState.Live; i++) events.AddRange(Step(state));
            Assert.That(Count(events, DashArenaEventKind.WallHit), Is.EqualTo(1));
            Assert.That(Count(events, DashArenaEventKind.Impact), Is.Zero);
            Assert.That(Count(events, DashArenaEventKind.Goal), Is.EqualTo(1));
            Assert.That(state.Scores, Is.EqualTo(new[] { 1, 0 }));
        }

        [Test]
        public void BothPlayersCanFallTogetherWhileBallContinuesAndRespawnAfter180Ticks()
        {
            DashArenaState state = Live();
            float edge = state.Rules.HalfWidth + state.Rules.PlayerRadius - .05f;
            state.Players[0].Position = new Vector2(edge, 0);
            state.Players[1].Position = new Vector2(-edge, 0);
            state.Players[0].PurpleTicks = state.Players[1].PurpleTicks = 100;
            state.Balls[0] = new DashBallState { Active = true, Position = new Vector2(0, -12), Velocity = Vector2.up * 10 };
            IReadOnlyList<DashArenaEvent> events = Step(state, Input(Vector2.right, true), Input(Vector2.left, true));
            Assert.That(Count(events, DashArenaEventKind.Fell), Is.EqualTo(2));
            Assert.That(state.Players.All(p => !p.Alive && p.RespawnTicks == 180 && p.DashCooldownTicks == 180), Is.True);
            Assert.That(state.Players.All(p => p.PurpleTicks == 0), Is.True, "Armor cannot prevent a fall.");
            Assert.That(state.Scores, Is.EqualTo(new[] { 0, 0 }));
            float y = state.Balls[0].Position.y;
            Run(state, 179);
            Assert.That(state.Players.All(p => !p.Alive && p.RespawnTicks == 1), Is.True);
            Assert.That(state.Balls[0].Position.y, Is.GreaterThan(y));
            events = Step(state);
            Assert.That(Count(events, DashArenaEventKind.Respawned), Is.EqualTo(2));
            Assert.That(state.Players.All(p => p.Alive && p.Velocity == Vector2.zero), Is.True);
            Assert.That(state.Players[0].Position, Is.EqualTo(state.Rules.RespawnCandidates(0)[0]));
            Assert.That(state.Players[1].Position, Is.EqualTo(state.Rules.RespawnCandidates(1)[0]));
        }

        [Test]
        public void EveryRespawnFootprintBlockedWaitsThenUsesFirstSafeOwnCandidate()
        {
            var builder = Empty(2);
            builder.Respawn0 = new[] { new Vector2(0, 18), new Vector2(3, 18) };
            DashArenaState state = Live(builder);
            state.Players[0].Alive = false; state.Players[0].RespawnTicks = 1;
            float near = state.Rules.PlayerRadius + state.Rules.BallRadius + state.Rules.RepairClearance - .01f;
            state.Balls[0] = new DashBallState { Active = true, Position = builder.Respawn0[0] + Vector2.right * near };
            state.Balls[1] = new DashBallState { Active = true, Position = builder.Respawn0[1] };
            IReadOnlyList<DashArenaEvent> waiting = Run(state, 6);
            Assert.That(state.Players[0].Alive, Is.False);
            Assert.That(state.Players[0].RespawnTicks, Is.Zero);
            Assert.That(Count(waiting, DashArenaEventKind.Respawned), Is.Zero);
            // Explicit occupancy-boundary fixture: one ball leaves, the other still blocks.
            state.Balls[0].Position = new Vector2(-10, 8);
            IReadOnlyList<DashArenaEvent> clear = Step(state);
            Assert.That(state.Players[0].Alive, Is.True);
            Assert.That(state.Players[0].Position, Is.EqualTo(builder.Respawn0[0]));
            Assert.That(Count(clear, DashArenaEventKind.Respawned), Is.EqualTo(1));
        }

        [Test]
        public void SimultaneousDashesBreakSameWallOnceAndBothContinueThrough()
        {
            DashArenaState state = WallFixture();
            float y = 8 - .5f - state.Rules.PlayerRadius - .1f;
            state.Players[0].Position = new Vector2(9, y);
            state.Players[1].Position = new Vector2(11, y);
            IReadOnlyList<DashArenaEvent> events = Step(state, Input(Vector2.up, true), Input(Vector2.up, true));
            Assert.That(state.Walls[0].Broken, Is.True);
            Assert.That(state.Walls[0].RepairTicks, Is.EqualTo(600));
            Assert.That(Count(events, DashArenaEventKind.WallBroken), Is.EqualTo(1));
            Assert.That(events.Single(e => e.Kind == DashArenaEventKind.WallBroken).Seat, Is.Zero);
            Assert.That(state.Players.All(p => p.DashTicks == 18 && p.ControlVelocity.y == state.Rules.DashSpeed), Is.True);
            Run(state, 4);
            Assert.That(state.Players.All(p => p.Position.y > 8 && p.DashTicks > 0), Is.True);
        }

        [Test]
        public void TenTickSnapshotCorrectionAcrossActualWallBreakReplaysStateAndEvents()
        {
            DashArenaState checkpoint = WallFixture();
            checkpoint.Players[0].Position = new Vector2(10, 8 - .5f - checkpoint.Rules.PlayerRadius - .2f);
            DashArenaState baseline = checkpoint.Clone(), corrected = checkpoint.Clone();
            var expected = new List<DashArenaEvent>();
            for (int tick = 0; tick < 10; tick++)
            {
                expected.AddRange(Step(baseline, Input(Vector2.up, tick == 2)));
                Step(corrected, Input(Vector2.left));
            }
            Assert.That(baseline.Walls[0].Broken, Is.True);
            Assert.That(corrected.Walls[0].Broken, Is.False);
            corrected.CopyFrom(checkpoint);
            var replayed = new List<DashArenaEvent>();
            for (int tick = 0; tick < 10; tick++) replayed.AddRange(Step(corrected, Input(Vector2.up, tick == 2)));
            AssertSnapshotEqual(baseline, corrected);
            CollectionAssert.AreEqual(expected, replayed);
            Assert.That(Count(replayed, DashArenaEventKind.WallBroken), Is.EqualTo(1));
        }

        [TestCase("Fall")]
        [TestCase("RespawnPlayers")]
        [TestCase("Teleport")]
        public void IndividualResetBoundaryClearsOnlyItsPlayersContactLatches(string boundary)
        {
            DashArenaState state = Live(ArenaRulesProfile.Builder.CreateGreybox());
            SeedLatches(state);
            var events = new List<DashArenaEvent>();
            if (boundary == "Fall") Invoke(boundary, state, 0, events);
            else if (boundary == "RespawnPlayers")
            {
                state.Players[0].Alive = false;
                Invoke(boundary, state, events);
                Assert.That(state.Players[0].Alive, Is.True);
            }
            else Assert.That(Invoke(boundary, state, 0, 17, events), Is.EqualTo(true));
            Assert.That(state.PlayerContacts[0], Is.False);
            AssertSeatLatches(state.PlayerBallContacts, state.Balls.Length);
            AssertSeatLatches(state.CylinderContacts, state.Rules.Pillars.Count);
            AssertSeatLatches(state.WallContacts, state.Walls.Length);
            Assert.That(state.BallWallContacts.All(value => value), Is.True, "Individual reset must not clear unrelated ball-wall contacts.");
        }

        [Test]
        public void GoalKickoffResetClearsAllContactEpisodesButPreservesWallRepair()
        {
            DashArenaState state = Live(ArenaRulesProfile.Builder.CreateGreybox());
            SeedLatches(state);
            state.Walls[0] = new DashWallState { Broken = true, RepairPending = false, RepairTicks = 123 };
            state.Players[0].Alive = false; state.Players[0].RespawnTicks = 77;
            Invoke("BeginKickoff", state); // Isolate reset from the preceding geometric release pass.
            foreach (bool[] contacts in Contacts(state)) Assert.That(contacts.All(value => !value), Is.True);
            Assert.That(state.Walls[0].Broken, Is.True);
            Assert.That(state.Walls[0].RepairTicks, Is.EqualTo(123));
            Assert.That(state.Players.All(p => p.Alive && p.RespawnTicks == 0), Is.True);
            Assert.That(state.Balls.All(b => !b.Active), Is.True);
            Assert.That(state.PhaseTicks, Is.EqualTo(180));
        }

        [TestCase(0f, 0)]
        [TestCase(-.15f, 0)]
        [TestCase(.15f, 1)]
        public void SimultaneousPickupUsesNearestSeatThenSeatZeroForExactTie(float offset, int receiver)
        {
            DashArenaState state = Live();
            state.Players[0].Position = new Vector2(-.9f, 8);
            state.Players[1].Position = new Vector2(.9f, 8);
            state.Pickups[0] = Pickup(42, DashPickupKind.Green, new Vector2(offset, 8));
            IReadOnlyList<DashArenaEvent> events = Step(state);
            Assert.That(events.Single(e => e.Kind == DashArenaEventKind.Pickup).Seat, Is.EqualTo(receiver));
            Assert.That(state.Players[receiver].GreenTicks, Is.EqualTo(state.Rules.GreenTicks));
            Assert.That(state.Players[1 - receiver].GreenTicks, Is.Zero);
            Assert.That(state.Pickups[0].Active, Is.False);
        }

        [Test]
        public void SameColorRefreshesInsteadOfAddingAndDifferentColorsCoexistInStableIdOrder()
        {
            DashArenaState state = Live();
            state.Players[0].Position = new Vector2(10, 8);
            state.Players[0].GreenTicks = 123; state.Players[0].PurpleTicks = 234;
            state.Pickups[0] = Pickup(20, DashPickupKind.Green, new Vector2(10.6f, 8));
            state.Pickups[1] = Pickup(10, DashPickupKind.Purple, new Vector2(9.4f, 8));
            uint random = state.RandomState;
            IReadOnlyList<DashArenaEvent> events = Step(state);
            Assert.That(events.Where(e => e.Kind == DashArenaEventKind.Pickup).Select(e => e.EntityId), Is.EqualTo(new[] { 10, 20 }));
            Assert.That(state.Players[0].GreenTicks, Is.EqualTo(state.Rules.GreenTicks));
            Assert.That(state.Players[0].PurpleTicks, Is.EqualTo(state.Rules.PurpleTicks));
            Assert.That(state.RandomState, Is.EqualTo(random));
        }

        [Test]
        public void DynamicallyBlockedGenerationSpendsNoRandomOrIdAndRetriesWhenClear()
        {
            var builder = Empty(); builder.PickupCandidates = new[] { new Vector2(10, 8) };
            DashArenaState state = Live(builder);
            state.SpawnTicks = 1; state.NextPickupId = 77;
            float near = state.Rules.PickupRadius + state.Rules.BallRadius + state.Rules.RepairClearance - .01f;
            state.Balls[0] = new DashBallState { Active = true, Position = builder.PickupCandidates[0] + Vector2.right * near };
            uint original = state.RandomState;
            Run(state, 5);
            Assert.That(state.Pickups.All(p => !p.Active), Is.True);
            Assert.That(state.RandomState, Is.EqualTo(original));
            Assert.That(state.NextPickupId, Is.EqualTo(77));
            Assert.That(state.SpawnTicks, Is.Zero);
            state.Balls[0].Position = new Vector2(-10, 8);
            uint expected = original;
            ArenaRandom.TryChoosePickup(ref expected, 1, out _, out int kind);
            Step(state);
            Assert.That(state.RandomState, Is.EqualTo(expected));
            Assert.That(state.NextPickupId, Is.EqualTo(78));
            Assert.That(state.Pickups[0].Id, Is.EqualTo(77));
            Assert.That(state.Pickups[0].Kind, Is.EqualTo((DashPickupKind)kind));
            Assert.That(state.Pickups[0].Position, Is.EqualTo(builder.PickupCandidates[0]));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PurpleRejectsOrdinaryAndGreenDashDynamicRecoilButPreservesNonpenetration(bool green)
        {
            DashArenaState state = Live();
            state.Players[0].Position = new Vector2(-.9f, 8);
            state.Players[1].Position = new Vector2(.9f, 8);
            state.Players[0].GreenTicks = green ? 100 : 0;
            state.Players[1].PurpleTicks = 100;
            DashArenaState vulnerable = state.Clone(); vulnerable.Players[1].PurpleTicks = 0;
            IReadOnlyList<DashArenaEvent> events = Step(state, Input(Vector2.right, true));
            Step(vulnerable, Input(Vector2.right, true));
            Assert.That(Count(events, DashArenaEventKind.Impact), Is.EqualTo(1));
            Assert.That(state.Players[1].ControlVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(state.Players[1].ExternalVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(vulnerable.Players[1].ExternalVelocity.x, Is.GreaterThan(0));
            Assert.That(Vector2.Distance(state.Players[0].Position, state.Players[1].Position),
                Is.GreaterThanOrEqualTo(2 * state.Rules.PlayerRadius - .0001f));
        }

        [Test]
        public void PurpleRejectsDangerousBallImpactWithoutRemovingBallCollision()
        {
            DashArenaState state = Live();
            state.Players[0].Position = new Vector2(0, 8); state.Players[0].PurpleTicks = 100;
            state.Balls[0] = new DashBallState { Active = true, Position = new Vector2(1.6f, 8), Velocity = Vector2.left * 48 };
            IReadOnlyList<DashArenaEvent> events = Step(state);
            Assert.That(Count(events, DashArenaEventKind.Impact), Is.EqualTo(1));
            Assert.That(state.Players[0].Velocity, Is.EqualTo(Vector2.zero));
            Assert.That(state.Balls[0].Velocity.x, Is.GreaterThan(0));
            Assert.That(state.Balls[0].DangerousImpactCount, Is.Zero);
            Assert.That(Vector2.Distance(state.Players[0].Position, state.Balls[0].Position),
                Is.GreaterThanOrEqualTo(state.Rules.PlayerRadius + state.Rules.BallRadius - .0001f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedBlueBlockedByBallOrOpponentPreservesMovingDashExactly(bool opponent)
        {
            var builder = Empty(); builder.TeleportCandidates = new[] { new Vector2(-10, 8) };
            DashArenaState state = Live(builder);
            state.Players[0].Position = new Vector2(10, 8);
            state.Players[0].DashTicks = 10; state.Players[0].DashDirection = Vector2.right;
            state.Players[0].ControlVelocity = Vector2.right * state.Rules.DashSpeed;
            state.Players[0].ExternalVelocity = Vector2.up * 6;
            if (opponent) state.Players[1].Position = builder.TeleportCandidates[0];
            else state.Balls[0] = new DashBallState { Active = true, Position = builder.TeleportCandidates[0] };
            state.Pickups[0] = Pickup(42, DashPickupKind.Blue, state.Players[0].Position);
            DashArenaState noPickup = state.Clone(); noPickup.Pickups[0].Active = false;
            uint expected = state.RandomState; ArenaRandom.Next(ref expected);
            IReadOnlyList<DashArenaEvent> events = Step(state);
            Step(noPickup);
            Assert.That(state.Players[0], Is.EqualTo(noPickup.Players[0]), "A failed teleport must not alter post-physics motion or Dash.");
            Assert.That(state.Players[0].DashTicks, Is.EqualTo(9));
            Assert.That(state.Players[0].DashCooldownTicks, Is.Zero);
            Assert.That(state.Players[0].ExternalVelocity.y, Is.GreaterThan(0));
            Assert.That(state.Players[0].Position.x, Is.GreaterThan(10));
            Assert.That(Count(events, DashArenaEventKind.TeleportFailed), Is.EqualTo(1));
            Assert.That(Count(events, DashArenaEventKind.Teleport), Is.Zero);
            Assert.That(state.RandomState, Is.EqualTo(expected));
        }

        [Test]
        public void BothPlayersPressingKickoffGateCannotOverlapAnyOfEightSpawnedBalls()
        {
            var rules = ArenaRulesProfile.CreateGreybox(ballCount: 8);
            DashArenaState state = DashArenaSimulation.Create(rules, 123);
            DashArenaSimulation.StartMatch(state);
            var events = new List<DashArenaEvent>();
            for (int tick = 0; tick < rules.KickoffTicks; tick++)
                events.AddRange(Step(state, Input(Vector2.down, true), Input(Vector2.up, true)));
            Assert.That(state.Phase, Is.EqualTo(MatchState.Live));
            Assert.That(state.Balls.Count(ball => ball.Active), Is.EqualTo(8));
            Assert.That(Count(events, DashArenaEventKind.DashStarted), Is.Zero);
            Assert.That(Count(events, DashArenaEventKind.GateOpened), Is.EqualTo(1));
            for (int seat = 0; seat < 2; seat++)
            for (int ball = 0; ball < state.Balls.Length; ball++)
                Assert.That(Vector2.Distance(state.Players[seat].Position, state.Balls[ball].Position),
                    Is.GreaterThanOrEqualTo(rules.PlayerRadius + rules.BallRadius + rules.RepairClearance - .0001f),
                    $"Seat {seat}, ball {ball} at the spawn boundary");
            Assert.That(state.Players[0].Position.y, Is.GreaterThan(0));
            Assert.That(state.Players[1].Position.y, Is.LessThan(0));
        }

        static ArenaRulesProfile.Builder Empty(int balls = 1)
        {
            var builder = ArenaRulesProfile.Builder.CreateGreybox(ballCount: balls);
            builder.Pillars = Array.Empty<ArenaPillarDefinition>();
            builder.Walls = Array.Empty<ArenaWallDefinition>();
            builder.PickupCandidates = new[] { new Vector2(0, 26) };
            builder.TeleportCandidates = new[] { new Vector2(-10, 0) };
            return builder;
        }

        static DashArenaState Live(ArenaRulesProfile.Builder builder = null)
        {
            DashArenaState state = DashArenaSimulation.Create((builder ?? Empty()).Freeze(), 0x12345678);
            DashArenaSimulation.StartMatch(state);
            state.Phase = MatchState.Live; state.PhaseTicks = 0; state.GateClosed = false;
            return state;
        }

        static DashArenaState WallFixture()
        {
            var builder = Empty();
            builder.Walls = new[] { new ArenaWallDefinition(200, new Vector2(10, 8), new Vector2(2, .5f)) };
            return Live(builder);
        }

        static DashArenaInput Input(Vector2 move, bool dash = false) => new DashArenaInput { Move = move, DashPressed = dash };
        static DashPickupState Pickup(int id, DashPickupKind kind, Vector2 position) =>
            new DashPickupState { Active = true, Id = id, Kind = kind, Position = position };
        static IReadOnlyList<DashArenaEvent> Step(DashArenaState state, DashArenaInput first = default, DashArenaInput second = default) =>
            DashArenaSimulation.Step(state, first, second);
        static int Count(IEnumerable<DashArenaEvent> events, DashArenaEventKind kind) => events.Count(item => item.Kind == kind);
        static IReadOnlyList<DashArenaEvent> Run(DashArenaState state, int ticks)
        {
            var events = new List<DashArenaEvent>();
            for (int tick = 0; tick < ticks; tick++) events.AddRange(Step(state));
            return events;
        }

        static void CrossGoal(DashArenaState state, int scorer)
        {
            float sign = scorer == 0 ? -1 : 1;
            state.Balls[0] = new DashBallState { Active = true,
                Position = Vector2.up * sign * (state.Rules.HalfHeight + .2f), Velocity = Vector2.up * sign * 30 };
            IReadOnlyList<DashArenaEvent> events = Step(state);
            Assert.That(events.Single(e => e.Kind == DashArenaEventKind.Goal).Seat, Is.EqualTo(scorer));
        }

        static object Invoke(string method, params object[] args)
        {
            MethodInfo target = typeof(DashArenaSimulation).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(target, Is.Not.Null, "Focused boundary fixture requires " + method);
            return target.Invoke(null, args);
        }

        static IEnumerable<bool[]> Contacts(DashArenaState state)
        {
            yield return state.PlayerContacts; yield return state.PlayerBallContacts;
            yield return state.CylinderContacts; yield return state.WallContacts; yield return state.BallWallContacts;
        }

        static void SeedLatches(DashArenaState state)
        {
            foreach (bool[] contacts in Contacts(state))
                for (int i = 0; i < contacts.Length; i++) contacts[i] = true;
        }

        static void AssertSeatLatches(bool[] contacts, int stride)
        {
            Assert.That(contacts.Take(stride).All(value => !value), Is.True);
            Assert.That(contacts.Skip(stride).All(value => value), Is.True);
        }

        static void AssertSnapshotEqual(DashArenaState expected, DashArenaState actual)
        {
            Assert.That(actual.Rules.ContentHash, Is.EqualTo(expected.Rules.ContentHash));
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            foreach (FieldInfo field in typeof(DashArenaState).GetFields(flags))
                Assert.That(field.GetValue(actual), Is.EqualTo(field.GetValue(expected)), field.Name);
            foreach (PropertyInfo property in typeof(DashArenaState).GetProperties(flags))
                if (property.PropertyType.IsArray)
                    CollectionAssert.AreEqual((Array)property.GetValue(expected), (Array)property.GetValue(actual), property.Name);
            Assert.That(actual.Checksum(), Is.EqualTo(expected.Checksum()));
        }
    }
}
