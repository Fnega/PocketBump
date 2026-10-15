using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 显示比分、倒计时、状态提示和赛后操作。
// HUD 只读取 MatchSim 的结果，避免 UI 回调成为另一套比赛规则来源。
namespace PocketBump
{
    public sealed class MatchHud : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI _score;
        [SerializeField] TextMeshProUGUI _countdown;
        [SerializeField] TextMeshProUGUI _banner;
        [SerializeField] TextMeshProUGUI _resultTitle;
        [SerializeField] GameObject _resultPanel;
        [SerializeField] GameObject _networkIssuePanel;
        [SerializeField] TextMeshProUGUI _networkIssueReason;
        [SerializeField] TextMeshProUGUI _networkIssueDetail;
        TextMeshProUGUI _lanRulesNote;
        Button _rematchButton;
        Button _modifyRulesButton;
        TextMeshProUGUI _rematchLabel;
        bool _showedConfirmedResult;

        public bool NetworkIssueVisible => _networkIssuePanel != null && _networkIssuePanel.activeSelf;
        public string NetworkIssueReason => _networkIssueReason != null ? _networkIssueReason.text : "";
        public RectTransform NetworkIssueRect => _networkIssuePanel != null
            ? _networkIssuePanel.GetComponent<RectTransform>() : null;

        void Awake()
        {
            ResolveReferences();
            EnsureNetworkIssuePanel();
            WireResultButtons();
        }

        // 动态构建与正式场景使用相同命名，便于 ResolveReferences 和 PlayMode 测试定位。
        public static MatchHud Build(Transform parent)
        {
            Transform canvas = UiFactory.CreateCanvas("MatchCanvas", parent);
            var hud = canvas.gameObject.AddComponent<MatchHud>();

            hud._score = UiFactory.Text(
                canvas,
                "Score",
                "",
                44f,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.955f),
                Vector2.zero,
                new Vector2(820f, 76f),
                VisualPalette.Text);
            hud._countdown = UiFactory.Text(
                canvas,
                "Countdown",
                "",
                96f,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(300f, 140f),
                VisualPalette.Gate);
            hud._banner = UiFactory.Text(
                canvas,
                "Banner",
                "",
                26f,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.055f),
                Vector2.zero,
                new Vector2(1600f, 68f),
                VisualPalette.Text);
            hud.EnsureNetworkIssuePanel();
            hud.BuildResult(canvas);
            hud.ShowScores(0, 0);
            hud.ShowCountdown(-1f);
            hud.ShowBanner("");
            hud.WireResultButtons();
            return hud;
        }

        void EnsureNetworkIssuePanel()
        {
            if (_networkIssuePanel != null) return;
            Transform existing = transform.Find("NetworkIssuePanel");
            if (existing != null)
            {
                _networkIssuePanel = existing.gameObject;
                ResolveReferences();
                return;
            }

            _networkIssuePanel = UiFactory.Panel(
                transform,
                "NetworkIssuePanel",
                new Vector2(1f, 1f),
                new Vector2(-235f, -155f),
                new Vector2(430f, 250f),
                VisualPalette.WithAlpha(VisualPalette.TabletopAccent, 0.97f));
            UiFactory.Text(
                _networkIssuePanel.transform,
                "NetworkIssueTitle",
                "NETWORK PAUSED",
                28f,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.82f),
                Vector2.zero,
                new Vector2(380f, 40f),
                VisualPalette.Danger);
            _networkIssueReason = UiFactory.Text(
                _networkIssuePanel.transform,
                "NetworkIssueReason",
                "",
                24f,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.62f),
                Vector2.zero,
                new Vector2(380f, 36f),
                VisualPalette.Text);
            _networkIssueDetail = UiFactory.Text(
                _networkIssuePanel.transform,
                "NetworkIssueDetail",
                "",
                17f,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.43f),
                Vector2.zero,
                new Vector2(380f, 52f),
                VisualPalette.Text);
            _networkIssueDetail.enableWordWrapping = true;
            UiFactory.Button(
                _networkIssuePanel.transform,
                "NetworkMainMenu",
                "MAIN MENU",
                new Vector2(0.5f, 0.16f),
                Vector2.zero,
                new Vector2(250f, 50f),
                VisualPalette.Wall,
                ReturnToMenuFromNetworkIssue);
            _networkIssuePanel.SetActive(false);
        }

        void BuildResult(Transform canvas)
        {
            _resultPanel = UiFactory.Panel(
                canvas,
                "ResultPanel",
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(760f, 500f),
                VisualPalette.WithAlpha(VisualPalette.TabletopAccent, 0.97f));
            _resultTitle = UiFactory.Text(
                _resultPanel.transform,
                "Winner",
                "",
                62f,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.76f),
                Vector2.zero,
                new Vector2(620f, 100f),
                VisualPalette.Text);

            UiFactory.Button(
                _resultPanel.transform,
                "Rematch",
                "REMATCH",
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(340f, 70f),
                VisualPalette.PlayerOne,
                () => GameSession.Instance.Rematch());
            UiFactory.Button(
                _resultPanel.transform,
                "ModifyRules",
                "MODIFY RULES",
                new Vector2(0.5f, 0.31f),
                Vector2.zero,
                new Vector2(340f, 70f),
                VisualPalette.PlayerTwo,
                () => GameSession.Instance.ModifyRules());
            UiFactory.Button(
                _resultPanel.transform,
                "MainMenu",
                "MAIN MENU",
                new Vector2(0.5f, 0.12f),
                Vector2.zero,
                new Vector2(340f, 70f),
                VisualPalette.Wall,
                () => GameSession.Instance.ReturnToMainMenu());
            _resultPanel.SetActive(false);
        }

        void ResolveReferences()
        {
            TextMeshProUGUI[] texts = GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (TextMeshProUGUI text in texts)
            {
                switch (text.name)
                {
                    case "Score": _score = text; break;
                    case "Countdown": _countdown = text; break;
                    case "Banner": _banner = text; break;
                    case "Winner": _resultTitle = text; break;
                    case "NetworkIssueReason": _networkIssueReason = text; break;
                    case "NetworkIssueDetail": _networkIssueDetail = text; break;
                }
            }
            Transform panel = transform.Find("ResultPanel");
            if (panel != null) _resultPanel = panel.gameObject;
            Transform networkPanel = transform.Find("NetworkIssuePanel");
            if (networkPanel != null) _networkIssuePanel = networkPanel.gameObject;
        }

        // 重新绑定前清空监听，避免编辑器烘焙监听和运行时监听叠加。
        void WireResultButtons()
        {
            Button[] buttons = GetComponentsInChildren<Button>(true);
            foreach (Button button in buttons)
            {
                button.onClick.RemoveAllListeners();
                switch (button.name)
                {
                    case "Rematch": button.onClick.AddListener(() => GameSession.Instance.Rematch()); break;
                    case "ModifyRules": button.onClick.AddListener(() => GameSession.Instance.ModifyRules()); break;
                    case "MainMenu": button.onClick.AddListener(() => GameSession.Instance.ReturnToMainMenu()); break;
                    case "NetworkMainMenu": button.onClick.AddListener(ReturnToMenuFromNetworkIssue); break;
                }
            }
        }

        public void ShowNetworkIssue(string reason)
        {
            EnsureNetworkIssuePanel();
            if (_resultPanel != null) _resultPanel.SetActive(false);
            _networkIssueReason.text = string.IsNullOrEmpty(reason) ? "NETWORK ERROR" : reason;
            _networkIssueDetail.text = NetworkIssueDescription(reason);
            _networkIssuePanel.SetActive(true);
        }

        public static string NetworkIssueDescription(string reason)
        {
            switch (reason)
            {
                case "INPUT HOLE": return "REMOTE INPUT COULD NOT BE RECOVERED";
                case "DESYNC": return "THE TWO GAME STATES NO LONGER MATCH";
                case "PEER TIMEOUT": return "NO VALID PACKETS RECEIVED FOR 5 SECONDS";
                case "PEER LEFT": return "THE OTHER PLAYER LEFT THE MATCH";
                case "LAN HANDSHAKE TIMEOUT": return "THE CONNECTION COULD NOT BE ESTABLISHED";
                case "SETTINGS MISMATCH": return "PLAYERS USE DIFFERENT MATCH SETTINGS";
                case "COULD NOT BIND PORT": return "UDP PORT 7777 IS NOT AVAILABLE";
                case "COULD NOT OPEN SOCKET": return "THE NETWORK SOCKET COULD NOT BE OPENED";
                case "BAD ADDRESS": return "THE HOST ADDRESS IS INVALID";
                case "RESULT CONFIRMATION TIMEOUT": return "THE FINAL RESULT COULD NOT BE CONFIRMED";
                case "REMATCH TIMEOUT": return "THE NEXT MATCH COULD NOT BE STARTED";
                case "REMATCH EPOCH EXHAUSTED": return "LEAVE AND HOST A NEW SESSION";
                default: return "THE NETWORK SESSION HAS STOPPED";
            }
        }

        void ReturnToMenuFromNetworkIssue()
        {
            Time.timeScale = 1f;
            GameSession.Instance.ReturnToMainMenu();
        }

        public void ShowScores(int p1, int p2)
        {
            string cyan = ColorUtility.ToHtmlStringRGB(VisualPalette.PlayerOne);
            string orange = ColorUtility.ToHtmlStringRGB(VisualPalette.PlayerTwo);
            _score.text = $"<color=#{cyan}>P1  {p1}</color>    -    <color=#{orange}>{p2}  P2</color>";
        }

        public void ShowCountdown(float secondsLeft)
        {
            _countdown.text = secondsLeft < 0f
                ? ""
                : Mathf.CeilToInt(secondsLeft).ToString();
        }

        public void ShowBanner(string text)
        {
            _banner.text = text;
            _banner.color = VisualPalette.Text;
        }

        public void ShowScoreEvent(int scorer, string reason)
        {
            _banner.text = reason;
            _banner.color = VisualPalette.PlayerColor(scorer);
            StopCoroutine(nameof(ScorePulse));
            StartCoroutine(ScorePulse());
        }

        // 分数缩放只使用渲染时间，是纯表现动画，不写回确定性 MatchSim。
        IEnumerator ScorePulse()
        {
            float age = 0f;
            const float duration = 0.35f;
            while (age < duration)
            {
                age += Time.deltaTime;
                float t = age / duration;
                float scale = 1f + Mathf.Sin(t * Mathf.PI) * 0.22f;
                _score.rectTransform.localScale = Vector3.one * scale;
                yield return null;
            }
            _score.rectTransform.localScale = Vector3.one;
        }

        public void ShowResult(int winner)
        {
            ConfigureResultMode(false);
            Color color = VisualPalette.PlayerColor(winner);
            _resultTitle.color = color;
            _resultTitle.text = winner == 0 ? "PLAYER 1 WINS" : "PLAYER 2 WINS";
            _resultPanel.SetActive(true);
            GameFx.Pulse(Vector2.zero, color, 0.5f, 5f, 0.7f);
        }

        void ConfigureResultMode(bool lan)
        {
            if (_resultPanel == null) return;
            if (_rematchButton == null)
            {
                _rematchButton = _resultPanel.transform.Find("Rematch").GetComponent<Button>();
                _modifyRulesButton = _resultPanel.transform.Find("ModifyRules").GetComponent<Button>();
                _rematchLabel = _rematchButton.GetComponentInChildren<TextMeshProUGUI>(true);
            }
            if (lan && _lanRulesNote == null)
            {
                Transform existing = _resultPanel.transform.Find("LanRulesNote");
                _lanRulesNote = existing != null ? existing.GetComponent<TextMeshProUGUI>() : UiFactory.Text(
                    _resultPanel.transform, "LanRulesNote",
                    "REMATCH USES THE SAME RULES\nTO CHANGE RULES: MAIN MENU > HOST LAN", 20f,
                    TextAlignmentOptions.Center, new Vector2(0.5f, 0.31f), Vector2.zero,
                    new Vector2(680f, 76f), VisualPalette.Text);
                _lanRulesNote.enableWordWrapping = true;
            }
            _modifyRulesButton.gameObject.SetActive(!lan);
            _modifyRulesButton.interactable = !lan;
            if (_lanRulesNote != null) _lanRulesNote.gameObject.SetActive(lan);
            _rematchButton.interactable = true;
            _rematchLabel.text = "REMATCH";
            _resultTitle.fontSize = 62f;
        }

        public void ShowLanLifecycle(LanMatchLifecycle lifecycle)
        {
            if (lifecycle == null || _resultPanel == null) return;
            if (lifecycle.Phase == LanLifecyclePhase.Playing)
            {
                _resultPanel.SetActive(false);
                _showedConfirmedResult = false;
                return;
            }
            if (lifecycle.Phase == LanLifecyclePhase.Failed) return;
            ConfigureResultMode(true);
            _resultPanel.SetActive(true);
            bool starting = lifecycle.Phase == LanLifecyclePhase.PreparingNextEpoch ||
                lifecycle.Phase == LanLifecyclePhase.StartupBarrier;
            _rematchButton.interactable = lifecycle.ResultConfirmed && !lifecycle.LocalConsent && !starting;
            _rematchLabel.text = starting ? "STARTING..." : lifecycle.LocalConsent ? "WAITING FOR PEER" : "REMATCH";
            if (!lifecycle.ResultConfirmed || starting)
            {
                _resultTitle.fontSize = 36f;
                _resultTitle.color = VisualPalette.Text;
                _resultTitle.text = starting
                    ? (lifecycle.Epoch == 0 ? "STARTING MATCH" : "STARTING NEXT MATCH")
                    : "CONFIRMING RESULT";
                return;
            }
            int winner = lifecycle.ConfirmedFinish.Winner;
            Color color = VisualPalette.PlayerColor(winner);
            _resultTitle.color = color;
            _resultTitle.text = winner == 0 ? "PLAYER 1 WINS" : "PLAYER 2 WINS";
            if (!_showedConfirmedResult)
            {
                _showedConfirmedResult = true;
                GameFx.Pulse(Vector2.zero, color, 0.5f, 5f, 0.7f);
            }
        }

        public void ResetForMatch()
        {
            StopAllCoroutines();
            _showedConfirmedResult = false;
            if (_score != null) _score.rectTransform.localScale = Vector3.one;
            if (_resultPanel != null) _resultPanel.SetActive(false);
            if (_networkIssuePanel != null) _networkIssuePanel.SetActive(false);
            ShowBanner("");
        }
    }
}
