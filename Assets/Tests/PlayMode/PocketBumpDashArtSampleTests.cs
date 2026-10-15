using System;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class PocketBumpDashArtSampleTests
    {
        GameObject _root;
        Material _authoredMaterial;
        DashArenaArtSample _sample;
        DashArenaArtSample.SceneReferences _views;

        [SetUp]
        public void Setup()
        {
            _root = new GameObject("Isolated art presentation fixture");
            _sample = _root.AddComponent<DashArenaArtSample>();
            _sample.AutomaticTicking = false;
            _sample.ShowHud = false;
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
            _sample.Configure(_views);
            _sample.RestartPreview();
        }

        [TearDown]
        public void Teardown()
        {
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
            if (_authoredMaterial != null) UnityEngine.Object.DestroyImmediate(_authoredMaterial);
        }

        [Test]
        public void EmptyComponentDoesNotGenerateOrRecolorAnAuthoredHierarchy()
        {
            var empty = new GameObject("Unconfigured sample");
            try
            {
                DashArenaArtSample sample = empty.AddComponent<DashArenaArtSample>();
                Assert.That(sample.State, Is.Null);
                Assert.That(empty.transform.childCount, Is.Zero);
                Assert.Throws<InvalidOperationException>(() => sample.RestartPreview());
            }
            finally { UnityEngine.Object.DestroyImmediate(empty); }
        }

        [Test]
        public void RenderingReadsProductionStateWithoutChangingRulesOrSharedMaterials()
        {
            Assert.That(_sample.State.Rules.IsProvisional, Is.False);
            int checksum = _sample.State.Checksum();
            Color authored = _authoredMaterial.GetColor("_BaseColor");
            _sample.RenderPreview(0f); _sample.RenderPreview(.5f); _sample.RenderPreview(1f);
            Assert.That(_sample.State.Checksum(), Is.EqualTo(checksum));
            Assert.That(_authoredMaterial.GetColor("_BaseColor"), Is.EqualTo(authored));
            Assert.That(_views.Players[0].Motion.Find("Customized visible head").GetComponent<Renderer>().sharedMaterial,
                Is.SameAs(_authoredMaterial));
            Assert.That(_root.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(_root.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(_root.GetComponentsInChildren<AudioListener>(true).Length, Is.EqualTo(1));
        }

        [Test]
        public void TemporaryDashAnimationRestoresTheAuthoredPoseAndKeepsAddedDetails()
        {
            Transform motion = _views.Players[0].Motion;
            Vector3 restPosition = motion.localPosition, restScale = motion.localScale;
            Transform custom = Child(motion, "Author-added model detail");
            _sample.State.Players[0].DashTicks = 10;
            _sample.RenderPreview(1f);
            Assert.That(motion.localScale, Is.Not.EqualTo(restScale));
            _sample.State.Players[0].DashTicks = 0;
            _sample.RenderPreview(1f);
            Assert.That(motion.localScale, Is.EqualTo(restScale));
            Assert.That(motion.localPosition, Is.EqualTo(restPosition));
            Assert.That(motion.Find(custom.name), Is.SameAs(custom));
            _sample.enabled = false;
            Assert.That(motion.localScale, Is.EqualTo(restScale));
        }

        [Test]
        public void UnusedBallSlotsHideAndBothViewportsDoNotDoubleStep()
        {
            for (int i = 0; i < _sample.State.Rules.KickoffTicks; i++) _sample.StepPreview(default, default);
            int before = _sample.State.Tick;
            _sample.RenderPreview(1f); _sample.RenderPreview(1f);
            Assert.That(_sample.State.Tick, Is.EqualTo(before));
            Assert.That(_views.Balls[0].Root.gameObject.activeSelf, Is.True);
            for (int i = 1; i < 8; i++) Assert.That(_views.Balls[i].Root.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void StunAndArmorVisualsFollowStateAndPillarContactLightsTheBell()
        {
            _sample.State.Players[0].StunTicks = 12;
            _sample.RenderPreview(1f);
            Assert.That(_views.Players[0].Stars.gameObject.activeSelf, Is.True);
            _sample.State.Players[0].StunTicks = 0;
            _sample.State.Players[0].PurpleTicks = 120;
            _sample.PresentEvents(new[] { new DashArenaEvent(_sample.State.Tick, _sample.State.Epoch, 0,
                _views.Pillars[0].Id, DashArenaEventKind.PillarHit, Vector2.zero) });
            _sample.RenderPreview(1f);
            Assert.That(_views.Players[0].Stars.gameObject.activeSelf, Is.False);
            Assert.That(_views.Players[0].PurpleAura.gameObject.activeSelf, Is.True);
            Assert.That(_views.Pillars[0].Bell.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void WallPresentationTracksBreakPendingAndCorrectedRestoration()
        {
            Vector3 authoredPosition = _views.Walls[0].Solid.localPosition;
            _sample.State.Walls[0] = new DashWallState { Broken = true, RepairPending = true };
            _sample.RenderPreview(1f);
            Assert.That(_views.Walls[0].Solid.gameObject.activeSelf, Is.False);
            Assert.That(_views.Walls[0].Outline.gameObject.activeSelf, Is.True);
            _sample.State.Walls[0] = default;
            _sample.RenderPreview(1f);
            Assert.That(_views.Walls[0].Solid.gameObject.activeSelf, Is.True);
            Assert.That(_views.Walls[0].Outline.gameObject.activeSelf, Is.False);
            Assert.That(_views.Walls[0].Solid.localPosition, Is.EqualTo(authoredPosition));
        }

        [Test]
        public void TeleportAndRematchClearTheBoundedTrailHistory()
        {
            for (int i = 0; i < 12; i++)
            {
                _sample.StepPreview(new DashArenaInput { Move = Vector2.right }, default);
                _sample.RenderPreview(1f);
            }
            Assert.That(_views.Players[0].Trail.positionCount, Is.GreaterThan(0));
            _sample.PresentEvents(new[] { new DashArenaEvent(_sample.State.Tick, _sample.State.Epoch, 0, 0,
                DashArenaEventKind.Teleport, Vector2.zero, 0, DashPickupKind.Blue) });
            Assert.That(_views.Players[0].Trail.positionCount, Is.Zero);
            _sample.RestartPreview();
            Assert.That(_views.Players[0].Trail.positionCount, Is.Zero);
        }

        [Test]
        public void DestroyingSampleDoesNotDestroyItsSharedFontReference()
        {
            var font = new Font();
            try
            {
                _sample.PreviewFont = font;
                UnityEngine.Object.DestroyImmediate(_root);
                _root = null;
                Assert.That(font == null, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(font); }
        }

        [Test]
        public void PickupPulseIsDeduplicatedBoundedAndExpiresWithoutStateMutation()
        {
            var pickup = new DashArenaEvent(_sample.State.Tick, _sample.State.Epoch, 0, 7,
                DashArenaEventKind.Pickup, new Vector2(1f, 2f), 0, DashPickupKind.Purple);
            int checksum = _sample.State.Checksum();
            _sample.PresentEvents(new[] { pickup, pickup });
            _sample.RenderPreview(1f);
            Assert.That(ActivePulses(), Is.EqualTo(1));
            Assert.That(_sample.MessageForSeat(0), Does.Contain("免击退、免眩晕").And.Contain("仍可掉落"));
            Assert.That(_sample.State.Checksum(), Is.EqualTo(checksum));
            for (int i = 0; i < 15; i++)
                _sample.PresentEvents(new[] { new DashArenaEvent(_sample.State.Tick, _sample.State.Epoch, 1, 20 + i,
                    DashArenaEventKind.Pickup, Vector2.zero, 0, DashPickupKind.Green) });
            _sample.RenderPreview(1f);
            Assert.That(ActivePulses(), Is.EqualTo(8), "The preauthored eight-slot pool must be reused.");
            _sample.State.Tick += 36;
            _sample.RenderPreview(1f);
            Assert.That(ActivePulses(), Is.Zero);
        }

        [Test]
        public void TeleportAndRespawnUseDistinctPulseVariantsWithoutFalseFailedTeleport()
        {
            _sample.PresentEvents(new[] { new DashArenaEvent(0, 0, 0, 1, DashArenaEventKind.TeleportFailed,
                Vector2.zero, 0, DashPickupKind.Blue) });
            _sample.RenderPreview(1f);
            Assert.That(ActivePulses(), Is.Zero);
            Assert.That(_sample.MessageForSeat(0), Does.Contain("未找到安全位置"));
            _sample.PresentEvents(new[] { new DashArenaEvent(0, 0, 0, 2, DashArenaEventKind.Teleport,
                Vector2.one, 0, DashPickupKind.Blue) });
            _sample.RenderPreview(1f);
            Assert.That(ActivePulses(), Is.EqualTo(2), "Departure and arrival have separate visual rings.");
            Assert.That(_views.Pulses[0].Variants[3].gameObject.activeSelf, Is.True);
            _sample.PresentEvents(new[] { new DashArenaEvent(0, 0, 1, 1, DashArenaEventKind.Respawned, Vector2.up) });
            _sample.RenderPreview(1f);
            Assert.That(ActivePulses(), Is.EqualTo(3));
            Assert.That(_views.Pulses[2].Variants[4].gameObject.activeSelf, Is.True);
        }

        [Test]
        public void PillarSquashPreservesItsFootprintAndReturnsToAuthoredScale()
        {
            Vector3 authored = _views.Pillars[0].Root.localScale;
            _sample.PresentEvents(new[] { new DashArenaEvent(0, 0, 0, _views.Pillars[0].Id,
                DashArenaEventKind.PillarHit, Vector2.zero) });
            _sample.State.Tick = 9;
            int checksum = _sample.State.Checksum();
            _sample.RenderPreview(1f);
            Vector3 squeezed = _views.Pillars[0].Root.localScale;
            Assert.That(squeezed.y, Is.LessThan(authored.y));
            Assert.That(squeezed.x, Is.EqualTo(authored.x));
            Assert.That(squeezed.z, Is.EqualTo(authored.z));
            Assert.That(_sample.State.Checksum(), Is.EqualTo(checksum));
            _sample.State.Tick = 36;
            _sample.RenderPreview(1f);
            Assert.That(_views.Pillars[0].Root.localScale, Is.EqualTo(authored));
        }

        [TestCase(DashArenaEventKind.Teleport)]
        [TestCase(DashArenaEventKind.Respawned)]
        public void ShortDiscontinuitySnapsRobotAndCameraInsteadOfInterpolating(DashArenaEventKind kind)
        {
            Vector2 destination = _sample.State.Players[0].Position + Vector2.right;
            _sample.State.Players[0].Position = destination;
            _sample.PresentEvents(new[] { new DashArenaEvent(0, 0, 0, 1, kind, destination) });
            _sample.RenderPreview(.5f);
            Assert.That(_views.Players[0].Root.position.x, Is.EqualTo(destination.x));
            Assert.That(_views.Players[0].Root.position.z, Is.EqualTo(destination.y));
            Assert.That(_views.GameplayCameras[0].transform.position.x, Is.EqualTo(destination.x));
        }

        [Test]
        public void ActualNearbyBluePickupResetsInterpolationBeforeRendering()
        {
            _sample.State.Phase = MatchState.Live;
            _sample.State.GateClosed = false;
            _sample.State.Players[0].Position = new Vector2(1f, 8f);
            _sample.State.RandomState = 1u;
            _sample.State.Pickups[0] = new DashPickupState
            { Active = true, Id = 1, Kind = DashPickupKind.Blue, Position = new Vector2(0f, 8f) };
            _sample.StepPreview(default, default);
            Assert.That(_sample.State.Players[0].Position, Is.EqualTo(new Vector2(0f, 8f)));
            _sample.RenderPreview(.5f);
            Assert.That(_views.Players[0].Root.position, Is.EqualTo(new Vector3(0f, 0f, 8f)));
            Assert.That(_views.GameplayCameras[0].transform.position.x, Is.Zero);
            Assert.That(_sample.MessageForSeat(0), Does.Contain("传送成功"));
        }

        [Test]
        public void DashStatusIsLockedOutsideLiveAndShowsKickoffCountdown()
        {
            Assert.That(_sample.StatusForSeat(0), Does.Contain("KICKOFF 3.0s").And.Contain("DASH LOCKED"));
            _sample.State.Phase = MatchState.Live;
            Assert.That(_sample.StatusForSeat(0), Is.EqualTo("DASH READY"));
            _sample.State.Phase = MatchState.Scored;
            Assert.That(_sample.StatusForSeat(0), Does.Contain("DASH LOCKED").And.Not.Contain("READY"));
            _sample.State.Phase = MatchState.MatchOver;
            Assert.That(_sample.StatusForSeat(0), Does.Contain("DASH LOCKED").And.Not.Contain("READY"));
        }

        [Test]
        public void PillarHitBrieflySquashesRobotAndRestoresAuthoredScale()
        {
            Vector3 authored = _views.Players[0].Motion.localScale;
            _sample.PresentEvents(new[] { new DashArenaEvent(0, 0, 0, _views.Pillars[0].Id,
                DashArenaEventKind.PillarHit, Vector2.zero) });
            _sample.State.Tick = 5;
            int checksum = _sample.State.Checksum();
            _sample.RenderPreview(1f);
            Assert.That(_views.Players[0].Motion.localScale.y, Is.LessThan(authored.y));
            Assert.That(_sample.State.Checksum(), Is.EqualTo(checksum));
            _sample.State.Tick = 10;
            _sample.RenderPreview(1f);
            Assert.That(_views.Players[0].Motion.localScale, Is.EqualTo(authored));
        }

        int ActivePulses()
        {
            int result = 0;
            foreach (DashArenaArtSample.PulseView pulse in _views.Pulses) if (pulse.Root.gameObject.activeSelf) result++;
            return result;
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
