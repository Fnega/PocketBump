using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace PocketBump.Tests
{
    public sealed partial class PocketBumpDashPresentationTests
    {
        readonly List<Renderer>[] _goalCameraGeometry = { new List<Renderer>(), new List<Renderer>() };
        readonly Renderer[] _goalCameraBodies = new Renderer[2];

        void BuildGoalCameraGeometry(ArenaRulesProfile rules)
        {
            for (int seat = 0; seat < 2; seat++)
            {
                _goalCameraGeometry[seat].Clear();
                int sign = seat == 0 ? 1 : -1;
                _goalCameraGeometry[seat].Add(GoalCameraCube(_root.transform, "Goal mouth " + sign,
                    new Vector3(0f, -.05f, sign * (rules.HalfHeight + .7f)), new Vector3(rules.GoalWidth, .15f, 1.4f)));
                for (int side = -1; side <= 1; side += 2)
                    _goalCameraGeometry[seat].Add(GoalCameraCube(_root.transform, "Goal post " + sign + " " + side,
                        new Vector3(side * (rules.GoalWidth * .5f + .18f), 1.25f, sign * (rules.HalfHeight + .2f)),
                        new Vector3(.36f, 2.5f, .6f)));
                _goalCameraGeometry[seat].Add(GoalCameraCube(_root.transform, "Goal lintel " + sign,
                    new Vector3(0f, 2.42f, sign * (rules.HalfHeight + .2f)), new Vector3(rules.GoalWidth + .72f, .36f, .6f)));
                _goalCameraBodies[seat] = GoalCameraCube(_views.Players[seat].Motion, "Camera-test authored robot body",
                    new Vector3(0f, .95f, 0f), new Vector3(1.2f, 1.9f, 1.1f));
            }
        }

        Renderer GoalCameraCube(Transform parent, string name, Vector3 at, Vector3 scale)
        {
            GameObject item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
            item.name = name; item.transform.SetParent(parent, false);
            item.transform.localPosition = at; item.transform.localScale = scale;
            Renderer renderer = item.GetComponent<Renderer>(); renderer.sharedMaterial = _authoredMaterial;
            return renderer;
        }

        static IEnumerable<TestCaseData> GoalViewportCases()
        {
            foreach (Vector2Int size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(1024, 768) })
                foreach (bool lan in new[] { false, true })
                    for (int seat = 0; seat < 2; seat++)
                        for (int sign = -1; sign <= 1; sign += 2)
                            yield return new TestCaseData(size.x, size.y, lan, seat, sign)
                                .SetName("NearGoal_" + size.x + "x" + size.y + "_" + (lan ? "Lan" : "Local") + seat + "_End" + sign);
        }

        [TestCaseSource(nameof(GoalViewportCases))]
        public void CompleteGoalAndRobotFitActualHudAtEverySupportedViewport(int width, int height, bool lan, int seat, int endSign)
        {
            WithGoalViewport(width, height, lan, seat, (camera, uiCamera) =>
            {
                float lateralLimit = _state.Rules.GoalWidth * .5f + _state.Rules.PlayerRadius;
                foreach (float distance in new[] { 8f, 4f, .25f })
                    foreach (float lateral in new[] { -lateralLimit, 0f, lateralLimit })
                    {
                        _state.Players[seat].Position = new Vector2(lateral, endSign * (_state.Rules.HalfHeight - distance));
                        _state.Players[seat].Facing = endSign * Vector2.up;
                        int checksum = _state.Checksum();
                        _presentation.ReconcileState(_state); _presentation.Render(_state, 1f);
                        string label = "end=" + endSign + " distance=" + distance + " lateral=" + lateral;
                        Rect safe = _presentation.GoalSafeViewport(camera);
                        foreach (Renderer part in _goalCameraGeometry[endSign > 0 ? 0 : 1])
                        {
                            AssertGoalBoundsVisible(camera, part.bounds, safe, label + " " + part.name);
                            AssertGoalDoesNotCoverHud(camera, uiCamera, part.bounds, seat, label);
                        }
                        AssertGoalBoundsVisible(camera, _goalCameraBodies[seat].bounds, safe, label + " robot body");
                        AssertGoalDoesNotCoverHud(camera, uiCamera, _goalCameraBodies[seat].bounds, seat, label + " robot body");
                        Assert.That(camera.orthographicSize, Is.EqualTo(10.5f));
                        Assert.That(_state.Checksum(), Is.EqualTo(checksum), "Framing never changes the gameplay world.");
                        AssertNorthUp(camera);
                        Assert.That(_views.GameplayCameras[0].GetComponent<AudioListener>().enabled, Is.True);
                        Assert.That(_root.GetComponentsInChildren<AudioListener>(true).Length, Is.EqualTo(1));
                    }
            });
        }

        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(true, 0)]
        [TestCase(true, 1)]
        public void EndZoneCorrectionIsContinuousAndMidfieldKeepsItsFollowScale(bool lan, int seat)
        {
            WithGoalViewport(1024, 768, lan, seat, (camera, uiCamera) =>
            {
                foreach (int sign in new[] { -1, 1 })
                {
                    Vector3 last = default;
                    for (int step = 0; step <= 180; step++)
                    {
                        _state.Players[seat].Position = new Vector2(0f, sign * (18f + step * .1f));
                        _presentation.ReconcileState(_state); _presentation.Render(_state, 1f);
                        if (step > 0) Assert.That(Vector3.Distance(last, camera.transform.position), Is.LessThan(.45f),
                            "Crossing a goal blend boundary cannot snap the camera.");
                        last = camera.transform.position;
                    }
                }
                _state.Players[seat].Position = new Vector2(3f, 0f);
                _presentation.ReconcileState(_state); _presentation.Render(_state, 1f);
                Assert.That(camera.transform.position.x, Is.EqualTo(3f).Within(.001f));
                Assert.That(camera.transform.position.z, Is.EqualTo(-12f).Within(.001f));
                Assert.That(camera.orthographicSize, Is.EqualTo(10.5f));
                Assert.That(camera.WorldToViewportPoint(new Vector3(0f, 0f, _state.Rules.HalfHeight)).y, Is.GreaterThan(1f),
                    "The main view is still local; distant goals belong on the minimap.");
            });
        }

        [TestCase(DashArenaEventKind.Teleport)]
        [TestCase(DashArenaEventKind.Respawned)]
        public void GoalFramingSnapsAcrossTransitionsAndIgnoresVisualDeathHeight(DashArenaEventKind kind)
        {
            WithGoalViewport(1280, 720, true, 1, (camera, uiCamera) =>
            {
                _state.Players[1].Position = new Vector2(0f, -32f);
                _presentation.ReconcileState(_state); _presentation.Render(_state, 1f);
                _presentation.CapturePreviousPose(_state);
                _state.Players[1].Position = new Vector2(0f, 32f);
                _presentation.ApplyTransitions(new[] { new DashArenaEvent(_state.Tick, _state.Epoch, 1, 1, kind, _state.Players[1].Position) });
                _presentation.Render(_state, .1f);
                Vector3 arrived = camera.transform.position;
                _presentation.Render(_state, 1f);
                Assert.That(camera.transform.position, Is.EqualTo(arrived), "No stale interpolated camera path crosses the arena.");
                foreach (Renderer part in _goalCameraGeometry[0])
                    AssertGoalBoundsVisible(camera, part.bounds, _presentation.GoalSafeViewport(camera), kind.ToString());
                _state.Players[1].Alive = false; _state.Players[1].RespawnTicks = 180;
                _views.Players[1].Root.position += Vector3.down * 30f;
                _presentation.Render(_state, 1f);
                Assert.That(camera.transform.position, Is.EqualTo(arrived));
                Assert.That(camera.transform.position.y, Is.EqualTo(20f));
                Assert.That(_views.Players[1].Root.gameObject.activeSelf, Is.False);
                _state.Players[1].Alive = true; _state.Players[1].Position = _state.Rules.SpawnForSeat(1);
                _presentation.ApplyTransitions(new[] { new DashArenaEvent(_state.Tick, _state.Epoch, 1, 2,
                    DashArenaEventKind.Respawned, _state.Players[1].Position) });
                _presentation.Render(_state, .1f);
                Assert.That(camera.transform.position.z, Is.EqualTo(_state.Players[1].Position.y - 12f).Within(.001f));
                AssertNorthUp(camera);
            });
        }

        [TestCase(DashArenaEventKind.Teleport, -1)]
        [TestCase(DashArenaEventKind.Teleport, 1)]
        [TestCase(DashArenaEventKind.Respawned, -1)]
        [TestCase(DashArenaEventKind.Respawned, 1)]
        public void OneUnitEndZoneTransitionResetsCameraInterpolationWithoutLargeJumpFallback(DashArenaEventKind kind, int endSign)
        {
            WithGoalViewport(1280, 720, true, 1, (camera, uiCamera) =>
            {
                // Both endpoints lie in the spatial blend. One unit is deliberately
                // below Render's three-unit discontinuity fallback, so the event path
                // must reset history rather than accidentally passing on that fallback.
                _state.Players[1].Position = new Vector2(0f, endSign * 24f);
                _presentation.ReconcileState(_state); _presentation.Render(_state, 1f);
                Vector3 before = camera.transform.position;
                _presentation.CapturePreviousPose(_state);
                _state.Players[1].Position += endSign * Vector2.up;
                int checksum = _state.Checksum();
                _presentation.ApplyTransitions(new[] { new DashArenaEvent(_state.Tick, _state.Epoch, 1, 3, kind, _state.Players[1].Position) });
                _presentation.Render(_state, .1f);
                Vector3 after = camera.transform.position;
                Assert.That(_views.Players[1].Root.position.z, Is.EqualTo(_state.Players[1].Position.y).Within(.0001f));
                Assert.That(Vector3.Distance(before, after), Is.GreaterThan(.1f), "The changed end-zone framing is actually exercised.");
                _presentation.Render(_state, 1f);
                Assert.That(camera.transform.position, Is.EqualTo(after), "An event reset cannot leave a partial camera interpolation behind.");
                Assert.That(_state.Checksum(), Is.EqualTo(checksum));
                AssertNorthUp(camera);
            });
        }

        [Test]
        public void GoalFramingReadsAuthoredRendererBoundsNotOnlyRulesOrCentre()
        {
            WithGoalViewport(1920, 1080, true, 0, (camera, uiCamera) =>
            {
                Renderer lintel = _goalCameraGeometry[0][3];
                lintel.transform.localScale += new Vector3(.8f, .4f, .4f);
                lintel.transform.position += new Vector3(.25f, .4f, .2f);
                _state.Players[0].Position = new Vector2(0f, 31f);
                _presentation.ReconcileState(_state); _presentation.Render(_state, 1f);
                Assert.That(_presentation.GoalVisualBounds(1).max.y, Is.EqualTo(lintel.bounds.max.y).Within(.001f));
                foreach (Renderer part in _goalCameraGeometry[0])
                {
                    AssertGoalBoundsVisible(camera, part.bounds, _presentation.GoalSafeViewport(camera), "authored edit");
                    AssertGoalDoesNotCoverHud(camera, uiCamera, part.bounds, 0, "authored edit");
                }
                AssertGoalBoundsVisible(camera, _goalCameraBodies[0].bounds, _presentation.GoalSafeViewport(camera), "edited goal + robot");
            });
        }

        void WithGoalViewport(int width, int height, bool lan, int seat, Action<Camera, Camera> check)
        {
            _state.Phase = MatchState.Live;
            _presentation.BindState(_state, lan, seat);
            var target = new RenderTexture(width, height, 24);
            Camera uiCamera = Child(_root.transform, "Goal projection fixture UI camera").gameObject.AddComponent<Camera>();
            uiCamera.enabled = false; uiCamera.orthographic = true; uiCamera.targetTexture = target;
            uiCamera.transform.position = new Vector3(10000f, 10000f, -10000f);
            uiCamera.nearClipPlane = .1f; uiCamera.farClipPlane = 100f;
            Canvas canvas = _hud.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = uiCamera; canvas.planeDistance = 1f;
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    Camera view = _views.GameplayCameras[i];
                    view.targetTexture = target; view.aspect = (float)width / height * (lan ? 1f : .5f);
                    var region = _hud.Seats[i].Region;
                    region.gameObject.SetActive(!lan || i == seat);
                    region.anchorMin = lan ? Vector2.zero : new Vector2(i * .5f, 0f);
                    region.anchorMax = lan ? Vector2.one : new Vector2((i + 1) * .5f, 1f);
                    region.offsetMin = region.offsetMax = Vector2.zero;
                    _hud.Seats[i].PickupPanel.SetActive(true);
                }
                Canvas.ForceUpdateCanvases();
                canvas.GetComponent<CanvasScaler>().SendMessage("Handle", SendMessageOptions.DontRequireReceiver);
                Canvas.ForceUpdateCanvases();
                Assert.That(canvas.pixelRect.width, Is.EqualTo(width).Within(1f));
                Assert.That(canvas.pixelRect.height, Is.EqualTo(height).Within(1f));
                check(_views.GameplayCameras[lan ? 0 : seat], uiCamera);
            }
            finally
            {
                foreach (Camera view in _views.GameplayCameras) view.targetTexture = null;
                canvas.worldCamera = null; canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                uiCamera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(uiCamera.gameObject);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        static void AssertNorthUp(Camera camera)
        {
            Vector3 focus = camera.transform.position - new Vector3(0f, 20f, -12f);
            Vector3 center = camera.WorldToViewportPoint(focus), north = camera.WorldToViewportPoint(focus + Vector3.forward);
            Assert.That(north.y, Is.GreaterThan(center.y));
            Assert.That(north.x, Is.EqualTo(center.x).Within(.0001f));
            Assert.That(camera.transform.position.y, Is.EqualTo(20f));
        }

        static IEnumerable<Vector3> GoalBoundsCorners(Bounds bounds)
        {
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                        yield return bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
        }

        static void AssertGoalBoundsVisible(Camera camera, Bounds bounds, Rect safe, string label)
        {
            foreach (Vector3 corner in GoalBoundsCorners(bounds))
            {
                Vector3 point = camera.WorldToViewportPoint(corner);
                Assert.That(point.z, Is.GreaterThan(camera.nearClipPlane), label);
                Assert.That(point.x, Is.InRange(0f, 1f), label + " actual viewport horizontal");
                Assert.That(point.y, Is.InRange(0f, 1f), label + " actual viewport vertical");
                Assert.That(point.x, Is.InRange(safe.xMin - .0001f, safe.xMax + .0001f), label + " horizontal " + point);
                Assert.That(point.y, Is.InRange(safe.yMin - .0001f, safe.yMax + .0001f), label + " vertical " + point);
            }
        }

        void AssertGoalDoesNotCoverHud(Camera camera, Camera uiCamera, Bounds bounds, int seat, string label)
        {
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
            foreach (Vector3 corner in GoalBoundsCorners(bounds))
            {
                Vector3 viewport = camera.WorldToViewportPoint(corner);
                Vector2 pixel = camera.pixelRect.position + Vector2.Scale(new Vector2(viewport.x, viewport.y), camera.pixelRect.size);
                min = Vector2.Min(min, pixel); max = Vector2.Max(max, pixel);
            }
            Rect subject = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            RectTransform region = _hud.Seats[seat].Region;
            foreach (RectTransform panel in new[]
            {
                region.Find("StatusPanel") as RectTransform,
                _hud.Seats[seat].PickupPanel.transform as RectTransform,
                region.Find("Minimap") as RectTransform,
                _hud.transform.Find("Score") as RectTransform,
                _hud.transform.Find("Banner") as RectTransform
            })
            {
                Assert.That(panel, Is.Not.Null, "The fixture must contain every real HUD obstruction, including its minimap.");
                var corners = new Vector3[4]; panel.GetWorldCorners(corners);
                Vector2 panelMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity), panelMax = -panelMin;
                foreach (Vector3 corner in corners)
                {
                    Vector2 pixel = RectTransformUtility.WorldToScreenPoint(uiCamera, corner);
                    panelMin = Vector2.Min(panelMin, pixel); panelMax = Vector2.Max(panelMax, pixel);
                }
                Assert.That(subject.Overlaps(Rect.MinMaxRect(panelMin.x, panelMin.y, panelMax.x, panelMax.y)), Is.False,
                    label + " must not be hidden by actual " + panel.name + " rectangle.");
            }
        }
    }
}
