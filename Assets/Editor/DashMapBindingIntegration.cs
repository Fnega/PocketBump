using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PocketBump.Editor
{
    // Saves the new settings card without rebuilding, repainting or moving old UI.
    public static class DashMapBindingIntegration
    {
        const string FixtureKey = "PocketBump.MapBindingFixture.";
        const string ArtistChild = "Map settings artist child";
        static readonly Color ArtistColor = new Color(.31f, .43f, .69f, .8f);

        [MenuItem("Tools/Pocket Bump/Dash/Install Local Map Binding (Incremental)")]
        public static void IntegrateFormalMenu()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before explicit menu authoring.");
            string path = DashProductionSceneBaker.MainMenuPath;
            Scene loaded = SceneManager.GetSceneByPath(path);
            Require(!loaded.IsValid() || !loaded.isLoaded || !loaded.isDirty, "Save your menu edits before creating the exact-byte backup.");
            var protectedFiles = ProtectedFiles(); string previousHash = Hash(path);
            string backup = Path.GetFullPath(".art-source/backups/map-binding/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backup);
            File.Copy(path, Path.Combine(backup, "MainMenu.unity"), false);
            File.Copy(path + ".meta", Path.Combine(backup, "MainMenu.unity.meta"), false);
            Require(Hash(Path.Combine(backup, "MainMenu.unity")) == previousHash, "Menu backup differs from source.");
            Require(Hash(Path.Combine(backup, "MainMenu.unity.meta")) == protectedFiles[path + ".meta"], "Menu metadata backup differs from source.");
            Scene active = SceneManager.GetActiveScene(); bool opened = !loaded.IsValid() || !loaded.isLoaded;
            Scene scene = opened ? EditorSceneManager.OpenScene(path, OpenSceneMode.Additive) : loaded;
            try
            {
                bool changed = UpgradeLoadedMenu(scene);
                Require(!UpgradeLoadedMenu(scene), "Second menu upgrade must be a no-op.");
                VerifyFiles(protectedFiles);
                if (changed)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    Require(EditorSceneManager.SaveScene(scene, path), "Could not save the incrementally authored menu.");
                }
                else Require(Hash(path) == previousHash, "A no-op upgrade changed menu bytes.");
                VerifyFiles(protectedFiles);
                Debug.Log("Local map binding integration passed: changed=" + changed + ", exact backup=" + backup + ", MainMenu SHA256=" + Hash(path));
            }
            finally
            {
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }

        public static void IntegrateFromCommandLine()
        {
            Require(Application.isBatchMode, "Use an explicit Unity batch invocation.");
            IntegrateFormalMenu();
        }

        public static bool UpgradeLoadedMenu(Scene scene)
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && scene.IsValid() && scene.isLoaded, "Load the explicit menu scene in Edit Mode.");
            MainMenuController controller = FindController(scene);
            Transform settings = controller.transform.Find("MainMenuCanvas/SettingsPanel");
            Require(settings != null && settings is RectTransform, "Existing authored SettingsPanel is required.");
            var previousChildren = Enumerable.Range(0, settings.childCount).Select(settings.GetChild).ToArray();
            var snapshot = new Dictionary<UnityEngine.Object, string>();
            foreach (Component component in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Component>(true)))
            {
                if (component == null) continue;
                snapshot[component] = Snapshot(component, settings);
                snapshot[component.gameObject] = EditorJsonUtility.ToJson(component.gameObject);
            }
            bool changed = controller.InstallMinimapBindingSettings();
            foreach (var old in snapshot)
                Require(old.Key != null && Snapshot(old.Key, settings) == old.Value, "An existing authored menu object changed: " + old.Key);
            Require(settings.childCount == previousChildren.Length + (changed ? 1 : 0), "Only one new settings child is permitted.");
            for (int index = 0; index < previousChildren.Length; index++)
                Require(settings.GetChild(index) == previousChildren[index], "Original settings children were moved or removed.");
            Validate(controller);
            return changed;
        }

        static string Snapshot(UnityEngine.Object value, Transform settings)
        {
            string json = EditorJsonUtility.ToJson(value);
            // Appending a child legitimately changes only this parent's child list.
            return value == settings ? Regex.Replace(json, "\"m_Children\":\\[[^\\]]*\\]", "\"m_Children\":[]") : json;
        }
        static MainMenuController FindController(Scene scene) => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MainMenuController>(true)).Single();
        static Transform Card(MainMenuController controller) => controller.transform.Find("MainMenuCanvas/SettingsPanel/" + MainMenuController.MinimapBindingCardName);
        static void Validate(MainMenuController controller)
        {
            Transform card = Card(controller);
            Require(card != null && card.Find("MinimapToggleBind")?.GetComponent<Button>() != null &&
                card.Find("MinimapDefault")?.GetComponent<Button>() != null && card.Find("MapBindingHint")?.GetComponent<TMP_Text>() != null,
                "The authored map settings card is incomplete.");
            Require(controller.GetComponentsInChildren<Button>(true).Count(b => b.name == "MinimapToggleBind") == 1, "Expected one map binding button.");
        }
        static Dictionary<string, string> ProtectedFiles() => new[] {
            DashProductionSceneBaker.MainMenuPath + ".meta", DashProductionSceneBaker.ArenaPath, DashProductionSceneBaker.ArenaPath + ".meta",
            "ProjectSettings/EditorBuildSettings.asset", "ProjectSettings/TagManager.asset", DashProductionSceneBaker.FontAssetPath
        }.ToDictionary(p => p, Hash);
        static void VerifyFiles(Dictionary<string, string> files)
        { foreach (var pair in files) Require(Hash(pair.Key) == pair.Value, "Protected file changed: " + pair.Key); }
        static string Hash(string path)
        { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""); }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        public static void PreparePersistenceFixture()
        {
            const string folder = "Assets/PocketBumpDashArt/VerificationFixtures";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/PocketBumpDashArt", "VerificationFixtures");
            string path = folder + "/MapBindings-" + Guid.NewGuid().ToString("N") + ".unity";
            SessionState.SetString(FixtureKey + "Path", path);
            SessionState.SetString(FixtureKey + "Menu", Hash(DashProductionSceneBaker.MainMenuPath));
            foreach (var pair in ProtectedFiles()) SessionState.SetString(FixtureKey + pair.Key, pair.Value);
            Require(AssetDatabase.CopyAsset(DashProductionSceneBaker.MainMenuPath, path), "Could not copy the authored menu fixture.");
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            UpgradeLoadedMenu(scene);
            Transform card = Card(FindController(scene));
            card.GetComponent<Image>().color = ArtistColor;
            ((RectTransform)card).anchoredPosition += new Vector2(3, 7);
            var artist = new GameObject(ArtistChild); artist.transform.SetParent(card, false); artist.transform.localPosition = new Vector3(2, 3, 4);
            EditorSceneManager.MarkSceneDirty(scene); Require(EditorSceneManager.SaveScene(scene), "Could not save map settings fixture.");
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Require(!UpgradeLoadedMenu(scene), "Reloaded authored map settings were overwritten.");
            SessionState.SetString(FixtureKey + "SavedFixture", Hash(path));
            VerifyPersistenceFixture(false);
        }
        public static void VerifyPersistenceFixture(bool playing)
        {
            Require(EditorApplication.isPlaying == playing, "Unexpected Play Mode state.");
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.path == SessionState.GetString(FixtureKey + "Path", ""), "Only the disposable menu fixture is allowed.");
            MainMenuController controller = FindController(scene); Validate(controller);
            Transform card = Card(controller);
            Require(card.GetComponent<Image>().color == ArtistColor, "Authored card colour changed.");
            Require(((RectTransform)card).anchoredPosition == new Vector2(173, 7), "Authored card offset changed.");
            Require(card.Find(ArtistChild)?.localPosition == new Vector3(2, 3, 4), "Unknown author child was lost or changed.");
            if (!playing) Require(!UpgradeLoadedMenu(scene), "Repeated map settings integration was not a no-op.");
        }
        public static void FinishPersistenceFixture()
        {
            VerifyPersistenceFixture(false);
            string path = SessionState.GetString(FixtureKey + "Path", "");
            Require(Hash(path) == SessionState.GetString(FixtureKey + "SavedFixture", ""), "Play Mode changed saved fixture bytes.");
            Require(Hash(DashProductionSceneBaker.MainMenuPath) == SessionState.GetString(FixtureKey + "Menu", ""), "Persistence test changed the production menu.");
            foreach (var pair in ProtectedFiles()) Require(pair.Value == SessionState.GetString(FixtureKey + pair.Key, ""), "Persistence test changed protected file " + pair.Key);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }
}
