using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    // Isolated mechanism fixtures. Reflection invokes the actual contact solver so
    // pre-contact velocity samples can be tested without mixing in drive/drag updates.
    public sealed class HandfeelSimulationTests
    {
        static ArenaRulesProfile candidate, previousCandidate, previousSixCandidate, legacy;
        [OneTimeSetUp] public void Setup()
        {
            candidate = ArenaRulesProfile.CreateHandfeelCandidate();
            legacy = ArenaRulesProfile.CreateProductionV1();
            var previous = legacy.ToBuilder();
            previous.IsProvisional = true; previous.HandfeelPolicyVersion = 1;
            previous.RecoilGraceTicks = 6; previous.RecoilTailMultiplier = 3f;
            previousCandidate = previous.Freeze();
            previous.RecoilTailMultiplier = 6f;
            previousSixCandidate = previous.Freeze();
        }

        static object Invoke(string method, params object[] args) => typeof(DashArenaSimulation)
            .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
        static DashArenaState Live(ArenaRulesProfile rules)
        {
            var state = DashArenaSimulation.Create(rules, 12345);
            state.Phase = MatchState.Live; state.GateClosed = false;
            state.Players[0].Position = new Vector2(0, -18);
            state.Players[1].Position = new Vector2(10, 26);
            return state;
        }
        static DashArenaState BallContact(ArenaRulesProfile rules, int seat, Vector2 control,
            Vector2 external, Vector2 ballVelocity, Vector2 normal, bool green = false, bool dash = false)
        {
            var state = Live(rules);
            state.Players[1 - seat].Position = new Vector2(10, 26);
            state.Players[seat].Position = new Vector2(0, -18);
            state.Players[seat].ControlVelocity = control;
            state.Players[seat].ExternalVelocity = external;
            state.Players[seat].GreenTicks = green ? 300 : 0;
            state.Players[seat].DashTicks = dash ? 18 : 0;
            state.Players[seat].DashDirection = normal;
            state.Balls[0] = new DashBallState { Active = true,
                Position = state.Players[seat].Position + normal * (rules.PlayerRadius + rules.BallRadius), Velocity = ballVelocity };
            return state;
        }
        static void Contact(DashArenaState state, int seat = 0, int ball = 0) =>
            Invoke("ResolveBodies", state, seat, ball + 2, new List<DashArenaEvent>());
        static void Update(DashArenaState state, int seat = 0) =>
            Invoke("UpdatePlayer", state, seat, default(DashArenaInput), new List<DashArenaEvent>());
        static void Step(DashArenaState state) => DashArenaSimulation.Step(state, default, default);
        static DashArenaState PlayerPair(ArenaRulesProfile rules, bool dash0, bool dash1,
            bool green0 = false, bool green1 = false, bool purple0 = false, bool purple1 = false,
            bool mirrored = false, bool latched = false)
        {
            var state = Live(rules);
            Vector2 n = mirrored ? Vector2.left : Vector2.right;
            state.Players[0].Position = new Vector2(0, -18) - n * rules.PlayerRadius;
            state.Players[1].Position = new Vector2(0, -18) + n * rules.PlayerRadius;
            for (int seat = 0; seat < 2; seat++)
            {
                bool dash = seat == 0 ? dash0 : dash1;
                Vector2 drive = seat == 0 ? n : -n;
                state.Players[seat].ControlVelocity = drive * (dash ? rules.DashSpeed : rules.NormalSpeed);
                state.Players[seat].Facing = state.Players[seat].DashDirection = drive;
                state.Players[seat].DashTicks = dash ? rules.DashTicks : 0;
                state.Players[seat].GreenTicks = (seat == 0 ? green0 : green1) ? rules.GreenTicks : 0;
                state.Players[seat].PurpleTicks = (seat == 0 ? purple0 : purple1) ? rules.PurpleTicks : 0;
            }
            state.PlayerContacts[0] = latched;
            return state;
        }
        static List<DashArenaEvent> ResolvePair(DashArenaState state)
        {
            var events = new List<DashArenaEvent>();
            Invoke("ResolveBodies", state, 0, 1, events);
            return events;
        }
        static void VectorNear(Vector2 actual, Vector2 expected, string message = null)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(.0001f), message);
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(.0001f), message);
        }

        [Test]
        public void OrdinaryAssistanceMatrixUsesOnlyPreContactNormalApproach()
        {
            foreach (int seat in new[] { 0, 1 })
            foreach (float speed in new[] { 0f, 2f, 6f, 12f })
            foreach (float degrees in new[] { 0f, 30f, 60f })
            foreach (bool green in new[] { false, true })
            {
                Vector2 n = seat == 0 ? Vector2.right : Vector2.left;
                float angle = degrees * Mathf.Deg2Rad;
                Vector2 control = (n * Mathf.Cos(angle) + Vector2.up * Mathf.Sin(angle)) * speed;
                var state = BallContact(candidate, seat, control, Vector2.zero, Vector2.zero, n, green);
                var baseOnly = state.Clone(); baseOnly.PlayerBallContacts[seat] = true;
                Contact(baseOnly, seat); Contact(state, seat);
                float expected = 4f * speed * Mathf.Cos(angle) / 12f * (green ? 1.5f : 1f);
                VectorNear(state.Balls[0].Velocity - baseOnly.Balls[0].Velocity, n * expected,
                    $"seat={seat}, speed={speed}, angle={degrees}, green={green}");
                // Weighting the ball's outgoing assistance cannot alter the first base recoil.
                VectorNear(state.Players[seat].ExternalVelocity, baseOnly.Players[seat].ExternalVelocity);
            }
        }

        [Test]
        public void MeasuredHeadOnOrdinaryBaseAndAssistanceAreReportedSeparately()
        {
            foreach (float speed in new[] { 0f, 2f, 6f, 12f })
            {
                var before = BallContact(legacy, 0, Vector2.right * speed, Vector2.zero, Vector2.zero, Vector2.right);
                var after = BallContact(candidate, 0, Vector2.right * speed, Vector2.zero, Vector2.zero, Vector2.right);
                var baseOnly = after.Clone(); baseOnly.PlayerBallContacts[0] = true;
                Contact(before); Contact(after); Contact(baseOnly);
                float basis = baseOnly.Balls[0].Velocity.x;
                float oldExtra = before.Balls[0].Velocity.x - basis;
                float newExtra = after.Balls[0].Velocity.x - basis;
                Assert.That(oldExtra, Is.EqualTo(speed == 0 ? 0 : 4).Within(.0001f));
                Assert.That(newExtra, Is.EqualTo(4f * speed / 12f).Within(.0001f));
                VectorNear(after.Players[0].ExternalVelocity, before.Players[0].ExternalVelocity);
                TestContext.Out.WriteLine($"MEASURED ordinary precontact={speed:R}, base={basis:R}; V1 extra={oldExtra:R}, result={before.Balls[0].Velocity.x:R}; candidate extra={newExtra:R}, result={after.Balls[0].Velocity.x:R}; equal initial recoil={after.Players[0].ExternalVelocity.x:R}");
            }
        }

        [TestCase(12f, 0f, 10f, 2f)] // Chasing: relative approach is the limiting projection.
        [TestCase(0f, 6f, 0f, 0f)]  // External motion alone is not active drive.
        [TestCase(0f, 0f, -6f, 0f)] // Passive incoming ball.
        [TestCase(12f, -6f, 0f, 6f)]
        [TestCase(2f, 6f, 0f, 2f)] // Extra external speed cannot amplify active drive.
        public void AssistanceIsLimitedByAllThreeProjections(float drive, float external, float ballSpeed, float expectedApproach)
        {
            foreach (int seat in new[] { 0, 1 })
            {
                Vector2 n = seat == 0 ? Vector2.right : Vector2.left;
                var state = BallContact(candidate, seat, n * drive, n * external, n * ballSpeed, n);
                var baseOnly = state.Clone(); baseOnly.PlayerBallContacts[seat] = true;
                Contact(baseOnly, seat); Contact(state, seat);
                // A speed-ten chasing ball still meets the unchanged dangerous-ball
                // threshold. Its new-contact response retains energy AFTER assistance;
                // the latched base-only fixture deliberately skips that gameplay branch.
                float retention = Mathf.Abs(ballSpeed) >= candidate.DangerousBallSpeed ? candidate.DangerousEnergyRetention : 1f;
                VectorNear(state.Balls[0].Velocity - baseOnly.Balls[0].Velocity * retention,
                    n * (4f * expectedApproach / 12f * retention));
                Assert.That(state.Balls[0].DangerousImpactCount, Is.EqualTo(retention < 1f ? 1 : 0));
            }
        }

        [Test]
        public void TangentialDriveAndBackwardsActualMotionDoNotManufactureAssistance()
        {
            foreach (bool backwards in new[] { false, true })
            {
                Vector2 control = backwards ? Vector2.right * 12f : Vector2.up * 12f;
                Vector2 external = backwards ? Vector2.left * 15f : Vector2.zero;
                var state = BallContact(candidate, 0, control, external, Vector2.left * 10f, Vector2.right);
                float closing = Vector2.Dot(control + external - state.Balls[0].Velocity, Vector2.right);
                float inversePlayer = 1f / candidate.PlayerMass, inverseBall = 1f / candidate.BallMass;
                float impulse = (1f + candidate.Bounciness) * closing / (inversePlayer + inverseBall);
                Vector2 expected = (Vector2.left * 10f + Vector2.right * (impulse * inverseBall)) * candidate.DangerousEnergyRetention;
                var old = BallContact(legacy, 0, control, external, Vector2.left * 10f, Vector2.right);
                Contact(state); Contact(old);
                VectorNear(state.Balls[0].Velocity, expected);
                VectorNear(state.Players[0].ExternalVelocity, old.Players[0].ExternalVelocity,
                    "Dangerous-ball initial knockback is unchanged.");
                Assert.That(state.Balls[0].DangerousImpactCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void FullSpeedOrdinaryAndAllValidDashAnglesKeepFirstResponseIncludingGreenCap()
        {
            foreach (bool dash in new[] { false, true })
            foreach (bool green in new[] { false, true })
            foreach (int seat in new[] { 0, 1 })
            foreach (float degrees in dash ? new[] { 0f, 30f, 60f } : new[] { 0f })
            {
                Vector2 n = seat == 0 ? Vector2.right : Vector2.left;
                Vector2 control = (n * Mathf.Cos(degrees * Mathf.Deg2Rad) + Vector2.up * Mathf.Sin(degrees * Mathf.Deg2Rad)) * (dash ? 24f : 12f);
                var before = BallContact(legacy, seat, control, Vector2.zero, Vector2.zero, n, green, dash);
                var after = BallContact(candidate, seat, control, Vector2.zero, Vector2.zero, n, green, dash);
                Contact(before, seat); Contact(after, seat);
                VectorNear(after.Balls[0].Velocity, before.Balls[0].Velocity);
                VectorNear(after.Players[seat].ExternalVelocity, before.Players[seat].ExternalVelocity);
                Assert.That(after.Balls[0].Velocity.magnitude, Is.LessThanOrEqualTo(48.00001f));
                if (dash && green && degrees == 0) Assert.That(after.Balls[0].Velocity.magnitude, Is.EqualTo(48f).Within(.0001f));
            }
        }

        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void PlayerContactReducesOnlyExtrasAndPreservesBaseResponseForBothDirections(bool dash0, bool dash1)
        {
            foreach (bool mirrored in new[] { false, true })
            foreach (int greenMask in new[] { 0, 1, 2, 3 })
            {
                bool green0 = (greenMask & 1) != 0, green1 = (greenMask & 2) != 0;
                var previous = PlayerPair(previousSixCandidate, dash0, dash1, green0, green1, mirrored: mirrored);
                var current = PlayerPair(candidate, dash0, dash1, green0, green1, mirrored: mirrored);
                var previousBase = PlayerPair(previousSixCandidate, dash0, dash1, green0, green1, mirrored: mirrored, latched: true);
                var currentBase = PlayerPair(candidate, dash0, dash1, green0, green1, mirrored: mirrored, latched: true);
                Assert.That(ResolvePair(previous).Single().Kind, Is.EqualTo(DashArenaEventKind.Impact));
                Assert.That(ResolvePair(current).Single().Kind, Is.EqualTo(DashArenaEventKind.Impact));
                Assert.That(ResolvePair(previousBase), Is.Empty);
                Assert.That(ResolvePair(currentBase), Is.Empty);
                Vector2 n = mirrored ? Vector2.left : Vector2.right;
                float basis = .95f * ((dash0 ? 24f : 12f) + (dash1 ? 24f : 12f));
                for (int seat = 0; seat < 2; seat++)
                {
                    Vector2 away = seat == 0 ? -n : n;
                    bool sourceDash = seat == 0 ? dash1 : dash0;
                    bool sourceGreen = seat == 0 ? green1 : green0;
                    float oldExtra = (sourceDash ? 14f : 6f) * (sourceGreen ? 1.5f : 1f);
                    float newExtra = oldExtra * .75f;
                    VectorNear(currentBase.Players[seat].ExternalVelocity, previousBase.Players[seat].ExternalVelocity);
                    VectorNear(currentBase.Players[seat].ExternalVelocity, away * basis);
                    VectorNear(previous.Players[seat].ExternalVelocity, away * (basis + oldExtra));
                    VectorNear(current.Players[seat].ExternalVelocity, away * (basis + newExtra));
                    VectorNear(current.Players[seat].ControlVelocity, previous.Players[seat].ControlVelocity);
                    VectorNear(current.Players[seat].Position, previous.Players[seat].Position);
                    Assert.That(current.Players[seat].StunTicks, Is.Zero, "Player contact must not add a new input lock.");
                    Assert.That(current.Players[seat].RecoilGraceTicks, Is.EqualTo(6));
                    Assert.That(current.Players[seat].DashTicks, Is.EqualTo(previous.Players[seat].DashTicks));
                    float previousTotal = Vector2.Dot(previous.Players[seat].Velocity, away);
                    float currentTotal = Vector2.Dot(current.Players[seat].Velocity, away);
                    Assert.That(previousTotal - currentTotal, Is.EqualTo(oldExtra * .25f).Within(.0001f));
                    if (!mirrored && greenMask == 0)
                        TestContext.Out.WriteLine($"MEASURED player pair dash0={dash0}, dash1={dash1}, recipient={seat}: base external={basis:R}; old extra={oldExtra:R}, new extra={newExtra:R}; total external={previous.Players[seat].ExternalVelocity.magnitude:R}->{current.Players[seat].ExternalVelocity.magnitude:R}; composed away speed={previousTotal:R}->{currentTotal:R}. Extra falls 25%; total knockback does not fall 25%.");
                }
            }
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void ReducedPlayerExtrasPreserveArmorImmunityAndExternalSpeedCap(bool purple0, bool purple1)
        {
            foreach (bool dash0 in new[] { false, true })
            foreach (bool dash1 in new[] { false, true })
            foreach (bool mirrored in new[] { false, true })
            {
                // Both green isolates the unchanged outgoing enhancement even when one recipient is immune.
                var previous = PlayerPair(previousSixCandidate, dash0, dash1, true, true, purple0, purple1, mirrored);
                var current = PlayerPair(candidate, dash0, dash1, true, true, purple0, purple1, mirrored);
                ResolvePair(previous); ResolvePair(current);
                Vector2 n = mirrored ? Vector2.left : Vector2.right;
                float basis = 1.9f * ((dash0 ? 24f : 12f) + (dash1 ? 24f : 12f));
                for (int seat = 0; seat < 2; seat++)
                {
                    bool immune = seat == 0 ? purple0 : purple1;
                    Vector2 away = seat == 0 ? -n : n;
                    float oldExtra = ((seat == 0 ? dash1 : dash0) ? 14f : 6f) * 1.5f;
                    float oldExpected = immune ? 0f : Mathf.Min(72f, basis + oldExtra);
                    float newExpected = immune ? 0f : Mathf.Min(72f, basis + oldExtra * .75f);
                    VectorNear(previous.Players[seat].ExternalVelocity, away * oldExpected);
                    VectorNear(current.Players[seat].ExternalVelocity, away * newExpected);
                    VectorNear(current.Players[seat].ControlVelocity, previous.Players[seat].ControlVelocity);
                    VectorNear(current.Players[seat].Position, previous.Players[seat].Position);
                    Assert.That(current.Players[seat].ExternalVelocity.magnitude, Is.LessThanOrEqualTo(72.00001f));
                    Assert.That(current.Players[seat].RecoilGraceTicks, Is.EqualTo(immune ? 0 : 6));
                    Assert.That(current.Players[seat].StunTicks, Is.Zero);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PersistentPlayerContactDoesNotReapplyReducedExtrasOrRestartRecoil(bool dash)
        {
            foreach (bool mirrored in new[] { false, true })
            {
                var previous = PlayerPair(previousSixCandidate, dash, dash, mirrored: mirrored, latched: true);
                var current = PlayerPair(candidate, dash, dash, mirrored: mirrored, latched: true);
                previous.Players[0].RecoilGraceTicks = current.Players[0].RecoilGraceTicks = 2;
                previous.Players[1].RecoilGraceTicks = current.Players[1].RecoilGraceTicks = 4;
                Assert.That(ResolvePair(previous), Is.Empty);
                Assert.That(ResolvePair(current), Is.Empty);
                for (int seat = 0; seat < 2; seat++)
                {
                    VectorNear(current.Players[seat].ExternalVelocity, previous.Players[seat].ExternalVelocity);
                    Assert.That(current.Players[seat].RecoilGraceTicks, Is.EqualTo(seat == 0 ? 2 : 4));
                }
                Vector2 first0 = current.Players[0].ExternalVelocity, first1 = current.Players[1].ExternalVelocity;
                Assert.That(ResolvePair(current), Is.Empty, "The separating continuation must not produce another gameplay impact.");
                VectorNear(current.Players[0].ExternalVelocity, first0);
                VectorNear(current.Players[1].ExternalVelocity, first1);
                Assert.That(current.Players[0].RecoilGraceTicks, Is.EqualTo(2));
                Assert.That(current.Players[1].RecoilGraceTicks, Is.EqualTo(4));
            }
        }

        [Test]
        public void SixthAndSeventhExternalUpdatesHavePreciselyDifferentRates()
        {
            var before = Live(legacy); var after = Live(candidate);
            before.Players[0].ExternalVelocity = after.Players[0].ExternalVelocity = Vector2.right * 12;
            after.Players[0].RecoilGraceTicks = 6;
            for (int step = 1; step <= 6; step++)
            {
                Step(before); Step(after);
                VectorNear(after.Players[0].ExternalVelocity, before.Players[0].ExternalVelocity);
                VectorNear(after.Players[0].Position, before.Players[0].Position);
                Assert.That(after.Players[0].RecoilGraceTicks, Is.EqualTo(6 - step));
                TestContext.Out.WriteLine($"RECOIL step={step} V1={before.Players[0].ExternalVelocity.x:R} candidate={after.Players[0].ExternalVelocity.x:R}");
            }
            float six = after.Players[0].ExternalVelocity.x;
            Step(before); Step(after);
            Assert.That(six - after.Players[0].ExternalVelocity.x, Is.EqualTo(48f / 60f).Within(.00001f));
            Assert.That(six - before.Players[0].ExternalVelocity.x, Is.EqualTo(8f / 60f).Within(.00001f));
            Assert.That(after.Players[0].RecoilGraceTicks, Is.Zero);
            TestContext.Out.WriteLine($"RECOIL step=7 V1={before.Players[0].ExternalVelocity.x:R} candidate={after.Players[0].ExternalVelocity.x:R}");
        }

        [Test]
        public void PlayerExtraReductionLeavesBallResponseAndExistingSixTimesTailUnchanged()
        {
            foreach (int seat in new[] { 0, 1 })
            foreach (bool dash in new[] { false, true })
            foreach (bool green in new[] { false, true })
            foreach (float drive in dash ? new[] { 24f } : new[] { 0f, 2f, 12f })
            foreach (float incomingBall in new[] { 0f, 10f, 20f })
            {
                Vector2 n = seat == 0 ? Vector2.right : Vector2.left;
                var previous = BallContact(previousSixCandidate, seat, n * drive, Vector2.zero, -n * incomingBall, n, green, dash);
                var current = BallContact(candidate, seat, n * drive, Vector2.zero, -n * incomingBall, n, green, dash);
                Contact(previous, seat); Contact(current, seat);
                VectorNear(current.Balls[0].Velocity, previous.Balls[0].Velocity);
                Assert.That(current.Balls[0].DangerousImpactCount, Is.EqualTo(previous.Balls[0].DangerousImpactCount));
                VectorNear(current.Players[seat].ExternalVelocity, previous.Players[seat].ExternalVelocity);
                VectorNear(current.Players[seat].ControlVelocity, previous.Players[seat].ControlVelocity);
                for (int update = 1; update <= 7; update++)
                {
                    Update(previous, seat); Update(current, seat);
                    VectorNear(current.Players[seat].ExternalVelocity, previous.Players[seat].ExternalVelocity);
                    VectorNear(current.Players[seat].ControlVelocity, previous.Players[seat].ControlVelocity);
                    Assert.That(current.Players[seat].RecoilGraceTicks, Is.EqualTo(previous.Players[seat].RecoilGraceTicks));
                    Assert.That(current.Players[seat].StunTicks, Is.Zero);
                }
            }
        }

        [TestCase(.1f, false)]
        [TestCase(.1f, true)]
        [TestCase(12f, false)]
        [TestCase(12f, true)]
        [TestCase(36f, false)]
        [TestCase(36f, true)]
        public void PlayerExtraReductionDoesNotChangeEqualStartSixTimesTailOrReverseDrive(float initialSpeed, bool reverse)
        {
            var previous = Live(previousSixCandidate); var current = Live(candidate);
            Vector2 start = new Vector2(-18f, -18f);
            foreach (var state in new[] { previous, current })
            {
                state.Players[0].Position = start;
                state.Players[0].ExternalVelocity = Vector2.right * initialSpeed;
                state.Players[0].RecoilGraceTicks = 6;
            }
            var input = new DashArenaInput { Move = reverse ? Vector2.left : Vector2.zero };
            int stopped = 0;
            for (int tick = 1; tick <= 180; tick++)
            {
                Assert.That(DashArenaSimulation.Step(previous, input, default), Is.Empty);
                Assert.That(DashArenaSimulation.Step(current, input, default), Is.Empty);
                VectorNear(current.Players[0].Position, previous.Players[0].Position);
                VectorNear(current.Players[0].ExternalVelocity, previous.Players[0].ExternalVelocity);
                VectorNear(current.Players[0].ControlVelocity, previous.Players[0].ControlVelocity);
                VectorNear(current.Players[0].Facing, previous.Players[0].Facing);
                Assert.That(current.Players[0].RecoilGraceTicks, Is.EqualTo(previous.Players[0].RecoilGraceTicks));
                Assert.That(current.Players[0].StunTicks, Is.Zero);
                Assert.That(current.Players[0].Alive && previous.Players[0].Alive, Is.True);
                if (!current.Players[0].ExternalVelocity.Equals(Vector2.zero)) continue;
                Assert.That(previous.Players[0].ExternalVelocity, Is.EqualTo(Vector2.zero));
                stopped = tick;
                break;
            }
            Assert.That(stopped, Is.GreaterThan(0));
            TestContext.Out.WriteLine($"MEASURED fixed initial external={initialSpeed:R}, reverse={reverse}: old and new 6x candidate have identical external-zero tick={stopped}, displacement={current.Players[0].Position.x - start.x:R}. This isolates the unchanged decay; an actual player hit now starts with less external velocity.");
        }

        [Test]
        public void SmallExternalImpulseStopsWithoutReversalAndClearsStageOnFirstUpdate()
        {
            var state = Live(candidate);
            state.Players[0].ExternalVelocity = Vector2.right * .1f;
            state.Players[0].RecoilGraceTicks = 6;
            Step(state);
            Assert.That(state.Players[0].ExternalVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(state.Players[0].RecoilGraceTicks, Is.Zero);
        }

        [Test]
        public void PersistentContactDoesNotRefreshButSeparatedRecontactDoes()
        {
            var state = BallContact(candidate, 0, Vector2.right * 12, Vector2.left, Vector2.zero, Vector2.right);
            state.PlayerBallContacts[0] = true;
            state.Players[0].RecoilGraceTicks = 2;
            Vector2 before = state.Players[0].ExternalVelocity;
            Contact(state);
            Assert.That(state.Players[0].ExternalVelocity, Is.Not.EqualTo(before), "Latched base response still runs.");
            Assert.That(state.Players[0].RecoilGraceTicks, Is.EqualTo(2));
            Update(state); Assert.That(state.Players[0].RecoilGraceTicks, Is.EqualTo(1));
            Update(state); Assert.That(state.Players[0].RecoilGraceTicks, Is.Zero);
            state.Balls[0].Position = new Vector2(5, -18);
            Invoke("ReleaseContacts", state);
            Assert.That(state.PlayerBallContacts[0], Is.False);
            state.Players[0].Position = new Vector2(0, -18);
            state.Players[0].ControlVelocity = Vector2.right * 12;
            state.Players[0].ExternalVelocity = Vector2.left;
            state.Balls[0].Position = new Vector2(1.08f, -18);
            state.Balls[0].Velocity = Vector2.zero;
            Contact(state);
            Assert.That(state.Players[0].RecoilGraceTicks, Is.EqualTo(6));
        }

        [Test]
        public void AnotherDynamicBodyRestartsStageButIgnoredAndFullyCappedHitsDoNot()
        {
            var state = BallContact(ArenaRulesProfile.CreateHandfeelCandidate(ballCount: 2), 0,
                Vector2.right * 12, Vector2.zero, Vector2.zero, Vector2.right);
            Contact(state); Assert.That(state.Players[0].RecoilGraceTicks, Is.EqualTo(6));
            state.Players[0].RecoilGraceTicks = 1;
            state.Balls[1] = new DashBallState { Active = true,
                Position = state.Players[0].Position - Vector2.right * 1.08f, Velocity = Vector2.right * 20 };
            Contact(state, 0, 1);
            Assert.That(state.Players[0].RecoilGraceTicks, Is.EqualTo(6));

            var armor = BallContact(candidate, 0, Vector2.right * 12, Vector2.zero, Vector2.zero, Vector2.right);
            armor.Players[0].PurpleTicks = 300;
            Contact(armor);
            Assert.That(armor.Players[0].ExternalVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(armor.Players[0].RecoilGraceTicks, Is.Zero);

            // Deliberately saturated solver fixture: only actual accepted delta counts.
            var saturated = BallContact(candidate, 0, Vector2.right * 100, Vector2.left * 72, Vector2.zero, Vector2.right);
            saturated.Players[0].RecoilGraceTicks = 2;
            Contact(saturated);
            Assert.That(saturated.Players[0].ExternalVelocity, Is.EqualTo(Vector2.left * 72));
            Assert.That(saturated.Players[0].RecoilGraceTicks, Is.EqualTo(2));
        }

        [Test]
        public void SubEpsilonButActuallyAcceptedHitStartsStage()
        {
            var state = BallContact(candidate, 0, Vector2.right * .000001f, Vector2.zero, Vector2.zero, Vector2.right);
            Contact(state);
            Assert.That(state.Players[0].ExternalVelocity.x, Is.LessThan(0f));
            Assert.That(state.Players[0].RecoilGraceTicks, Is.EqualTo(6));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ZeroFinalExternalVelocityClearsStageAfterCompleteNewOrLatchedContact(bool latched)
        {
            // Equal masses and restitution 1 give exact binary cancellation of six;
            // these are isolated test parameters, not changes to the candidate preset.
            var builder = candidate.ToBuilder(); builder.PlayerMass = builder.BallMass = 1f; builder.Bounciness = 1f;
            var state = BallContact(builder.Freeze(), 0, Vector2.zero, Vector2.right * 6, Vector2.zero, Vector2.right);
            state.Players[0].RecoilGraceTicks = 3; state.PlayerBallContacts[0] = latched;
            Contact(state);
            Assert.That(state.Players[0].ExternalVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(state.Players[0].RecoilGraceTicks, Is.Zero);
        }

        [TestCase("pillar")]
        [TestCase("wall")]
        [TestCase("boundary")]
        [TestCase("gate")]
        public void StaticResponsesNeverStartOrResetDynamicStage(string obstacle)
        {
            foreach (int grace in new[] { 0, 4 })
            {
                var state = Live(candidate);
                Vector2 direction;
                if (obstacle == "pillar")
                {
                    var pillar = candidate.Pillars[0]; direction = Vector2.right;
                    state.Players[0].Position = pillar.Position - direction * (pillar.Radius + candidate.PlayerRadius + .01f);
                }
                else if (obstacle == "wall")
                {
                    var wall = candidate.Walls[0]; direction = Vector2.up;
                    state.Players[0].Position = wall.Position - direction * (wall.HalfSize.y + candidate.PlayerRadius + .01f);
                }
                else if (obstacle == "boundary")
                {
                    direction = Vector2.right;
                    state.Players[0].Position = new Vector2(candidate.HalfWidth - candidate.PlayerRadius - .01f, 20);
                }
                else
                {
                    direction = Vector2.down; state.Phase = MatchState.Kickoff; state.PhaseTicks = 100; state.GateClosed = true;
                    state.Players[0].Position = Vector2.up * (candidate.KickoffHalfDepth + candidate.PlayerRadius + .01f);
                }
                state.Players[0].ControlVelocity = direction * 12;
                state.Players[0].ExternalVelocity = grace == 0 ? Vector2.zero : direction * 2;
                state.Players[0].RecoilGraceTicks = grace;
                Step(state);
                Assert.That(state.Players[0].RecoilGraceTicks, Is.EqualTo(grace == 0 ? 0 : 3), obstacle);
                Assert.That(Vector2.Dot(state.Players[0].Velocity, direction), Is.LessThanOrEqualTo(.0001f), "Static response still applies.");
                if (obstacle == "pillar") Assert.That(state.Players[0].StunTicks, Is.EqualTo(36));
                if (obstacle == "wall") Assert.That(state.Walls[0].Broken, Is.False);
            }
        }

        [Test]
        public void StunKeepsItsDurationWhileExistingExternalUpdateConsumesGrace()
        {
            var state = Live(candidate);
            state.Players[0].ExternalVelocity = Vector2.right * 12;
            state.Players[0].RecoilGraceTicks = 6;
            state.Players[0].StunTicks = 36;
            for (int i = 1; i <= 7; i++)
            {
                DashArenaSimulation.Step(state, new DashArenaInput { Move = Vector2.left, DashPressed = true }, default);
                Assert.That(state.Players[0].StunTicks, Is.EqualTo(36 - i));
                Assert.That(state.Players[0].ControlVelocity, Is.EqualTo(Vector2.zero));
                Assert.That(state.Players[0].DashTicks, Is.Zero);
            }
            Assert.That(state.Players[0].ExternalVelocity.x, Is.EqualTo(10.4f).Within(.00001f));
        }

        [Test]
        public void FallRespawnSuccessfulTeleportAndKickoffClearPhaseButFailedTeleportPreservesIt()
        {
            var state = Live(candidate);
            state.Players[0].ExternalVelocity = Vector2.right * 12; state.Players[0].RecoilGraceTicks = 6;
            Invoke("Fall", state, 0, new List<DashArenaEvent>());
            Assert.That(state.Players[0].RecoilGraceTicks, Is.Zero);
            Assert.That(state.Players[0].ExternalVelocity, Is.EqualTo(Vector2.zero));
            state.Players[0].RecoilGraceTicks = 3; state.Players[0].RespawnTicks = 0;
            Invoke("RespawnPlayers", state, new List<DashArenaEvent>());
            Assert.That(state.Players[0].Alive, Is.True); Assert.That(state.Players[0].RecoilGraceTicks, Is.Zero);

            state.Players[0].RecoilGraceTicks = 6; state.Players[0].ExternalVelocity = Vector2.right * 12;
            Assert.That((bool)Invoke("Teleport", state, 0, 77, new List<DashArenaEvent>()), Is.True);
            Assert.That(state.Players[0].RecoilGraceTicks, Is.Zero);
            Assert.That(state.Players[0].ExternalVelocity, Is.EqualTo(Vector2.zero));

            var builder = candidate.ToBuilder(); builder.TeleportCandidates = new[] { candidate.SpawnForSeat(1) };
            var failure = Live(builder.Freeze()); failure.Players[1].Position = candidate.SpawnForSeat(1);
            failure.Players[0].ExternalVelocity = Vector2.right * 12; failure.Players[0].RecoilGraceTicks = 4;
            uint random = failure.RandomState; ArenaRandom.Next(ref random);
            Assert.That((bool)Invoke("Teleport", failure, 0, 78, new List<DashArenaEvent>()), Is.False);
            Assert.That(failure.RandomState, Is.EqualTo(random));
            Assert.That(failure.Players[0].ExternalVelocity, Is.EqualTo(Vector2.right * 12));
            Assert.That(failure.Players[0].RecoilGraceTicks, Is.EqualTo(4));

            failure.Phase = MatchState.Scored; failure.PhaseTicks = 2;
            Step(failure); Assert.That(failure.Players[0].RecoilGraceTicks, Is.EqualTo(4));
            Assert.That(failure.Players[0].ExternalVelocity, Is.EqualTo(Vector2.right * 12));
            Step(failure); Assert.That(failure.Phase, Is.EqualTo(MatchState.Kickoff));
            Assert.That(failure.Players[0].RecoilGraceTicks, Is.Zero);
            Assert.That(failure.Players[0].ExternalVelocity, Is.EqualTo(Vector2.zero));
            failure.Phase = MatchState.MatchOver; failure.Players[0].RecoilGraceTicks = 4;
            int frozen = failure.Checksum(); Step(failure); Assert.That(failure.Checksum(), Is.EqualTo(frozen));
            DashArenaSimulation.StartMatch(failure); Assert.That(failure.Players[0].RecoilGraceTicks, Is.Zero);
        }

        [Test]
        public void MeasuredIsolatedCandidateTailStopsEarlierAndDoesNotChangeDriveBraking()
        {
            var before = Live(legacy); var after = Live(candidate);
            before.Players[0].ExternalVelocity = after.Players[0].ExternalVelocity = Vector2.right * 12;
            after.Players[0].RecoilGraceTicks = 6;
            int legacyStop = 0, candidateStop = 0;
            for (int tick = 1; tick <= 120; tick++)
            {
                Step(before); Step(after);
                if (legacyStop == 0 && before.Players[0].ExternalVelocity.Equals(Vector2.zero)) legacyStop = tick;
                if (candidateStop == 0 && after.Players[0].ExternalVelocity.Equals(Vector2.zero)) candidateStop = tick;
                VectorNear(after.Players[0].ControlVelocity, before.Players[0].ControlVelocity);
            }
            Assert.That(candidateStop, Is.GreaterThan(6).And.LessThan(legacyStop));
            Assert.That(after.Players[0].Position.x, Is.LessThan(before.Players[0].Position.x));
            TestContext.Out.WriteLine($"MEASURED equal initial external=12, no drive: V1 zero tick={legacyStop}, distance={before.Players[0].Position.x:R}; candidate zero tick={candidateStop}, distance={after.Players[0].Position.x:R}");

            before = Live(legacy); after = Live(candidate);
            before.Players[0].ControlVelocity = after.Players[0].ControlVelocity = Vector2.right * 12;
            for (int tick = 0; tick < 40; tick++)
            {
                Step(before); Step(after);
                VectorNear(after.Players[0].ControlVelocity, before.Players[0].ControlVelocity);
                VectorNear(after.Players[0].Position, before.Players[0].Position);
            }
        }

        [TestCase(.1f)]
        [TestCase(2f)]
        [TestCase(12f)]
        [TestCase(36f)]
        public void MeasuredThreeAndSixTimesTailsShareFirstSixUpdatesThenStopWithoutReversal(float initialSpeed)
        {
            var previous = Live(previousCandidate); var current = Live(candidate);
            Vector2 start = new Vector2(-18f, -18f); // Clear horizontal lane; even the 36-speed 3x tail stays inside the arena.
            foreach (var state in new[] { previous, current })
            {
                state.Players[0].Position = start;
                state.Players[0].ExternalVelocity = Vector2.right * initialSpeed;
                state.Players[0].RecoilGraceTicks = 6;
            }
            int previousStop = 0, currentStop = 0;
            for (int tick = 1; tick <= 180 && (previousStop == 0 || currentStop == 0); tick++)
            {
                Assert.That(DashArenaSimulation.Step(previous, default, default), Is.Empty, "The measurement must remain free of contacts/goals/falls.");
                Assert.That(DashArenaSimulation.Step(current, default, default), Is.Empty, "The measurement must remain free of contacts/goals/falls.");
                Assert.That(previous.Players[0].Alive && current.Players[0].Alive, Is.True);
                Assert.That(current.Players[0].ControlVelocity, Is.EqualTo(Vector2.zero));
                Assert.That(previous.Players[0].ControlVelocity, Is.EqualTo(Vector2.zero));
                Assert.That(current.Players[0].ExternalVelocity.x, Is.InRange(0f, previous.Players[0].ExternalVelocity.x + .00001f));
                if (tick <= 6)
                {
                    VectorNear(current.Players[0].ExternalVelocity, previous.Players[0].ExternalVelocity);
                    VectorNear(current.Players[0].Position, previous.Players[0].Position);
                }
                if (previousStop == 0 && previous.Players[0].ExternalVelocity.Equals(Vector2.zero)) previousStop = tick;
                if (currentStop == 0 && current.Players[0].ExternalVelocity.Equals(Vector2.zero)) currentStop = tick;
            }
            Assert.That(previousStop, Is.GreaterThan(0));
            Assert.That(currentStop, Is.GreaterThan(0).And.LessThanOrEqualTo(previousStop));
            float previousDistance = Vector2.Distance(start, previous.Players[0].Position);
            float currentDistance = Vector2.Distance(start, current.Players[0].Position);
            Assert.That(currentDistance, Is.LessThanOrEqualTo(previousDistance + .0001f));
            if (initialSpeed > .8f)
            {
                Assert.That(currentStop, Is.LessThan(previousStop));
                Assert.That(currentDistance, Is.LessThan(previousDistance));
            }
            else Assert.That(currentStop, Is.EqualTo(previousStop), "Small impulses may end during the unchanged initial stage.");
            TestContext.Out.WriteLine($"MEASURED no-input equal-start recoil={initialSpeed:R}: previous 3x zero tick={previousStop}, distance={previousDistance:R}; current 6x zero tick={currentStop}, distance={currentDistance:R}. Isolated open-lane fixture, not a universal recovery-time bound.");
        }

        [TestCase(12f)]
        [TestCase(36f)]
        public void MeasuredSixTimesTailKeepsReverseControlAndRecoversTotalDirectionEarlier(float initialSpeed)
        {
            var previous = Live(previousCandidate); var current = Live(candidate);
            Vector2 start = new Vector2(-18f, -18f);
            foreach (var state in new[] { previous, current })
            {
                state.Players[0].Position = start;
                state.Players[0].ExternalVelocity = Vector2.right * initialSpeed;
                state.Players[0].RecoilGraceTicks = 6;
            }
            int previousReverse = 0, currentReverse = 0;
            float previousDistanceAtReverse = 0f, currentDistanceAtReverse = 0f;
            var reverse = new DashArenaInput { Move = Vector2.left };
            for (int tick = 1; tick <= 180 && (previousReverse == 0 || currentReverse == 0); tick++)
            {
                Assert.That(DashArenaSimulation.Step(previous, reverse, default), Is.Empty);
                Assert.That(DashArenaSimulation.Step(current, reverse, default), Is.Empty);
                VectorNear(current.Players[0].ControlVelocity, previous.Players[0].ControlVelocity);
                Assert.That(current.Players[0].ControlVelocity.x, Is.LessThan(0f), "Reverse input must still drive immediately; no hit-stun lock is added.");
                Assert.That(current.Players[0].StunTicks, Is.Zero);
                if (tick <= 6)
                {
                    VectorNear(current.Players[0].ExternalVelocity, previous.Players[0].ExternalVelocity);
                    VectorNear(current.Players[0].Position, previous.Players[0].Position);
                }
                if (previousReverse == 0 && previous.Players[0].Velocity.x < 0f)
                {
                    previousReverse = tick;
                    previousDistanceAtReverse = previous.Players[0].Position.x - start.x;
                }
                if (currentReverse == 0 && current.Players[0].Velocity.x < 0f)
                {
                    currentReverse = tick;
                    currentDistanceAtReverse = current.Players[0].Position.x - start.x;
                }
            }
            Assert.That(previousReverse, Is.GreaterThan(6));
            Assert.That(currentReverse, Is.GreaterThan(6).And.LessThan(previousReverse));
            TestContext.Out.WriteLine($"MEASURED reverse-input equal-start recoil={initialSpeed:R}: previous 3x first reverse tick={previousReverse}, displacement={previousDistanceAtReverse:R}; current 6x first reverse tick={currentReverse}, displacement={currentDistanceAtReverse:R}. Control acceleration is identical each tick; only the external tail differs.");
        }

        [Test]
        public void DangerousBallKeepsFirstImpulseAndUsesTheSameRevisedTailWithoutNewStun()
        {
            var previous = BallContact(previousCandidate, 0, Vector2.zero, Vector2.zero, Vector2.left * 20f, Vector2.right);
            var current = BallContact(candidate, 0, Vector2.zero, Vector2.zero, Vector2.left * 20f, Vector2.right);
            Contact(previous); Contact(current);
            VectorNear(current.Players[0].ExternalVelocity, previous.Players[0].ExternalVelocity);
            VectorNear(current.Balls[0].Velocity, previous.Balls[0].Velocity);
            Assert.That(current.Balls[0].DangerousImpactCount, Is.EqualTo(1));
            Assert.That(current.Players[0].RecoilGraceTicks, Is.EqualTo(6));
            float initial = current.Players[0].ExternalVelocity.magnitude;
            Assert.That(initial, Is.GreaterThan(1f));
            for (int update = 1; update <= 6; update++)
            {
                Update(previous); Update(current); // Isolate the actual velocity update from another ball contact.
                VectorNear(current.Players[0].ExternalVelocity, previous.Players[0].ExternalVelocity);
                Assert.That(current.Players[0].StunTicks, Is.Zero);
                Assert.That(current.Players[0].ControlVelocity, Is.EqualTo(Vector2.zero));
            }
            float sixth = current.Players[0].ExternalVelocity.magnitude;
            Update(previous); Update(current);
            Assert.That(sixth - previous.Players[0].ExternalVelocity.magnitude, Is.EqualTo(24f / 60f).Within(.00001f));
            Assert.That(sixth - current.Players[0].ExternalVelocity.magnitude, Is.EqualTo(48f / 60f).Within(.00001f));
            TestContext.Out.WriteLine($"MEASURED dangerous-ball initial external={initial:R} unchanged; update 7: previous 3x={previous.Players[0].ExternalVelocity.magnitude:R}, current 6x={current.Players[0].ExternalVelocity.magnitude:R}");
        }

        [Test]
        public void MeasuredReverseInputTrajectoryKeepsInitialDriveAndShortensExternalTail()
        {
            var before = Live(legacy); var after = Live(candidate);
            before.Players[0].ExternalVelocity = after.Players[0].ExternalVelocity = Vector2.right * 12;
            after.Players[0].RecoilGraceTicks = 6;
            int oldReverse = 0, newReverse = 0;
            var reverse = new DashArenaInput { Move = Vector2.left };
            for (int tick = 1; tick <= 30; tick++)
            {
                DashArenaSimulation.Step(before, reverse, default);
                DashArenaSimulation.Step(after, reverse, default);
                VectorNear(after.Players[0].ControlVelocity, before.Players[0].ControlVelocity);
                if (tick <= 6)
                {
                    VectorNear(after.Players[0].ExternalVelocity, before.Players[0].ExternalVelocity);
                    VectorNear(after.Players[0].Position, before.Players[0].Position);
                }
                if (oldReverse == 0 && before.Players[0].Velocity.x < 0f) oldReverse = tick;
                if (newReverse == 0 && after.Players[0].Velocity.x < 0f) newReverse = tick;
                if (tick == 1 || tick == 6 || tick == 7 || tick == 15 || tick == 30)
                    TestContext.Out.WriteLine($"MEASURED reverse-input step={tick}, common drive={after.Players[0].ControlVelocity.x:R}; V1 external={before.Players[0].ExternalVelocity.x:R}, total={before.Players[0].Velocity.x:R}, displacement={before.Players[0].Position.x:R}; candidate external={after.Players[0].ExternalVelocity.x:R}, total={after.Players[0].Velocity.x:R}, displacement={after.Players[0].Position.x:R}");
            }
            Assert.That(newReverse, Is.GreaterThan(6).And.LessThan(oldReverse));
            TestContext.Out.WriteLine($"MEASURED reverse-input first negative total velocity: V1 tick={oldReverse}, candidate tick={newReverse}; isolated initial external=12 only, not a universal recovery-time promise.");
        }
    }
}
