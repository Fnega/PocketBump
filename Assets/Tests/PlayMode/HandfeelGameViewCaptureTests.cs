#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace PocketBump.Tests
{
    // Opt-in ACTUAL GameView framebuffer capture of STAGED presentation roles.
    // No natural match, Player build, networking acceptance or performance claim.
    public sealed class HandfeelGameViewCaptureTests
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const string ArenaPath = "Assets/Scenes/Arena.unity";
        Scene previousScene, captureScene, menuCaptureScene;
        GameSession gameSession, previousSession;
        PlayKind previousKind;
        int previousPoints, previousBalls;
        bool oldPreview, configured;
        float oldFixed, oldScale;
        EditorWindow window, previousFocus;
        object sizeGroup;
        Type gameViewType;
        readonly List<object> ownedSizes = new List<object>();
        readonly Dictionary<EditorWindow, int> oldSizes = new Dictionary<EditorWindow, int>();
        readonly Dictionary<EditorWindow, object> oldSizeObjects = new Dictionary<EditorWindow, object>();
        readonly Dictionary<Behaviour, bool> oldViews = new Dictionary<Behaviour, bool>();
        readonly Dictionary<string, string> protectedHashes = new Dictionary<string, string>();
        readonly List<Capture> captures = new List<Capture>();
        readonly List<SizeRecord> removedOwnedSizes = new List<SizeRecord>();
        readonly List<string> cleanupErrors = new List<string>();
        SizeRecord[] customSizesBefore = Array.Empty<SizeRecord>(), preservedCustomSizes = Array.Empty<SizeRecord>();
        SizeRecord[] customSizesAfter = Array.Empty<SizeRecord>();
        string preferencesPath, preferencesHashBefore, preferencesHashAfter;
        bool captureComplete, customSizesRestored, localOnly, localMapVisuals;
        string output;
        DashMatchDirector original;
        DashArenaPresentation presentation;
        DashMatchHud hud;

        [UnityTest]
        public IEnumerator CaptureAuthoredLocalAndLanRolesAtThreeSizes()
        {
            string[] args = Environment.GetCommandLineArgs();
            localMapVisuals = Array.IndexOf(args, "-localMapVisuals") >= 0;
            localOnly = localMapVisuals || Array.IndexOf(args, "-handfeelLocalOnly") >= 0;
            int argument = Array.IndexOf(args, "-handfeelVisualOutput");
            if (argument < 0) Assert.Ignore("Opt-in visible GameView capture; requires -handfeelVisualOutput <new Tools/HandfeelApplyResults directory>.");
            Assert.That(argument + 1, Is.LessThan(args.Length));
            Assert.That(Application.isBatchMode, Is.False, "A visible interactive Editor is required, not a batch/offscreen substitute.");
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(UnityEngine.Rendering.GraphicsDeviceType.Null));
            string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "HandfeelApplyResults"));
            output = Path.GetFullPath(args[argument + 1]);
            Assert.That(output.StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(Directory.Exists(output) || File.Exists(output), Is.False, "Never overwrite capture evidence.");
            Directory.CreateDirectory(output);
            oldFixed = Time.fixedDeltaTime; oldScale = Time.timeScale;
            oldPreview = DashHandfeelPreview.Selected;
            previousScene = SceneManager.GetActiveScene(); previousFocus = EditorWindow.focusedWindow;
            previousSession = Object.FindObjectOfType<GameSession>();
            gameSession = GameSession.Ensure(); previousKind = gameSession.Kind;
            previousPoints = gameSession.SelectedSettings.pointsToWin; previousBalls = gameSession.SelectedSettings.marbleCount;
            configured = true;
            SetKind(gameSession, PlayKind.Local);
            gameSession.SelectedSettings.pointsToWin = 8; gameSession.SelectedSettings.marbleCount = 1;
            DashHandfeelPreview.Selected = true; Time.timeScale = 1f;
            foreach (string path in new[] { ArenaPath, ArenaPath + ".meta", "Assets/Scenes/MainMenu.unity", "ProjectSettings/EditorBuildSettings.asset" })
                protectedHashes.Add(path, Hash(path));
            foreach (Camera camera in Object.FindObjectsOfType<Camera>()) { oldViews.Add(camera, camera.enabled); camera.enabled = false; }
            foreach (Canvas canvas in Object.FindObjectsOfType<Canvas>()) { oldViews.Add(canvas, canvas.enabled); canvas.enabled = false; }
            foreach (AudioListener listener in Object.FindObjectsOfType<AudioListener>()) { oldViews.Add(listener, listener.enabled); listener.enabled = false; }
            // ArenaSceneController.Awake disables legacy directors globally. Also
            // prevent any previously open world's clock/input from running behind this
            // additive fixture, then restore exactly the enabled flags we observed.
            foreach (MatchDirector director in Object.FindObjectsOfType<MatchDirector>())
            { oldViews.Add(director, director.enabled); director.enabled = false; }
            foreach (DashMatchDirector director in Object.FindObjectsOfType<DashMatchDirector>())
            { oldViews.Add(director, director.enabled); director.enabled = false; }
            CreateOwnedGameView();
            if (localMapVisuals) yield return CaptureMapSettings();
            yield return SceneManager.LoadSceneAsync(ArenaPath, LoadSceneMode.Additive);
            captureScene = SceneManager.GetSceneAt(SceneManager.sceneCount - 1);
            Assert.That(captureScene.path, Is.EqualTo(ArenaPath));
            SceneManager.SetActiveScene(captureScene);
            original = captureScene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<DashMatchDirector>(true)).Single();
            original.AutomaticTicking = false; original.enabled = false;
            Assert.That(original.Rules.UsesRefinedHandfeel, Is.True);
            presentation = original.Presentation; hud = original.Hud;
            Assert.That(hud.GetComponent<DashReadinessHud>(), Is.Not.Null,
                "Capture never installs UI: the saved Arena must contain authored marker references.");
            foreach (int seat in localOnly ? new[] { -1 } : new[] { -1, 0, 1 })
            {
                DashMatchDirector director = seat < 0 ? original : StagedLanDirector(seat);
                foreach (string sample in localMapVisuals ? new[] { "maps-on", "maps-off", "maps-restored" }
                    : localOnly ? new[] { "half-ready", "last-empty", "dash-stun" } : new[] { "half-ready" })
                {
                    Stage(director, sample);
                    if (localMapVisuals && hud.LocalMinimapsVisible != (sample != "maps-off"))
                        Assert.That(director.TryToggleLocalMinimaps(), Is.True);
                    foreach (Vector2Int size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(1024, 768) })
                    {
                        SelectOwnedSize(size.x, size.y);
                        for (int frame = 0; frame < 12; frame++)
                        {
                            window.Focus(); window.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
                            presentation.Render(director.Sim, 1f); hud.RefreshFromDirector(); Canvas.ForceUpdateCanvases();
                            yield return null;
                        }
                        Assert.That(Screen.width, Is.EqualTo(size.x), "Capture must use the actual requested GameView framebuffer.");
                        Assert.That(Screen.height, Is.EqualTo(size.y));
                        if (localMapVisuals)
                            foreach (var seatView in hud.Seats)
                            {
                                Assert.That(seatView.Minimap.gameObject.activeInHierarchy, Is.EqualTo(sample != "maps-off"));
                                Assert.That(seatView.Status.gameObject.activeInHierarchy, Is.True);
                            }
                        var markers = hud.GetComponent<DashReadinessHud>();
                        markers.Refresh(director.Sim, presentation, seat >= 0, Math.Max(0, seat));
                        int view = Math.Max(0, seat);
                        if (seat < 0 && markers.HasLocalGroundRings)
                        {
                            Assert.That(markers.LocalGroundRingsActive, Is.True);
                            for (int actor = 0; actor < 2; actor++)
                            {
                                var ring = markers.GroundRingFor(actor);
                                Assert.That(ring.GetComponent<MeshRenderer>().enabled, Is.True);
                                Assert.That(ring.State.Kind, Is.EqualTo(DashReadinessState.Evaluate(director.Sim, actor, actor).Kind));
                                for (int localView = 0; localView < 2; localView++)
                                    Assert.That(markers.MarkerFor(localView, actor).gameObject.activeInHierarchy, Is.False);
                            }
                        }
                        else
                        {
                            Assert.That(localOnly, Is.False, "Local feedback capture requires authored ground rings.");
                            Assert.That(markers.MarkerFor(view, view).gameObject.activeInHierarchy, Is.True);
                            Assert.That(markers.MarkerFor(view, 1 - view).gameObject.activeInHierarchy, Is.True);
                        }
                        int before = director.Sim.Checksum();
                        string role = seat < 0 ? "local-split" : "staged-lan-seat" + seat;
                        string image = role + (localOnly ? "-" + sample : "") + "-" + size.x + "x" + size.y + ".png";
                        string path = Path.Combine(output, image);
                        yield return new WaitForEndOfFrame();
                        ScreenCapture.CaptureScreenshot(path);
                        float deadline = Time.realtimeSinceStartup + 5f;
                        while ((!File.Exists(path) || new FileInfo(path).Length == 0) && Time.realtimeSinceStartup < deadline) yield return null;
                        Assert.That(File.Exists(path), Is.True, path);
                        yield return null;
                        VerifyPng(path, size.x, size.y);
                        Assert.That(director.Sim.Checksum(), Is.EqualTo(before), "View refreshes cannot step or mutate authority.");
                        captures.Add(new Capture { image = image, role = role, width = size.x, height = size.y, checksum = before });
                    }
                }
                if (seat >= 0)
                {
                    Assert.That(director.LanSession.SendAttempts, Is.Zero, "Staged role must never pump a transport.");
                    Assert.That(director.LanSession.ReceivedDatagrams, Is.Zero);
                    director.Configure(null, null);
                    Object.DestroyImmediate(director.gameObject);
                }
            }
            Assert.That(captures.Count, Is.EqualTo(localMapVisuals ? 12 : 9));
            captureComplete = true; // Report is written AFTER teardown restoration checks.
        }

        IEnumerator CaptureMapSettings()
        {
            const string menuPath = "Assets/Scenes/MainMenu.unity";
            yield return SceneManager.LoadSceneAsync(menuPath, LoadSceneMode.Additive);
            menuCaptureScene = SceneManager.GetSceneAt(SceneManager.sceneCount - 1);
            Assert.That(menuCaptureScene.path, Is.EqualTo(menuPath));
            SceneManager.SetActiveScene(menuCaptureScene);
            var menu = menuCaptureScene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MainMenuController>(true)).Single();
            // UI actions use a memory-only preference store. Never alter the user's
            // real bindings just to demonstrate a custom map key or rejected key.
            var bindings = new DashControlBindings(new CaptureBindingStore());
            typeof(MainMenuController).GetField("_bindings", Instance).SetValue(menu, bindings);
            typeof(MainMenuController).GetMethod("ShowSettings", Instance).Invoke(menu, new object[] { true });
            Transform card = menu.transform.Find("MainMenuCanvas/SettingsPanel/" + MainMenuController.MinimapBindingCardName);
            Assert.That(card, Is.Not.Null, "The saved settings scene must contain the map control.");
            int sample = 0;
            foreach (Vector2Int size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(1024, 768) })
            {
                if (sample > 0)
                {
                    card.Find("MinimapToggleBind").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                    menu.SubmitBindingKey(sample == 1 ? KeyCode.Tab : KeyCode.K);
                    Assert.That(bindings.MinimapToggle, Is.EqualTo(KeyCode.Tab));
                }
                SelectOwnedSize(size.x, size.y);
                for (int frame = 0; frame < 12; frame++)
                {
                    window.Focus(); window.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
                    Canvas.ForceUpdateCanvases(); yield return null;
                }
                Assert.That(Screen.width, Is.EqualTo(size.x)); Assert.That(Screen.height, Is.EqualTo(size.y));
                var corners = new Vector3[4]; ((RectTransform)card).GetWorldCorners(corners);
                foreach (Vector3 corner in corners)
                {
                    Vector2 pixel = RectTransformUtility.WorldToScreenPoint(null, corner);
                    Assert.That(pixel.x, Is.InRange(0f, size.x), "Local map settings must fit the actual GameView.");
                    Assert.That(pixel.y, Is.InRange(0f, size.y));
                }
                string role = sample == 0 ? "settings-map-default-M" : sample == 1 ? "settings-map-custom-Tab" : "settings-map-conflict-keeps-Tab";
                string filename = role + "-" + size.x + "x" + size.y + ".png";
                string path = Path.Combine(output, filename);
                yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(path);
                float deadline = Time.realtimeSinceStartup + 5f;
                while ((!File.Exists(path) || new FileInfo(path).Length == 0) && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(File.Exists(path), Is.True, path); yield return null; VerifyPng(path, size.x, size.y);
                captures.Add(new Capture { image = filename, role = role, width = size.x, height = size.y });
                sample++;
            }
            menu.SubmitBindingKey(KeyCode.Escape);
            yield return SceneManager.UnloadSceneAsync(menuCaptureScene);
        }

        sealed class CaptureBindingStore : IDashBindingStore
        {
            readonly Dictionary<string, int> values = new Dictionary<string, int>();
            public bool HasKey(string key) => values.ContainsKey(key);
            public int GetInt(string key, int fallback) => values.TryGetValue(key, out int value) ? value : fallback;
            public void SetInt(string key, int value) => values[key] = value;
            public void Save() { }
        }

        DashMatchDirector StagedLanDirector(int seat)
        {
            var child = new GameObject("Owned staged LAN presentation role " + seat);
            SceneManager.MoveGameObjectToScene(child, captureScene);
            var director = child.AddComponent<DashMatchDirector>();
            director.AutomaticTicking = false; director.enabled = false;
            child.AddComponent<DashNetworkReporter>().Bind(director, Path.Combine(output, "staged-reports"));
            ArenaRulesProfile rules = ArenaRulesProfile.CreateHandfeelCandidate();
            DashLanSession session = seat == 0
                ? DashLanSession.Host(rules, 671, 314159u, () => 0f, allowHandfeelCandidate: true)
                : DashLanSession.Join(rules, new IPEndPoint(IPAddress.Loopback, 9), () => 0f, 671UL, allowHandfeelCandidate: true);
            Assert.That(session.SocketBound, Is.True, session.LastSocketError);
            director.InitializeLan(session);
            director.Configure(presentation, hud);
            return director;
        }

        void Stage(DashMatchDirector director, string sample)
        {
            DashArenaState state = director.Sim;
            state.Tick = 240; state.Phase = MatchState.Live; state.PhaseTicks = 0; state.GateClosed = false;
            for (int seat = 0; seat < 2; seat++)
            {
                state.Players[seat].Alive = true;
                state.Players[seat].Position = seat == 0 ? new Vector2(-2f, 0f) : new Vector2(2f, 2f);
                state.Players[seat].Facing = seat == 0 ? Vector2.up : Vector2.down;
                state.Players[seat].ControlVelocity = state.Players[seat].ExternalVelocity = Vector2.zero;
                state.Players[seat].DashTicks = state.Players[seat].StunTicks = state.Players[seat].GreenTicks = state.Players[seat].PurpleTicks = 0;
                state.Players[seat].DashCooldownTicks = seat == 0 ? 90 : 0;
            }
            if (sample == "last-empty")
            {
                state.Players[0].DashCooldownTicks = 1;
                state.Players[1].DashCooldownTicks = 180;
            }
            else if (sample == "dash-stun")
            {
                state.Players[0].DashCooldownTicks = 0;
                state.Players[0].DashTicks = 9;
                state.Players[1].StunTicks = 12;
            }
            state.Balls[0] = new DashBallState { Active = true, Position = new Vector2(0f, -1.8f) };
            presentation.ReconcileState(state); presentation.Render(state, 1f);
            hud.ClearPickupMessages(); hud.RefreshFromDirector();
        }

        void CreateOwnedGameView()
        {
            Assembly editor = typeof(EditorWindow).Assembly;
            gameViewType = editor.GetType("UnityEditor.GameView", true);
            foreach (Object found in Resources.FindObjectsOfTypeAll(gameViewType))
            {
                var old = (EditorWindow)found;
                oldSizes.Add(old, (int)gameViewType.GetProperty("selectedSizeIndex", Instance).GetValue(old));
            }
            Type sizesType = editor.GetType("UnityEditor.GameViewSizes", true);
            object sizes = sizesType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null);
            Type groupType = editor.GetType("UnityEditor.GameViewSizeGroupType", true);
            sizeGroup = sizesType.GetMethod("GetGroup", Instance).Invoke(sizes, new[] { Enum.Parse(groupType, "Standalone") });
            customSizesBefore = ReadCustomSizes();
            preservedCustomSizes = customSizesBefore.ToArray();
            preferencesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Unity", "Editor-5.x", "Preferences", "GameViewSizes.asset");
            preferencesHashBefore = File.Exists(preferencesPath) ? Hash(preferencesPath) : "absent";
            Type group = sizeGroup.GetType();
            int total = (int)group.GetMethod("GetTotalCount", Instance).Invoke(sizeGroup, null);
            foreach (var old in oldSizes)
            {
                Assert.That(old.Value, Is.InRange(0, total - 1));
                oldSizeObjects.Add(old.Key, group.GetMethod("GetGameViewSize", Instance).Invoke(sizeGroup, new object[] { old.Value }));
                gameViewType.GetProperty("selectedSizeIndex", Instance).SetValue(old.Key, 0);
            }
            // Preserve EVERY pre-existing entry. Unity may truncate labels, so names
            // cannot establish ownership, and this run never cleans another run's entries.
            RestorePreviousSizeSelections();
            AssertCustomSizesPreserved();
            window = (EditorWindow)ScriptableObject.CreateInstance(gameViewType);
            window.titleContent = new GUIContent("Handfeel capture (owned test view)");
            window.position = new Rect(60f, 60f, 1180f, 780f);
            window.Show(); window.Focus();
        }

        void SelectOwnedSize(int width, int height)
        {
            Assembly editor = typeof(EditorWindow).Assembly;
            Type sizeType = editor.GetType("UnityEditor.GameViewSize", true);
            Type kind = editor.GetType("UnityEditor.GameViewSizeType", true);
            object size = Activator.CreateInstance(sizeType, Instance, null,
                new[] { Enum.Parse(kind, "FixedResolution"), (object)width, height, "Handfeel owned " + Guid.NewGuid().ToString("N") }, null);
            Type group = sizeGroup.GetType();
            group.GetMethod("AddCustomSize", Instance).Invoke(sizeGroup, new[] { size }); ownedSizes.Add(size);
            int builtins = (int)group.GetMethod("GetBuiltinCount", Instance).Invoke(sizeGroup, null);
            int custom = (int)group.GetMethod("GetCustomCount", Instance).Invoke(sizeGroup, null);
            gameViewType.GetProperty("selectedSizeIndex", Instance).SetValue(window, builtins + custom - 1);
            window.Focus(); window.Repaint();
        }

        [UnityTearDown]
        public IEnumerator RestoreOwnedCaptureState()
        {
            if (!configured) yield break;
            DashHandfeelPreview.Selected = oldPreview;
            Time.fixedDeltaTime = oldFixed; Time.timeScale = oldScale;
            if (captureScene.IsValid() && captureScene.isLoaded) yield return SceneManager.UnloadSceneAsync(captureScene);
            if (menuCaptureScene.IsValid() && menuCaptureScene.isLoaded) yield return SceneManager.UnloadSceneAsync(menuCaptureScene);
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            foreach (var old in oldViews) if (old.Key != null) old.Key.enabled = old.Value;
            if (gameSession != null)
            {
                if (previousSession == null) Object.DestroyImmediate(gameSession.gameObject);
                else
                {
                    SetKind(gameSession, previousKind);
                    gameSession.SelectedSettings.pointsToWin = previousPoints; gameSession.SelectedSettings.marbleCount = previousBalls;
                }
            }
            if (window != null) window.Close();
            if (sizeGroup != null)
            {
                try
                {
                    foreach (var old in oldSizes)
                        if (old.Key != null) gameViewType.GetProperty("selectedSizeIndex", Instance).SetValue(old.Key, 0);
                    RemoveOwnedSizes();
                    RestorePreviousSizeSelections();
                    AssertCustomSizesPreserved();
                    customSizesRestored = true;
                }
                catch (Exception error) { cleanupErrors.Add("GameView sizes: " + error); }
                finally { customSizesAfter = ReadCustomSizes(); }
            }
            if (previousFocus != null) previousFocus.Focus();
            foreach (var protectedFile in protectedHashes)
                if (Hash(protectedFile.Key) != protectedFile.Value) cleanupErrors.Add("Capture changed protected file: " + protectedFile.Key);
            preferencesHashAfter = !string.IsNullOrEmpty(preferencesPath) && File.Exists(preferencesPath) ? Hash(preferencesPath) : "absent";
            if (preferencesHashBefore != null && preferencesHashAfter != preferencesHashBefore)
                cleanupErrors.Add("GameViewSizes.asset changed on disk; this capture never calls SaveToHDD.");
            var report = new CaptureReport
            {
                evidence_kind = "actual_visible_Editor_GameView_of_staged_presentation_roles",
                limitation = localOnly
                    ? "Local-only staged Editor presentation. No LAN role or socket created. Not a Player, natural match, performance test or final user handfeel approval."
                    : "Not a Player, natural game, real LAN acceptance, performance test or final user handfeel approval. LAN role uses an unpumped ephemeral socket; only rendered seat ownership is inspected.",
                source_scene = localMapVisuals ? "Assets/Scenes/MainMenu.unity; " + ArenaPath : ArenaPath,
                unity = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName,
                captures = captures.ToArray(), captures_complete = captureComplete,
                custom_sizes_before = customSizesBefore, preserved_custom_sizes = preservedCustomSizes,
                removed_owned_sizes = removedOwnedSizes.ToArray(), custom_sizes_after = customSizesAfter,
                custom_sizes_restored = customSizesRestored,
                preferences_hash_before = preferencesHashBefore, preferences_hash_after = preferencesHashAfter,
                cleanup_complete = cleanupErrors.Count == 0 && customSizesRestored, cleanup_errors = cleanupErrors.ToArray()
            };
            using (var stream = new FileStream(Path.Combine(output, "capture-report.json"), FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream)) writer.Write(JsonUtility.ToJson(report, true));
            Assert.That(cleanupErrors, Is.Empty);
            Assert.That(customSizesRestored, Is.True);
        }

        static SizeRecord DescribeSize(object size)
        {
            Type type = size.GetType();
            return new SizeRecord
            {
                label = (string)type.GetProperty("baseText", Instance).GetValue(size),
                width = (int)type.GetProperty("width", Instance).GetValue(size),
                height = (int)type.GetProperty("height", Instance).GetValue(size),
                kind = Convert.ToInt32(type.GetProperty("sizeType", Instance).GetValue(size))
            };
        }

        SizeRecord[] ReadCustomSizes()
        {
            Type group = sizeGroup.GetType();
            int builtin = (int)group.GetMethod("GetBuiltinCount", Instance).Invoke(sizeGroup, null);
            int count = (int)group.GetMethod("GetCustomCount", Instance).Invoke(sizeGroup, null);
            var result = new SizeRecord[count];
            for (int i = 0; i < count; i++)
                result[i] = DescribeSize(group.GetMethod("GetGameViewSize", Instance).Invoke(sizeGroup, new object[] { builtin + i }));
            return result;
        }

        void RemoveOwnedSizes()
        {
            Type group = sizeGroup.GetType();
            int builtin = (int)group.GetMethod("GetBuiltinCount", Instance).Invoke(sizeGroup, null);
            int count = (int)group.GetMethod("GetCustomCount", Instance).Invoke(sizeGroup, null);
            for (int index = count - 1; index >= 0; index--)
            {
                int totalIndex = builtin + index;
                object size = group.GetMethod("GetGameViewSize", Instance).Invoke(sizeGroup, new object[] { totalIndex });
                SizeRecord record = DescribeSize(size);
                if (!ownedSizes.Any(owned => ReferenceEquals(owned, size))) continue;
                // Both lookup and removal take the TOTAL index in Unity 2022.3.
                // Ownership is the object this invocation added, never its truncated label.
                // Recheck the exact target immediately before removing anything.
                object target = group.GetMethod("GetGameViewSize", Instance).Invoke(sizeGroup, new object[] { totalIndex });
                Assert.That(ReferenceEquals(size, target), Is.True);
                Assert.That(ownedSizes.Any(owned => ReferenceEquals(owned, target)), Is.True);
                int beforeCount = (int)group.GetMethod("GetCustomCount", Instance).Invoke(sizeGroup, null);
                group.GetMethod("RemoveCustomSize", Instance).Invoke(sizeGroup, new object[] { totalIndex });
                Assert.That((int)group.GetMethod("GetCustomCount", Instance).Invoke(sizeGroup, null), Is.EqualTo(beforeCount - 1));
                removedOwnedSizes.Add(record);
            }
        }

        void RestorePreviousSizeSelections()
        {
            Type group = sizeGroup.GetType();
            int total = (int)group.GetMethod("GetTotalCount", Instance).Invoke(sizeGroup, null);
            foreach (var old in oldSizeObjects)
            {
                if (old.Key == null) continue;
                int selected = -1;
                for (int index = 0; index < total; index++)
                    if (ReferenceEquals(old.Value, group.GetMethod("GetGameViewSize", Instance).Invoke(sizeGroup, new object[] { index }))) { selected = index; break; }
                // Every pre-existing selected object must still exist, even if its
                // label resembles a previous test's label. Never silently replace it.
                Assert.That(selected, Is.GreaterThanOrEqualTo(0), "An unrelated selected resolution must be preserved.");
                gameViewType.GetProperty("selectedSizeIndex", Instance).SetValue(old.Key, selected);
            }
        }

        void AssertCustomSizesPreserved() => CollectionAssert.AreEqual(
            preservedCustomSizes.Select(size => JsonUtility.ToJson(size)).ToArray(),
            ReadCustomSizes().Select(size => JsonUtility.ToJson(size)).ToArray(),
            "Non-owned custom resolution labels, dimensions, types and order must be unchanged.");

        static void SetKind(GameSession session, PlayKind kind) =>
            typeof(GameSession).GetField("<Kind>k__BackingField", Instance).SetValue(session, kind);

        static void VerifyPng(string path, int width, int height)
        {
            var image = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                Assert.That(image.LoadImage(File.ReadAllBytes(path)), Is.True);
                Assert.That(image.width, Is.EqualTo(width)); Assert.That(image.height, Is.EqualTo(height));
                Color32[] pixels = image.GetPixels32(); int visible = 0;
                for (int i = 0; i < pixels.Length; i += 53)
                    if (pixels[i].r + pixels[i].g + pixels[i].b > 30) visible++;
                Assert.That(visible, Is.GreaterThan(pixels.Length / 530), "Blank/black capture is not accepted as visual evidence.");
            }
            finally { Object.DestroyImmediate(image); }
        }

        static string Hash(string path)
        {
            using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(stream));
        }
        [Serializable] sealed class Capture { public string image, role; public int width, height, checksum; }
        [Serializable] sealed class SizeRecord { public string label; public int width, height, kind; }
        [Serializable] sealed class CaptureReport
        {
            public string evidence_kind, limitation, source_scene, unity, gpu;
            public Capture[] captures;
            public bool captures_complete, custom_sizes_restored, cleanup_complete;
            public SizeRecord[] custom_sizes_before, preserved_custom_sizes, removed_owned_sizes, custom_sizes_after;
            public string preferences_hash_before, preferences_hash_after;
            public string[] cleanup_errors;
        }
    }
}
#endif
