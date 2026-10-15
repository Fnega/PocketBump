using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class PocketBumpPhysicsReplayTests
    {
        const int RunTicks = 180;

        sealed class Delivery
        {
            public int Due;
            public int Tick;
            public PlayerInput Input;
        }

        [TestCase(0, 0)]
        [TestCase(0, 1)]
        [TestCase(1, 0)]
        [TestCase(1, 1)]
        public void HighPowerShotAndBodyImpactConvergeAfterEitherSeatsLateInput(int localSeat, int shooter)
        {
            MatchSimState initial = PreparedShot(shooter);
            MatchSimState baseline = initial.Clone();
            var replayed = new RollbackSession(initial.Clone(), localSeat);
            var pending = new List<Delivery>();
            var baselineHashes = new List<int>();
            int shots = 0, dangerousContacts = 0, rollbacks = 0;
            int comparedHistories = 0;
            float fireSpeed = 0f;

            for (int frame = 0; frame < RunTicks; frame++)
            {
                pending.Add(new Delivery
                {
                    Due = frame + 5 + frame % 3,
                    Tick = frame + RollbackSession.DelayTicks,
                    Input = Tape(1 - localSeat, shooter, frame)
                });
                // The independently varying delay also reorders some adjacent inputs.
                DeliverDue(pending, replayed, frame);

                SimEvents events = MatchSim.Step(baseline,
                    Tape(0, shooter, frame - RollbackSession.DelayTicks),
                    Tape(1, shooter, frame - RollbackSession.DelayTicks));
                baselineHashes.Add(baseline.Checksum());
                if (events.Fired0 || events.Fired1)
                {
                    Assert.That(shooter == 0 ? events.Fired0 : events.Fired1, Is.True);
                    shots++;
                    fireSpeed = baseline.Marbles[0].Velocity.magnitude;
                    Assert.That(fireSpeed, Is.EqualTo(ArenaLayout.ShotMaxSpeed /
                        (1f + ArenaLayout.MarbleLinearDrag * MatchSim.TickDt)).Within(0.0001f),
                        "The input tape must actually fire at full power through the ordinary drag stage.");
                    Assert.That(fireSpeed, Is.GreaterThan(16f), "Do not replay only a clipped/low-power shot.");
                    Assert.That(baseline.Players[shooter].Carried, Is.EqualTo(-1));
                    Assert.That(baseline.Marbles[0].Carrier, Is.EqualTo(-1));
                }
                if (events.DangerousImpact)
                {
                    dangerousContacts++;
                    Assert.That(baseline.Players[1 - shooter].Velocity.x, Is.GreaterThan(0f),
                        "The target body must receive the shot's rightward impact.");
                    Assert.That(baseline.Marbles[0].Velocity.x, Is.LessThan(0f),
                        "The corrected unequal-mass circle contact must rebound the lighter marble.");
                }

                replayed.Advance(Tape(localSeat, shooter, frame));
                Assert.That(replayed.Status, Is.EqualTo(RollbackStatus.Ok), "frame=" + frame);
                Assert.That(replayed.State.Tick, Is.EqualTo(baseline.Tick));
                if (replayed.LastRollbackTick >= 0) rollbacks++;
                if (replayed.TryGetConfirmedChecksum(frame, out int confirmedTick, out int confirmedHash))
                {
                    Assert.That(confirmedHash, Is.EqualTo(baselineHashes[confirmedTick]),
                        "Corrected collision/shot history differs at tick " + confirmedTick);
                    comparedHistories++;
                }
            }

            // Stop advancing while the final in-flight inputs arrive, then perform the last
            // correction. Both snapshots being compared are now wholly confirmed, not predictions.
            DeliverDue(pending, replayed, RunTicks + 7);
            Assert.That(pending, Is.Empty);
            MatchSim.Step(baseline, PlayerInput.Neutral, PlayerInput.Neutral);
            replayed.Advance(PlayerInput.Neutral);
            Assert.That(replayed.Status, Is.EqualTo(RollbackStatus.Ok));
            Assert.That(replayed.State.Tick, Is.EqualTo(baseline.Tick));
            Assert.That(replayed.ConfirmedTick, Is.GreaterThanOrEqualTo(baseline.Tick - 1));
            Assert.That(replayed.TryGetConfirmedChecksum(baseline.Tick - 1, out int finalTick, out int finalHash), Is.True);
            Assert.That(finalTick, Is.EqualTo(baseline.Tick - 1));
            Assert.That(finalHash, Is.EqualTo(baseline.Checksum()));
            Assert.That(replayed.State.Checksum(), Is.EqualTo(baseline.Checksum()));
            Assert.That(JsonUtility.ToJson(replayed.State), Is.EqualTo(JsonUtility.ToJson(baseline)),
                "Compare un-hashed locks, charge direction and impact cooldowns as well as the wire checksum.");
            Assert.That(shots, Is.EqualTo(1), "All three charge presses must lead to an actual shot.");
            Assert.That(dangerousContacts, Is.GreaterThan(0), "The tape must contain an actual body collision.");
            Assert.That(rollbacks, Is.GreaterThan(0), "Either local-seat assignment must exercise correction.");
            Assert.That(comparedHistories, Is.GreaterThan(100));
            TestContext.WriteLine("local=" + localSeat + ", shooter=" + shooter + ", fire-speed=" + fireSpeed +
                ", dangerous-contacts=" + dangerousContacts + ", rollbacks=" + rollbacks + ", confirmed=" + finalTick);
        }

        static MatchSimState PreparedShot(int shooter)
        {
            var state = MatchSim.Create(new MatchSettings { pointsToWin = 21, marbleCount = 1 });
            MatchSim.StartMatch(state);
            for (int i = 0; i < MatchSim.KickoffTicks; i++)
                MatchSim.Step(state, PlayerInput.Neutral, PlayerInput.Neutral);
            Assert.That(state.Phase, Is.EqualTo(MatchState.Live));
            MatchSim.DebugPlacePlayer(state, shooter, new Vector2(-4f, 0f), Vector2.right);
            MatchSim.DebugPlacePlayer(state, 1 - shooter, new Vector2(2f, 0f), Vector2.up);
            MatchSim.DebugSetMarble(state, 0,
                state.Players[shooter].Position + Vector2.right * ArenaLayout.BasketOffset, Vector2.zero);
            Assert.That(MatchSim.DebugCollect(state, shooter, 0), Is.True);
            state.Tick = 0;
            return state;
        }

        static PlayerInput Tape(int seat, int shooter, int sampledTick)
        {
            var input = PlayerInput.Neutral;
            if (sampledTick < 0) return input;
            if (seat == shooter)
            {
                // Direction oscillates for half a period to lock straight right; power then
                // advances a quarter period to its maximum. These are inputs, not DebugShoot.
                input.ShootPressed = sampledTick == 0 || sampledTick == 30 || sampledTick == 45;
            }
            else if (sampledTick >= 52 && sampledTick < 57)
            {
                // Small target movement during the incoming shot makes the other local-seat
                // assignment require rollback too, without moving its body out of the shot path.
                input.Move = Vector2.up;
            }
            return PlayerInput.Unpack(input.Pack());
        }

        static void DeliverDue(List<Delivery> pending, RollbackSession session, int frame)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Delivery delivery = pending[i];
                if (delivery.Due > frame) continue;
                Assert.That(session.TryReceiveRemote(delivery.Tick, delivery.Input), Is.True,
                    "Late input must remain inside the supported rollback window.");
                pending.RemoveAt(i);
            }
        }
    }
}
