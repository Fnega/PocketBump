using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PocketBump
{
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] GameObject _mainPanel;
        [SerializeField] GameObject _rulesPanel;
        [SerializeField] TextMeshProUGUI _pointsValue;
        [SerializeField] TextMeshProUGUI _marblesValue;
        [SerializeField] GameObject _joinPanel;
        [SerializeField] TMP_InputField _joinAddress;
        [SerializeField] TextMeshProUGUI _errorText;
        int _pointsToWin;
        int _marbleCount;
        SkillBinding? _listening;
        bool _hosting;

        void Awake()
        {
            GameSession session = GameSession.Ensure();
            _pointsToWin = session.SelectedSettings.pointsToWin;
            _marbleCount = session.SelectedSettings.marbleCount;
            if (transform.Find("MainMenuCanvas") == null)
                BuildUi();
            ResolveReferences();
            EnsureControlsUi();
            EnsureLanUi();
            WireButtons();
            ShowRules(session.ConsumeOpenRulesRequest());
            ShowJoin(false);
            ShowError(session.ErrorMessage);
        }

        public void BuildAuthoredHierarchy()
        {
            _pointsToWin = MatchSettings.DefaultPointsToWin;
            _marbleCount = MatchSettings.DefaultMarbleCount;
            if (transform.Find("MainMenuCanvas") == null)
                BuildUi();
            ResolveReferences();
            EnsureControlsUi();
            EnsureLanUi();
            WireButtons();
            ShowRules(false);
            ShowJoin(false);
        }

        void Update()
        {
            if (_rulesPanel == null || !_rulesPanel.activeSelf) return;

            if (_listening.HasValue)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    _listening = null;
                    RefreshBindings();
                    return;
                }

                if (TryReadPressedKey(out KeyCode key))
                {
                    ControlBindings.TrySet(_listening.Value, key);
                    _listening = null;
                    RefreshBindings();
                }

                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
                ShowRules(false);
        }

        void BuildUi()
        {
            Transform canvas = UiFactory.CreateCanvas("MainMenuCanvas", transform);
            UiFactory.Panel(
                canvas,
                "Background",
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(1920f, 1080f),
                VisualPalette.Tabletop);

            UiFactory.Text(
                canvas,
                "Title",
                "POCKET  BUMP",
                88f,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.84f),
                Vector2.zero,
                new Vector2(1000f, 130f),
                VisualPalette.PlayerOne);
            UiFactory.Text(
                canvas,
                "Subtitle",
                "NEON TABLETOP DUEL",
                28f,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.74f),
                Vector2.zero,
                new Vector2(700f, 60f),
                VisualPalette.PlayerTwo);

            _mainPanel = UiFactory.Panel(
                canvas,
                "MainPanel",
                new Vector2(0.5f, 0.36f),
                Vector2.zero,
                new Vector2(470f, 520f),
                VisualPalette.WithAlpha(VisualPalette.TabletopAccent, 0.88f));
            UiFactory.Button(
                _mainPanel.transform,
                "Start",
                "START",
                new Vector2(0.5f, 0.84f),
                Vector2.zero,
                new Vector2(300f, 64f),
                VisualPalette.PlayerOne,
                () => OpenLocalRules());
            UiFactory.Button(
                _mainPanel.transform,
                "HostLan",
                "HOST LAN",
                new Vector2(0.5f, 0.64f),
                Vector2.zero,
                new Vector2(300f, 64f),
                VisualPalette.Gate,
                () => OpenHostRules());
            UiFactory.Button(
                _mainPanel.transform,
                "JoinLan",
                "JOIN LAN",
                new Vector2(0.5f, 0.44f),
                Vector2.zero,
                new Vector2(300f, 64f),
                VisualPalette.PlayerTwo,
                () => ShowJoin(true));
            UiFactory.Button(
                _mainPanel.transform,
                "Exit",
                "EXIT",
                new Vector2(0.5f, 0.22f),
                Vector2.zero,
                new Vector2(300f, 64f),
                VisualPalette.Danger,
                ExitGame);
            _errorText = UiFactory.Text(
                _mainPanel.transform,
                "Error",
                "",
                18f,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.06f),
                Vector2.zero,
                new Vector2(420f, 40f),
                VisualPalette.Danger);

            BuildJoinPanel(canvas);
            BuildRulesPanel(canvas);
        }

        void BuildRulesPanel(Transform canvas)
        {
            _rulesPanel = UiFactory.Panel(
                canvas,
                "RuleSetupPanel",
                new Vector2(0.5f, 0.42f),
                Vector2.zero,
                new Vector2(780f, 720f),
                VisualPalette.WithAlpha(VisualPalette.TabletopAccent, 0.94f));

            UiFactory.Text(
                _rulesPanel.transform,
                "RulesTitle",
                "MATCH RULES",
                40f,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.94f),
                Vector2.zero,
                new Vector2(500f, 56f),
                VisualPalette.Gate);

            BuildRuleRow("POINTS TO WIN", 0.84f, true);
            BuildRuleRow("MARBLES", 0.74f, false);
            BuildControlsSection();

            UiFactory.Button(
                _rulesPanel.transform,
                "Play",
                "PLAY",
                new Vector2(0.68f, 0.08f),
                Vector2.zero,
                new Vector2(210f, 62f),
                VisualPalette.PlayerOne,
                StartGame);
            UiFactory.Button(
                _rulesPanel.transform,
                "Back",
                "BACK",
                new Vector2(0.32f, 0.08f),
                Vector2.zero,
                new Vector2(210f, 62f),
                VisualPalette.Wall,
                () => ShowRules(false));
        }

        void BuildJoinPanel(Transform canvas)
        {
            _joinPanel = UiFactory.Panel(
                canvas,
                "JoinPanel",
                new Vector2(0.5f, 0.42f),
                Vector2.zero,
                new Vector2(640f, 360f),
                VisualPalette.WithAlpha(VisualPalette.TabletopAccent, 0.94f));
            UiFactory.Text(
                _joinPanel.transform,
                "JoinTitle",
                "JOIN LAN",
                40f,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.82f),
                Vector2.zero,
                new Vector2(400f, 56f),
                VisualPalette.PlayerTwo);
            UiFactory.Text(
                _joinPanel.transform,
                "JoinHint",
                "HOST ADDRESS",
                22f,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.6f),
                Vector2.zero,
                new Vector2(400f, 36f),
                VisualPalette.Text);
            _joinAddress = CreateAddressInput(_joinPanel.transform);
            UiFactory.Button(
                _joinPanel.transform,
                "JoinConfirm",
                "JOIN",
                new Vector2(0.68f, 0.16f),
                Vector2.zero,
                new Vector2(210f, 62f),
                VisualPalette.PlayerTwo,
                ConfirmJoin);
            UiFactory.Button(
                _joinPanel.transform,
                "JoinBack",
                "BACK",
                new Vector2(0.32f, 0.16f),
                Vector2.zero,
                new Vector2(210f, 62f),
                VisualPalette.Wall,
                () => ShowJoin(false));
            _joinPanel.SetActive(false);
        }

        static TMP_InputField CreateAddressInput(Transform parent)
        {
            var go = new GameObject("JoinAddress");
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.42f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(420f, 56f);
            go.AddComponent<Image>().color = VisualPalette.WithAlpha(VisualPalette.Tabletop, 0.9f);
            var input = go.AddComponent<TMP_InputField>();
            var text = UiFactory.Text(
                go.transform,
                "Text",
                "127.0.0.1",
                28f,
                TextAlignmentOptions.MidlineLeft,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(400f, 52f),
                VisualPalette.Text);
            text.raycastTarget = true;
            input.textComponent = text;
            input.text = "127.0.0.1";
            input.pointSize = 28f;
            return input;
        }

        void OpenLocalRules()
        {
            _hosting = false;
            ShowJoin(false);
            ShowRules(true);
        }

        void OpenHostRules()
        {
            _hosting = true;
            ShowJoin(false);
            ShowRules(true);
        }

        void ShowJoin(bool show)
        {
            if (_joinPanel == null) return;
            _listening = null;
            if (show)
            {
                _hosting = false;
                if (_rulesPanel != null) _rulesPanel.SetActive(false);
                _mainPanel.SetActive(false);
                _joinPanel.SetActive(true);
                if (_joinAddress != null && string.IsNullOrEmpty(_joinAddress.text))
                    _joinAddress.text = "127.0.0.1";
            }
            else
            {
                _joinPanel.SetActive(false);
                if (_rulesPanel == null || !_rulesPanel.activeSelf)
                    _mainPanel.SetActive(true);
            }
        }

        void ShowError(string message)
        {
            if (_errorText == null) return;
            _errorText.text = message ?? "";
        }

        void EnsureControlsUi()
        {
            if (_rulesPanel == null) return;
            var rect = _rulesPanel.GetComponent<RectTransform>();
            if (rect != null)
                rect.sizeDelta = new Vector2(780f, 720f);
            if (_rulesPanel.transform.Find("P1ShootBind") == null)
                BuildControlsSection();
        }

        void EnsureLanUi()
        {
            if (_mainPanel == null) return;
            var rect = _mainPanel.GetComponent<RectTransform>();
            if (rect != null)
                rect.sizeDelta = new Vector2(470f, 520f);
            if (_mainPanel.transform.Find("HostLan") == null)
            {
                UiFactory.Button(
                    _mainPanel.transform,
                    "HostLan",
                    "HOST LAN",
                    new Vector2(0.5f, 0.64f),
                    Vector2.zero,
                    new Vector2(300f, 64f),
                    VisualPalette.Gate,
                    OpenHostRules);
            }

            if (_mainPanel.transform.Find("JoinLan") == null)
            {
                UiFactory.Button(
                    _mainPanel.transform,
                    "JoinLan",
                    "JOIN LAN",
                    new Vector2(0.5f, 0.44f),
                    Vector2.zero,
                    new Vector2(300f, 64f),
                    VisualPalette.PlayerTwo,
                    () => ShowJoin(true));
            }

            Transform canvas = transform.Find("MainMenuCanvas");
            if (_joinPanel == null && canvas != null)
                BuildJoinPanel(canvas);
        }

        void BuildControlsSection()
        {
            UiFactory.Text(
                _rulesPanel.transform,
                "P1Move",
                "P1 MOVE  WASD",
                22f,
                TextAlignmentOptions.MidlineLeft,
                new Vector2(0.28f, 0.62f),
                Vector2.zero,
                new Vector2(280f, 40f),
                VisualPalette.PlayerOne);
            BuildBindButton("P1ShootBind", "SHOOT", SkillBinding.P1Shoot, new Vector2(0.62f, 0.62f), VisualPalette.PlayerOne);
            BuildBindButton("P1BoostBind", "BOOST", SkillBinding.P1Boost, new Vector2(0.84f, 0.62f), VisualPalette.PlayerOne);

            UiFactory.Text(
                _rulesPanel.transform,
                "P2Move",
                "P2 MOVE  ARROWS",
                22f,
                TextAlignmentOptions.MidlineLeft,
                new Vector2(0.28f, 0.5f),
                Vector2.zero,
                new Vector2(280f, 40f),
                VisualPalette.PlayerTwo);
            BuildBindButton("P2ShootBind", "SHOOT", SkillBinding.P2Shoot, new Vector2(0.62f, 0.5f), VisualPalette.PlayerTwo);
            BuildBindButton("P2BoostBind", "BOOST", SkillBinding.P2Boost, new Vector2(0.84f, 0.5f), VisualPalette.PlayerTwo);

            UiFactory.Text(
                _rulesPanel.transform,
                "RebindHint",
                "CLICK A KEY THEN PRESS A NEW KEY  ·  ESC CANCELS",
                16f,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.38f),
                Vector2.zero,
                new Vector2(640f, 36f),
                VisualPalette.Wall);
        }

        void BuildBindButton(string name, string role, SkillBinding slot, Vector2 anchor, Color color)
        {
            if (_rulesPanel.transform.Find(name) != null) return;
            UiFactory.Button(
                _rulesPanel.transform,
                name,
                role,
                anchor,
                Vector2.zero,
                new Vector2(150f, 52f),
                color,
                () => BeginListen(slot));
        }

        void ResolveReferences()
        {
            if (_mainPanel == null)
            {
                Transform found = transform.Find("MainMenuCanvas/MainPanel");
                if (found != null) _mainPanel = found.gameObject;
            }
            if (_rulesPanel == null)
            {
                Transform found = transform.Find("MainMenuCanvas/RuleSetupPanel");
                if (found != null) _rulesPanel = found.gameObject;
            }

            if (_joinPanel == null)
            {
                Transform found = transform.Find("MainMenuCanvas/JoinPanel");
                if (found != null) _joinPanel = found.gameObject;
            }

            TextMeshProUGUI[] texts = GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (TextMeshProUGUI text in texts)
            {
                if (text.name == "POINTS TO WINValue") _pointsValue = text;
                else if (text.name == "MARBLESValue") _marblesValue = text;
                else if (text.name == "Error") _errorText = text;
            }

            if (_joinAddress == null)
            {
                Transform addr = transform.Find("MainMenuCanvas/JoinPanel/JoinAddress");
                if (addr != null) _joinAddress = addr.GetComponent<TMP_InputField>();
            }
        }

        void WireButtons()
        {
            Button[] buttons = GetComponentsInChildren<Button>(true);
            foreach (Button button in buttons)
            {
                button.onClick.RemoveAllListeners();
                switch (button.name)
                {
                    case "Start": button.onClick.AddListener(OpenLocalRules); break;
                    case "HostLan": button.onClick.AddListener(OpenHostRules); break;
                    case "JoinLan": button.onClick.AddListener(() => ShowJoin(true)); break;
                    case "JoinConfirm": button.onClick.AddListener(ConfirmJoin); break;
                    case "JoinBack": button.onClick.AddListener(() => ShowJoin(false)); break;
                    case "Exit": button.onClick.AddListener(ExitGame); break;
                    case "Play": button.onClick.AddListener(StartGame); break;
                    case "Back": button.onClick.AddListener(() => CancelListenAnd(() => ShowRules(false))); break;
                    case "POINTS TO WINMinus": button.onClick.AddListener(() => Nudge(true, -1)); break;
                    case "POINTS TO WINPlus": button.onClick.AddListener(() => Nudge(true, 1)); break;
                    case "MARBLESMinus": button.onClick.AddListener(() => Nudge(false, -1)); break;
                    case "MARBLESPlus": button.onClick.AddListener(() => Nudge(false, 1)); break;
                    case "P1ShootBind": button.onClick.AddListener(() => BeginListen(SkillBinding.P1Shoot)); break;
                    case "P1BoostBind": button.onClick.AddListener(() => BeginListen(SkillBinding.P1Boost)); break;
                    case "P2ShootBind": button.onClick.AddListener(() => BeginListen(SkillBinding.P2Shoot)); break;
                    case "P2BoostBind": button.onClick.AddListener(() => BeginListen(SkillBinding.P2Boost)); break;
                }
            }
        }

        void BuildRuleRow(string label, float y, bool points)
        {
            UiFactory.Text(
                _rulesPanel.transform,
                label,
                label,
                24f,
                TextAlignmentOptions.MidlineLeft,
                new Vector2(0.26f, y),
                Vector2.zero,
                new Vector2(270f, 50f),
                VisualPalette.Text);
            UiFactory.Button(
                _rulesPanel.transform,
                label + "Minus",
                "-",
                new Vector2(0.61f, y),
                Vector2.zero,
                new Vector2(56f, 50f),
                VisualPalette.Wall,
                () => Nudge(points, -1));
            TextMeshProUGUI value = UiFactory.Text(
                _rulesPanel.transform,
                label + "Value",
                "0",
                32f,
                TextAlignmentOptions.Center,
                new Vector2(0.73f, y),
                Vector2.zero,
                new Vector2(80f, 50f),
                points ? VisualPalette.PlayerOne : VisualPalette.PlayerTwo);
            UiFactory.Button(
                _rulesPanel.transform,
                label + "Plus",
                "+",
                new Vector2(0.86f, y),
                Vector2.zero,
                new Vector2(56f, 50f),
                VisualPalette.Wall,
                () => Nudge(points, 1));

            if (points) _pointsValue = value;
            else _marblesValue = value;
        }

        void BeginListen(SkillBinding slot)
        {
            _listening = slot;
            RefreshBindings();
        }

        void CancelListenAnd(Action next)
        {
            _listening = null;
            RefreshBindings();
            next();
        }

        void Nudge(bool points, int delta)
        {
            if (points)
                _pointsToWin = Mathf.Clamp(_pointsToWin + delta, MatchSettings.MinPointsToWin, MatchSettings.MaxPointsToWin);
            else
                _marbleCount = Mathf.Clamp(_marbleCount + delta, MatchSettings.MinMarbles, MatchSettings.MaxMarbles);
            RefreshValues();
        }

        void ShowRules(bool show)
        {
            if (!show) _listening = null;
            if (_joinPanel != null && show) _joinPanel.SetActive(false);
            _mainPanel.SetActive(!show);
            _rulesPanel.SetActive(show);
            RefreshValues();
            RefreshBindings();
        }

        void RefreshValues()
        {
            if (_pointsValue != null) _pointsValue.text = _pointsToWin.ToString();
            if (_marblesValue != null) _marblesValue.text = _marbleCount.ToString();
        }

        void RefreshBindings()
        {
            SetBindLabel("P1ShootBind", SkillBinding.P1Shoot, ControlBindings.P1Shoot);
            SetBindLabel("P1BoostBind", SkillBinding.P1Boost, ControlBindings.P1Boost);
            SetBindLabel("P2ShootBind", SkillBinding.P2Shoot, ControlBindings.P2Shoot);
            SetBindLabel("P2BoostBind", SkillBinding.P2Boost, ControlBindings.P2Boost);
        }

        void SetBindLabel(string buttonName, SkillBinding slot, KeyCode key)
        {
            if (_rulesPanel == null) return;
            Transform button = _rulesPanel.transform.Find(buttonName);
            if (button == null) return;
            var text = button.GetComponentInChildren<TextMeshProUGUI>();
            if (text == null) return;
            string role = slot == SkillBinding.P1Shoot || slot == SkillBinding.P2Shoot ? "SHOOT" : "BOOST";
            text.text = _listening == slot ? "..." : role + " " + ControlBindings.Label(key);
            text.fontSize = 18f;
            text.enableWordWrapping = false;
        }

        static bool TryReadPressedKey(out KeyCode key)
        {
            foreach (KeyCode candidate in Enum.GetValues(typeof(KeyCode)))
            {
                if (!ControlBindings.IsAllowedSkillKey(candidate)) continue;
                if (Input.GetKeyDown(candidate))
                {
                    key = candidate;
                    return true;
                }
            }

            key = KeyCode.None;
            return false;
        }

        void StartGame()
        {
            _listening = null;
            if (_hosting)
                GameSession.Instance.BeginHost(_pointsToWin, _marbleCount);
            else
                GameSession.Instance.BeginMatch(_pointsToWin, _marbleCount);
        }

        void ConfirmJoin()
        {
            string address = _joinAddress != null ? _joinAddress.text : "127.0.0.1";
            GameSession.Instance.BeginJoin(address, _pointsToWin, _marbleCount);
        }

        static void ExitGame()
        {
            Application.Quit();
#if UNITY_EDITOR
            Debug.Log("Exit requested (ignored while running in the Unity Editor).");
#endif
        }
    }
}
