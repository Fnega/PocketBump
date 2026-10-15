using UnityEngine;
using UnityEngine.SceneManagement;

// 跨 MainMenu 与 Arena 场景保存游玩模式、规则和联机地址。
// 菜单只负责收集选择，比赛场景只消费选择，降低两个场景之间的直接耦合。
namespace PocketBump
{
    public enum PlayKind
    {
        Local,
        HostLan,
        JoinLan
    }

    public sealed class GameSession : MonoBehaviour
    {
        public const string MainMenuScene = "MainMenu";
        public const string ArenaScene = "Arena";

        static GameSession _instance;
        readonly MatchSettings _selectedSettings = new MatchSettings();

        public static GameSession Instance => Ensure();
        public bool OpenRulesOnMenu { get; private set; }
        public MatchSettings SelectedSettings => _selectedSettings;
        public PlayKind Kind { get; private set; } = PlayKind.Local;
        public string JoinAddress { get; set; } = "127.0.0.1";
        public string ErrorMessage { get; set; } = "";
        public LanDiagnosticSnapshot LastLanDiagnostics { get; set; }

        // 直接打开 Arena 或运行测试时可能没有菜单预建会话，因此提供惰性兜底。
        public static GameSession Ensure()
        {
            if (_instance != null) return _instance;
            _instance = FindObjectOfType<GameSession>();
            if (_instance != null) return _instance;

            var go = new GameObject("GameSession");
            _instance = go.AddComponent<GameSession>();
            DontDestroyOnLoad(go);
            DisplayModeSettings.ApplyOnStartup();
            return _instance;
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            _selectedSettings.Clamp();
            DisplayModeSettings.ApplyOnStartup();
        }

        // 返回副本，避免本局 Clamp/握手修改静态会话中的下局默认设置。
        public MatchSettings CopySettings()
        {
            return new MatchSettings
            {
                pointsToWin = _selectedSettings.pointsToWin,
                marbleCount = _selectedSettings.marbleCount
            };
        }

        public void BeginMatch(int pointsToWin, int marbleCount)
        {
            StoreRules(pointsToWin, marbleCount);
            Kind = PlayKind.Local;
            SceneManager.LoadScene(ArenaScene);
        }

        public void BeginHost(int pointsToWin, int marbleCount)
        {
            StoreRules(pointsToWin, marbleCount);
            Kind = PlayKind.HostLan;
            SceneManager.LoadScene(ArenaScene);
        }

        public void BeginJoin(string address, int pointsToWin, int marbleCount)
        {
            StoreRules(pointsToWin, marbleCount);
            JoinAddress = string.IsNullOrEmpty(address) ? "127.0.0.1" : address.Trim();
            Kind = PlayKind.JoinLan;
            SceneManager.LoadScene(ArenaScene);
        }

        public void Rematch()
        {
            DashMatchDirector dash = FindObjectOfType<DashMatchDirector>();
            if (dash != null)
            {
                dash.RequestRematch();
                return;
            }
            if (Kind != PlayKind.Local)
            {
                MatchDirector director = FindObjectOfType<MatchDirector>();
                if (director != null) director.RequestRematch();
                return;
            }
            OpenRulesOnMenu = false;
            SceneManager.LoadScene(ArenaScene);
        }

        // 用一次性标记让返回菜单后自动展开规则面板，完成赛后修改规则流程。
        public void ModifyRules()
        {
            // LAN rule changes require an explicit leave and a fresh Host LAN room.
            if (Kind != PlayKind.Local) return;
            OpenRulesOnMenu = true;
            Kind = PlayKind.Local;
            SceneManager.LoadScene(MainMenuScene);
        }

        public void ReturnToMainMenu()
        {
            DashMatchDirector dash = FindObjectOfType<DashMatchDirector>();
            if (dash != null) dash.LeaveNetwork();
            MatchDirector director = FindObjectOfType<MatchDirector>();
            if (director != null) director.LeaveNetwork();
            Time.timeScale = 1f;
            OpenRulesOnMenu = false;
            Kind = PlayKind.Local;
            SceneManager.LoadScene(MainMenuScene);
        }

        public bool ConsumeOpenRulesRequest()
        {
            bool requested = OpenRulesOnMenu;
            OpenRulesOnMenu = false;
            return requested;
        }

        // 所有开始方式共用校验入口，保证本地、Host 和 Join 使用合法范围。
        void StoreRules(int pointsToWin, int marbleCount)
        {
            _selectedSettings.pointsToWin = pointsToWin;
            _selectedSettings.marbleCount = marbleCount;
            _selectedSettings.Clamp();
            OpenRulesOnMenu = false;
            ErrorMessage = "";
            LastLanDiagnostics = null;
        }
    }
}
