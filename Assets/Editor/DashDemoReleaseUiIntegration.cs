using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PocketBump.Editor
{
    // Additive authoring pass. No old scene/prefab/material baker is invoked and no
    // simulation geometry changes. Backups are the exact on-disk source bytes.
    public static class DashDemoReleaseUiIntegration
    {
        public const string MarkerName = "Demo scoring ball contrast ring";
        public const string MarkerMeshPath = "Assets/PocketBumpDashArt/Release/ScoringBallContrastRing.asset";
        public const float MarkerRadius = .48f, MarkerWidth = .08f, MarkerGroundHeight = .04f;

        [MenuItem("Tools/Pocket Bump/Dash/Install Free Demo UI (Incremental)")]
        public static void UpgradeFormalScenes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before explicit scene authoring.");
            string[] sources = { DashProductionSceneBaker.MainMenuPath, DashProductionSceneBaker.ArenaPath, DashProductionSceneBaker.FontAssetPath };
            foreach (string path in sources)
            {
                if (!File.Exists(path) || !File.Exists(path + ".meta")) throw new FileNotFoundException("Approved existing asset is required.", path);
                Scene existing = SceneManager.GetSceneByPath(path);
                if (existing.IsValid() && existing.isLoaded && existing.isDirty) throw new InvalidOperationException("Save pending scene edits before exact-byte backup: " + path);
            }
            string backup = Path.GetFullPath(Path.Combine(".art-source/backups/free-demo-1-0", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8)));
            Directory.CreateDirectory(backup);
            foreach (string source in sources)
                foreach (string file in new[] { source, source + ".meta" })
                {
                    string destination = Path.Combine(backup, Path.GetFileName(file)); File.Copy(file, destination, false);
                    if (Hash(file) != Hash(destination)) throw new IOException("Source backup verification failed: " + file);
                }
            AddChineseGlyphs();
            Scene menu = EditorSceneManager.OpenScene(DashProductionSceneBaker.MainMenuPath, OpenSceneMode.Single);
            MainMenuController controller = Find<MainMenuController>(menu);
            if (controller == null) throw new InvalidOperationException("Existing main menu controller missing.");
            DashDemoHomeUi.Install(controller).Wire();
            EditorSceneManager.MarkSceneDirty(menu);
            if (!EditorSceneManager.SaveScene(menu)) throw new IOException("Cannot save main menu.");
            Scene arena = EditorSceneManager.OpenScene(DashProductionSceneBaker.ArenaPath, OpenSceneMode.Single);
            UpgradeLoadedArena(arena);
            EditorSceneManager.MarkSceneDirty(arena);
            if (!EditorSceneManager.SaveScene(arena)) throw new IOException("Cannot save arena.");
            AssetDatabase.SaveAssets();
            Debug.Log("Free-demo UI, report/audio components and ball rings authored. Exact scene/font backup: " + backup);
        }

        public static bool UpgradeLoadedArena(Scene scene)
        {
            DashMatchHud hud = Find<DashMatchHud>(scene);
            DashMatchDirector director = Find<DashMatchDirector>(scene);
            DashArenaPresentation presentation = Find<DashArenaPresentation>(scene);
            if (hud == null || director == null || presentation == null) throw new InvalidOperationException("Approved Dash arena components are required.");
            bool changed = hud.GetComponent<DashDemoMatchMenu>() == null;
            DashDemoMatchMenu.Install(hud);
            if (director.GetComponent<DashNetworkReporter>() == null) { director.gameObject.AddComponent<DashNetworkReporter>(); changed = true; }
            if (director.GetComponent<DashAudioFeedback>() == null) { director.gameObject.AddComponent<DashAudioFeedback>(); changed = true; }
            Material ink = AssetDatabase.LoadAssetAtPath<Material>("Assets/PocketBumpDashArt/Materials/Dark robot visor and joints.mat");
            if (ink == null) throw new FileNotFoundException("Approved dark material missing.");
            Mesh markerMesh = EnsureMarkerMesh();
            foreach (DashArenaArtSample.BallView ball in presentation.Views.Balls)
                changed |= UpgradeBallMarker(ball.Root, markerMesh, ink);
            return changed;
        }

        static Mesh EnsureMarkerMesh()
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MarkerMeshPath);
            if (mesh != null) return mesh; // Never regenerate a later artist-edited asset.
            if (File.Exists(MarkerMeshPath)) throw new InvalidOperationException("Marker mesh path contains an unexpected asset.");
            if (!AssetDatabase.IsValidFolder("Assets/PocketBumpDashArt/Release"))
                AssetDatabase.CreateFolder("Assets/PocketBumpDashArt", "Release");
            mesh = BuildMarkerMesh(); AssetDatabase.CreateAsset(mesh, MarkerMeshPath); return mesh;
        }

        // An explicit, lit annulus avoids LineRenderer-facing/normal ambiguities.
        // Winding and normals both point up; it is saved as a reusable editable asset.
        public static Mesh BuildMarkerMesh()
        {
            const int segments = 48;
            var vertices = new Vector3[segments * 2]; var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length]; var triangles = new int[segments * 6];
            float inner = MarkerRadius - MarkerWidth * .5f, outer = MarkerRadius + MarkerWidth * .5f;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = direction * outer; vertices[i * 2 + 1] = direction * inner;
                normals[i * 2] = normals[i * 2 + 1] = Vector3.up;
                for (int j = 0; j < 2; j++) uv[i * 2 + j] = new Vector2(vertices[i * 2 + j].x, vertices[i * 2 + j].z) / (outer * 2f) + Vector2.one * .5f;
                int next = ((i + 1) % segments) * 2, offset = i * 6;
                triangles[offset] = i * 2; triangles[offset + 1] = i * 2 + 1; triangles[offset + 2] = next;
                triangles[offset + 3] = next; triangles[offset + 4] = i * 2 + 1; triangles[offset + 5] = next + 1;
            }
            var mesh = new Mesh { name = "Scoring ball contrast annulus" };
            mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.triangles = triangles; mesh.RecalculateBounds();
            return mesh;
        }

        public static bool UpgradeBallMarker(Transform ball, Mesh mesh, Material ink)
        {
            Transform marker = ball.Find(MarkerName);
            if (marker != null && marker.GetComponent<MeshFilter>() != null && marker.GetComponent<MeshRenderer>() != null)
                return false; // Preserve subsequent authored color/transform edits.
            if (marker == null)
            {
                marker = new GameObject(MarkerName).transform; marker.SetParent(ball, false);
            }
            else
            {
                // The only repaired legacy representation is this release's own named
                // line marker; unknown user-authored objects are not overwritten.
                LineRenderer oldLine = marker.GetComponent<LineRenderer>();
                if (oldLine == null) throw new InvalidOperationException("Unexpected existing ball marker; explicit author review required.");
                if (oldLine.sharedMaterial != null) ink = oldLine.sharedMaterial;
                UnityEngine.Object.DestroyImmediate(oldLine);
            }
            marker.localRotation = Quaternion.identity;
            marker.localPosition = Vector3.up * (MarkerGroundHeight - ArenaRulesProfile.CreateProductionV1().BallRadius);
            var filter = marker.gameObject.AddComponent<MeshFilter>(); filter.sharedMesh = mesh;
            var renderer = marker.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = ink;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            return true;
        }

        static void AddChineseGlyphs()
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DashProductionSceneBaker.FontAssetPath);
            string corpus = DashMatchHud.RequiredChineseGlyphs + DashDemoGuide.Title + DashDemoGuide.LeftRules + DashDemoGuide.RightRules + DashDemoGuide.Scope + DashDemoGuide.ExtraGlyphs;
            // Include the authored labels and useful production-report errors verbatim,
            // so the serialized atlas also works without runtime glyph generation.
            foreach (string source in new[] { "DashDemoGuide.cs", "DashDemoMatchMenu.cs", "DashDemoHomeUi.cs", "DashNetworkReport.cs" })
                corpus += File.ReadAllText("Assets/Scripts/PocketBump/" + source);
            corpus = new string(corpus.Where(c => (c >= '\u2e80' && c <= '\u9fff') || c == '↑' || c == '↓' || c == '（' || c == '）' || c == '“' || c == '”' || c == '：' || c == '；' || c == '，').Distinct().ToArray());
            if (!font.HasCharacters(corpus, out uint[] missing, false, false))
            {
                if (!font.TryAddCharacters(missing, out uint[] unavailable)) throw new InvalidOperationException("Approved font lacks demo glyphs: " + string.Join(",", unavailable));
                foreach (Texture2D atlas in font.atlasTextures)
                {
                    if (atlas == null) continue;
                    if (!AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, font);
                    EditorUtility.SetDirty(atlas);
                }
                EditorUtility.SetDirty(font); AssetDatabase.SaveAssets();
            }
        }
        static T Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).FirstOrDefault(c => c.gameObject.activeInHierarchy);
        static string Hash(string path)
        { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)); }
    }
}
