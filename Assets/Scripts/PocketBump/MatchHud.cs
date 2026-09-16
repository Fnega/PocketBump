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

        void Awake()
        {
            ResolveReferences();
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
            hud.BuildResult(canvas);
            hud.ShowScores(0, 0);
            hud.ShowCountdown(-1f);
            hud.ShowBanner("");
            hud.WireResultButtons();
            return hud;
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
                }
            }
            Transform panel = transform.Find("ResultPanel");
            if (panel != null) _resultPanel = panel.gameObject;
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
                }
            }
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
            Color color = VisualPalette.PlayerColor(winner);
            _resultTitle.color = color;
            _resultTitle.text = winner == 0 ? "PLAYER 1 WINS" : "PLAYER 2 WINS";
            _resultPanel.SetActive(true);
            GameFx.Pulse(Vector2.zero, color, 0.5f, 5f, 0.7f);
        }
    }
}
