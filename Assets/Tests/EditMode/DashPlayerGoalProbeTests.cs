using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class DashPlayerGoalProbeTests
    {
        [Test]
        public void OrdinaryGoalRouteActuallyArrivesAndRemainsNearBothGoals()
        {
            DashArenaState state = DashArenaSimulation.Create(ArenaRulesProfile.CreateProductionV1(), 777u);
            DashArenaSimulation.StartMatch(state);
            bool arrived = false;
            for (int tick = 0; tick < 900; tick++)
            {
                int before = state.Checksum();
                DashArenaInput first = DashPlayerPresentationProbe.GoalApproachInput(0, state);
                DashArenaInput second = DashPlayerPresentationProbe.GoalApproachInput(1, state);
                Assert.That(state.Checksum(), Is.EqualTo(before), "Driver must only produce ordinary inputs.");
                DashArenaSimulation.Step(state, first, second);
                arrived |= DashPlayerPresentationProbe.GoalApproachesReached(state);
                if (tick >= 600) Assert.That(DashPlayerPresentationProbe.GoalApproachesReached(state), Is.True, "Both robots must stay at actual near-goal positions.");
            }
            Assert.That(arrived, Is.True);
            Assert.That(state.Phase, Is.EqualTo(MatchState.Live));
            Assert.That(state.Scores, Is.EqualTo(new[] { 0, 0 }));
        }

        [Test]
        public void GoalRouteUsesOrdinaryInputsAndNeverMutatesSampledState()
        {
            var state = new DashArenaState(ArenaRulesProfile.CreateProductionV1(), 777u);
            state.Players[0] = new DashPlayerState { Alive = true, Position = new Vector2(0f, 18f), Facing = Vector2.down };
            state.Players[1] = new DashPlayerState { Alive = true, Position = new Vector2(0f, -18f), Facing = Vector2.up };
            int before = state.Checksum();
            DashArenaInput north = DashPlayerPresentationProbe.GoalApproachInput(0, state);
            DashArenaInput south = DashPlayerPresentationProbe.GoalApproachInput(1, state);
            Assert.That(north.Move, Is.EqualTo(Vector2.up));
            Assert.That(south.Move, Is.EqualTo(Vector2.down));
            Assert.That(north.DashPressed || south.DashPressed, Is.False);
            Assert.That(DashPlayerPresentationProbe.GoalApproachesReached(state), Is.False);
            Assert.That(state.Checksum(), Is.EqualTo(before));
        }

        [Test]
        public void ArrivalNeedsBothLivingPlayersAndNeutralizesAtTarget()
        {
            var state = new DashArenaState(ArenaRulesProfile.CreateProductionV1(), 777u);
            for (int seat = 0; seat < 2; seat++)
                state.Players[seat] = new DashPlayerState { Alive = true, Position = new Vector2(0f, (seat == 0 ? 1f : -1f) * (state.Rules.HalfHeight - 4f)) };
            int before = state.Checksum();
            Assert.That(DashPlayerPresentationProbe.GoalApproachesReached(state), Is.True);
            Assert.That(DashPlayerPresentationProbe.GoalApproachInput(0, state).Pack(), Is.Zero);
            Assert.That(DashPlayerPresentationProbe.GoalApproachInput(1, state).Pack(), Is.Zero);
            Assert.That(state.Checksum(), Is.EqualTo(before));
            state.Players[1].Alive = false;
            Assert.That(DashPlayerPresentationProbe.GoalApproachesReached(state), Is.False);
            state.Players[1].Alive = true;
            state.Players[1].Position = Vector2.zero;
            Assert.That(DashPlayerPresentationProbe.GoalApproachesReached(state), Is.False);
        }
    }
}
