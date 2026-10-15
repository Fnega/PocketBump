using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

namespace PocketBump.Editor
{
    // First migration preserves the old authored hierarchy inactive, with a byte-for-byte
    // external backup. Subsequent runs validate/repair UI references, not recreate art.
    public static class DashProductionSceneBaker
    {
        public const string MainMenuPath = "Assets/Scenes/MainMenu.unity";
        public const string ArenaPath = "Assets/Scenes/Arena.unity";
        public const string ThemePath = "Assets/Resources/PocketBump/DashUiTheme.asset";
        public const string FontAssetPath = "Assets/PocketBumpDashArt/Fonts/NotoSansCJKsc-Regular SDF.asset";
        public const string WorldRootName = "Dash Match - Authored v7";
        public const string LegacyRootName = "Legacy v6 - Preserved inactive (not gameplay)";

        [MenuItem("Tools/Pocket Bump/Dash/Integrate Approved Art (Incremental)")]
        public static void MigrateFormalScenes()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(MainMenuPath) || !File.Exists(ArenaPath))
                throw new FileNotFoundException("Formal scenes must exist; this tool never silently recreates them.");
            DashUiTheme theme = EnsureTheme();
            MigrateMenu(theme);

            Scene arenaScene = EditorSceneManager.OpenScene(ArenaPath, OpenSceneMode.Single);
            DashAuthoredScene existing = Find<DashAuthoredScene>(arenaScene);
            if (existing == null) MigrateArena(arenaScene, theme, BackupScene(ArenaPath));
            else if (existing.Version != DashAuthoredScene.CurrentVersion)
                throw new InvalidOperationException("Unknown authored scene version; an explicit migration is required.");
            ValidateArena(arenaScene);
            Save(arenaScene);
            AssetDatabase.SaveAssets();
            Debug.Log("Dash production scenes integrated. Authored assets and inactive legacy content preserved; Build Settings unchanged.");
        }

        public static void MigrateMainMenuOnly()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(MainMenuPath)) throw new FileNotFoundException("Formal main menu is missing.");
            MigrateMenu(EnsureTheme());
            AssetDatabase.SaveAssets();
        }

        static void MigrateMenu(DashUiTheme theme)
        {
            Scene menuScene = EditorSceneManager.OpenScene(MainMenuPath, OpenSceneMode.Single);
            string backup = Find<DashAuthoredScene>(menuScene) == null ? BackupScene(MainMenuPath) : null;
            RepairMenuInScene(menuScene, theme, backup);
            Save(menuScene);
        }

        // Operates on the explicitly supplied loaded scene. This also lets verification
        // exercise copies without writing temporary author edits to the real MainMenu.
        public static void RepairMenuInScene(Scene menuScene, DashUiTheme theme, string firstBackup = null)
        {
            if (!menuScene.IsValid() || !menuScene.isLoaded) throw new ArgumentException("Load the target scene first.");
            MainMenuController menu = Find<MainMenuController>(menuScene);
            if (menu == null) throw new InvalidOperationException("MainMenuController missing; refusing to replace authored content.");
            bool migrateMenu = Find<DashAuthoredScene>(menuScene) == null;
            if (migrateMenu) menu.Theme = theme != null ? theme : EnsureTheme();
            menu.BuildAuthoredHierarchy();
            if (migrateMenu)
            {
                menu.ApplyThemeToAuthoredHierarchy();
                menu.gameObject.AddComponent<DashAuthoredScene>().RecordBackup(firstBackup);
            }
            EditorUtility.SetDirty(menu);
        }

        public static DashUiTheme EnsureTheme()
        {
            DashUiTheme theme = AssetDatabase.LoadAssetAtPath<DashUiTheme>(ThemePath);
            if (theme != null)
            {
                if (theme.Font == null) throw new InvalidOperationException("Existing Dash UI theme has no font. Repair the reference without resetting the theme.");
                return theme;
            }
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (font == null)
            {
                Font source = AssetDatabase.LoadAssetAtPath<Font>(DashArenaArtSampleBaker.FontPath);
                if (source == null) throw new FileNotFoundException("Approved Noto source font missing.");
                font = TMP_FontAsset.CreateFontAsset(source, 64, 7, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                if (font == null) throw new InvalidOperationException("Could not create the approved TMP font asset.");
                font.name = "Noto Sans CJK SC - Pocket Bump";
                string ascii = new string(Enumerable.Range(32, 95).Select(i => (char)i).ToArray());
                string chinese = DashArenaArtSample.RequiredChineseGlyphs + DashMatchHud.RequiredChineseGlyphs + "击退冷却准备就绪无敌掉落复活对战网络暂停失败原因主菜单重赛等待对手确认连接结束规则比分本机玩家目标得分冲刺眩晕护盾重开球开启恢复修复伤害移动按键剩余拒绝不可用状态同步超时断开比赛胜利敌方无法返回输入丢包，。！：；（）秒·—→";
                if (!font.TryAddCharacters(ascii + chinese, out string missing))
                    throw new InvalidOperationException("Approved font does not cover required characters: " + missing);
                AssetDatabase.CreateAsset(font, FontAssetPath);
                AssetDatabase.AddObjectToAsset(font.material, font);
                foreach (Texture2D texture in font.atlasTextures)
                    if (texture != null && !AssetDatabase.Contains(texture)) AssetDatabase.AddObjectToAsset(texture, font);
                EditorUtility.SetDirty(font);
            }
            theme = ScriptableObject.CreateInstance<DashUiTheme>();
            theme.Font = font;
            AssetDatabase.CreateAsset(theme, ThemePath);
            AssetDatabase.SaveAssets();
            return theme;
        }

        static void MigrateArena(Scene arenaScene, DashUiTheme theme, string backup)
        {
            if (Find<MatchDirector>(arenaScene) == null)
                throw new InvalidOperationException("Expected v6 MatchDirector missing; refusing an ambiguous migration.");
            GameObject[] oldRoots = arenaScene.GetRootGameObjects();
            Scene sampleScene = EditorSceneManager.OpenScene(DashArenaArtSampleBaker.ScenePath, OpenSceneMode.Additive);
            GameObject production = null;
            try
            {
                DashArenaArtSample source = Find<DashArenaArtSample>(sampleScene);
                if (source == null || !source.IsConfigured) throw new InvalidOperationException("Approved authored sample is incomplete.");
                production = UnityEngine.Object.Instantiate(source.gameObject);
                production.name = WorldRootName;
                SceneManager.MoveGameObjectToScene(production, arenaScene);
            }
            finally { EditorSceneManager.CloseScene(sampleScene, true); }
            SceneManager.SetActiveScene(arenaScene);
            DashArenaArtSample clonedSample = production.GetComponent<DashArenaArtSample>();
            DashArenaArtSample.SceneReferences views = clonedSample.Views;
            if (views.ShowcaseCamera != null) UnityEngine.Object.DestroyImmediate(views.ShowcaseCamera.gameObject);
            views.ShowcaseCamera = null;
            Transform board = production.transform.Find("STAGED ART COMPARISON BOARD - NOT GAMEPLAY EVIDENCE");
            if (board != null) UnityEngine.Object.DestroyImmediate(board.gameObject);
            UnityEngine.Object.DestroyImmediate(clonedSample);

            var legacy = new GameObject(LegacyRootName);
            SceneManager.MoveGameObjectToScene(legacy, arenaScene);
            legacy.SetActive(false);
            foreach (GameObject root in oldRoots)
            {
                // Only known generated roots are retired; unknown artist roots remain untouched.
                if (root.GetComponent<MatchDirector>() != null || root.GetComponent<ArenaSceneController>() != null ||
                    root.GetComponent<MatchBootstrap>() != null || root.name == "Main Camera" || root.name == "Directional Light")
                    root.transform.SetParent(legacy.transform, true);
            }
            // Closing an additive scene can unload assets not yet referenced by the
            // destination. Resolve the persistent font again instead of using a stale wrapper.
            TMP_FontAsset hudFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (hudFont == null) throw new InvalidOperationException("Approved HUD font was not imported.");
            DashMatchHud hud = DashMatchHud.Build(production.transform, hudFont);
            DashArenaPresentation presentation = production.AddComponent<DashArenaPresentation>();
            presentation.Configure(views, hud);
            DashMatchDirector director = production.AddComponent<DashMatchDirector>();
            director.Configure(presentation, hud);
            var systems = new GameObject("Dash Scene Systems");
            systems.transform.SetParent(production.transform, false);
            systems.AddComponent<ArenaSceneController>().SetDirector(director);
            production.AddComponent<DashAuthoredScene>().RecordBackup(backup);
            for (int i = 0; i < views.GameplayCameras.Length; i++)
                views.GameplayCameras[i].tag = i == 0 ? "MainCamera" : "Untagged";
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.73f, .82f, .91f);
            RenderSettings.ambientEquatorColor = new Color(.64f, .71f, .66f);
            RenderSettings.ambientGroundColor = new Color(.4f, .43f, .36f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = false;
        }

        public static void ValidateArena(Scene scene)
        {
            DashMatchDirector[] directors = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<DashMatchDirector>(true))
                .Where(d => d.gameObject.activeInHierarchy && d.enabled).ToArray();
            if (directors.Length != 1) throw new InvalidOperationException("Expected exactly one active Dash director.");
            if (scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MatchDirector>(true)).Any(d => d.gameObject.activeInHierarchy && d.enabled))
                throw new InvalidOperationException("Legacy director still active in the production scene.");
            if (Find<DashArenaPresentation>(scene) == null || Find<DashMatchHud>(scene) == null)
                throw new InvalidOperationException("Production presentation or HUD reference missing.");
            if (directors[0].Presentation == null || directors[0].Hud == null)
                throw new InvalidOperationException("Dash director's serialized presentation bindings are incomplete.");
            if (scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<AudioListener>(true)).Count(a => a.enabled && a.gameObject.activeInHierarchy) != 1)
                throw new InvalidOperationException("The two viewports must share exactly one active AudioListener.");
            if (scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayerBasket>(true)).Any(b => b.gameObject.activeInHierarchy))
                throw new InvalidOperationException("A retired basket remains active in the production arena.");
            if (Find<DashArenaArtSample>(scene) != null)
                throw new InvalidOperationException("Independent sample simulation must not run in the formal arena.");
        }

        // Explicit one-time repair for the first integration's unloaded font reference.
        // Kept separate from the incremental tool: subsequent author font edits are preserved.
        public static void AssignApprovedHudFont()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Scene scene = EditorSceneManager.OpenScene(ArenaPath, OpenSceneMode.Single);
            DashMatchDirector director = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<DashMatchDirector>(true))
                .Single(d => d.gameObject.activeInHierarchy);
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (font == null || director.Hud == null) throw new InvalidOperationException("Missing HUD or approved font.");
            foreach (TMP_Text text in director.Hud.GetComponentsInChildren<TMP_Text>(true))
            {
                text.font = font;
                EditorUtility.SetDirty(text);
            }
            Save(scene);
            Debug.Log("Explicitly assigned the approved Noto font to the new HUD only; colors and layout unchanged.");
        }

        [MenuItem("Tools/Pocket Bump/Dash/Fit Approved Menu Title Rectangles")]
        public static void FitApprovedMenuTitles()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Scene scene = EditorSceneManager.OpenScene(MainMenuPath, OpenSceneMode.Single);
            MainMenuController menu = Find<MainMenuController>(scene);
            if (menu == null) throw new InvalidOperationException("Saved menu is missing.");
            string[] names = { "RulesTitle", "JoinTitle", "SettingsTitle" };
            TMP_Text[] titles = menu.GetComponentsInChildren<TMP_Text>(true).Where(t => names.Contains(t.name)).ToArray();
            if (titles.Length != names.Length) throw new InvalidOperationException("Expected exactly three saved menu titles.");
            BackupScene(MainMenuPath);
            foreach (TMP_Text title in titles)
            {
                // Explicit editor repair only. Do not resize/recolor authored objects on Awake.
                RectTransform rect = title.rectTransform;
                float required = Mathf.Ceil(title.GetPreferredValues(title.text, rect.rect.width, Mathf.Infinity).y + 6f);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(rect.rect.height, required));
                EditorUtility.SetDirty(rect);
            }
            Save(scene);
            Debug.Log("Fitted the three saved menu title rectangles to their actual font metrics; font, color, width and authored children preserved.");
        }

        public static void FitMenuAndCaptureFromCommandLine()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use the explicit batch verification entry point.");
            FitApprovedMenuTitles();
            DashProductionCapture.CaptureFromCommandLine();
        }

        static string BackupScene(string scenePath)
        {
            string folder = Path.Combine(".art-source", "scene-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(folder);
            string destination = Path.Combine(folder, Path.GetFileName(scenePath));
            File.Copy(scenePath, destination, false);
            if (File.Exists(scenePath + ".meta")) File.Copy(scenePath + ".meta", destination + ".meta", false);
            Debug.Log("Preserved pre-migration scene: " + Path.GetFullPath(destination));
            return folder.Replace('\\', '/');
        }

        static T Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).FirstOrDefault();
        static void Save(Scene scene)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + scene.path);
        }
    }
}
