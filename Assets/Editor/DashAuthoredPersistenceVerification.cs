using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PocketBump.Editor
{
    // Test-only fixtures are copies, never the production scenes or shared art assets.
    // SessionState deliberately survives EnterPlayMode/ExitPlayMode domain reloads.
    public static class DashAuthoredPersistenceVerification
    {
        const string Key = "PocketBump.Dash.AuthoredPersistence.";
        const string CustomRoot = "User authored test object - must survive incremental repair";
        const string CustomModel = "User authored robot model addition";
        static readonly Vector3 ArtistScale = new Vector3(1.13f, .91f, 1.07f);
        static readonly Vector3 ArtistPosition = new Vector3(.13f, .05f, -.11f);
        static readonly Color ArtistColor = new Color(.37f, .25f, .68f, 1f);
        static readonly Color MenuColor = new Color(.79f, .89f, .72f, 1f);

        public static void PrepareMenuFixture()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Prepare the fixture outside Play Mode.");
            string id = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string folder = "Assets/PocketBumpDashArt/VerificationFixtures/" + id;
            if (!AssetDatabase.IsValidFolder("Assets/PocketBumpDashArt/VerificationFixtures"))
                AssetDatabase.CreateFolder("Assets/PocketBumpDashArt", "VerificationFixtures");
            AssetDatabase.CreateFolder("Assets/PocketBumpDashArt/VerificationFixtures", id);
            string output = Path.GetFullPath("Tools/DashArenaPresentationResults/" + id + "-authored-persistence");
            if (Directory.Exists(output)) throw new IOException("Refusing to overwrite verification evidence.");
            Directory.CreateDirectory(output);
            SessionState.SetString(Key + "Folder", folder);
            SessionState.SetString(Key + "Output", output);
            SessionState.SetString(Key + "Log", "Explicit author-edit persistence fixture; no production scene/material/mesh is modified.\n");
            SessionState.SetString(Key + "MenuHash", Hash(DashProductionSceneBaker.MainMenuPath));
            SessionState.SetString(Key + "ArenaHash", Hash(DashProductionSceneBaker.ArenaPath));
            SessionState.SetString(Key + "BuildHash", Hash("ProjectSettings/EditorBuildSettings.asset"));
            Copy(DashProductionSceneBaker.MainMenuPath, folder + "/MainMenuFixture.unity");
            Copy(DashProductionSceneBaker.ArenaPath, folder + "/ArenaFixture.unity");

            Scene scene = EditorSceneManager.OpenScene(folder + "/MainMenuFixture.unity", OpenSceneMode.Single);
            MainMenuController menu = Find<MainMenuController>(scene);
            DashUiTheme customTheme = UnityEngine.Object.Instantiate(menu.Theme);
            customTheme.name = "Explicit custom artist theme fixture";
            customTheme.Panel = ArtistColor;
            AssetDatabase.CreateAsset(customTheme, folder + "/CustomTheme.asset");
            menu.Theme = customTheme;
            menu.transform.Find("MainMenuCanvas/Background").GetComponent<Image>().color = MenuColor;
            var marker = new GameObject(CustomRoot);
            marker.transform.SetParent(menu.transform, false); marker.transform.localPosition = new Vector3(7f, 2f, 3f);
            Save(scene); AssetDatabase.SaveAssets();
            scene = EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);
            DashProductionSceneBaker.RepairMenuInScene(scene, DashUiTheme.Load());
            Save(scene);
            scene = EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);
            VerifyMenuFixture(false);
            SessionState.SetString(Key + "MenuFixtureHash", Hash(scene.path));
            Note("Menu: copied, saved custom Theme/material color/user node, reloaded, ran actual incremental repair, saved and reloaded successfully.");
        }

        public static void VerifyMenuFixture(bool playing)
        {
            Expect(EditorApplication.isPlaying == playing, "Menu verification runs in expected Play Mode state: " + playing);
            Scene scene = SceneManager.GetActiveScene();
            Expect(scene.path == Folder + "/MainMenuFixture.unity", "Only the menu fixture is open");
            MainMenuController menu = Find<MainMenuController>(scene);
            Expect(AssetDatabase.GetAssetPath(menu.Theme) == Folder + "/CustomTheme.asset", "Saved custom theme reference is preserved");
            Expect(menu.Theme.Panel == ArtistColor, "Saved custom theme palette is preserved");
            Expect(menu.transform.Find("MainMenuCanvas/Background").GetComponent<Image>().color == MenuColor, "Saved per-object menu color is preserved");
            Expect(menu.transform.Find(CustomRoot) != null, "Hand-authored menu child is preserved");
            Note("Menu actual " + (playing ? "Play Mode initialization" : "Edit Mode reload") + " preserves the theme, color, and user child.");
        }

        public static void PrepareArenaFixture()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Prepare the arena fixture outside Play Mode.");
            Expect(Hash(Folder + "/MainMenuFixture.unity") == SessionState.GetString(Key + "MenuFixtureHash", ""), "Menu saved bytes unchanged by entering/exiting Play");
            Scene scene = EditorSceneManager.OpenScene(Folder + "/ArenaFixture.unity", OpenSceneMode.Single);
            DashMatchDirector director = Find<DashMatchDirector>(scene);
            director.AutomaticTicking = false;
            var robot = director.Presentation.Views.Players[0];
            robot.Motion.localScale = ArtistScale;
            robot.Motion.localPosition = ArtistPosition;
            Renderer sourceRenderer = robot.Motion.GetComponentsInChildren<Renderer>(true).First(r => r.sharedMaterial != null && !(r is LineRenderer));
            var material = new Material(sourceRenderer.sharedMaterial) { name = "Saved artist material fixture" };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", ArtistColor);
            else material.color = ArtistColor;
            AssetDatabase.CreateAsset(material, Folder + "/CustomRobot.mat");
            sourceRenderer.sharedMaterial = material;
            SessionState.SetString(Key + "RendererPath", PathBelow(sourceRenderer.transform, robot.Motion));
            GameObject customModel = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            customModel.name = CustomModel;
            UnityEngine.Object.DestroyImmediate(customModel.GetComponent<Collider>());
            customModel.transform.SetParent(robot.Motion, false);
            customModel.transform.localPosition = new Vector3(0f, 1.8f, .1f);
            customModel.transform.localScale = Vector3.one * .17f;
            customModel.GetComponent<Renderer>().sharedMaterial = material;
            var marker = new GameObject(CustomRoot);
            marker.transform.SetParent(director.transform, false);
            marker.transform.localPosition = new Vector3(7f, 2f, 3f);
            Save(scene); AssetDatabase.SaveAssets();
            scene = EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);
            // This is the exact already-migrated arena branch of the incremental tool.
            DashProductionSceneBaker.ValidateArena(scene);
            Save(scene);
            scene = EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);
            VerifyArenaFixture(false);
            SessionState.SetString(Key + "ArenaFixtureHash", Hash(scene.path));
            Note("Arena: copied, saved custom material/model addition/Motion transform/user node, reloaded, validated using incremental branch, saved and reloaded.");
        }

        public static void VerifyArenaFixture(bool playing)
        {
            Expect(EditorApplication.isPlaying == playing, "Arena verification runs in expected Play Mode state: " + playing);
            Scene scene = SceneManager.GetActiveScene();
            Expect(scene.path == Folder + "/ArenaFixture.unity", "Only the arena fixture is open");
            DashMatchDirector director = Find<DashMatchDirector>(scene);
            var robot = director.Presentation.Views.Players[0];
            if (playing)
            {
                Expect(director.Initialized, "Formal ArenaSceneController initialized the director in actual Play Mode");
                director.Presentation.Render(director.Sim, 1f);
            }
            Expect((robot.Motion.localScale - ArtistScale).sqrMagnitude < .000001f, "Robot artist scaling survives runtime presentation");
            // Decorative bobbing may temporarily offset position in Play, but the saved
            // authored pose and scale must remain intact after exiting Play.
            if (!playing) Expect((robot.Motion.localPosition - ArtistPosition).sqrMagnitude < .000001f, "Robot authored local pose survives reload");
            Transform rendererTransform = robot.Motion.Find(SessionState.GetString(Key + "RendererPath", ""));
            Expect(rendererTransform != null, "Authored renderer path survives");
            Material material = rendererTransform.GetComponent<Renderer>().sharedMaterial;
            Expect(AssetDatabase.GetAssetPath(material) == Folder + "/CustomRobot.mat", "Saved replacement material reference survives");
            Expect((material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color) == ArtistColor, "Saved replacement material color survives");
            Transform model = robot.Motion.Find(CustomModel);
            Expect(model != null && model.GetComponent<MeshFilter>().sharedMesh != null, "Authored model addition remains visible and editable");
            Expect((model.localScale - Vector3.one * .17f).sqrMagnitude < .000001f, "Authored additional model scale survives");
            Expect(director.transform.Find(CustomRoot) != null, "Hand-authored arena child survives");
            Note("Arena actual " + (playing ? "Play Mode initialization/render" : "Edit Mode reload") + " preserves material/model/author scale/user child.");
        }

        public static void FinishVerification()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before final disk verification.");
            Expect(Hash(Folder + "/ArenaFixture.unity") == SessionState.GetString(Key + "ArenaFixtureHash", ""), "Arena saved bytes unchanged by entering/exiting Play");
            EditorSceneManager.OpenScene(Folder + "/ArenaFixture.unity", OpenSceneMode.Single);
            VerifyArenaFixture(false);
            Expect(Hash(DashProductionSceneBaker.MainMenuPath) == SessionState.GetString(Key + "MenuHash", ""), "Production MainMenu SHA256 unchanged");
            Expect(Hash(DashProductionSceneBaker.ArenaPath) == SessionState.GetString(Key + "ArenaHash", ""), "Production Arena SHA256 unchanged");
            Expect(Hash("ProjectSettings/EditorBuildSettings.asset") == SessionState.GetString(Key + "BuildHash", ""), "Build Settings SHA256 unchanged");
            Note("PASS: actual EnterPlayMode and ExitPlayMode completed for both copied authored scenes. Formal scenes untouched.");
            FlushReport();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        public static void ReportIncompleteVerification()
        {
            if (SessionState.GetString(Key + "Folder", "").Length == 0) return;
            Note("TearDown reached. A PASS above is required for full-chain success; otherwise this is partial/failed evidence.");
            FlushReport();
        }

        static string Folder => SessionState.GetString(Key + "Folder", "");
        static T Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).First(c => c.gameObject.activeInHierarchy);
        static string PathBelow(Transform child, Transform root) => child.parent == root ? child.name : PathBelow(child.parent, root) + "/" + child.name;
        static void Copy(string from, string to)
        {
            if (File.Exists(to) || !AssetDatabase.CopyAsset(from, to)) throw new IOException("Could not safely copy fixture: " + to);
        }
        static void Save(Scene scene)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save fixture: " + scene.path);
        }
        static void Expect(bool condition, string message)
        {
            if (!condition) { Note("FAIL: " + message); FlushReport(); throw new InvalidOperationException(message); }
            Note("PASS: " + message);
        }
        static void Note(string message) => SessionState.SetString(Key + "Log", SessionState.GetString(Key + "Log", "") + message + "\n");
        static void FlushReport()
        {
            string output = SessionState.GetString(Key + "Output", "");
            if (output.Length == 0) return;
            // Only this unique invocation's generated report is updated across reloads.
            File.WriteAllText(Path.Combine(output, "authored-persistence.txt"),
                "Fixture assets: " + Folder + "\nUnity: " + Application.unityVersion + "\n" + SessionState.GetString(Key + "Log", ""), Encoding.UTF8);
            Debug.Log("Dash authored-persistence report: " + output);
        }
        static string Hash(string path)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
    }
}
