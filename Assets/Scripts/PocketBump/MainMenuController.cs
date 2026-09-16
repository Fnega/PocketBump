using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 构建并控制主菜单、规则设置、局域网加入和按键重绑定界面。
// 同时兼容编辑器烘焙层级与运行时动态 UI，便于正式场景和测试复用。
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
            EnsureHomeRegions();
            WireButtons();
            ShowRules(session.ConsumeOpenRulesRequest());
            ShowJoin(false);
            ShowError(session.ErrorMessage);
        }

        // 编辑器烘焙时可显式重建层级；运行时也调用同一套构建函数，避免两份 UI 定义。
        public void BuildAuthoredHierarchy()
        {
            _pointsToWin = MatchSettings.DefaultPointsToWin;
            _marbleCount = MatchSettings.DefaultMarbleCount;
            if (transform.Find("MainMenuCanvas") == null)
                BuildUi();
            ResolveReferences();
            EnsureControlsUi();
            EnsureHomeRegions();
            WireButtons();
            ShowRules(false);
            ShowJoin(false);
        }

        // 键位监听必须逐渲染帧检查 KeyDown；完成绑定后立即退出监听，避免同一键重复处理。
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

        // 代码化创建默认 UI，使空场景也能启动，并为 PlayMode 测试提供稳定对象名称。
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

        void EnsureHomeRegions()
        {
            if (_mainPanel == null) return;
            LayoutRect(_mainPanel.transform, new Vector2(0.5f, 0.36f), Vector2.zero,
                new Vector2(520f, 630f));
            _mainPanel.GetComponent<Image>().color = Color.clear;
            Transform start = EnsureRegion("StartRegion", "LOCAL PLAY", 230f, 140f, VisualPalette.PlayerOne);
            Transform server = EnsureRegion("ServerRegion", "SERVER / LAN", 34f, 220f, VisualPalette.Gate);
            Transform exit = EnsureRegion("ExitRegion", "EXIT", -162f, 140f, VisualPalette.Danger);
            EnsureHomeButton(start, "Start", "START", -22f, VisualPalette.PlayerOne);
            EnsureHomeButton(server, "HostLan", "HOST LAN", 18f, VisualPalette.Gate);
            EnsureHomeButton(server, "JoinLan", "JOIN LAN", -58f, VisualPalette.PlayerTwo);
            EnsureHomeButton(exit, "Exit", "EXIT", -22f, VisualPalette.Danger);
            if (_errorText == null)
                _errorText = UiFactory.Text(_mainPanel.transform, "Error", "", 18f,
                    TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(500f, 60f), VisualPalette.Danger);
            LayoutRect(_errorText.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -275f),
                new Vector2(500f, 60f));
            _errorText.enableWordWrapping = true;
            _errorText.enableAutoSizing = true;
            _errorText.fontSizeMin = 12f;
            _errorText.fontSizeMax = 18f;
            _errorText.overflowMode = TextOverflowModes.Ellipsis;

            Transform canvas = transform.Find("MainMenuCanvas");
            if (_joinPanel == null && canvas != null)
                BuildJoinPanel(canvas);
        }

        Transform EnsureRegion(string name, string title, float y, float height, Color accent)
        {
            Transform region = _mainPanel.transform.Find(name);
            if (region == null)
                region = UiFactory.Panel(_mainPanel.transform, name, new Vector2(0.5f, 0.5f),
                    Vector2.zero, Vector2.zero,
                    VisualPalette.WithAlpha(VisualPalette.TabletopAccent, 0.94f)).transform;
            LayoutRect(region, new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(470f, height));
            Transform heading = region.Find("Heading");
            if (heading == null)
                heading = UiFactory.Text(region, "Heading", title, 22f, TextAlignmentOptions.Center,
                    new Vector2(0.5f, 1f), Vector2.zero, new Vector2(420f, 32f), accent).transform;
            LayoutRect(heading, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(420f, 32f));
            return region;
        }

        void EnsureHomeButton(Transform region, string name, string label, float y, Color color)
        {
            Button found = null;
            foreach (Button button in _mainPanel.GetComponentsInChildren<Button>(true))
                if (button.name == name) { found = button; break; }
            if (found == null)
                found = UiFactory.Button(region, name, label, new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(300f, 64f), color, () => { });
            found.transform.SetParent(region, false);
            LayoutRect(found.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(300f, 64f));
        }

        static void LayoutRect(Transform target, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            var rect = target.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
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

        // 已烘焙场景通过约定名称恢复序列化引用，允许控制器脚本独立重载。
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

        // 先清除旧监听再绑定，防止 Awake/烘焙重建后一次点击触发多次场景跳转。
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

        // 进入一次性按键捕获状态；真正合法性检查交给 ControlBindings 统一处理。
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

        // UI 增减同样经过 MatchSettings 边界，网络握手双方才会对相同规则求哈希。
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

        // KeyCode 没有统一“任意键”事件，因此遍历枚举并返回当前帧第一个有效按键。
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

        // 将菜单值写入跨场景 GameSession，再由 ArenaSceneController 创建本局状态。
        void StartGame()
        {
            _listening = null;
            if (_hosting)
                GameSession.Instance.BeginHost(_pointsToWin, _marbleCount);
            else
                GameSession.Instance.BeginMatch(_pointsToWin, _marbleCount);
        }

        // Join 地址留到 Arena 初始化 socket 时解析，错误可沿统一 Abort 流程返回菜单。
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
