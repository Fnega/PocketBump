using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
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
    // Off-screen presentation fixtures, NOT a Player/GameView or natural LAN test.
    // Opens the saved production scenes, renders their actual world + TMP hierarchy,
    // and never saves those scenes or changes gameplay/PlayerPrefs/Build Settings.
    public static class DashProductionCapture
    {
        const string ResultsRoot = "Tools/DashArenaPresentationResults";
        static readonly Vector2Int[] Sizes = { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(1024, 768) };
        static readonly List<string> Checks = new List<string>();
        static readonly List<string> Failures = new List<string>();
        static string _output;

        public static void CaptureFromCommandLine()
        {
            if (!Application.isBatchMode || EditorApplication.isPlaying)
                throw new InvalidOperationException("Use an edit-mode Unity batch with -executeMethod PocketBump.Editor.DashProductionCapture.CaptureFromCommandLine (without -nographics).");
            _output = NewOutputDirectory(); Checks.Clear(); Failures.Clear();
            string menuHash = Hash(DashProductionSceneBaker.MainMenuPath), arenaHash = Hash(DashProductionSceneBaker.ArenaPath);
            string buildHash = Hash("ProjectSettings/EditorBuildSettings.asset");
            float oldFixedDelta = Time.fixedDeltaTime;
            Exception error = null;
            try
            {
                VerifyFont();
                CaptureMenu();
                foreach (PlayKind kind in new[] { PlayKind.Local, PlayKind.HostLan, PlayKind.JoinLan }) CaptureArena(kind);
            }
            catch (Exception caught) { error = caught; Failures.Add(caught.ToString()); }
            finally
            {
                Time.fixedDeltaTime = oldFixedDelta;
                // Discard all temporary hierarchy, fixture state, and canvas changes.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Check(Hash(DashProductionSceneBaker.MainMenuPath) == menuHash, "Saved MainMenu scene SHA256 unchanged");
                Check(Hash(DashProductionSceneBaker.ArenaPath) == arenaHash, "Saved Arena scene SHA256 unchanged");
                Check(Hash("ProjectSettings/EditorBuildSettings.asset") == buildHash, "Build Settings SHA256 unchanged");
                WriteReport();
            }
            if (error != null || Failures.Count > 0)
                throw new InvalidOperationException("Presentation capture has " + Failures.Count + " failure(s); inspect " + Path.Combine(_output, "capture-info.md"), error);
            Debug.Log("Dash presentation fixtures captured and geometry/glyph checks passed: " + _output);
        }

        static void VerifyFont()
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DashProductionSceneBaker.FontAssetPath);
            if (font == null) throw new InvalidOperationException("Approved TMP font is missing.");
            bool covered = font.HasCharacters(DashMatchHud.RequiredChineseGlyphs, out uint[] missing, false, false);
            Check(covered, "Saved approved TMP font covers all HUD RequiredChineseGlyphs without fallback or dynamic additions" +
                (covered ? "" : ": " + string.Join(", ", missing.Select(c => "U+" + c.ToString("X4")))));
        }

        static void CaptureMenu()
        {
            Scene scene = EditorSceneManager.OpenScene(DashProductionSceneBaker.MainMenuPath, OpenSceneMode.Single);
            MainMenuController menu = FindActive<MainMenuController>(scene);
            foreach (string mode in new[] { "home", "rules", "join", "settings" })
            {
                menu.BuildAuthoredHierarchy();
                if (mode != "home")
                {
                    string buttonName = mode == "rules" ? "Start" : mode == "join" ? "JoinLan" : "Settings";
                    Button button = menu.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == buttonName);
                    if (button == null) throw new InvalidOperationException("Menu navigation button missing: " + buttonName);
                    button.onClick.Invoke();
                }
                Canvas canvas = menu.GetComponentInChildren<Canvas>(true);
                foreach (Vector2Int size in Sizes)
                    Capture("menu-" + mode, canvas, Array.Empty<Camera>(), size, null, PlayKind.Local);
            }
        }

        static void CaptureArena(PlayKind kind)
        {
            Scene scene = EditorSceneManager.OpenScene(DashProductionSceneBaker.ArenaPath, OpenSceneMode.Single);
            DashProductionSceneBaker.ValidateArena(scene);
            DashMatchDirector director = FindActive<DashMatchDirector>(scene);
            director.AutomaticTicking = false;
            ArenaRulesProfile rules = ArenaRulesProfile.CreateProductionV1();
            DashLanSession session = null;
            try
            {
                if (kind == PlayKind.Local) director.InitializeLocal(rules, 314159u);
                else
                {
                    // Ephemeral local port; no Pump/Advance, no peer, no datagrams sent.
                    // Only the real mode/assigned-seat presentation binding is exercised.
                    session = kind == PlayKind.HostLan
                        ? DashLanSession.Host(rules, 709, 314159u, () => 0f)
                        : DashLanSession.Join(rules, new IPEndPoint(IPAddress.Loopback, 9), () => 0f, 709UL);
                    if (!session.SocketBound) throw new InvalidOperationException("Cannot create bounded presentation fixture socket: " + session.LastSocketError);
                    director.InitializeLan(session);
                }
                DashArenaState state = director.Sim;
                state.Phase = MatchState.Live; state.GateClosed = false; state.Tick = 240;
                state.Players[0].Position = new Vector2(-5f, -5f);
                state.Players[1].Position = new Vector2(5f, 5f);
                state.Players[0].GreenTicks = 240; state.Players[0].DashCooldownTicks = 95;
                state.Players[1].PurpleTicks = 240;
                state.Players[0].StunTicks = 24;
                state.Balls[0].Active = true; state.Balls[0].Position = new Vector2(-3f, -2f);
                for (int i = 0; i < state.Pickups.Length; i++)
                    state.Pickups[i] = new DashPickupState { Active = true, Id = i + 1, Kind = (DashPickupKind)i, Position = new Vector2(-3f + 3f * i, 0f) };
                director.Presentation.ReconcileState(state);
                director.Presentation.Render(state, 1f);
                director.Hud.RefreshFromDirector();
                string mode = kind == PlayKind.Local ? "local-split" : kind == PlayKind.HostLan ? "lan-host-seat0-fixture" : "lan-join-seat1-fixture";
                foreach (Vector2Int size in Sizes)
                {
                    SetPickup(director, 0, DashPickupKind.Green);
                    SetPickup(director, 1, DashPickupKind.Purple);
                    Capture(mode + "-status", director.Hud.GetComponent<Canvas>(), ActiveCameras(director), size, director.Hud, kind);
                }
                for (int seat = 0; seat < 2; seat++)
                    director.Hud.ShowConfirmedPickup(new DashArenaEvent(state.Tick, state.Epoch, seat, 40 + seat,
                        DashArenaEventKind.TeleportFailed, state.Players[seat].Position, seat, DashPickupKind.Blue), rules);
                director.Hud.RefreshFromDirector();
                Capture(mode + "-blue-no-safe-position", director.Hud.GetComponent<Canvas>(), ActiveCameras(director), Sizes[0], director.Hud, kind);
                if (kind != PlayKind.Local)
                {
                    // Deliberate wait-display fixture only; real wait/resume transport
                    // is verified independently by pacing socket/PlayMode tests.
                    FieldInfo waitingSince = typeof(DashLanSession).GetField("_inputWaitSince", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (waitingSince == null) throw new InvalidOperationException("Pacing display fixture binding changed.");
                    waitingSince.SetValue(session, 0f);
                    director.Hud.RefreshFromDirector();
                    foreach (Vector2Int size in Sizes)
                        Capture(mode + "-input-wait-display-fixture", director.Hud.GetComponent<Canvas>(), ActiveCameras(director), size, director.Hud, kind);
                    waitingSince.SetValue(session, -1f);
                    typeof(DashMatchDirector).GetField("_failure", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(director, "INPUT PROGRESS TIMEOUT");
                    director.Hud.RefreshFromDirector();
                    foreach (Vector2Int size in Sizes)
                        Capture(mode + "-input-progress-timeout-display-fixture", director.Hud.GetComponent<Canvas>(), ActiveCameras(director), size, director.Hud, kind);
                    // Explicit failure-display fixture, not a claimed observed DESYNC.
                    typeof(DashMatchDirector).GetField("_failure", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(director, "DESYNC (DISPLAY FIXTURE)");
                    director.Hud.RefreshFromDirector();
                    Capture(mode + "-network-paused-display-fixture", director.Hud.GetComponent<Canvas>(), ActiveCameras(director), Sizes[0], director.Hud, kind);
                    Check(session.SendAttempts == 0 && session.ReceivedDatagrams == 0, mode + " created no network traffic");
                }
                else
                {
                    director.Hud.ClearPickupMessages(); state.Phase = MatchState.Kickoff; state.PhaseTicks = 180;
                    director.Hud.RefreshFromDirector();
                    Capture(mode + "-kickoff", director.Hud.GetComponent<Canvas>(), ActiveCameras(director), Sizes[0], director.Hud, kind);
                }
                Check(director.PresentationErrorCount == 0, mode + " presentation callbacks raised no exception");
            }
            finally { if (session != null) session.Dispose(); }
        }

        static Camera[] ActiveCameras(DashMatchDirector director) => director.Presentation.Views.GameplayCameras.Where(c => c.enabled).ToArray();
        static void SetPickup(DashMatchDirector director, int seat, DashPickupKind kind)
        {
            director.Hud.ShowConfirmedPickup(new DashArenaEvent(director.Sim.Tick, director.MatchEpoch, seat, seat + 1,
                DashArenaEventKind.Pickup, director.Sim.Players[seat].Position, seat, kind), director.Rules);
            director.Hud.RefreshFromDirector();
        }

        static void Capture(string name, Canvas canvas, Camera[] worldCameras, Vector2Int size, DashMatchHud hud, PlayKind kind)
        {
            if (canvas == null) throw new InvalidOperationException("No authored Canvas: " + name);
            string label = name + "-" + size.x + "x" + size.y;
            var image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            var background = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            RenderTexture oldActive = RenderTexture.active;
            RenderTexture composed = RenderTexture.GetTemporary(size.x, size.y, 24, RenderTextureFormat.ARGB32);
            var cameraObject = new GameObject("Temporary offscreen TMP UI camera");
            GameObject worldBackdrop = null;
            Camera uiCamera = cameraObject.AddComponent<Camera>();
            uiCamera.enabled = false; uiCamera.orthographic = true;
            uiCamera.transform.position = new Vector3(10000f, 10000f, -10000f);
            uiCamera.nearClipPlane = .1f; uiCamera.farClipPlane = 100f;
            uiCamera.cullingMask = 1 << 5; uiCamera.clearFlags = CameraClearFlags.SolidColor;
            uiCamera.backgroundColor = new Color(.95f, .95f, .86f);
            uiCamera.targetTexture = composed; uiCamera.rect = new Rect(0f, 0f, 1f, 1f);
            var layers = canvas.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => t.gameObject.layer);
            RenderMode oldMode = canvas.renderMode; Camera oldCamera = canvas.worldCamera;
            float oldPlane = canvas.planeDistance, oldScale = canvas.scaleFactor;
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            var cameraAspects = worldCameras.ToDictionary(c => c, c => c.aspect);
            try
            {
                foreach (Camera worldCamera in worldCameras) worldCamera.aspect = (float)size.x / worldCameras.Length / size.y;
                if (hud != null)
                {
                    DashMatchDirector director = hud.GetComponentInParent<DashMatchDirector>();
                    director.Presentation.Render(director.Sim, 1f);
                    hud.RefreshFromDirector();
                }
                RenderTexture.active = composed;
                GL.Clear(true, true, new Color(.95f, .95f, .86f));
                if (worldCameras.Length > 0)
                {
                    for (int i = 0; i < worldCameras.Length; i++) RenderWorldPart(worldCameras[i], background, size, i, worldCameras.Length);
                    background.Apply(false, false);
                    // URP Base cameras clear their target even with CameraClearFlags.Depth.
                    // Preserve the actual world render as the first full-canvas draw rather
                    // than blitting into an RT that the following UI render will erase.
                    worldBackdrop = new GameObject("Temporary actual-world RT backdrop", typeof(RectTransform), typeof(RawImage));
                    worldBackdrop.layer = 5;
                    var backdropRect = (RectTransform)worldBackdrop.transform;
                    backdropRect.SetParent(canvas.transform, false); backdropRect.SetAsFirstSibling();
                    backdropRect.anchorMin = Vector2.zero; backdropRect.anchorMax = Vector2.one;
                    backdropRect.offsetMin = backdropRect.offsetMax = Vector2.zero;
                    RawImage backdrop = worldBackdrop.GetComponent<RawImage>();
                    backdrop.texture = background; backdrop.color = Color.white; backdrop.raycastTarget = false;
                    var colors = new HashSet<Color32>();
                    for (int y = size.y / 5; y < size.y * 4 / 5; y += 29)
                        for (int x = size.x / 10; x < size.x * 9 / 10; x += 29) colors.Add(background.GetPixel(x, y));
                    Check(colors.Count > 12, label + " world RT contains scene detail, not a flat clear color (sample colors=" + colors.Count + ")");
                }
                foreach (Transform item in layers.Keys) item.gameObject.layer = 5;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = uiCamera; canvas.planeDistance = 1f;
                Canvas.ForceUpdateCanvases();
                if (scaler != null) scaler.SendMessage("Handle", SendMessageOptions.DontRequireReceiver);
                Canvas.ForceUpdateCanvases();
                foreach (TMP_Text text in canvas.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
                Canvas.ForceUpdateCanvases();
                Check(Mathf.Abs(canvas.pixelRect.width - size.x) < 1f && Mathf.Abs(canvas.pixelRect.height - size.y) < 1f,
                    label + " Canvas uses actual target pixel dimensions (" + canvas.pixelRect + ")");
                CheckCanvasScale(canvas, scaler, size, label);
                if (hud != null) CheckHud(hud, uiCamera, size, kind, label);
                ValidateText(canvas, uiCamera, size, label);
                uiCamera.Render(); RenderTexture.active = composed;
                image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0, false); image.Apply(false, false);
                byte[] png = image.EncodeToPNG();
                if (png == null || png.Length < 128) throw new IOException("Empty Unity image: " + label);
                WriteNew(Path.Combine(_output, label + ".png"), png);
                Checks.Add("Captured " + label + ".png (world + actual TMP/UI render)");
            }
            finally
            {
                if (worldBackdrop != null) UnityEngine.Object.DestroyImmediate(worldBackdrop);
                canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldPlane; canvas.scaleFactor = oldScale;
                foreach (var item in layers) if (item.Key != null) item.Key.gameObject.layer = item.Value;
                foreach (var item in cameraAspects) if (item.Key != null) item.Key.aspect = item.Value;
                uiCamera.targetTexture = null; RenderTexture.active = oldActive;
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(background);
                RenderTexture.ReleaseTemporary(composed);
                Canvas.ForceUpdateCanvases();
            }
        }

        static void RenderWorldPart(Camera camera, Texture2D image, Vector2Int size, int index, int count)
        {
            int width = size.x / count;
            RenderTexture target = RenderTexture.GetTemporary(width, size.y, 24, RenderTextureFormat.ARGB32);
            RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
            Rect oldRect = camera.rect; float oldAspect = camera.aspect; int oldMask = camera.cullingMask; bool oldEnabled = camera.enabled;
            try
            {
                camera.enabled = false; camera.targetTexture = target; camera.rect = new Rect(0f, 0f, 1f, 1f);
                camera.aspect = (float)width / size.y; camera.cullingMask &= ~(1 << 5);
                camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0f, 0f, width, size.y), width * index, 0, false);
            }
            finally
            {
                camera.targetTexture = oldTarget; camera.rect = oldRect; camera.aspect = oldAspect;
                camera.cullingMask = oldMask; camera.enabled = oldEnabled;
                RenderTexture.active = oldActive; RenderTexture.ReleaseTemporary(target);
            }
        }

        static void CheckCanvasScale(Canvas canvas, CanvasScaler scaler, Vector2Int size, string label)
        {
            if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return;
            float w = size.x / scaler.referenceResolution.x, h = size.y / scaler.referenceResolution.y;
            float expected = scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand ? Mathf.Min(w, h)
                : scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Shrink ? Mathf.Max(w, h)
                : Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(w, 2f), Mathf.Log(h, 2f), scaler.matchWidthOrHeight));
            Check(Mathf.Abs(canvas.scaleFactor - expected) < .001f, label + " actual CanvasScaler factor " + canvas.scaleFactor + " equals target-size expectation " + expected);
        }

        static void CheckHud(DashMatchHud hud, Camera camera, Vector2Int size, PlayKind kind, string label)
        {
            int localSeat = kind == PlayKind.JoinLan ? 1 : 0;
            for (int seat = 0; seat < 2; seat++)
            {
                DashMatchHud.SeatView view = hud.Seats[seat];
                bool expectedActive = kind == PlayKind.Local || seat == localSeat;
                Check(view.Region.gameObject.activeSelf == expectedActive, label + " seat " + seat + " viewport ownership");
                if (!expectedActive)
                {
                    Check(hud.MessageForSeat(seat).Length == 0, label + " no other-seat pickup announcement retained");
                    continue;
                }
                Rect expected = kind == PlayKind.Local ? new Rect(seat * size.x * .5f, 0, size.x * .5f, size.y) : new Rect(0, 0, size.x, size.y);
                Check(IsInside(ScreenRect(view.Region, camera), expected), label + " seat " + seat + " region inside assigned viewport");
                Check(view.Status.text.StartsWith("P" + (seat + 1) + " ", StringComparison.Ordinal), label + " seat " + seat + " status identifies actual local seat");
                foreach (RectTransform rect in new[] { view.Status.rectTransform, view.Buffs.rectTransform, (RectTransform)view.PickupPanel.transform })
                    if (rect.gameObject.activeInHierarchy) Check(IsInside(ScreenRect(rect, camera), expected), label + " " + seat + "/" + rect.name + " stays inside viewport");
            }
            if (hud.NetworkNoticeVisible)
            {
                Rect at = ScreenRect(hud.NetworkIssueRect, camera);
                Check(at.center.x > size.x * .5f && at.center.y < size.y * .5f && IsInside(at, new Rect(0, 0, size.x, size.y)), label + " network reason remains lower-right and on-screen");
            }
        }

        static void ValidateText(Canvas canvas, Camera camera, Vector2Int size, string label)
        {
            foreach (TMP_Text text in canvas.GetComponentsInChildren<TMP_Text>())
            {
                if (!text.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(text.text)) continue;
                string id = label + " / " + HierarchyPath(text.transform);
                Check(!text.isTextOverflowing, id + " not overflowing");
                Bounds bounds = text.textBounds;
                Rect local = text.rectTransform.rect;
                Check(bounds.min.x >= local.xMin - 1.5f && bounds.max.x <= local.xMax + 1.5f &&
                    bounds.min.y >= local.yMin - 1.5f && bounds.max.y <= local.yMax + 1.5f, id + " visible glyph geometry fits text rect");
                var corners = new[] { new Vector3(bounds.min.x, bounds.min.y), new Vector3(bounds.max.x, bounds.max.y) };
                Vector2 a = camera.WorldToScreenPoint(text.transform.TransformPoint(corners[0]));
                Vector2 b = camera.WorldToScreenPoint(text.transform.TransformPoint(corners[1]));
                Check(IsInside(Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)), new Rect(0, 0, size.x, size.y)), id + " text stays on-screen");
                uint[] missing;
                Check(text.font != null && text.font.HasCharacters(StripTags(text.text), out missing, false, false), id + " glyphs exist in selected font without OS fallback");
            }
        }

        static string StripTags(string value) => System.Text.RegularExpressions.Regex.Replace(value, "<[^>]+>", "").Replace("\n", "").Replace("\r", "").Replace("\t", "");
        static Rect ScreenRect(RectTransform rect, Camera camera)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Vector3[] at = corners.Select(camera.WorldToScreenPoint).ToArray();
            return Rect.MinMaxRect(at.Min(p => p.x), at.Min(p => p.y), at.Max(p => p.x), at.Max(p => p.y));
        }
        static bool IsInside(Rect inner, Rect outer) => inner.xMin >= outer.xMin - 1.5f && inner.xMax <= outer.xMax + 1.5f && inner.yMin >= outer.yMin - 1.5f && inner.yMax <= outer.yMax + 1.5f;
        static string HierarchyPath(Transform transform) => transform.parent == null ? transform.name : HierarchyPath(transform.parent) + "/" + transform.name;
        static T FindActive<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).First(c => c.gameObject.activeInHierarchy);
        static void Check(bool ok, string description) { (ok ? Checks : Failures).Add(description); }
        static string Hash(string file)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
        static string NewOutputDirectory()
        {
            string root = Path.GetFullPath(ResultsRoot), requested = null;
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-dashCaptureOutput");
            if (index >= 0 && index + 1 < args.Length) requested = args[index + 1];
            string output = Path.GetFullPath(requested ?? Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6)));
            if (!output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Capture output must be a new child directory inside " + root);
            if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Refusing to overwrite capture results: " + output);
            Directory.CreateDirectory(output); return output;
        }
        static void WriteReport()
        {
            var report = new StringBuilder("# Off-screen production presentation verification\n\n");
            report.AppendLine("Evidence kind: **explicit presentation fixture**, not Player/GameView, natural gameplay, packaged performance, or LAN-network acceptance.");
            report.AppendLine("Source: saved MainMenu/Arena production scenes, opened without saving. World cameras render to per-viewport RTs. Their joined, unedited world texture is displayed in a temporary full-canvas RawImage behind the real authored Canvas/TMP, temporarily ScreenSpaceCamera, using its actual CanvasScaler. This avoids URP Base-camera clears erasing a pre-blitted world. All temporary changes are discarded.");
            report.AppendLine("LAN host/join fixtures use real session seat assignment with ephemeral local sockets, but no Pump/Advance and no traffic. Buffs, stun, positions, and labelled input-wait/INPUT PROGRESS TIMEOUT/DESYNC panels are display fixtures, not claimed observed gameplay/network events.");
            report.AppendLine("Resolutions: 1280x720, 1920x1080, 1024x768. Same scene/HUD hierarchy is recaptured at each target size. Fullscreen/window OS transitions and frame-time performance require separate Player evidence.");
            report.AppendLine("Unity: " + Application.unityVersion + "; GPU: " + SystemInfo.graphicsDeviceName + ".");
            report.AppendLine("\n## Failures (" + Failures.Count + ")\n");
            foreach (string failure in Failures) report.AppendLine("- " + failure);
            report.AppendLine("\n## Passed checks / captures (" + Checks.Count + ")\n");
            foreach (string check in Checks) report.AppendLine("- " + check);
            WriteNew(Path.Combine(_output, "capture-info.md"), Encoding.UTF8.GetBytes(report.ToString()));
        }
        static void WriteNew(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes, 0, bytes.Length);
        }
    }
}
