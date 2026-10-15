using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PocketBump.Tests
{
    // Menu-only integration fixtures. Gameplay/navigation across Arena is verified by
    // the production director tests, not by pretending a button-only fixture is a match.
    public sealed class PocketBumpDashMenuTests
    {
        static readonly string[] PreferenceKeys = {
            DashControlBindings.P1PreferenceKey, DashControlBindings.P2PreferenceKey,
            "PocketBump.P1Boost", "PocketBump.P2Boost", "PocketBump.P1Shoot", "PocketBump.P2Shoot",
            "PocketBump.DisplayMode"
        };
        readonly Dictionary<string, int?> _savedPreferences = new Dictionary<string, int?>();
        GameObject _root;
        MainMenuController _menu;
        EventSystem _existingEvents;
        GameSession _existingSession;
        DashUiTheme _testTheme;

        [SetUp]
        public void SetUp()
        {
            foreach (string key in PreferenceKeys)
            {
                _savedPreferences[key] = PlayerPrefs.HasKey(key) ? (int?)PlayerPrefs.GetInt(key) : null;
                PlayerPrefs.DeleteKey(key);
            }
            _existingEvents = Object.FindObjectOfType<EventSystem>();
            _existingSession = Object.FindObjectOfType<GameSession>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            if (_testTheme != null) Object.DestroyImmediate(_testTheme);
            if (_existingEvents == null)
            {
                var created = Object.FindObjectOfType<EventSystem>();
                if (created != null) Object.DestroyImmediate(created.gameObject);
            }
            if (_existingSession == null)
            {
                var created = Object.FindObjectOfType<GameSession>();
                if (created != null) Object.DestroyImmediate(created.gameObject);
            }
            foreach (var saved in _savedPreferences)
            {
                if (saved.Value.HasValue) PlayerPrefs.SetInt(saved.Key, saved.Value.Value);
                else PlayerPrefs.DeleteKey(saved.Key);
            }
            PlayerPrefs.Save();
            _savedPreferences.Clear();
        }

        void Build()
        {
            _root = new GameObject("Dash menu fixture");
            _root.SetActive(false); // Explicit authoring first, without starting a game session.
            _menu = _root.AddComponent<MainMenuController>();
            _menu.BuildAuthoredHierarchy();
        }

        Transform Panel(string name) => _root.transform.Find("MainMenuCanvas/" + name);
        TMP_Text RuleText(string name) => Panel("RuleSetupPanel").Find(name).GetComponent<TMP_Text>();
        Button Button(string name) => _root.GetComponentsInChildren<Button>(true).Single(item => item.name == name);
        void Click(string name) => Button(name).onClick.Invoke();
        string Label(string name) => Button(name).GetComponentInChildren<TMP_Text>(true).text;

        [Test]
        public void FreeDemoHelpReturnsWithoutLosingRuleChoicesOrChangingPreferences()
        {
            Build(); Click("Start"); Click("MARBLESPlus");
            var home = _menu.GetComponent<DashDemoHomeUi>();
            Click("DemoHelp"); Assert.That(home.HelpVisible, Is.True);
            var guide = _menu.GetComponentInChildren<DashDemoGuide>(true);
            Assert.That(guide.ControlsText, Does.Contain("P2：方向键 + Num2 冲刺").And.Contain("Num2 = 小键盘"));
            Click("CloseGuide"); Assert.That(home.HelpVisible, Is.False);
            Assert.That(Panel("RuleSetupPanel").gameObject.activeSelf, Is.True);
            Assert.That(RuleText("MARBLESValue").text, Is.EqualTo("2"));
            Assert.That(PlayerPrefs.HasKey(DashControlBindings.P1PreferenceKey), Is.False);
            Assert.That(PlayerPrefs.HasKey(DashControlBindings.P2PreferenceKey), Is.False);
        }

        [Test]
        public void RulesExposeOnlyTwoDashBindingsAndExplainScoringAndLocalView()
        {
            Build(); Click("Start");
            Assert.That(Panel("RuleSetupPanel").gameObject.activeSelf, Is.True);
            Assert.That(Label("P1DashBind"), Is.EqualTo("DASH  K"));
            Assert.That(Label("P2DashBind"), Is.EqualTo("DASH  Num2"));
            Assert.That(RuleText("MARBLES").text, Is.EqualTo("FREE BALLS"));
            Assert.That(RuleText("ModeHint").text, Does.Contain("LEFT / RIGHT SPLIT"));
            Assert.That(RuleText("WinRuleHint").text, Does.Contain("LEAD BY 2"));
            string copy = string.Join(" ", _root.GetComponentsInChildren<TMP_Text>(true).Select(text => text.text));
            Assert.That(copy.ToUpperInvariant(), Does.Not.Contain("SHOOT").And.Not.Contain("BOOST").And.Not.Contain("CHARGE"));
            Assert.That(Panel("RuleSetupPanel").GetComponentsInChildren<Button>(true).Count(b => b.name.EndsWith("Bind")), Is.EqualTo(2));
            Assert.That(PlayerPrefs.HasKey(DashControlBindings.P1PreferenceKey), Is.False, "Opening the menu is read-only migration.");
        }

        [Test]
        public void MenuReadsValidLegacyBoostWithoutRewritingAnyPreference()
        {
            PlayerPrefs.SetInt("PocketBump.P1Boost", (int)KeyCode.Q);
            PlayerPrefs.SetInt("PocketBump.P2Boost", (int)KeyCode.Keypad3);
            PlayerPrefs.SetInt("PocketBump.P1Shoot", (int)KeyCode.Q);
            Build(); Click("Start");
            Assert.That(Label("P1DashBind"), Is.EqualTo("DASH  Q"));
            Assert.That(Label("P2DashBind"), Is.EqualTo("DASH  Num3"));
            Assert.That(PlayerPrefs.HasKey(DashControlBindings.P1PreferenceKey), Is.False);
            Assert.That(PlayerPrefs.HasKey(DashControlBindings.P2PreferenceKey), Is.False);
            Assert.That(PlayerPrefs.GetInt("PocketBump.P1Boost"), Is.EqualTo((int)KeyCode.Q));
            Assert.That(PlayerPrefs.GetInt("PocketBump.P1Shoot"), Is.EqualTo((int)KeyCode.Q));
        }

        [TestCase(DashControlBindings.P2PreferenceKey)]
        [TestCase("PocketBump.P2Boost")]
        public void SavedNumberRowP2BindingRemainsVisibleInMenuAndGuideWithoutMigrationWrites(string preferenceKey)
        {
            PlayerPrefs.SetInt(preferenceKey, (int)KeyCode.Alpha2);
            Build(); Click("Start");
            Assert.That(Label("P2DashBind"), Is.EqualTo("DASH  2"));
            Click("DemoHelp");
            var guide = _menu.GetComponentInChildren<DashDemoGuide>(true);
            Assert.That(guide.ControlsText, Does.Contain("P2：方向键 + 2 冲刺"));
            Assert.That(guide.ControlsText, Does.Not.Contain("P2：方向键 + Num2 冲刺"));
            Assert.That(PlayerPrefs.GetInt(preferenceKey), Is.EqualTo((int)KeyCode.Alpha2));
            Assert.That(PlayerPrefs.HasKey(DashControlBindings.P1PreferenceKey), Is.False);
            if (preferenceKey != DashControlBindings.P2PreferenceKey)
                Assert.That(PlayerPrefs.HasKey(DashControlBindings.P2PreferenceKey), Is.False);
        }

        [TestCase("RuleSetupPanel", "RulesTitle")]
        [TestCase("JoinPanel", "JoinTitle")]
        [TestCase("SettingsPanel", "SettingsTitle")]
        public void NewMenuTitleRectAccommodatesApprovedCjkFontMetrics(string panel, string titleName)
        {
            Build();
            DashUiTheme theme = DashUiTheme.Load();
            Assert.That(theme, Is.Not.Null);
            var title = Panel(panel).Find(titleName).GetComponent<TMP_Text>();
            title.font = theme.Font;
            float preferred = title.GetPreferredValues(title.text, title.rectTransform.rect.width, Mathf.Infinity).y;
            Assert.That(title.rectTransform.rect.height, Is.GreaterThanOrEqualTo(preferred),
                "Approved Noto line metrics must fit without overflow or clipping.");
        }

        [Test]
        public void RetiredShootKeyCanBeBoundAndTwoDashChoicesPersist()
        {
            PlayerPrefs.SetInt("PocketBump.P1Shoot", (int)KeyCode.J);
            Build(); Click("Start"); Click("P1DashBind");
            Assert.That(_menu.SubmitBindingKey(KeyCode.J), Is.True);
            Assert.That(Label("P1DashBind"), Is.EqualTo("DASH  J"));
            Click("P2DashBind");
            Assert.That(_menu.SubmitBindingKey(KeyCode.Keypad1), Is.True);
            Assert.That(Label("P2DashBind"), Is.EqualTo("DASH  Num1"));
            Assert.That(_menu.ListeningDashSeat, Is.Null);
            var loaded = new DashControlBindings();
            Assert.That(loaded.P1Dash, Is.EqualTo(KeyCode.J));
            Assert.That(loaded.P2Dash, Is.EqualTo(KeyCode.Keypad1));
            Assert.That(PlayerPrefs.GetInt("PocketBump.P1Shoot"), Is.EqualTo((int)KeyCode.J));
            Assert.That(PlayerPrefs.HasKey("PocketBump.P1Boost"), Is.False);
        }

        [Test]
        public void ConflictingAndMovementKeysReportRejectionAndEscapeOnlyCancelsListening()
        {
            Build(); Click("Start"); Click("P2DashBind");
            Assert.That(_menu.SubmitBindingKey(KeyCode.K), Is.False);
            Assert.That(_menu.ListeningDashSeat, Is.EqualTo(1));
            Assert.That(RuleText("RebindHint").text, Does.Contain("KEY UNAVAILABLE"));
            Assert.That(_menu.SubmitBindingKey(KeyCode.W), Is.False);
            Assert.That(_menu.SubmitBindingKey(KeyCode.LeftArrow), Is.False);
            Assert.That(PlayerPrefs.HasKey(DashControlBindings.P2PreferenceKey), Is.False);
            Assert.That(_menu.SubmitBindingKey(KeyCode.Escape), Is.False);
            Assert.That(_menu.ListeningDashSeat, Is.Null);
            Assert.That(Panel("RuleSetupPanel").gameObject.activeSelf, Is.True);
            Assert.That(Label("P2DashBind"), Is.EqualTo("DASH  Num2"));
            Assert.That(_menu.SubmitBindingKey(KeyCode.Keypad1), Is.False, "No binding changes without an active capture.");
        }

        [Test]
        public void LocalHostJoinAndSettingsReturnWithoutLoadingAnotherScene()
        {
            Build(); Scene before = SceneManager.GetActiveScene();
            Click("Start"); Assert.That(RuleText("ModeHint").text, Does.Contain("LOCAL"));
            Click("P1DashBind"); Click("Back");
            Assert.That(_menu.ListeningDashSeat, Is.Null);
            Click("HostLan"); Assert.That(RuleText("ModeHint").text, Does.Contain("LAN").And.Contain("FULL-SCREEN"));
            Click("Back"); Click("JoinLan");
            Assert.That(Panel("JoinPanel").gameObject.activeSelf, Is.True);
            Click("JoinBack"); Click("Settings");
            Assert.That(Panel("SettingsPanel").gameObject.activeSelf, Is.True);
            Click("SettingsBack");
            Assert.That(Panel("MainPanel").gameObject.activeSelf, Is.True);
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(before));
        }

        [Test]
        public void RulesKeepTheirSupportedRangesAndSingleClickListeners()
        {
            Build(); _menu.BuildAuthoredHierarchy(); _menu.BuildAuthoredHierarchy(); Click("Start");
            Click("POINTS TO WINPlus"); Click("MARBLESPlus");
            Assert.That(RuleText("POINTS TO WINValue").text, Is.EqualTo("9"));
            Assert.That(RuleText("MARBLESValue").text, Is.EqualTo("2"));
            for (int i = 0; i < 30; i++) { Click("POINTS TO WINMinus"); Click("MARBLESMinus"); }
            Assert.That(RuleText("POINTS TO WINValue").text, Is.EqualTo("1"));
            Assert.That(RuleText("MARBLESValue").text, Is.EqualTo("1"));
            for (int i = 0; i < 30; i++) { Click("POINTS TO WINPlus"); Click("MARBLESPlus"); }
            Assert.That(RuleText("POINTS TO WINValue").text, Is.EqualTo("21"));
            Assert.That(RuleText("MARBLESValue").text, Is.EqualTo("8"));
            Assert.That(Panel("MainPanel").GetComponentsInChildren<Button>(true).Length, Is.EqualTo(5));
            Assert.That(Panel("RuleSetupPanel").GetComponentsInChildren<TMP_Text>(true).Count(t => t.name == "P1Move"), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator LegacySkillButtonsAreRemovedWithoutDeletingCustomDecoration()
        {
            Build();
            Transform rules = Panel("RuleSetupPanel");
            foreach (string name in new[] { "P1ShootBind", "P1BoostBind", "P2ShootBind", "P2BoostBind" })
                UiFactory.Button(rules, name, name.Contains("Shoot") ? "SHOOT" : "BOOST", Vector2.zero,
                    Vector2.zero, new Vector2(100f, 40f), Color.red, () => Assert.Fail("Retired binding remained clickable."));
            var custom = new GameObject("User-owned decoration", typeof(RectTransform), typeof(Image));
            custom.transform.SetParent(rules, false); custom.GetComponent<Image>().color = Color.magenta;
            _menu.BuildAuthoredHierarchy(); _menu.BuildAuthoredHierarchy();
            yield return null; // Runtime migration disables immediately; destruction completes this frame.
            Assert.That(rules.GetComponentsInChildren<Button>(true).Count(b => b.name.EndsWith("Bind")), Is.EqualTo(2));
            Assert.That(rules.Find("User-owned decoration"), Is.SameAs(custom.transform));
            Assert.That(custom.GetComponent<Image>().color, Is.EqualTo(Color.magenta));
            Assert.That(RuleText("P1Move"), Is.Not.Null);
        }

        [Test]
        public void WindowAndFullscreenButtonsPreserveSelectedRulesAndSessionMode()
        {
            Build(); Click("Start"); Click("POINTS TO WINMinus"); Click("MARBLESPlus"); Click("Back");
            GameSession session = GameSession.Ensure();
            PlayKind kind = session.Kind; MatchSettings settings = session.CopySettings();
            Click("Settings"); Click("Fullscreen");
            Assert.That(DisplayModeSettings.Load(), Is.EqualTo(DisplayModeChoice.Fullscreen));
            Click("Settings"); Click("Windowed");
            Assert.That(DisplayModeSettings.Load(), Is.EqualTo(DisplayModeChoice.Windowed));
            Assert.That(DisplayModeSettings.WindowWidth, Is.EqualTo(1280));
            Assert.That(DisplayModeSettings.WindowHeight, Is.EqualTo(720));
            Assert.That(session.Kind, Is.EqualTo(kind));
            Assert.That(session.SelectedSettings.pointsToWin, Is.EqualTo(settings.pointsToWin));
            Assert.That(session.SelectedSettings.marbleCount, Is.EqualTo(settings.marbleCount));
            Click("Start");
            Assert.That(RuleText("POINTS TO WINValue").text, Is.EqualTo("7"));
            Assert.That(RuleText("MARBLESValue").text, Is.EqualTo("2"));
        }

        [Test]
        public void JoinInputRetainsTheSafeViewportAndSingleLineContract()
        {
            Build(); Click("JoinLan");
            TMP_InputField address = Panel("JoinPanel").Find("JoinAddress").GetComponent<TMP_InputField>();
            Assert.That(address.textViewport, Is.Not.Null);
            Assert.That(address.textComponent.transform.parent, Is.SameAs(address.textViewport.transform));
            Assert.That(address.textComponent.raycastTarget, Is.False);
            Assert.That(address.lineType, Is.EqualTo(TMP_InputField.LineType.SingleLine));
            address.text = "192.168.1.55";
            Click("JoinBack"); Click("JoinLan");
            Assert.That(address.text, Is.EqualTo("192.168.1.55"));
        }

        [UnityTest]
        public IEnumerator ExplicitThemeThenAuthorEditsSurviveRepairAndAwake()
        {
            Build();
            _testTheme = ScriptableObject.CreateInstance<DashUiTheme>();
            _testTheme.Background = new Color(.8f, .9f, .7f);
            _testTheme.Panel = new Color(.9f, .9f, .8f);
            _testTheme.Text = Color.black;
            _menu.Theme = _testTheme;
            _menu.ApplyThemeToAuthoredHierarchy();
            Image background = Panel("Background").GetComponent<Image>();
            Image rulePanel = Panel("RuleSetupPanel").GetComponent<Image>();
            TMP_Text dash = Button("P1DashBind").GetComponentInChildren<TMP_Text>(true);
            Assert.That(background.color, Is.EqualTo(_testTheme.Background));
            background.color = new Color(.11f, .22f, .33f, .44f);
            rulePanel.color = new Color(.42f, .32f, .22f, .82f);
            dash.color = Color.magenta;
            TMP_FontAsset authoredFont = dash.font;
            Color backgroundColor = background.color, panelColor = rulePanel.color;
            _menu.BuildAuthoredHierarchy(); _menu.BuildAuthoredHierarchy();
            _root.SetActive(true); // Awake must wire behavior, not repaint the authored scene.
            yield return null;
            Assert.That(background.color, Is.EqualTo(backgroundColor));
            Assert.That(rulePanel.color, Is.EqualTo(panelColor));
            Assert.That(dash.color, Is.EqualTo(Color.magenta));
            Assert.That(dash.font, Is.SameAs(authoredFont));
            Assert.That(_menu.Theme, Is.SameAs(_testTheme));
        }
    }
}
