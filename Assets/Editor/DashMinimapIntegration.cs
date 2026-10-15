using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PocketBump.Editor
{
    // Explicit HUD-only migration. Never calls a world/menu/material baker or SaveAssets.
    public static class DashMinimapIntegration
    {
        const string BackupRoot = ".art-source/backups/minimap";

        [MenuItem("Tools/Pocket Bump/Dash/Install Minimap (Incremental HUD Only)")]
        public static void IntegrateFormalArena()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before the explicit minimap migration.");
            string arena = DashProductionSceneBaker.ArenaPath;
            if (!File.Exists(arena) || !File.Exists(arena + ".meta"))
                throw new FileNotFoundException("The authored Arena and its meta must already exist; nothing will be rebuilt.");
            Scene loaded = SceneManager.GetSceneByPath(arena);
            if (loaded.IsValid() && loaded.isLoaded && loaded.isDirty)
                throw new InvalidOperationException("Save or discard your pending Arena edits yourself before migration so the byte backup is exact.");

            string backup = Path.GetFullPath(Path.Combine(BackupRoot,
                DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8)));
            if (Directory.Exists(backup) || File.Exists(backup)) throw new IOException("Backup destination already exists: " + backup);
            string arenaBefore = FileHash(arena), metaBefore = FileHash(arena + ".meta");
            Directory.CreateDirectory(backup);
            File.Copy(arena, Path.Combine(backup, "Arena.unity"), false);
            File.Copy(arena + ".meta", Path.Combine(backup, "Arena.unity.meta"), false);
            Require(FileHash(Path.Combine(backup, "Arena.unity")) == arenaBefore &&
                FileHash(Path.Combine(backup, "Arena.unity.meta")) == metaBefore, "Byte-for-byte Arena backup verification failed.");
            WriteNew(Path.Combine(backup, "source.json"), JsonUtility.ToJson(new BackupIdentity
            {
                source = Path.GetFullPath(arena), scene_sha256 = arenaBefore, meta_sha256 = metaBefore,
                evidence_kind = "pre-migration exact scene bytes; not a regenerated scene"
            }, true));

            Scene previousActive = SceneManager.GetActiveScene();
            bool opened = !loaded.IsValid() || !loaded.isLoaded;
            Scene scene = opened ? EditorSceneManager.OpenScene(arena, OpenSceneMode.Additive) : loaded;
            string menuBefore = FileHash(DashProductionSceneBaker.MainMenuPath);
            string buildsBefore = FileHash("ProjectSettings/EditorBuildSettings.asset");
            bool changed = false;
            try
            {
                SceneManager.SetActiveScene(scene);
                DashProductionSceneBaker.ValidateArena(scene);
                DashMatchHud hud = FindHud(scene);
                var protectedObjects = ProtectedObjects(scene, hud);
                var styles = ExistingStyles(hud);
                var materials = ReferencedMaterialHashes(scene);
                changed = UpgradeLoadedArena(scene);
                Require(!UpgradeLoadedArena(scene), "A second minimap upgrade must be a no-op; author changes must not be re-applied.");
                AssertObjectsUnchanged(protectedObjects);
                foreach (var item in styles) Require(Style(item.Key) == item.Value, "Existing author UI styling changed: " + item.Key.name);
                foreach (var item in materials) Require(FileHash(item.Key) == item.Value, "Authored material bytes changed: " + item.Key);
                DashProductionSceneBaker.ValidateArena(scene);
                if (changed)
                {
                    EditorUtility.SetDirty(hud);
                    EditorSceneManager.MarkSceneDirty(scene);
                    Require(EditorSceneManager.SaveScene(scene, arena), "Could not save the upgraded Arena; exact backup is at " + backup);
                }
                Require(FileHash(arena + ".meta") == metaBefore, "Arena meta identity changed.");
                Require(FileHash(DashProductionSceneBaker.MainMenuPath) == menuBefore, "MainMenu bytes changed.");
                Require(FileHash("ProjectSettings/EditorBuildSettings.asset") == buildsBefore, "Build Settings bytes changed.");
                WriteNew(Path.Combine(backup, "integration-result.json"), JsonUtility.ToJson(new IntegrationResult
                {
                    passed = true, changed = changed, backup = backup, scene_before_sha256 = arenaBefore,
                    scene_after_sha256 = FileHash(arena), preserved_objects = protectedObjects.Count,
                    preserved_graphic_styles = styles.Count, preserved_material_files = materials.Count,
                    second_install_was_noop = true, main_menu_unchanged = true, build_settings_unchanged = true
                }, true));
                Debug.Log("Minimap HUD-only migration complete (changed=" + changed + "). Exact Arena backup: " + backup);
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
                if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }

        public static void IntegrateFromCommandLine()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("This entrypoint requires an explicit Unity batch invocation.");
            IntegrateFormalArena();
        }

        // Available to tests operating on their own disposable scene. The caller owns
        // backup/save policy; this method never saves any scene or changes any asset.
        public static bool UpgradeLoadedArena(Scene scene)
        {
            Require(scene.IsValid() && scene.isLoaded, "Load the explicitly selected scene before upgrading its HUD.");
            DashMatchHud hud = FindHud(scene);
            return hud.InstallMinimapAndUpgradeLayout();
        }

        static DashMatchHud FindHud(Scene scene)
        {
            DashMatchHud[] found = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<DashMatchHud>(true))
                .Where(h => h.gameObject.activeInHierarchy).ToArray();
            Require(found.Length == 1, "Exactly one existing active authored HUD is required; this tool does not create a replacement HUD.");
            return found[0];
        }

        static Dictionary<UnityEngine.Object, string> ProtectedObjects(Scene scene, DashMatchHud hud)
        {
            // Only documented layout targets may change. Unknown author objects,
            // including children inside those panels, are independently protected.
            var allowed = new HashSet<UnityEngine.Object> { hud };
            foreach (DashMatchHud.SeatView seat in hud.Seats)
            {
                allowed.Add(seat.Region);
                if (seat.Status != null) allowed.Add(seat.Status.transform.parent);
                if (seat.PickupPanel != null) allowed.Add(seat.PickupPanel.transform);
            }
            if (hud.NetworkIssueRect != null) allowed.Add(hud.NetworkIssueRect);
            var result = new Dictionary<UnityEngine.Object, string>();
            foreach (Transform node in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)))
            {
                result[node.gameObject] = EditorJsonUtility.ToJson(node.gameObject);
                foreach (Component component in node.GetComponents<Component>())
                    if (component != null && !allowed.Contains(component)) result[component] = EditorJsonUtility.ToJson(component);
            }
            return result;
        }

        static void AssertObjectsUnchanged(Dictionary<UnityEngine.Object, string> before)
        {
            foreach (var item in before)
                Require(item.Key != null && EditorJsonUtility.ToJson(item.Key) == item.Value,
                    "Migration touched a protected authored object/component: " + (item.Key != null ? item.Key.name + "/" + item.Key.GetType().Name : "destroyed object"));
        }

        static Dictionary<Graphic, string> ExistingStyles(DashMatchHud hud) =>
            hud.GetComponentsInChildren<Graphic>(true).ToDictionary(g => g, Style);

        static string Style(Graphic graphic)
        {
            if (graphic == null) return "DESTROYED";
            TMP_Text text = graphic as TMP_Text;
            Image image = graphic as Image;
            return ColorUtility.ToHtmlStringRGBA(graphic.color) + "/" + Id(graphic.material) + "/" +
                (text != null ? Id(text.font) + "/" + Id(text.fontSharedMaterial) : "") + "/" +
                (image != null ? Id(image.sprite) : "");
        }
        static int Id(UnityEngine.Object value) => value != null ? value.GetInstanceID() : 0;

        static Dictionary<string, string> ReferencedMaterialHashes(Scene scene)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Renderer renderer in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)))
                foreach (Material material in renderer.sharedMaterials)
                {
                    string path = AssetDatabase.GetAssetPath(material);
                    if (!string.IsNullOrEmpty(path) && File.Exists(path) && !result.ContainsKey(path)) result.Add(path, FileHash(path));
                }
            return result;
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static string FileHash(string path)
        {
            using (var sha = SHA256.Create()) using (var file = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
        }
        static void WriteNew(string path, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes, 0, bytes.Length);
        }
        [Serializable] sealed class BackupIdentity { public string source, scene_sha256, meta_sha256, evidence_kind; }
        [Serializable] sealed class IntegrationResult
        {
            public bool passed, changed, second_install_was_noop, main_menu_unchanged, build_settings_unchanged;
            public string backup, scene_before_sha256, scene_after_sha256;
            public int preserved_objects, preserved_graphic_styles, preserved_material_files;
        }
    }
}
