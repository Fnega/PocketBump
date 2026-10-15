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
    // Explicitly staged presentation evidence. Never advances a simulation, pumps
    // a peer, changes saved scene bytes, or claims these fixtures are network tests.
    public static class DashMinimapCapture
    {
        const string OutputRoot = "Tools/MinimapResults";
        static readonly Vector2Int[] Sizes = { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(1024, 768) };
        static readonly List<string> Checks = new List<string>(), Failures = new List<string>();
        static readonly List<string> Images = new List<string>();
        static string _output;

        public static void CaptureFromCommandLine()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Use an edit-mode Unity batch without -nographics, with -minimapCaptureOutput <new directory in Tools/MinimapResults>.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Refusing to discard an unsaved scene during presentation capture.");
            _output = NewOutputDirectory(); Checks.Clear(); Failures.Clear(); Images.Clear();
            var paths = new[] { DashProductionSceneBaker.ArenaPath, DashProductionSceneBaker.ArenaPath + ".meta",
                DashProductionSceneBaker.MainMenuPath, DashProductionSceneBaker.MainMenuPath + ".meta", "ProjectSettings/EditorBuildSettings.asset" };
            var originals = paths.ToDictionary(p => p, Hash);
            SceneSetup[] originalScenes = EditorSceneManager.GetSceneManagerSetup();
            float fixedDelta = Time.fixedDeltaTime;
            try
            {
                foreach (PlayKind kind in new[] { PlayKind.Local, PlayKind.HostLan, PlayKind.JoinLan }) CaptureMode(kind);
            }
            catch (Exception error) { Failures.Add(error.ToString()); }
            finally
            {
                Time.fixedDeltaTime = fixedDelta;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (originalScenes.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(originalScenes);
                foreach (var original in originals) Check(Hash(original.Key) == original.Value, "Saved file unchanged: " + original.Key);
                var report = new CaptureReport
                {
                    passed = Failures.Count == 0, evidence_kind = "staged_offscreen_Unity_presentation_fixture",
                    source_scene = DashProductionSceneBaker.ArenaPath,
                    limitation = "Not GameView, actual Player, natural gameplay, real network fault, or performance evidence. LAN assignment uses an unpumped ephemeral socket; no network traffic. Raw world+HUD PNGs require manual review.",
                    unity = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName,
                    images = Images.ToArray(), checks = Checks.ToArray(), failures = Failures.ToArray()
                };
                WriteNew(Path.Combine(_output, "capture-report.json"), Encoding.UTF8.GetBytes(JsonUtility.ToJson(report, true)));
            }
            if (Failures.Count > 0) throw new InvalidOperationException("Minimap presentation fixture checks failed: " + Path.Combine(_output, "capture-report.json"));
            Debug.Log("Minimap presentation fixtures passed automated checks; inspect raw PNGs: " + _output);
        }

        static void CaptureMode(PlayKind kind)
        {
            Scene scene = EditorSceneManager.OpenScene(DashProductionSceneBaker.ArenaPath, OpenSceneMode.Single);
            DashProductionSceneBaker.ValidateArena(scene);
            DashMatchDirector director = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<DashMatchDirector>(true))
                .Single(d => d.gameObject.activeInHierarchy && d.enabled);
            director.AutomaticTicking = false;
            if (director.Hud.Seats.Any(s => s.Minimap == null))
                throw new InvalidOperationException("Saved Arena has no authored minimap; run explicit incremental integration first. Capture never installs UI.");
            ArenaRulesProfile rules = ArenaRulesProfile.CreateProductionV1(ballCount: 8);
            DashLanSession session = null;
            try
            {
                if (kind == PlayKind.Local) director.InitializeLocal(rules, 314159u);
                else
                {
                    session = kind == PlayKind.HostLan ? DashLanSession.Host(rules, 719, 314159u, () => 0f)
                        : DashLanSession.Join(rules, new IPEndPoint(IPAddress.Loopback, 9), () => 0f, 719UL);
                    if (!session.SocketBound) throw new InvalidOperationException("Could not create the unpumped seat-assignment fixture: " + session.LastSocketError);
                    director.InitializeLan(session);
                }
                string mode = kind == PlayKind.Local ? "local-split" : kind == PlayKind.HostLan ? "lan-seat0" : "lan-seat1";
                foreach (int end in new[] { 1, -1 })
                {
                    foreach (float distance in new[] { 8f, .25f })
                    {
                        SetFixture(director, end, distance);
                        foreach (Vector2Int size in Sizes)
                            Capture(director, kind, end, size, mode + "-" + (end > 0 ? "north" : "south") + "-distance-" +
                                (distance > 1f ? "8" : "0_25") + "-display-fixture");
                    }
                    if (session == null) continue;
                    SetFixture(director, end, 4f);
                    FieldInfo waiting = typeof(DashLanSession).GetField("_inputWaitSince", BindingFlags.Instance | BindingFlags.NonPublic);
                    FieldInfo failure = typeof(DashMatchDirector).GetField("_failure", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (waiting == null || failure == null) throw new InvalidOperationException("Explicit network-display fixture binding changed.");
                    waiting.SetValue(session, 0f);
                    foreach (Vector2Int size in Sizes)
                        Capture(director, kind, end, size, mode + "-" + (end > 0 ? "north" : "south") + "-network-wait-display-fixture");
                    waiting.SetValue(session, -1f);
                    failure.SetValue(director, "INPUT PROGRESS TIMEOUT");
                    foreach (Vector2Int size in Sizes)
                        Capture(director, kind, end, size, mode + "-" + (end > 0 ? "north" : "south") + "-network-fault-display-fixture");
                    failure.SetValue(director, "");
                }
                Check(session == null || session.SendAttempts == 0 && session.ReceivedDatagrams == 0, mode + " fixture generated zero network traffic");
                Check(director.PresentationErrorCount == 0, mode + " presentation callbacks have zero errors");
            }
            finally { session?.Dispose(); }
        }

        static void SetFixture(DashMatchDirector director, int end, float distance)
        {
            DashArenaState state = director.Sim;
            state.Tick = 240; state.Phase = MatchState.Live; state.GateClosed = false;
            for (int seat = 0; seat < 2; seat++)
            {
                state.Players[seat].Alive = true;
                state.Players[seat].Position = new Vector2(seat == 0 ? -1f : 1f, end * (state.Rules.HalfHeight - distance));
                state.Players[seat].Facing = seat == 0 ? Vector2.up : Vector2.right;
                state.Players[seat].GreenTicks = seat == 0 ? 240 : 0;
                state.Players[seat].PurpleTicks = seat == 1 ? 240 : 0;
                state.Players[seat].DashCooldownTicks = 95;
            }
            for (int i = 0; i < state.Balls.Length; i++)
            {
                state.Balls[i].Active = true;
                state.Balls[i].Position = i == 0 ? new Vector2(0f, end * (state.Rules.HalfHeight - 2f))
                    : new Vector2(-13f + i * 3.5f, i % 2 == 0 ? 11f : -11f);
            }
            for (int i = 0; i < state.Walls.Length; i++)
                state.Walls[i] = new DashWallState { Broken = i < 2, RepairPending = i == 1, RepairTicks = i == 0 ? 200 : 0 };
            director.Presentation.ReconcileState(state);
            director.Hud.ClearPickupMessages();
        }

        static void Capture(DashMatchDirector director, PlayKind kind, int end, Vector2Int size, string name)
        {
            string label = name + "-" + size.x + "x" + size.y;
            DashMatchHud hud = director.Hud;
            Canvas canvas = hud.GetComponent<Canvas>();
            Camera[] cameras = director.Presentation.Views.GameplayCameras.Where(c => c.enabled).ToArray();
            int before = director.Sim.Checksum();
            var image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            var background = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(size.x, size.y, 24, RenderTextureFormat.ARGB32);
            var uiObject = new GameObject("TEMP minimap offscreen UI camera");
            Camera ui = uiObject.AddComponent<Camera>(); ui.enabled = false; ui.orthographic = true;
            ui.transform.position = new Vector3(10000f, 10000f, -10000f);
            ui.nearClipPlane = .1f; ui.farClipPlane = 100f; ui.cullingMask = 1 << 5;
            ui.clearFlags = CameraClearFlags.SolidColor; ui.backgroundColor = new Color(.95f, .95f, .86f);
            ui.targetTexture = target;
            var layers = canvas.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => t.gameObject.layer);
            var aspects = cameras.ToDictionary(c => c, c => c.aspect);
            var cameraTargets = cameras.ToDictionary(c => c, c => c.targetTexture);
            RenderMode oldMode = canvas.renderMode; Camera oldCamera = canvas.worldCamera;
            float oldPlane = canvas.planeDistance, oldScale = canvas.scaleFactor;
            GameObject backdrop = null;
            try
            {
                foreach (var item in layers) item.Key.gameObject.layer = 5;
                foreach (Camera camera in cameras)
                {
                    // The full-size target retains each camera's real split rect, so
                    // camera.pixelRect belongs to the correct output-size viewport.
                    camera.targetTexture = target;
                    camera.aspect = (float)size.x / cameras.Length / size.y;
                }
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = ui; canvas.planeDistance = 1f;
                Canvas.ForceUpdateCanvases();
                CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null) scaler.SendMessage("Handle", SendMessageOptions.DontRequireReceiver);
                Canvas.ForceUpdateCanvases();
                // Force the target-size Canvas first: goal-safe calculations must not
                // use the batch host window's dimensions or the previous capture size.
                for (int seat = 0; seat < 2; seat++)
                    hud.ShowConfirmedPickup(new DashArenaEvent(director.Sim.Tick, director.Sim.Epoch, seat, seat + 1,
                        DashArenaEventKind.Pickup, director.Sim.Players[seat].Position, seat,
                        seat == 0 ? DashPickupKind.Green : DashPickupKind.Purple), director.Rules);
                hud.RefreshFromDirector(); Canvas.ForceUpdateCanvases();
                director.Presentation.Render(director.Sim, 1f); hud.RefreshFromDirector();
                Canvas.ForceUpdateCanvases();
                foreach (TMP_Text text in canvas.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
                Canvas.ForceUpdateCanvases();
                Check(Mathf.Abs(canvas.pixelRect.width - size.x) < 1f && Mathf.Abs(canvas.pixelRect.height - size.y) < 1f,
                    label + " Canvas uses requested actual pixel dimensions");
                Check(cameras.Length == (kind == PlayKind.Local ? 2 : 1), label + " existing gameplay camera count unchanged");
                VerifyOverlay(director, ui, kind, end, size, label);
                VerifyText(canvas, label);
                for (int i = 0; i < cameras.Length; i++) RenderWorldPart(cameras[i], background, size, i, cameras.Length);
                background.Apply(false, false);
                var colors = new HashSet<Color32>();
                // Near the south goal the intentionally HUD-safe subjects sit near
                // the bottom edge. Sample the whole uncomposited world, including
                // edges, rather than misclassifying its empty central floor as flat.
                int columns = Mathf.Min(128, size.x), rows = Mathf.Min(128, size.y), nonBlack = 0;
                for (int row = 0; row < rows; row++)
                    for (int column = 0; column < columns; column++)
                    {
                        int x = column * (size.x - 1) / (columns - 1), y = row * (size.y - 1) / (rows - 1);
                        Color32 pixel = background.GetPixel(x, y); colors.Add(pixel);
                        if (Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)) > 8) nonBlack++;
                    }
                Check(colors.Count > 12 && nonBlack > columns * rows * .01f,
                    label + " actual full-world RT is detailed, not a flat/black background (colors=" + colors.Count + ")");
                // Same URP-safe composition as DashProductionCapture: UI Base-camera
                // clears cannot erase the world because it is the first real UI draw.
                backdrop = new GameObject("TEMP unedited actual-world backdrop", typeof(RectTransform), typeof(RawImage));
                backdrop.layer = 5;
                RectTransform rect = (RectTransform)backdrop.transform;
                rect.SetParent(canvas.transform, false); rect.SetAsFirstSibling();
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                RawImage raw = backdrop.GetComponent<RawImage>(); raw.texture = background; raw.color = Color.white; raw.raycastTarget = false;
                Canvas.ForceUpdateCanvases();
                ui.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0, false); image.Apply(false, false);
                string path = Path.Combine(_output, label + ".png"); WriteNew(path, image.EncodeToPNG()); Images.Add(path);
                Check(director.Sim.Checksum() == before, label + " map/camera/HUD capture does not mutate simulation checksum");
            }
            finally
            {
                if (backdrop != null) UnityEngine.Object.DestroyImmediate(backdrop);
                canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldPlane; canvas.scaleFactor = oldScale;
                foreach (var item in layers) if (item.Key != null) item.Key.gameObject.layer = item.Value;
                foreach (var item in aspects) if (item.Key != null) item.Key.aspect = item.Value;
                foreach (var item in cameraTargets) if (item.Key != null) item.Key.targetTexture = item.Value;
                ui.targetTexture = null; RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(uiObject); UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(background);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        static void VerifyOverlay(DashMatchDirector director, Camera ui, PlayKind kind, int end, Vector2Int size, string label)
        {
            DashMatchHud hud = director.Hud; DashArenaState state = director.Sim;
            Rect screen = new Rect(0, 0, size.x, size.y);
            for (int seat = 0; seat < 2; seat++)
            {
                var view = hud.Seats[seat]; bool active = kind == PlayKind.Local || seat == director.LocalSeat;
                Check(view.Region.gameObject.activeSelf == active, label + " assigned region seat " + seat);
                if (!active) continue;
                var map = view.Minimap;
                Rect viewport = kind == PlayKind.Local ? new Rect(seat * size.x * .5f, 0, size.x * .5f, size.y) : screen;
                Rect mapPixels = ScreenRect(map.rectTransform, ui);
                Check(map.gameObject.activeInHierarchy && Inside(mapPixels, viewport) &&
                    mapPixels.center.x > viewport.center.x && mapPixels.center.y > viewport.center.y, label + " seat " + seat + " map stays upper-right in own viewport");
                Check(ReferenceEquals(map.State, state) && map.LocalSeat == seat && map.ActivePlayerCount == 2 && map.ActiveBallCount == 8,
                    label + " seat " + seat + " map represents both players, eight active balls, correct local identity");
                Check(map.SolidWallCount == state.Walls.Length - 2 && map.PendingWallCount == 1 && map.BrokenWallCount == 1,
                    label + " map distinguishes intact, broken, and repair-pending walls");
                Check(map.WorldToMap(Vector2.up).y > map.WorldToMap(Vector2.zero).y &&
                    map.WorldToMap(Vector2.right).x > map.WorldToMap(Vector2.zero).x, label + " map remains north-up");
                Check(Mathf.Abs(Vector2.Distance(map.WorldToMap(Vector2.right), map.WorldToMap(Vector2.zero)) -
                    Vector2.Distance(map.WorldToMap(Vector2.up), map.WorldToMap(Vector2.zero))) < .001f, label + " map uses uniform world scale");
                Rect status = ScreenRect((RectTransform)view.Status.transform.parent, ui), pickup = ScreenRect((RectTransform)view.PickupPanel.transform, ui);
                Check(Inside(status, viewport) && !Overlaps(status, mapPixels), label + " status leaves map space");
                Check(Inside(pickup, viewport) && !Overlaps(pickup, mapPixels), label + " visible pickup explanation leaves map space");
                Camera world = director.Presentation.Views.GameplayCameras[kind == PlayKind.Local ? seat : 0];
                Bounds goal = ActualGoalBounds(director.transform, end);
                Rect normalized = Project(world, goal, out bool inFront);
                Rect safe = director.Presentation.GoalSafeViewport(world);
                Check(inFront && Inside(normalized, safe, .001f), label + " seat " + seat + " full actual goal frame+mouth bounds inside HUD-safe projection: " + normalized + " safe=" + safe);
                Rect goalPixels = new Rect(viewport.x + normalized.x * viewport.width, normalized.y * viewport.height,
                    normalized.width * viewport.width, normalized.height * viewport.height);
                Check(!Overlaps(goalPixels, status) && !Overlaps(goalPixels, pickup) && !Overlaps(goalPixels, mapPixels), label + " full goal not occluded by status, pickup, or minimap");
                foreach (string name in new[] { "Score", "Banner" })
                {
                    Transform shared = hud.transform.Find(name);
                    if (shared != null) Check(!Overlaps(goalPixels, ScreenRect((RectTransform)shared, ui)), label + " full goal avoids shared " + name);
                }
                Vector3 robot = world.WorldToViewportPoint(director.Presentation.Views.Players[seat].Root.position + Vector3.up);
                Check(robot.z > 0 && robot.x > 0 && robot.x < 1 && robot.y > 0 && robot.y < 1, label + " followed robot remains visible");
                if (hud.NetworkNoticeVisible)
                {
                    Rect network = ScreenRect(hud.NetworkIssueRect, ui);
                    Check(Inside(network, screen) && network.center.x > screen.center.x && network.center.y < screen.center.y &&
                        !Overlaps(network, mapPixels), label + " active network panel stays bottom-right separately from minimap");
                    Check(!Overlaps(network, goalPixels), label + " nearby goal not hidden by bottom-right network panel");
                    Transform banner = hud.transform.Find("Banner");
                    if (banner != null) Check(!Overlaps(network, ScreenRect((RectTransform)banner, ui)), label + " network panel avoids bottom banner");
                }
            }
            if (label.Contains("network-wait")) Check(director.WaitingForRemoteInput && !director.NetworkPaused &&
                !hud.NetworkNoticeVisible && !hud.NetworkWaitingVisible && !hud.NetworkIssueVisible,
                label + " preserves recoverable input waiting without showing a network panel");
            if (label.Contains("network-fault")) Check(hud.NetworkIssueVisible && hud.NetworkIssueReason == "INPUT PROGRESS TIMEOUT", label + " explicitly labelled terminal fault display");
        }

        static Bounds ActualGoalBounds(Transform root, int end)
        {
            Renderer[] parts = root.GetComponentsInChildren<Renderer>(true).Where(r =>
                r.name == "Goal mouth " + end || r.name == "Goal lintel " + end || r.name.StartsWith("Goal post " + end + " ", StringComparison.Ordinal)).ToArray();
            if (parts.Length != 4) throw new InvalidOperationException("Expected one saved goal mouth, two posts, and lintel for end " + end + "; cannot substitute a point or fabricated bound.");
            Bounds bounds = parts[0].bounds; foreach (Renderer part in parts.Skip(1)) bounds.Encapsulate(part.bounds); return bounds;
        }
        static Rect Project(Camera camera, Bounds bounds, out bool inFront)
        {
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            inFront = true;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 projected = camera.WorldToViewportPoint(corner); inFront &= projected.z > 0;
                min = Vector2.Min(min, projected); max = Vector2.Max(max, projected);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        static void VerifyText(Canvas canvas, string label)
        {
            foreach (TMP_Text text in canvas.GetComponentsInChildren<TMP_Text>())
            {
                if (!text.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(text.text)) continue;
                string visible = System.Text.RegularExpressions.Regex.Replace(text.text, "<[^>]+>", "").Replace("\n", "").Replace("\r", "");
                Check(!text.isTextOverflowing, label + " text fits: " + text.name);
                Check(text.font != null && text.font.HasCharacters(visible, out uint[] _, false, false), label + " font covers: " + text.name);
            }
        }
        static void RenderWorldPart(Camera camera, Texture2D image, Vector2Int size, int index, int count)
        {
            int width = size.x / count; RenderTexture target = RenderTexture.GetTemporary(width, size.y, 24, RenderTextureFormat.ARGB32);
            RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
            Rect rect = camera.rect; float aspect = camera.aspect; int mask = camera.cullingMask; bool enabled = camera.enabled;
            try
            {
                camera.enabled = false; camera.targetTexture = target; camera.rect = new Rect(0, 0, 1, 1);
                camera.aspect = (float)width / size.y; camera.cullingMask &= ~(1 << 5);
                camera.Render(); RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, width, size.y), width * index, 0, false);
            }
            finally
            {
                camera.targetTexture = oldTarget; camera.rect = rect; camera.aspect = aspect; camera.cullingMask = mask; camera.enabled = enabled;
                RenderTexture.active = oldActive; RenderTexture.ReleaseTemporary(target);
            }
        }
        static Rect ScreenRect(RectTransform rect, Camera camera)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners); Vector3[] at = corners.Select(camera.WorldToScreenPoint).ToArray();
            return Rect.MinMaxRect(at.Min(p => p.x), at.Min(p => p.y), at.Max(p => p.x), at.Max(p => p.y));
        }
        static bool Inside(Rect inner, Rect outer, float tolerance = 1.5f) => inner.xMin >= outer.xMin - tolerance &&
            inner.xMax <= outer.xMax + tolerance && inner.yMin >= outer.yMin - tolerance && inner.yMax <= outer.yMax + tolerance;
        static bool Overlaps(Rect a, Rect b) => Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > 1.5f &&
            Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > 1.5f;
        static void Check(bool ok, string description) { (ok ? Checks : Failures).Add(description); }
        static string Hash(string path)
        {
            using (var sha = SHA256.Create()) using (var file = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
        }
        static string NewOutputDirectory()
        {
            string[] args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-minimapCaptureOutput");
            if (at < 0 || at + 1 >= args.Length) throw new ArgumentException("Specify a new -minimapCaptureOutput child directory explicitly.");
            string root = Path.GetFullPath(OutputRoot), output = Path.GetFullPath(args[at + 1]);
            if (!output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output) || File.Exists(output))
                throw new IOException("Output must be a new child of " + root + "; existing evidence is never overwritten.");
            Directory.CreateDirectory(output); return output;
        }
        static void WriteNew(string path, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) throw new IOException("Empty capture artifact: " + path);
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) file.Write(bytes, 0, bytes.Length);
        }
        [Serializable] sealed class CaptureReport
        {
            public bool passed; public string evidence_kind, source_scene, limitation, unity, gpu;
            public string[] images, checks, failures;
        }
    }
}
