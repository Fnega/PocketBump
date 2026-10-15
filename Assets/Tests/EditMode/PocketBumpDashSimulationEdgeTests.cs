using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    // Focused pre-baseline mechanic fixtures and snapshot replay, not a natural full-match
    // or network-rollback acceptance claim. No test depends on the main mechanism suite.
    public sealed class PocketBumpDashSimulationEdgeTests
    {
        [Test]
        public void BlueAcceptsAWholeColliderSafeDestinationLessThanOneDiameterAway()
        {
            ArenaRulesProfile.Builder builder = EmptyArena();
            builder.TeleportCandidates = new[] { new Vector2(11, 0) };
            DashArenaState state = LiveFixture(builder);
            state.Players[0].Position = new Vector2(10, 0);
            Pickup(state, 0, 10, DashPickupKind.Blue, new Vector2(10, 0));

            IReadOnlyList<DashArenaEvent> events = Step(state);

            Assert.That(state.Players[0].Position, Is.EqualTo(new Vector2(11, 0)));
            Assert.That(Count(events, DashArenaEventKind.Teleport), Is.EqualTo(1));
            Assert.That(Count(events, DashArenaEventKind.TeleportFailed), Is.Zero);
        }

        [Test]
        public void FailedBlueDoesNotSuppressLaterOriginalPositionPickup()
        {
            ArenaRulesProfile.Builder builder = EmptyArena();
            builder.TeleportCandidates = new[] { new Vector2(-10, 0) };
            DashArenaState state = LiveFixture(builder);
            state.Players[0].Position = new Vector2(10, 0);
            state.Players[1].Position = new Vector2(-10, 0); // The only authored safe slot is occupied now.
            // Two nonoverlapping pickup footprints, both within the player's reach.
            Pickup(state, 0, 10, DashPickupKind.Blue, new Vector2(9.35f, 0));
            Pickup(state, 1, 20, DashPickupKind.Green, new Vector2(10.65f, 0));
            uint expectedRandom = state.RandomState;
            ArenaRandom.Next(ref expectedRandom);

            IReadOnlyList<DashArenaEvent> events = Step(state);

            Assert.That(Count(events, DashArenaEventKind.TeleportFailed), Is.EqualTo(1));
            Assert.That(Count(events, DashArenaEventKind.Pickup), Is.EqualTo(2));
            Assert.That(state.Players[0].GreenTicks, Is.EqualTo(state.Rules.GreenTicks));
            Assert.That(state.Players[0].Position, Is.EqualTo(new Vector2(10, 0)));
            Assert.That(state.Pickups[0].Active || state.Pickups[1].Active, Is.False);
            Assert.That(state.RandomState, Is.EqualTo(expectedRandom));
        }

        [Test]
        public void SuccessfulBlueDoesNotRecursivelyConsumeDestinationPickup()
        {
            ArenaRulesProfile.Builder builder = EmptyArena();
            builder.TeleportCandidates = new[] { new Vector2(15, 0) };
            DashArenaState state = LiveFixture(builder);
            state.Players[0].Position = new Vector2(10, 0);
            Pickup(state, 0, 10, DashPickupKind.Blue, new Vector2(10, 0));
            Pickup(state, 1, 20, DashPickupKind.Green, new Vector2(15, 0));

            IReadOnlyList<DashArenaEvent> first = Step(state);

            Assert.That(Count(first, DashArenaEventKind.Teleport), Is.EqualTo(1));
            Assert.That(Count(first, DashArenaEventKind.Pickup), Is.EqualTo(1));
            Assert.That(state.Players[0].GreenTicks, Is.Zero);
            Assert.That(state.Pickups[1].Active, Is.True);
            IReadOnlyList<DashArenaEvent> next = Step(state);
            Assert.That(Count(next, DashArenaEventKind.Pickup), Is.EqualTo(1));
            Assert.That(state.Players[0].GreenTicks, Is.EqualTo(state.Rules.GreenTicks));
        }

        [Test]
        public void TwoGoalsAndFallInOneTickSelectNorthGoalWithoutPickupOrFallPoint()
        {
            ArenaRulesProfile.Builder builder = EmptyArena(2);
            DashArenaState state = LiveFixture(builder);
            state.Players[0].Position = new Vector2(state.Rules.HalfWidth + .70f, 0);
            state.Players[0].ControlVelocity = Vector2.right * 10f;
            state.Players[1].Position = new Vector2(0, -10);
            Pickup(state, 0, 10, DashPickupKind.Green, state.Players[1].Position);
            state.Balls[0] = new DashBallState
            {
                Active = true, Position = new Vector2(0, -state.Rules.HalfHeight - .2f),
                Velocity = Vector2.down * 20f
            };
            state.Balls[1] = new DashBallState
            {
                Active = true, Position = new Vector2(0, state.Rules.HalfHeight + .2f),
                Velocity = Vector2.up * 20f
            };

            IReadOnlyList<DashArenaEvent> events = Step(state);

            Assert.That(state.Scores, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(state.LastScorer, Is.EqualTo(1));
            Assert.That(state.Phase, Is.EqualTo(MatchState.Scored));
            Assert.That(Count(events, DashArenaEventKind.Goal), Is.EqualTo(1));
            Assert.That(Count(events, DashArenaEventKind.Fell), Is.EqualTo(1));
            Assert.That(Count(events, DashArenaEventKind.Pickup), Is.Zero);
            Assert.That(state.Players[1].GreenTicks, Is.Zero);
        }

        [Test]
        public void ArmorExpiryDuringContinuousPillarContactDoesNotCreateAStun()
        {
            ArenaRulesProfile.Builder builder = EmptyArena();
            builder.Pillars = new[] { new ArenaPillarDefinition(100, new Vector2(0, 8), 1f) };
            DashArenaState state = LiveFixture(builder);
            state.Players[0].Position = new Vector2(-1f - state.Rules.PlayerRadius, 8);
            state.Players[0].PurpleTicks = 2;
            var intoPillar = new DashArenaInput { Move = Vector2.right };

            IReadOnlyList<DashArenaEvent> first = Step(state, intoPillar);

            Assert.That(state.CylinderContacts[0], Is.True);
            Assert.That(Count(first, DashArenaEventKind.Stunned), Is.Zero);
            for (int i = 0; i < 8; i++)
            {
                IReadOnlyList<DashArenaEvent> events = Step(state, intoPillar);
                Assert.That(state.Players[0].StunTicks, Is.Zero, "Continuing episode tick " + i);
                Assert.That(Count(events, DashArenaEventKind.Stunned), Is.Zero);
            }
            Assert.That(state.Players[0].PurpleTicks, Is.Zero);
        }

        [Test]
        public void DueWallRepairIgnoresFallenPlayerButNotActiveBall()
        {
            ArenaRulesProfile.Builder builder = EmptyArena();
            builder.Walls = new[] { new ArenaWallDefinition(200, new Vector2(10, 8), new Vector2(2, .5f)) };
            DashArenaState state = LiveFixture(builder);
            state.Walls[0] = new DashWallState { Broken = true, RepairTicks = 1 };
            state.Players[0].Alive = false;
            state.Players[0].RespawnTicks = 100;
            state.Players[0].Position = builder.Walls[0].Position;
            state.Balls[0] = new DashBallState { Active = true, Position = builder.Walls[0].Position };

            IReadOnlyList<DashArenaEvent> occupied = Step(state);

            Assert.That(state.Walls[0].RepairPending, Is.True);
            Assert.That(Count(occupied, DashArenaEventKind.WallRepairPending), Is.EqualTo(1));
            // Explicit next-boundary fixture: the ball has cleared, the fallen pose has not.
            state.Balls[0].Position = new Vector2(0, -8);
            IReadOnlyList<DashArenaEvent> clear = Step(state);
            Assert.That(state.Walls[0].Broken, Is.False);
            Assert.That(Count(clear, DashArenaEventKind.WallRepaired), Is.EqualTo(1));
            Assert.That(state.Players[0].Alive, Is.False);
        }

        [Test]
        public void MatchOverAndPregameDoNotAdvanceAnyAuthoritativeState()
        {
            foreach (MatchState phase in new[] { MatchState.Pregame, MatchState.MatchOver })
            {
                var state = DashArenaSimulation.Create(ArenaRulesProfile.CreateGreybox(), 123);
                state.Phase = phase;
                state.Tick = 50;
                state.Players[0].DashTicks = 12;
                state.Players[0].StunTicks = 9;
                state.Walls[0] = new DashWallState { Broken = true, RepairTicks = 1 };
                DashArenaState before = state.Clone();
                for (int i = 0; i < 10; i++)
                    Assert.That(Step(state, new DashArenaInput { Move = Vector2.right, DashPressed = true }), Is.Empty);
                AssertComplete(before, state);
            }
        }

        [Test]
        public void SnapshotReplayAcrossTeleportAndRepairReproducesStateAndEventIdentities()
        {
            ArenaRulesProfile.Builder builder = EmptyArena();
            builder.Walls = new[] { new ArenaWallDefinition(200, new Vector2(10, 8), new Vector2(2, .5f)) };
            builder.TeleportCandidates = new[] { new Vector2(15, 0) };
            DashArenaState baseline = LiveFixture(builder);
            baseline.Players[0].Position = new Vector2(10, 0);
            baseline.Walls[0] = new DashWallState { Broken = true, RepairTicks = 7 };
            Pickup(baseline, 0, 10, DashPickupKind.Blue, new Vector2(10, 0));
            DashArenaState checkpoint = baseline.Clone();
            var expectedEvents = new List<DashArenaEvent>();
            for (int tick = 0; tick < 10; tick++) expectedEvents.AddRange(Step(baseline, ReplayInput(tick)));

            DashArenaState corrected = checkpoint.Clone();
            for (int tick = 0; tick < 10; tick++) Step(corrected, new DashArenaInput { Move = Vector2.left });
            corrected.CopyFrom(checkpoint);
            var correctedEvents = new List<DashArenaEvent>();
            for (int tick = 0; tick < 10; tick++) correctedEvents.AddRange(Step(corrected, ReplayInput(tick)));

            AssertComplete(baseline, corrected);
            CollectionAssert.AreEqual(expectedEvents, correctedEvents);
            Assert.That(Count(expectedEvents, DashArenaEventKind.Teleport), Is.EqualTo(1));
            Assert.That(Count(expectedEvents, DashArenaEventKind.WallRepaired), Is.EqualTo(1));
            var identities = new HashSet<DashArenaEventIdentity>();
            foreach (DashArenaEvent item in correctedEvents) Assert.That(identities.Add(item.Identity), Is.True);
        }

        [Test]
        public void LongTapeUsesSeparateTenTickCorrectionsAroundTeleportAndSixHundredTickRepair()
        {
            ArenaRulesProfile.Builder builder = EmptyArena();
            builder.Walls = new[] { new ArenaWallDefinition(200, new Vector2(10, 8), new Vector2(2, .5f)) };
            builder.TeleportCandidates = new[] { new Vector2(15, 0) };
            DashArenaState baseline = LiveFixture(builder);
            baseline.Players[0].Position = new Vector2(10, 0);
            baseline.Walls[0] = new DashWallState { Broken = true, RepairTicks = 600 };
            Pickup(baseline, 0, 10, DashPickupKind.Blue, new Vector2(10, 0));
            DashArenaState corrected = baseline.Clone();
            int teleports = 0, repairs = 0;
            for (int tick = 0; tick < 720;)
            {
                if (tick == 0 || tick == 590 || tick == 610)
                {
                    DashArenaState checkpoint = corrected.Clone();
                    var expected = new List<DashArenaEvent>();
                    for (int offset = 0; offset < 10; offset++)
                    {
                        expected.AddRange(Step(baseline, LongTapeInput(tick + offset)));
                        Step(corrected, new DashArenaInput { Move = Vector2.left });
                    }
                    corrected.CopyFrom(checkpoint);
                    var replayed = new List<DashArenaEvent>();
                    for (int offset = 0; offset < 10; offset++)
                        replayed.AddRange(Step(corrected, LongTapeInput(tick + offset)));
                    CollectionAssert.AreEqual(expected, replayed);
                    teleports += Count(replayed, DashArenaEventKind.Teleport);
                    repairs += Count(replayed, DashArenaEventKind.WallRepaired);
                    tick += 10;
                }
                else
                {
                    IReadOnlyList<DashArenaEvent> expected = Step(baseline, LongTapeInput(tick));
                    IReadOnlyList<DashArenaEvent> replayed = Step(corrected, LongTapeInput(tick));
                    CollectionAssert.AreEqual(expected, replayed);
                    teleports += Count(replayed, DashArenaEventKind.Teleport);
                    repairs += Count(replayed, DashArenaEventKind.WallRepaired);
                    tick++;
                }
                AssertComplete(baseline, corrected);
            }
            Assert.That(baseline.Tick, Is.EqualTo(720));
            Assert.That(teleports, Is.EqualTo(1));
            Assert.That(repairs, Is.EqualTo(1));
        }

        static DashArenaInput LongTapeInput(int tick)
        {
            Vector2[] directions = { Vector2.up, Vector2.right, Vector2.down, Vector2.left };
            return new DashArenaInput { Move = directions[(tick / 30) % 4] };
        }

        static DashArenaInput ReplayInput(int tick) => new DashArenaInput
        { Move = tick < 5 ? Vector2.up : Vector2.left, DashPressed = tick == 2 };

        static ArenaRulesProfile.Builder EmptyArena(int ballCount = 1)
        {
            ArenaRulesProfile.Builder builder = ArenaRulesProfile.Builder.CreateGreybox(ballCount: ballCount);
            builder.Pillars = Array.Empty<ArenaPillarDefinition>();
            builder.Walls = Array.Empty<ArenaWallDefinition>();
            builder.PickupCandidates = new[] { new Vector2(0, 26) };
            builder.TeleportCandidates = new[] { new Vector2(-10, 0) };
            return builder;
        }

        static DashArenaState LiveFixture(ArenaRulesProfile.Builder builder)
        {
            DashArenaState state = DashArenaSimulation.Create(builder.Freeze(), 0x12345678);
            DashArenaSimulation.StartMatch(state);
            state.Phase = MatchState.Live;
            state.PhaseTicks = 0;
            state.GateClosed = false;
            return state;
        }

        static IReadOnlyList<DashArenaEvent> Step(DashArenaState state, DashArenaInput first = default) =>
            DashArenaSimulation.Step(state, first, DashArenaInput.Neutral);

        static void Pickup(DashArenaState state, int slot, int id, DashPickupKind kind, Vector2 position) =>
            state.Pickups[slot] = new DashPickupState { Active = true, Id = id, Kind = kind, Position = position };

        static int Count(IReadOnlyList<DashArenaEvent> events, DashArenaEventKind kind)
        {
            int result = 0;
            for (int i = 0; i < events.Count; i++) if (events[i].Kind == kind) result++;
            return result;
        }

        static void AssertComplete(DashArenaState expected, DashArenaState actual)
        {
            Assert.That(actual.Rules.ContentHash, Is.EqualTo(expected.Rules.ContentHash));
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            foreach (FieldInfo field in typeof(DashArenaState).GetFields(flags))
                AssertExact(field.GetValue(expected), field.GetValue(actual), field.Name);
            foreach (PropertyInfo property in typeof(DashArenaState).GetProperties(flags))
            {
                if (!property.PropertyType.IsArray) continue;
                var first = (Array)property.GetValue(expected);
                var second = (Array)property.GetValue(actual);
                Assert.That(second.Length, Is.EqualTo(first.Length), property.Name);
                for (int i = 0; i < first.Length; i++) AssertExact(first.GetValue(i), second.GetValue(i), property.Name + i);
            }
            Assert.That(actual.Checksum(), Is.EqualTo(expected.Checksum()));
        }

        static void AssertExact(object expected, object actual, string path)
        {
            Type type = expected.GetType();
            if (type == typeof(float))
                Assert.That(BitConverter.ToInt32(BitConverter.GetBytes((float)actual), 0),
                    Is.EqualTo(BitConverter.ToInt32(BitConverter.GetBytes((float)expected), 0)), path);
            else if (type.IsPrimitive || type.IsEnum) Assert.That(actual, Is.EqualTo(expected), path);
            else foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                AssertExact(field.GetValue(expected), field.GetValue(actual), path + "." + field.Name);
        }
    }
}
