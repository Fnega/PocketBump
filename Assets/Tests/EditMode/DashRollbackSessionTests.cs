using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    // These exercise the pure rollback adapter and explicit mechanism/terminal fixtures.
    // They do not claim socket transport, a naturally completed match, or LAN acceptance.
    public sealed class DashRollbackSessionTests
    {
        [Test]
        public void InputEqualityIncludesExactMovementAndDashAndHasConsistentHash()
        {
            var original = Input(Vector2.right, true);
            Assert.That(original.EqualsInput(Input(Vector2.right, true)), Is.True);
            Assert.That(original.Equals((object)Input(Vector2.right, true)), Is.True);
            Assert.That(original.GetHashCode(), Is.EqualTo(Input(Vector2.right, true).GetHashCode()));
            Assert.That(original.EqualsInput(Input(Vector2.right)), Is.False);
            Assert.That(original.EqualsInput(Input(new Vector2(1f, .000001f), true)), Is.False,
                "Rollback input equality must not use Unity's approximate vector operator.");
            Assert.That(original.Equals(null), Is.False);
        }

        [Test]
        public void ConstructorValidatesSeatAndKeepsTheLiveStateReference()
        {
            DashArenaState initial = Live();
            Assert.Throws<ArgumentNullException>(() => new DashRollbackSession(null, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DashRollbackSession(initial, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DashRollbackSession(initial, 2));
            var session = new DashRollbackSession(initial, 1);
            Assert.That(session.State, Is.SameAs(initial));
            Assert.That(session.ConfirmedTick, Is.EqualTo(1));
            Assert.That(session.ConfirmedStateTick, Is.EqualTo(-1));
            Assert.That(DashRollbackSession.DelayTicks, Is.EqualTo(2));
            Assert.That(DashRollbackSession.BufferTicks, Is.EqualTo(12));
        }

        [TestCase(0)]
        [TestCase(1)]
        public void LocalDashIsAppliedAfterExactlyTwoTicksOnlyToAssignedSeat(int seat)
        {
            var session = new DashRollbackSession(Live(), seat);
            for (int tick = 0; tick < 3; tick++)
            {
                session.ReceiveRemote(tick + 2, default);
                IReadOnlyList<DashArenaEvent> events = session.Advance(tick == 0 ? Input(Vector2.right, true) : default);
                Assert.That(events.Count(e => e.Kind == DashArenaEventKind.DashStarted), Is.EqualTo(tick == 2 ? 1 : 0));
            }
            Assert.That(session.State.Players[seat].DashTicks, Is.EqualTo(18));
            Assert.That(session.State.Players[1 - seat].DashTicks, Is.Zero);
            Assert.That(session.State.Players[seat].DashDirection, Is.EqualTo(Vector2.right));
        }

        [Test]
        public void StoredLocalSampleIsNeverResampledAndHistoryIsNewestFirst()
        {
            var session = new DashRollbackSession(Live(), 0);
            session.QueueLocal(2, Input(Vector2.right, true));
            session.QueueLocal(2, Input(Vector2.left));
            session.Advance(default);
            session.QueueLocal(3, Input(Vector2.up));
            var history = new byte[8];
            Assert.That(session.CopyLocalHistory(3, history), Is.EqualTo(2));
            Assert.That(history[0], Is.EqualTo(Input(Vector2.up).Pack()));
            Assert.That(history[1], Is.EqualTo(Input(Vector2.right, true).Pack()));
            Assert.That(session.CopyLocalHistory(4, history), Is.Zero);
        }

        [Test]
        public void ReorderedInputDoesNotRegressPredictionOrRepeatDash()
        {
            var session = new DashRollbackSession(Live(), 0);
            Assert.That(session.TryReceiveRemote(5, Input(Vector2.right, true)), Is.True);
            Assert.That(session.TryReceiveRemote(2, Input(Vector2.left, true)), Is.True);
            Assert.That(session.NewestRemoteTick, Is.EqualTo(5));
            Assert.That(session.ConfirmedTick, Is.EqualTo(2));
            Assert.That(session.PredictedRemoteFor(6).EqualsInput(Input(Vector2.right)), Is.True);
            Assert.That(session.PredictedRemoteFor(4).EqualsInput(Input(Vector2.left)), Is.True,
                "A future real sample cannot drive an earlier predicted tick.");
            Assert.That(session.PredictedRemoteFor(1).EqualsInput(default), Is.True);
            Assert.That(session.TryReceiveRemote(5, Input(Vector2.right, true)), Is.True);
            Assert.That(session.TryReceiveRemote(5, Input(Vector2.up)), Is.False);
            Assert.That(session.PredictedRemoteFor(5).DashPressed, Is.True, "Real input keeps its own edge.");
        }

        [TestCase(0x20)]
        [TestCase(0x40)]
        [TestCase(0x80)]
        [TestCase(0x03)]
        [TestCase(0x0c)]
        public void InvalidHistoryVocabularyRejectsWholePacketBeforeOldestSampleInsertion(int invalid)
        {
            for (int offset = 0; offset < 3; offset++)
            {
                var session = new DashRollbackSession(Live(), 0);
                var history = new byte[] { 1, 4, 0 };
                history[offset] = (byte)invalid;
                Assert.That(session.ReceiveRemoteHistory(4, history, 3), Is.Zero);
                Assert.That(session.NewestRemoteTick, Is.EqualTo(-1));
                Assert.That(session.ConfirmedTick, Is.EqualTo(1));
                Assert.That(session.PredictedRemoteFor(6).EqualsInput(default), Is.True);
            }
        }

        [Test]
        public void ConflictingRetransmissionRejectsWholeHistoryWithoutFillingOtherSlots()
        {
            var session = new DashRollbackSession(Live(), 0);
            session.ReceiveRemote(3, Input(Vector2.up));
            Assert.That(session.ReceiveRemoteHistory(4, new byte[] { 1, 8, 0 }, 3), Is.Zero);
            Assert.That(session.ConfirmedTick, Is.EqualTo(1));
            Assert.That(session.NewestRemoteTick, Is.EqualTo(3));
            Assert.That(session.TryReceiveRemote(4, Input(Vector2.left)), Is.True);
            Assert.That(session.TryReceiveRemote(2, Input(Vector2.down)), Is.True);
            Assert.That(session.ConfirmedTick, Is.EqualTo(4));
        }

        [Test]
        public void InvalidHistoryLengthsAndFutureNewestTickDoNotMutateInput()
        {
            var session = new DashRollbackSession(Live(), 0);
            Assert.That(session.ReceiveRemoteHistory(2, null, 1), Is.Zero);
            Assert.That(session.ReceiveRemoteHistory(2, new byte[1], 0), Is.Zero);
            Assert.That(session.ReceiveRemoteHistory(10, new byte[9], 9), Is.Zero);
            Assert.That(session.ReceiveRemoteHistory(2, new byte[1], 2), Is.Zero);
            Assert.That(session.ReceiveRemoteHistory(0, new byte[3], 3), Is.Zero);
            Assert.That(session.ReceiveRemoteHistory(NetplayProtocol.FutureTickAllowance + 1, new byte[8], 8), Is.Zero);
            Assert.That(session.ReceiveRemoteHistory(int.MaxValue, new byte[8], 8), Is.Zero);
            Assert.That(session.TryReceiveRemote(-1, default), Is.False);
            Assert.That(session.TryReceiveRemote(NetplayProtocol.FutureTickAllowance + 1, default), Is.False);
            Assert.That(session.NewestRemoteTick, Is.EqualTo(-1));
            Assert.That(session.ConfirmedTick, Is.EqualTo(1));
        }

        [Test]
        public void ValidRetainedSuffixRepairsHoleEvenWhenPacketIncludesExpiredSamples()
        {
            var session = new DashRollbackSession(Live(), 0);
            for (int tick = 0; tick < 20; tick++)
            {
                if (tick + 2 != 10) session.ReceiveRemote(tick + 2, default);
                session.Advance(default);
            }
            Assert.That(session.Status, Is.EqualTo(RollbackStatus.Ok));
            Assert.That(session.ConfirmedTick, Is.EqualTo(9));
            Assert.That(session.ReceiveRemoteHistory(12, new byte[8], 8), Is.EqualTo(5), "Only ticks 8..12 remain in the allowed window.");
            Assert.That(session.ConfirmedTick, Is.EqualTo(21));
            session.Reconcile();
            Assert.That(session.LastRollbackTick, Is.EqualTo(-1), "The missing real neutral input matches prediction.");
            Assert.That(session.ConfirmedStateTick, Is.EqualTo(19));
        }

        [Test]
        public void MissingInputAtExactlyTwelveTicksCanStillRestoreItsPreviousSnapshot()
        {
            DashArenaState initial = Live();
            var baseline = new DashRollbackSession(initial.Clone(), 0);
            var late = new DashRollbackSession(initial.Clone(), 0);
            for (int frame = 0; frame < 14; frame++)
            {
                var remote = frame == 0 ? Input(Vector2.right, true) : default;
                baseline.ReceiveRemote(frame + 2, remote);
                if (frame != 0) late.ReceiveRemote(frame + 2, remote);
                baseline.Advance(default); late.Advance(default);
            }
            Assert.That(late.State.Tick, Is.EqualTo(14));
            Assert.That(late.TryReceiveRemote(2, Input(Vector2.right, true)), Is.True);
            late.Reconcile();
            Assert.That(late.LastRollbackTick, Is.EqualTo(2));
            AssertComplete(baseline.State, late.State);
            late.Advance(default);
            Assert.That(late.Status, Is.EqualTo(RollbackStatus.Ok));
            Assert.That(late.State.Tick, Is.EqualTo(15));
            Assert.That(late.TryReceiveRemote(2, Input(Vector2.right, true)), Is.False, "Thirteen-tick-old input is outside the window.");
        }

        [Test]
        public void GenuinelyMissingInputStopsBeforeOverwritingTheTwelveTickWindow()
        {
            var session = new DashRollbackSession(Live(), 0);
            for (int frame = 0; frame < 15; frame++)
            {
                if (frame != 0) session.ReceiveRemote(frame + 2, default);
                session.Advance(default);
            }
            Assert.That(session.Status, Is.EqualTo(RollbackStatus.InputHole));
            Assert.That(session.State.Tick, Is.EqualTo(14));
            int hash = session.State.Checksum();
            Assert.That(session.Advance(Input(Vector2.right, true)), Is.Empty);
            session.Reconcile();
            Assert.That(session.State.Checksum(), Is.EqualTo(hash));
        }

        [Test]
        public void CorrectedChecksumWaitsUntilInputsAreReplayedThenMatchesBaseline()
        {
            DashArenaState initial = Live();
            var baseline = new DashRollbackSession(initial.Clone(), 0);
            var late = new DashRollbackSession(initial.Clone(), 0);
            for (int frame = 0; frame < 12; frame++)
            {
                DashArenaInput remote = frame == 0 ? Input(Vector2.right, true) : default;
                baseline.ReceiveRemote(frame + 2, remote);
                if (frame != 0) late.ReceiveRemote(frame + 2, remote);
                baseline.Advance(default); late.Advance(default);
            }
            Assert.That(late.QueueRemoteChecksum(10, baseline.ChecksumAt(10)), Is.True);
            late.Reconcile();
            Assert.That(late.ValidatedChecksumCount, Is.Zero);
            Assert.That(late.TryGetConfirmedChecksum(10, out int before, out _), Is.True);
            Assert.That(before, Is.EqualTo(1));
            late.ReceiveRemote(2, Input(Vector2.right, true));
            Assert.That(late.TryGetConfirmedChecksum(10, out before, out _), Is.True);
            Assert.That(before, Is.EqualTo(1), "Receiving alone cannot publish an uncorrected checksum.");
            late.Reconcile();
            Assert.That(late.Status, Is.EqualTo(RollbackStatus.Ok));
            Assert.That(late.ValidatedChecksumCount, Is.EqualTo(1));
            AssertComplete(baseline.State, late.State);
        }

        [Test]
        public void DelayedDashReplaysEightBallStateTeleportRandomPickupAndWallRepairEvents()
        {
            var builder = Empty(8);
            builder.Walls = new[] { new ArenaWallDefinition(200, new Vector2(10, 16), new Vector2(2, .5f)) };
            DashArenaState initial = Live(builder);
            initial.Players[1].Position = new Vector2(10, 8);
            initial.Players[1].GreenTicks = 120; initial.Players[1].PurpleTicks = 180;
            initial.Walls[0] = new DashWallState { Broken = true, RepairTicks = 7 };
            initial.Pickups[0] = new DashPickupState { Active = true, Id = 41, Kind = DashPickupKind.Blue, Position = new Vector2(10, 9.6f) };
            initial.SpawnTicks = 3; initial.NextPickupId = 50;
            for (int i = 0; i < initial.Balls.Length; i++)
                initial.Balls[i] = new DashBallState { Active = true, Position = initial.Rules.BallSpawn(i) };
            var baseline = new DashRollbackSession(initial.Clone(), 0);
            var late = new DashRollbackSession(initial.Clone(), 0);
            for (int frame = 0; frame < 12; frame++)
            {
                DashArenaInput remote = frame == 0 ? Input(Vector2.up, true) : default;
                baseline.ReceiveRemote(frame + 2, remote);
                if (frame != 0) late.ReceiveRemote(frame + 2, remote);
                if (frame == 7) late.ReceiveRemote(2, Input(Vector2.up, true));
                baseline.Advance(default); late.Advance(default);
            }
            AssertComplete(baseline.State, late.State);
            var events = CompareConfirmedEvents(baseline, late, 0, 11);
            Assert.That(events.Count(e => e.Kind == DashArenaEventKind.DashStarted), Is.EqualTo(1));
            Assert.That(events.Count(e => e.Kind == DashArenaEventKind.Teleport), Is.EqualTo(1));
            Assert.That(events.Count(e => e.Kind == DashArenaEventKind.WallRepaired), Is.EqualTo(1));
            Assert.That(baseline.State.NextPickupId, Is.EqualTo(51));
            Assert.That(baseline.State.RandomState, Is.Not.EqualTo(initial.RandomState));
        }

        [Test]
        public void LongReorderedTapeMatchesEveryStateFieldAfterRepeatedBoundedCorrections()
        {
            var builder = Empty(8);
            builder.Walls = new[] { new ArenaWallDefinition(200, new Vector2(10, 16), new Vector2(2, .5f)) };
            DashArenaState initial = Live(builder);
            initial.Walls[0] = new DashWallState { Broken = true, RepairTicks = 600 };
            initial.Players[0].Position = new Vector2(-10, 8);
            initial.Players[1].Position = new Vector2(10, -8);
            var baseline = new DashRollbackSession(initial.Clone(), 0);
            var late = new DashRollbackSession(initial.Clone(), 0);
            for (int frame = 0; frame < 720; frame++)
            {
                baseline.ReceiveRemote(frame + 2, Tape(frame, 1));
                // Alternating delivery order supplies a newer sample before one old sample.
                if (frame >= 6)
                {
                    if (frame % 2 == 0) late.ReceiveRemote(frame - 3, Tape(frame - 5, 1));
                    late.ReceiveRemote(frame - 4, Tape(frame - 6, 1));
                }
                baseline.Advance(Tape(frame, 0)); late.Advance(Tape(frame, 0));
                Assert.That(late.Status, Is.EqualTo(RollbackStatus.Ok), "frame " + frame);
            }
            for (int frame = 714; frame < 720; frame++) late.ReceiveRemote(frame + 2, Tape(frame, 1));
            late.Reconcile();
            Assert.That(late.State.Tick, Is.EqualTo(720));
            AssertComplete(baseline.State, late.State);
            CompareConfirmedEvents(baseline, late, 708, 719);
            Assert.That(late.State.Walls[0].Broken, Is.False);
        }

        [Test]
        public void ConfirmedStateAndEventConsumersCannotMutateRetainedEvidence()
        {
            var session = new DashRollbackSession(Live(), 0);
            for (int frame = 0; frame < 3; frame++)
            {
                session.ReceiveRemote(frame + 2, default);
                session.Advance(frame == 0 ? Input(Vector2.right, true) : default);
            }
            Assert.That(session.TryGetConfirmedEvents(2, out IReadOnlyList<DashArenaEvent> events), Is.True);
            Assert.That(events.Count, Is.GreaterThan(0));
            Assert.Throws<NotSupportedException>(() => ((IList<DashArenaEvent>)events)[0] = default);
            int checksum = session.ChecksumAt(2);
            Assert.That(session.TryGetConfirmedState(2, out DashArenaState copy), Is.True);
            copy.Players[0].DashCooldownTicks = 999;
            copy.Scores[0] = 999;
            Assert.That(session.TryGetConfirmedState(2, out DashArenaState again), Is.True);
            Assert.That(again.Checksum(), Is.EqualTo(checksum));
            Assert.That(session.State.Checksum(), Is.EqualTo(checksum));
        }

        [Test]
        public void TerminalPollingDoesNotTickResampleOrRepeatChecksumValidation()
        {
            DashRollbackSession session = Finished();
            int tick = session.State.Tick, checksum = session.State.Checksum();
            session.QueueRemoteChecksum(tick - 1, checksum);
            session.Reconcile();
            Assert.That(session.ValidatedChecksumCount, Is.EqualTo(1));
            var before = new byte[8];
            int count = session.CopyLocalHistory(tick + 1, before);
            for (int i = 0; i < 20; i++)
            {
                session.Reconcile();
                Assert.That(session.Advance(Input(Vector2.right, true)), Is.Empty);
            }
            Assert.That(session.State.Tick, Is.EqualTo(tick));
            Assert.That(session.State.Checksum(), Is.EqualTo(checksum));
            Assert.That(session.ValidatedChecksumCount, Is.EqualTo(1));
            var after = new byte[8];
            Assert.That(session.CopyLocalHistory(tick + 1, after), Is.EqualTo(count));
            CollectionAssert.AreEqual(before, after);
            Assert.That(session.CopyLocalHistory(tick + 2, after), Is.Zero);
        }

        [Test]
        public void TerminalReconcileValidatesFinalChecksumWithoutExecutingAnotherTick()
        {
            DashRollbackSession session = Finished();
            int tick = session.State.Tick;
            session.QueueRemoteChecksum(tick - 1, session.ChecksumAt(tick - 1) ^ 1);
            session.Reconcile();
            Assert.That(session.State.Tick, Is.EqualTo(tick));
            Assert.That(session.Status, Is.EqualTo(RollbackStatus.Desync));
            Assert.That(session.DesyncTick, Is.EqualTo(tick - 1));
        }

        [Test]
        public void SyntheticEarlierFinishInvalidatesAllSupersededTerminalHistory()
        {
            DashRollbackSession session = Finished(6, true);
            CorrectedBaseline(session).PhaseTicks = 1;
            Correct(session);
            Assert.That(session.State.Tick, Is.EqualTo(3));
            Assert.That(session.State.Phase, Is.EqualTo(MatchState.MatchOver));
            Assert.That(session.ConfirmedStateTick, Is.EqualTo(2));
            for (int tick = 3; tick < 6; tick++)
            {
                Assert.That(session.ChecksumAt(tick), Is.Zero);
                Assert.That(session.TryGetConfirmedState(tick, out _), Is.False);
                Assert.That(session.TryGetConfirmedEvents(tick, out _), Is.False);
            }
            Assert.That(session.TryGetConfirmedChecksum(5, out int terminal, out int checksum), Is.True);
            Assert.That(terminal, Is.EqualTo(2));
            Assert.That(checksum, Is.EqualTo(session.State.Checksum()));
            session.Reconcile();
            Assert.That(session.State.Tick, Is.EqualTo(3));
        }

        [Test]
        public void SyntheticCorrectedWinnerReplacesFinalEventAndChecksum()
        {
            DashRollbackSession session = Finished(3, true);
            int old = session.State.Checksum();
            DashArenaState corrected = CorrectedBaseline(session);
            corrected.Scores[0] = 0; corrected.Scores[1] = 2;
            Correct(session);
            Assert.That(session.State.Checksum(), Is.Not.EqualTo(old));
            Assert.That(session.TryGetConfirmedEvents(2, out IReadOnlyList<DashArenaEvent> events), Is.True);
            Assert.That(events.Single(e => e.Kind == DashArenaEventKind.MatchOver).Seat, Is.EqualTo(1));
            Assert.That(session.TryGetConfirmedChecksum(2, out _, out int hash), Is.True);
            Assert.That(hash, Is.EqualTo(session.State.Checksum()));
        }

        [Test]
        public void SyntheticRemovedFinishReturnsLiveWithoutSamplingUntilNextAdvance()
        {
            DashRollbackSession session = Finished(6, true);
            DashArenaState corrected = CorrectedBaseline(session);
            corrected.Scores[0] = corrected.Scores[1] = 0;
            corrected.Phase = MatchState.Live; corrected.PhaseTicks = 0; corrected.GateClosed = false;
            var before = new byte[8]; int count = session.CopyLocalHistory(7, before);
            Correct(session);
            Assert.That(session.State.Phase, Is.EqualTo(MatchState.Live));
            Assert.That(session.State.Tick, Is.EqualTo(6));
            Assert.That(session.TryGetConfirmedEvents(5, out IReadOnlyList<DashArenaEvent> events), Is.True);
            Assert.That(events.Any(e => e.Kind == DashArenaEventKind.MatchOver), Is.False);
            var after = new byte[8];
            Assert.That(session.CopyLocalHistory(7, after), Is.EqualTo(count));
            CollectionAssert.AreEqual(before, after);
            Assert.That(session.CopyLocalHistory(8, after), Is.Zero);
            session.Advance(Input(Vector2.right, true));
            Assert.That(session.State.Tick, Is.EqualTo(7));
            Assert.That(session.CopyLocalHistory(8, after), Is.GreaterThan(0));
            Assert.That(after[0], Is.EqualTo(Input(Vector2.right, true).Pack()));
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
            DashArenaState state = DashArenaSimulation.Create((builder ?? Empty()).Freeze(), 0x12345678, 3);
            DashArenaSimulation.StartMatch(state);
            state.Phase = MatchState.Live; state.PhaseTicks = 0; state.GateClosed = false;
            return state;
        }

        static DashArenaInput Input(Vector2 move, bool dash = false) => new DashArenaInput { Move = move, DashPressed = dash };
        static DashArenaInput Tape(int frame, int seat)
        {
            Vector2[] directions = { Vector2.up, Vector2.right, Vector2.down, Vector2.left };
            return Input(directions[(frame / 15 + seat * 2) % 4], frame % 97 == 0);
        }

        static List<DashArenaEvent> CompareConfirmedEvents(DashRollbackSession first, DashRollbackSession second, int start, int end)
        {
            var all = new List<DashArenaEvent>();
            for (int tick = start; tick <= end; tick++)
            {
                Assert.That(first.TryGetConfirmedEvents(tick, out IReadOnlyList<DashArenaEvent> expected), Is.True, "baseline " + tick);
                Assert.That(second.TryGetConfirmedEvents(tick, out IReadOnlyList<DashArenaEvent> actual), Is.True, "corrected " + tick);
                CollectionAssert.AreEqual(expected, actual, "tick " + tick);
                all.AddRange(actual);
            }
            return all;
        }

        static DashRollbackSession Finished(int freeze = 3, bool omitTwo = false)
        {
            var builder = Empty(); builder.PointsToWin = 1;
            DashArenaState initial = Live(builder);
            initial.Phase = MatchState.Scored; initial.PhaseTicks = freeze; initial.Scores[0] = 2;
            var session = new DashRollbackSession(initial, 0);
            for (int tick = 0; tick < freeze; tick++)
            {
                if (!omitTwo || tick != 0) session.ReceiveRemote(tick + 2, default);
                session.Advance(default);
            }
            Assert.That(session.State.Phase, Is.EqualTo(MatchState.MatchOver));
            return session;
        }

        // The real scored freeze is longer than twelve ticks. Supplying a synthetic corrected
        // retained baseline isolates terminal-history truncation/replacement, not a natural goal.
        static DashArenaState CorrectedBaseline(DashRollbackSession session)
        {
            var field = typeof(DashRollbackSession).GetField("_after", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var snapshots = (DashArenaState[])field.GetValue(session);
            snapshots[1] = snapshots[1].Clone();
            Assert.That(snapshots[1].Tick, Is.EqualTo(2));
            return snapshots[1];
        }

        static void Correct(DashRollbackSession session)
        {
            Assert.That(session.ConfirmedTick, Is.EqualTo(1));
            Assert.That(session.TryReceiveRemote(2, Input(Vector2.up, true)), Is.True);
            session.Reconcile();
            Assert.That(session.Status, Is.EqualTo(RollbackStatus.Ok));
            Assert.That(session.LastRollbackTick, Is.EqualTo(2));
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
                var first = (Array)property.GetValue(expected); var second = (Array)property.GetValue(actual);
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
