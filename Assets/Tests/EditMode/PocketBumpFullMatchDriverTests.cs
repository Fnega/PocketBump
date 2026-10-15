using System.Collections.Generic;
using NUnit.Framework;

namespace PocketBump.Tests
{
    public sealed class PocketBumpFullMatchDriverTests
    {
        struct Delivery
        {
            public int At;
            public int Tick;
            public PlayerInput Input;
        }

        [TestCase(0)]
        [TestCase(4)]
        [TestCase(8)]
        public void OrdinaryPackedInputsCompleteARealMatchWithConfirmedMilestones(int latencyTicks)
        {
            var states = new[] { MatchSim.Create(new MatchSettings()), MatchSim.Create(new MatchSettings()) };
            MatchSim.StartMatch(states[0]);
            MatchSim.StartMatch(states[1]);
            var sessions = new[] { new RollbackSession(states[0], 0), new RollbackSession(states[1], 1) };
            var drivers = new[] { new LanFullMatchDriver(), new LanFullMatchDriver() };
            var ledgers = new[] { new LanConfirmedGameplay(), new LanConfirmedGameplay() };
            var pending = new[] { new List<Delivery>(), new List<Delivery>() };

            for (int wallTick = 0; wallTick < 9000; wallTick++)
            {
                for (int seat = 0; seat < 2; seat++)
                {
                    sessions[seat].Reconcile();
                    if (states[seat].Phase == MatchState.MatchOver) continue;
                    int before = states[seat].Checksum();
                    PlayerInput input = drivers[seat].Sample(seat, states[seat].Tick, states[seat]);
                    Assert.That(states[seat].Checksum(), Is.EqualTo(before), "Driver may only produce input.");
                    Assert.That(input.EqualsInput(PlayerInput.Unpack(input.Pack())), Is.True);
                    pending[1 - seat].Add(new Delivery
                    {
                        At = wallTick + latencyTicks,
                        Tick = states[seat].Tick + RollbackSession.DelayTicks,
                        Input = input
                    });
                    sessions[seat].QueueLocal(states[seat].Tick + RollbackSession.DelayTicks, input);
                }
                for (int seat = 0; seat < 2; seat++)
                {
                    for (int i = pending[seat].Count - 1; i >= 0; i--)
                    {
                        Delivery delivery = pending[seat][i];
                        if (delivery.At > wallTick) continue;
                        sessions[seat].ReceiveRemote(delivery.Tick, delivery.Input);
                        pending[seat].RemoveAt(i);
                    }
                    if (states[seat].Phase == MatchState.MatchOver) sessions[seat].Reconcile();
                    else
                    {
                        var history = new byte[NetplayProtocol.InputHistoryLength];
                        sessions[seat].CopyLocalHistory(states[seat].Tick + RollbackSession.DelayTicks, history);
                        sessions[seat].Advance(PlayerInput.Unpack(history[0]));
                    }
                    Assert.That(sessions[seat].Status, Is.EqualTo(RollbackStatus.Ok), "Unexpected rollback failure at wall tick " + wallTick);
                    for (int tick = ledgers[seat].LastTick + 1; tick <= sessions[seat].ConfirmedStateTick; tick++)
                    {
                        Assert.That(sessions[seat].TryGetConfirmedEvents(tick, out SimEvents events), Is.True);
                        Assert.That(ledgers[seat].Commit(tick, events), Is.True);
                        drivers[seat].ObserveConfirmed(events);
                    }
                }
                if (states[0].Phase == MatchState.MatchOver && states[1].Phase == MatchState.MatchOver &&
                    sessions[0].ConfirmedStateTick == states[0].Tick - 1 && sessions[1].ConfirmedStateTick == states[1].Tick - 1)
                    break;
            }

            for (int seat = 0; seat < 2; seat++)
            {
                Assert.That(states[seat].Phase, Is.EqualTo(MatchState.MatchOver), "Normal-input controller failed to finish.");
                Assert.That(ledgers[seat].HasGameplayMilestones, Is.True,
                    "Required actual collection, release, dangerous dynamic impact, score and winner events.");
                Assert.That(states[seat].ScoreP1, Is.EqualTo(8));
                Assert.That(states[seat].ScoreP2, Is.Zero);
                Assert.That(MatchDirector.HasWinner(states[seat].ScoreP1, states[seat].ScoreP2, 8, out int winner), Is.True);
                Assert.That(winner, Is.Zero);
            }
            Assert.That(states[0].Checksum(), Is.EqualTo(states[1].Checksum()));
            Assert.That(ledgers[0].Collections, Is.EqualTo(ledgers[1].Collections));
            Assert.That(ledgers[0].Shots, Is.EqualTo(ledgers[1].Shots));
            Assert.That(ledgers[0].Collisions, Is.EqualTo(ledgers[1].Collisions));
            Assert.That(ledgers[0].Scores, Is.EqualTo(ledgers[1].Scores));
        }

        [Test]
        public void ConfirmedEventLedgerRejectsReplayDuplicatesAndDoesNotCountShootIntent()
        {
            var ledger = new LanConfirmedGameplay();
            Assert.That(ledger.Commit(4, default), Is.True);
            Assert.That(ledger.Shots, Is.Zero);
            var release = new SimEvents { Collected0 = true, Fired0 = true, DangerousImpact = true, Scored = true };
            Assert.That(ledger.Commit(5, release), Is.True);
            Assert.That(ledger.Commit(5, release), Is.False);
            Assert.That(ledger.Commit(3, release), Is.False);
            Assert.That(ledger.Shots, Is.EqualTo(1));
            Assert.That(ledger.Collisions, Is.EqualTo(1));
            Assert.That(ledger.HasGameplayMilestones, Is.False, "A legal winner is still required.");
            ledger.Commit(6, new SimEvents { MatchOver = true, Winner = 0 });
            Assert.That(ledger.HasGameplayMilestones, Is.True);
        }

        [Test]
        public void EarlyNegativeReportRetainsActualSeededSourceEventsAndShortHold()
        {
            var diagnostics = new LanDiagnosticSnapshot
            {
                MatchId = 27, Epoch = 0, FinishSource = "seeded_fixture",
                FinishTick = 60, FinishChecksum = -2020856943,
                FinalScore0 = 8, FinalScore1 = 0, Winner = 0, SettingsHash = 3087,
                ResultHoldSeconds = 6f, ValidatedChecksums = 61
            };
            diagnostics.RetainEpoch(new LanEpochDiagnosticSummary { MatchId = 27, Epoch = 0 });
            var gameplay = new LanConfirmedGameplay();
            gameplay.Commit(0, new SimEvents { Scored = true, Scorer = 0 });
            gameplay.Commit(60, new SimEvents { MatchOver = true, Winner = 0 });

            diagnostics.RetainCurrentConfirmedEpoch(gameplay);
            diagnostics.RetainCurrentConfirmedEpoch(gameplay);
            string report = diagnostics.FormatReport(true, "Expected silent-peer timeout.");

            Assert.That(report, Does.Contain("completed_epoch_count=1"));
            Assert.That(report, Does.Contain("completed_0_finish_source=seeded_fixture"));
            Assert.That(report, Does.Contain("completed_0_finish_tick=60"));
            Assert.That(report, Does.Contain("completed_0_checksum=-2020856943"));
            Assert.That(report, Does.Contain("completed_0_score_0=8"));
            Assert.That(report, Does.Contain("completed_0_score_1=0"));
            Assert.That(report, Does.Contain("completed_0_score_events=1"));
            Assert.That(report, Does.Contain("completed_0_shots=0"));
            Assert.That(report, Does.Contain("completed_0_dynamic_collisions=0"));
            Assert.That(report, Does.Contain("completed_0_confirmed_ticks=61"));
            Assert.That(report, Does.Contain("completed_0_validated_checksums=61"));
            Assert.That(report, Does.Contain("completed_0_result_hold_seconds=6.000"));
            Assert.That(gameplay.HasGameplayMilestones, Is.False);
        }

        [Test]
        public void EpochEvidenceIsBoundedAndDeduplicatedAcrossMatchCounterResets()
        {
            var diagnostics = new LanDiagnosticSnapshot();
            for (int epoch = 0; epoch < 12; epoch++)
            {
                var summary = new LanEpochDiagnosticSummary { MatchId = 10, Epoch = epoch, Shots = epoch + 1 };
                diagnostics.RetainEpoch(summary);
                diagnostics.RetainEpoch(summary);
            }
            diagnostics.SimulationTick = 0;
            diagnostics.ValidatedChecksums = 0;
            diagnostics.RetainEpoch(new LanEpochDiagnosticSummary { MatchId = 10, Epoch = 11, Shots = 99 });
            string report = diagnostics.FormatReport(true, "done");
            Assert.That(report, Does.Contain("completed_epoch_count=8"));
            Assert.That(report, Does.Contain("completed_0_epoch=4"));
            Assert.That(report, Does.Contain("completed_7_epoch=11"));
            Assert.That(report, Does.Contain("completed_7_shots=99"));
        }
    }
}
