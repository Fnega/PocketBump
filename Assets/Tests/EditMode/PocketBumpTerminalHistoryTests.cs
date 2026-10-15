using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class PocketBumpTerminalHistoryTests
    {
        static RollbackSession FinishedSession(int freezeTicks = 3, bool omitRemoteTickTwo = false)
        {
            var state = MatchSim.Create(new MatchSettings { pointsToWin = 1, marbleCount = 1 });
            state.Phase = MatchState.Scored;
            state.PhaseTicks = freezeTicks;
            state.ScoreP1 = 2;
            var session = new RollbackSession(state, 0);
            for (int tick = 0; tick < freezeTicks; tick++)
            {
                if (!omitRemoteTickTwo || tick != 0)
                    session.ReceiveRemote(tick + RollbackSession.DelayTicks, PlayerInput.Neutral);
                session.Advance(PlayerInput.Neutral);
            }
            Assert.That(state.Phase, Is.EqualTo(MatchState.MatchOver));
            return session;
        }

        [Test]
        public void StoppedAdvanceCannotResampleAnAlreadyAssignedTick()
        {
            var session = FinishedSession();
            int tick = session.State.Tick;
            var sample = new PlayerInput { Move = Vector2.up, ShootPressed = true };
            session.QueueLocal(tick + RollbackSession.DelayTicks, sample);
            session.Advance(PlayerInput.Neutral);
            var history = new byte[NetplayProtocol.InputHistoryLength];
            Assert.That(session.CopyLocalHistory(tick + RollbackSession.DelayTicks, history), Is.GreaterThan(0));
            Assert.That(history[0], Is.EqualTo(sample.Pack()), "A stopped game must not overwrite queued input.");
            Assert.That(session.State.Tick, Is.EqualTo(tick));
        }

        [Test]
        public void TerminalReconciliationChecksQueuedFinalChecksumWithoutAdvancing()
        {
            var session = FinishedSession();
            int tick = session.State.Tick;
            session.QueueRemoteChecksum(tick - 1, session.ChecksumAt(tick - 1) ^ 1);
            MethodInfo reconcile = typeof(RollbackSession).GetMethod("Reconcile", new System.Type[0]);
            Assert.That(reconcile, Is.Not.Null, "Terminal history needs reconciliation independently of a new tick.");
            reconcile.Invoke(session, null);
            Assert.That(session.State.Tick, Is.EqualTo(tick));
            Assert.That(session.Status, Is.EqualTo(RollbackStatus.Desync));
            Assert.That(session.DesyncTick, Is.EqualTo(tick - 1));
        }

        [Test]
        public void RepeatedTerminalPollingDoesNotCreateTicksSamplesOrDuplicateChecksumValidation()
        {
            var session = FinishedSession();
            int tick = session.State.Tick;
            int checksum = session.State.Checksum();
            var before = new byte[NetplayProtocol.InputHistoryLength];
            int newest = tick - 1 + RollbackSession.DelayTicks;
            int count = session.CopyLocalHistory(newest, before);
            session.QueueRemoteChecksum(tick - 1, checksum);
            session.Reconcile();
            int validated = session.ValidatedChecksumCount;
            Assert.That(validated, Is.EqualTo(1));
            for (int poll = 0; poll < 40; poll++) session.Reconcile();
            var after = new byte[NetplayProtocol.InputHistoryLength];
            Assert.That(session.CopyLocalHistory(newest, after), Is.EqualTo(count));
            Assert.That(after, Is.EqualTo(before));
            Assert.That(session.State.Tick, Is.EqualTo(tick));
            Assert.That(session.State.Checksum(), Is.EqualTo(checksum));
            Assert.That(session.ValidatedChecksumCount, Is.EqualTo(validated));
            Assert.That(session.LastRollbackTick, Is.EqualTo(-1));
            Assert.That(session.TryGetConfirmedChecksum(tick, out int finalTick, out int finalChecksum), Is.True);
            Assert.That(finalTick, Is.EqualTo(tick - 1));
            Assert.That(finalChecksum, Is.EqualTo(checksum));
        }

        [Test]
        public void SyntheticEarlierTerminalCorrectionInvalidatesEverySupersededSnapshotAndEvent()
        {
            var session = FinishedSession(6, true);
            int predictedNextTick = session.State.Tick;
            Assert.That(session.ChecksumAt(5), Is.Not.Zero);
            MatchSimState corrected = CorrectedReplayBaseline(session);
            corrected.PhaseTicks = 1;
            DeliverCorrection(session);

            Assert.That(session.Status, Is.EqualTo(RollbackStatus.Ok));
            Assert.That(session.LastRollbackTick, Is.EqualTo(2));
            Assert.That(session.State.Tick, Is.EqualTo(3));
            Assert.That(session.State.Phase, Is.EqualTo(MatchState.MatchOver));
            Assert.That(session.ConfirmedStateTick, Is.EqualTo(2));
            for (int tick = session.State.Tick; tick < predictedNextTick; tick++)
            {
                Assert.That(session.ChecksumAt(tick), Is.Zero, "Superseded snapshot at " + tick);
                Assert.That(session.TryGetConfirmedState(tick, out _), Is.False);
                Assert.That(session.TryGetConfirmedEvents(tick, out _), Is.False);
            }
            Assert.That(session.TryGetConfirmedChecksum(predictedNextTick - 1, out int terminal, out int hash), Is.True);
            Assert.That(terminal, Is.EqualTo(2));
            Assert.That(hash, Is.EqualTo(session.State.Checksum()));
            Assert.That(session.TryGetConfirmedEvents(terminal, out SimEvents events), Is.True);
            Assert.That(events.MatchOver, Is.True);
            session.Reconcile();
            Assert.That(session.State.Tick, Is.EqualTo(3), "Idempotent terminal polling cannot stamp no-op ticks.");
        }

        [Test]
        public void SyntheticWinnerCorrectionReplacesProvisionalWinnerAndFinalChecksum()
        {
            var session = FinishedSession(3, true);
            int predictedChecksum = session.State.Checksum();
            Assert.That(MatchDirector.HasWinner(session.State.ScoreP1, session.State.ScoreP2,
                session.State.PointsToWin, out int predictedWinner), Is.True);
            Assert.That(predictedWinner, Is.Zero);
            MatchSimState corrected = CorrectedReplayBaseline(session);
            corrected.ScoreP1 = 0;
            corrected.ScoreP2 = 2;
            DeliverCorrection(session);

            Assert.That(session.Status, Is.EqualTo(RollbackStatus.Ok));
            Assert.That(session.State.Tick, Is.EqualTo(3));
            Assert.That(session.State.Checksum(), Is.Not.EqualTo(predictedChecksum));
            Assert.That(session.TryGetConfirmedEvents(2, out SimEvents terminal), Is.True);
            Assert.That(terminal.MatchOver, Is.True);
            Assert.That(terminal.Winner, Is.EqualTo(1));
            Assert.That(session.TryGetConfirmedChecksum(2, out int tick, out int hash), Is.True);
            Assert.That(tick, Is.EqualTo(2));
            Assert.That(hash, Is.EqualTo(session.State.Checksum()));
        }

        [Test]
        public void SyntheticRemovedFinishReturnsToLiveWithoutSamplingUntilNextAdvance()
        {
            var session = FinishedSession(6, true);
            int oldNextTick = session.State.Tick;
            MatchSimState corrected = CorrectedReplayBaseline(session);
            corrected.ScoreP1 = corrected.ScoreP2 = 0;
            corrected.Phase = MatchState.Live;
            corrected.PhaseTicks = 0;
            corrected.GateClosed = false;
            int newest = oldNextTick - 1 + RollbackSession.DelayTicks;
            var samples = new byte[NetplayProtocol.InputHistoryLength];
            int count = session.CopyLocalHistory(newest, samples);
            DeliverCorrection(session);

            Assert.That(session.Status, Is.EqualTo(RollbackStatus.Ok));
            Assert.That(session.State.Phase, Is.EqualTo(MatchState.Live));
            Assert.That(session.State.Tick, Is.EqualTo(oldNextTick));
            Assert.That(session.TryGetConfirmedEvents(oldNextTick - 1, out SimEvents events), Is.True);
            Assert.That(events.MatchOver, Is.False);
            var after = new byte[NetplayProtocol.InputHistoryLength];
            Assert.That(session.CopyLocalHistory(newest, after), Is.EqualTo(count));
            Assert.That(after, Is.EqualTo(samples));
            Assert.That(session.CopyLocalHistory(newest + 1, after), Is.Zero);
            session.Advance(new PlayerInput { Move = Vector2.right });
            Assert.That(session.State.Tick, Is.EqualTo(oldNextTick + 1));
            Assert.That(session.CopyLocalHistory(newest + 1, after), Is.GreaterThan(0));
            Assert.That(after[0], Is.EqualTo(new PlayerInput { Move = Vector2.right }.Pack()));
        }

        // Production scoring freezes longer than the rollback window, so this deliberately
        // synthetic fixture supplies a corrected retained baseline before tick 2. It verifies
        // restore/replay/history invalidation only; it is not evidence of a natural match win.
        // The late real input still enters through the public receive/reconciliation path.
        static MatchSimState CorrectedReplayBaseline(RollbackSession session)
        {
            FieldInfo field = typeof(RollbackSession).GetField("_after", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var snapshots = (MatchSimState[])field.GetValue(session);
            snapshots[1] = snapshots[1].Clone();
            Assert.That(snapshots[1].Tick, Is.EqualTo(2));
            return snapshots[1];
        }

        static void DeliverCorrection(RollbackSession session)
        {
            Assert.That(session.ConfirmedTick, Is.EqualTo(1));
            Assert.That(session.TryReceiveRemote(2, new PlayerInput { Move = Vector2.up }), Is.True);
            session.Reconcile();
        }
    }
}
