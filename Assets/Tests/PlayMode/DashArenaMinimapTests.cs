using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PocketBump.Tests
{
    // Presentation fixtures may place entities explicitly; they do not establish
    // gameplay/transport success. Mesh generation must never write those worlds.
    public sealed class DashArenaMinimapTests
    {
        GameObject _root;
        float _oldFixed;
        [SetUp] public void Setup() { _oldFixed = Time.fixedDeltaTime; _root = new GameObject("Read-only minimap fixture"); }
        [TearDown] public void Teardown() { Object.DestroyImmediate(_root); Time.fixedDeltaTime = _oldFixed; }

        DashArenaMinimap Map()
        {
            var go = new GameObject("Minimap", typeof(RectTransform)); go.transform.SetParent(_root.transform, false);
            var map = go.AddComponent<DashArenaMinimap>(); map.RectTransform.sizeDelta = new Vector2(190, 288); return map;
        }
        static DashArenaState World(uint seed = 31u)
        {
            var state = DashArenaSimulation.Create(ArenaRulesProfile.CreateProductionV1(8, 8), seed);
            DashArenaSimulation.StartMatch(state);
            // Production activates scoring balls when the real kickoff timer expires,
            // not on StartMatch. Reach that state instead of manufacturing Active flags.
            for (int i = 0; i < state.Rules.KickoffTicks; i++) DashArenaSimulation.Step(state, default, default);
            Assert.That(state.Phase, Is.EqualTo(MatchState.Live));
            return state;
        }
        static List<UIVertex> Mesh(DashArenaMinimap map)
        {
            using (var mesh = new VertexHelper())
            {
                typeof(DashArenaMinimap).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] { typeof(VertexHelper) }, null).Invoke(map, new object[] { mesh });
                var vertices = new List<UIVertex>(); mesh.GetUIVertexStream(vertices); return vertices;
            }
        }
        static int VerticesAt(List<UIVertex> vertices, Vector2 at, Color color)
        {
            int count = 0;
            foreach (UIVertex vertex in vertices)
                if (((Vector2)vertex.position - at).sqrMagnitude < .00001f && vertex.color.Equals((Color32)color)) count++;
            return count;
        }

        [Test] public void NorthUpProjectionIncludesGoalsWithUniformScaleAndNeverAddsWorldComponents()
        {
            DashArenaMinimap map = Map(); DashArenaState state = World(); map.SetState(state, 0);
            Vector2 center = map.WorldToMap(Vector2.zero);
            Assert.That(center, Is.EqualTo(map.MapRect.center));
            Vector2 east = map.WorldToMap(Vector2.right) - center, north = map.WorldToMap(Vector2.up) - center;
            Assert.That(east.x, Is.GreaterThan(0)); Assert.That(east.y, Is.Zero.Within(.001));
            Assert.That(north.y, Is.EqualTo(east.x).Within(.001)); Assert.That(north.x, Is.Zero.Within(.001));
            Assert.That(map.WorldRect.width, Is.EqualTo(54)); Assert.That(map.WorldRect.height, Is.EqualTo(78));
            foreach (float sign in new[] { -1f, 1f })
            {
                Assert.That(map.MapRect.Contains(map.WorldToMap(new Vector2(0, sign * 38.2f))), Is.True, "Goal rear margin remains mapped.");
                Assert.That(map.MapRect.Contains(map.WorldToMap(new Vector2(sign * 24, sign * 36))), Is.True);
            }
            Assert.That(map.raycastTarget, Is.False);
            Assert.That(map.GetComponentsInChildren<Camera>(true), Is.Empty);
            Assert.That(map.GetComponentsInChildren<AudioListener>(true), Is.Empty);
            Assert.That(map.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(map.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
        }

        [TestCase(0)] [TestCase(1)]
        public void MeshShowsBothFacingsAndAllEightBallsWithoutChangingAuthority(int localSeat)
        {
            DashArenaMinimap map = Map(); DashArenaState state = World();
            state.Players[0].Position = new Vector2(-15, -25); state.Players[0].Facing = Vector2.right;
            state.Players[1].Position = new Vector2(15, 25); state.Players[1].Facing = Vector2.down;
            for (int i = 0; i < state.Balls.Length; i++) state.Balls[i].Position = new Vector2(-15 + (i % 4) * 10, -12 + (i / 4) * 24);
            int checksum = state.Checksum(); map.SetState(state, localSeat); List<UIVertex> vertices = Mesh(map);
            Assert.That(map.LocalSeat, Is.EqualTo(localSeat)); Assert.That(map.ActivePlayerCount, Is.EqualTo(2)); Assert.That(map.ActiveBallCount, Is.EqualTo(8));
            foreach (DashBallState ball in state.Balls) Assert.That(VerticesAt(vertices, map.WorldToMap(ball.Position), Color.white), Is.EqualTo(16));
            Assert.That(VerticesAt(vertices, map.WorldToMap(state.Players[0].Position) + Vector2.right * 5.1f, map.PlayerOneColor), Is.EqualTo(1));
            Assert.That(VerticesAt(vertices, map.WorldToMap(state.Players[1].Position) + Vector2.down * 5.1f, map.PlayerTwoColor), Is.EqualTo(1));
            Assert.That(map.FacingToMap(Vector2.zero), Is.EqualTo(Vector2.up));
            Assert.That(vertices.Count, Is.LessThan(10000), "Bounded UGUI mesh, no per-entity scene objects.");
            Assert.That(state.Checksum(), Is.EqualTo(checksum));
        }

        [Test] public void SameTickCorrectionDeathInactiveBallsWallsAndFreshEpochRefreshImmediately()
        {
            DashArenaMinimap map = Map(); DashArenaState state = World(); map.SetState(state, 0);
            int intactVertices = Mesh(map).Count, tick = state.Tick;
            state.Players[0].Alive = false; state.Balls[0].Active = false;
            state.Walls[0].Broken = true; state.Walls[1].Broken = true; state.Walls[1].RepairPending = true;
            state.Players[1].Position = new Vector2(-17, 23);
            int corrected = state.Checksum(); map.SetState(state, 1); var vertices = Mesh(map);
            Assert.That(state.Tick, Is.EqualTo(tick)); Assert.That(state.Checksum(), Is.EqualTo(corrected));
            Assert.That(map.ActivePlayerCount, Is.EqualTo(1)); Assert.That(map.ActiveBallCount, Is.EqualTo(7));
            Assert.That(map.BrokenWallCount, Is.EqualTo(1)); Assert.That(map.PendingWallCount, Is.EqualTo(1));
            Assert.That(map.SolidWallCount, Is.EqualTo(state.Walls.Length - 2)); Assert.That(vertices.Count, Is.LessThan(intactVertices));
            Assert.That(VerticesAt(vertices, map.WorldToMap(state.Players[1].Position) + map.FacingToMap(state.Players[1].Facing) * 5.1f, map.PlayerTwoColor), Is.EqualTo(1));
            DashArenaState next = World(99); next.Epoch = 1; int nextHash = next.Checksum(); map.SetState(next, 0); Mesh(map);
            Assert.That(map.State, Is.SameAs(next)); Assert.That(next.Checksum(), Is.EqualTo(nextHash));
            Assert.That(map.ActivePlayerCount, Is.EqualTo(2)); Assert.That(map.ActiveBallCount, Is.EqualTo(8));
            Assert.That(map.PendingWallCount + map.BrokenWallCount, Is.Zero);
            map.SetState(null, 0); Mesh(map); Assert.That(map.ActivePlayerCount + map.ActiveBallCount, Is.Zero);
        }

        [Test] public void ExplicitLegacyMigrationIsIdempotentAndPreservesAuthoredColorsFontsUnknownObjectsAndLaterLayout()
        {
            DashMatchHud hud = DashMatchHud.Build(_root.transform, DashUiTheme.Load().Font);
            // Recreate a pre-change saved HUD fixture. No runtime bootstrap is used.
            foreach (DashMatchHud.SeatView seat in hud.Seats) Object.DestroyImmediate(seat.Minimap.gameObject);
            typeof(DashMatchHud).GetField("_minimapLayoutVersion", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(hud, 0);
            var status = (RectTransform)hud.Seats[0].Status.transform.parent;
            Color authored = new Color(.13f, .24f, .37f, .68f); status.GetComponent<Image>().color = authored;
            TMP_FontAsset font = hud.Seats[0].Status.font; float size = hud.Seats[0].Status.fontSize;
            Vector2 childOffset = hud.Seats[0].Status.rectTransform.offsetMin;
            var custom = new GameObject("User authored child"); custom.transform.SetParent(hud.Seats[0].Region, false);
            Assert.That(hud.InstallMinimapAndUpgradeLayout(), Is.True);
            Assert.That(hud.InstallMinimapAndUpgradeLayout(), Is.False);
            Assert.That(hud.GetComponentsInChildren<DashArenaMinimap>(true).Length, Is.EqualTo(2));
            Assert.That(status.GetComponent<Image>().color, Is.EqualTo(authored)); Assert.That(hud.Seats[0].Status.font, Is.SameAs(font));
            Assert.That(hud.Seats[0].Status.fontSize, Is.EqualTo(size)); Assert.That(hud.Seats[0].Status.rectTransform.offsetMin, Is.EqualTo(childOffset));
            Assert.That(custom != null && custom.transform.parent == hud.Seats[0].Region, Is.True);
            DashArenaMinimap map = hud.Seats[0].Minimap;
            map.FieldColor = authored; map.RectTransform.anchoredPosition = new Vector2(-21, -73); map.RectTransform.sizeDelta = new Vector2(180, 275);
            hud.NetworkIssueRect.anchoredPosition = new Vector2(-32, 83); status.offsetMax = new Vector2(-225, -65);
            Assert.That(hud.InstallMinimapAndUpgradeLayout(), Is.False);
            var director = Director("Local"); director.InitializeLocal(ArenaRulesProfile.CreateProductionV1(), 17); director.Configure(null, hud); hud.RefreshFromDirector();
            Assert.That(map.FieldColor, Is.EqualTo(authored)); Assert.That(map.RectTransform.anchoredPosition, Is.EqualTo(new Vector2(-21, -73)));
            Assert.That(map.RectTransform.sizeDelta, Is.EqualTo(new Vector2(180, 275))); Assert.That(status.offsetMax, Is.EqualTo(new Vector2(-225, -65)));
            Assert.That(hud.NetworkIssueRect.anchoredPosition, Is.EqualTo(new Vector2(-32, 83)));
        }

        [TestCase(1280, 720, -1)] [TestCase(1920, 1080, -1)] [TestCase(1024, 768, -1)]
        [TestCase(1280, 720, 0)] [TestCase(1920, 1080, 0)] [TestCase(1024, 768, 0)]
        [TestCase(1280, 720, 1)] [TestCase(1920, 1080, 1)] [TestCase(1024, 768, 1)]
        public void LocalAndBothLanSeatsKeepMapsStatusAndPickupsSeparatedAtSupportedSizes(int width, int height, int lanSeat)
        {
            DashMatchDirector director = Director("Local or host"); ArenaRulesProfile rules = ArenaRulesProfile.CreateProductionV1(8, 8);
            if (lanSeat < 0)
            {
                director.InitializeLocal(rules, 311);
                for (int i = 0; i < rules.KickoffTicks; i++) director.AdvanceOneTick();
            }
            else
            {
                float now = 0;
                director.InitializeLan(DashLanSession.Host(rules, 373, 311, () => now));
                DashMatchDirector join = Director("Join");
                join.InitializeLan(DashLanSession.Join(rules, new IPEndPoint(IPAddress.Loopback, director.LanSession.LocalEndpoint.Port), () => now, 71));
                for (int i = 0; i < 150 && (!director.ReadyForNetplay || !join.ReadyForNetplay); i++)
                { director.PumpFrame(); join.PumpFrame(); now += .01f; Thread.Sleep(1); }
                Assert.That(director.ReadyForNetplay && join.ReadyForNetplay, Is.True);
                // Advance both real session clocks using neutral production inputs.
                // Do not mutate rollback-owned state to force eight balls visible.
                for (int i = 0; i < rules.KickoffTicks * 4 + 200 &&
                    (director.State != MatchState.Live || join.State != MatchState.Live); i++)
                {
                    if (director.State != MatchState.Live) director.AdvanceOneTick();
                    if (join.State != MatchState.Live) join.AdvanceOneTick();
                    director.PumpFrame(); join.PumpFrame(); now += .01f; Thread.Sleep(1);
                    Assert.That(director.NetworkPaused || join.NetworkPaused, Is.False, director.FailureReason + "/" + join.FailureReason);
                }
                Assert.That(director.State, Is.EqualTo(MatchState.Live)); Assert.That(join.State, Is.EqualTo(MatchState.Live));
                if (lanSeat == 1) director = join;
            }
            Assert.That(director.State, Is.EqualTo(MatchState.Live));
            DashMatchHud hud = DashMatchHud.Build(_root.transform, DashUiTheme.Load().Font); director.Configure(null, hud);
            var cameraGo = new GameObject("Canvas target camera"); cameraGo.transform.SetParent(_root.transform);
            Camera camera = cameraGo.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true;
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24);
            Canvas canvas = hud.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            camera.targetTexture = target;
            try
            {
                for (int seat = 0; seat < 2; seat++) hud.ShowConfirmedPickup(new DashArenaEvent(0, 0, seat, 0, DashArenaEventKind.Pickup, Vector2.zero, 0, DashPickupKind.Green), rules);
                hud.RefreshFromDirector(); Canvas.ForceUpdateCanvases(); canvas.GetComponent<CanvasScaler>().SendMessage("Handle", SendMessageOptions.DontRequireReceiver); Canvas.ForceUpdateCanvases();
                int active = 0;
                for (int seat = 0; seat < 2; seat++)
                {
                    DashMatchHud.SeatView view = hud.Seats[seat];
                    Assert.That(view.Minimap.gameObject.activeInHierarchy, Is.EqualTo(lanSeat < 0 || seat == lanSeat));
                    if (!view.Minimap.gameObject.activeInHierarchy) continue;
                    active++; Assert.That(view.Minimap.LocalSeat, Is.EqualTo(seat)); Assert.That(view.Minimap.State, Is.SameAs(director.Sim));
                    Assert.That(view.Minimap.ActiveBallCount, Is.EqualTo(8));
                    Rect region = Pixels(view.Region, camera), map = Pixels(view.Minimap.RectTransform, camera);
                    Assert.That(map.xMin, Is.GreaterThan(region.xMin)); Assert.That(map.xMax, Is.LessThan(region.xMax));
                    Assert.That(map.yMin, Is.GreaterThan(height * .5f)); Assert.That(map.yMax, Is.LessThan(height));
                    Assert.That(Pixels(view.Status.transform.parent as RectTransform, camera).Overlaps(map), Is.False);
                    Assert.That(Pixels(view.PickupPanel.transform as RectTransform, camera).Overlaps(map), Is.False);
                    Assert.That(map.Overlaps(Pixels(hud.transform.Find("Score") as RectTransform, camera)), Is.False);
                    foreach (TMP_Text label in new[] { view.Status, view.Buffs, view.Pickup })
                    { label.ForceMeshUpdate(); Assert.That(label.isTextOverflowing, Is.False, width + "x" + height + " " + label.name + " " + label.text); }
                }
                Assert.That(active, Is.EqualTo(lanSeat < 0 ? 2 : 1));
                Assert.That(hud.GetComponentsInChildren<Camera>(true), Is.Empty);
                Assert.That(hud.GetComponentsInChildren<AudioListener>(true), Is.Empty);
                Rect safe = hud.GetGoalSafeViewport(camera);
                Assert.That(safe.xMin, Is.LessThan(.05f)); Assert.That(safe.xMax, Is.GreaterThan(.95f));
                Assert.That(safe.yMin, Is.GreaterThan(0)); Assert.That(safe.yMax, Is.InRange(.5f, .8f));
            }
            finally { camera.targetTexture = null; RenderTexture.ReleaseTemporary(target); }
        }
        DashMatchDirector Director(string name)
        {
            var go = new GameObject(name); go.transform.SetParent(_root.transform);
            var director = go.AddComponent<DashMatchDirector>(); director.AutomaticTicking = false;
            director.InputOverride = (seat, tick, state) => default;
            return director;
        }
        static Rect Pixels(RectTransform rect, Camera camera)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Vector3 a = camera.WorldToScreenPoint(corners[0]), b = camera.WorldToScreenPoint(corners[2]);
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }
    }
}
