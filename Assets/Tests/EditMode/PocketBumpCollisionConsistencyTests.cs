using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    // Analytical helper tests distinguish correct physics from two peers agreeing on a bad result.
    // Reflection keeps test-only access out of the runtime API and replicated state.
    public sealed class PocketBumpCollisionConsistencyTests
    {
        const float Epsilon = 0.00002f;
        const float NormalTolerance = 0.0001f;
        static readonly MethodInfo Bounce = typeof(MatchSim).GetMethod(
            "BounceCircles", BindingFlags.Static | BindingFlags.NonPublic);

        struct Pair
        {
            public Vector2 Pa, Va, Pb, Vb;
            public float Ra, Ma, Rb, Mb, Restitution;
        }

        static bool Resolve(ref Pair pair)
        {
            Assert.That(Bounce, Is.Not.Null, "The shared circle solver must remain available.");
            object[] args = { pair.Pa, pair.Va, pair.Ra, pair.Ma, pair.Pb, pair.Vb,
                pair.Rb, pair.Mb, pair.Restitution };
            bool contact = (bool)Bounce.Invoke(null, args);
            pair.Pa = (Vector2)args[0];
            pair.Va = (Vector2)args[1];
            pair.Pb = (Vector2)args[4];
            pair.Vb = (Vector2)args[5];
            return contact;
        }

        static Pair ConfiguredPair(int kind)
        {
            return new Pair
            {
                Ra = kind == 2 ? ArenaLayout.MarbleRadius : ArenaLayout.PlayerRadius,
                Ma = kind == 2 ? ArenaLayout.MarbleMass : ArenaLayout.PlayerMass,
                Rb = kind == 0 ? ArenaLayout.PlayerRadius : ArenaLayout.MarbleRadius,
                Mb = kind == 0 ? ArenaLayout.PlayerMass : ArenaLayout.MarbleMass,
                Restitution = kind == 0 ? ArenaLayout.PlayerPlayerBounciness : ArenaLayout.Bounciness
            };
        }

        static void EqualVector(Vector2 actual, Vector2 expected, string context)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Epsilon), context + " x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Epsilon), context + " y");
        }

        static void AssertFinite(Vector2 value)
        {
            Assert.That(float.IsNaN(value.x) || float.IsInfinity(value.x), Is.False);
            Assert.That(float.IsNaN(value.y) || float.IsInfinity(value.y), Is.False);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ApproachingConfiguredPairsConserveMomentumAndTangentsAndUseRestitution(int kind)
        {
            Pair pair = ConfiguredPair(kind);
            Vector2 normal = new Vector2(0.6f, 0.8f);
            Vector2 tangent = new Vector2(-0.8f, 0.6f);
            pair.Pa = new Vector2(-2f, 1f);
            pair.Pb = pair.Pa + normal * (pair.Ra + pair.Rb - 0.1f);
            pair.Va = normal * 3f + tangent * 2f;
            pair.Vb = normal * -2f + tangent * -1f;
            Pair before = pair;
            float closingSpeed = Vector2.Dot(before.Va - before.Vb, normal);

            Assert.That(Resolve(ref pair), Is.True);

            EqualVector(pair.Va * pair.Ma + pair.Vb * pair.Mb,
                before.Va * pair.Ma + before.Vb * pair.Mb, "momentum");
            Assert.That(Vector2.Dot(pair.Va - pair.Vb, normal),
                Is.EqualTo(-pair.Restitution * closingSpeed).Within(Epsilon));
            Assert.That(Vector2.Dot(pair.Va, tangent), Is.EqualTo(2f).Within(Epsilon));
            Assert.That(Vector2.Dot(pair.Vb, tangent), Is.EqualTo(-1f).Within(Epsilon));
            Assert.That(Vector2.Distance(pair.Pa, pair.Pb),
                Is.EqualTo(pair.Ra + pair.Rb).Within(Epsilon));
            float inverseMassShareA = (1f / pair.Ma) / (1f / pair.Ma + 1f / pair.Mb);
            Assert.That(Vector2.Distance(pair.Pa, before.Pa),
                Is.EqualTo(0.1f * inverseMassShareA).Within(Epsilon));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void SeparatingOverlapOnlyCorrectsPositions(int kind)
        {
            Pair pair = ConfiguredPair(kind);
            pair.Pb = Vector2.right * (pair.Ra + pair.Rb - 0.2f);
            pair.Va = new Vector2(-2f, 1f);
            pair.Vb = new Vector2(3f, -1f);
            Pair before = pair;

            Assert.That(Resolve(ref pair), Is.True, "Overlap is a contact even without an impulse.");

            EqualVector(pair.Va, before.Va, "separating A velocity");
            EqualVector(pair.Vb, before.Vb, "separating B velocity");
            Assert.That(Vector2.Distance(pair.Pa, pair.Pb),
                Is.EqualTo(pair.Ra + pair.Rb).Within(Epsilon));
            EqualVector(pair.Pa * pair.Ma + pair.Pb * pair.Mb,
                before.Pa * pair.Ma + before.Pb * pair.Mb, "mass-weighted position");
        }

        [Test]
        public void TangentialOverlapHasNoVelocityImpulse()
        {
            Pair pair = ConfiguredPair(1);
            pair.Pb = Vector2.right * 0.8f;
            pair.Va = new Vector2(1f, 3f);
            pair.Vb = new Vector2(1f, -2f);
            Pair before = pair;

            Assert.That(Resolve(ref pair), Is.True);

            EqualVector(pair.Va, before.Va, "tangential A velocity");
            EqualVector(pair.Vb, before.Vb, "tangential B velocity");
            Assert.That(Vector2.Distance(pair.Pa, pair.Pb),
                Is.EqualTo(pair.Ra + pair.Rb).Within(Epsilon));
        }

        [Test]
        public void ExactTouchingAppliesClosingImpulseWithoutMovingPositions()
        {
            Pair pair = ConfiguredPair(0);
            // Binary-exact unit radius sum makes this an exact touching case, not near-overlap.
            pair.Ra = pair.Rb = 0.5f;
            pair.Pb = Vector2.right;
            pair.Va = Vector2.right * 2f;
            pair.Vb = Vector2.left * 2f;
            Pair before = pair;

            Assert.That(Resolve(ref pair), Is.True);

            EqualVector(pair.Pa, before.Pa, "touching A position");
            EqualVector(pair.Pb, before.Pb, "touching B position");
            EqualVector(pair.Va, Vector2.left * (2f * pair.Restitution), "touching A velocity");
            EqualVector(pair.Vb, Vector2.right * (2f * pair.Restitution), "touching B velocity");
        }

        [Test]
        public void NoncontactDoesNotMutateEitherBody()
        {
            Pair pair = ConfiguredPair(1);
            pair.Pb = new Vector2(pair.Ra + pair.Rb + 0.01f, 0f);
            pair.Va = new Vector2(3f, 1f);
            pair.Vb = new Vector2(-4f, -2f);
            Pair before = pair;

            Assert.That(Resolve(ref pair), Is.False);

            Assert.That(pair.Pa, Is.EqualTo(before.Pa));
            Assert.That(pair.Pb, Is.EqualTo(before.Pb));
            Assert.That(pair.Va, Is.EqualTo(before.Va));
            Assert.That(pair.Vb, Is.EqualTo(before.Vb));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ExchangingOrdinaryPairPreservesEachPhysicalOutput(int kind)
        {
            Pair pair = ConfiguredPair(kind);
            pair.Pa = new Vector2(-1f, 2f);
            pair.Pb = pair.Pa + new Vector2(0.6f, 0.8f) * (pair.Ra + pair.Rb - 0.1f);
            pair.Va = new Vector2(3f, 2f);
            pair.Vb = new Vector2(-1f, -2f);
            Pair exchanged = new Pair
            {
                Pa = pair.Pb, Va = pair.Vb, Ra = pair.Rb, Ma = pair.Mb,
                Pb = pair.Pa, Vb = pair.Va, Rb = pair.Ra, Mb = pair.Ma,
                Restitution = pair.Restitution
            };

            Assert.That(Resolve(ref pair), Is.True);
            Assert.That(Resolve(ref exchanged), Is.True);

            EqualVector(pair.Pa, exchanged.Pb, "exchanged A position");
            EqualVector(pair.Pb, exchanged.Pa, "exchanged B position");
            EqualVector(pair.Va, exchanged.Vb, "exchanged A velocity");
            EqualVector(pair.Vb, exchanged.Va, "exchanged B velocity");
        }

        [TestCase(0f, 0f)]
        [TestCase(0.5f, 0f)]
        [TestCase(-0.5f, 0f)]
        [TestCase(2f, 0f)]
        [TestCase(-2f, 0f)]
        [TestCase(0f, 0.5f)]
        [TestCase(0f, -0.5f)]
        [TestCase(0f, 2f)]
        [TestCase(0f, -2f)]
        public void DegenerateStationaryContactsRemainBoundedMassWeightedAndRemoveOverlap(float x, float y)
        {
            Pair pair = ConfiguredPair(1);
            pair.Pb = new Vector2(x, y) * NormalTolerance;
            Pair initial = pair;
            float radiusSum = pair.Ra + pair.Rb;

            for (int pass = 0; pass < 4; pass++)
            {
                Pair before = pair;
                Resolve(ref pair);
                AssertFinite(pair.Pa);
                AssertFinite(pair.Pb);
                AssertFinite(pair.Va);
                AssertFinite(pair.Vb);
                Assert.That(Vector2.Distance(pair.Pa, before.Pa), Is.LessThanOrEqualTo(radiusSum + Epsilon));
                Assert.That(Vector2.Distance(pair.Pb, before.Pb), Is.LessThanOrEqualTo(radiusSum + Epsilon));
                EqualVector(pair.Pa * pair.Ma + pair.Pb * pair.Mb,
                    initial.Pa * pair.Ma + initial.Pb * pair.Mb, "degenerate mass-weighted position");
                EqualVector(pair.Va, Vector2.zero, "stationary A velocity");
                EqualVector(pair.Vb, Vector2.zero, "stationary B velocity");
            }

            Assert.That(Vector2.Distance(pair.Pa, pair.Pb), Is.EqualTo(radiusSum).Within(Epsilon));
            if (x == 0f && y == 0f)
            {
                float shareA = pair.Mb / (pair.Ma + pair.Mb);
                EqualVector(pair.Pa, Vector2.left * (radiusSum * shareA), "coincident +X fallback A");
                EqualVector(pair.Pb, Vector2.right * (radiusSum * (1f - shareA)), "coincident +X fallback B");
            }
        }

        [TestCase(3f, -2f)]
        [TestCase(-3f, 2f)]
        public void CoincidentMovingContactsAreFiniteBoundedAndExactlyRepeatable(float ax, float bx)
        {
            Pair initial = ConfiguredPair(1);
            initial.Va = new Vector2(ax, 4f);
            initial.Vb = new Vector2(bx, -1f);
            Pair first = initial;
            Assert.That(Resolve(ref first), Is.True);
            AssertFinite(first.Pa);
            AssertFinite(first.Pb);
            AssertFinite(first.Va);
            AssertFinite(first.Vb);
            Assert.That(first.Va.magnitude, Is.LessThan(20f));
            Assert.That(first.Vb.magnitude, Is.LessThan(20f));
            Assert.That(first.Pa.magnitude, Is.LessThanOrEqualTo(first.Ra + first.Rb + Epsilon));
            Assert.That(first.Pb.magnitude, Is.LessThanOrEqualTo(first.Ra + first.Rb + Epsilon));
            EqualVector(first.Va * first.Ma + first.Vb * first.Mb,
                initial.Va * initial.Ma + initial.Vb * initial.Mb, "coincident momentum");
            float expectedRelative = ax > bx ? -(ax - bx) * first.Restitution : ax - bx;
            Assert.That(first.Va.x - first.Vb.x, Is.EqualTo(expectedRelative).Within(Epsilon));
            Assert.That(first.Va.y, Is.EqualTo(initial.Va.y));
            Assert.That(first.Vb.y, Is.EqualTo(initial.Vb.y));

            for (int repeat = 0; repeat < 5; repeat++)
            {
                Pair again = initial;
                Resolve(ref again);
                Assert.That(again.Pa, Is.EqualTo(first.Pa));
                Assert.That(again.Pb, Is.EqualTo(first.Pb));
                Assert.That(again.Va, Is.EqualTo(first.Va));
                Assert.That(again.Vb, Is.EqualTo(first.Vb));
            }
        }

        [TestCase(0, true)]
        [TestCase(0, false)]
        [TestCase(1, true)]
        [TestCase(1, false)]
        [TestCase(2, true)]
        [TestCase(2, false)]
        public void RealStepRoutesAllCirclePairsAndDoesNotReboundSeparatingContacts(int kind, bool closing)
        {
            Pair pair = ConfiguredPair(kind);
            var state = MatchSim.Create(new MatchSettings { marbleCount = 2 });
            state.Phase = MatchState.Live;
            state.GateClosed = false;
            MatchSim.DebugPlacePlayer(state, 0, new Vector2(-8f, 8f), Vector2.left);
            MatchSim.DebugPlacePlayer(state, 1, new Vector2(8f, 8f), Vector2.right);
            float direction = closing ? 1f : -1f;
            Vector2 initialA = Vector2.right * direction;
            Vector2 initialB = Vector2.left * direction;
            Vector2 positionA = Vector2.zero;
            Vector2 positionB = Vector2.right * (pair.Ra + pair.Rb - 0.05f);
            if (kind == 2)
                MatchSim.DebugSetMarble(state, 0, positionA, initialA);
            else
                SetPlayer(state, 0, positionA, initialA, Vector2.left);
            if (kind == 0)
                SetPlayer(state, 1, positionB, initialB, Vector2.right);
            else
                MatchSim.DebugSetMarble(state, kind == 2 ? 1 : 0, positionB, initialB);

            float dragA = kind == 2 ? ArenaLayout.MarbleLinearDrag : ArenaLayout.PlayerLinearDrag;
            float dragB = kind == 0 ? ArenaLayout.PlayerLinearDrag : ArenaLayout.MarbleLinearDrag;
            Vector2 incomingA = initialA / (1f + dragA * MatchSim.TickDt);
            Vector2 incomingB = initialB / (1f + dragB * MatchSim.TickDt);
            Vector2 expectedA = incomingA;
            Vector2 expectedB = incomingB;
            if (closing)
            {
                // Independent scalar two-body collision formula, not a call to the tested helper.
                expectedA.x = (incomingA.x * (pair.Ma - pair.Restitution * pair.Mb) +
                    incomingB.x * pair.Mb * (1f + pair.Restitution)) / (pair.Ma + pair.Mb);
                expectedB.x = (incomingB.x * (pair.Mb - pair.Restitution * pair.Ma) +
                    incomingA.x * pair.Ma * (1f + pair.Restitution)) / (pair.Ma + pair.Mb);
            }
            Assert.That((incomingA - incomingB).magnitude, Is.LessThan(ArenaLayout.MarbleMildImpactSpeed),
                "Low-speed fixtures isolate base impulses from additional gameplay knockback.");

            SimEvents events = MatchSim.Step(state, PlayerInput.Neutral, PlayerInput.Neutral);

            Vector2 actualA = kind == 2 ? state.Marbles[0].Velocity : state.Players[0].Velocity;
            Vector2 actualB = kind == 0 ? state.Players[1].Velocity : state.Marbles[kind == 2 ? 1 : 0].Velocity;
            Vector2 finalA = kind == 2 ? state.Marbles[0].Position : state.Players[0].Position;
            Vector2 finalB = kind == 0 ? state.Players[1].Position : state.Marbles[kind == 2 ? 1 : 0].Position;
            EqualVector(actualA, expectedA, "real-step A velocity");
            EqualVector(actualB, expectedB, "real-step B velocity");
            Assert.That(Vector2.Distance(finalA, finalB), Is.GreaterThanOrEqualTo(pair.Ra + pair.Rb - Epsilon));
            Assert.That(events.DangerousImpact || events.Collected0 || events.Collected1 || events.Scored, Is.False);
            Assert.That(state.Phase, Is.EqualTo(MatchState.Live));
        }

        static void SetPlayer(MatchSimState state, int seat, Vector2 position, Vector2 velocity, Vector2 facing)
        {
            MatchSim.DebugPlacePlayer(state, seat, position, facing);
            PlayerSim player = state.Players[seat];
            player.Velocity = velocity;
            // Prevent braking from obscuring the analytical collision response; normal drag still runs.
            player.ImpactCoast = 1f;
            state.Players[seat] = player;
        }
    }
}
