using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class PocketBumpDashGreyboxTests
    {
        GameObject _root;
        DashArenaGreybox _preview;

        [SetUp]
        public void CreateIsolatedHarness()
        {
            _root = new GameObject("Test isolated Dash greybox");
            _preview = _root.AddComponent<DashArenaGreybox>();
            _preview.AutomaticTicking = false;
            _preview.ShowHud = false;
        }

        [TearDown]
        public void DestroyIsolatedHarness()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void TwoLocalViewportsShareStateAndOneListenerWithoutAuthorityColliders()
        {
            Assert.That(_preview.State, Is.Not.Null);
            Assert.That(_preview.PreviewCameras.Count, Is.EqualTo(2));
            Assert.That(_preview.PreviewCameras[0].rect, Is.EqualTo(new Rect(0f, 0f, 0.5f, 1f)));
            Assert.That(_preview.PreviewCameras[1].rect, Is.EqualTo(new Rect(0.5f, 0f, 0.5f, 1f)));
            Assert.That(_root.GetComponentsInChildren<AudioListener>(true).Length, Is.EqualTo(1));
            foreach (Collider collider in _root.GetComponentsInChildren<Collider>(true))
                Assert.That(collider.enabled, Is.False, collider.name);
            Assert.That(_root.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(_root.GetComponentsInChildren<Rigidbody2D>(true), Is.Empty);
            Assert.That(_root.GetComponentsInChildren<MatchDirector>(true), Is.Empty);
            Assert.That(_root.GetComponentsInChildren<GameSession>(true), Is.Empty);
        }

        [Test]
        public void RenderingBothViewsDoesNotAdvanceOrMutateSimulation()
        {
            DashArenaState state = _preview.State;
            int before = state.Checksum();
            _preview.RenderPreview(0f);
            _preview.RenderPreview(0.5f);
            _preview.RenderPreview(1f);
            Assert.That(_preview.State, Is.SameAs(state));
            Assert.That(state.Checksum(), Is.EqualTo(before));
            int tick = state.Tick;
            _preview.StepPreview(default, default);
            Assert.That(state.Tick, Is.EqualTo(tick + 1), "Two cameras must not double-step the world.");
        }

        [Test]
        public void BothCamerasKeepNorthAtScreenTop()
        {
            for (int seat = 0; seat < 2; seat++)
            {
                Vector2 at = _preview.State.Players[seat].Position;
                Camera camera = _preview.PreviewCameras[seat];
                Vector3 center = camera.WorldToViewportPoint(new Vector3(at.x, 0f, at.y));
                Vector3 north = camera.WorldToViewportPoint(new Vector3(at.x, 0f, at.y + 2f));
                Vector3 east = camera.WorldToViewportPoint(new Vector3(at.x + 2f, 0f, at.y));
                Assert.That(north.y, Is.GreaterThan(center.y), "North must read up for either seat.");
                Assert.That(east.x, Is.GreaterThan(center.x), "East must not be mirrored for player two.");
            }
        }

        [Test]
        public void ExplicitAuthoringRefusesToReplaceAnExistingHierarchy()
        {
            Transform world = _root.transform.Find("GreyboxWorld");
            int count = world.childCount;
            Assert.Throws<System.InvalidOperationException>(() => _preview.BuildAuthoredPreview());
            Assert.That(_root.transform.Find("GreyboxWorld"), Is.SameAs(world));
            Assert.That(world.childCount, Is.EqualTo(count));
        }

        [Test]
        public void DisablingAndEnablingHarnessDoesNotRestartTheMatch()
        {
            _preview.StepPreview(default, default);
            DashArenaState state = _preview.State;
            int checksum = state.Checksum();
            _preview.enabled = false;
            _preview.enabled = true;
            Assert.That(_preview.State, Is.SameAs(state));
            Assert.That(_preview.State.Checksum(), Is.EqualTo(checksum));
        }

        [Test]
        public void RenderingPreservesTheSharedShaderTemplateMaterial()
        {
            Material template = Resources.Load<Material>("PocketBump/NeonLit");
            Assert.That(template, Is.Not.Null);
            Color before = template.HasProperty("_BaseColor") ? template.GetColor("_BaseColor") : template.color;
            _preview.RenderPreview(1f);
            Color after = template.HasProperty("_BaseColor") ? template.GetColor("_BaseColor") : template.color;
            Assert.That(after, Is.EqualTo(before), "Greybox tint belongs in property blocks, never the shared asset.");
        }

        [Test]
        public void BluePickupDoesNotOverwriteTeleportResultToast()
        {
            DashArenaState state = _preview.State;
            state.Phase = MatchState.Live;
            state.GateClosed = false;
            state.Pickups[0] = new DashPickupState
            {
                Active = true, Id = 1, Kind = DashPickupKind.Blue, Position = state.Players[0].Position
            };
            _preview.StepPreview(default, default);
            Assert.That(_preview.MessageForSeat(0), Does.StartWith("BLUE:"));
            Assert.That(_preview.MessageForSeat(0), Does.Not.Contain("attempting"));
            Assert.That(_preview.MessageForSeat(0), Does.Not.Contain("PICKUP"));
        }

        [Test]
        public void PendingRepairHasAnOutlineButNoSolidVisualOrCollider()
        {
            DashArenaState state = _preview.State;
            state.Walls[0] = new DashWallState { Broken = true, RepairPending = true };
            _preview.RenderPreview(1f);
            int id = state.Rules.Walls[0].Id;
            Transform outline = _root.transform.Find("GreyboxWorld/RepairPending " + id);
            Transform solid = _root.transform.Find("GreyboxWorld/BreakableWall " + id);
            Assert.That(outline.gameObject.activeSelf, Is.True);
            Assert.That(solid.gameObject.activeSelf, Is.False);
            foreach (Collider collider in outline.GetComponentsInChildren<Collider>(true))
                Assert.That(collider.enabled, Is.False);
            state.Walls[0] = default;
            _preview.RenderPreview(1f);
            Assert.That(outline.gameObject.activeSelf, Is.False);
            Assert.That(solid.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void IncrementalBallPoolPreservesAuthoredObjectsAndDisplaysAllEightBalls()
        {
            Transform world = _root.transform.Find("GreyboxWorld");
            Transform originalBall = world.Find("ScoringBall 0");
            Vector3 originalScale = originalBall.localScale;
            var annotation = new GameObject("Keep this authored annotation");
            annotation.transform.SetParent(world, false);
            for (int i = 1; i < ArenaRulesProfile.MaximumBalls; i++)
                Object.DestroyImmediate(world.Find("ScoringBall " + i).gameObject);

            Assert.That(_preview.EnsureBallVisualPool(), Is.True);
            Assert.That(_preview.EnsureBallVisualPool(), Is.False, "The incremental upgrade must be idempotent.");
            Assert.That(world.Find("ScoringBall 0"), Is.SameAs(originalBall));
            Assert.That(originalBall.localScale, Is.EqualTo(originalScale));
            Assert.That(world.Find(annotation.name), Is.SameAs(annotation.transform));
            _preview.BallCountForNextMatch = 8;
            _preview.RestartPreview();
            for (int i = 0; i < _preview.State.Rules.KickoffTicks; i++) _preview.StepPreview(default, default);
            _preview.RenderPreview(1f);
            Assert.That(_preview.State.Balls.Length, Is.EqualTo(8));
            for (int i = 0; i < 8; i++)
            {
                Transform visual = world.Find("ScoringBall " + i);
                Assert.That(visual.gameObject.activeSelf, Is.True, "Missing active visual for ball " + i);
                Vector2 position = _preview.State.Balls[i].Position;
                Assert.That(visual.position, Is.EqualTo(new Vector3(position.x, _preview.State.Rules.BallRadius, position.y)));
            }
        }

        [Test]
        public void BallCountChangesApplyOnlyOnRestartAndUnusedVisualSlotsStayHidden()
        {
            DashArenaState oneBallMatch = _preview.State;
            ArenaRulesProfile initialRules = oneBallMatch.Rules;
            _preview.BallCountForNextMatch = 8;
            Assert.DoesNotThrow(() => _preview.RenderPreview(0.5f));
            Assert.That(_preview.State, Is.SameAs(oneBallMatch));
            Assert.That(_preview.State.Rules, Is.SameAs(initialRules));
            Assert.That(_preview.State.Balls.Length, Is.EqualTo(1));

            _preview.RestartPreview();
            Assert.That(_preview.State.Balls.Length, Is.EqualTo(8));
            DashArenaState eightBallMatch = _preview.State;
            _preview.BallCountForNextMatch = 1;
            Assert.DoesNotThrow(() => _preview.RenderPreview(1f));
            Assert.That(_preview.State, Is.SameAs(eightBallMatch));
            Assert.That(_preview.State.Rules.BallCount, Is.EqualTo(8));

            _preview.RestartPreview();
            Assert.That(_preview.State.Balls.Length, Is.EqualTo(1));
            for (int i = 0; i < _preview.State.Rules.KickoffTicks; i++) _preview.StepPreview(default, default);
            Assert.DoesNotThrow(() => _preview.RenderPreview(1f));
            Transform world = _root.transform.Find("GreyboxWorld");
            Assert.That(world.Find("ScoringBall 0").gameObject.activeSelf, Is.True);
            for (int i = 1; i < 8; i++)
                Assert.That(world.Find("ScoringBall " + i).gameObject.activeSelf, Is.False);
        }
    }
}
