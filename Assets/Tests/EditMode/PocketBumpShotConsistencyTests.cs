using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class PocketBumpShotConsistencyTests
    {
        const float Epsilon = 0.0002f;
        static readonly float[] Powers = { 0f, 0.5f, 0.75f, 1f };

        [TestCase(0)]
        [TestCase(1)]
        public void ThreePressShotsPreserveIncreasingPowerSpeedPossessionAndRecoil(int seat)
        {
            Vector2 direction = seat == 0 ? Vector2.right : Vector2.left;
            float previousSpeed = -1f;
            foreach (float power in Powers)
            {
                MatchSimState state = PreparedShot(seat, Vector2.zero, direction);
                SimEvents fired = FireAtPower(state, seat, power);
                float launchSpeed = Mathf.Lerp(8f, 22f, power);
                float expectedSpeed = launchSpeed / (1f + 2f * MatchSim.TickDt);
                float speed = state.Marbles[0].Velocity.magnitude;

                Assert.That(PlayerChargeMath.ShotSpeed(power), Is.EqualTo(launchSpeed).Within(Epsilon));
                Assert.That(speed, Is.EqualTo(expectedSpeed).Within(Epsilon), "seat=" + seat + ", power=" + power);
                Assert.That(speed, Is.GreaterThan(previousSpeed), "More power must survive the complete fire tick.");
                Assert.That(Vector2.Dot(state.Marbles[0].Velocity.normalized, direction), Is.EqualTo(1f).Within(Epsilon));
                Assert.That(seat == 0 ? fired.Fired0 : fired.Fired1, Is.True);
                Assert.That(seat == 0 ? fired.Fired1 : fired.Fired0, Is.False);
                Assert.That(state.Players[seat].Carried, Is.EqualTo(-1));
                Assert.That(state.Marbles[0].Carrier, Is.EqualTo(-1));
                Assert.That(state.Players[seat].Charge, Is.EqualTo(ChargePhase.None));
                Assert.That(state.Players[seat].CollectLock,
                    Is.EqualTo(ArenaLayout.CollectLockSeconds - MatchSim.TickDt).Within(Epsilon));

                float impulse = Mathf.Lerp(12f, 32f, power);
                Assert.That(PlayerChargeMath.RecoilImpulse(direction, power).magnitude,
                    Is.EqualTo(impulse).Within(Epsilon));
                // Fire applies impulse/mass, then the ordinary player drag runs in the same tick.
                Vector2 expectedRecoil = -direction * impulse / ArenaLayout.PlayerMass /
                    (1f + ArenaLayout.PlayerLinearDrag * MatchSim.TickDt);
                Assert.That(Vector2.Distance(state.Players[seat].Velocity, expectedRecoil), Is.LessThan(Epsilon));
                previousSpeed = speed;
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        public void FullPowerFlightAppliesFirstTickDragAndThenCoastsToRest(int seat)
        {
            Vector2 direction = seat == 0 ? Vector2.right : Vector2.left;
            MatchSimState state = PreparedShot(seat, Vector2.zero, direction);
            FireAtPower(state, seat, 1f);
            Assert.That(state.Marbles[0].Velocity.magnitude,
                Is.EqualTo(22f / (1f + 2f / 60f)).Within(Epsilon));
            float previous = state.Marbles[0].Velocity.magnitude;
            for (int tick = 0; tick < 360; tick++)
            {
                MatchSim.Step(state, PlayerInput.Neutral, PlayerInput.Neutral);
                MarbleSim marble = state.Marbles[0];
                Assert.That(marble.Carrier, Is.EqualTo(-1));
                Assert.That(state.Phase, Is.EqualTo(MatchState.Live));
                Assert.That(marble.Velocity.magnitude, Is.LessThanOrEqualTo(previous + Epsilon));
                Assert.That(Mathf.Abs(marble.Position.x), Is.LessThan(ArenaLayout.HalfWidth - ArenaLayout.MarbleRadius));
                if (tick < 5)
                    Assert.That(marble.Velocity.magnitude,
                        Is.EqualTo(previous / (1f + ArenaLayout.MarbleLinearDrag * MatchSim.TickDt)).Within(Epsilon));
                previous = marble.Velocity.magnitude;
            }
            Assert.That(state.Marbles[0].Velocity, Is.EqualTo(Vector2.zero));
        }

        [TestCase(18f)]
        [TestCase(22f)]
        [TestCase(30f)]
        public void SharedFreeMarbleStageRetainsHighSpeedAndCapsOnlyAtShotMaximum(float incomingSpeed)
        {
            MatchSimState state = Live();
            // This free velocity can be supplied by a preceding collision, not just by Fire.
            MatchSim.DebugSetMarble(state, 0, Vector2.zero, Vector2.right * incomingSpeed);
            MatchSim.Step(state, PlayerInput.Neutral, PlayerInput.Neutral);
            float expected = Mathf.Min(incomingSpeed / (1f + 2f / 60f), 22f);
            Assert.That(ArenaLayout.MarbleMaxSpeed, Is.EqualTo(ArenaLayout.ShotMaxSpeed));
            Assert.That(ArenaLayout.MarbleMaxSpeed, Is.EqualTo(22f));
            Assert.That(state.Marbles[0].Velocity.x, Is.EqualTo(expected).Within(Epsilon));
            Assert.That(state.Marbles[0].Velocity.x, Is.GreaterThan(16f));
            Assert.That(state.Marbles[0].Velocity.y, Is.EqualTo(0f).Within(Epsilon));
        }

        [TestCase(0, -0.12f)]
        [TestCase(0, 0f)]
        [TestCase(0, 0.12f)]
        [TestCase(1, -0.12f)]
        [TestCase(1, 0f)]
        [TestCase(1, 0.12f)]
        public void FullPowerShotBouncesFromSolidWallWithoutPassingThrough(int seat, float offset)
        {
            MatchSimState state = PreparedShot(seat, new Vector2(12f, 8f + offset), Vector2.right);
            FireAtPower(state, seat, 1f);
            AssertHighPower(state);
            AssertHorizontalBounce(state, ArenaLayout.HalfWidth - ArenaLayout.MarbleRadius);
        }

        [TestCase(0, -0.12f)]
        [TestCase(0, 0f)]
        [TestCase(0, 0.12f)]
        [TestCase(1, -0.12f)]
        [TestCase(1, 0f)]
        [TestCase(1, 0.12f)]
        public void FullPowerShotIsBlockedFromPlayerOnlyPocket(int seat, float offset)
        {
            MatchSimState state = PreparedShot(seat, new Vector2(12f, offset), Vector2.right);
            FireAtPower(state, seat, 1f);
            AssertHighPower(state);
            float blockFace = ArenaLayout.HalfWidth - ArenaLayout.Wall * 0.5f - ArenaLayout.MarbleRadius;
            AssertHorizontalBounce(state, blockFace);
        }

        [TestCase(0, -0.08f)]
        [TestCase(0, 0f)]
        [TestCase(0, 0.08f)]
        [TestCase(1, -0.08f)]
        [TestCase(1, 0f)]
        [TestCase(1, 0.08f)]
        public void FullPowerShotBesideGoalOpeningHitsPocketEdge(int seat, float offset)
        {
            float x = ArenaLayout.MarblePocketWidth * 0.5f + ArenaLayout.MarbleRadius + 0.05f + offset;
            MatchSimState state = PreparedShot(seat, new Vector2(x, 20f), Vector2.up);
            FireAtPower(state, seat, 1f);
            AssertHighPower(state);
            bool bounced = false;
            for (int tick = 0; tick < 20; tick++)
            {
                Assert.That(state.Marbles[0].Position.y,
                    Is.LessThanOrEqualTo(ArenaLayout.HalfHeight - ArenaLayout.MarbleRadius + Epsilon));
                Assert.That(state.Phase, Is.EqualTo(MatchState.Live));
                if (state.Marbles[0].Velocity.y < 0f)
                {
                    bounced = true;
                    break;
                }
                MatchSim.Step(state, PlayerInput.Neutral, PlayerInput.Neutral);
            }
            Assert.That(bounced, Is.True, "The shot must hit the wall beside the goal, not cross its edge.");
            Assert.That(state.ScoreP1 + state.ScoreP2, Is.EqualTo(0));
        }

        [TestCase(0, -0.12f)]
        [TestCase(0, 0f)]
        [TestCase(0, 0.12f)]
        [TestCase(1, -0.12f)]
        [TestCase(1, 0f)]
        [TestCase(1, 0.12f)]
        public void EmptyBasketCollectsFullPowerShotAcrossInteriorOffsets(int seat, float offset)
        {
            MatchSimState state = PreparedShot(seat, new Vector2(0f, offset), Vector2.right);
            int receiver = 1 - seat;
            MatchSim.DebugPlacePlayer(state, receiver, new Vector2(5f, 0f), Vector2.left);
            FireAtPower(state, seat, 1f);
            AssertHighPower(state);
            bool collected = false;
            for (int tick = 0; tick < 16; tick++)
            {
                SimEvents events = MatchSim.Step(state, PlayerInput.Neutral, PlayerInput.Neutral);
                if (state.Marbles[0].Carrier != receiver) continue;
                Assert.That(receiver == 0 ? events.Collected0 : events.Collected1, Is.True);
                collected = true;
                break;
            }
            Assert.That(collected, Is.True, "A shot entering an eligible empty well must be caught.");
            Assert.That(state.Players[receiver].Carried, Is.EqualTo(0));
            Assert.That(state.Players[seat].Carried, Is.EqualTo(-1));
            Assert.That(state.Marbles[0].Velocity, Is.EqualTo(Vector2.zero));
            Assert.That(state.Marbles[0].DangerousImpactCount, Is.EqualTo(0));
        }

        [TestCase(0, -0.82f)]
        [TestCase(0, -0.8f)]
        [TestCase(0, -0.78f)]
        [TestCase(0, 0.78f)]
        [TestCase(0, 0.8f)]
        [TestCase(0, 0.82f)]
        [TestCase(1, -0.82f)]
        [TestCase(1, -0.8f)]
        [TestCase(1, -0.78f)]
        [TestCase(1, 0.78f)]
        [TestCase(1, 0.8f)]
        [TestCase(1, 0.82f)]
        public void FullPowerShotTouchesRimOutsideCollectionWell(int seat, float offset)
        {
            MatchSimState state = PreparedShot(seat, new Vector2(0f, offset), Vector2.right);
            int receiver = 1 - seat;
            MatchSim.DebugPlacePlayer(state, receiver, new Vector2(5f, 0f), Vector2.left);
            FireAtPower(state, seat, 1f);
            AssertHighPower(state);
            bool rimResponse = false;
            for (int tick = 0; tick < 12; tick++)
            {
                MatchSim.Step(state, PlayerInput.Neutral, PlayerInput.Neutral);
                MarbleSim marble = state.Marbles[0];
                Assert.That(marble.Carrier, Is.EqualTo(-1), "Outside-well rim contact is not collection.");
                Assert.That(marble.DangerousImpactCount, Is.EqualTo(0), "Observe the rim before any body contact.");
                if (Mathf.Abs(marble.Position.y - offset) > Epsilon || marble.Velocity.x < 0f)
                {
                    rimResponse = true;
                    break;
                }
                Assert.That(marble.Position.x, Is.LessThan(4.25f), "The marble passed the basket without a rim response.");
            }
            Assert.That(rimResponse, Is.True);
        }

        static MatchSimState Live()
        {
            MatchSimState state = MatchSim.Create(new MatchSettings());
            MatchSim.StartMatch(state);
            for (int tick = 0; tick < MatchSim.KickoffTicks; tick++)
                MatchSim.Step(state, PlayerInput.Neutral, PlayerInput.Neutral);
            Assert.That(state.Phase, Is.EqualTo(MatchState.Live));
            return state;
        }

        static MatchSimState PreparedShot(int seat, Vector2 position, Vector2 direction)
        {
            MatchSimState state = Live();
            MatchSim.DebugPlacePlayer(state, 1 - seat, new Vector2(-10f, -12f), Vector2.down);
            // The first press advances direction time by one tick. Pre-rotate the resting facing
            // so the second press locks the requested axis rather than silently ignoring that tick.
            Vector2 facing = PlayerChargeMath.LockedShotDirection(direction,
                -PlayerChargeMath.DirectionOffsetDegrees(MatchSim.TickDt));
            // Keep the basket origin on the requested shot line despite that small facing offset.
            Vector2 playerPosition = position + (direction - facing) * ArenaLayout.BasketOffset;
            MatchSim.DebugPlacePlayer(state, seat, playerPosition, facing);
            MatchSim.DebugSetMarble(state, 0, playerPosition + facing * ArenaLayout.BasketOffset, Vector2.zero);
            Assert.That(MatchSim.DebugCollect(state, seat, 0), Is.True);
            return state;
        }

        static SimEvents FireAtPower(MatchSimState state, int seat, float power)
        {
            StepSeat(state, seat, true);
            Assert.That(state.Players[seat].Charge, Is.EqualTo(ChargePhase.Direction));
            Assert.That(state.Players[seat].Carried, Is.EqualTo(0));
            StepSeat(state, seat, true);
            Assert.That(state.Players[seat].Charge, Is.EqualTo(ChargePhase.Power));
            Assert.That(state.Players[seat].ChargeTime, Is.EqualTo(MatchSim.TickDt).Within(Epsilon));

            // Power readings at 45, 30, 5, and 15 ticks are 0, .5, .75, and 1 respectively.
            // The second press has already advanced power time one tick; Fire reads that time
            // before the next time increment. All waiting and the third press use the real step.
            int powerTicks = power == 0f ? 45 : power == 0.5f ? 30 : power == 0.75f ? 5 : 15;
            Vector2 heldPosition = state.Players[seat].Position;
            for (int tick = 1; tick < powerTicks; tick++)
            {
                StepSeat(state, seat, false);
                Assert.That(state.Players[seat].Position, Is.EqualTo(heldPosition));
                Assert.That(state.Players[seat].Carried, Is.EqualTo(0));
            }
            Assert.That(PlayerChargeMath.Power01(state.Players[seat].ChargeTime), Is.EqualTo(power).Within(Epsilon));
            return StepSeat(state, seat, true);
        }

        static SimEvents StepSeat(MatchSimState state, int seat, bool shoot)
        {
            var input = new PlayerInput { ShootPressed = shoot };
            return seat == 0
                ? MatchSim.Step(state, input, PlayerInput.Neutral)
                : MatchSim.Step(state, PlayerInput.Neutral, input);
        }

        static void AssertHighPower(MatchSimState state)
        {
            Assert.That(state.Marbles[0].Velocity.magnitude, Is.GreaterThan(16f));
        }

        static void AssertHorizontalBounce(MatchSimState state, float furthestCenter)
        {
            bool bounced = false;
            for (int tick = 0; tick < 20; tick++)
            {
                Assert.That(state.Marbles[0].Position.x, Is.LessThanOrEqualTo(furthestCenter + Epsilon));
                Assert.That(state.Phase, Is.EqualTo(MatchState.Live));
                if (state.Marbles[0].Velocity.x < 0f)
                {
                    bounced = true;
                    break;
                }
                MatchSim.Step(state, PlayerInput.Neutral, PlayerInput.Neutral);
            }
            Assert.That(bounced, Is.True, "The shot must bounce before crossing the blocking face.");
            Assert.That(state.ScoreP1 + state.ScoreP2, Is.EqualTo(0));
        }
    }
}
