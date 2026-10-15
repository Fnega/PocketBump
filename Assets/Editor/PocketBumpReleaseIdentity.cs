using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PocketBump.Editor
{
    // Explicit authoring entry point: approved project models supply the icon.
    public static class PocketBumpReleaseIdentity
    {
        public const string CurrentVersion = "1.2";
        public const string IconPath = "Assets/PocketBumpDashArt/Release/PocketBumpIcon.png";

        // Version-only migration: retain the reviewed icon, scene art and display defaults.
        public static void PrepareVersion11()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before changing release settings.");
            if (PlayerSettings.companyName != "Fnega" || PlayerSettings.productName != "PocketBump" ||
                (PlayerSettings.bundleVersion != "1.0" && PlayerSettings.bundleVersion != "1.1"))
                throw new InvalidOperationException("Expected the reviewed Fnega / PocketBump 1.0 or 1.1 identity.");
            string backup = BackupSettings();
            PlayerSettings.bundleVersion = "1.1";
            AssetDatabase.SaveAssets();
            Debug.Log("Prepared PocketBump 1.1 version only. Exact settings backup: " + Path.GetFullPath(backup));
        }

        public static void PrepareVersion12()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before changing release settings.");
            if (PlayerSettings.companyName != "Fnega" || PlayerSettings.productName != "PocketBump" ||
                (PlayerSettings.bundleVersion != "1.1" && PlayerSettings.bundleVersion != "1.2"))
                throw new InvalidOperationException("Expected the reviewed Fnega / PocketBump 1.1 or 1.2 identity.");
            string backup = BackupSettings();
            PlayerSettings.bundleVersion = "1.2";
            AssetDatabase.SaveAssets();
            Debug.Log("Prepared PocketBump 1.2 version only. Exact settings backup: " + Path.GetFullPath(backup));
        }

        public static void PrepareDemo()
        {
            Prepare();
            DashDemoReleaseUiIntegration.UpgradeFormalScenes();
        }

        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before changing release settings.");
            string backup = BackupSettings();
            if (!File.Exists(IconPath)) CreateIcon();
            Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null) throw new InvalidOperationException("The authored release icon could not be loaded.");
            PlayerSettings.companyName = "Fnega";
            PlayerSettings.productName = "PocketBump";
            PlayerSettings.bundleVersion = CurrentVersion;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { icon });
            AssetDatabase.SaveAssets();
            Debug.Log("Prepared PocketBump " + CurrentVersion + " / Fnega identity. Exact settings backup: " + Path.GetFullPath(backup));
        }

        static string BackupSettings()
        {
            string backup = Path.Combine(".art-source/backups/release-identity",
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(backup);
            File.Copy("ProjectSettings/ProjectSettings.asset", Path.Combine(backup, "ProjectSettings.asset"), false);
            return backup;
        }

        public static void RebuildIcon()
        {
            if (File.Exists(IconPath))
            {
                string backup = ".art-source/backups/release-identity/icon-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png";
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                File.Copy(IconPath, backup, false);
            }
            CreateIcon(true);
        }

        static void CreateIcon(bool replace = false)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("Render the icon without -nographics.");
            var scene = EditorSceneManager.NewPreviewScene();
            RenderTexture target = null;
            Texture2D pixels = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                GameObject robot = Instantiate("Robot_Cyan", scene);
                robot.transform.position = new Vector3(-.22f, -.1f, 0f);
                GameObject ball = Instantiate("FreeScoringBall", scene);
                ball.transform.position = new Vector3(.9f, .35f, .8f);
                ball.transform.localScale *= 1.2f;
                foreach (LineRenderer trail in ball.GetComponentsInChildren<LineRenderer>(true)) trail.enabled = false;
                GameObject lightObject = new GameObject("Icon key light");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject, scene);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.7f;
                light.transform.rotation = Quaternion.Euler(35f, 155f, 0f);
                var fillObject = new GameObject("Icon fill light");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(fillObject, scene);
                Light fill = fillObject.AddComponent<Light>(); fill.type = LightType.Directional; fill.intensity = .55f;
                fill.transform.rotation = Quaternion.Euler(15f, -35f, 0f);
                var cameraObject = new GameObject("Icon camera");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.scene = scene;
                camera.transform.position = new Vector3(2.4f, 2.5f, 4.8f);
                camera.transform.LookAt(new Vector3(.05f, .9f, 0f));
                camera.orthographic = true; camera.orthographicSize = 1.7f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.08f, .22f, .24f, 1f);
                target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
                target.Create(); camera.targetTexture = target; camera.Render();
                RenderTexture.active = target;
                pixels = new Texture2D(512, 512, TextureFormat.RGBA32, false);
                pixels.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); pixels.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(IconPath));
                using (var file = new FileStream(IconPath, replace ? FileMode.Create : FileMode.CreateNew, FileAccess.Write))
                {
                    byte[] bytes = pixels.EncodeToPNG(); file.Write(bytes, 0, bytes.Length);
                }
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(IconPath);
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }

        static GameObject Instantiate(string name, UnityEngine.SceneManagement.Scene scene)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PocketBumpDashArt/Prefabs/" + name + ".prefab");
            if (prefab == null) throw new FileNotFoundException("Approved icon model missing: " + name);
            var instance = UnityEngine.Object.Instantiate(prefab);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance, scene);
            instance.SetActive(true);
            return instance;
        }
    }
}
