using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PocketBump.Editor
{
    // Adds only the passive readiness binding and four HUD children. Never invokes a
    // world/material/menu baker, never changes Build Settings or saves shared assets.
    public static class DashReadinessIntegration
    {
        [MenuItem("Tools/Pocket Bump/Dash/Install Readiness Markers (Incremental HUD Only)")]
        public static void IntegrateFormalArena()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before explicit scene integration.");
            string arena = DashProductionSceneBaker.ArenaPath;
            Scene loaded = SceneManager.GetSceneByPath(arena);
            if (loaded.IsValid() && loaded.isLoaded && loaded.isDirty) throw new InvalidOperationException("Save your Arena edits before integration so its backup is exact.");
            string before = Hash(arena), meta = Hash(arena + ".meta"), menu = Hash(DashProductionSceneBaker.MainMenuPath), builds = Hash("ProjectSettings/EditorBuildSettings.asset");
            string backup = Path.GetFullPath(".art-source/backups/dash-readiness/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            if (Directory.Exists(backup)) throw new IOException("Backup destination already exists.");
            Directory.CreateDirectory(backup);
            File.Copy(arena, Path.Combine(backup, "Arena.unity"), false);
            File.Copy(arena + ".meta", Path.Combine(backup, "Arena.unity.meta"), false);
            Require(Hash(Path.Combine(backup, "Arena.unity")) == before && Hash(Path.Combine(backup, "Arena.unity.meta")) == meta, "Exact scene backup verification failed.");
            Scene active = SceneManager.GetActiveScene();
            bool opened = !loaded.IsValid() || !loaded.isLoaded;
            Scene scene = opened ? EditorSceneManager.OpenScene(arena, OpenSceneMode.Additive) : loaded;
            try
            {
                DashProductionSceneBaker.ValidateArena(scene);
                var preserved = Snapshot(scene);
                var regions = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<DashMatchHud>(true))
                    .Single().Seats.ToDictionary(s => s.Region, s => RectStyle(s.Region));
                bool changed = UpgradeLoadedArena(scene);
                Require(!UpgradeLoadedArena(scene), "The second readiness install must be a no-op.");
                foreach (var item in preserved)
                    Require(item.Key != null && EditorJsonUtility.ToJson(item.Key) == item.Value, "An existing authored component was changed: " + item.Key);
                foreach (var item in regions) Require(RectStyle(item.Key) == item.Value, "An authored viewport transform changed.");
                DashProductionSceneBaker.ValidateArena(scene);
                if (changed)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    Require(EditorSceneManager.SaveScene(scene, arena), "Could not save Arena; exact backup: " + backup);
                }
                Require(Hash(arena + ".meta") == meta, "Arena meta changed.");
                Require(Hash(DashProductionSceneBaker.MainMenuPath) == menu, "MainMenu changed.");
                Require(Hash("ProjectSettings/EditorBuildSettings.asset") == builds, "Build Settings changed.");
                Debug.Log("Dash readiness integration passed: changed=" + changed + ", existing components preserved=" + preserved.Count + ", exact backup=" + backup + ", Arena SHA256=" + Hash(arena));
            }
            finally
            {
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }
        public static void IntegrateFromCommandLine()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an explicit Unity batch invocation.");
            IntegrateFormalArena();
        }
        public static bool UpgradeLoadedArena(Scene scene)
        {
            var huds = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<DashMatchHud>(true)).ToArray();
            Require(huds.Length == 1, "Exactly one existing authored HUD is required; no HUD is rebuilt.");
            return DashReadinessHud.Install(huds[0]);
        }
        static Dictionary<UnityEngine.Object, string> Snapshot(Scene scene)
        {
            var result = new Dictionary<UnityEngine.Object, string>();
            var regions = new HashSet<Component>(scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<DashMatchHud>(true)).Single().Seats.Select(s => (Component)s.Region));
            foreach (Component component in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Component>(true)))
                // Only Region child lists grow. Its layout/style is checked separately;
                // every existing child and all other components remain byte-equivalent.
                if (component != null && !regions.Contains(component)) result[component] = EditorJsonUtility.ToJson(component);
            return result;
        }
        static string RectStyle(RectTransform rect) => rect.anchorMin.ToString("R") + rect.anchorMax.ToString("R") + rect.pivot.ToString("R") +
            rect.anchoredPosition3D.ToString("R") + rect.sizeDelta.ToString("R") + rect.localScale.ToString("R") + rect.localRotation.ToString("R");
        static string Hash(string path)
        { using (var sha = SHA256.Create()) using (var file = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", ""); }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        // Disposable-copy persistence fixture invoked by the EditMode test. Production
        // bytes are checked across actual Play Mode/domain reload as well as scene IO.
        const string Key = "PocketBump.ReadinessPersistence.";
        static readonly Color ArtistColor = new Color(.31f, .46f, .71f, .82f);
        public static void PreparePersistenceFixture()
        {
            Require(!EditorApplication.isPlaying, "Prepare outside Play Mode.");
            string folder = "Assets/PocketBumpDashArt/VerificationFixtures";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/PocketBumpDashArt", "VerificationFixtures");
            string path = folder + "/Readiness-" + Guid.NewGuid().ToString("N") + ".unity";
            SessionState.SetString(Key + "Path", path);
            SessionState.SetString(Key + "Arena", Hash(DashProductionSceneBaker.ArenaPath));
            SessionState.SetString(Key + "Menu", Hash(DashProductionSceneBaker.MainMenuPath));
            SessionState.SetString(Key + "Builds", Hash("ProjectSettings/EditorBuildSettings.asset"));
            Require(AssetDatabase.CopyAsset(DashProductionSceneBaker.ArenaPath, path), "Could not copy the existing Arena fixture.");
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            UpgradeLoadedArena(scene);
            var binding = FindBinding(scene);
            binding.MarkerFor(0, 0).IdentityColor = ArtistColor;
            binding.MarkerFor(0, 0).RectTransform.sizeDelta = new Vector2(63, 37);
            binding.MarkerFor(0, 0).FootOffset = new Vector2(2, -3);
            var artist = new GameObject("Artist readiness test child"); artist.transform.SetParent(binding.MarkerFor(0, 0).transform, false);
            artist.transform.localPosition = new Vector3(3, 4, 5);
            foreach (var director in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<DashMatchDirector>(true))) director.AutomaticTicking = false;
            EditorSceneManager.MarkSceneDirty(scene); Require(EditorSceneManager.SaveScene(scene), "Fixture save failed.");
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Require(!UpgradeLoadedArena(scene), "Repeated integration must preserve edited marker styles.");
            VerifyPersistenceFixture(false);
            SessionState.SetString(Key + "Fixture", Hash(path));
        }
        public static void VerifyPersistenceFixture(bool playing)
        {
            Require(EditorApplication.isPlaying == playing, "Unexpected Play Mode state.");
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.path == SessionState.GetString(Key + "Path", ""), "Only the disposable copied fixture may be used.");
            var binding = FindBinding(scene);
            if (playing)
            {
                // Exercise the candidate visual binding in the copied authored world,
                // without selecting a release profile or advancing a second simulation.
                var presentation = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<DashArenaPresentation>(true)).Single();
                var candidate = DashArenaSimulation.Create(ArenaRulesProfile.CreateHandfeelCandidate(), 191);
                DashArenaSimulation.StartMatch(candidate); candidate.Phase = MatchState.Live; candidate.Players[0].DashCooldownTicks = 90;
                int checksum = candidate.Checksum();
                presentation.BindState(candidate, false, 0);
                binding.Refresh(candidate, presentation, false, 0);
                DashReadinessState displayed = binding.HasLocalGroundRings ? binding.GroundRingFor(0).State : binding.MarkerFor(0, 0).State;
                Require(displayed.Kind == DashReadinessKind.Cooling && displayed.Progress == .5f,
                    "Candidate refresh did not bind the saved detailed marker.");
                Require(candidate.Checksum() == checksum, "Presentation changed candidate authority.");
            }
            var marker = binding.MarkerFor(0, 0);
            Require(marker.IdentityColor == ArtistColor, "Saved marker identity colour was overwritten.");
            Require(marker.RectTransform.sizeDelta == new Vector2(63, 37), "Saved marker dimensions were overwritten.");
            Require(marker.FootOffset == new Vector2(2, -3), "Saved foot offset was overwritten.");
            Require(marker.transform.Find("Artist readiness test child") != null, "Unknown artist child was lost.");
            Require(binding.Hud != null && binding.GetComponentsInChildren<DashReadinessGraphic>(true).Length == 4, "References/marker count changed.");
            if (!playing) Require(!UpgradeLoadedArena(scene), "Repeated explicit integration changed existing markers.");
        }
        public static void FinishPersistenceFixture()
        {
            VerifyPersistenceFixture(false);
            Require(Hash(SessionState.GetString(Key + "Path", "")) == SessionState.GetString(Key + "Fixture", ""), "Play Mode changed saved fixture bytes.");
            Require(Hash(DashProductionSceneBaker.ArenaPath) == SessionState.GetString(Key + "Arena", ""), "Production Arena changed during test.");
            Require(Hash(DashProductionSceneBaker.MainMenuPath) == SessionState.GetString(Key + "Menu", ""), "MainMenu changed during test.");
            Require(Hash("ProjectSettings/EditorBuildSettings.asset") == SessionState.GetString(Key + "Builds", ""), "Build Settings changed during test.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
        static DashReadinessHud FindBinding(Scene scene) => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<DashReadinessHud>(true)).Single();
    }
}
