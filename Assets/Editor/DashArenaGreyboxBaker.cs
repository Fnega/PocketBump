using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PocketBump.Editor
{
    public static class DashArenaGreyboxBaker
    {
        public const string ScenePath = "Assets/Scenes/DashArenaGreybox.unity";

        // One explicit batch action for the first non-overwriting authoring + real render.
        public static void BuildAndCapturePreview()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch entry only.");
            BuildPreviewScene();
            CapturePreview();
        }

        public static void UpgradeBallPoolAndCapture()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch entry only.");
            UpgradeBallVisualPool();
            CapturePreview();
        }
        public const string ResultsDirectory = "Tools/DashArenaGreyboxResults";

        [MenuItem("Tools/Pocket Bump/Greybox/Create Isolated Preview (Never Overwrite)")]
        public static void CreatePreviewFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildPreviewScene();
            // Creation itself is additive to avoid mutating other scenes. The explicit
            // menu action then opens only this saved scene so Play cannot run two games.
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        // Batch executeMethod entry. Existing authored scenes and Build Settings are not touched.
        public static void BuildPreviewScene()
        {
            if (File.Exists(ScenePath) || AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                throw new InvalidOperationException("Refusing to overwrite existing preview: " + ScenePath + ". Use Open Isolated Preview instead.");
            // A fresh batch worker starts with an untitled scene: Unity cannot add
            // another scene beside it until it has been saved. Only the explicit
            // interactive menu keeps the existing, user-confirmed scene additive.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var root = new GameObject("PROVISIONAL DASH GREYBOX - NOT FINAL ART");
            SceneManager.MoveGameObjectToScene(root, scene);
            DashArenaGreybox preview = root.AddComponent<DashArenaGreybox>();
            preview.BuildAuthoredPreview();
            EditorUtility.SetDirty(preview);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Could not save isolated greybox scene: " + ScenePath);
            Debug.Log("Created isolated, provisional greybox: " + ScenePath + ". No production scenes or Build Settings changed.");
        }

        [MenuItem("Tools/Pocket Bump/Greybox/Open Isolated Preview")]
        public static void OpenPreviewScene()
        {
            if (!File.Exists(ScenePath)) throw new FileNotFoundException("Create the greybox preview first.", ScenePath);
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        // Explicit batch-only migration of our fixed, isolated preview asset. It adds
        // missing ball slots without rebuilding the world or touching Build Settings.
        public static void UpgradeBallVisualPool()
        {
            if (!Application.isBatchMode || EditorApplication.isPlaying)
                throw new InvalidOperationException("Ball visual pool upgrade is an edit-mode batch entry only.");
            if (!File.Exists(ScenePath)) throw new FileNotFoundException("Create the isolated greybox first.", ScenePath);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            try
            {
                DashArenaGreybox preview = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (DashArenaGreybox candidate in root.GetComponentsInChildren<DashArenaGreybox>(true))
                    {
                        if (preview != null) throw new InvalidOperationException("Expected one isolated greybox; refusing ambiguous upgrade.");
                        preview = candidate;
                    }
                if (preview == null || preview.gameObject.name != "PROVISIONAL DASH GREYBOX - NOT FINAL ART")
                    throw new InvalidOperationException("The fixed scene is not the expected isolated preview; refusing upgrade.");
                if (preview.EnsureBallVisualPool())
                {
                    EditorUtility.SetDirty(preview);
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene, ScenePath))
                        throw new IOException("Could not save incremental ball visual pool upgrade: " + ScenePath);
                    Debug.Log("Upgraded isolated greybox to eight ball visual slots, preserving the existing hierarchy: " + ScenePath);
                }
                else Debug.Log("Isolated greybox already has all eight ball visual slots; no scene saved.");
            }
            finally
            {
                // No runtime state is started or written into the authored scene.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        // Synchronous executeMethod entry. Needs a graphics device: do not use -nographics.
        // Camera.Render excludes IMGUI, so this evidence is explicitly world-only. A real
        // Game view screenshot through CaptureRunningPreview remains the HUD check.
        public static void CapturePreview()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("World-only capture is batch-only; use Capture Actual Running Preview in the editor.");
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Use Capture Actual Running Preview during Play Mode.");
            if (!File.Exists(ScenePath)) throw new FileNotFoundException("Create the isolated greybox first.", ScenePath);
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Actual greybox capture requires a graphics device; omit -nographics.");

            string[] args = Environment.GetCommandLineArgs();
            int width = ReadInt(args, "-greyboxCaptureWidth", 1280, 640, 4096);
            int height = ReadInt(args, "-greyboxCaptureHeight", 720, 480, 2160);
            int ticks = ReadInt(args, "-greyboxCaptureTicks", 240, 0, 1800);
            if ((width & 1) != 0) throw new ArgumentException("Capture width must be even for equal viewports.");
            string output = ResolveNewCaptureDirectory(ReadArg(args, "-greyboxCaptureOutput"));
            string imagePath = Path.Combine(output, "greybox-world-only.png");
            Scene scene = default;
            Texture2D image = null;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                // This dedicated batch worker can replace its in-memory scene setup.
                // Load the saved scene without saving any changes back to its asset.
                // Unity 2022.3 has no public OpenPreviewScene API.
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                SceneManager.SetActiveScene(scene);
                DashArenaGreybox preview = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    preview = root.GetComponentInChildren<DashArenaGreybox>(true);
                    if (preview != null) break;
                }
                if (preview == null) throw new InvalidOperationException("Saved scene contains no DashArenaGreybox.");
                preview.AutomaticTicking = false;
                preview.ShowHud = false;
                preview.RestartPreview();
                // Declared fixture, not a natural complete-match test: both move toward
                // midfield. No direct placement, score edits, or forced simulation phase.
                for (int tick = 0; tick < ticks; tick++)
                    preview.StepPreview(new DashArenaInput { Move = Vector2.down },
                        new DashArenaInput { Move = Vector2.up });

                image = new Texture2D(width, height, TextureFormat.RGB24, false);
                for (int seat = 0; seat < 2; seat++)
                {
                    Camera camera = preview.PreviewCameras[seat];
                    RenderTexture oldTarget = camera.targetTexture;
                    Rect oldRect = camera.rect;
                    float oldAspect = camera.aspect;
                    Scene oldScene = camera.scene;
                    bool oldEnabled = camera.enabled;
                    RenderTexture target = RenderTexture.GetTemporary(width / 2, height, 24, RenderTextureFormat.ARGB32);
                    try
                    {
                        camera.enabled = false;
                        camera.scene = scene;
                        camera.targetTexture = target;
                        camera.rect = new Rect(0f, 0f, 1f, 1f);
                        camera.aspect = (width * 0.5f) / height;
                        preview.RenderPreview(1f);
                        camera.Render();
                        RenderTexture.active = target;
                        image.ReadPixels(new Rect(0f, 0f, width / 2, height), seat * (width / 2), 0, false);
                    }
                    finally
                    {
                        camera.targetTexture = oldTarget;
                        camera.rect = oldRect;
                        camera.aspect = oldAspect;
                        camera.scene = oldScene;
                        camera.enabled = oldEnabled;
                        RenderTexture.active = previousActive;
                        RenderTexture.ReleaseTemporary(target);
                    }
                }
                image.Apply(false, false);
                byte[] png = image.EncodeToPNG();
                if (png == null || png.Length == 0) throw new IOException("Unity produced no PNG data.");
                Directory.CreateDirectory(output);
                using (var stream = new FileStream(imagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    stream.Write(png, 0, png.Length);
                string info = "capture=actual_unity_camera_render\n" +
                    "scope=PROVISIONAL_GREYBOX_WORLD_ONLY_NOT_FINAL_ART\n" +
                    "fixture=scripted_midfield_movement_not_full_match_evidence\n" +
                    "hud_included=false; IMGUI requires Capture Actual Running Preview\n" +
                    "scene=" + ScenePath + "\n" +
                    "dimensions=" + width + "x" + height + "\n" +
                    "tick=" + preview.State.Tick + "\nphase=" + preview.State.Phase + "\n" +
                    "scores=" + preview.State.Scores[0] + ":" + preview.State.Scores[1] + "\n" +
                    "rules=" + preview.State.Rules.ContentHash + "\n" +
                    "graphics=" + SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName + "\n";
                using (var stream = new FileStream(Path.Combine(output, "capture-info.txt"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream)) writer.Write(info);
                Debug.Log("Saved actual Unity greybox world-only capture: " + imagePath +
                    ". IMGUI HUD, final art, and completed gameplay are NOT validated by this image.");
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
                // Discard only this batch worker's transient simulation/render state.
                // Never save it back over the authored preview scene.
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        static string ResolveNewCaptureDirectory(string requested)
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string results = Path.GetFullPath(Path.Combine(project, ResultsDirectory));
            string path = string.IsNullOrEmpty(requested)
                ? Path.Combine(results, "capture-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"))
                : Path.GetFullPath(Path.IsPathRooted(requested) ? requested : Path.Combine(results, requested));
            string allowedPrefix = results.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Capture output must be a new child directory inside " + results);
            if (Directory.Exists(path) || File.Exists(path))
                throw new IOException("Refusing to overwrite existing capture target: " + path);
            return path;
        }

        static string ReadArg(string[] args, string flag)
        {
            int index = Array.IndexOf(args, flag);
            if (index < 0) return null;
            if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException("Missing value for " + flag);
            return args[index + 1];
        }

        static int ReadInt(string[] args, string flag, int fallback, int minimum, int maximum)
        {
            string value = ReadArg(args, flag);
            if (value == null) return fallback;
            if (!int.TryParse(value, out int parsed) || parsed < minimum || parsed > maximum)
                throw new ArgumentException(flag + " must be between " + minimum + " and " + maximum);
            return parsed;
        }

        [MenuItem("Tools/Pocket Bump/Greybox/Capture Actual Running Preview")]
        public static void CaptureRunningPreview()
        {
            if (!EditorApplication.isPlaying || UnityEngine.Object.FindObjectOfType<DashArenaGreybox>() == null)
                throw new InvalidOperationException("Run the isolated greybox scene before capturing it.");
            string directory = Path.GetFullPath(ResultsDirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "greybox-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
            if (File.Exists(path)) throw new IOException("Refusing to overwrite screenshot: " + path);
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("Requested actual running greybox screenshot (not final art): " + path);
        }
    }
}
