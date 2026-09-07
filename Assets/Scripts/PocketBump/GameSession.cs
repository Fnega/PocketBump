using UnityEngine;
using UnityEngine.SceneManagement;

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

        public static GameSession Ensure()
        {
            if (_instance != null) return _instance;
            _instance = FindObjectOfType<GameSession>();
            if (_instance != null) return _instance;

            var go = new GameObject("GameSession");
            _instance = go.AddComponent<GameSession>();
            DontDestroyOnLoad(go);
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
        }

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
            OpenRulesOnMenu = false;
            SceneManager.LoadScene(ArenaScene);
        }

        public void ModifyRules()
        {
            OpenRulesOnMenu = true;
            Kind = PlayKind.Local;
            SceneManager.LoadScene(MainMenuScene);
        }

        public void ReturnToMainMenu()
        {
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

        void StoreRules(int pointsToWin, int marbleCount)
        {
            _selectedSettings.pointsToWin = pointsToWin;
            _selectedSettings.marbleCount = marbleCount;
            _selectedSettings.Clamp();
            OpenRulesOnMenu = false;
            ErrorMessage = "";
        }
    }
}
