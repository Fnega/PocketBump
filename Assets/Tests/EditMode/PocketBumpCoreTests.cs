using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class PocketBumpCoreTests
    {
        [Test]
        public void MovementAcceleratesWithoutInstantlyReachingTopSpeed()
        {
            Vector2 velocity = Vector2.zero;
            for (int i = 0; i < 10; i++)
                velocity = PlayerMovementMath.StepVelocity(velocity, Vector2.up, false, 0.02f);

            Assert.That(velocity.magnitude, Is.GreaterThan(0f));
            Assert.That(velocity.magnitude, Is.LessThan(ArenaLayout.NormalMaxSpeed));
        }

        [Test]
        public void PerpendicularTurnUsesForwardAcceleration()
        {
            const float dt = 0.02f;
            Vector2 velocity = Vector2.up * ArenaLayout.NormalMaxSpeed;
            Vector2 turned = PlayerMovementMath.StepVelocity(velocity, Vector2.right, false, dt);
            float moved = Vector2.Distance(velocity, turned);
            Assert.That(moved, Is.EqualTo(ArenaLayout.NormalAcceleration * dt).Within(0.0001f));
            Assert.That(ArenaLayout.TurnAcceleration, Is.EqualTo(ArenaLayout.NormalAcceleration));
        }

        [Test]
        public void BoostTurnIsNotSlowerThanNormalTurn()
        {
            const float dt = 0.02f;
            Vector2 velocity = Vector2.up * ArenaLayout.NormalMaxSpeed;
            Vector2 normal = PlayerMovementMath.StepVelocity(velocity, Vector2.right, false, dt);
            Vector2 boosted = PlayerMovementMath.StepVelocity(velocity, Vector2.right, true, dt);
            Assert.That(
                Vector2.Distance(velocity, boosted),
                Is.GreaterThanOrEqualTo(Vector2.Distance(velocity, normal) - 0.0001f));
            Assert.That(ArenaLayout.BoostSteeringMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void ReversalTakesMoreThanOnePhysicsStep()
        {
            Vector2 velocity = Vector2.up * ArenaLayout.NormalMaxSpeed;
            velocity = PlayerMovementMath.StepVelocity(velocity, Vector2.down, false, 0.02f);

            Assert.That(velocity.y, Is.GreaterThan(0f));
        }

        [Test]
        public void BoostUsesIndependentKeysAndReturnsToNormal()
        {
            ControlBindings.ResetToDefaults(false);
            Assert.That(PlayerMotor.BoostKeyForPlayer(0), Is.EqualTo(KeyCode.K));
            Assert.That(PlayerMotor.BoostKeyForPlayer(1), Is.EqualTo(KeyCode.Alpha2));
            Assert.That(PlayerMotor.ShootKeyForPlayer(0), Is.EqualTo(KeyCode.J));
            Assert.That(PlayerMotor.ShootKeyForPlayer(1), Is.EqualTo(KeyCode.Alpha1));

            BoostPhase phase = BoostPhase.Normal;
            float phaseTime = 0f;
            for (int i = 0; i < 30; i++)
                PlayerMovementMath.AdvanceBoost(ref phase, ref phaseTime, true, 0.02f);
            Assert.That(phase, Is.EqualTo(BoostPhase.Boosting));

            for (int i = 0; i < 30; i++)
                PlayerMovementMath.AdvanceBoost(ref phase, ref phaseTime, false, 0.02f);
            Assert.That(phase, Is.EqualTo(BoostPhase.Normal));
        }

        [Test]
        public void MarbleImpactBandsPreserveDribblingAndScaleDanger()
        {
            Assert.That(MarbleMotor.CalculateAdditionalImpulse(5f), Is.Zero);
            Assert.That(
                MarbleMotor.CalculateAdditionalImpulse(12f),
                Is.GreaterThan(MarbleMotor.CalculateAdditionalImpulse(8f)));
        }

        [Test]
        public void DefaultBindingsRejectDuplicatesAndMovementKeys()
        {
            ControlBindings.ResetToDefaults(false);
            Assert.That(ControlBindings.P1Shoot, Is.EqualTo(KeyCode.J));
            Assert.That(ControlBindings.P1Boost, Is.EqualTo(KeyCode.K));
            Assert.That(ControlBindings.P2Shoot, Is.EqualTo(KeyCode.Alpha1));
            Assert.That(ControlBindings.P2Boost, Is.EqualTo(KeyCode.Alpha2));
            Assert.That(ControlBindings.TrySet(SkillBinding.P1Boost, KeyCode.J), Is.False);
            Assert.That(ControlBindings.P1Boost, Is.EqualTo(KeyCode.K));
            Assert.That(ControlBindings.TrySet(SkillBinding.P2Shoot, KeyCode.W), Is.False);
            Assert.That(ControlBindings.TrySet(SkillBinding.P2Shoot, KeyCode.Keypad1), Is.True);
            Assert.That(ControlBindings.P2Shoot, Is.EqualTo(KeyCode.Keypad1));
            ControlBindings.ResetToDefaults(true);
        }

        [Test]
        public void FacingFollowsInputAndHoldsWhenReleased()
        {
            const float dt = 0.02f;
            Vector2 facing = PlayerChargeMath.SpawnFacing(0);
            Assert.That(facing, Is.EqualTo(Vector2.down));

            Vector2 afterOne = PlayerChargeMath.StepFacing(facing, Vector2.right, dt);
            Assert.That(afterOne.x, Is.GreaterThan(0f));
            Assert.That(Vector2.Dot(afterOne.normalized, Vector2.right), Is.LessThan(0.999f));

            facing = afterOne;
            for (int i = 0; i < 12; i++)
                facing = PlayerChargeMath.StepFacing(facing, Vector2.right, dt);
            Assert.That(Vector2.Dot(facing.normalized, Vector2.right), Is.GreaterThan(0.999f));

            facing = PlayerChargeMath.StepFacing(facing, Vector2.zero, dt);
            Assert.That(Vector2.Dot(facing.normalized, Vector2.right), Is.GreaterThan(0.999f));
            Assert.That(PlayerChargeMath.SpawnFacing(1), Is.EqualTo(Vector2.up));
        }

        [Test]
        public void ChargeCannotStartEmptyAndPowerCancelKeepsCarry()
        {
            var state = MatchSim.Create(new MatchSettings());
            MatchSim.StartMatch(state);
            Assert.That(MatchSim.DebugShoot(state, 0), Is.False);
            Assert.That(state.Players[0].Charge, Is.EqualTo(ChargePhase.None));

            MatchSim.DebugPlacePlayer(state, 0, Vector2.zero, Vector2.right);
            MatchSim.DebugSetMarble(state, 0, Vector2.right * ArenaLayout.BasketOffset, Vector2.zero);
            Assert.That(MatchSim.DebugCollect(state, 0, 0), Is.True);
            Assert.That(state.Players[0].Carried, Is.EqualTo(0));
            Assert.That(MatchSim.DebugShoot(state, 0), Is.True);
            Assert.That(state.Players[0].Charge, Is.EqualTo(ChargePhase.Direction));
            Assert.That(MatchSim.DebugShoot(state, 0), Is.True);
            Assert.That(state.Players[0].Charge, Is.EqualTo(ChargePhase.Power));
            Assert.That(PlayerChargeMath.ShouldCancelPower(ChargePhase.Power, Vector2.left), Is.True);
            PlayerSim p = state.Players[0];
            p.Charge = ChargePhase.None;
            p.ChargeTime = 0f;
            state.Players[0] = p;
            Assert.That(state.Players[0].Carried, Is.EqualTo(0));
            Assert.That(state.Players[0].Charge, Is.EqualTo(ChargePhase.None));
        }

        [Test]
        public void MatchRulesClampAndKeepWinByTwo()
        {
            var settings = new MatchSettings { pointsToWin = 99, marbleCount = 0 };
            settings.Clamp();
            Assert.That(settings.pointsToWin, Is.EqualTo(MatchSettings.MaxPointsToWin));
            Assert.That(settings.marbleCount, Is.EqualTo(MatchSettings.MinMarbles));
            Assert.That(MatchDirector.HasWinner(8, 7, 8, out _), Is.False);
            Assert.That(MatchDirector.HasWinner(9, 7, 8, out int winner), Is.True);
            Assert.That(winner, Is.EqualTo(0));
        }

        [Test]
        public void ArenaSpawnsConfiguredMarbleCountWithoutOverlap()
        {
            var root = new GameObject("TestRoot");
            try
            {
                Arena arena = Arena.Build(root.transform);
                var marbles = arena.SpawnMarbles(5);
                Assert.That(marbles.Count, Is.EqualTo(5));
                for (int i = 0; i < marbles.Count; i++)
                {
                    for (int j = i + 1; j < marbles.Count; j++)
                    {
                        Assert.That(
                            Vector2.Distance(marbles[i].position, marbles[j].position),
                            Is.GreaterThanOrEqualTo(ArenaLayout.MarbleRadius * 2f - 0.02f));
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void AimingCameraIsUntiltedAndFramesTheTableWithWAsScreenUp()
        {
            var go = new GameObject("TestCamera");
            try
            {
                Camera cam = go.AddComponent<Camera>();
                MatchDirector.ApplyMatchCamera(cam);
                Assert.That(cam.orthographic, Is.False);
                Assert.That(cam.fieldOfView, Is.EqualTo(ArenaLayout.CameraFov));
                Assert.That(cam.transform.rotation, Is.EqualTo(Quaternion.identity));
                Assert.That(cam.transform.position.z, Is.LessThan(0f));
                Assert.That(cam.transform.eulerAngles.sqrMagnitude, Is.EqualTo(0f).Within(0.001f));

                Vector3 north = cam.WorldToViewportPoint(new Vector3(0f, ArenaLayout.HalfHeight, 0f));
                Vector3 south = cam.WorldToViewportPoint(new Vector3(0f, -ArenaLayout.HalfHeight, 0f));
                Vector3 east = cam.WorldToViewportPoint(new Vector3(ArenaLayout.HalfWidth, 0f, 0f));
                Vector3 west = cam.WorldToViewportPoint(new Vector3(-ArenaLayout.HalfWidth, 0f, 0f));
                Assert.That(north.y, Is.GreaterThan(south.y));
                Assert.That(InView(north));
                Assert.That(InView(south));
                Assert.That(InView(east));
                Assert.That(InView(west));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void MatchPiecesUseMeshesAndHudStaysOverlay()
        {
            var root = new GameObject("MeshVisualRoot");
            try
            {
                Arena arena = Arena.Build(root.transform);
                PlayerMotor player = MatchDirector.CreatePlayer(
                    root.transform,
                    0,
                    ArenaLayout.PlayerOneSpawn,
                    arena.BounceMat);
                MatchHud hud = MatchHud.Build(root.transform);

                Assert.That(arena.GetComponentInChildren<MeshFilter>(), Is.Not.Null);
                Assert.That(player.GetComponent<MeshFilter>(), Is.Not.Null);
                Assert.That(player.GetComponent<Rigidbody2D>(), Is.Not.Null);
                Assert.That(player.GetComponent<Collider>(), Is.Null);
                Assert.That(hud.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static bool InView(Vector3 viewport)
        {
            return viewport.z > 0f
                && viewport.x > 0.02f && viewport.x < 0.98f
                && viewport.y > 0.02f && viewport.y < 0.98f;
        }
    }
}
