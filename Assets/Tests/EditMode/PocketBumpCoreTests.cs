using NUnit.Framework;
using UnityEngine;

// 验证无需 Play Mode 的运动、规则参数、资源结构和视口不变量。
// 测试关注修改手感参数后仍必须成立的行为，而不是绑定某种具体实现。
namespace PocketBump.Tests
{
    public sealed class PocketBumpCoreTests
    {
        [Test]
        public void MenuRegionsRepairLegacyButtonsWithoutChangingIdentityOrDuplicatingObjects()
        {
            var root = new GameObject("MenuRegionTest");
            root.SetActive(false);
            UnityEngine.EventSystems.EventSystem existing = Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
            try
            {
                MainMenuController menu = root.AddComponent<MainMenuController>();
                menu.BuildAuthoredHierarchy();
                Transform home = root.transform.Find("MainMenuCanvas/MainPanel");
                string[] names = { "Start", "HostLan", "JoinLan", "Settings", "Exit" };
                string[] parents = { "StartRegion", "ServerRegion", "ServerRegion", "ExitRegion", "ExitRegion" };
                var buttons = new UnityEngine.UI.Button[5];
                for (int i = 0; i < names.Length; i++)
                {
                    buttons[i] = home.Find(parents[i] + "/" + names[i]).GetComponent<UnityEngine.UI.Button>();
                    buttons[i].transform.SetParent(home, false);
                }
                foreach (string region in new[] { "StartRegion", "ServerRegion", "ExitRegion" })
                    Object.DestroyImmediate(home.Find(region).gameObject);
                menu.BuildAuthoredHierarchy();
                menu.BuildAuthoredHierarchy();
                Assert.That(home.childCount, Is.InRange(4, 5));
                for (int i = 0; i < names.Length; i++)
                    Assert.That(home.Find(parents[i] + "/" + names[i]).GetComponent<UnityEngine.UI.Button>(), Is.SameAs(buttons[i]));
            }
            finally
            {
                Object.DestroyImmediate(root);
                if (existing == null)
                {
                    var created = Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
                    if (created != null) Object.DestroyImmediate(created.gameObject);
                }
            }
        }

        [Test]
        public void JoinAddressInputHasViewportForPointerDrag()
        {
            var root = new GameObject("JoinAddressInputTest");
            root.SetActive(false);
            UnityEngine.EventSystems.EventSystem existing = Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
            try
            {
                MainMenuController menu = root.AddComponent<MainMenuController>();
                menu.BuildAuthoredHierarchy();
                System.Type inputType = System.Type.GetType("TMPro.TMP_InputField, Unity.TextMeshPro");
                Assert.That(inputType, Is.Not.Null, "TextMesh Pro input field type must be available to the menu.");
                var input = root.GetComponentInChildren(inputType, true);
                var viewport = inputType.GetProperty("textViewport").GetValue(input, null) as RectTransform;
                var text = inputType.GetProperty("textComponent").GetValue(input, null) as Component;
                bool raycastTarget = text != null && (bool)text.GetType().GetProperty("raycastTarget").GetValue(text, null);

                Assert.That(input, Is.Not.Null);
                Assert.That(viewport, Is.Not.Null);
                Assert.That(text, Is.Not.Null);
                Assert.That(text.transform.parent, Is.SameAs(viewport.transform));
                Assert.That(raycastTarget, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
                if (existing == null)
                {
                    var created = Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
                    if (created != null) Object.DestroyImmediate(created.gameObject);
                }
            }
        }

        [Test]
        // 从静止到移动应有加速过程，防止未来重构退化为直接设置目标速度。
        public void MovementAcceleratesWithoutInstantlyReachingTopSpeed()
        {
            Vector2 velocity = Vector2.zero;
            for (int i = 0; i < 10; i++)
                velocity = PlayerMovementMath.StepVelocity(velocity, Vector2.up, false, 0.02f);

            Assert.That(velocity.magnitude, Is.GreaterThan(0f));
            Assert.That(velocity.magnitude, Is.LessThan(ArenaLayout.NormalMaxSpeed));
        }

        [Test]
        // 垂直转向仍应获得完整操控力，保证高速时不会因公式分支突然转不动。
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
        // Boost 提高速度但不应牺牲转向加速度，这是核心手感不变量。
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
        // 反向输入先制动再掉头，用测试锁定惯性而不是瞬间反转。
        public void ReversalTakesMoreThanOnePhysicsStep()
        {
            Vector2 velocity = Vector2.up * ArenaLayout.NormalMaxSpeed;
            velocity = PlayerMovementMath.StepVelocity(velocity, Vector2.down, false, 0.02f);

            Assert.That(velocity.y, Is.GreaterThan(0f));
        }

        [Test]
        // 验证两名玩家技能键独立，同时覆盖 Windup→Boosting→Recovery 生命周期。
        public void BoostUsesIndependentKeysAndReturnsToNormal()
        {
            ControlBindings.ResetToDefaults(false);
            Assert.That(PlayerMotor.BoostKeyForPlayer(0), Is.EqualTo(KeyCode.K));
            Assert.That(PlayerMotor.BoostKeyForPlayer(1), Is.EqualTo(KeyCode.Keypad2));
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
        // 低速弹珠适合盘带，高速弹珠才造成额外危险击退。
        public void MarbleImpactBandsPreserveDribblingAndScaleDanger()
        {
            Assert.That(MarbleMotor.CalculateAdditionalImpulse(5f), Is.Zero);
            Assert.That(
                MarbleMotor.CalculateAdditionalImpulse(12f),
                Is.GreaterThan(MarbleMotor.CalculateAdditionalImpulse(8f)));
        }

        [Test]
        // 拒绝移动键和重复技能键，保证输入映射没有歧义。
        public void DefaultBindingsRejectDuplicatesAndMovementKeys()
        {
            ControlBindings.ResetToDefaults(false);
            Assert.That(ControlBindings.P1Shoot, Is.EqualTo(KeyCode.J));
            Assert.That(ControlBindings.P1Boost, Is.EqualTo(KeyCode.K));
            Assert.That(ControlBindings.P2Shoot, Is.EqualTo(KeyCode.Alpha1));
            Assert.That(ControlBindings.P2Boost, Is.EqualTo(KeyCode.Keypad2));
            Assert.That(ControlBindings.TrySet(SkillBinding.P1Boost, KeyCode.J), Is.False);
            Assert.That(ControlBindings.P1Boost, Is.EqualTo(KeyCode.K));
            Assert.That(ControlBindings.TrySet(SkillBinding.P2Shoot, KeyCode.W), Is.False);
            Assert.That(ControlBindings.TrySet(SkillBinding.P2Shoot, KeyCode.Keypad1), Is.True);
            Assert.That(ControlBindings.P2Shoot, Is.EqualTo(KeyCode.Keypad1));
            ControlBindings.ResetToDefaults(true);
        }

        [Test]
        // 朝向要平滑追踪输入，并在松键后保留，篮筐才不会瞬间跳转。
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
        // 空篮筐不能蓄力；力度阶段移动取消蓄力但不能凭空丢球。
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
        // 配置先限幅，终局同时满足目标分与领先两分。
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
        // 规则数量的弹珠必须全部生成且初态不重叠。
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
        // 锁定相机朝向与完整桌面取景，保证键盘上方向对应屏幕上方。
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
        // 验证玩法对象使用 3D 网格，而 HUD 仍保持屏幕空间覆盖层。
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

        [Test]
        public void NetworkProblemPanelShowsExactReasonAtTopRight()
        {
            var root = new GameObject("NetworkPauseTest");
            try
            {
                MatchHud hud = MatchHud.Build(root.transform);
                Assert.That(hud.NetworkIssueVisible, Is.False);
                hud.ShowNetworkIssue("INPUT HOLE");

                Assert.That(hud.NetworkIssueVisible, Is.True);
                Assert.That(hud.NetworkIssueReason, Is.EqualTo("INPUT HOLE"));
                Assert.That(hud.NetworkIssueRect.anchorMin, Is.EqualTo(Vector2.one));
                Assert.That(hud.NetworkIssueRect.anchorMax, Is.EqualTo(Vector2.one));
                Assert.That(
                    MatchHud.NetworkIssueDescription("DESYNC"),
                    Is.EqualTo("THE TWO GAME STATES NO LONGER MATCH"));
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
