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
    // Explicit, additive authoring. Never runs a world baker or rewrites old art.
    public static class DashGroundReadinessIntegration
    {
        public const string RootName = "Local Dash Readiness - Authored";
        public const string AssetFolder = "Assets/PocketBumpDashArt/GroundDashReadiness";
        public const string MeshPath = AssetFolder + "/GroundReadinessQuad.asset";
        public const string MaterialPath = AssetFolder + "/GroundReadiness.mat";
        const string ShaderName = "PocketBump/DashGroundReadiness";
        const string TagManagerPath = "ProjectSettings/TagManager.asset";
        const string BuildSettingsPath = "ProjectSettings/EditorBuildSettings.asset";

        [MenuItem("Tools/Pocket Bump/Dash/Install Local Ground Rings (Incremental Only)")]
        public static void IntegrateFormalArena()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before explicit scene integration.");
            string arena = DashProductionSceneBaker.ArenaPath;
            Scene loaded = SceneManager.GetSceneByPath(arena);
            Require(!loaded.IsValid() || !loaded.isLoaded || !loaded.isDirty, "Save your Arena edits first so the backup exactly matches the authored scene.");
            string arenaBefore = Hash(arena);
            var protectedFiles = ProtectedFiles();
            string backup = Path.GetFullPath(".art-source/backups/dash-ground-readiness/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N"));
            Require(!Directory.Exists(backup), "Backup destination already exists.");
            Directory.CreateDirectory(backup);
            File.Copy(arena, Path.Combine(backup, "Arena.unity"), false);
            File.Copy(arena + ".meta", Path.Combine(backup, "Arena.unity.meta"), false);
            Require(Hash(Path.Combine(backup, "Arena.unity")) == arenaBefore && Hash(Path.Combine(backup, "Arena.unity.meta")) == protectedFiles[arena + ".meta"], "Exact scene backup verification failed.");
            Scene active = SceneManager.GetActiveScene();
            bool opened = !loaded.IsValid() || !loaded.isLoaded;
            Scene scene = opened ? EditorSceneManager.OpenScene(arena, OpenSceneMode.Additive) : loaded;
            try
            {
                DashProductionSceneBaker.ValidateArena(scene);
                var binding = FindOne<DashReadinessHud>(scene);
                var preserved = Snapshot(scene, binding);
                string oldBinding = LegacyBindingReferences(binding);
                bool changed = UpgradeLoadedArena(scene);
                Require(!UpgradeLoadedArena(scene), "The second ground-ring install must be a no-op.");
                VerifySnapshot(preserved);
                Require(LegacyBindingReferences(binding) == oldBinding, "An existing readiness HUD field or old marker reference changed.");
                DashProductionSceneBaker.ValidateArena(scene);
                VerifyFiles(protectedFiles);
                if (changed)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    Require(EditorSceneManager.SaveScene(scene, arena), "Could not save Arena. Exact backup: " + backup);
                }
                else Require(Hash(arena) == arenaBefore, "A no-op integration changed scene bytes.");
                VerifyFiles(protectedFiles);
                Debug.Log("Local ground-ring integration passed: changed=" + changed + ", preserved authored objects=" + preserved.Count + ", exact backup=" + backup + ", Arena SHA256=" + Hash(arena));
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
            IntegrateFormalArena();
        }

        // Also usable on a disposable copied scene. Never saves the scene itself.
        public static bool UpgradeLoadedArena(Scene scene)
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Integrate in Edit Mode only.");
            Require(scene.IsValid() && scene.isLoaded, "Load the explicit target scene first.");
            var binding = FindOne<DashReadinessHud>(scene);
            var presentation = FindOne<DashArenaPresentation>(scene);
            Require(binding.Hud != null && presentation.Views?.Players != null && presentation.Views.Players.Length == 2, "Existing authored HUD and two robot roots are required.");
            var footprints = new Renderer[2];
            for (int seat = 0; seat < 2; seat++)
            {
                Transform actor = presentation.Views.Players[seat]?.Root;
                Require(actor != null && actor.gameObject.scene == scene, "Robot root reference is missing or belongs to another scene.");
                Transform footprint = actor.Find("Identity footprint");
                footprints[seat] = footprint != null ? footprint.GetComponent<Renderer>() : null;
                Require(footprints[seat] != null, "The original robot Identity footprint renderer is required; no replacement art is inferred.");
            }
            if (binding.HasLocalGroundRings)
            {
                ValidateRings(scene, binding);
                // Validation must be read-only, including on failure. Calling the
                // installer here could silently replace an artist's mistaken refs
                // and hide renderers before reporting that the binding differs.
                var serialized = new SerializedObject(binding);
                var savedFootprints = serialized.FindProperty("_identityFootprints");
                Require(savedFootprints != null && savedFootprints.isArray && savedFootprints.arraySize == 2,
                    "Existing ground-ring footprint bindings are incomplete. Repair the explicit references before integration.");
                for (int seat = 0; seat < 2; seat++)
                    Require(savedFootprints.GetArrayElementAtIndex(seat).objectReferenceValue == footprints[seat],
                        "Existing ground-ring footprint binding differs for player " + (seat + 1) + ". Repair the explicit references before integration.");
                return false;
            }
            Require(binding.GroundRingFor(0) == null && binding.GroundRingFor(1) == null, "Partial ground-ring references require explicit repair; existing art was not replaced.");
            Require(!scene.GetRootGameObjects().Any(g => g.name == RootName) && !All<DashGroundReadinessRing>(scene).Any(), "Unbound ground-ring art already exists. Bind or rename it explicitly; it was not replaced.");
            EnsureAssets(out Mesh mesh, out Material material);
            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            var created = new DashGroundReadinessRing[2];
            for (int seat = 0; seat < 2; seat++)
            {
                var actorRing = new GameObject("Player " + (seat + 1) + " Ground Ring");
                actorRing.transform.SetParent(root.transform, false);
                actorRing.layer = presentation.Views.Players[seat].Root.gameObject.layer;
                Vector3 point = presentation.Views.Players[seat].Root.position;
                actorRing.transform.position = new Vector3(point.x, 0f, point.z);
                var ring = actorRing.AddComponent<DashGroundReadinessRing>();
                ring.ConfigureAssets(mesh, material);
                ring.IdentityColor = binding.Hud.PlayerIdentityColor(seat);
                actorRing.transform.position = new Vector3(point.x, ring.GroundHeight, point.z);
                actorRing.transform.localScale = Vector3.one * ring.Radius;
                created[seat] = ring;
            }
            Require(binding.InstallLocalGroundRings(created, footprints), "New ground-ring references were not installed.");
            EditorUtility.SetDirty(binding);
            ValidateRings(scene, binding);
            return true;
        }

        static void EnsureAssets(out Mesh mesh, out Material material)
        {
            Shader shader = Shader.Find(ShaderName);
            Require(shader != null, "Import the ground readiness shader before integrating the scene.");
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Require(mesh != null || (!File.Exists(MeshPath) && !File.Exists(MeshPath + ".meta")), "Mesh path is occupied by an unreadable or different asset; it was not overwritten.");
            Require(material != null || (!File.Exists(MaterialPath) && !File.Exists(MaterialPath + ".meta")), "Material path is occupied by an unreadable or different asset; it was not overwritten.");
            Require(material == null || material.shader == shader, "Existing ground-ring material uses a different shader. It was not reset.");
            if (!AssetDatabase.IsValidFolder(AssetFolder))
                Require(!string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets/PocketBumpDashArt", "GroundDashReadiness")), "Could not create ground-ring asset folder.");
            if (mesh == null)
            {
                mesh = DashGroundReadinessRing.CreateUnitGroundQuad();
                mesh.name = "Ground Readiness Unit XZ Quad";
                AssetDatabase.CreateAsset(mesh, MeshPath);
                AssetDatabase.SaveAssetIfDirty(mesh);
            }
            if (material == null)
            {
                material = new Material(shader) { name = "Ground Readiness" };
                AssetDatabase.CreateAsset(material, MaterialPath);
                AssetDatabase.SaveAssetIfDirty(material);
            }
            // No global SaveAssets: unrelated edited materials must remain untouched.
        }

        static void ValidateRings(Scene scene, DashReadinessHud binding)
        {
            Require(binding.HasLocalGroundRings, "Both ground-ring references are required.");
            Require(binding.GroundRingFor(0) != binding.GroundRingFor(1), "Each player requires its own ground ring.");
            for (int seat = 0; seat < 2; seat++)
            {
                var ring = binding.GroundRingFor(seat);
                Require(ring != null && ring.gameObject.scene == scene, "A ground ring belongs to a different scene.");
                Require(ring.transform.parent != null && ring.transform.parent.parent == null, "Ground rings require an independent scene root, outside robot yaw and bob transforms.");
                Require(ring.RingRenderer != null && ring.RingRenderer.sharedMaterial != null && ring.GetComponent<MeshFilter>()?.sharedMesh != null, "A ground ring has missing authored mesh/material references.");
                Require(ring.GetComponent<Collider>() == null, "Readiness rings must not participate in gameplay collisions.");
            }
        }

        static IEnumerable<T> All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true));
        static T FindOne<T>(Scene scene) where T : Component
        {
            T[] found = All<T>(scene).ToArray();
            Require(found.Length == 1, "Exactly one existing " + typeof(T).Name + " is required; no old hierarchy is rebuilt.");
            return found[0];
        }
        static Dictionary<UnityEngine.Object, string> Snapshot(Scene scene, DashReadinessHud binding)
        {
            var result = new Dictionary<UnityEngine.Object, string>();
            foreach (Component component in All<Component>(scene))
            {
                if (component == null) continue;
                if (component != binding) result[component] = EditorJsonUtility.ToJson(component);
                result[component.gameObject] = EditorJsonUtility.ToJson(component.gameObject);
            }
            return result;
        }
        static void VerifySnapshot(Dictionary<UnityEngine.Object, string> snapshot)
        {
            foreach (var item in snapshot)
                Require(item.Key != null && EditorJsonUtility.ToJson(item.Key) == item.Value, "An existing authored object/component changed: " + item.Key);
        }
        static string LegacyBindingReferences(DashReadinessHud binding)
        {
            // These are all pre-ground-ring serialized fields: _hud, _views plus the
            // component's own enabled/name/hide flags. Only new binding fields may grow.
            string result = binding.enabled + ":" + binding.name + ":" + binding.hideFlags + ":" + binding.Hud.GetInstanceID();
            var serialized = new SerializedObject(binding);
            var views = serialized.FindProperty("_views");
            result += ":" + views.arraySize;
            for (int view = 0; view < views.arraySize; view++)
            {
                var actors = views.GetArrayElementAtIndex(view).FindPropertyRelative("Actors");
                result += ":" + actors.arraySize;
                for (int actor = 0; actor < actors.arraySize; actor++) result += ":" + actors.GetArrayElementAtIndex(actor).objectReferenceInstanceIDValue;
            }
            return result;
        }
        static Dictionary<string, string> ProtectedFiles() => new[] { DashProductionSceneBaker.ArenaPath + ".meta", DashProductionSceneBaker.MainMenuPath, DashProductionSceneBaker.MainMenuPath + ".meta", BuildSettingsPath, TagManagerPath }.ToDictionary(p => p, Hash);
        static void VerifyFiles(Dictionary<string, string> files)
        { foreach (var file in files) Require(Hash(file.Key) == file.Value, "Protected file changed: " + file.Key); }
        static string Hash(string path)
        { using (var sha = SHA256.Create()) using (var file = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", ""); }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        // Tests use only a disposable scene copy. SessionState survives the real
        // Play Mode/domain reload, so production bytes can be checked afterwards.
        const string Key = "PocketBump.GroundReadinessPersistence.";
        static readonly Color ArtistColor = new Color(.31f, .46f, .71f, .82f);
        const string ArtistChild = "Artist ground-ring child - preserve";
        public static void PreparePersistenceFixture()
        {
            Require(!EditorApplication.isPlaying, "Prepare outside Play Mode.");
            string folder = "Assets/PocketBumpDashArt/VerificationFixtures";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/PocketBumpDashArt", "VerificationFixtures");
            string path = folder + "/GroundReadiness-" + Guid.NewGuid().ToString("N") + ".unity";
            SessionState.SetString(Key + "Path", path);
            SessionState.SetString(Key + "Arena", Hash(DashProductionSceneBaker.ArenaPath));
            foreach (var file in ProtectedFiles()) SessionState.SetString(Key + file.Key, file.Value);
            Require(AssetDatabase.CopyAsset(DashProductionSceneBaker.ArenaPath, path), "Could not create disposable Arena fixture.");
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var binding = FindOne<DashReadinessHud>(scene);
            var preserved = Snapshot(scene, binding);
            string oldBinding = LegacyBindingReferences(binding);
            UpgradeLoadedArena(scene);
            VerifySnapshot(preserved);
            Require(LegacyBindingReferences(binding) == oldBinding, "Fixture integration changed old HUD references.");
            var ring = binding.GroundRingFor(0);
            ring.IdentityColor = ArtistColor;
            ring.Radius = 1.32f; ring.Width = .29f; ring.GroundHeight = .075f;
            ring.MinimumCoolingGapDegrees = 18f; ring.OutlineWidth = .035f;
            var artist = new GameObject(ArtistChild); artist.transform.SetParent(ring.transform, false);
            artist.transform.localPosition = new Vector3(.2f, .3f, .4f);
            foreach (var director in All<DashMatchDirector>(scene)) director.AutomaticTicking = false;
            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene), "Ground-ring fixture save failed.");
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Require(!UpgradeLoadedArena(scene), "Second install must preserve artist edits.");
            VerifyPersistenceFixture(false);
            SessionState.SetString(Key + "Fixture", Hash(path));
            SessionState.SetString(Key + "Mesh", Hash(MeshPath));
            SessionState.SetString(Key + "Material", Hash(MaterialPath));
        }
        public static void VerifyPersistenceFixture(bool playing)
        {
            Require(EditorApplication.isPlaying == playing, "Unexpected Play Mode state.");
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.path == SessionState.GetString(Key + "Path", ""), "Only the disposable scene fixture may be used.");
            var binding = FindOne<DashReadinessHud>(scene);
            ValidateRings(scene, binding);
            Require(All<DashGroundReadinessRing>(scene).Count() == 2, "Expected exactly two authored world rings.");
            Require(binding.GetComponentsInChildren<DashReadinessGraphic>(true).Length == 4, "Existing LAN markers were removed or duplicated.");
            if (playing)
            {
                var presentation = FindOne<DashArenaPresentation>(scene);
                var candidate = DashArenaSimulation.Create(ArenaRulesProfile.CreateHandfeelCandidate(), 191);
                DashArenaSimulation.StartMatch(candidate); candidate.Phase = MatchState.Live;
                candidate.Players[0].DashCooldownTicks = 90;
                candidate.Players[1].DashCooldownTicks = 45;
                int checksum = candidate.Checksum();
                presentation.BindState(candidate, false, 0);
                binding.Refresh(candidate, presentation, false, 0);
                Require(binding.LocalGroundRingsActive, "The local candidate did not use the saved ground-ring binding.");
                Require(binding.GroundRingFor(0).State.Kind == DashReadinessKind.Cooling && binding.GroundRingFor(0).State.Progress == .5f && binding.GroundRingFor(1).State.Progress == .75f, "Both players must receive their full cooldown progress.");
                Require(candidate.Checksum() == checksum, "Presentation changed gameplay authority.");
            }
            var ring = binding.GroundRingFor(0);
            Require(ring.IdentityColor == ArtistColor && ring.Radius == 1.32f && ring.Width == .29f && ring.GroundHeight == .075f && ring.MinimumCoolingGapDegrees == 18f && ring.OutlineWidth == .035f, "Saved artist ring styles were overwritten.");
            Transform child = ring.transform.Find(ArtistChild);
            Require(child != null && child.localPosition == new Vector3(.2f, .3f, .4f), "Unknown artist child was lost or edited.");
            if (!playing) Require(!UpgradeLoadedArena(scene), "Repeated installation changed authored rings.");
        }
        public static void FinishPersistenceFixture()
        {
            VerifyPersistenceFixture(false);
            Require(Hash(SessionState.GetString(Key + "Path", "")) == SessionState.GetString(Key + "Fixture", ""), "Play Mode changed saved fixture bytes.");
            Require(Hash(DashProductionSceneBaker.ArenaPath) == SessionState.GetString(Key + "Arena", ""), "Production Arena changed during persistence test.");
            foreach (var file in ProtectedFiles()) Require(file.Value == SessionState.GetString(Key + file.Key, ""), "Protected production file changed: " + file.Key);
            Require(Hash(MeshPath) == SessionState.GetString(Key + "Mesh", "") && Hash(MaterialPath) == SessionState.GetString(Key + "Material", ""), "Play Mode rewrote shared ground-ring assets.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }
}
