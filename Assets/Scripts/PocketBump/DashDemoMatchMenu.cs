using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PocketBump
{
    public sealed class DashDemoMatchMenu : MonoBehaviour
    {
        [SerializeField] DashMatchHud _hud;
        [SerializeField] GameObject _panel;
        [SerializeField] TMP_Text _mode, _reportStatus;
        [SerializeField] DashDemoGuide _guide;
        [SerializeField] TMP_Text[] _attackLabels;
        [SerializeField] Button _continue, _help, _leave, _reports;
        [SerializeField] Button _faultReports;
        [SerializeField] Slider _volume;
        bool _wired;
        string _directoryError = "";
        public event Action MainMenuRequested;
        public bool Visible => _panel != null && _panel.activeSelf;
        public bool HelpVisible => _guide != null && _guide.gameObject.activeSelf;
        public string ModeText => _mode != null ? _mode.text : "";
        public TMP_Text[] AttackLabels => _attackLabels;

        public static DashDemoMatchMenu Install(DashMatchHud hud)
        {
            var existing = hud.GetComponent<DashDemoMatchMenu>();
            if (existing != null) return existing;
            var menu = hud.gameObject.AddComponent<DashDemoMatchMenu>(); menu._hud = hud;
            TMP_FontAsset font = hud.Seats[0].Status.font;
            menu._attackLabels = new TMP_Text[2];
            for (int seat = 0; seat < 2; seat++)
            {
                menu._attackLabels[seat] = DemoUi.Label(hud.Seats[seat].Region, "AttackDirection", AttackText(seat), font, 22f,
                    new Vector2(.5f, 0f), Vector2.zero);
                RectTransform rect = menu._attackLabels[seat].rectTransform;
                rect.anchorMin = new Vector2(.02f, 0f); rect.anchorMax = new Vector2(.98f, 0f);
                rect.anchoredPosition = new Vector2(0f, 68f); rect.sizeDelta = new Vector2(0f, 42f);
            }
            RectTransform shade = DemoUi.Panel(hud.transform, "MatchMenuOverlay", new Color(0f, 0f, 0f, .4f), Vector2.zero, Vector2.one);
            shade.GetComponent<Image>().raycastTarget = true; menu._panel = shade.gameObject;
            RectTransform card = DemoUi.Panel(shade, "MatchMenuCard", DemoUi.PanelColor, new Vector2(.25f, .13f), new Vector2(.75f, .87f));
            DemoUi.Label(card, "MenuTitle", "比赛菜单 / ESC", font, 32f, new Vector2(.5f, .92f), new Vector2(.94f, .09f));
            menu._mode = DemoUi.Label(card, "MenuMode", "", font, 22f, new Vector2(.5f, .79f), new Vector2(.93f, .15f));
            menu._continue = DemoUi.Button(card, "ContinueMatch", "继续比赛", font, new Vector2(.5f, .64f), new Vector2(350f, 56f));
            menu._help = DemoUi.Button(card, "MatchHelp", "玩法说明", font, new Vector2(.5f, .53f), new Vector2(350f, 56f));
            menu._volume = DemoUi.Volume(card, font, new Vector2(.5f, .40f), new Vector2(390f, 80f));
            menu._reports = DemoUi.Button(card, "OpenReports", "打开报告目录", font, new Vector2(.5f, .27f), new Vector2(350f, 56f));
            menu._leave = DemoUi.Button(card, "LeaveMatch", "返回主菜单", font, new Vector2(.5f, .16f), new Vector2(350f, 56f));
            menu._reportStatus = DemoUi.Label(card, "ReportStatus", "", font, 17f, new Vector2(.5f, .06f), new Vector2(.94f, .08f));
            menu._guide = DashDemoGuide.Create(hud.transform, font);
            if (hud.NetworkIssueRect != null)
            {
                RectTransform issue = hud.NetworkIssueRect;
                issue.sizeDelta = new Vector2(issue.sizeDelta.x, 310f);
                MoveAnchor(issue.Find("NetworkIssueTitle"), .89f);
                MoveAnchor(issue.Find("NetworkIssueReason"), .72f);
                MoveAnchor(issue.Find("NetworkIssueDetail"), .52f);
                MoveAnchor(issue.Find("NetworkMainMenu"), .12f);
                menu._faultReports = DemoUi.Button(issue, "NetworkOpenReports", "打开报告目录", font,
                    new Vector2(.5f, .31f), new Vector2(360f, 43f));
            }
            shade.gameObject.SetActive(false); menu.Wire(); return menu;
        }
        static void MoveAnchor(Transform target, float y)
        {
            if (target is RectTransform rect) rect.anchorMin = rect.anchorMax = new Vector2(.5f, y);
        }
        public static string AttackText(int seat) => seat == 0 ? "P1 蓝方：向南进攻橙方球门 ↓    ESC 菜单" : "P2 橙方：向北进攻蓝方球门 ↑    ESC 菜单";
        void Awake() => Wire();
        void Wire()
        {
            if (_wired || _continue == null) return;
            _wired = true;
            _continue.onClick.AddListener(() => _hud.Director?.SetMenuOpen(false));
            _help.onClick.AddListener(OpenHelp);
            _leave.onClick.AddListener(() => { if (MainMenuRequested != null) MainMenuRequested(); else GameSession.Instance.ReturnToMainMenu(); });
            _reports.onClick.AddListener(() =>
            {
                bool opened = DashNetworkReports.TryOpenDirectory(out string error);
                SetDirectoryResult(opened, error);
            });
            if (_faultReports != null) _faultReports.onClick.AddListener(() =>
            {
                bool opened = DashNetworkReports.TryOpenDirectory(out string error);
                if (!opened) _hud.Director?.SetMenuOpen(true);
                SetDirectoryResult(opened, error);
            });
            _volume.onValueChanged.AddListener(DashAudioSettings.SetVolume);
            _guide.Refresh(new DashControlBindings(), false);
            _guide.CloseButton.onClick.AddListener(() => _guide.gameObject.SetActive(false));
        }
        void SetDirectoryResult(bool opened, string error)
        {
            _directoryError = opened ? "" : error ?? "";
            Refresh();
        }
        public void OpenHelp()
        {
            if (_hud == null || _hud.Director == null) return;
            _guide.Refresh(_hud.Director.Bindings, _hud.Director.Kind != PlayKind.Local);
            _guide.gameObject.SetActive(true); _guide.transform.SetAsLastSibling();
        }
        void LateUpdate() => Refresh();
        public void Refresh()
        {
            if (_hud == null || _hud.Director == null || _panel == null) return;
            Wire();
            DashMatchDirector director = _hud.Director;
            bool open = director.MenuOpen;
            if (open && !_panel.activeSelf) _volume.SetValueWithoutNotify(DashAudioSettings.Volume);
            _panel.SetActive(open);
            if (!open) _guide.gameObject.SetActive(false);
            _mode.text = director.NetworkPaused ? "网络问题：画面已暂停\n" + director.FailureReason
                : director.Kind == PlayKind.Local ? "本地比赛已暂停" : "局域网比赛仍在进行\n个人菜单不会暂停对手；你的角色仍可能受击";
            DashNetworkReporter reporter = director.GetComponent<DashNetworkReporter>();
            // Automatic save failures may recover; retain a manual folder-open
            // failure independently until an explicit open succeeds.
            _reportStatus.text = reporter != null && reporter.LastWriteError.Length > 0
                ? "报告保存失败：" + reporter.LastWriteError : _directoryError;
            // Keep the network fault notice visible above the personal menu.
            if (director.NetworkPaused && _hud.NetworkIssueRect != null) _hud.NetworkIssueRect.SetAsLastSibling();
        }
    }
}
