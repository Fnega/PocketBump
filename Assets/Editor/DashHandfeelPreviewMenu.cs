using UnityEditor;
using UnityEngine;

namespace PocketBump.Editor
{
    // Editor-session-only A/B selector. Never writes assets, PlayerPrefs, Build Settings
    // or a release default. Returning from Play Mode disarms the candidate selection.
    [InitializeOnLoad]
    public static class DashHandfeelPreviewMenu
    {
        const string Key = "PocketBump.HandfeelCandidate.NextPlay";
        const string CandidateMenu = "Tools/Pocket Bump/Handfeel Preview/Use Candidate for Next Play";
        const string BaselineMenu = "Tools/Pocket Bump/Handfeel Preview/Use Released V1 for Next Play";

        static DashHandfeelPreviewMenu()
        {
            DashHandfeelPreview.Selected = SessionState.GetBool(Key, false);
            EditorApplication.playModeStateChanged += OnPlayState;
        }

        [MenuItem(CandidateMenu)]
        public static void SelectCandidate() => Select(true);
        [MenuItem(BaselineMenu)]
        public static void SelectBaseline() => Select(false);
        [MenuItem(CandidateMenu, true)]
        static bool CanSelectCandidate() => !EditorApplication.isPlayingOrWillChangePlaymode;
        [MenuItem(BaselineMenu, true)]
        static bool CanSelectBaseline() => !EditorApplication.isPlayingOrWillChangePlaymode;

        static void Select(bool candidate)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Stop Play Mode before selecting an A/B profile.");
            SessionState.SetBool(Key, candidate);
            DashHandfeelPreview.Selected = candidate;
            Debug.Log(candidate
                ? "Handfeel CANDIDATE selected for next Play only. Production V1 remains the release default."
                : "Released V1 selected for next Play. No assets or rules were changed.");
        }

        static void OnPlayState(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            SessionState.SetBool(Key, false);
            DashHandfeelPreview.Selected = false;
        }
    }
}
