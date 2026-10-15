using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class DashLanNaturalDiagnosticsTests
    {
        [TestCase("drop-finish", "both")]
        [TestCase("drop-finish-ack", "both")]
        [TestCase("drop-consent", "both")]
        [TestCase("drop-consent-ack", "both")]
        [TestCase("drop-offer", "host")]
        [TestCase("drop-prepared", "join")]
        [TestCase("drop-commit", "host")]
        [TestCase("drop-commit-ack", "join")]
        [TestCase("drop-start", "host")]
        [TestCase("drop-startup-input", "both")]
        [TestCase("drop-startup-ack", "both")]
        [TestCase("duplicate-reorder", "both")]
        [TestCase("stale-epoch", "both")]
        [TestCase("peer-silence", "both")]
        [TestCase("none", "none")]
        public void FaultEvidenceRequiresActualInjectionOnlyFromThePrescribedProtocolSenders(string profile, string sender)
        {
            Assert.That(DashLanDiagnosticReport.RequiredFaultSender(profile), Is.EqualTo(sender));
            foreach (string role in new[] { "host", "join" })
            {
                bool required = sender == "both" || sender == role;
                Assert.That(DashLanDiagnosticReport.HasRequiredFaultInjection(profile, role, 1), Is.EqualTo(required), role);
                Assert.That(DashLanDiagnosticReport.HasRequiredFaultInjection(profile, role, 0), Is.EqualTo(!required), role);
                Assert.That(DashLanDiagnosticReport.HasRequiredFaultInjection(profile, role, -1), Is.False, role);
            }
        }

        [Test]
        public void FaultEvidenceRejectsUnknownOrCombinedProfilesAndRequiresBothHandshakeSenders()
        {
            Assert.That(DashLanDiagnosticReport.RequiredFaultSender("unknown"), Is.EqualTo("unknown"));
            Assert.That(DashLanDiagnosticReport.HasRequiredFaultInjection("unknown", "host", 1), Is.False);
            Assert.That(DashLanDiagnosticReport.HasRequiredFaultInjection("drop-offer", "invalid-role", 1), Is.False);
            Assert.That(DashLanDiagnosticReport.HasRequiredFaultInjection("drop-offer", "host", 1, true), Is.False);
            foreach (string role in new[] { "host", "join" })
            {
                Assert.That(DashLanDiagnosticReport.HasRequiredFaultInjection("none", role, 0, true), Is.False);
                Assert.That(DashLanDiagnosticReport.HasRequiredFaultInjection("none", role, 1, true), Is.True);
            }
        }

        [TestCase(true, false, true, 311, 321, 311, 311, true)]
        [TestCase(false, false, true, 311, 321, 311, 311, false)]
        [TestCase(true, true, true, 311, 321, 311, 311, false)]
        [TestCase(true, false, false, 311, 321, 311, 311, false)]
        [TestCase(true, false, true, -1, 11, -1, -1, false)]
        [TestCase(true, false, true, 311, 322, 310, 310, false)]
        [TestCase(true, false, true, 311, 321, 311, 312, false)]
        [TestCase(true, false, true, 311, 323, 311, 311, false)]
        [TestCase(true, false, true, 311, 322, 311, 311, false)]
        [TestCase(true, false, true, 311, 324, 311, 311, false)]
        public void PacingEvidenceRequiresActiveStallAndItsExactExhaustedPrefix(bool started, bool ended, bool waiting,
            int lastProduced, int consumerTick, int contiguous, int newest, bool expected)
        {
            // Predicate fixture only: not standalone, transport, or timing acceptance.
            Assert.That(DashLanSession.MaximumInputLeadTicks, Is.EqualTo(10));
            Assert.That(DashRollbackSession.BufferTicks, Is.EqualTo(12));
            Assert.That(DashRollbackSession.DelayTicks, Is.EqualTo(2));
            Assert.That(DashLanDiagnosticReport.IsActiveStallWait(started, ended, waiting,
                lastProduced, consumerTick, contiguous, newest), Is.EqualTo(expected));
        }

        [TestCase(1u)]
        [TestCase(123456u)]
        [TestCase(0x71d38abu)]
        public void OrdinaryProductionMatchFinishesWithBothSeatsActualDashCollisionGoals(uint seed)
        {
            ArenaRulesProfile rules = ArenaRulesProfile.CreateProductionV1(8, 1);
            DashArenaState state = DashArenaSimulation.Create(rules, seed);
            DashArenaSimulation.StartMatch(state);
            Assert.That(state.Tick, Is.Zero); Assert.That(state.Scores, Is.EqualTo(new[] { 0, 0 }));
            var driver = new DashLanFullMatchDriver();
            var ledger = new DashLanConfirmedGameplay(rules, 0);
            // This is ordinary deterministic simulation coverage, not standalone UDP proof.
            // A two-tick input queue uses the same canonical action vocabulary as rollback.
            var tape0 = new Queue<DashArenaInput>(new[] { DashArenaInput.Neutral, DashArenaInput.Neutral });
            var tape1 = new Queue<DashArenaInput>(new[] { DashArenaInput.Neutral, DashArenaInput.Neutral });
            while (state.Tick < 10800 && state.Phase != MatchState.MatchOver)
            {
                int before = state.Checksum();
                DashArenaInput input0 = driver.Sample(0, state.Tick, state);
                DashArenaInput input1 = driver.Sample(1, state.Tick, state);
                Assert.That(state.Checksum(), Is.EqualTo(before), "Sampling cannot mutate authority.");
                Assert.That(input0.Equals(DashArenaInput.Unpack(input0.Pack())), Is.True);
                Assert.That(input1.Equals(DashArenaInput.Unpack(input1.Pack())), Is.True);
                tape0.Enqueue(input0); tape1.Enqueue(input1);
                ledger.Commit(DashArenaSimulation.Step(state, tape0.Dequeue(), tape1.Dequeue()));
            }
            Assert.That(state.Phase, Is.EqualTo(MatchState.MatchOver),
                "Natural driver deadline. tick=" + state.Tick + " scores=" + string.Join(",", state.Scores) +
                " players=" + state.Players[0].Position + "/" + state.Players[1].Position + " ball=" + state.Balls[0].Position);
            Assert.That(state.Scores, Is.EqualTo(new[] { 8, 1 }));
            Assert.That(ledger.HasNaturalMilestones, Is.True,
                "dash=" + string.Join(",", ledger.Dashes) + " goals=" + string.Join(",", ledger.CollisionGoals));
            Assert.That(ledger.GoalCount, Is.EqualTo(9)); Assert.That(ledger.WinnerCount, Is.EqualTo(1));
            Assert.That(ledger.Events.Any(item => item.Kind == DashArenaEventKind.Fell), Is.False,
                "Natural scoring must not rely on player falls.");
        }

        [Test]
        public void ConfirmationLedgerDeduplicatesAndRequiresActualLinkedDashImpactGoal()
        {
            var rules = ArenaRulesProfile.CreateProductionV1();
            var ledger = new DashLanConfirmedGameplay(rules, 4);
            var events = new[]
            {
                Event(10, 4, 0, 0, DashArenaEventKind.DashStarted),
                Event(14, 4, 0, 2, DashArenaEventKind.Impact),
                Event(90, 4, 0, 0, DashArenaEventKind.Goal, new Vector2(0, -36.3f)),
                Event(100, 4, 1, 1, DashArenaEventKind.DashStarted),
                Event(104, 4, 1, 2, DashArenaEventKind.Impact),
                Event(180, 4, 1, 0, DashArenaEventKind.Goal, new Vector2(0, 36.3f)),
                Event(200, 4, 0, -1, DashArenaEventKind.MatchOver)
            };
            ledger.Commit(events); ledger.Commit(events);
            Assert.That(ledger.Dashes, Is.EqualTo(new[] { 1, 1 }));
            Assert.That(ledger.CollisionGoals, Is.EqualTo(new[] { 1, 1 }));
            Assert.That(ledger.Events.Count, Is.EqualTo(events.Length));
            Assert.That(ledger.HasNaturalMilestones, Is.True);
            ledger.Commit(new[] { Event(210, 4, 0, 0, DashArenaEventKind.Goal, new Vector2(0, -36.3f)) });
            Assert.That(ledger.CollisionGoals[0], Is.EqualTo(1), "A previous rally's impact cannot satisfy a later goal.");
        }

        [Test]
        public void RequestedDashWrongPocketAndExpiredDashCannotProveParticipation()
        {
            var ledger = new DashLanConfirmedGameplay(ArenaRulesProfile.CreateProductionV1(), 0);
            ledger.Commit(new[]
            {
                Event(10, 0, 0, 2, DashArenaEventKind.Impact), // No actual DashStarted.
                Event(20, 0, 0, 0, DashArenaEventKind.Goal, new Vector2(0, -36.3f)),
                Event(30, 0, 1, 1, DashArenaEventKind.DashStarted),
                Event(60, 0, 1, 2, DashArenaEventKind.Impact), // Dash no longer active.
                Event(70, 0, 1, 0, DashArenaEventKind.Goal, new Vector2(0, 36.3f)),
                Event(80, 0, 0, 0, DashArenaEventKind.DashStarted),
                Event(84, 0, 0, 2, DashArenaEventKind.Impact),
                Event(90, 0, 0, 0, DashArenaEventKind.Goal, new Vector2(0, 36.3f)), // Wrong defended pocket.
                Event(100, 1, 1, 1, DashArenaEventKind.DashStarted) // Retired/other epoch ignored.
            });
            Assert.That(ledger.CollisionGoals, Is.EqualTo(new[] { 0, 0 }));
            Assert.That(ledger.HasNaturalMilestones, Is.False);
            Assert.That(ledger.Dashes, Is.EqualTo(new[] { 1, 1 }));
        }

        static DashArenaEvent Event(int tick, int epoch, int seat, int entity, DashArenaEventKind kind, Vector2 position = default) =>
            new DashArenaEvent(tick, epoch, seat, entity, kind, position);
    }
}
