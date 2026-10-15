using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 编辑器菜单工具：把可运行时生成的主菜单和竞技场烘焙为正式 Scene/资源。
// 烘焙后仍使用相同控制器，便于版本管理，也避免手工摆放与代码参数失配。
namespace PocketBump.Editor
{
    public static class PocketBumpSceneBaker
    {
        const string ResourcesFolder = "Assets/Resources/PocketBump";
        const string MainMenuPath = "Assets/Scenes/MainMenu.unity";
        const string ArenaPath = "Assets/Scenes/Arena.unity";

        [MenuItem("Tools/Pocket Bump/Bake Authored Scenes")]
        // 一次性刷新依赖资源并重建两个正式场景，确保提交的 Scene 与当前代码生成逻辑一致。
        public static void BakeAuthoredScenes()
        {
            DashProductionSceneBaker.MigrateFormalScenes();
        }

        // Retained only as reference for old fixtures. Never exposed as a scene command.
        static void BakeLegacyAuthoredScenes()
        {
            EnsureFolders();
            EnsureTmpResources();
            CreateSpriteAsset("Box", false);
            CreateSpriteAsset("Circle", true);
            CreateMaterials();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            SpriteFactory.ReloadAssets();
            VisualPalette.ReloadAssets();
            MeshFactory.ReloadAssets();
            UiFactory.ReloadAssets();

            BakeMainMenu();
            BakeArena();
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MainMenuPath, true),
                new EditorBuildSettingsScene(ArenaPath, true)
            };
            AssetDatabase.SaveAssets();
            Debug.Log("Pocket Bump authored scenes baked successfully.");
        }

        [MenuItem("Tools/Pocket Bump/Bake Main Menu Only")]
        public static void BakeMainMenuOnly()
        {
            DashProductionSceneBaker.MigrateMainMenuOnly();
        }

        // 创建最小根对象后让 MainMenuController 生成 UI，再保存为可直接运行的场景。
        static void BakeMainMenu()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateCamera(false);
            CreateLight();

            var session = new GameObject("GameSession");
            session.AddComponent<GameSession>();

            var menu = new GameObject("MainMenu");
            MainMenuController controller = menu.AddComponent<MainMenuController>();
            controller.BuildAuthoredHierarchy();

            EditorSceneManager.SaveScene(scene, MainMenuPath);
        }

        // 使用运行时工厂搭建场地/玩家/HUD，随后写入序列化引用，避免手工拖拽漏项。
        static void BakeArena()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateCamera(true);
            CreateLight();

            var matchRoot = new GameObject("Match");
            MatchDirector director = matchRoot.AddComponent<MatchDirector>();
            Arena arena = Arena.Build(matchRoot.transform);
            PlayerMotor playerOne = MatchDirector.CreatePlayer(
                matchRoot.transform,
                0,
                ArenaLayout.PlayerOneSpawn,
                arena.BounceMat);
            PlayerMotor playerTwo = MatchDirector.CreatePlayer(
                matchRoot.transform,
                1,
                ArenaLayout.PlayerTwoSpawn,
                arena.BounceMat);
            MatchHud hud = MatchHud.Build(matchRoot.transform);

            director.Arena = arena;
            director.PlayerOne = playerOne;
            director.PlayerTwo = playerTwo;
            director.Hud = hud;

            arena.EnsureMarblePool(MatchSettings.MaxMarbles);
            arena.ClearMarbles();

            var systems = new GameObject("Scene Systems");
            ArenaSceneController controller = systems.AddComponent<ArenaSceneController>();
            controller.SetDirector(director);

            EditorSceneManager.SaveScene(scene, ArenaPath);
        }

        static void CreateCamera(bool gameplay)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            Camera camera = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
            camera.backgroundColor = VisualPalette.Tabletop;
            if (gameplay)
            {
                MatchDirector.ApplyMatchCamera(camera);
            }
            else
            {
                camera.orthographic = true;
                camera.orthographicSize = 5f;
                go.transform.position = new Vector3(0f, 0f, -10f);
            }
        }

        static void CreateLight()
        {
            MatchDirector.EnsureMatchLight();
        }

        // AssetDatabase 创建资源前要求父目录存在，逐级保证可让首次克隆项目也能烘焙。
        static void EnsureFolders()
        {
            EnsureFolder("Assets", "Resources");
            EnsureFolder("Assets/Resources", "PocketBump");
            EnsureFolder("Assets", "Scenes");
        }

        static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        // TMP 默认资源可能尚未导入；烘焙前检查并尝试导入，避免保存缺字体的 UI。
        static void EnsureTmpResources()
        {
            if (Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF") != null)
                return;

            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
            if (package == null)
                throw new FileNotFoundException("TextMeshPro package could not be located.");

            string unityPackage = Path.Combine(
                package.resolvedPath,
                "Package Resources",
                "TMP Essential Resources.unitypackage");
            if (!File.Exists(unityPackage))
                throw new FileNotFoundException("TMP Essential Resources package was not found.", unityPackage);

            AssetDatabase.ImportPackage(unityPackage, false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        // 只在资源缺失时生成基础纹理，避免每次烘焙覆盖已存在的 meta/GUID 引用。
        static void CreateSpriteAsset(string name, bool circle)
        {
            const int size = 64;
            string assetPath = $"{ResourcesFolder}/{name}.png";
            string absolutePath = Path.GetFullPath(assetPath);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = (size - 1) * 0.5f;
            float radius = size * 0.5f - 1.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool visible = !circle ||
                        Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) <= radius;
                    texture.SetPixel(x, y, visible ? Color.white : Color.clear);
                }
            }
            texture.Apply();
            File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = size;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        // 预建共享材质供 Resources 加载，使运行时构建与烘焙场景使用相同着色配置。
        static void CreateMaterials()
        {
            string materialPath = $"{ResourcesFolder}/NeonSprite.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Sprites/Default"))
                {
                    name = "NeonSprite"
                };
                AssetDatabase.CreateAsset(material, materialPath);
            }

            string litPath = $"{ResourcesFolder}/NeonLit.mat";
            Material lit = AssetDatabase.LoadAssetAtPath<Material>(litPath);
            if (lit == null)
            {
                Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
                if (litShader == null) litShader = Shader.Find("Sprites/Default");
                lit = new Material(litShader) { name = "NeonLit" };
                if (lit.HasProperty("_Smoothness")) lit.SetFloat("_Smoothness", 0.42f);
                if (lit.HasProperty("_Metallic")) lit.SetFloat("_Metallic", 0.08f);
                AssetDatabase.CreateAsset(lit, litPath);
            }

            string physicsPath = $"{ResourcesFolder}/BumpBounce.physicsMaterial2D";
            PhysicsMaterial2D physics = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(physicsPath);
            if (physics == null)
            {
                physics = new PhysicsMaterial2D("BumpBounce")
                {
                    bounciness = ArenaLayout.Bounciness,
                    friction = 0.12f
                };
                AssetDatabase.CreateAsset(physics, physicsPath);
            }
        }
    }
}
