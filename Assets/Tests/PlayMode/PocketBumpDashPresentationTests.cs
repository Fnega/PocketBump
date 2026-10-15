using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed partial class PocketBumpDashPresentationTests
    {
        GameObject _root;
        Material _authoredMaterial;
        DashArenaPresentation _presentation;
        DashMatchHud _hud;
        DashArenaState _state;
        DashArenaArtSample.SceneReferences _views;
        Renderer _occluder;
        float _fixedDeltaTime;
        [SetUp]
        public void Setup()
        {
            _fixedDeltaTime = Time.fixedDeltaTime;
            _root = new GameObject("Isolated art presentation fixture");
            _presentation = _root.AddComponent<DashArenaPresentation>();
            Material template = Resources.Load<Material>("PocketBump/NeonLit");
            Assert.That(template, Is.Not.Null);
            _authoredMaterial = new Material(template) { name = "Test-only intentionally customized material" };
            _authoredMaterial.SetColor("_BaseColor", new Color(.29f, .41f, .76f));
            ArenaRulesProfile rules = ArenaRulesProfile.CreateProductionV1();
            _views = new DashArenaArtSample.SceneReferences
            {
                Players = new DashArenaArtSample.RobotView[2], Balls = new DashArenaArtSample.BallView[8],
                Pillars = new DashArenaArtSample.PillarView[rules.Pillars.Count],
                Walls = new DashArenaArtSample.WallView[rules.Walls.Count],
                Pickups = new DashArenaArtSample.PickupView[rules.PickupLimit],
                Gate = Child(_root.transform, "Gate"), GameplayCameras = new Camera[2], ThemeMaterials = new[] { _authoredMaterial },
                Pulses = new DashArenaArtSample.PulseView[8]
            };
            for (int seat = 0; seat < 2; seat++)
            {
                Transform robot = Child(_root.transform, "Authored robot " + seat);
                Transform motion = Child(robot, "Motion");
                motion.localScale = new Vector3(1.07f, .94f, 1.03f);
                motion.localPosition = new Vector3(.02f, .03f, -.04f);
                Transform head = Child(motion, "Customized visible head");
                head.gameObject.AddComponent<MeshRenderer>().sharedMaterial = _authoredMaterial;
                _views.Players[seat] = new DashArenaArtSample.RobotView
                {
                    Root = robot, Motion = motion, LeftArm = Child(motion, "Left arm"), RightArm = Child(motion, "Right arm"),
                    LeftFoot = Child(motion, "Left foot"), RightFoot = Child(motion, "Right foot"),
                    GreenAura = Child(robot, "Green aura"), PurpleAura = Child(robot, "Purple aura"), Stars = Child(robot, "Stars"),
                    Trail = Line(robot, "Trail")
                };
                _views.GameplayCameras[seat] = Child(_root.transform, "Camera " + seat).gameObject.AddComponent<Camera>();
                _views.GameplayCameras[seat].rect = new Rect(seat * .5f, 0f, .5f, 1f);
                if (seat == 0) _views.GameplayCameras[seat].gameObject.AddComponent<AudioListener>();
            }
            for (int i = 0; i < 8; i++)
            {
                Transform ball = Child(_root.transform, "Ball " + i);
                _views.Balls[i] = new DashArenaArtSample.BallView { Root = ball, Trail = Line(ball, "Trail") };
            }
            for (int i = 0; i < rules.Pillars.Count; i++)
            {
                Transform pillar = Child(_root.transform, "Pillar " + i);
                _views.Pillars[i] = new DashArenaArtSample.PillarView { Id = rules.Pillars[i].Id, Root = pillar, Bell = Child(pillar, "Bell") };
            }
            for (int i = 0; i < rules.Walls.Count; i++)
            {
                Transform wall = Child(_root.transform, "Wall " + i), solid = Child(wall, "Solid");
                solid.localPosition = new Vector3(.04f, .02f, -.01f);
                _views.Walls[i] = new DashArenaArtSample.WallView
                { Id = rules.Walls[i].Id, Root = wall, Solid = solid, Outline = Child(wall, "Outline"), Debris = Child(wall, "Debris") };
            }
            for (int i = 0; i < _views.Pickups.Length; i++)
            {
                Transform pickup = Child(_root.transform, "Pickup " + i);
                _views.Pickups[i] = new DashArenaArtSample.PickupView
                { Root = pickup, Icons = new[] { Child(pickup, "Green"), Child(pickup, "Blue"), Child(pickup, "Purple") } };
            }
            for (int i = 0; i < _views.Pulses.Length; i++)
            {
                Transform pulse = Child(_root.transform, "Pulse " + i);
                var variants = new Transform[5];
                for (int kind = 0; kind < 5; kind++) variants[kind] = Child(pulse, "Effect " + kind);
                _views.Pulses[i] = new DashArenaArtSample.PulseView { Root = pulse, Variants = variants };
            }
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.DestroyImmediate(blocker.GetComponent<Collider>());
            blocker.transform.SetParent(_views.Pillars[0].Root, false);
            blocker.transform.position = new Vector3(40f, 1f, 40f);
            _occluder = blocker.GetComponent<Renderer>(); _occluder.sharedMaterial = _authoredMaterial;
            _state = DashArenaSimulation.Create(rules, 1u);
            DashArenaSimulation.StartMatch(_state);
            DashUiTheme theme = DashUiTheme.Load();
            Assert.That(theme, Is.Not.Null, "The formal HUD must have its approved serialized theme.");
            Assert.That(theme.Font, Is.Not.Null);
            _hud = DashMatchHud.Build(_root.transform, theme.Font);
            BuildGoalCameraGeometry(rules);
            _presentation.Configure(_views, _hud);
            _presentation.BindState(_state, false, 0);
        }

        [TearDown]
        public void Teardown()
        {
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
            if (_authoredMaterial != null) UnityEngine.Object.DestroyImmediate(_authoredMaterial);
            Time.fixedDeltaTime = _fixedDeltaTime;
        }

        [Test]
        public void RenderingNeverStepsTheWorldOrChangesAuthoredArt()
        {
            int hash = _state.Checksum(), tick = _state.Tick;
            Color color = _authoredMaterial.GetColor("_BaseColor");
            _presentation.Render(_state, 0f); _presentation.Render(_state, .5f); _presentation.Render(_state, 1f);
            Assert.That(_state.Checksum(), Is.EqualTo(hash));
            Assert.That(_state.Tick, Is.EqualTo(tick));
            Assert.That(_authoredMaterial.GetColor("_BaseColor"), Is.EqualTo(color));
            Assert.That(_root.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(_root.GetComponentsInChildren<AudioListener>(true).Length, Is.EqualTo(1));
        }

        [Test]
        public void PlayersAndBallsInterpolateBetweenCapturedTickPoses()
        {
            _state.Phase = MatchState.Live; _state.Balls[0].Active = true;
            Vector2 previous = _state.Players[0].Position;
            _state.Balls[0].Position = new Vector2(2f, 3f);
            _presentation.CapturePreviousPose(_state);
            _state.Players[0].Position += Vector2.right * 2f;
            _state.Balls[0].Position += Vector2.right * 2f;
            _state.Tick++;
            _presentation.Render(_state, .5f);
            Assert.That(_views.Players[0].Root.position.x, Is.EqualTo(previous.x + 1f));
            Assert.That(_views.Balls[0].Root.position.x, Is.EqualTo(3f));
            Assert.That(_views.Balls[0].Root.position.z, Is.EqualTo(3f));
        }

        [TestCase(DashArenaEventKind.Teleport)]
        [TestCase(DashArenaEventKind.Respawned)]
        public void SmallDiscontinuitySnapsPlayerAndCameraWithoutSpeculativeCues(DashArenaEventKind kind)
        {
            _state.Phase = MatchState.Live;
            _presentation.CapturePreviousPose(_state);
            _state.Players[0].Position += Vector2.right;
            var item = new DashArenaEvent(0, 0, 0, 1, kind, _state.Players[0].Position);
            _presentation.ApplyTransitions(new[] { item });
            _presentation.Render(_state, .5f);
            Assert.That(_views.Players[0].Root.position.x, Is.EqualTo(_state.Players[0].Position.x));
            Assert.That(_views.GameplayCameras[0].transform.position.x, Is.EqualTo(_state.Players[0].Position.x));
            Assert.That(_presentation.ConfirmedCueCount, Is.Zero);
            Assert.That(ActivePulses(), Is.Zero);
            _presentation.PresentConfirmedEvents(new[] { item });
            _presentation.Render(_state, .5f);
            Assert.That(_presentation.ConfirmedCueCount, Is.EqualTo(1));
            Assert.That(ActivePulses(), Is.EqualTo(1));
        }

        [Test]
        public void GoalResetAndRollbackDoNotInterpolateAcrossNewSpawnPositions()
        {
            _state.Phase = MatchState.Live; _state.Balls[0].Active = true;
            _state.Players[0].Position = new Vector2(2f, 17f);
            _state.Balls[0].Position = Vector2.right * 5f;
            _presentation.CapturePreviousPose(_state);
            _state.Phase = MatchState.Kickoff;
            _state.Players[0].Position = _state.Rules.SpawnForSeat(0);
            _state.Balls[0].Position = Vector2.zero;
            _presentation.Render(_state, .5f);
            Assert.That(_views.Players[0].Root.position.x, Is.Zero);
            Assert.That(_views.Balls[0].Root.position.x, Is.Zero);
            _state.Players[0].Position = new Vector2(1f, 18f);
            _presentation.ReconcileState(_state);
            _presentation.Render(_state, .1f);
            Assert.That(_views.Players[0].Root.position.x, Is.EqualTo(1f));
        }

        [Test]
        public void LocalHalvesAndLanAssignedSeatUseSameWorldAndOneListener()
        {
            Assert.That(_views.GameplayCameras[0].rect, Is.EqualTo(new Rect(0f, 0f, .5f, 1f)));
            Assert.That(_views.GameplayCameras[1].rect, Is.EqualTo(new Rect(.5f, 0f, .5f, 1f)));
            _presentation.BindState(_state, true, 1);
            Assert.That(_views.GameplayCameras[0].rect, Is.EqualTo(new Rect(0f, 0f, 1f, 1f)));
            Assert.That(_views.GameplayCameras[1].enabled, Is.False);
            Assert.That(_views.GameplayCameras[0].transform.position.z, Is.EqualTo(_state.Players[1].Position.y - 12f).Within(.01f));
            Assert.That(_views.GameplayCameras[0].transform.forward.z, Is.GreaterThan(0f));
            Assert.That(_views.GameplayCameras[0].GetComponent<AudioListener>().enabled, Is.True);
        }

        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(true, 0)]
        [TestCase(true, 1)]
        public void FollowCameraKeepsPlayableCornersAndNorthUpWithoutFollowingDeathHeight(bool lan, int seat)
        {
            _state.Phase = MatchState.Live;
            _presentation.BindState(_state, lan, seat);
            Camera camera = _views.GameplayCameras[lan ? 0 : seat];
            camera.aspect = lan ? 16f / 9f : 8f / 9f;
            foreach (Vector2 corner in new[] { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(1, 1) })
            {
                _state.Players[seat].Position = new Vector2(corner.x * (_state.Rules.HalfWidth - _state.Rules.PlayerRadius),
                    corner.y * (_state.Rules.HalfHeight - _state.Rules.PlayerRadius));
                _presentation.ReconcileState(_state);
                _presentation.Render(_state, 1f);
                Vector3 focus = camera.transform.position - new Vector3(0f, 20f, -12f);
                Vector3 followed = camera.WorldToViewportPoint(_views.Players[seat].Root.position);
                Assert.That(followed.x, Is.InRange(0f, 1f), "A playable side-edge position remains visible.");
                Assert.That(followed.y, Is.InRange(0f, 1f), "A playable end-edge position remains visible.");
                Assert.That(camera.orthographicSize, Is.EqualTo(10.5f), "Following does not zoom out to show the whole arena.");
                Assert.That(Mathf.Abs(focus.x), Is.LessThan(_state.Rules.HalfWidth));
                Assert.That(Mathf.Abs(focus.z), Is.LessThan(_state.Rules.HalfHeight));
                Vector3 center = camera.WorldToViewportPoint(focus);
                Vector3 north = camera.WorldToViewportPoint(focus + Vector3.forward);
                Assert.That(north.y, Is.GreaterThan(center.y), "Both seats keep world north at screen top.");
                Assert.That(north.x, Is.EqualTo(center.x).Within(.0001f));
                Vector3 beforeDeath = camera.transform.position;
                _state.Players[seat].Alive = false;
                _state.Players[seat].RespawnTicks = 180;
                _views.Players[seat].Root.position += Vector3.down * 20f; // Visual fall must not drive the camera.
                _presentation.Render(_state, 1f);
                Assert.That(camera.transform.position, Is.EqualTo(beforeDeath));
                Assert.That(camera.transform.position.y, Is.EqualTo(20f));
                Assert.That(_views.Players[seat].Root.gameObject.activeSelf, Is.False);
                _state.Players[seat].Alive = true;
            }
        }

        [Test]
        public void DelayedConfirmedEventsAreDeliveredOnceAndOldEpochsAreIgnored()
        {
            _state.Tick = 100;
            var item = new DashArenaEvent(10, 0, 0, 2, DashArenaEventKind.Pickup, Vector2.zero, 0, DashPickupKind.Green);
            _presentation.PresentConfirmedEvents(new[] { item, item });
            _presentation.Render(_state, 1f);
            Assert.That(_presentation.ConfirmedCueCount, Is.EqualTo(1));
            Assert.That(ActivePulses(), Is.EqualTo(1), "A delayed confirmed cue starts when received, not expired at its old tick.");
            _presentation.PresentConfirmedEvents(new[] { new DashArenaEvent(110, 9, 0, 3, DashArenaEventKind.Pickup, Vector2.zero, 0, DashPickupKind.Green) });
            Assert.That(_presentation.ConfirmedCueCount, Is.EqualTo(1));
            _state.Tick += 36; _presentation.Render(_state, 1f);
            Assert.That(ActivePulses(), Is.Zero);
        }

        [Test]
        public void ConfirmedEffectBurstReusesEightSlotsAndRecyclesAtThirtySixTicks()
        {
            _state.Tick = 100;
            var burst = new List<DashArenaEvent>();
            for (int i = 0; i < 26; i++)
                burst.Add(new DashArenaEvent(99, _state.Epoch, i % 2, 100 + i,
                    DashArenaEventKind.Pickup, new Vector2(i, 0), 0, (DashPickupKind)(i % 3)));
            int children = _root.GetComponentsInChildren<Transform>(true).Length;
            int checksum = _state.Checksum();
            _presentation.PresentConfirmedEvents(burst);
            _presentation.Render(_state, 1f);
            Assert.That(_presentation.ConfirmedCueCount, Is.EqualTo(26));
            Assert.That(ActivePulses(), Is.EqualTo(8));
            Assert.That(_root.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(children));
            Assert.That(_state.Checksum(), Is.EqualTo(checksum), "Visual overflow cannot mutate gameplay.");
            _presentation.PresentConfirmedEvents(burst);
            Assert.That(_presentation.ConfirmedCueCount, Is.EqualTo(26), "The burst is not replayed on retransmission.");
            _state.Tick = 135; _presentation.Render(_state, 1f);
            Assert.That(ActivePulses(), Is.EqualTo(8));
            _state.Tick = 136; _presentation.Render(_state, 1f);
            Assert.That(ActivePulses(), Is.Zero);
        }

        [Test]
        public void NewEpochClearsConfirmedEffectsTrailsAndHudAndRejectsOldEvents()
        {
            DashMatchDirector director = CreateLocalDirector();
            AdvanceToLive(director);
            DashArenaState old = director.Sim;
            _state = old;
            old.Players[0].ControlVelocity = Vector2.right * 6f;
            old.Balls[0].Velocity = Vector2.right * 10f;
            _presentation.Render(old, 1f);
            var pickup = new DashArenaEvent(old.Tick, old.Epoch, 0, 701, DashArenaEventKind.Pickup,
                old.Players[0].Position, 0, DashPickupKind.Green);
            _presentation.PresentConfirmedEvents(new[] { pickup });
            _hud.RefreshFromDirector();
            _presentation.Render(old, 1f);
            Assert.That(ActivePulses(), Is.GreaterThan(0));
            Assert.That(_views.Players[0].Trail.positionCount, Is.GreaterThan(0));
            Assert.That(_views.Balls[0].Trail.positionCount, Is.GreaterThan(0));
            Assert.That(_hud.MessageForSeat(0), Is.Not.Empty);

            DashArenaState next = DashArenaSimulation.Create(old.Rules, 2u, old.Epoch + 1);
            DashArenaSimulation.StartMatch(next);
            _presentation.BindState(next, false, 0);
            Assert.That(_presentation.State, Is.SameAs(next));
            Assert.That(_presentation.ConfirmedCueCount, Is.Zero);
            Assert.That(ActivePulses(), Is.Zero);
            foreach (var player in _views.Players) Assert.That(player.Trail.positionCount, Is.Zero);
            foreach (var ball in _views.Balls) Assert.That(ball.Trail.positionCount, Is.Zero);
            Assert.That(_hud.MessageForSeat(0), Is.Empty);
            Assert.That(_hud.MessageForSeat(1), Is.Empty);
            _hud.RefreshFromDirector();
            Assert.That(_hud.Seats[0].PickupPanel.activeSelf || _hud.Seats[1].PickupPanel.activeSelf, Is.False);
            _presentation.PresentConfirmedEvents(new[] { pickup });
            _presentation.Render(next, 1f);
            Assert.That(_presentation.ConfirmedCueCount, Is.Zero);
            Assert.That(ActivePulses(), Is.Zero);
            Assert.That(_hud.MessageForSeat(0), Is.Empty);
        }

        [Test]
        public void CorrectedWallAndBuffStateRestoresAuthoritativeVisibility()
        {
            _state.Walls[0].Broken = true; _state.Walls[0].RepairPending = true; _state.Players[0].PurpleTicks = 10;
            _presentation.Render(_state, 1f);
            Assert.That(_views.Walls[0].Solid.gameObject.activeSelf, Is.False);
            Assert.That(_views.Walls[0].Outline.gameObject.activeSelf, Is.True);
            _state.Walls[0].Broken = false; _state.Walls[0].RepairPending = false; _state.Players[0].PurpleTicks = 0;
            _presentation.ReconcileState(_state); _presentation.Render(_state, 1f);
            Assert.That(_views.Walls[0].Solid.gameObject.activeSelf, Is.True);
            Assert.That(_views.Walls[0].Outline.gameObject.activeSelf, Is.False);
            Assert.That(_views.Players[0].PurpleAura.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void CameraOcclusionRestoresSharedMaterialBeforeOtherViewportAndOnDisable()
        {
            Camera camera = _views.GameplayCameras[0];
            Vector3 target = _views.Players[0].Root.position + Vector3.up;
            _occluder.transform.position = target - camera.transform.forward * 3f;
            _occluder.transform.localScale = new Vector3(2f, 2f, 2f);
            Color authored = _authoredMaterial.GetColor("_BaseColor");
            _presentation.BeginCameraOcclusion(camera);
            Assert.That(_occluder.sharedMaterial, Is.Not.SameAs(_authoredMaterial));
            Assert.That(_authoredMaterial.GetColor("_BaseColor"), Is.EqualTo(authored));
            _presentation.EndCameraOcclusion(camera);
            Assert.That(_occluder.sharedMaterial, Is.SameAs(_authoredMaterial));
            _presentation.BeginCameraOcclusion(_views.GameplayCameras[1]);
            Assert.That(_occluder.sharedMaterial, Is.SameAs(_authoredMaterial), "The other viewport is not occluded by this pillar.");
            _presentation.EndCameraOcclusion(_views.GameplayCameras[1]);
            _presentation.BeginCameraOcclusion(camera);
            _presentation.enabled = false;
            Assert.That(_occluder.sharedMaterial, Is.SameAs(_authoredMaterial));
        }

        [Test]
        public void AuthoredScalesAndAddedModelDetailsSurviveFxAndCleanup()
        {
            Vector3 baseline = _views.Players[0].Motion.localScale;
            Transform custom = Child(_views.Players[0].Motion, "Author added detail");
            _state.Players[0].DashTicks = 6;
            _presentation.Render(_state, 1f);
            Assert.That(_views.Players[0].Motion.localScale, Is.Not.EqualTo(baseline));
            _presentation.ClearTransientPresentation();
            Assert.That(_views.Players[0].Motion.localScale, Is.EqualTo(baseline));
            Assert.That(_views.Players[0].Motion.Find(custom.name), Is.SameAs(custom));
        }

        [Test]
        public void HudPickupExplanationIsPersonalNonblockingAndRetainsBlueOutcome()
        {
            DashMatchHud hud = _hud;
            DashMatchDirector director = CreateLocalDirector();
            var blue = new DashArenaEvent(0, 0, 1, 2, DashArenaEventKind.Teleport, Vector2.zero, 0, DashPickupKind.Blue);
            hud.ShowConfirmedPickup(blue, _state.Rules);
            hud.ShowConfirmedPickup(new DashArenaEvent(0, 0, 1, 2, DashArenaEventKind.Pickup, Vector2.zero, 0, DashPickupKind.Blue), _state.Rules);
            hud.RefreshFromDirector();
            Assert.That(hud.MessageForSeat(1), Does.Contain("传送成功"));
            Assert.That(hud.MessageForSeat(0), Is.Empty);
            Assert.That(hud.Seats[1].Region.anchorMin.x, Is.EqualTo(.5f));
            Assert.That(hud.Seats[1].PickupPanel.activeSelf, Is.True);
            foreach (UnityEngine.UI.Graphic graphic in hud.Seats[1].PickupPanel.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                Assert.That(graphic.raycastTarget, Is.False);
            Assert.That(DashMatchHud.Status(director.Sim, 0), Does.Contain("KICKOFF").And.Contain("LOCKED"));
        }

        [Test]
        public void LocalPopupCoalescesWithoutBlockingEitherPlayersRealDash()
        {
            DashMatchDirector director = CreateLocalDirector();
            AdvanceToLive(director);
            Vector2 p0 = director.Sim.Players[0].Position, p1 = director.Sim.Players[1].Position;
            int tick = director.Sim.Tick;
            // Explicit presentation events: this verifies nonmodal UI, not natural pickup acquisition.
            for (int i = 0; i < 25; i++)
                _presentation.PresentConfirmedEvents(new[] { new DashArenaEvent(tick, director.MatchEpoch, 1, 1000 + i,
                    DashArenaEventKind.Pickup, p1, 0, i % 2 == 0 ? DashPickupKind.Green : DashPickupKind.Purple) });
            _hud.RefreshFromDirector();
            Assert.That(_hud.Seats[1].PickupPanel.activeSelf, Is.True);
            Assert.That(_hud.Seats[0].PickupPanel.activeSelf, Is.False);
            Assert.That(_hud.Seats[1].Region.GetComponentsInChildren<UnityEngine.UI.RectMask2D>(true).Length, Is.EqualTo(1));
            Assert.That(_hud.Seats[1].Region.Find("PickupPanel"), Is.SameAs(_hud.Seats[1].PickupPanel.transform));
            Assert.That(_hud.MessageForSeat(1), Does.Contain("冲击强化"));
            Assert.That(_hud.MessageForSeat(0), Is.Empty);
            int dashCount = 0;
            director.ConfirmedEvents += events => { foreach (var item in events) if (item.Kind == DashArenaEventKind.DashStarted) dashCount++; };
            director.InputOverride = (seat, at, state) => new DashArenaInput
                { Move = seat == 0 ? Vector2.right : Vector2.left, DashPressed = at == tick };
            for (int i = 0; i < 12; i++) director.AdvanceOneTick();
            _hud.RefreshFromDirector();
            Assert.That(director.Sim.Tick, Is.EqualTo(tick + 12));
            Assert.That(dashCount, Is.EqualTo(2));
            Assert.That(director.Sim.Players[0].Position.x, Is.GreaterThan(p0.x));
            Assert.That(director.Sim.Players[1].Position.x, Is.LessThan(p1.x));
            Assert.That(_hud.Seats[1].PickupPanel.activeSelf, Is.True);
            foreach (UnityEngine.UI.Graphic graphic in _hud.Seats[1].PickupPanel.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                Assert.That(graphic.raycastTarget, Is.False);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void LanPopupUsesAssignedSeatWhileBothSocketsContinueTransportingRealInputs(int displayedSeat)
        {
            float now = 0;
            DashMatchDirector host = CreateDirector("Host"), join = CreateDirector("Join");
            host.InitializeLan(DashLanSession.Host(_state.Rules, 9183, 7u, () => now));
            join.InitializeLan(DashLanSession.Join(_state.Rules,
                new IPEndPoint(IPAddress.Loopback, host.LanSession.LocalEndpoint.Port), () => now, 29));
            DashMatchDirector displayed = displayedSeat == 0 ? host : join;
            displayed.Configure(_presentation, _hud);
            Action<int> runTo = target =>
            {
                for (int guard = 0; (host.Sim.Tick < target || join.Sim.Tick < target) && guard < 1800; guard++)
                {
                    if (host.Sim.Tick < target) host.AdvanceOneTick();
                    if (join.Sim.Tick < target) join.AdvanceOneTick();
                    host.PumpFrame(); join.PumpFrame();
                    Assert.That(host.NetworkPaused || join.NetworkPaused, Is.False, host.FailureReason + "/" + join.FailureReason);
                    now += 1f / 60f; Thread.Sleep(1);
                }
                Assert.That(host.Sim.Tick, Is.EqualTo(target)); Assert.That(join.Sim.Tick, Is.EqualTo(target));
            };
            runTo(200);
            Assert.That(displayed.LocalSeat, Is.EqualTo(displayedSeat));
            int tick = displayed.Sim.Tick;
            // A confirmed-presentation fixture isolates seat routing; real sockets and input progression remain active.
            _presentation.PresentConfirmedEvents(new[]
            {
                new DashArenaEvent(tick - 1, displayed.MatchEpoch, 1 - displayedSeat, 501, DashArenaEventKind.Pickup, Vector2.zero, 0, DashPickupKind.Green),
                new DashArenaEvent(tick - 1, displayed.MatchEpoch, displayedSeat, 502, DashArenaEventKind.Teleport, Vector2.zero, 0, DashPickupKind.Blue)
            });
            _hud.RefreshFromDirector();
            Assert.That(_hud.MessageForSeat(displayedSeat), Does.Contain("传送成功"));
            Assert.That(_hud.MessageForSeat(1 - displayedSeat), Is.Empty);
            Assert.That(_hud.Seats[displayedSeat].Region.gameObject.activeSelf, Is.True);
            Assert.That(_hud.Seats[1 - displayedSeat].Region.gameObject.activeSelf, Is.False);
            Assert.That(_hud.Seats[displayedSeat].Region.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(_hud.Seats[displayedSeat].Region.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(_hud.Seats[displayedSeat].Status.text, Does.StartWith("P" + (displayedSeat + 1) + "  WASD"));
            int hostBefore = host.LanSession.AcceptedInputDatagrams, joinBefore = join.LanSession.AcceptedInputDatagrams;
            host.InputOverride = (seat, at, state) => new DashArenaInput { Move = Vector2.right, DashPressed = at == tick };
            join.InputOverride = (seat, at, state) => new DashArenaInput { Move = Vector2.left, DashPressed = at == tick };
            runTo(212);
            _hud.RefreshFromDirector();
            Assert.That(host.LanSession.AcceptedInputDatagrams, Is.GreaterThan(hostBefore));
            Assert.That(join.LanSession.AcceptedInputDatagrams, Is.GreaterThan(joinBefore));
            Assert.That(host.Sim.Checksum(), Is.EqualTo(join.Sim.Checksum()));
            Assert.That(host.Sim.Players[0].DashTicks, Is.GreaterThan(0));
            Assert.That(join.Sim.Players[1].DashTicks, Is.GreaterThan(0));
            Assert.That(_hud.Seats[displayedSeat].PickupPanel.activeSelf, Is.True);
        }

        [Test]
        public void PersonalStatusTracksDashBuffStunAndRespawnWithoutChangingWorld()
        {
            DashMatchDirector director = CreateLocalDirector();
            AdvanceToLive(director);
            director.Sim.Players[0].DashTicks = 18;
            director.Sim.Players[0].GreenTicks = 120; director.Sim.Players[0].PurpleTicks = 60;
            _hud.RefreshFromDirector();
            Assert.That(_hud.Seats[0].Status.text, Does.EndWith("DASH"));
            Assert.That(_hud.Seats[0].Buffs.text, Does.Contain("GREEN 2.0s").And.Contain("PURPLE 1.0s"));
            director.Sim.Players[0].DashTicks = 0; director.Sim.Players[0].DashCooldownTicks = 180;
            _hud.RefreshFromDirector(); Assert.That(_hud.Seats[0].Status.text, Does.Contain("COOLDOWN 3.0s"));
            director.Sim.Players[0].StunTicks = 36;
            _hud.RefreshFromDirector(); Assert.That(_hud.Seats[0].Status.text, Does.Contain("晕 / STUN 0.6s"));
            Assert.That(_hud.Seats[0].Stun[0].gameObject.activeSelf, Is.True);
            director.Sim.Players[0].Alive = false; director.Sim.Players[0].RespawnTicks = 180;
            int hash = director.Sim.Checksum();
            _hud.RefreshFromDirector(); Assert.That(_hud.Seats[0].Status.text, Does.Contain("RESPAWN 3.0s"));
            Assert.That(_hud.Seats[0].Stun[0].gameObject.activeSelf, Is.False);
            Assert.That(director.Sim.Checksum(), Is.EqualTo(hash));
        }

        DashMatchDirector CreateDirector(string name)
        {
            var director = Child(_root.transform, name).gameObject.AddComponent<DashMatchDirector>();
            director.AutomaticTicking = false;
            return director;
        }
        DashMatchDirector CreateLocalDirector()
        {
            DashMatchDirector director = CreateDirector("Director");
            director.InitializeLocal(_state.Rules, 1u);
            director.Configure(_presentation, _hud);
            return director;
        }
        static void AdvanceToLive(DashMatchDirector director)
        {
            for (int i = 0; i < director.Rules.KickoffTicks; i++) director.AdvanceOneTick();
            Assert.That(director.State, Is.EqualTo(MatchState.Live));
        }

        int ActivePulses()
        {
            int count = 0; foreach (var pulse in _views.Pulses) if (pulse.Root.gameObject.activeSelf) count++; return count;
        }
        static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform;
        }
        LineRenderer Line(Transform parent, string name)
        {
            LineRenderer line = Child(parent, name).gameObject.AddComponent<LineRenderer>();
            line.sharedMaterial = _authoredMaterial; line.positionCount = 0; return line;
        }
    }
}
