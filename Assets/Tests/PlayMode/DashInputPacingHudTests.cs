using System;
using System.IO;
using System.Net;
using System.Threading;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PocketBump.Tests
{
    // Real loopback transport with manual fixed-step scheduling. Not natural-match
    // or physical-network evidence; HUD never receives a forged waiting flag here.
    public sealed class DashInputPacingHudTests
    {
        sealed class Keyboard : IDashKeyboardInput
        {
            public int FrameCount { get; set; }
            public KeyCode DashKey;
            public bool Pressed;
            public bool IsHeld(KeyCode key) => false;
            public bool WasPressedThisFrame(KeyCode key) => Pressed && key == DashKey;
        }
        sealed class DefaultBindingStore : IDashBindingStore
        {
            public bool HasKey(string key) => false;
            public int GetInt(string key, int fallback) => fallback;
            public void SetInt(string key, int value) => Assert.Fail("Pacing tests must not persist control bindings.");
            public void Save() => Assert.Fail("Pacing tests must not save control bindings.");
        }
        GameObject _root;
        DashMatchDirector _fast, _slow;
        DashMatchHud _hud;
        Keyboard _keyboard;
        float _now, _oldScale, _oldFixed;
        string _reportDirectory, _reportDirectoryName;
        bool _ownsReportDirectory;

        [SetUp] public void Setup()
        {
            _oldScale = Time.timeScale; _oldFixed = Time.fixedDeltaTime;
            Time.timeScale = 1f; _now = 0f;
            _ownsReportDirectory = false;
            _reportDirectoryName = "PocketBump-PacingHud-Test-" + Guid.NewGuid().ToString("N");
            _reportDirectory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), _reportDirectoryName));
            Assert.That(Directory.Exists(_reportDirectory), Is.False, "The fixture must own a new report directory.");
            Directory.CreateDirectory(_reportDirectory);
            _ownsReportDirectory = true;
            _root = new GameObject("Actual socket pacing presentation fixture");
        }
        [TearDown] public void Teardown()
        {
            try { if (_root != null) Object.DestroyImmediate(_root); }
            finally
            {
                Time.timeScale = _oldScale; Time.fixedDeltaTime = _oldFixed;
                // Reporter OnDisable writes before cleanup. Only this fixture's flat,
                // GUID-named temporary directory may be removed, never player reports.
                if (_ownsReportDirectory && _reportDirectory != null && Directory.Exists(_reportDirectory))
                {
                    string resolved = Path.GetFullPath(_reportDirectory);
                    string expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), _reportDirectoryName));
                    Assert.That(resolved, Is.EqualTo(expected));
                    Assert.That(Path.GetFileName(resolved), Is.EqualTo(_reportDirectoryName));
                    Assert.That(File.GetAttributes(resolved) & FileAttributes.ReparsePoint, Is.EqualTo((FileAttributes)0));
                    Assert.That(Directory.GetDirectories(resolved), Is.Empty);
                    Directory.Delete(resolved, true);
                    _ownsReportDirectory = false;
                }
            }
        }

        void CreatePair(int fastSeat)
        {
            ArenaRulesProfile rules = ArenaRulesProfile.CreateProductionV1();
            DashMatchDirector host = ChildDirector("Host"), join = ChildDirector("Join");
            var bindings = new DashControlBindings(new DefaultBindingStore());
            _keyboard = new Keyboard { DashKey = bindings.Dash(0) };
            var sampler = new DashTickInputSampler(bindings, _keyboard);
            host.InitializeLan(DashLanSession.Host(rules, 7193, 29u, () => _now), fastSeat == 0 ? sampler : null);
            join.InitializeLan(DashLanSession.Join(rules, new IPEndPoint(IPAddress.Loopback, host.LanSession.LocalEndpoint.Port), () => _now, 892u), fastSeat == 1 ? sampler : null);
            _fast = fastSeat == 0 ? host : join; _slow = fastSeat == 0 ? join : host;
            TMP_FontAsset font = DashUiTheme.Load().Font;
            _hud = DashMatchHud.Build(_fast.transform, font);
            _fast.Configure(null, _hud);
            DriveBothTo(220);
        }
        DashMatchDirector ChildDirector(string name)
        {
            var go = new GameObject(name); go.transform.SetParent(_root.transform);
            var director = go.AddComponent<DashMatchDirector>(); director.AutomaticTicking = false;
            // Bind before InitializeLan/BindViews: later Bind calls deliberately keep
            // the first explicit directory instead of falling back to real reports.
            go.AddComponent<DashNetworkReporter>().Bind(director, _reportDirectory);
            return director;
        }
        void DriveBothTo(int target)
        {
            for (int guard = 0; (_fast.Sim.Tick < target || _slow.Sim.Tick < target) && guard < target * 8 + 200; guard++)
            {
                if (_fast.Sim.Tick < target) _fast.AdvanceOneTick();
                if (_slow.Sim.Tick < target) _slow.AdvanceOneTick();
                _fast.PumpFrame(); _slow.PumpFrame(); _now += .002f; Thread.Sleep(1);
                Assert.That(_fast.NetworkPaused || _slow.NetworkPaused, Is.False, _fast.FailureReason + "/" + _slow.FailureReason);
            }
            Assert.That(_fast.Sim.Tick, Is.EqualTo(target)); Assert.That(_slow.Sim.Tick, Is.EqualTo(target));
            for (int i = 0; i < 3; i++) { _fast.PumpFrame(); _slow.PumpFrame(); Thread.Sleep(1); }
        }
        void EnterWait()
        {
            for (int i = 0; i < 40 && !_fast.WaitingForRemoteInput; i++)
            { _fast.AdvanceOneTick(); _fast.PumpFrame(); _slow.PumpFrame(); _now += .001f; Thread.Sleep(1); }
            Assert.That(_fast.WaitingForRemoteInput, Is.True);
            Assert.That(_fast.Sim.Tick - _fast.LanSession.RemoteContiguousInputTick,
                Is.EqualTo(DashLanSession.MaximumInputLeadTicks), "Input pacing preserves delay headroom inside the unchanged rollback history.");
            Assert.That(_fast.NetworkPaused, Is.False);
            Assert.That(_fast.Hud.NetworkNoticeVisible, Is.False);
            Assert.That(_fast.Hud.NetworkWaitingVisible, Is.False);
            Assert.That(_fast.Hud.NetworkIssueVisible, Is.False);
        }

        [TestCase(0)] [TestCase(1)]
        public void SilentActualInputWaitFreezesTicksButPumpsAndAutomaticallyResumesAfterQueuedInput(int fastSeat)
        {
            CreatePair(fastSeat); EnterWait();
            int tick = _fast.Sim.Tick, checksum = _fast.Sim.Checksum();
            int sends = _fast.LanSession.SendAttempts, receives = _fast.LanSession.ReceivedDatagrams;
            Assert.That(_hud.NetworkIssueVisible, Is.False);
            Assert.That(_hud.NetworkNoticeVisible, Is.False);
            Assert.That(_hud.NetworkIssueRect.anchorMin, Is.EqualTo(new Vector2(1f, 0f)));
            Assert.That(_hud.Seats[fastSeat].Minimap.gameObject.activeInHierarchy, Is.True);
            Assert.That(_hud.Seats[fastSeat].Minimap.State, Is.SameAs(_fast.Sim));
            int menuRequests = 0; _hud.MainMenuRequested += () => menuRequests++;
            for (int i = 0; i < 12; i++)
            {
                _now += .05f; _slow.PumpFrame(); Thread.Sleep(1); _fast.PumpFrame(); _fast.AdvanceOneTick();
            }
            Assert.That(_fast.Sim.Tick, Is.EqualTo(tick)); Assert.That(_fast.Sim.Checksum(), Is.EqualTo(checksum));
            Assert.That(_fast.LanSession.SendAttempts, Is.GreaterThan(sends));
            Assert.That(_fast.LanSession.ReceivedDatagrams, Is.GreaterThan(receives));
            Assert.That(menuRequests, Is.Zero); Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(_hud.NetworkNoticeVisible, Is.False, "Pumping a recoverable wait never opens the fault panel.");
            Button menu = _hud.transform.Find("NetworkIssuePanel/NetworkMainMenu").GetComponent<Button>();
            Assert.That(menu.gameObject.activeInHierarchy, Is.False);
            DashNetworkReport waitingReport = DashNetworkReport.Capture(_fast, "silent-wait-test");
            Assert.That(waitingReport.waiting_for_remote_input, Is.True, "Hiding the notice does not hide diagnostic state.");
            Assert.That(waitingReport.input_wait_count, Is.GreaterThan(0));
            Assert.That(waitingReport.input_wait_seconds, Is.GreaterThan(0f));
            Assert.That(waitingReport.failure_reason, Is.Empty);
            Assert.That(DashNetworkTimelineSample.Capture(_fast).waiting_for_remote_input, Is.True);
            _slow.AdvanceOneTick();
            for (int i = 0; i < 10 && _fast.WaitingForRemoteInput; i++) { Thread.Sleep(1); _fast.PumpFrame(); }
            Assert.That(_fast.WaitingForRemoteInput, Is.False, "Queued valid input resumes a silent wait from Pump alone.");
            Assert.That(_hud.NetworkNoticeVisible, Is.False); Assert.That(_fast.NetworkPaused, Is.False);
            DashNetworkReport resumedReport = DashNetworkReport.Capture(_fast, "resumed-wait-test");
            Assert.That(resumedReport.waiting_for_remote_input, Is.False);
            Assert.That(resumedReport.input_wait_count, Is.EqualTo(waitingReport.input_wait_count));
            Assert.That(resumedReport.input_wait_seconds, Is.GreaterThanOrEqualTo(waitingReport.input_wait_seconds));
            _fast.AdvanceOneTick(); Assert.That(_fast.Sim.Tick, Is.EqualTo(tick + 1));
            Assert.That(menuRequests, Is.Zero);
        }

        [TestCase(0)] [TestCase(1)]
        public void DashPressedDuringWaitSurvivesBlockedSamplesAndIsConsumedExactlyOnce(int fastSeat)
        {
            CreatePair(fastSeat); EnterWait();
            int waitingTick = _fast.Sim.Tick;
            _keyboard.FrameCount++; _keyboard.Pressed = true; _fast.PumpFrame();
            for (int i = 0; i < 6; i++) _fast.AdvanceOneTick();
            _keyboard.FrameCount++; _keyboard.Pressed = false; _fast.PumpFrame();
            Assert.That(_fast.Sim.Tick, Is.EqualTo(waitingTick));
            Assert.That(_hud.NetworkNoticeVisible, Is.False);
            _slow.AdvanceOneTick(); Thread.Sleep(1); _fast.PumpFrame();
            Assert.That(_fast.WaitingForRemoteInput, Is.False);
            int dashEvents = 0;
            _fast.ConfirmedEvents += events => { foreach (var item in events) if (item.Kind == DashArenaEventKind.DashStarted && item.Seat == fastSeat) dashEvents++; };
            DriveBothTo(waitingTick + 10);
            int inputTick = waitingTick + DashRollbackSession.DelayTicks;
            Assert.That(_fast.LanSession.Rollback.TryGetConfirmedInputs(inputTick, out DashArenaInput p0, out DashArenaInput p1), Is.True);
            Assert.That((fastSeat == 0 ? p0 : p1).DashPressed, Is.True);
            Assert.That(_fast.LanSession.Rollback.TryGetConfirmedInputs(inputTick + 1, out p0, out p1), Is.True);
            Assert.That((fastSeat == 0 ? p0 : p1).DashPressed, Is.False);
            Assert.That(dashEvents, Is.EqualTo(1)); Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(_hud.NetworkNoticeVisible, Is.False);
        }

        [TestCase(0)] [TestCase(1)]
        public void SilentInputWaitStillAllowsPersonalMenuReportsAndExplicitReturn(int fastSeat)
        {
            CreatePair(fastSeat); EnterWait();
            DashDemoMatchMenu menu = _hud.GetComponent<DashDemoMatchMenu>();
            int requests = 0; menu.MainMenuRequested += () => requests++;
            // Exercise the same menu action used by Esc without fabricating a key event.
            _fast.SetMenuOpen(true); menu.Refresh();
            Assert.That(menu.Visible, Is.True);
            Assert.That(_fast.WaitingForRemoteInput, Is.True);
            Assert.That(_hud.NetworkNoticeVisible, Is.False);
            Transform card = _hud.transform.Find("MatchMenuOverlay/MatchMenuCard");
            foreach (string name in new[] { "OpenReports", "LeaveMatch", "ContinueMatch" })
            {
                Button button = card.Find(name).GetComponent<Button>();
                Assert.That(button.gameObject.activeInHierarchy && button.interactable, Is.True, name);
            }
            Assert.That(requests, Is.Zero);
            card.Find("LeaveMatch").GetComponent<Button>().onClick.Invoke();
            Assert.That(requests, Is.EqualTo(1), "A hidden wait notice does not remove explicit menu navigation.");
        }

        [TestCase(0)] [TestCase(1)]
        public void HeartbeatsWithoutInputProgressBecomePersistentTerminalPanelNotAutoRecovery(int fastSeat)
        {
            CreatePair(fastSeat); EnterWait();
            int before = _fast.Sim.Checksum();
            for (int i = 0; i < 60 && !_fast.NetworkPaused; i++)
            { _now += .1f; _slow.PumpFrame(); Thread.Sleep(1); _fast.PumpFrame(); }
            Assert.That(_fast.FailureReason, Is.EqualTo("INPUT PROGRESS TIMEOUT"));
            Assert.That(_fast.WaitingForRemoteInput, Is.False);
            Assert.That(_hud.NetworkWaitingVisible, Is.False); Assert.That(_hud.NetworkIssueVisible, Is.True);
            Assert.That(_hud.NetworkIssueTitle, Is.EqualTo("NETWORK PAUSED / 网络暂停"));
            Assert.That(_hud.NetworkIssueReason, Is.EqualTo("INPUT PROGRESS TIMEOUT"));
            Assert.That(_hud.NetworkIssueDetail, Is.EqualTo(DashMatchHud.InputProgressTimeoutDetail));
            Assert.That(_hud.NetworkIssueRect.anchorMin, Is.EqualTo(new Vector2(1f, 0f)));
            Assert.That(_hud.NetworkIssueRect.anchorMax, Is.EqualTo(new Vector2(1f, 0f)));
            foreach (string name in new[] { "NetworkMainMenu", "NetworkOpenReports" })
            {
                Button button = _hud.NetworkIssueRect.Find(name).GetComponent<Button>();
                Assert.That(button.gameObject.activeInHierarchy && button.interactable, Is.True, name);
            }
            Assert.That(_hud.Seats[fastSeat].Minimap.gameObject.activeInHierarchy, Is.True);
            Assert.That(_hud.Seats[fastSeat].Minimap.State, Is.SameAs(_fast.Sim));
            for (int i = 0; i < 10; i++) { _now += .2f; _fast.PumpFrame(); _fast.AdvanceOneTick(); }
            Assert.That(_fast.Sim.Checksum(), Is.EqualTo(before)); Assert.That(_hud.NetworkIssueVisible, Is.True);
            int requests = 0; _hud.MainMenuRequested += () => requests++;
            Assert.That(requests, Is.Zero); Assert.That(Time.timeScale, Is.EqualTo(1f));
            _hud.transform.Find("NetworkIssuePanel/NetworkMainMenu").GetComponent<Button>().onClick.Invoke();
            Assert.That(requests, Is.EqualTo(1), "Only an explicit button action requests navigation.");
        }

        [Test]
        public void RebindingFreshLocalMatchDoesNotRetainLanWaitingNotice()
        {
            CreatePair(1); EnterWait();
            var local = ChildDirector("Fresh local match");
            local.InitializeLocal(ArenaRulesProfile.CreateProductionV1(), 391u);
            _fast.Configure(null, null); local.Configure(null, _hud);
            Assert.That(local.WaitingForRemoteInput, Is.False);
            Assert.That(_hud.NetworkNoticeVisible, Is.False); Assert.That(_hud.NetworkWaitingVisible, Is.False);
            Assert.That(_hud.Seats[0].Region.gameObject.activeSelf && _hud.Seats[1].Region.gameObject.activeSelf, Is.True);
        }

        [TestCase(1280, 720)] [TestCase(1920, 1080)] [TestCase(1024, 768)]
        public void SilentWaitBecomesVisibleTimeoutWithChineseGlyphsAndLowerRightLayoutAtSupportedSizes(int width, int height)
        {
            CreatePair(1); EnterWait();
            var cameraObject = new GameObject("Size assertion camera"); cameraObject.transform.SetParent(_root.transform);
            Camera camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true;
            camera.transform.position = new Vector3(10000, 10000, -10000);
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24);
            Canvas canvas = _hud.GetComponent<Canvas>();
            RenderMode mode = canvas.renderMode; Camera oldCamera = canvas.worldCamera;
            try
            {
                camera.targetTexture = target; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                _hud.RefreshFromDirector(); Canvas.ForceUpdateCanvases();
                Assert.That(_fast.WaitingForRemoteInput, Is.True);
                Assert.That(_hud.NetworkNoticeVisible, Is.False);
                for (int i = 0; i < 60 && !_fast.NetworkPaused; i++)
                { _now += .1f; _slow.PumpFrame(); Thread.Sleep(1); _fast.PumpFrame(); }
                Assert.That(_fast.NetworkPaused, Is.True);
                Assert.That(_hud.NetworkIssueVisible, Is.True); AssertNoticeLayout();
            }
            finally
            {
                canvas.renderMode = mode; canvas.worldCamera = oldCamera; camera.targetTexture = null; RenderTexture.ReleaseTemporary(target);
            }
            void AssertNoticeLayout()
            {
                _hud.RefreshFromDirector(); Canvas.ForceUpdateCanvases();
                canvas.GetComponent<CanvasScaler>().SendMessage("Handle", SendMessageOptions.DontRequireReceiver);
                Canvas.ForceUpdateCanvases();
                Assert.That(canvas.pixelRect.width, Is.EqualTo(width).Within(.1f)); Assert.That(canvas.pixelRect.height, Is.EqualTo(height).Within(.1f));
                var corners = new Vector3[4]; _hud.NetworkIssueRect.GetWorldCorners(corners);
                foreach (Vector3 corner in corners)
                {
                    Vector3 pixel = camera.WorldToScreenPoint(corner);
                    Assert.That(pixel.x, Is.InRange(width * .5f, width)); Assert.That(pixel.y, Is.InRange(0f, height * .5f));
                }
                var mapCorners = new Vector3[4]; _hud.Seats[1].Minimap.RectTransform.GetWorldCorners(mapCorners);
                Assert.That(camera.WorldToScreenPoint(corners[1]).y,
                    Is.LessThan(camera.WorldToScreenPoint(mapCorners[0]).y), "Network notice stays below the minimap.");
                var bannerCorners = new Vector3[4]; ((RectTransform)_hud.transform.Find("Banner")).GetWorldCorners(bannerCorners);
                Assert.That(camera.WorldToScreenPoint(corners[0]).y,
                    Is.GreaterThan(camera.WorldToScreenPoint(bannerCorners[1]).y), "Network notice stays above the bottom banner.");
                foreach (TMP_Text label in _hud.NetworkIssueRect.GetComponentsInChildren<TMP_Text>())
                {
                    label.ForceMeshUpdate();
                    Assert.That(label.font.HasCharacters(label.text.Replace("\n", ""), out uint[] missing, false, false), Is.True, label.text);
                    Assert.That(label.isTextOverflowing, Is.False, label.name);
                    Bounds text = label.textBounds; Rect rect = label.rectTransform.rect;
                    Assert.That(text.min.x, Is.GreaterThanOrEqualTo(rect.xMin - 1.5f), label.name);
                    Assert.That(text.max.x, Is.LessThanOrEqualTo(rect.xMax + 1.5f), label.name);
                    Assert.That(text.min.y, Is.GreaterThanOrEqualTo(rect.yMin - 1.5f), label.name);
                    Assert.That(text.max.y, Is.LessThanOrEqualTo(rect.yMax + 1.5f), label.name);
                }
            }
        }
    }
}
