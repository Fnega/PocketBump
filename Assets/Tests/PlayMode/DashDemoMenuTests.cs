using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PocketBump.Tests
{
    public sealed class DashDemoMenuTests
    {
        GameObject _root;
        float _fixedDelta, _volume;
        bool _hadVolume;
        string _reportDirectory;
        [SetUp] public void Setup()
        {
            _fixedDelta = Time.fixedDeltaTime; _hadVolume = PlayerPrefs.HasKey(DashAudioSettings.VolumeKey);
            _volume = PlayerPrefs.GetFloat(DashAudioSettings.VolumeKey);
            _reportDirectory = null;
            _root = new GameObject("Demo menu fixture");
        }
        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(_root); Time.fixedDeltaTime = _fixedDelta;
            if (_hadVolume) PlayerPrefs.SetFloat(DashAudioSettings.VolumeKey, _volume); else PlayerPrefs.DeleteKey(DashAudioSettings.VolumeKey);
            PlayerPrefs.Save();
            // This fixture owns the GUID directory and every file below it.
            if (_reportDirectory != null && Directory.Exists(_reportDirectory)) Directory.Delete(_reportDirectory, true);
        }
        DashMatchDirector Build()
        {
            var director = _root.AddComponent<DashMatchDirector>(); director.AutomaticTicking = false;
            DashMatchHud hud = DashMatchHud.Build(_root.transform, DashUiTheme.Load().Font);
            director.InitializeLocal(ArenaRulesProfile.CreateProductionV1(), 10u); director.Configure(null, hud); return director;
        }
        static Button Button(Component root, string name) => root.GetComponentsInChildren<Button>(true).Single(b => b.name == name);
        DashMatchDirector BuildLanWithBlockedReports(out DashNetworkReporter reporter, out string blockedPath)
        {
            _reportDirectory = Path.Combine(Path.GetTempPath(), "PocketBump-Menu-Test-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_reportDirectory);
            blockedPath = Path.Combine(_reportDirectory, "blocked-reports");
            File.WriteAllText(blockedPath, "Only this test's file blocks report storage.");
            var director = _root.AddComponent<DashMatchDirector>(); director.AutomaticTicking = false;
            reporter = _root.AddComponent<DashNetworkReporter>(); reporter.Bind(director, blockedPath);
            DashMatchHud hud = DashMatchHud.Build(_root.transform, DashUiTheme.Load().Font);
            director.InitializeLan(DashLanSession.Host(ArenaRulesProfile.CreateProductionV1(), 1951, 10u, () => 0f));
            director.Configure(null, hud); director.SetMenuOpen(true); return director;
        }
        static string ReportText(Component menu) => menu.GetComponentsInChildren<TMP_Text>(true).Single(t => t.name == "ReportStatus").text;
        static void DirectoryResult(DashDemoMatchMenu menu, bool opened, string error)
        {
            // Exercise presentation of an explicit folder result without opening
            // Explorer or depending on the operating system's shell association.
            MethodInfo method = typeof(DashDemoMatchMenu).GetMethod("SetDirectoryResult", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(menu, new object[] { opened, error });
        }

        [Test]
        public void AuthoredMenuHelpAndContinueKeepOneWorldAndExposeReportsAndVolume()
        {
            DashMatchDirector director = Build(); DashDemoMatchMenu menu = director.Hud.GetComponent<DashDemoMatchMenu>();
            int checksum = director.Sim.Checksum();
            director.SetMenuOpen(true); menu.Refresh();
            Assert.That(menu.Visible, Is.True); Assert.That(menu.ModeText, Does.Contain("本地比赛已暂停"));
            Button(menu, "MatchHelp").onClick.Invoke(); Assert.That(menu.HelpVisible, Is.True);
            Button(menu, "CloseGuide").onClick.Invoke(); Assert.That(menu.HelpVisible, Is.False);
            Assert.That(Button(menu, "OpenReports").onClick.GetPersistentEventCount(), Is.Zero, "Folder action is explicitly wired at runtime, not an external upload.");
            Slider volume = menu.GetComponentInChildren<Slider>(true); volume.value = 0f;
            Assert.That(DashAudioSettings.Volume, Is.Zero);
            Button(menu, "ContinueMatch").onClick.Invoke(); menu.Refresh();
            Assert.That(menu.Visible, Is.False); Assert.That(director.MenuOpen, Is.False);
            Assert.That(director.Sim.Checksum(), Is.EqualTo(checksum));
            Assert.That(_root.GetComponentsInChildren<DashMatchDirector>(true).Length, Is.EqualTo(1));
        }

        [Test]
        public void MenuLeaveUsesExplicitActionAndDirectionLabelsMatchGoalOwnership()
        {
            DashMatchDirector director = Build(); DashDemoMatchMenu menu = director.Hud.GetComponent<DashDemoMatchMenu>();
            int exits = 0; menu.MainMenuRequested += () => { exits++; director.LeaveNetwork(); };
            director.SetMenuOpen(true); menu.Refresh(); Button(menu, "LeaveMatch").onClick.Invoke();
            Assert.That(exits, Is.EqualTo(1)); Assert.That(director.MenuOpen, Is.False);
            Assert.That(menu.AttackLabels[0].text, Does.Contain("向南").And.Contain("橙方球门"));
            Assert.That(menu.AttackLabels[1].text, Does.Contain("向北").And.Contain("蓝方球门"));
            Assert.That(director.Rules.SpawnForSeat(0).y, Is.GreaterThan(0));
            Assert.That(director.Rules.SpawnForSeat(1).y, Is.LessThan(0));
        }

        [Test]
        public void SharedGuideUsesActualBindingsAndLanUsesP1ControlsOnBothComputers()
        {
            DashMatchDirector director = Build();
            var guide = director.Hud.GetComponentInChildren<DashDemoGuide>(true);
            var bindings = new DashControlBindings(new MemoryStore());
            Assert.That(bindings.TrySet(0, KeyCode.Q), Is.True);
            Assert.That(bindings.TrySet(1, KeyCode.Keypad3), Is.True);
            guide.Refresh(bindings, false);
            Assert.That(guide.ControlsText, Does.Contain("Q").And.Contain("Num3").And.Contain("数字行"));
            guide.Refresh(bindings, true);
            Assert.That(guide.ControlsText, Does.Contain("Q").And.Contain("自己电脑").And.Not.Contain("Num3"));
            Assert.That(DashDemoGuide.LeftRules, Does.Contain("0.3 秒").And.Contain("冷却 3 秒").And.Contain("掉落不给"));
            Assert.That(DashDemoGuide.RightRules, Does.Contain("眩晕").And.Contain("10 秒").And.Contain("绿色").And.Contain("蓝色").And.Contain("紫色"));
        }

        [Test]
        public void SharedGuideLabelsDefaultKeypadDashAndExplicitNumberRowBindingDistinctly()
        {
            DashMatchDirector director = Build();
            var guide = director.Hud.GetComponentInChildren<DashDemoGuide>(true);
            var bindings = new DashControlBindings(new MemoryStore());
            guide.Refresh(bindings, false);
            Assert.That(guide.ControlsText, Does.Contain("P2：方向键 + Num2 冲刺").And.Contain("Num2 = 小键盘"));
            Assert.That(bindings.TrySet(1, KeyCode.Alpha2), Is.True);
            guide.Refresh(bindings, false);
            Assert.That(guide.ControlsText, Does.Contain("P2：方向键 + 2 冲刺").And.Contain("2 = 键盘上方数字行"));
            Assert.That(guide.ControlsText, Does.Not.Contain("P2：方向键 + Num2 冲刺"));
            guide.Refresh(bindings, true);
            Assert.That(guide.ControlsText, Does.Contain("WASD 移动 + K 冲刺").And.Not.Contain("P2："));
        }

        [Test]
        public void RepeatInstallationPreservesAuthoredMenuAndAttackLabelStyle()
        {
            DashMatchDirector director = Build(); DashDemoMatchMenu menu = director.Hud.GetComponent<DashDemoMatchMenu>();
            menu.AttackLabels[0].color = Color.magenta;
            menu.AttackLabels[0].rectTransform.anchoredPosition = new Vector2(5, 72);
            Assert.That(DashDemoMatchMenu.Install(director.Hud), Is.SameAs(menu));
            Assert.That(menu.AttackLabels[0].color, Is.EqualTo(Color.magenta));
            Assert.That(menu.AttackLabels[0].rectTransform.anchoredPosition, Is.EqualTo(new Vector2(5, 72)));
            Assert.That(director.Hud.GetComponents<DashDemoMatchMenu>().Length, Is.EqualTo(1));
        }

        [Test]
        public void ReportSaveRecoveryClearsAutomaticMenuErrorWithoutChangingMatch()
        {
            DashMatchDirector director = BuildLanWithBlockedReports(out DashNetworkReporter reporter, out string blockedPath);
            DashDemoMatchMenu menu = director.Hud.GetComponent<DashDemoMatchMenu>();
            int checksum = director.Sim.Checksum();
            Assert.That(reporter.CaptureNow("periodic"), Is.False); menu.Refresh();
            Assert.That(ReportText(menu), Does.StartWith("报告保存失败："));
            Assert.That(reporter.LastWriteError, Is.Not.Empty);
            File.Delete(blockedPath); Directory.CreateDirectory(blockedPath);
            Assert.That(reporter.CaptureNow("periodic"), Is.True); menu.Refresh();
            Assert.That(reporter.LastWriteError, Is.Empty);
            Assert.That(ReportText(menu), Is.Empty);
            Assert.That(director.NetworkPaused, Is.False);
            Assert.That(director.Sim.Checksum(), Is.EqualTo(checksum));
        }

        [Test]
        public void ManualFolderErrorSurvivesSaveRecoveryUntilExplicitOpenSucceeds()
        {
            DashMatchDirector director = BuildLanWithBlockedReports(out DashNetworkReporter reporter, out string blockedPath);
            DashDemoMatchMenu menu = director.Hud.GetComponent<DashDemoMatchMenu>();
            const string manualError = "无法打开报告目录：test shell unavailable";
            DirectoryResult(menu, false, manualError); menu.Refresh();
            Assert.That(ReportText(menu), Is.EqualTo(manualError));
            Assert.That(reporter.CaptureNow("periodic"), Is.False); menu.Refresh();
            Assert.That(ReportText(menu), Does.StartWith("报告保存失败："));
            File.Delete(blockedPath); Directory.CreateDirectory(blockedPath);
            Assert.That(reporter.CaptureNow("periodic"), Is.True); menu.Refresh();
            Assert.That(ReportText(menu), Is.EqualTo(manualError), "Automatic recovery must not dismiss an unrelated folder-open failure.");
            DirectoryResult(menu, true, ""); menu.Refresh();
            Assert.That(ReportText(menu), Is.Empty);
        }
        sealed class MemoryStore : IDashBindingStore
        {
            public bool HasKey(string key) => false;
            public int GetInt(string key, int fallback) => fallback;
            public void SetInt(string key, int value) { }
            public void Save() { }
        }
    }
}
