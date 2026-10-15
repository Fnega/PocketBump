using UnityEngine;

namespace PocketBump
{
    public enum DisplayModeChoice { Windowed, Fullscreen }

    // Keeps display preferences separate from menu layout and safe to exercise in EditMode tests.
    public static class DisplayModeSettings
    {
        const string ModeKey = "PocketBump.DisplayMode";
        public const int WindowWidth = 1280;
        public const int WindowHeight = 720;
        static bool _applied;

        public static DisplayModeChoice Load()
        {
            return PlayerPrefs.GetInt(ModeKey, (int)DisplayModeChoice.Windowed) == (int)DisplayModeChoice.Fullscreen
                ? DisplayModeChoice.Fullscreen : DisplayModeChoice.Windowed;
        }

        public static void Save(DisplayModeChoice mode)
        {
            PlayerPrefs.SetInt(ModeKey, (int)mode);
            PlayerPrefs.Save();
        }

        public static void Apply(DisplayModeChoice mode)
        {
            Save(mode);
            if (Application.isEditor) return;
            if (mode == DisplayModeChoice.Fullscreen)
                Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
            else
                Screen.SetResolution(WindowWidth, WindowHeight, FullScreenMode.Windowed);
        }

        public static void ApplyOnStartup()
        {
            if (_applied) return;
            _applied = true;
            Apply(Load());
        }

        public static void ResetForTests() { _applied = false; }
    }
}
