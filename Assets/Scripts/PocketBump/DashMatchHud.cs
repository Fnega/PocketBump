using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PocketBump
{
    // Authored, passive TMP UI. The director owns rules, time, results and all network state.
    public sealed class DashMatchHud : MonoBehaviour
    {
        [Serializable] public sealed class SeatView
        {
            public RectTransform Region;
            public TMP_Text Status, Buffs, Pickup;
            public GameObject PickupPanel;
            public Transform[] PickupIcons;
            public TMP_Text[] Stun;
            public DashArenaMinimap Minimap;
        }
        public const string RequiredChineseGlyphs = "晕绿蓝紫冲击强化霸体安全传送无位置撞击玩家与球持续秒免击退眩晕仍可掉落成功速度清零未找到拾取已消耗，、。·×网络暂停两端状态不一致检查双方版本输入缺失连接中断返回主菜单重赛修改规则等待对方确认胜利开球锁定复活或画面收到新后自动继续进展超时保持请";
        public const string InputWaitTitle = "NETWORK WAIT / 等待输入";
        public const string InputWaitReason = "INPUT PACING WAIT";
        public const string InputWaitDetail = "等待对方输入。\n收到新输入后自动继续。";
        public const string InputProgressTimeoutDetail = "No peer input progress for 5 seconds.\nOther peer traffic may still be arriving.";
        public const string PeerTimeoutDetail = "No accepted peer traffic for 5 seconds.\nMatch frozen. Return to the main menu.";
        public const string PeerReportedTimeoutDetail = "Peer received no accepted traffic for 5s.\nMatch frozen. Return to the main menu.";
        public const string PeerReportedInputTimeoutDetail = "Peer reported no input progress for 5s.\nMatch frozen. Return to the main menu.";
        public const string PeerLeftDetail = "The other player left the match.\nReturn to the main menu when ready.";
        [SerializeField] SeatView[] _seats;
        [SerializeField] TMP_Text _score, _banner, _resultTitle, _lanRulesNote, _networkReason, _networkDetail;
        [SerializeField] TMP_Text _networkTitle;
        [SerializeField] GameObject _resultPanel, _networkPanel;
        [SerializeField] Button _rematch, _modifyRules, _mainMenu, _networkMainMenu;
        [SerializeField] GameObject _divider;
        readonly string[] _messages = { "", "" };
        readonly float[] _messageUntil = new float[2];
        readonly DashPickupKind[] _messageKinds = new DashPickupKind[2];
        [SerializeField] Color _playerOneColor, _playerTwoColor;
        [SerializeField] int _minimapLayoutVersion;
        readonly Vector3[] _uiCorners = new Vector3[4];
        string _transitionMessage = "";
        float _transitionUntil;
        DashMatchDirector _director;
        bool _wired, _lan;
        int _localSeat, _epoch = -1;

        public event Action MainMenuRequested;
        public event Action ModifyRulesRequested;
        public bool NetworkNoticeVisible => _networkPanel != null && _networkPanel.activeSelf;
        public bool NetworkIssueVisible => NetworkNoticeVisible && _director != null && _director.NetworkPaused;
        public bool NetworkWaitingVisible => NetworkNoticeVisible && _director != null && _director.WaitingForRemoteInput;
        public string NetworkIssueTitle => _networkTitle != null ? _networkTitle.text : "";
        public string NetworkIssueReason => _networkReason != null ? _networkReason.text : "";
        public string NetworkIssueDetail => _networkDetail != null ? _networkDetail.text : "";
        public RectTransform NetworkIssueRect => _networkPanel != null ? (RectTransform)_networkPanel.transform : null;
        public bool ResultVisible => _resultPanel != null && _resultPanel.activeSelf;
        public SeatView[] Seats => _seats;
        public DashMatchDirector Director => _director;
        public string MessageForSeat(int seat) => _messages[seat];

        public static DashMatchHud Build(Transform parent, TMP_FontAsset approvedFont)
        {
            var canvasObject = new GameObject("DashMatchCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(parent, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 1000f); scaler.matchWidthOrHeight = .5f;
            var hud = canvasObject.AddComponent<DashMatchHud>();
            DashUiTheme theme = DashUiTheme.Load();
            Color panel = theme != null ? theme.Panel : new Color(.83f, .93f, .85f);
            Color ink = theme != null ? theme.Text : new Color(.10f, .23f, .23f);
            Color button = theme != null ? theme.Mint : new Color(.57f, .83f, .66f);
            Color danger = theme != null ? theme.Danger : new Color(.86f, .29f, .30f);
            hud._playerOneColor = theme != null ? theme.Cyan : new Color(.28f, .78f, .83f);
            hud._playerTwoColor = theme != null ? theme.Orange : new Color(1f, .67f, .36f);
            panel.a = .93f;
            RectTransform canvas = (RectTransform)canvasObject.transform;
            hud._score = Text(canvas, "Score", approvedFont, ink, 31f, new Vector2(.5f, 1f), new Vector2(0f, -30f), new Vector2(490f, 52f));
            hud._banner = Text(canvas, "Banner", approvedFont, ink, 23f, new Vector2(.5f, 0f), new Vector2(0f, 28f), new Vector2(1150f, 48f));
            hud._seats = new SeatView[2];
            for (int seat = 0; seat < 2; seat++)
            {
                RectTransform region = Rect(canvas, "Seat" + seat, new Vector2(seat * .5f, 0f), new Vector2((seat + 1) * .5f, 1f));
                region.gameObject.AddComponent<RectMask2D>();
                RectTransform status = Panel(region, "StatusPanel", panel, new Vector2(0f, 1f), new Vector2(1f, 1f));
                status.offsetMin = new Vector2(12f, -158f); status.offsetMax = new Vector2(-12f, -64f);
                TMP_Text statusLabel = StretchText(status, "Status", approvedFont, ink, 23f, new Vector2(10f, 39f), new Vector2(-10f, -7f));
                TMP_Text buffs = StretchText(status, "BuffTimers", approvedFont, ink, 20f, new Vector2(10f, 6f), new Vector2(-10f, -51f));
                RectTransform pickup = Panel(region, "PickupPanel", panel, new Vector2(0f, 1f), new Vector2(1f, 1f));
                pickup.offsetMin = new Vector2(12f, -270f); pickup.offsetMax = new Vector2(-12f, -170f);
                TMP_Text description = StretchText(pickup, "PickupDescription", approvedFont, ink, 24f, new Vector2(70f, 8f), new Vector2(-13f, -8f));
                Transform[] icons = BuildBuffIcons(pickup, ink);
                var labels = new TMP_Text[2];
                for (int actor = 0; actor < 2; actor++)
                {
                    labels[actor] = Text(region, "Stun" + actor, approvedFont, ink, 32f, Vector2.one * .5f, Vector2.zero, new Vector2(60f, 45f));
                    labels[actor].text = "晕"; labels[actor].gameObject.SetActive(false);
                }
                pickup.gameObject.SetActive(false);
                hud._seats[seat] = new SeatView { Region = region, Status = statusLabel, Buffs = buffs, Pickup = description, PickupPanel = pickup.gameObject, PickupIcons = icons, Stun = labels };
            }
            RectTransform divider = Panel(canvas, "ViewportDivider", ink, new Vector2(.5f, 0f), new Vector2(.5f, 1f));
            divider.offsetMin = new Vector2(-1f, 0f); divider.offsetMax = new Vector2(1f, -58f); hud._divider = divider.gameObject;
            RectTransform result = Panel(canvas, "ResultPanel", panel, new Vector2(.23f, .25f), new Vector2(.77f, .75f));
            hud._resultPanel = result.gameObject;
            hud._resultTitle = Text(result, "Winner", approvedFont, ink, 42f, new Vector2(.5f, .84f), Vector2.zero, new Vector2(700f, 65f));
            hud._rematch = ButtonAt(result, "Rematch", "REMATCH / 重赛", approvedFont, ink, button, .61f);
            hud._modifyRules = ButtonAt(result, "ModifyRules", "RULES / 修改规则", approvedFont, ink, button, .43f);
            hud._mainMenu = ButtonAt(result, "MainMenu", "MAIN MENU / 返回主菜单", approvedFont, ink, button, .25f);
            hud._lanRulesNote = Text(result, "LanRulesNote", approvedFont, ink, 20f, new Vector2(.5f, .08f), Vector2.zero, new Vector2(730f, 65f));
            result.gameObject.SetActive(false);
            RectTransform issue = Panel(canvas, "NetworkIssuePanel", panel, Vector2.one, Vector2.one);
            issue.pivot = Vector2.one; issue.anchoredPosition = new Vector2(-18f, -18f); issue.sizeDelta = new Vector2(465f, 245f);
            hud._networkPanel = issue.gameObject;
            hud._networkTitle = Text(issue, "NetworkIssueTitle", approvedFont, danger, 26f, new Vector2(.5f, .87f), Vector2.zero, new Vector2(430f, 42f));
            hud._networkTitle.text = "NETWORK PAUSED / 网络暂停";
            hud._networkReason = Text(issue, "NetworkIssueReason", approvedFont, ink, 26f, new Vector2(.5f, .68f), Vector2.zero, new Vector2(430f, 43f));
            hud._networkDetail = Text(issue, "NetworkIssueDetail", approvedFont, ink, 20f, new Vector2(.5f, .46f), Vector2.zero, new Vector2(430f, 61f));
            hud._networkMainMenu = ButtonAt(issue, "NetworkMainMenu", "MAIN MENU / 返回主菜单", approvedFont, ink, button, .16f);
            issue.gameObject.SetActive(false);
            hud.InstallMinimapAndUpgradeLayout();
            DashDemoMatchMenu.Install(hud);
            hud.WireButtons();
            return hud;
        }

        void Awake() => WireButtons();
        void LateUpdate() => RefreshFromDirector();

        // Explicit saved-scene migration, never called from Awake or Bind. Its version
        // guard lets subsequent author edits survive both repeated installation and play.
        public bool InstallMinimapAndUpgradeLayout()
        {
            if (_seats == null) return false;
            bool changed = false;
            foreach (SeatView view in _seats)
            {
                if (view == null || view.Region == null) continue;
                if (view.Minimap == null)
                {
                    Transform existing = view.Region.Find("Minimap");
                    if (existing != null) view.Minimap = existing.GetComponent<DashArenaMinimap>();
                    if (view.Minimap == null)
                    {
                        RectTransform rect = Rect(view.Region, "Minimap", Vector2.one, Vector2.one);
                        rect.pivot = Vector2.one;
                        rect.anchoredPosition = new Vector2(-12f, -64f);
                        rect.sizeDelta = new Vector2(190f, 288f);
                        view.Minimap = rect.gameObject.AddComponent<DashArenaMinimap>();
                        view.Minimap.PlayerOneColor = _playerOneColor;
                        view.Minimap.PlayerTwoColor = _playerTwoColor;
                        TMP_FontAsset font = view.Status != null ? view.Status.font : null;
                        Text(rect, "PlayerOneKey", font, _playerOneColor, 15f, new Vector2(.19f, 1f), new Vector2(0f, -15f), new Vector2(42f, 25f)).text = "P1";
                        Text(rect, "North", font, Color.white, 16f, new Vector2(.5f, 1f), new Vector2(0f, -15f), new Vector2(42f, 25f)).text = "N";
                        Text(rect, "PlayerTwoKey", font, _playerTwoColor, 15f, new Vector2(.81f, 1f), new Vector2(0f, -15f), new Vector2(42f, 25f)).text = "P2";
                    }
                    changed = true;
                }
                if (_minimapLayoutVersion < 1)
                {
                    Transform status = view.Region.Find("StatusPanel");
                    if (status is RectTransform statusRect)
                        statusRect.offsetMax = new Vector2(-214f, statusRect.offsetMax.y);
                    if (view.PickupPanel != null)
                    {
                        var pickupRect = (RectTransform)view.PickupPanel.transform;
                        pickupRect.offsetMax = new Vector2(-214f, pickupRect.offsetMax.y);
                    }
                }
            }
            if (_minimapLayoutVersion < 1)
            {
                if (NetworkIssueRect != null)
                {
                    NetworkIssueRect.anchorMin = NetworkIssueRect.anchorMax = new Vector2(1f, 0f);
                    NetworkIssueRect.pivot = new Vector2(1f, 0f);
                    NetworkIssueRect.anchoredPosition = new Vector2(-18f, 74f);
                }
                _minimapLayoutVersion = 1;
                changed = true;
            }
            return changed;
        }

        // Camera-local normalized clear band. Reserve inactive pickup space too, so
        // confirmation popups do not cause the gameplay camera to jump. The map is
        // a top obstruction, not a full-height exclusion of the right of the arena.
        public Rect GetGoalSafeViewport(Camera camera)
        {
            if (camera == null || _seats == null) return new Rect(.03f, .10f, .94f, .54f);
            Rect pixels = camera.pixelRect;
            if (pixels.width <= 1f || pixels.height <= 1f) return new Rect(.03f, .10f, .94f, .54f);
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            float top = .92f, bottom = .08f;
            bool found = false;
            foreach (SeatView view in _seats)
            {
                if (view == null || view.Region == null || !view.Region.gameObject.activeInHierarchy) continue;
                Rect region = UiScreenRect(view.Region, uiCamera);
                float overlap = Mathf.Min(region.xMax, pixels.xMax) - Mathf.Max(region.xMin, pixels.xMin);
                if (overlap < pixels.width * .5f) continue;
                found = true;
                if (view.Status != null) ReserveTop(view.Status.transform.parent as RectTransform, uiCamera, pixels, ref top);
                if (view.PickupPanel != null) ReserveTop(view.PickupPanel.transform as RectTransform, uiCamera, pixels, ref top);
                if (view.Minimap != null) ReserveTop(view.Minimap.RectTransform, uiCamera, pixels, ref top);
            }
            if (!found) return new Rect(.03f, .10f, .94f, .54f);
            if (_banner != null)
                bottom = Mathf.Max(bottom, (UiScreenRect(_banner.rectTransform, uiCamera).yMax - pixels.yMin) / pixels.height + .015f);
            top = Mathf.Clamp(top, bottom + .15f, .92f);
            return UnityEngine.Rect.MinMaxRect(.03f, bottom, .97f, top);
        }
        void ReserveTop(RectTransform rect, Camera uiCamera, Rect pixels, ref float top)
        {
            if (rect != null)
                top = Mathf.Min(top, (UiScreenRect(rect, uiCamera).yMin - pixels.yMin) / pixels.height - .015f);
        }
        Rect UiScreenRect(RectTransform rect, Camera uiCamera)
        {
            rect.GetWorldCorners(_uiCorners);
            Vector2 first = RectTransformUtility.WorldToScreenPoint(uiCamera, _uiCorners[0]);
            Vector2 min = first, max = first;
            for (int i = 1; i < 4; i++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(uiCamera, _uiCorners[i]);
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            return UnityEngine.Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        void WireButtons()
        {
            // Existing saved scenes already contain this label. Resolve the newly
            // serialized reference without rebuilding or restyling authored UI.
            if (_networkTitle == null && _networkPanel != null)
            {
                Transform title = _networkPanel.transform.Find("NetworkIssueTitle");
                if (title != null) _networkTitle = title.GetComponent<TMP_Text>();
            }
            if (_wired || _rematch == null) return;
            _wired = true;
            // A validated peer cause can be longer than a local reason. Fit it in
            // the existing authored label without moving the panel or its actions.
            _networkReason.enableAutoSizing = true;
            _networkReason.fontSizeMin = Mathf.Min(_networkReason.fontSizeMin, 16f);
            _rematch.onClick.AddListener(() => { if (_director != null && _director.CanRequestRematch) _director.RequestRematch(); });
            _modifyRules.onClick.AddListener(() =>
            {
                if (_director == null || !_director.CanModifyRules) return;
                if (ModifyRulesRequested != null) ModifyRulesRequested(); else GameSession.Instance.ModifyRules();
            });
            _mainMenu.onClick.AddListener(ReturnToMainMenu); _networkMainMenu.onClick.AddListener(ReturnToMainMenu);
        }
        void ReturnToMainMenu()
        {
            if (MainMenuRequested != null) MainMenuRequested(); else GameSession.Instance.ReturnToMainMenu();
        }
        public void Bind(DashMatchDirector director)
        {
            if (director != _director || _epoch != director.MatchEpoch) ClearPickupMessages();
            _director = director; _epoch = director.MatchEpoch;
            WireButtons(); RefreshFromDirector();
        }
        public void RefreshFromDirector()
        {
            if (_director == null || _director.Sim == null || _seats == null) return;
            _lan = _director.Kind != PlayKind.Local; _localSeat = _director.LocalSeat;
            DashArenaState state = _director.Sim;
            _score.text = "<color=#" + ColorUtility.ToHtmlStringRGB(_playerOneColor) + ">P1  " + state.Scores[0] + "</color>  :  <color=#" +
                ColorUtility.ToHtmlStringRGB(_playerTwoColor) + ">" + state.Scores[1] + "  P2</color>";
            _banner.text = _director.StatusMessage.Length > 0 ? _director.StatusMessage : _transitionUntil > Time.unscaledTime ? _transitionMessage : "";
            _divider.SetActive(!_lan);
            for (int seat = 0; seat < 2; seat++)
            {
                SeatView view = _seats[seat];
                view.Region.gameObject.SetActive(!_lan || seat == _localSeat);
                view.Region.anchorMin = _lan ? Vector2.zero : new Vector2(seat * .5f, 0f);
                view.Region.anchorMax = _lan ? Vector2.one : new Vector2((seat + 1) * .5f, 1f);
                view.Region.offsetMin = view.Region.offsetMax = Vector2.zero;
                if (_lan && seat != _localSeat) continue;
                if (view.Minimap != null) view.Minimap.SetState(state, seat);
                int bindingSeat = _lan ? 0 : seat;
                DashPlayerState player = state.Players[seat];
                view.Status.text = "P" + (seat + 1) + (bindingSeat == 0 ? "  WASD + " : "  ARROWS + ") +
                    DashControlBindings.Label(_director.Bindings.Dash(bindingSeat)) + "    " + Status(state, seat);
                view.Buffs.text = "GREEN " + Seconds(player.GreenTicks) + "s    PURPLE " + Seconds(player.PurpleTicks) + "s";
                bool message = _messageUntil[seat] > Time.unscaledTime;
                view.PickupPanel.SetActive(message); view.Pickup.text = message ? _messages[seat] : "";
                if (view.PickupIcons != null) for (int i = 0; i < view.PickupIcons.Length; i++) view.PickupIcons[i].gameObject.SetActive(i == (int)_messageKinds[seat]);
                UpdateStunLabels(view, seat, state);
            }
            // Recoverable input pacing stays silent; terminal faults remain actionable.
            _networkPanel.SetActive(_director.NetworkPaused);
            if (_director.NetworkPaused)
            {
                if (_networkTitle != null) _networkTitle.text = "NETWORK PAUSED / 网络暂停";
                DashLanSession session = _director.LanSession;
                bool peerFault = session != null && session.FailureOrigin == "peer" && session.PeerFailureReason.Length > 0;
                string reason = peerFault ? session.PeerFailureReason : _director.FailureReason;
                _networkReason.text = peerFault ? "PEER FAULT: " + reason : reason;
                _networkDetail.text = NetworkFailureDetail(reason, peerFault);
            }
            bool waitingLifecycle = _lan && _director.Lifecycle != null &&
                (_director.Lifecycle.Phase == LanLifecyclePhase.SettlingResult ||
                 _director.Lifecycle.Phase == LanLifecyclePhase.PreparingNextEpoch ||
                 _director.Lifecycle.Phase == LanLifecyclePhase.StartupBarrier);
            _resultPanel.SetActive((_director.ResultsReady || waitingLifecycle) && !_director.NetworkPaused);
            if (_resultPanel.activeSelf)
            {
                _resultTitle.text = _director.ResultsReady ? "P" + (_director.Winner + 1) + " WINS / 胜利" : _director.StatusMessage;
                _rematch.interactable = _director.CanRequestRematch;
                _rematch.GetComponentInChildren<TMP_Text>().text = _lan && _director.Lifecycle != null && _director.Lifecycle.LocalConsent
                    ? "WAITING FOR PEER" : "REMATCH / 重赛";
                _modifyRules.gameObject.SetActive(!_lan);
                _modifyRules.interactable = _director.CanModifyRules;
                _lanRulesNote.gameObject.SetActive(_lan);
                _lanRulesNote.text = _lan ? "LAN: SAME RULES / 等待对方确认\n" + _director.StatusMessage : "";
            }
        }
        static string NetworkFailureDetail(string reason, bool peerFault)
        {
            if (reason == "PEER TIMEOUT") return peerFault ? PeerReportedTimeoutDetail : PeerTimeoutDetail;
            if (reason == "INPUT PROGRESS TIMEOUT") return peerFault ? PeerReportedInputTimeoutDetail : InputProgressTimeoutDetail;
            if (reason == "PEER LEFT") return PeerLeftDetail;
            if (reason.IndexOf("DESYNC", StringComparison.OrdinalIgnoreCase) >= 0)
                return peerFault ? "Peer reported different match states.\nCheck both players use the same version." : "两端状态不一致。检查双方版本。";
            return peerFault ? "Peer reported a terminal fault.\nMatch frozen. Return to the main menu." : "连接中断或输入缺失。画面已暂停。";
        }
        void UpdateStunLabels(SeatView view, int seat, DashArenaState state)
        {
            DashArenaPresentation presentation = _director.Presentation;
            if (presentation == null || presentation.Views == null) return;
            Camera camera = presentation.Views.GameplayCameras[_lan ? 0 : seat];
            for (int actor = 0; actor < 2; actor++)
            {
                TMP_Text label = view.Stun[actor];
                Vector3 at = camera.WorldToViewportPoint(presentation.Views.Players[actor].Root.position + Vector3.up * 2.35f);
                bool visible = state.Players[actor].Alive && state.Players[actor].StunTicks > 0 && at.z > 0f && at.x >= 0f && at.x <= 1f && at.y >= 0f && at.y <= 1f;
                label.gameObject.SetActive(visible);
                if (!visible) continue;
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(Mathf.Clamp(at.x, .07f, .93f), Mathf.Clamp(at.y, .08f, .75f));
                label.rectTransform.anchoredPosition = Vector2.zero;
            }
        }
        public void ShowConfirmedPickup(DashArenaEvent item, ArenaRulesProfile rules)
        {
            if (item.Seat < 0 || item.Seat > 1 || (_lan && item.Seat != _localSeat)) return;
            string message = null;
            if (item.Kind == DashArenaEventKind.Pickup && item.PickupKind == DashPickupKind.Green)
                message = "绿·冲击强化 / GREEN\n撞击玩家与球 ×" + rules.GreenImpactMultiplier.ToString("0.0") + "，持续 " + Seconds(rules.GreenTicks) + " 秒";
            else if (item.Kind == DashArenaEventKind.Pickup && item.PickupKind == DashPickupKind.Purple)
                message = "紫·霸体 / PURPLE\n免击退、免眩晕 " + Seconds(rules.PurpleTicks) + " 秒，仍可掉落";
            else if (item.Kind == DashArenaEventKind.Teleport) message = "蓝·安全传送 / BLUE\n传送成功，速度清零";
            else if (item.Kind == DashArenaEventKind.TeleportFailed) message = "蓝·安全传送 / BLUE\n未找到安全位置，拾取已消耗";
            if (message == null) return;
            _messageKinds[item.Seat] = item.PickupKind ?? DashPickupKind.Blue;
            _messages[item.Seat] = message; _messageUntil[item.Seat] = Time.unscaledTime + 2.8f;
        }
        public void ShowConfirmedTransition(DashArenaEvent item)
        {
            if (item.Kind == DashArenaEventKind.Goal && item.Seat >= 0 && item.Seat < 2)
            {
                Color identity = item.Seat == 0 ? _playerOneColor : _playerTwoColor;
                _transitionMessage = "<color=#" + ColorUtility.ToHtmlStringRGB(identity) + ">P" + (item.Seat + 1) + " GOAL!</color>";
                _transitionUntil = Time.unscaledTime + 1.6f;
            }
            else if (item.Kind == DashArenaEventKind.GateOpened)
            {
                _transitionMessage = "GO!"; _transitionUntil = Time.unscaledTime + .7f;
            }
        }
        public void ClearPickupMessages()
        {
            for (int i = 0; i < 2; i++) { _messages[i] = ""; _messageUntil[i] = 0f; }
            _transitionUntil = 0f; _transitionMessage = "";
        }
        public static string Status(DashArenaState state, int seat)
        {
            DashPlayerState player = state.Players[seat];
            if (!player.Alive) return "RESPAWN " + Seconds(player.RespawnTicks) + "s";
            if (state.Phase == MatchState.Kickoff) return "KICKOFF " + Seconds(state.PhaseTicks) + "s / DASH LOCKED";
            if (state.Phase != MatchState.Live) return state.Phase + " / DASH LOCKED";
            if (player.StunTicks > 0) return "晕 / STUN " + Seconds(player.StunTicks) + "s";
            if (player.DashTicks > 0) return "DASH";
            return player.DashCooldownTicks > 0 ? "COOLDOWN " + Seconds(player.DashCooldownTicks) + "s" : "DASH READY";
        }
        static string Seconds(int ticks) => (ticks / 60f).ToString("0.0");
        static Transform[] BuildBuffIcons(Transform parent, Color ink)
        {
            var icons = new Transform[3];
            for (int kind = 0; kind < 3; kind++)
            {
                RectTransform icon = Rect(parent, "BuffIcon" + kind, new Vector2(0f, .5f), new Vector2(0f, .5f));
                icon.anchoredPosition = new Vector2(35f, 0f); icon.sizeDelta = new Vector2(44f, 44f); icons[kind] = icon;
                var points = new System.Collections.Generic.List<Vector2>();
                if (kind == 0) for (int i = 0; i <= 10; i++)
                {
                    float angle = Mathf.PI * .5f + i * Mathf.PI / 5f, radius = i % 2 == 0 ? 20f : 9f;
                    points.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                }
                else if (kind == 1) for (int i = 0; i < 42; i++)
                {
                    float t = i / 41f, angle = t * Mathf.PI * 4.3f;
                    points.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (2f + 18f * t));
                }
                else points.AddRange(new[] { new Vector2(-17f, 17f), new Vector2(0f, 13f), new Vector2(17f, 17f),
                    new Vector2(16f, -1f), new Vector2(9f, -13f), new Vector2(0f, -20f), new Vector2(-16f, -1f), new Vector2(-17f, 17f) });
                for (int i = 1; i < points.Count; i++)
                {
                    Vector2 delta = points[i] - points[i - 1];
                    RectTransform line = Panel(icon, "Line" + i, ink, Vector2.one * .5f, Vector2.one * .5f);
                    line.anchoredPosition = (points[i] + points[i - 1]) * .5f; line.sizeDelta = new Vector2(delta.magnitude + 1f, 2.4f);
                    line.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                }
            }
            return icons;
        }
        static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero; return rect;
        }
        static RectTransform Panel(Transform parent, string name, Color color, Vector2 min, Vector2 max)
        {
            RectTransform rect = Rect(parent, name, min, max);
            Image image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return rect;
        }
        static TMP_Text Text(Transform parent, string name, TMP_FontAsset font, Color color, float size, Vector2 anchor, Vector2 position, Vector2 extent)
        {
            RectTransform rect = Rect(parent, name, anchor, anchor); rect.anchoredPosition = position; rect.sizeDelta = extent;
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.color = color; text.fontSize = size; text.enableAutoSizing = true; text.fontSizeMin = size * .72f; text.fontSizeMax = size;
            text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false; text.enableWordWrapping = true;
            return text;
        }
        static TMP_Text StretchText(Transform parent, string name, TMP_FontAsset font, Color color, float size, Vector2 min, Vector2 max)
        {
            TMP_Text text = Text(parent, name, font, color, size, Vector2.zero, Vector2.zero, Vector2.zero);
            text.rectTransform.anchorMax = Vector2.one; text.rectTransform.offsetMin = min; text.rectTransform.offsetMax = max;
            text.alignment = TextAlignmentOptions.Left; return text;
        }
        static Button ButtonAt(Transform parent, string name, string caption, TMP_FontAsset font, Color ink, Color color, float y)
        {
            RectTransform rect = Panel(parent, name, color, new Vector2(.5f, y), new Vector2(.5f, y));
            rect.sizeDelta = new Vector2(360f, 57f);
            Image image = rect.GetComponent<Image>(); image.raycastTarget = true;
            Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            Navigation navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
            Text(rect, "Label", font, ink, 24f, Vector2.one * .5f, Vector2.zero, new Vector2(342f, 51f)).text = caption;
            return button;
        }
    }
}
