using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace PocketBump.Tests
{
    // Local presentation fixtures. The staged LAN bool exercises compatibility only;
    // these tests never open a socket or stand in for network/handfeel acceptance.
    public sealed class DashGroundReadinessTests
    {
        GameObject _root;
        DashMatchHud _hud;
        DashReadinessHud _binding;
        DashArenaPresentation _presentation;
        DashArenaArtSample.SceneReferences _views;
        DashArenaState _state;
        DashGroundReadinessRing[] _rings;
        Renderer[] _footprints;
        Mesh _mesh;
        Material _material;

        [SetUp] public void SetUp()
        {
            _root = new GameObject("Local ground Dash readiness fixture");
            _hud = DashMatchHud.Build(_root.transform, DashUiTheme.Load().Font);
            _binding = _hud.GetComponent<DashReadinessHud>();
            _presentation = _root.AddComponent<DashArenaPresentation>();
            _views = new DashArenaArtSample.SceneReferences
            {
                Players = new DashArenaArtSample.RobotView[2], GameplayCameras = new Camera[2],
                Pillars = new DashArenaArtSample.PillarView[0], Walls = new DashArenaArtSample.WallView[0]
            };
            Shader shader = Shader.Find("PocketBump/DashGroundReadiness");
            Assert.That(shader, Is.Not.Null, "The authored ground-ring shader must be imported.");
            _material = new Material(shader);
            _mesh = DashGroundReadinessRing.CreateUnitGroundQuad();
            _rings = new DashGroundReadinessRing[2]; _footprints = new Renderer[2];
            for (int actor = 0; actor < 2; actor++)
            {
                Transform robot = Child("Robot " + actor);
                robot.position = new Vector3(actor == 0 ? -2f : 2f, 0, 0);
                _views.Players[actor] = new DashArenaArtSample.RobotView { Root = robot, Motion = Child("Motion", robot) };
                var footprint = Child("Identity footprint", robot).gameObject;
                footprint.AddComponent<MeshFilter>().sharedMesh = _mesh;
                _footprints[actor] = footprint.AddComponent<MeshRenderer>();
                _footprints[actor].sharedMaterial = _material;
                _footprints[actor].enabled = actor == 0; // An authored disabled renderer must stay disabled on restoration.
                _rings[actor] = Child("Ground Dash " + actor).gameObject.AddComponent<DashGroundReadinessRing>();
                _rings[actor].ConfigureAssets(_mesh, _material);
                _rings[actor].IdentityColor = actor == 0 ? Color.red : Color.blue;
                _rings[actor].TrackColor = Color.black; _rings[actor].OutlineColor = Color.black;
                Camera camera = Child("Camera " + actor).gameObject.AddComponent<Camera>();
                camera.orthographic = true; camera.orthographicSize = 10; camera.aspect = 1;
                camera.rect = new Rect(actor * .5f, 0, .5f, 1);
                camera.transform.position = new Vector3(0, 10, -10); camera.transform.LookAt(Vector3.zero);
                _views.GameplayCameras[actor] = camera;
            }
            Assert.That(_binding.InstallLocalGroundRings(_rings, _footprints), Is.True);
            _presentation.Configure(_views, _hud);
            _state = DashArenaSimulation.Create(ArenaRulesProfile.CreateHandfeelCandidate(), 19);
            DashArenaSimulation.StartMatch(_state); _state.Phase = MatchState.Live;
        }

        [TearDown] public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            if (_mesh != null) Object.DestroyImmediate(_mesh);
            if (_material != null) Object.DestroyImmediate(_material);
        }
        Transform Child(string name, Transform parent = null)
        {
            var go = new GameObject(name); go.transform.SetParent(parent != null ? parent : _root.transform, false); return go.transform;
        }
        void Refresh(bool stagedLan = false, int seat = 0) => _binding.Refresh(_state, _presentation, stagedLan, seat);

        [TestCase(180, 0f)] [TestCase(90, .5f)] [TestCase(1, 179f / 180f)] [TestCase(0, 1f)]
        public void BothActorsHaveExactSnapshotProgressAndOnlyReadyCanLookFull(int ticks, float progress)
        {
            _state.Players[0].DashCooldownTicks = _state.Players[1].DashCooldownTicks = ticks;
            int checksum = _state.Checksum(); Refresh();
            for (int actor = 0; actor < 2; actor++)
            {
                var ring = _binding.GroundRingFor(actor);
                Assert.That(ring, Is.SameAs(_rings[actor])); Assert.That(ring.Visible, Is.True);
                Assert.That(ring.State.Detailed, Is.True, "Local opponents also show full cooldown progress.");
                Assert.That(ring.State.Progress, Is.EqualTo(progress).Within(.00001f));
                Assert.That(ring.State.Kind, Is.EqualTo(ticks == 0 ? DashReadinessKind.Ready : DashReadinessKind.Cooling));
                if (ticks == 0) Assert.That(ring.DisplayedProgress, Is.EqualTo(1f));
                else Assert.That(ring.DisplayedProgress, Is.LessThanOrEqualTo(1f - ring.MinimumCoolingGapDegrees / 360f));
                if (ticks != 1) Assert.That(ring.DisplayedProgress, Is.EqualTo(progress).Within(.00001f));
            }
            Assert.That(_state.Checksum(), Is.EqualTo(checksum));
        }

        [TestCase("dash", DashReadinessKind.Dashing)]
        [TestCase("stun", DashReadinessKind.Disabled)]
        [TestCase("kickoff", DashReadinessKind.Disabled)]
        [TestCase("scored", DashReadinessKind.Disabled)]
        [TestCase("dead", DashReadinessKind.Hidden)]
        public void ZeroCooldownDoesNotClaimEligibilityWhileOtherRulesPreventDash(string condition, DashReadinessKind expected)
        {
            _state.Players[0].DashCooldownTicks = 0;
            if (condition == "dash") _state.Players[0].DashTicks = 18;
            if (condition == "stun") _state.Players[0].StunTicks = 36;
            if (condition == "kickoff") _state.Phase = MatchState.Kickoff;
            if (condition == "scored") _state.Phase = MatchState.Scored;
            if (condition == "dead") _state.Players[0].Alive = false;
            int checksum = _state.Checksum(); Refresh();
            Assert.That(_rings[0].State.Kind, Is.EqualTo(expected));
            Assert.That(_rings[0].DisplayedProgress, Is.Zero);
            Assert.That(_rings[0].Visible, Is.EqualTo(expected != DashReadinessKind.Hidden));
            Assert.That(_state.Checksum(), Is.EqualTo(checksum));
        }

        [Test] public void LocalViewsShareTwoWorldRingsAndHideAllFourLegacyMarkers()
        {
            _state.Players[0].DashCooldownTicks = 90; _state.Players[1].DashCooldownTicks = 0;
            Refresh(); Refresh();
            Assert.That(_binding.HasLocalGroundRings && _binding.LocalGroundRingsActive, Is.True);
            Assert.That(_root.GetComponentsInChildren<DashGroundReadinessRing>(true).Length, Is.EqualTo(2));
            Assert.That(_root.GetComponentsInChildren<Camera>(true).Length, Is.EqualTo(2));
            Assert.That(_root.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(_root.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            for (int seat = 0; seat < 2; seat++) for (int actor = 0; actor < 2; actor++)
            {
                Assert.That(_binding.MarkerFor(seat, actor).gameObject.activeSelf, Is.False);
                Assert.That(_views.GameplayCameras[seat].cullingMask & (1 << _rings[actor].gameObject.layer), Is.Not.Zero);
                Assert.That(_rings[actor].transform.IsChildOf(_views.GameplayCameras[seat].transform), Is.False);
            }
            Assert.That(_footprints[0].enabled || _footprints[1].enabled, Is.False);
        }

        [Test] public void RingFollowsGroundPositionWithoutRobotYawOrVisualBob()
        {
            Transform robot = _views.Players[0].Root, motion = _views.Players[0].Motion;
            _rings[0].GroundHeight = .073f;
            foreach (float angle in new[] { 0f, 87f, 201f })
            {
                robot.position = new Vector3(angle * .03f, 2.5f, -angle * .02f);
                robot.rotation = Quaternion.Euler(0, angle, 0);
                motion.localPosition = new Vector3(.2f, angle * .01f, -.3f);
                motion.localScale = new Vector3(.8f, 1.3f, 1.1f);
                int checksum = _state.Checksum(); Refresh();
                Assert.That(_rings[0].transform.position, Is.EqualTo(new Vector3(robot.position.x, .073f, robot.position.z)));
                Assert.That(Quaternion.Angle(_rings[0].transform.rotation, Quaternion.identity), Is.LessThan(.001f));
                Assert.That(_rings[0].transform.lossyScale.x, Is.EqualTo(_rings[0].Radius).Within(.00001f));
                foreach (Vector3 vertex in _mesh.vertices)
                    Assert.That(_rings[0].transform.TransformPoint(vertex).y, Is.EqualTo(.073f).Within(.00001f));
                Assert.That(motion.localPosition.y, Is.EqualTo(angle * .01f));
                Assert.That(_state.Checksum(), Is.EqualTo(checksum));
            }
        }

        [TestCase("lan-seat-0")] [TestCase("lan-seat-1")] [TestCase("v1")] [TestCase("disable")] [TestCase("missing-state")]
        public void LeavingLocalCandidateRestoresOriginallyEnabledFootprintOnly(string destination)
        {
            Refresh(); Refresh(); // Repeated local frames must not replace the saved enabled flags with false.
            if (destination.StartsWith("lan"))
            {
                int seat = destination.EndsWith("0") ? 0 : 1;
                _state.Players[0].DashCooldownTicks = _state.Players[1].DashCooldownTicks = 90;
                Refresh(true, seat); // Presentation branch only; deliberately no transport/session is created.
                for (int view = 0; view < 2; view++) for (int actor = 0; actor < 2; actor++)
                {
                    var marker = _binding.MarkerFor(view, actor);
                    Assert.That(marker.gameObject.activeSelf, Is.EqualTo(view == seat));
                    if (view != seat) continue;
                    Assert.That(marker.State.Detailed, Is.EqualTo(actor == seat));
                    Assert.That(marker.State.Progress, Is.EqualTo(actor == seat ? .5f : 0f));
                }
            }
            else if (destination == "v1")
            {
                _state = DashArenaSimulation.Create(ArenaRulesProfile.CreateProductionV1(), 20);
                DashArenaSimulation.StartMatch(_state); Refresh();
            }
            else if (destination == "disable") _binding.enabled = false;
            else _binding.Refresh(null, _presentation, false, 0);
            Assert.That(_binding.LocalGroundRingsActive, Is.False);
            Assert.That(_rings[0].Visible || _rings[1].Visible, Is.False);
            Assert.That(_footprints[0].enabled, Is.True); Assert.That(_footprints[1].enabled, Is.False);
            if (!destination.StartsWith("lan"))
                for (int seat = 0; seat < 2; seat++) for (int actor = 0; actor < 2; actor++)
                    Assert.That(_binding.MarkerFor(seat, actor).gameObject.activeSelf, Is.False);
        }

        [UnityTest] public IEnumerator FrozenTickAndSameTickCorrectionsAreReadOnlyAndNeverAnimateCooldownByWallTime()
        {
            _state.Players[0].DashCooldownTicks = 90; Refresh();
            float before = _rings[0].DisplayedProgress;
            int tick = _state.Tick, checksum = _state.Checksum();
            // This fixture has no director; refresh is controlled explicitly before and after the wait.
            yield return new WaitForSecondsRealtime(.06f);
            Refresh(); Assert.That(_rings[0].DisplayedProgress, Is.EqualTo(before));
            Assert.That(_state.Tick, Is.EqualTo(tick)); Assert.That(_state.Checksum(), Is.EqualTo(checksum));
            _state.Players[0].DashCooldownTicks = 0; checksum = _state.Checksum(); Refresh();
            Assert.That(_rings[0].State.Kind, Is.EqualTo(DashReadinessKind.Ready));
            Assert.That(_state.Checksum(), Is.EqualTo(checksum));
            _state.Players[0].DashCooldownTicks = 179; _views.Players[0].Root.position = new Vector3(7, 0, -3);
            checksum = _state.Checksum(); Refresh();
            Assert.That(_rings[0].State.Kind, Is.EqualTo(DashReadinessKind.Cooling));
            Assert.That(_rings[0].DisplayedProgress, Is.LessThan(.01f));
            Assert.That(_rings[0].transform.position.x, Is.EqualTo(7f)); Assert.That(_rings[0].transform.position.z, Is.EqualTo(-3f));
            Assert.That(_state.Checksum(), Is.EqualTo(checksum)); Assert.That(_state.Tick, Is.EqualTo(tick));
            _state.Players[0].Alive = false; Refresh(); Assert.That(_rings[0].Visible, Is.False);
            _state.Players[0].Alive = true; _views.Players[0].Root.position = new Vector3(-5, 0, 4);
            Refresh(); Assert.That(_rings[0].Visible, Is.True); Assert.That(_rings[0].transform.position.x, Is.EqualTo(-5f));
        }

        [Test] public void ReinstallAndRefreshPreserveAuthoredStyleSharedAssetsAndUnknownChildren()
        {
            var ring = _rings[0]; Color edited = new Color(.31f, .42f, .73f, .81f);
            ring.IdentityColor = edited; ring.TrackColor = Color.gray; ring.OutlineColor = Color.green;
            ring.Radius = 1.35f; ring.Width = .31f; ring.GroundHeight = .071f; ring.OutlineWidth = .04f;
            ring.MinimumCoolingGapDegrees = 18f;
            Transform custom = Child("Artist-owned detail", ring.transform); custom.localPosition = new Vector3(.2f, .3f, .4f);
            Color sharedColor = _material.GetColor("_IdentityColor"); float sharedProgress = _material.GetFloat("_Progress");
            Assert.That(_binding.InstallLocalGroundRings(_rings, _footprints), Is.False);
            for (int i = 0; i < 3; i++) { _state.Players[0].DashCooldownTicks = 180 - i * 70; Refresh(); }
            Assert.That(ring.IdentityColor, Is.EqualTo(edited)); Assert.That(ring.TrackColor, Is.EqualTo(Color.gray));
            Assert.That(ring.OutlineColor, Is.EqualTo(Color.green)); Assert.That(ring.OutlineWidth, Is.EqualTo(.04f));
            Assert.That(ring.Radius, Is.EqualTo(1.35f)); Assert.That(ring.Width, Is.EqualTo(.31f));
            Assert.That(ring.GroundHeight, Is.EqualTo(.071f)); Assert.That(ring.MinimumCoolingGapDegrees, Is.EqualTo(18f));
            Assert.That(custom.parent, Is.SameAs(ring.transform)); Assert.That(custom.localPosition, Is.EqualTo(new Vector3(.2f, .3f, .4f)));
            Assert.That(ring.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(_mesh)); Assert.That(ring.RingRenderer.sharedMaterial, Is.SameAs(_material));
            Assert.That(_material.GetColor("_IdentityColor"), Is.EqualTo(sharedColor)); Assert.That(_material.GetFloat("_Progress"), Is.EqualTo(sharedProgress));
        }

        [Test] public void SharedMaterialRendersEachActorsOwnColourInBothCameras()
        {
            PreparePixelCameras(); Refresh();
            foreach (Camera camera in _views.GameplayCameras)
            {
                Color32[] pixels = RenderPixels(camera);
                Assert.That(CountColour(pixels, true), Is.GreaterThan(100), "Both cameras must see the same first player's world ring.");
                Assert.That(CountColour(pixels, false), Is.GreaterThan(100), "Per-renderer property blocks must not colour both rings identically.");
            }
        }

        [Test] public void RenderedCoolingRingRetainsVisibleGapUntilReady()
        {
            PreparePixelCameras(); _state.Players[1].Alive = false; _views.Players[0].Root.position = Vector3.zero;
            Camera camera = _views.GameplayCameras[0]; camera.orthographicSize = 1.6f;
            int[] bright = new int[4]; int[] ticks = { 180, 90, 1, 0 };
            for (int i = 0; i < ticks.Length; i++)
            {
                _state.Players[0].DashCooldownTicks = ticks[i]; Refresh(); bright[i] = CountColour(RenderPixels(camera), true);
            }
            Assert.That(bright[3], Is.GreaterThan(500), "The shader must produce a visible charged ring, not just update a field.");
            Assert.That(bright[0], Is.Zero);
            Assert.That(bright[1], Is.InRange(bright[3] * .4f, bright[3] * .6f));
            Assert.That(bright[2], Is.GreaterThan(bright[1]));
            Assert.That(bright[3] - bright[2], Is.GreaterThan(50), "One remaining cooldown tick still needs an actual visible opening.");
        }

        [Test] public void WorldRingUsesOrdinaryDepthOcclusionAndCanBeSeenAgainAfterOccluderIsHidden()
        {
            PreparePixelCameras(); _state.Players[1].Alive = false; _views.Players[0].Root.position = Vector3.zero;
            Camera camera = _views.GameplayCameras[0]; camera.orthographicSize = 1.6f; Refresh();
            int visible = CountColour(RenderPixels(camera), true); Assert.That(visible, Is.GreaterThan(500));
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.transform.SetParent(_root.transform, false);
            Object.DestroyImmediate(blocker.GetComponent<Collider>()); blocker.layer = 31;
            blocker.transform.position = new Vector3(0, 1, 0); blocker.transform.localScale = new Vector3(3, 1, 3);
            Shader opaque = Shader.Find("Universal Render Pipeline/Unlit"); Assert.That(opaque, Is.Not.Null);
            var surface = new Material(opaque); surface.SetColor("_BaseColor", Color.black);
            blocker.GetComponent<Renderer>().sharedMaterial = surface;
            try
            {
                Assert.That(CountColour(RenderPixels(camera), true), Is.Zero, "A ground ring must not draw through an opaque obstacle like an overlay.");
                blocker.SetActive(false);
                Assert.That(CountColour(RenderPixels(camera), true), Is.EqualTo(visible));
            }
            finally { Object.DestroyImmediate(surface); }
        }

        [Test] public void FinalCooldownGapRemainsVisibleInFrontOfAnOpaqueRobotSizedBody()
        {
            PreparePixelCameras(); _state.Players[1].Alive = false; _views.Players[0].Root.position = Vector3.zero;
            Camera camera = _views.GameplayCameras[0]; camera.orthographicSize = 1.6f;
            camera.transform.position = new Vector3(0, 8, -8);
            camera.transform.rotation = Quaternion.LookRotation(-camera.transform.position);
            Refresh(); int unobstructed = CountColour(RenderPixels(camera), true);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(_views.Players[0].Motion, false); body.layer = 31;
            // Unity's unit capsule is radius .5 / height 2: this matches the robot's
            // approximate .78 radius / 1.8 height and puts its base on the ground.
            body.transform.localPosition = new Vector3(0, .9f, 0);
            body.transform.localScale = new Vector3(1.56f, .9f, 1.56f);
            Shader opaque = Shader.Find("Universal Render Pipeline/Unlit"); Assert.That(opaque, Is.Not.Null);
            var surface = new Material(opaque); surface.SetColor("_BaseColor", Color.black);
            body.GetComponent<Renderer>().sharedMaterial = surface;
            try
            {
                _state.Players[0].DashCooldownTicks = 0; Refresh();
                int ready = CountColour(RenderPixels(camera), true);
                Assert.That(ready, Is.GreaterThan(500));
                Assert.That(unobstructed - ready, Is.GreaterThan(100), "The body must actually occlude the far side of the ground ring.");
                _state.Players[0].DashCooldownTicks = 1; Refresh();
                int nearlyReady = CountColour(RenderPixels(camera), true);
                Assert.That(ready - nearlyReady, Is.GreaterThan(50),
                    "The last cooldown opening must survive robot-body occlusion from the south-facing gameplay camera; a north-side gap looks fully charged.");
            }
            finally { Object.DestroyImmediate(surface); }
        }

        void PreparePixelCameras()
        {
            foreach (Canvas canvas in _hud.GetComponentsInChildren<Canvas>(true)) canvas.enabled = false;
            foreach (Transform node in _root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 31;
            for (int seat = 0; seat < 2; seat++)
            {
                Camera camera = _views.GameplayCameras[seat]; camera.enabled = false;
                camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.rect = new Rect(0, 0, 1, 1); camera.aspect = 1; camera.orthographicSize = 3.5f;
                camera.nearClipPlane = .1f; camera.farClipPlane = 30; camera.allowHDR = false; camera.allowMSAA = false;
                camera.transform.position = seat == 0 ? new Vector3(0, 10, 0) : new Vector3(0, 10, -4);
                camera.transform.rotation = seat == 0 ? Quaternion.Euler(90, 0, 0) : Quaternion.LookRotation(-camera.transform.position);
            }
        }
        static Color32[] RenderPixels(Camera camera)
        {
            RenderTexture previous = RenderTexture.active, previousTarget = camera.targetTexture;
            RenderTexture target = RenderTexture.GetTemporary(256, 256, 24);
            var texture = new Texture2D(256, 256, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); texture.Apply(); return texture.GetPixels32();
            }
            finally
            {
                camera.targetTexture = previousTarget; RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(texture);
            }
        }
        static int CountColour(Color32[] pixels, bool red)
        {
            int count = 0;
            foreach (Color32 pixel in pixels)
                if (red ? pixel.r > 180 && pixel.g < 40 && pixel.b < 40 : pixel.b > 180 && pixel.r < 40 && pixel.g < 40) count++;
            return count;
        }
    }
}
