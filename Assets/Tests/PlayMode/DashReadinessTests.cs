using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PocketBump.Tests
{
    // Staged presentation fixtures only; they neither simulate successful transport
    // nor replace actual visual/handfeel acceptance.
    public sealed class DashReadinessTests
    {
        GameObject _root;
        DashMatchHud _hud;
        DashReadinessHud _markers;
        DashArenaPresentation _presentation;
        DashArenaArtSample.SceneReferences _views;
        DashArenaState _state;

        [SetUp] public void SetUp()
        {
            _root = new GameObject("Read-only Dash readiness fixture");
            _hud = DashMatchHud.Build(_root.transform, DashUiTheme.Load().Font);
            _markers = _hud.GetComponent<DashReadinessHud>();
            _presentation = _root.AddComponent<DashArenaPresentation>();
            _views = new DashArenaArtSample.SceneReferences
            {
                Players = new DashArenaArtSample.RobotView[2], GameplayCameras = new Camera[2],
                Pillars = new DashArenaArtSample.PillarView[1], Walls = new DashArenaArtSample.WallView[0]
            };
            for (int seat = 0; seat < 2; seat++)
            {
                Transform actor = Child("Actor " + seat); actor.position = new Vector3(seat == 0 ? -2 : 2, 0, 0);
                _views.Players[seat] = new DashArenaArtSample.RobotView { Root = actor };
                Camera camera = Child("Camera " + seat).gameObject.AddComponent<Camera>();
                camera.orthographic = true; camera.orthographicSize = 10; camera.aspect = 1;
                camera.rect = new Rect(seat * .5f, 0, .5f, 1); camera.transform.position = new Vector3(0, 10, -10);
                camera.transform.LookAt(Vector3.zero); _views.GameplayCameras[seat] = camera;
            }
            _views.Pillars[0] = new DashArenaArtSample.PillarView { Root = Child("Pillar") };
            _presentation.Configure(_views, _hud);
            _state = DashArenaSimulation.Create(ArenaRulesProfile.CreateHandfeelCandidate(), 19);
            DashArenaSimulation.StartMatch(_state); _state.Phase = MatchState.Live;
        }
        [TearDown] public void TearDown() { if (_root != null) Object.DestroyImmediate(_root); }
        Transform Child(string name) { var go = new GameObject(name); go.transform.SetParent(_root.transform); return go.transform; }
        void Refresh(bool lan = false, int seat = 0) => _markers.Refresh(_state, _presentation, lan, seat);
        static List<UIVertex> Mesh(DashReadinessGraphic marker)
        {
            using (var mesh = new VertexHelper())
            {
                typeof(DashReadinessGraphic).GetMethod("OnPopulateMesh", BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new[] { typeof(VertexHelper) }, null).Invoke(marker, new object[] { mesh });
                var vertices = new List<UIVertex>(); mesh.GetUIVertexStream(vertices); return vertices;
            }
        }
        static void SameMesh(List<UIVertex> a, List<UIVertex> b)
        {
            Assert.That(b.Count, Is.EqualTo(a.Count));
            for (int i = 0; i < a.Count; i++) { Assert.That(b[i].position, Is.EqualTo(a[i].position)); Assert.That(b[i].color, Is.EqualTo(a[i].color)); }
        }

        [Test] public void EligibilityPriorityCannotShowReadyDuringDeathKickoffStunOrActiveDash()
        {
            _state.Players[0].DashCooldownTicks = 0;
            AssertKind(DashReadinessKind.Ready);
            _state.Players[0].DashTicks = 18; AssertKind(DashReadinessKind.Dashing);
            _state.Players[0].StunTicks = 36; AssertKind(DashReadinessKind.Disabled);
            _state.Phase = MatchState.Kickoff; AssertKind(DashReadinessKind.Disabled);
            _state.Players[0].Alive = false; AssertKind(DashReadinessKind.Hidden);
            _state.Players[0].Alive = true; _state.Players[0].StunTicks = 0; _state.Players[0].DashTicks = 0;
            foreach (MatchState phase in new[] { MatchState.Kickoff, MatchState.Scored, MatchState.MatchOver })
            { _state.Phase = phase; AssertKind(DashReadinessKind.Disabled); }
            _state.Phase = MatchState.Live; _state.Players[0].DashCooldownTicks = 90; AssertKind(DashReadinessKind.Cooling);
            Assert.That(DashReadinessState.Evaluate(_state, 0, 0).Progress, Is.EqualTo(.5f));
        }
        void AssertKind(DashReadinessKind kind)
        {
            Assert.That(DashReadinessState.Evaluate(_state, 0, 0).Kind, Is.EqualTo(kind));
            var opponent = DashReadinessState.Evaluate(_state, 0, 1);
            Assert.That(opponent.Kind, Is.EqualTo(kind == DashReadinessKind.Hidden || kind == DashReadinessKind.Ready ? kind : DashReadinessKind.Disabled));
            Assert.That(opponent.Progress, Is.EqualTo(kind == DashReadinessKind.Ready ? 1 : 0));
        }
        [Test] public void OpponentCooldowns18090And1HaveIdenticalMeshesWhileOwnerProgressChanges()
        {
            List<UIVertex> previous = null; float previousProgress = -1;
            foreach (int remaining in new[] { 180, 90, 1 })
            {
                _state.Players[0].DashCooldownTicks = _state.Players[1].DashCooldownTicks = remaining;
                int checksum = _state.Checksum(); Refresh();
                for (int seat = 0; seat < 2; seat++)
                {
                    DashReadinessGraphic own = _markers.MarkerFor(seat, seat), opponent = _markers.MarkerFor(seat, 1 - seat);
                    Assert.That(own.State.Detailed, Is.True); Assert.That(opponent.State.Detailed, Is.False);
                    Assert.That(own.State.Progress, Is.GreaterThan(previousProgress));
                    Assert.That(opponent.State.Progress, Is.Zero); Assert.That(opponent.State.Kind, Is.EqualTo(DashReadinessKind.Disabled));
                    Assert.That(own.raycastTarget || opponent.raycastTarget, Is.False);
                }
                var mesh = Mesh(_markers.MarkerFor(0, 1));
                if (previous != null) SameMesh(previous, mesh);
                previous = mesh; previousProgress = _markers.MarkerFor(0, 0).State.Progress;
                Assert.That(_state.Checksum(), Is.EqualTo(checksum));
            }
        }
        [TestCase(0)] [TestCase(1)] public void BothLanSeatsUseCameraZeroAndOnlyTheirOwnDetailedRing(int seat)
        {
            _views.GameplayCameras[0].rect = new Rect(0, 0, 1, 1);
            _views.GameplayCameras[1].enabled = false;
            _views.GameplayCameras[1].transform.position = Vector3.one * 1000;
            _state.Players[seat].DashCooldownTicks = 90;
            Refresh(true, seat);
            for (int view = 0; view < 2; view++) for (int actor = 0; actor < 2; actor++)
            {
                var marker = _markers.MarkerFor(view, actor);
                Assert.That(marker.gameObject.activeSelf, Is.EqualTo(view == seat));
                if (view != seat) continue;
                Vector3 expected = _views.GameplayCameras[0].WorldToViewportPoint(_views.Players[actor].Root.position);
                Assert.That(marker.RectTransform.anchorMin, Is.EqualTo(new Vector2(expected.x, expected.y)));
                Assert.That(marker.State.Detailed, Is.EqualTo(actor == seat));
            }
        }
        [Test] public void OffscreenBehindCameraAndPerCameraVisibilityNeverCreateEdgeMarkers()
        {
            Refresh(); Assert.That(_markers.MarkerFor(0, 1).gameObject.activeSelf, Is.True);
            _views.GameplayCameras[1].transform.position += Vector3.right * 100;
            Refresh(); Assert.That(_markers.MarkerFor(0, 1).gameObject.activeSelf, Is.True);
            Assert.That(_markers.MarkerFor(1, 0).gameObject.activeSelf, Is.False);
            _views.Players[1].Root.position = Vector3.right * 100;
            Refresh(); Assert.That(_markers.MarkerFor(0, 1).gameObject.activeSelf, Is.False);
            var camera = _views.GameplayCameras[0]; _views.Players[1].Root.position = camera.transform.position - camera.transform.forward * 5;
            Refresh(); Assert.That(_markers.MarkerFor(0, 1).gameObject.activeSelf, Is.False);
        }
        [Test] public void SolidOcclusionIsPerViewAndBrokenObstacleDoesNotRemainAnOccluder()
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(block.GetComponent<Collider>());
            block.transform.SetParent(_views.Pillars[0].Root);
            block.transform.position = new Vector3(2, 3, -3); block.transform.localScale = new Vector3(2, 7, 2);
            Refresh(); Assert.That(_markers.MarkerFor(0, 1).gameObject.activeSelf, Is.False, "A fully covered opponent must not have an overlay marker.");
            Assert.That(_markers.MarkerFor(1, 1).gameObject.activeSelf, Is.True, "The owner rule is separate from opponent visibility.");
            block.SetActive(false); Refresh(); Assert.That(_markers.MarkerFor(0, 1).gameObject.activeSelf, Is.True);
            block.SetActive(true); block.transform.localScale = Vector3.one * .2f;
            Refresh(); Assert.That(_markers.MarkerFor(0, 1).gameObject.activeSelf, Is.True, "Partially visible actors retain their status.");
        }
        [UnityTest] public IEnumerator RealTimeDoesNotAdvanceGraphicAndSameTickCorrectionsImmediatelyReplaceIt()
        {
            // No director is attached: suppress the automatic no-director HideAll so
            // the test explicitly controls refresh on an unchanged corrected snapshot.
            _markers.enabled = false;
            _state.Players[0].DashCooldownTicks = 90;
            Refresh(); var graphic = _markers.MarkerFor(0, 0); var before = Mesh(graphic);
            int tick = _state.Tick, checksum = _state.Checksum();
            yield return new WaitForSecondsRealtime(.05f);
            Refresh(); SameMesh(before, Mesh(graphic)); Assert.That(_state.Tick, Is.EqualTo(tick)); Assert.That(_state.Checksum(), Is.EqualTo(checksum));
            _state.Players[0].DashCooldownTicks = 0; Refresh(); Assert.That(graphic.State.Kind, Is.EqualTo(DashReadinessKind.Ready));
            _state.Players[0].DashCooldownTicks = 179; Refresh(); Assert.That(graphic.State.Kind, Is.EqualTo(DashReadinessKind.Cooling));
            Assert.That(_state.Tick, Is.EqualTo(tick));
            _state.Players[0].Alive = false; Refresh(); Assert.That(graphic.gameObject.activeSelf, Is.False);
            _state.Players[0].Alive = true; _views.Players[0].Root.position += Vector3.right;
            Refresh(); Assert.That(graphic.gameObject.activeSelf, Is.True);
            _state = DashArenaSimulation.Create(ArenaRulesProfile.CreateHandfeelCandidate(), 27); _state.Epoch = 2;
            DashArenaSimulation.StartMatch(_state); Refresh(); Assert.That(graphic.State.Kind, Is.EqualTo(DashReadinessKind.Disabled));
        }
        [Test] public void InstallationAndRepeatedRefreshPreserveAuthorStylesUnknownChildrenAndV1Baseline()
        {
            var marker = _markers.MarkerFor(0, 0);
            Color edited = new Color(.31f, .42f, .73f, .81f); marker.IdentityColor = edited;
            marker.RectTransform.sizeDelta = new Vector2(61, 37); marker.FootOffset = new Vector2(2, -3);
            var custom = Child("Artist custom child"); custom.SetParent(marker.transform);
            Assert.That(DashReadinessHud.Install(_hud), Is.False); Refresh();
            Assert.That(marker.IdentityColor, Is.EqualTo(edited)); Assert.That(marker.RectTransform.sizeDelta, Is.EqualTo(new Vector2(61, 37)));
            Assert.That(marker.RectTransform.anchoredPosition, Is.EqualTo(new Vector2(2, -3))); Assert.That(custom.parent, Is.SameAs(marker.transform));
            Assert.That(_hud.GetComponentsInChildren<DashReadinessGraphic>(true).Length, Is.EqualTo(4));
            _state = DashArenaSimulation.Create(ArenaRulesProfile.CreateProductionV1(), 1); DashArenaSimulation.StartMatch(_state);
            Refresh(); foreach (var graphic in _hud.GetComponentsInChildren<DashReadinessGraphic>(true)) Assert.That(graphic.gameObject.activeSelf, Is.False);
        }
        [TestCase(1280, 720)] [TestCase(1920, 1080)] [TestCase(1024, 768)]
        public void NormalizedFootAnchorsAndMasksStayOwnedByTheirViewports(int width, int height)
        {
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24);
            try
            {
                for (int seat = 0; seat < 2; seat++) _views.GameplayCameras[seat].targetTexture = target;
                Refresh();
                for (int seat = 0; seat < 2; seat++) for (int actor = 0; actor < 2; actor++)
                {
                    var marker = _markers.MarkerFor(seat, actor);
                    Assert.That(marker.transform.parent, Is.SameAs(_hud.Seats[seat].Region));
                    Assert.That(marker.GetComponentInParent<RectMask2D>(), Is.SameAs(_hud.Seats[seat].Region.GetComponent<RectMask2D>()));
                    Assert.That(marker.RectTransform.anchorMin.x, Is.InRange(0, 1)); Assert.That(marker.RectTransform.anchorMin.y, Is.InRange(0, 1));
                    Assert.That(marker.GetComponentsInChildren<Camera>(true), Is.Empty); Assert.That(marker.GetComponentsInChildren<Collider>(true), Is.Empty);
                }
            }
            finally { foreach (Camera camera in _views.GameplayCameras) camera.targetTexture = null; RenderTexture.ReleaseTemporary(target); }
        }
    }
}
