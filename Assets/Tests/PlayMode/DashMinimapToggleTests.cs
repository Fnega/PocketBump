using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PocketBump.Tests
{
    // Local presentation fixtures. Staging a LAN Kind only checks the view boundary;
    // these tests never construct, connect or pump a network session.
    public sealed class DashMinimapToggleTests
    {
        GameObject _root;
        DashMatchDirector _director;
        DashMatchHud _hud;
        float _fixedDeltaTime;

        [SetUp]
        public void Setup()
        {
            _fixedDeltaTime = Time.fixedDeltaTime;
            _root = new GameObject("Local minimap toggle fixture");
            _hud = DashMatchHud.Build(_root.transform, DashUiTheme.Load().Font);
            _director = LocalDirector("Local director");
            _director.Configure(null, _hud);
        }

        [TearDown]
        public void Teardown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            Time.fixedDeltaTime = _fixedDeltaTime;
        }

        DashMatchDirector LocalDirector(string name)
        {
            var child = new GameObject(name); child.transform.SetParent(_root.transform);
            var director = child.AddComponent<DashMatchDirector>();
            director.AutomaticTicking = false;
            director.InputOverride = (seat, tick, state) => default;
            director.InitializeLocal(ArenaRulesProfile.CreateProductionV1(), 521);
            director.Sim.Phase = MatchState.Live;
            return director;
        }

        void AssertLocalMaps(bool visible)
        {
            Assert.That(_hud.LocalMinimapsVisible, Is.EqualTo(visible));
            foreach (DashMatchHud.SeatView seat in _hud.Seats)
            {
                Assert.That(seat.Region.gameObject.activeInHierarchy, Is.True);
                Assert.That(seat.Minimap.gameObject.activeInHierarchy, Is.EqualTo(visible));
                Assert.That(seat.Minimap.transform.Find("North").gameObject.activeInHierarchy, Is.EqualTo(visible));
                Assert.That(seat.Status.gameObject.activeInHierarchy, Is.True);
            }
        }

        [Test]
        public void BothLocalMapsToggleTogetherWithoutChangingWorldOrOtherHud()
        {
            AssertLocalMaps(true);
            int before = _director.Sim.Checksum(), tick = _director.Sim.Tick;
            Assert.That(_director.TryToggleLocalMinimaps(), Is.True);
            AssertLocalMaps(false);
            for (int frame = 0; frame < 8; frame++) _hud.RefreshFromDirector();
            AssertLocalMaps(false);
            Assert.That(_director.Sim.Checksum(), Is.EqualTo(before));
            Assert.That(_director.Sim.Tick, Is.EqualTo(tick));
            Assert.That(_director.MenuOpen || _director.NetworkPaused, Is.False);
            Assert.That(_director.TryToggleLocalMinimaps(), Is.True);
            AssertLocalMaps(true);
            Assert.That(_director.Sim.Checksum(), Is.EqualTo(before));
            Assert.That(_director.PresentationErrorCount, Is.Zero);
        }

        [Test]
        public void RegularBindAndGoalKickoffKeepChoiceButRematchResetsToVisible()
        {
            _director.TryToggleLocalMinimaps();
            _hud.Bind(_director);
            _director.Sim.Phase = MatchState.Scored; _hud.RefreshFromDirector();
            _director.Sim.Phase = MatchState.Kickoff; _hud.RefreshFromDirector();
            AssertLocalMaps(false);
            _director.Sim.Scores[0] = _director.Rules.PointsToWin;
            _director.Sim.Phase = MatchState.MatchOver;
            int previousEpoch = _director.MatchEpoch;
            Assert.That(_director.RequestRematch(), Is.True);
            Assert.That(_director.MatchEpoch, Is.EqualTo(previousEpoch + 1));
            AssertLocalMaps(true);
        }

        [Test]
        public void NewLocalDirectorShowsBothMapsAndOldDirectorCannotToggleItsHud()
        {
            _director.TryToggleLocalMinimaps(); AssertLocalMaps(false);
            DashMatchDirector next = LocalDirector("Next local match");
            next.Configure(null, _hud);
            AssertLocalMaps(true);
            Assert.That(_director.TryToggleLocalMinimaps(), Is.False);
            Assert.That(next.TryToggleLocalMinimaps(), Is.True);
            AssertLocalMaps(false);
        }

        [Test]
        public void MenuAndLeavingBlockToggleWithoutQueuingIt()
        {
            _director.SetMenuOpen(true);
            int before = _director.Sim.Checksum();
            Assert.That(_director.TryToggleLocalMinimaps(), Is.False);
            AssertLocalMaps(true);
            _director.SetMenuOpen(false); _hud.RefreshFromDirector();
            AssertLocalMaps(true);
            Assert.That(_director.Sim.Checksum(), Is.EqualTo(before));
            Assert.That(_director.TryToggleLocalMinimaps(), Is.True); AssertLocalMaps(false);
            _director.LeaveNetwork(); // Existing local teardown, no Socket is present.
            Assert.That(_director.LanSession, Is.Null);
            Assert.That(_director.TryToggleLocalMinimaps(), Is.False);
            AssertLocalMaps(false);
        }

        [Test]
        public void HiddenMapsKeepCurrentCorrectedStateAndPreserveAuthoredStyles()
        {
            DashArenaMinimap map = _hud.Seats[0].Minimap;
            Color authored = new Color(.17f, .31f, .52f, .83f);
            map.FieldColor = authored;
            map.RectTransform.anchoredPosition = new Vector2(-22, -79);
            map.RectTransform.sizeDelta = new Vector2(178, 266);
            var artist = new GameObject("Artist minimap child"); artist.transform.SetParent(map.transform, false);
            _director.TryToggleLocalMinimaps();
            int tick = _director.Sim.Tick;
            _director.Sim.Players[0].Position = new Vector2(7, 9);
            _director.Sim.Players[1].Alive = false;
            int corrected = _director.Sim.Checksum();
            _hud.RefreshFromDirector();
            Assert.That(map.State, Is.SameAs(_director.Sim));
            Assert.That(map.ActivePlayerCount, Is.EqualTo(1));
            _director.TryToggleLocalMinimaps();
            Assert.That(map.State.Players[0].Position, Is.EqualTo(new Vector2(7, 9)));
            Assert.That(map.FieldColor, Is.EqualTo(authored));
            Assert.That(map.RectTransform.anchoredPosition, Is.EqualTo(new Vector2(-22, -79)));
            Assert.That(map.RectTransform.sizeDelta, Is.EqualTo(new Vector2(178, 266)));
            Assert.That(artist.transform.parent, Is.SameAs(map.transform));
            Assert.That(_hud.GetComponentsInChildren<DashArenaMinimap>(true).Length, Is.EqualTo(2));
            Assert.That(_director.Sim.Tick, Is.EqualTo(tick));
            Assert.That(_director.Sim.Checksum(), Is.EqualTo(corrected));
        }

        [TestCase(PlayKind.HostLan)]
        [TestCase(PlayKind.JoinLan)]
        public void LocalChoiceDoesNotCarryIntoStagedLanOrChangeItsAuthoredMapState(PlayKind kind)
        {
            // A separate HUD captures one deliberately disabled authored map before
            // local ownership. The normal local view still starts with both visible.
            var hud = DashMatchHud.Build(_root.transform, DashUiTheme.Load().Font);
            hud.Seats[1].Minimap.gameObject.SetActive(false);
            _director.Configure(null, hud);
            Assert.That(hud.Seats[1].Minimap.gameObject.activeSelf, Is.True);
            _director.TryToggleLocalMinimaps();
            DashMatchDirector lanView = LocalDirector("Staged LAN view only");
            typeof(DashMatchDirector).GetField("<Kind>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(lanView, kind);
            int before = lanView.Sim.Checksum();
            lanView.Configure(null, hud);
            Assert.That(lanView.LanSession, Is.Null);
            Assert.That(lanView.TryToggleLocalMinimaps(), Is.False);
            Assert.That(hud.ToggleLocalMinimaps(), Is.False);
            Assert.That(hud.Seats[0].Minimap.gameObject.activeSelf, Is.True);
            Assert.That(hud.Seats[1].Minimap.gameObject.activeSelf, Is.False);
            Assert.That(lanView.Sim.Checksum(), Is.EqualTo(before));
        }

        [TestCase(1280, 720)]
        [TestCase(1920, 1080)]
        [TestCase(1024, 768)]
        public void HidingMapsDoesNotReframeCameraOrRearrangeHud(int width, int height)
        {
            var cameraObject = new GameObject("Safe viewport query camera"); cameraObject.transform.SetParent(_root.transform);
            Camera camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24);
            camera.targetTexture = target;
            try
            {
                Canvas.ForceUpdateCanvases();
                Rect before = _hud.GetGoalSafeViewport(camera);
                Vector2 mapPosition = _hud.Seats[0].Minimap.RectTransform.anchoredPosition;
                Vector2 mapSize = _hud.Seats[0].Minimap.RectTransform.sizeDelta;
                Vector3 cameraPosition = camera.transform.position;
                _director.TryToggleLocalMinimaps(); _hud.RefreshFromDirector(); Canvas.ForceUpdateCanvases();
                Assert.That(_hud.GetGoalSafeViewport(camera), Is.EqualTo(before));
                Assert.That(_hud.Seats[0].Minimap.RectTransform.anchoredPosition, Is.EqualTo(mapPosition));
                Assert.That(_hud.Seats[0].Minimap.RectTransform.sizeDelta, Is.EqualTo(mapSize));
                Assert.That(camera.transform.position, Is.EqualTo(cameraPosition));
            }
            finally { camera.targetTexture = null; RenderTexture.ReleaseTemporary(target); }
        }
    }
}
