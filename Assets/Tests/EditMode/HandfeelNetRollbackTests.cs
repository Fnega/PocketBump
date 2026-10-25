using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    // Seeded mechanism fixtures retain production candidate geometry/tuning. Every
    // claimed contact/reset is asserted from actual Step output, never fabricated
    // in retained rollback snapshots. Reference execution calls the pure simulator.
    public sealed class HandfeelNetRollbackTests
    {
        [Test]
        public void LateReorderedInputReplaysGentleTouchSixthSeventhUpdateAndGenuineSecondContact()
        {
            ReplayResult result = Replay(false, 48);
            List<DashArenaEvent> impacts = result.Events.Where(e => e.Kind == DashArenaEventKind.Impact && e.Seat == 1 && e.EntityId == 2).ToList();
            Assert.That(impacts.Count, Is.GreaterThanOrEqualTo(2), "Fixture must actually separate and produce a second ball contact.");
            int hit = impacts[0].Tick;
            Assert.That(hit, Is.GreaterThanOrEqualTo(2), "First contact must depend on delayed remote input, not only pre-delay motion.");
            Assert.That(impacts[1].Tick, Is.GreaterThan(hit + 7), "The fixture must expose the sixth/seventh boundary before another hit.");
            Assert.That(result.States[hit].Players[1].RecoilGraceTicks, Is.EqualTo(6));
            Assert.That(result.States[hit].Players[1].ControlVelocity.magnitude, Is.LessThan(12f), "This is an ordinary gentle contact.");
            for (int step = 1; step <= 6; step++)
            {
                DashPlayerState before = result.States[hit + step - 1].Players[1];
                DashPlayerState after = result.States[hit + step].Players[1];
                Assert.That(after.RecoilGraceTicks, Is.EqualTo(6 - step), "initial update " + step);
                Assert.That(after.ExternalVelocity.magnitude,
                    Is.EqualTo(Mathf.Max(0, before.ExternalVelocity.magnitude - 8f / 60f)).Within(.00001f));
            }
            float sixth = result.States[hit + 6].Players[1].ExternalVelocity.magnitude;
            Assert.That(sixth, Is.GreaterThan(0f), "Nonzero external motion must reach the faster-tail boundary.");
            Assert.That(result.States[hit + 7].Players[1].ExternalVelocity.magnitude,
                Is.EqualTo(Mathf.Max(0, sixth - 48f / 60f)).Within(.00001f));
            Assert.That(result.States[impacts[1].Tick].Players[1].RecoilGraceTicks, Is.EqualTo(6));
            Assert.That(result.Rollbacks, Is.GreaterThan(0));
        }

        [Test]
        public void LateReorderedInputReplaysActualBluePickupAndClearsAnActiveRecoilStage()
        {
            ReplayResult result = Replay(true, 24);
            DashArenaEvent impact = result.Events.First(e => e.Kind == DashArenaEventKind.Impact && e.Seat == 1);
            DashArenaEvent teleport = result.Events.Single(e => e.Kind == DashArenaEventKind.Teleport && e.Seat == 1);
            Assert.That(teleport.Tick, Is.GreaterThan(impact.Tick));
            Assert.That(result.States[teleport.Tick - 1].Players[1].RecoilGraceTicks, Is.GreaterThan(0),
                "The actual teleport must clear a still-active phase, not an already zero field.");
            Assert.That(result.States[teleport.Tick].Players[1].RecoilGraceTicks, Is.Zero);
            Assert.That(result.States[teleport.Tick].Players[1].ExternalVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(result.States[teleport.Tick].Players[1].ControlVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(result.States[teleport.Tick].Pickups[0].Active, Is.False);
            Assert.That(result.States[teleport.Tick].RandomState, Is.Not.EqualTo(result.Initial.RandomState));
            Assert.That(result.Rollbacks, Is.GreaterThan(0));
        }

        [Test]
        public void StageOnlySnapshotDifferenceIsChecksummedAndExactCopyPreservesIt()
        {
            DashArenaState state = Initial(false);
            while (state.Tick < 8 && state.Players[1].RecoilGraceTicks == 0)
                DashArenaSimulation.Step(state, default, state.Tick < 2 ? default : Tape(state.Tick - 2));
            Assert.That(state.Players[1].RecoilGraceTicks, Is.EqualTo(6), "Obtain the baseline from a real collision first.");
            DashArenaState altered = state.Clone();
            altered.Players[1].RecoilGraceTicks--;
            Assert.That(altered.Checksum(), Is.Not.EqualTo(state.Checksum()));
            var restored = new DashArenaState(state.Rules, state.InitialSeed, state.Epoch);
            restored.CopyFrom(altered);
            AssertComplete(altered, restored);
            restored.CopyFrom(state);
            AssertComplete(state, restored);
            Assert.That(restored.Players, Is.Not.SameAs(state.Players));
        }

        sealed class ReplayResult
        {
            public DashArenaState Initial;
            public readonly List<DashArenaState> States = new List<DashArenaState>();
            public readonly List<DashArenaEvent> Events = new List<DashArenaEvent>();
            public readonly List<DashArenaEvent[]> EventFrames = new List<DashArenaEvent[]>();
            public int Rollbacks;
        }

        static ReplayResult Replay(bool teleport, int steps)
        {
            var result = new ReplayResult { Initial = Initial(teleport) };
            DashArenaState reference = result.Initial.Clone();
            for (int tick = 0; tick < steps; tick++)
            {
                DashArenaEvent[] events = DashArenaSimulation.Step(reference, default, tick < 2 ? default : Tape(tick - 2)).ToArray();
                result.Events.AddRange(events); result.EventFrames.Add(events); result.States.Add(reference.Clone());
            }
            var late = new DashRollbackSession(result.Initial.Clone(), 0);
            int compared = 0;
            for (int frame = 0; frame < steps; frame++)
            {
                if (frame >= 6)
                {
                    // Explicit reordering: the newer real sample arrives before the hole.
                    if (frame % 2 == 0) Assert.That(late.TryReceiveRemote(frame - 3, Tape(frame - 5)), Is.True);
                    Assert.That(late.TryReceiveRemote(frame - 4, Tape(frame - 6)), Is.True);
                }
                late.Advance(default);
                if (late.LastRollbackTick >= 0) result.Rollbacks++;
                Assert.That(late.Status, Is.EqualTo(RollbackStatus.Ok), "frame " + frame);
                CompareConfirmed();
            }
            // Flush newest-to-oldest as well, then require every retained/corrected frame,
            // not only one final checksum, to agree with independent on-time execution.
            for (int frame = steps - 1; frame >= steps - 6; frame--)
                Assert.That(late.TryReceiveRemote(frame + 2, Tape(frame)), Is.True);
            late.Reconcile();
            if (late.LastRollbackTick >= 0) result.Rollbacks++;
            CompareConfirmed();
            Assert.That(compared, Is.EqualTo(steps));
            AssertComplete(reference, late.State);
            Assert.That(late.Status, Is.EqualTo(RollbackStatus.Ok));
            return result;

            void CompareConfirmed()
            {
                while (compared <= late.ConfirmedStateTick)
                {
                    Assert.That(late.TryGetConfirmedState(compared, out DashArenaState actual), Is.True, "snapshot " + compared);
                    AssertComplete(result.States[compared], actual);
                    Assert.That(late.TryGetConfirmedEvents(compared, out IReadOnlyList<DashArenaEvent> events), Is.True);
                    CollectionAssert.AreEqual(result.EventFrames[compared], events, "events at " + compared);
                    compared++;
                }
            }
        }

        static DashArenaState Initial(bool teleport)
        {
            var state = DashArenaSimulation.Create(ArenaRulesProfile.CreateHandfeelCandidate(), 0x12345678, 3);
            DashArenaSimulation.StartMatch(state);
            state.Phase = MatchState.Live; state.PhaseTicks = 0; state.GateClosed = false;
            state.Players[0].Position = new Vector2(10f, 0f);
            state.Players[1].Position = Vector2.zero;
            state.Players[1].Facing = Vector2.right;
            state.Players[1].ControlVelocity = Vector2.right * 6f;
            state.Balls[0] = new DashBallState { Active = true, Position = new Vector2(1.3f, 0f) };
            if (teleport)
                state.Pickups[0] = new DashPickupState { Active = true, Id = 41, Kind = DashPickupKind.Blue, Position = new Vector2(1.85f, 0f) };
            return state;
        }

        static DashArenaInput Tape(int sampleTick) => new DashArenaInput
        { Move = sampleTick < 30 ? Vector2.right : Vector2.left };

        static void AssertComplete(DashArenaState expected, DashArenaState actual)
        {
            Assert.That(actual.Rules.ContentHash, Is.EqualTo(expected.Rules.ContentHash));
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            foreach (FieldInfo field in typeof(DashArenaState).GetFields(flags))
                AssertExact(field.GetValue(expected), field.GetValue(actual), field.Name);
            foreach (PropertyInfo property in typeof(DashArenaState).GetProperties(flags))
            {
                if (!property.PropertyType.IsArray) continue;
                var first = (Array)property.GetValue(expected); var second = (Array)property.GetValue(actual);
                Assert.That(second.Length, Is.EqualTo(first.Length), property.Name);
                for (int i = 0; i < first.Length; i++) AssertExact(first.GetValue(i), second.GetValue(i), property.Name + "[" + i + "]");
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
