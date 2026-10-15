using System;
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
    // Real loopback sessions with a manual protocol clock. The layout cases send
    // explicit valid fault fixtures; these are not physical-network acceptance.
    public sealed class DashPeerFaultHudTests
    {
        GameObject _root, _pairRoot;
        DashMatchDirector _host, _join;
        float _now, _oldScale, _oldFixed;

        [SetUp] public void Setup()
        {
            _oldScale = Time.timeScale; _oldFixed = Time.fixedDeltaTime;
            Time.timeScale = 1f;
            _root = new GameObject("Peer fault presentation fixture");
        }

        [TearDown] public void Teardown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            Time.timeScale = _oldScale; Time.fixedDeltaTime = _oldFixed;
        }

        void CreatePair()
        {
            if (_pairRoot != null) Object.DestroyImmediate(_pairRoot);
            _pairRoot = new GameObject("Session pair"); _pairRoot.transform.SetParent(_root.transform);
            _now = 0f;
            ArenaRulesProfile rules = ArenaRulesProfile.CreateProductionV1();
            _host = CreateDirector("Host"); _join = CreateDirector("Join");
            _host.InitializeLan(DashLanSession.Host(rules, 7197, 41u, () => _now));
            _join.InitializeLan(DashLanSession.Join(rules,
                new IPEndPoint(IPAddress.Loopback, _host.LanSession.LocalEndpoint.Port), () => _now, 907u));
            TMP_FontAsset font = DashUiTheme.Load().Font;
            _host.Configure(null, DashMatchHud.Build(_host.transform, font));
            _join.Configure(null, DashMatchHud.Build(_join.transform, font));
            for (int guard = 0; (_host.Sim.Tick < 12 || _join.Sim.Tick < 12) && guard < 300; guard++)
            {
                if (_host.Sim.Tick < 12) _host.AdvanceOneTick();
                if (_join.Sim.Tick < 12) _join.AdvanceOneTick();
                _host.PumpFrame(); _join.PumpFrame(); _now += .002f; Thread.Sleep(1);
                Assert.That(_host.NetworkPaused || _join.NetworkPaused, Is.False, _host.FailureReason + "/" + _join.FailureReason);
            }
            Assert.That(_host.Sim.Tick, Is.EqualTo(12)); Assert.That(_join.Sim.Tick, Is.EqualTo(12));
            for (int i = 0; i < 3; i++) { _host.PumpFrame(); _join.PumpFrame(); Thread.Sleep(1); }
        }

        DashMatchDirector CreateDirector(string name)
        {
            var go = new GameObject(name); go.transform.SetParent(_pairRoot.transform);
            var director = go.AddComponent<DashMatchDirector>();
            director.AutomaticTicking = false; director.InputOverride = (seat, tick, state) => default;
            return director;
        }

        static LanUdpPeer Socket(DashMatchDirector director) => (LanUdpPeer)typeof(DashLanSession)
            .GetField("_peer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(director.LanSession);

        static void ReceiveFailure(DashMatchDirector director)
        {
            for (int i = 0; i < 30 && !director.NetworkPaused; i++) { Thread.Sleep(1); director.PumpFrame(); }
            Assert.That(director.NetworkPaused, Is.True);
            director.Hud.RefreshFromDirector();
        }

        static void AssertFrozenControls(DashMatchDirector director)
        {
            DashMatchHud hud = director.Hud;
            Assert.That(hud.NetworkIssueVisible, Is.True);
            Assert.That(hud.NetworkWaitingVisible, Is.False);
            Assert.That(hud.NetworkIssueTitle, Is.EqualTo("NETWORK PAUSED / 网络暂停"));
            Assert.That(hud.NetworkIssueRect.anchorMin, Is.EqualTo(new Vector2(1f, 0f)));
            Assert.That(hud.NetworkIssueRect.anchorMax, Is.EqualTo(new Vector2(1f, 0f)));
            Assert.That(hud.Seats[director.LocalSeat].Minimap.gameObject.activeInHierarchy, Is.True);
            Assert.That(hud.Seats[director.LocalSeat].Minimap.State, Is.SameAs(director.Sim));
            Assert.That(hud.ResultVisible, Is.False);
            foreach (string name in new[] { "NetworkMainMenu", "NetworkOpenReports" })
            {
                Button button = hud.NetworkIssueRect.Find(name).GetComponent<Button>();
                Assert.That(button.gameObject.activeInHierarchy && button.interactable, Is.True, name);
            }
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [TestCase(0)] [TestCase(1)]
        public void SilenceTimeoutAndReceivedPeerFaultHaveDifferentPersistentDescriptions(int timeoutSeat)
        {
            CreatePair();
            DashMatchDirector local = timeoutSeat == 0 ? _host : _join;
            DashMatchDirector peer = timeoutSeat == 0 ? _join : _host;
            // Drain pending traffic, then leave this receiver silent for the real
            // five-second deadline. Its fault still travels through its real socket.
            while (Socket(local).TryRecv(out _, out _)) { }
            _now += DashLanLifecycle.DisconnectSeconds + .01f;
            local.PumpFrame(); ReceiveFailure(peer);
            Assert.That(local.LanSession.FailureOrigin, Is.EqualTo("local"));
            Assert.That(local.Hud.NetworkIssueReason, Is.EqualTo("PEER TIMEOUT"));
            Assert.That(local.Hud.NetworkIssueDetail, Is.EqualTo(DashMatchHud.PeerTimeoutDetail));
            Assert.That(local.Hud.NetworkIssueDetail, Does.Contain("No accepted peer traffic for 5 seconds"));
            Assert.That(local.Hud.NetworkIssueDetail, Is.Not.EqualTo(DashMatchHud.InputProgressTimeoutDetail));
            Assert.That(peer.LanSession.FailureOrigin, Is.EqualTo("peer"));
            Assert.That(peer.LanSession.PeerFailureReason, Is.EqualTo("PEER TIMEOUT"));
            Assert.That(peer.Hud.NetworkIssueReason, Is.EqualTo("PEER FAULT: PEER TIMEOUT"));
            Assert.That(peer.Hud.NetworkIssueReason, Does.Not.Contain("PEER LEFT"));
            Assert.That(peer.Hud.NetworkIssueDetail, Is.EqualTo(DashMatchHud.PeerReportedTimeoutDetail));

            int localHash = local.Sim.Checksum(), peerHash = peer.Sim.Checksum();
            int menuRequests = 0;
            local.Hud.MainMenuRequested += () => menuRequests++;
            peer.Hud.MainMenuRequested += () => menuRequests++;
            _now += 30f;
            for (int i = 0; i < 6; i++)
            {
                local.PumpFrame(); local.AdvanceOneTick(); peer.PumpFrame(); peer.AdvanceOneTick();
                local.Hud.RefreshFromDirector(); peer.Hud.RefreshFromDirector();
            }
            Assert.That(local.Sim.Checksum(), Is.EqualTo(localHash));
            Assert.That(peer.Sim.Checksum(), Is.EqualTo(peerHash));
            Assert.That(local.Hud.NetworkIssueReason, Is.EqualTo("PEER TIMEOUT"));
            Assert.That(peer.Hud.NetworkIssueReason, Is.EqualTo("PEER FAULT: PEER TIMEOUT"));
            AssertFrozenControls(local); AssertFrozenControls(peer);
            Assert.That(menuRequests, Is.Zero, "Failure presentation must not request navigation.");
            peer.Hud.NetworkIssueRect.Find("NetworkMainMenu").GetComponent<Button>().onClick.Invoke();
            Assert.That(menuRequests, Is.EqualTo(1), "Navigation requires the existing explicit action.");
        }

        [TestCase(0)] [TestCase(1)]
        public void VoluntaryLeaveStillShowsPeerLeftWithoutInventedFault(int leavingSeat)
        {
            CreatePair();
            DashMatchDirector leaving = leavingSeat == 0 ? _host : _join;
            DashMatchDirector remaining = leavingSeat == 0 ? _join : _host;
            leaving.LeaveNetwork(); ReceiveFailure(remaining);
            Assert.That(remaining.LanSession.FailureOrigin, Is.EqualTo("peer"));
            Assert.That(remaining.LanSession.PeerFailureReason, Is.Empty);
            Assert.That(remaining.Hud.NetworkIssueReason, Is.EqualTo("PEER LEFT"));
            Assert.That(remaining.Hud.NetworkIssueDetail, Is.EqualTo(DashMatchHud.PeerLeftDetail));
            AssertFrozenControls(remaining);
        }

        [TestCase(1280, 720)] [TestCase(1920, 1080)] [TestCase(1024, 768)]
        public void EveryValidatedPeerCauseFitsExistingPanelAndApprovedFont(int width, int height)
        {
            var cameraObject = new GameObject("Fault layout camera"); cameraObject.transform.SetParent(_root.transform);
            Camera camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true;
            camera.transform.position = new Vector3(10000, 10000, -10000);
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24);
            try
            {
                camera.targetTexture = target;
                foreach (DashPeerFaultReason reason in Enum.GetValues(typeof(DashPeerFaultReason)))
                {
                    CreatePair();
                    Assert.That(Socket(_join).Send(DashNetcode.EncodeFault(new DashPeerFaultMessage
                    {
                        MatchId = _host.MatchId, Epoch = _host.MatchEpoch, Seat = 1, Reason = reason
                    })), Is.True);
                    ReceiveFailure(_host);
                    Assert.That(_host.Hud.NetworkIssueReason, Is.EqualTo("PEER FAULT: " + DashPeerFault.ToFailureReason(reason)));
                    AssertFrozenControls(_host);
                    Canvas canvas = _host.Hud.GetComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f;
                    AssertPanelFits(_host.Hud, camera, width, height);
                }
            }
            finally { camera.targetTexture = null; RenderTexture.ReleaseTemporary(target); }
        }

        static void AssertPanelFits(DashMatchHud hud, Camera camera, int width, int height)
        {
            Canvas canvas = hud.GetComponent<Canvas>();
            Canvas.ForceUpdateCanvases();
            canvas.GetComponent<CanvasScaler>().SendMessage("Handle", SendMessageOptions.DontRequireReceiver);
            Canvas.ForceUpdateCanvases();
            Assert.That(canvas.pixelRect.width, Is.EqualTo(width).Within(.1f));
            Assert.That(canvas.pixelRect.height, Is.EqualTo(height).Within(.1f));
            var corners = new Vector3[4]; hud.NetworkIssueRect.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector3 pixel = camera.WorldToScreenPoint(corner);
                Assert.That(pixel.x, Is.InRange(width * .5f, width));
                Assert.That(pixel.y, Is.InRange(0f, height * .5f));
            }
            var mapCorners = new Vector3[4]; hud.Seats[hud.Director.LocalSeat].Minimap.RectTransform.GetWorldCorners(mapCorners);
            Assert.That(camera.WorldToScreenPoint(corners[1]).y, Is.LessThan(camera.WorldToScreenPoint(mapCorners[0]).y));
            var bannerCorners = new Vector3[4]; ((RectTransform)hud.transform.Find("Banner")).GetWorldCorners(bannerCorners);
            Assert.That(camera.WorldToScreenPoint(corners[0]).y, Is.GreaterThan(camera.WorldToScreenPoint(bannerCorners[1]).y));
            foreach (TMP_Text label in hud.NetworkIssueRect.GetComponentsInChildren<TMP_Text>())
            {
                label.ForceMeshUpdate();
                Assert.That(label.font.HasCharacters(label.text.Replace("\n", ""), out uint[] missing, false, false), Is.True, label.text);
                Assert.That(label.isTextOverflowing, Is.False, label.text);
                Bounds bounds = label.textBounds; Rect rect = label.rectTransform.rect;
                Assert.That(bounds.min.x, Is.GreaterThanOrEqualTo(rect.xMin - 1.5f), label.name);
                Assert.That(bounds.max.x, Is.LessThanOrEqualTo(rect.xMax + 1.5f), label.name);
                Assert.That(bounds.min.y, Is.GreaterThanOrEqualTo(rect.yMin - 1.5f), label.name);
                Assert.That(bounds.max.y, Is.LessThanOrEqualTo(rect.yMax + 1.5f), label.name);
            }
        }
    }
}
