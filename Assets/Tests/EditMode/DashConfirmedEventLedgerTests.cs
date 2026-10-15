using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class DashConfirmedEventLedgerTests
    {
        // Explicit mechanism fixture, created before the rollback baseline; not natural play.
        static DashRollbackSession Fixture(int epoch = 0)
        {
            var state = DashArenaSimulation.Create(ArenaRulesProfile.CreateProductionV1(), 19, epoch);
            DashArenaSimulation.StartMatch(state); state.Phase = MatchState.Live; state.PhaseTicks = 0;
            state.GateClosed = false; state.Players[1].Position = new Vector2(0, -2);
            state.Players[1].Facing = Vector2.up;
            state.Pickups[0] = new DashPickupState { Active = true, Id = 1, Kind = DashPickupKind.Green, Position = Vector2.zero };
            state.NextPickupId = 2;
            return new DashRollbackSession(state, 0);
        }

        [Test]
        public void DelayedPickupIsCommittedExactlyOnceOnlyAfterCorrectedConfirmation()
        {
            var session = Fixture(); var ledger = new DashConfirmedEventLedger();
            for (int i = 0; i < 8; i++) session.Advance(DashArenaInput.Neutral);
            Assert.That(ledger.TryCommit(session, out var before), Is.True);
            Assert.That(before, Is.Empty); Assert.That(ledger.NextTick, Is.EqualTo(2));
            for (int tick = 2; tick < 8; tick++)
                Assert.That(session.TryReceiveRemote(tick, new DashArenaInput { Move = Vector2.up, DashPressed = tick == 2 }), Is.True);
            Assert.That(ledger.TryCommit(session, out var unsettled), Is.True);
            Assert.That(unsettled, Is.Empty, "Receiving inputs alone cannot publish unreconciled events.");
            session.Reconcile();
            Assert.That(ledger.TryCommit(session, out var committed), Is.True);
            Assert.That(Count(committed, DashArenaEventKind.Pickup), Is.EqualTo(1));
            Assert.That(Count(committed, DashArenaEventKind.DashStarted), Is.EqualTo(1));
            Assert.That(session.State.Players[1].GreenTicks, Is.GreaterThan(0));
            Assert.That(ledger.CommittedEventCount, Is.EqualTo(committed.Count));
            session.Reconcile();
            Assert.That(ledger.TryCommit(session, out var duplicate), Is.True); Assert.That(duplicate, Is.Empty);
        }

        [Test]
        public void MissingHistoryIsExplicitWithoutPartialCommit()
        {
            var session = Fixture(); var ledger = new DashConfirmedEventLedger();
            for (int tick = 0; tick < 40; tick++)
            {
                session.TryReceiveRemote(tick + 2, DashArenaInput.Neutral);
                session.Advance(DashArenaInput.Neutral);
            }
            Assert.That(ledger.TryCommit(session, out var events), Is.False);
            Assert.That(events, Is.Empty); Assert.That(ledger.MissingTick, Is.Zero);
            Assert.That(ledger.NextTick, Is.Zero); Assert.That(ledger.CommittedEventCount, Is.Zero);
        }

        [Test]
        public void NewEpochRequiresExplicitPresentationResetAndCannotReuseOldCursor()
        {
            var old = Fixture(); var ledger = new DashConfirmedEventLedger();
            old.Advance(DashArenaInput.Neutral); Assert.That(ledger.TryCommit(old, out _), Is.True);
            var next = Fixture(1);
            Assert.Throws<InvalidOperationException>(() => ledger.TryCommit(next, out _));
            ledger.ResetForEpoch(1);
            Assert.That(ledger.NextTick, Is.Zero); Assert.That(ledger.CommittedEventCount, Is.Zero);
            next.Advance(DashArenaInput.Neutral);
            Assert.That(ledger.TryCommit(next, out _), Is.True); Assert.That(ledger.NextTick, Is.EqualTo(1));
        }

        [Test]
        public void FailedSessionCannotCommitNewNotifications()
        {
            var session = Fixture(); var ledger = new DashConfirmedEventLedger();
            for (int tick = 0; tick < 16; tick++) session.Advance(DashArenaInput.Neutral);
            Assert.That(session.Status, Is.EqualTo(RollbackStatus.InputHole));
            Assert.That(ledger.TryCommit(session, out var events), Is.False);
            Assert.That(events, Is.Empty); Assert.That(ledger.CommittedEventCount, Is.Zero);
        }

        static int Count(IReadOnlyList<DashArenaEvent> events, DashArenaEventKind kind)
        { int count = 0; foreach (var item in events) if (item.Kind == kind) count++; return count; }
    }
}
