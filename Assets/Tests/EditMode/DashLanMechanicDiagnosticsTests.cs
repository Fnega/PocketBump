using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class DashLanMechanicDiagnosticsTests
    {
        static IEnumerable<TestCaseData> MechanicCases()
        {
            foreach (string name in DashLanMechanicScenario.Names)
                for (int seat = 0; seat < 2; seat++) yield return new TestCaseData(name, seat);
        }

        [TestCaseSource(nameof(MechanicCases))]
        public void LabeledMechanicFixtureUsesOnlyDelayedInputsAfterBaselineAndConfirmedOutcomes(string name, int seat)
        {
            var mechanism = new DashLanMechanicScenario(name, seat);
            DashArenaState state = mechanism.CreateInitial(mechanism.Rules, DashLanMechanicScenario.FixtureSeed, 0);
            int baseline = state.Checksum();
            var inputs0 = new Queue<DashArenaInput>(new[] { DashArenaInput.Neutral, DashArenaInput.Neutral });
            var inputs1 = new Queue<DashArenaInput>(new[] { DashArenaInput.Neutral, DashArenaInput.Neutral });
            while (state.Tick < 1200 && !mechanism.Passed)
            {
                int before = state.Checksum();
                inputs0.Enqueue(mechanism.Sample(0, state.Tick, state));
                inputs1.Enqueue(mechanism.Sample(1, state.Tick, state));
                Assert.That(state.Checksum(), Is.EqualTo(before), "Driver must not alter live authority.");
                DashArenaInput input0 = inputs0.Dequeue(), input1 = inputs1.Dequeue();
                var events = DashArenaSimulation.Step(state, input0, input1);
                var frame = new DashConfirmedFrame(state.Clone(), events, input0, input1);
                mechanism.Commit(frame); mechanism.Commit(frame); // Stable cursor ignores duplicate frames.
            }
            Assert.That(mechanism.InitialChecksum, Is.EqualTo(baseline));
            Assert.That(mechanism.Passed, Is.True,
                "Missing=" + mechanism.MissingMilestones + " tick=" + state.Tick + " player=" + state.Players[seat].Position +
                " ball=" + state.Balls[0].Position + " wall0=" + state.Walls[0].Broken + "/" + state.Walls[0].RepairPending +
                " wall1=" + state.Walls[1].Broken + "/" + state.Walls[1].RepairPending);
            Assert.That(state.Scores, Is.EqualTo(new[] { 0, 0 }), "Mechanic fixture is not natural-goal or full-match evidence.");
            if (name == "green-impact")
            {
                Assert.That(mechanism.EnhancedBallSpeed, Is.GreaterThan(mechanism.UnbuffedCounterfactualSpeed + 1.9f));
                Assert.That(mechanism.ComparisonTick, Is.GreaterThan(0));
                Assert.That(seat == 0 ? mechanism.ComparisonInput0 : mechanism.ComparisonInput1, Is.Not.Zero);
            }
            if (name == "blue-no-safe")
                Assert.That(mechanism.MissingMilestones, Is.Empty); // Distinct outcome, never blue_safe_relocation.
        }

        [TestCase(0)]
        [TestCase(1)]
        public void GreenEvidenceRemainsOneConfirmedImpactWitnessAfterLaterContactsAndThreeHundredTicks(int seat)
        {
            var mechanism = new DashLanMechanicScenario("green-impact", seat);
            DashArenaState state = mechanism.CreateInitial(mechanism.Rules, DashLanMechanicScenario.FixtureSeed, 0);
            var inputs0 = new Queue<DashArenaInput>(new[] { DashArenaInput.Neutral, DashArenaInput.Neutral });
            var inputs1 = new Queue<DashArenaInput>(new[] { DashArenaInput.Neutral, DashArenaInput.Neutral });
            int firstTick = -1, buffedImpactTicks = 0;
            float firstEnhanced = 0f, firstUnbuffed = 0f;
            byte firstInput0 = 0, firstInput1 = 0;
            // The real runner continues past the first milestone to confirmed tick 300.
            // Stopping when Passed first becomes true misses later contacts overwriting
            // the comparison fields while the deduplicated milestone keeps its old tick.
            for (int step = 0; step < 420; step++)
            {
                DashArenaState previous = state.Clone();
                inputs0.Enqueue(mechanism.Sample(0, state.Tick, state));
                inputs1.Enqueue(mechanism.Sample(1, state.Tick, state));
                DashArenaInput input0 = inputs0.Dequeue(), input1 = inputs1.Dequeue();
                var events = DashArenaSimulation.Step(state, input0, input1);
                int tick = state.Tick - 1, authorityHash = state.Checksum();
                bool actualBuffedImpact = previous.Players[seat].GreenTicks > 0 && state.Players[seat].GreenTicks > 0 &&
                    events.Any(item => item.Kind == DashArenaEventKind.Impact && item.Seat == seat &&
                        item.EntityId == 2 && item.Tick == tick && item.Epoch == state.Epoch);
                if (actualBuffedImpact) buffedImpactTicks++;
                var frame = new DashConfirmedFrame(state.Clone(), events, input0, input1);
                mechanism.Commit(frame); mechanism.Commit(frame);
                Assert.That(state.Checksum(), Is.EqualTo(authorityHash), "Diagnostic comparison cannot alter live authority.");
                Assert.That(frame.State.Checksum(), Is.EqualTo(authorityHash), "The confirmed observation is readonly too.");
                if (mechanism.ComparisonTick < 0) continue;
                if (firstTick < 0)
                {
                    Assert.That(actualBuffedImpact, Is.True, "Evidence needs an actual same-tick confirmed ball impact.");
                    previous.Players[seat].GreenTicks = 0;
                    DashArenaSimulation.Step(previous, input0, input1);
                    firstTick = tick; firstEnhanced = state.Balls[0].Velocity.magnitude;
                    firstUnbuffed = previous.Balls[0].Velocity.magnitude;
                    firstInput0 = input0.Pack(); firstInput1 = input1.Pack();
                }
                DashMechanicMilestone milestone = mechanism.Milestones.Single(item => item.Name == "green_enhanced_impact");
                Assert.That(milestone.Tick, Is.EqualTo(firstTick));
                Assert.That(milestone.Seat, Is.EqualTo(seat)); Assert.That(milestone.Entity, Is.EqualTo(2));
                Assert.That(mechanism.ComparisonTick, Is.EqualTo(milestone.Tick), "Later impacts cannot mix different confirmed ticks.");
                Assert.That(mechanism.ComparisonInput0, Is.EqualTo(firstInput0));
                Assert.That(mechanism.ComparisonInput1, Is.EqualTo(firstInput1));
                Assert.That(mechanism.EnhancedBallSpeed, Is.EqualTo(firstEnhanced));
                Assert.That(mechanism.UnbuffedCounterfactualSpeed, Is.EqualTo(firstUnbuffed));
            }
            Assert.That(state.Tick, Is.EqualTo(420));
            Assert.That(buffedImpactTicks, Is.GreaterThan(1), "Exercise real subsequent green contacts, not just duplicate callbacks.");
            Assert.That(firstTick, Is.GreaterThan(0));
            Assert.That(seat == 0 ? firstInput0 : firstInput1, Is.Not.Zero);
            Assert.That(mechanism.Passed, Is.True);
            Assert.That(state.Scores, Is.EqualTo(new[] { 0, 0 }));
        }

        [Test]
        public void ConfirmedFrameClonesAndRealStoredInputsAreIsolatedFromBrokenSubscribers()
        {
            float now = 0f;
            ArenaRulesProfile rules = ArenaRulesProfile.CreateProductionV1();
            using (var host = DashLanSession.Host(rules, 1873, 31u, () => now))
            using (var join = DashLanSession.Join(rules, new IPEndPoint(IPAddress.Loopback, host.LocalEndpoint.Port), () => now, 73u))
            {
                int frames = 0; int lastTick = -1;
                host.ConfirmedFrame += frame => { frame.State.Scores[0] = 999; throw new InvalidOperationException("deliberate observer failure"); };
                host.ConfirmedFrame += frame =>
                {
                    Assert.That(frame.State.Scores[0], Is.Zero, "Other subscribers receive a separate snapshot clone.");
                    int tick = frame.State.Tick - 1;
                    Assert.That(tick, Is.EqualTo(lastTick + 1)); lastTick = tick;
                    if (tick >= DashRollbackSession.DelayTicks)
                    {
                        Assert.That(frame.Input0.Pack(), Is.EqualTo(1));
                        Assert.That(frame.Input1.Pack(), Is.EqualTo(2));
                    }
                    else Assert.That(frame.Input0.Pack() | frame.Input1.Pack(), Is.Zero);
                    frames++;
                };
                for (int iteration = 0; iteration < 3000 && frames < 50; iteration++)
                {
                    host.Pump(); join.Pump();
                    if (host.Ready && join.Ready)
                    {
                        if (host.State.Tick <= join.State.Tick) host.Advance((epoch, tick) => new DashArenaInput { Move = Vector2.right });
                        if (join.State.Tick <= host.State.Tick) join.Advance((epoch, tick) => new DashArenaInput { Move = Vector2.left });
                    }
                    now += .005f; Thread.Sleep(1);
                }
                Assert.That(frames, Is.GreaterThanOrEqualTo(50));
                Assert.That(host.PresentationErrorCount, Is.EqualTo(frames), "Observer failures are isolated and explicitly counted.");
                Assert.That(host.Frozen || join.Frozen, Is.False);
                Assert.That(host.State.Scores[0], Is.Zero, "Observer edits never write back into authority.");
                Assert.That(host.Rollback.TryGetConfirmedInputs(host.State.Tick + 1, out _, out _), Is.False,
                    "Future/predicted inputs are not confirmed evidence.");
            }
        }

        [Test]
        public void FixtureFactoryIsAppliedBeforeBothRollbackBaselinesAndNormalPathRemainsUnmodified()
        {
            float now = 0f;
            var hostMechanic = new DashLanMechanicScenario("blue-no-safe", 1);
            var joinMechanic = new DashLanMechanicScenario("blue-no-safe", 1);
            using (var host = DashLanSession.Host(hostMechanic.Rules, 2884, DashLanMechanicScenario.FixtureSeed,
                () => now, initialStateFactory: hostMechanic.CreateInitial))
            using (var join = DashLanSession.Join(joinMechanic.Rules, new IPEndPoint(IPAddress.Loopback, host.LocalEndpoint.Port),
                () => now, 84u, initialStateFactory: joinMechanic.CreateInitial))
            {
                for (int iteration = 0; iteration < 2000 && !(host.Ready && join.Ready); iteration++)
                { host.Pump(); join.Pump(); now += .005f; Thread.Sleep(1); }
                Assert.That(host.Ready && join.Ready, Is.True);
                Assert.That(host.State.Tick, Is.Zero); Assert.That(join.State.Tick, Is.Zero);
                Assert.That(host.State.Checksum(), Is.EqualTo(join.State.Checksum()));
                Assert.That(host.State.Checksum(), Is.EqualTo(hostMechanic.InitialChecksum));
                Assert.That(host.State.Phase, Is.EqualTo(MatchState.Live));
                Assert.That(host.Rules.ContentHash, Is.EqualTo(join.Rules.ContentHash));
                Assert.That(host.Rules.ContentHash, Is.Not.EqualTo(ArenaRulesProfile.CreateProductionV1().ContentHash),
                    "The bounded no-safe candidate profile has an explicit different negotiated identity.");
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void InvalidInitialFactoryFreezesOnceWithExplicitReasonInsteadOfRepeatingExceptions(int variant)
        {
            float now = 0f; int calls = 0;
            ArenaRulesProfile rules = ArenaRulesProfile.CreateProductionV1();
            Func<ArenaRulesProfile, uint, int, DashArenaState> factory = (profile, seed, epoch) =>
            {
                calls++;
                if (variant == 0) throw new InvalidOperationException("declared invalid fixture");
                if (variant == 1) return null;
                DashArenaState state = DashArenaSimulation.Create(profile, seed, epoch);
                DashArenaSimulation.StartMatch(state); state.Tick = 1;
                return state;
            };
            using (var host = DashLanSession.Host(rules, 3884 + variant, 81u, () => now, initialStateFactory: factory))
            using (var join = DashLanSession.Join(rules, new IPEndPoint(IPAddress.Loopback, host.LocalEndpoint.Port), () => now, 85u))
            {
                for (int iteration = 0; iteration < 2000 && !host.Frozen; iteration++)
                { Assert.DoesNotThrow(() => host.Pump()); join.Pump(); now += .005f; Thread.Sleep(1); }
                Assert.That(host.FailureReason, Is.EqualTo("INVALID INITIAL STATE"));
                Assert.That(host.InitializationError, Is.Not.Empty);
                Assert.That(host.InitializationCount, Is.Zero); Assert.That(host.Rollback, Is.Null);
                for (int i = 0; i < 10; i++) Assert.DoesNotThrow(() => host.Pump());
                Assert.That(calls, Is.EqualTo(1)); Assert.That(host.SocketBound, Is.False);
            }
        }

        [Test]
        public void LaterScoreChangeInvalidatesAlreadyCollectedMechanicMilestones()
        {
            var mechanism = new DashLanMechanicScenario("blue-teleport", 0);
            DashArenaState state = mechanism.CreateInitial(mechanism.Rules, DashLanMechanicScenario.FixtureSeed, 0);
            var events = DashArenaSimulation.Step(state, default, default);
            mechanism.Commit(new DashConfirmedFrame(state.Clone(), events, default, default));
            Assert.That(mechanism.Passed, Is.True);
            // Deliberately malformed observation fixture to exercise evidence rejection,
            // not a live runtime path and not accepted as natural scoring.
            state.Scores[0] = 1;
            events = DashArenaSimulation.Step(state, default, default);
            mechanism.Commit(new DashConfirmedFrame(state.Clone(), events, default, default));
            Assert.That(mechanism.Passed, Is.False);
            Assert.That(mechanism.InvalidReason, Is.EqualTo("mechanic_score_changed"));
        }
    }
}
