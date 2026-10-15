#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PocketBump
{
    // Opt-in observer for the actual rendered Player. Never a gameplay-state fixture.
    // The LAN runner remains the sole driver/session owner; only local mode supplies
    // ordinary movement/Dash intent through the existing input-override seam.
    public sealed class DashPlayerPresentationProbe : MonoBehaviour
    {
        [Serializable] sealed class Report
        {
            public string evidence_kind = "actual development Player, ordinary-input run";
            public bool complete, passed, editor_run, display_cycle_requested;
            public string message, role, utc_started, utc_finished, unity_version, scene, rules_hash, layout_hash;
            public string cpu, gpu, graphics_api, operating_system, driver, output_directory;
            public string limitation = "Frame intervals include scheduling/vsync and any temporary input-pacing waits (reported separately). Local split is one process; LAN single-screen peers can compete for CPU/GPU. This is not an isolated camera-cost benchmark, GPU timing, real-Wi-Fi acceptance, or proof of effects that were not observed.";
            public int process_id, cpu_cores, system_memory_mb, gpu_memory_mb, requested_frames, warmup_frames, sampled_frames;
            public int width, height, target_frame_rate, vsync_count, match_id, epoch, local_seat, tick_start, tick_end;
            public int pacing_wait_count_delta;
            public float pacing_wait_seconds_delta;
            public string full_screen_mode, quality;
            public bool focused_start, focused_end, run_in_background, Chinese_font_has_all_required_glyphs;
            public float realtime_start, realtime_end, p50_frame_ms, p95_frame_ms, max_frame_ms;
            public int warnings, errors, presentation_errors, active_camera_count;
            public Rect[] camera_rects;
            public int pulse_capacity, wall_debris_capacity, pickup_capacity, player_trail_limit = 18, ball_trail_limit = 16;
            public int peak_pulses, peak_wall_debris, peak_pickups, peak_player_trail_points, peak_ball_trail_points, peak_personal_popups;
            public bool effect_capacity_bounds_held = true;
            public string[] observed_confirmed_events, retained_logs, screenshots, display_observations;
            public string local_route;
            public int active_minimap_count;
            public int[] minimap_local_seats, minimap_player_counts, minimap_ball_counts;
            public Rect[] minimap_screen_rects;
            public Vector2[] final_player_positions;
            public bool minimap_layout_valid;
        }
        readonly List<float> _frames = new List<float>(600);
        readonly List<string> _logs = new List<string>(), _screenshots = new List<string>(), _displayObservations = new List<string>();
        readonly HashSet<DashArenaEventKind> _observedEvents = new HashSet<DashArenaEventKind>();
        readonly Report _report = new Report();
        DashMatchDirector _director;
        string _mode, _output, _display, _localRoute;
        int _width, _height, _frameGoal = 600, _warmupGoal = 180;
        bool _createdOutput, _written, _finishing, _quitOnComplete, _samplingStarted;
        float _deadline;
        int _waitCountAtSampleStart;
        float _waitSecondsAtSampleStart;
        static readonly Vector2[] Directions = { Vector2.right, Vector2.up, Vector2.left, Vector2.down };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            string mode = Argument("--dash-probe=");
            if (mode.Length == 0) return;
            var go = new GameObject("Dash Player Presentation Probe");
            DontDestroyOnLoad(go);
            var probe = go.AddComponent<DashPlayerPresentationProbe>();
            probe._mode = mode;
        }

        IEnumerator Start()
        {
            Exception configurationError = null;
            try { Configure(); }
            catch (Exception error) { configurationError = error; }
            if (configurationError != null)
            {
                Debug.LogError("Dash Player probe configuration rejected: " + configurationError.Message);
                enabled = false; yield break;
            }
            Application.logMessageReceived += ObserveLog;
            yield return null;
            if (_mode == "local") GameSession.Instance.BeginMatch(8, 1);
            while (_director == null || !_director.Initialized || (_mode == "observe" && !_director.ReadyForNetplay))
            {
                if (_director == null) _director = FindObjectOfType<DashMatchDirector>();
                if (_director != null && _director.NetworkPaused) { yield return Finish(false, "Network paused before sampling: " + _director.FailureReason); yield break; }
                if (Time.realtimeSinceStartup > _deadline) { yield return Finish(false, "Timed out waiting for a production match."); yield break; }
                yield return null;
            }
            if ((_mode == "local") != (_director.Kind == PlayKind.Local))
            { yield return Finish(false, "Requested probe mode does not match the actual session."); yield break; }
            if (_mode == "local")
            {
                if (_director.InputOverride != null) { yield return Finish(false, "Refusing to replace another local driver."); yield break; }
                _director.InputOverride = LocalInput;
            }
            _director.ConfirmedEvents += ObserveEvents;
            _report.role = _director.Kind.ToString(); _report.local_seat = _director.LocalSeat;
            _report.match_id = _director.MatchId; _report.epoch = _director.MatchEpoch;
            _report.rules_hash = _director.Rules.ContentHash; _report.layout_hash = _director.Rules.LayoutHash;
            _report.scene = SceneManager.GetActiveScene().path;
            _report.driver = _mode == "local" ? (_localRoute == "goals" ? "ordinary-movement-to-own-goal-approaches; no authoritative writes" :
                "ordinary-square-movement-and-edge-Dash; no authoritative writes") : "existing LAN smoke driver; not replaced by probe";
            DashArenaArtSample.SceneReferences views = _director.Presentation?.Views;
            if (views == null || _director.Hud == null) { yield return Finish(false, "Production authored presentation/HUD missing."); yield break; }
            _report.pulse_capacity = views.Pulses.Length; _report.wall_debris_capacity = views.Walls.Length; _report.pickup_capacity = views.Pickups.Length;
            TMP_FontAsset font = _director.Hud.Seats[0].Status.font;
            _report.Chinese_font_has_all_required_glyphs = font != null && font.HasCharacters(DashMatchHud.RequiredChineseGlyphs, out uint[] missing, false, false);
            ApplyTemporaryDisplay(_display);
            for (int i = 0; i < 12; i++) yield return null;
            ObserveDisplay("requested-mode");
            if (_report.display_cycle_requested)
            {
                string alternate = _display == "windowed" ? "fullscreen" : "windowed";
                ApplyTemporaryDisplay(alternate);
                for (int i = 0; i < 20; i++) yield return null;
                ObserveDisplay("temporary-" + alternate);
                yield return CaptureActualFrame("display-cycle-" + alternate);
                ApplyTemporaryDisplay(_display);
                for (int i = 0; i < 20; i++) yield return null;
                ObserveDisplay("restored-" + _display);
            }
            int warmed = 0;
            while (warmed < _warmupGoal)
            {
                string issue = SessionIssue();
                if (issue.Length > 0) { yield return Finish(false, issue); yield break; }
                if (_director.State == MatchState.Live && (_mode != "local" || _localRoute != "goals" || GoalApproachesReached(_director.Sim))) warmed++;
                yield return null;
            }
            _report.realtime_start = Time.realtimeSinceStartup;
            _samplingStarted = true;
            _report.tick_start = _director.Sim.Tick; _report.focused_start = Application.isFocused;
            _waitCountAtSampleStart = _director.LanSession?.InputWaitCount ?? 0;
            _waitSecondsAtSampleStart = _director.LanSession?.InputWaitSeconds ?? 0f;
            _report.width = Screen.width; _report.height = Screen.height;
            _report.full_screen_mode = Screen.fullScreenMode.ToString();
            _report.target_frame_rate = Application.targetFrameRate; _report.vsync_count = QualitySettings.vSyncCount;
            _report.run_in_background = Application.runInBackground;
            _report.quality = QualitySettings.names[QualitySettings.GetQualityLevel()];
            while (_frames.Count < _frameGoal)
            {
                string issue = SessionIssue();
                if (issue.Length > 0) { yield return Finish(false, issue); yield break; }
                if (Screen.width != _report.width || Screen.height != _report.height || Screen.fullScreenMode.ToString() != _report.full_screen_mode)
                { yield return Finish(false, "Display changed during the measured interval."); yield break; }
                if (_director.State != MatchState.Live)
                { yield return Finish(false, "Measured Live interval was interrupted by phase " + _director.State + "; no mixed-phase performance claim."); yield break; }
                _frames.Add(Time.unscaledDeltaTime * 1000f);
                ObserveEffects(views);
                yield return null;
            }
            _report.realtime_end = Time.realtimeSinceStartup;
            _report.tick_end = _director.Sim.Tick; _report.focused_end = Application.isFocused;
            RecordInputPacing();
            _report.active_camera_count = views.GameplayCameras.Count(c => c.enabled && c.gameObject.activeInHierarchy);
            _report.camera_rects = views.GameplayCameras.Where(c => c.enabled && c.gameObject.activeInHierarchy).Select(c => c.rect).ToArray();
            _report.presentation_errors = _director.PresentationErrorCount + (_director.LanSession?.PresentationErrorCount ?? 0);
            ObserveMinimaps();
            ObserveDisplay("after-measured-interval");
            bool passed = _report.errors == 0 && _report.presentation_errors == 0 && _report.effect_capacity_bounds_held && _report.Chinese_font_has_all_required_glyphs && _report.tick_end > _report.tick_start && _report.minimap_layout_valid;
            yield return Finish(passed, passed ? "Requested actual-Player frame interval completed; inspect screenshots and scope limitations." : "Interval completed with log, glyph, presentation, or effect-bound failures.");
        }

        void Configure()
        {
            if (_mode != "local" && _mode != "observe") throw new ArgumentException("--dash-probe must be local or observe.");
            if (_mode == "observe" && Argument("--lan-smoke=") != "host" && Argument("--lan-smoke=") != "join")
                throw new ArgumentException("observe requires --lan-smoke=host or join.");
            if (_mode == "local" && Argument("--lan-smoke=").Length > 0) throw new ArgumentException("local cannot also start a LAN runner.");
            _localRoute = ValueOr("--dash-probe-local-route=", "square");
            if (_localRoute != "square" && _localRoute != "goals") throw new ArgumentException("Local probe route must be square or goals.");
            if (_mode != "local" && _localRoute != "square") throw new ArgumentException("The goals route is local only; LAN transport keeps its existing input driver.");
            _report.local_route = _mode == "local" ? _localRoute : "not-applicable";
            string output = Argument("--dash-probe-output=");
            if (!Path.IsPathRooted(output)) throw new ArgumentException("--dash-probe-output must specify an absolute NEW directory.");
            _output = Path.GetFullPath(output);
            if (Directory.Exists(_output) || File.Exists(_output)) throw new IOException("Refusing to overwrite probe directory: " + _output);
            _display = ValueOr("--dash-probe-display=", "windowed");
            if (_display != "windowed" && _display != "fullscreen") throw new ArgumentException("Unknown probe display mode.");
            string[] dimensions = ValueOr("--dash-probe-size=", "1280x720").Split('x');
            if (dimensions.Length != 2 || !int.TryParse(dimensions[0], out _width) || !int.TryParse(dimensions[1], out _height) || _width < 640 || _height < 480 || _width > 3840 || _height > 2160)
                throw new ArgumentException("Probe size must be WIDTHxHEIGHT between 640x480 and 3840x2160.");
            _frameGoal = ReadInt("--dash-probe-frames=", 600, 60, 18000);
            _warmupGoal = ReadInt("--dash-probe-warmup=", 180, 0, 3600);
            _report.display_cycle_requested = Has("--dash-probe-cycle-display");
            _quitOnComplete = Has("--dash-probe-quit-on-complete");
            if (_quitOnComplete && _mode != "local") throw new ArgumentException("Only standalone local probes may request self-exit; the LAN launcher owns peer lifetimes.");
            _deadline = Time.realtimeSinceStartup + 240f;
            Directory.CreateDirectory(_output); _createdOutput = true;
            _report.output_directory = _output; _report.utc_started = DateTime.UtcNow.ToString("o");
            _report.editor_run = Application.isEditor; _report.unity_version = Application.unityVersion;
            _report.process_id = System.Diagnostics.Process.GetCurrentProcess().Id;
            _report.cpu = SystemInfo.processorType; _report.cpu_cores = SystemInfo.processorCount;
            _report.gpu = SystemInfo.graphicsDeviceName; _report.graphics_api = SystemInfo.graphicsDeviceVersion;
            _report.system_memory_mb = SystemInfo.systemMemorySize; _report.gpu_memory_mb = SystemInfo.graphicsMemorySize;
            _report.operating_system = SystemInfo.operatingSystem; _report.requested_frames = _frameGoal; _report.warmup_frames = _warmupGoal;
        }

        void ApplyTemporaryDisplay(string display)
        {
            // No DisplayModeSettings.Apply, Save, or PlayerPrefs writes in this probe.
            Screen.SetResolution(_width, _height, display == "windowed" ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow);
        }
        string SessionIssue()
        {
            if (Time.realtimeSinceStartup > _deadline) return "Probe deadline reached before requested samples completed.";
            if (_director == null || _director.Sim == null) return "Production director disappeared.";
            if (_director.NetworkPaused) return "Network paused: " + _director.FailureReason;
            if (_director.MatchEpoch != _report.epoch || _director.MatchId != _report.match_id || _director.LocalSeat != _report.local_seat || _director.Rules.ContentHash != _report.rules_hash)
                return "Session/rules/seat changed during display setup or sampling.";
            return "";
        }
        void ObserveDisplay(string stage)
        {
            string viewports = _director?.Presentation?.Views == null ? "none" : string.Join(";", _director.Presentation.Views.GameplayCameras.Where(c => c.enabled).Select(c => c.rect.ToString()));
            _displayObservations.Add(stage + ": " + Screen.width + "x" + Screen.height + " " + Screen.fullScreenMode + "; seat=" + _director?.LocalSeat + "; epoch=" + _director?.MatchEpoch + "; tick=" + _director?.Sim?.Tick + "; viewports=" + viewports);
        }
        void ObserveMinimaps()
        {
            DashArenaMinimap[] maps = _director.Hud.GetComponentsInChildren<DashArenaMinimap>()
                .Where(m => m.enabled && m.gameObject.activeInHierarchy).ToArray();
            _report.active_minimap_count = maps.Length;
            _report.minimap_local_seats = maps.Select(m => m.LocalSeat).ToArray();
            _report.minimap_player_counts = maps.Select(m => m.ActivePlayerCount).ToArray();
            _report.minimap_ball_counts = maps.Select(m => m.ActiveBallCount).ToArray();
            _report.minimap_screen_rects = new Rect[maps.Length];
            _report.final_player_positions = _director.Sim.Players.Select(p => p.Position).ToArray();
            _report.minimap_layout_valid = maps.Length == (_mode == "local" ? 2 : 1);
            int playerCount = _director.Sim.Players.Count(p => p.Alive), ballCount = _director.Sim.Balls.Count(b => b.Active);
            var corners = new Vector3[4];
            for (int i = 0; i < maps.Length; i++)
            {
                maps[i].RectTransform.GetWorldCorners(corners);
                Vector2 min = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
                Vector2 max = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
                Rect rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                _report.minimap_screen_rects[i] = rect;
                int seat = maps[i].LocalSeat;
                float left = _mode == "local" ? seat * Screen.width * .5f : 0f;
                float right = _mode == "local" ? (seat + 1) * Screen.width * .5f : Screen.width;
                _report.minimap_layout_valid &= seat >= 0 && seat < 2 && (_mode == "local" || seat == _director.LocalSeat) &&
                    rect.width > 0 && rect.height > 0 && rect.xMin >= left && rect.xMax <= right + 1f &&
                    rect.center.x > (left + right) * .5f && rect.center.y > Screen.height * .5f && rect.yMax <= Screen.height + 1f &&
                    maps[i].ActivePlayerCount == playerCount && maps[i].ActiveBallCount == ballCount;
            }
            if (_mode == "local") _report.minimap_layout_valid &= _report.minimap_local_seats.Distinct().Count() == 2;
        }
        void ObserveEffects(DashArenaArtSample.SceneReferences views)
        {
            int pulses = views.Pulses.Count(p => p.Root.gameObject.activeInHierarchy);
            int debris = views.Walls.Count(w => w.Debris.gameObject.activeInHierarchy);
            int pickups = views.Pickups.Count(p => p.Root.gameObject.activeInHierarchy);
            int playerTrail = views.Players.Max(p => p.Trail.positionCount), ballTrail = views.Balls.Max(b => b.Trail.positionCount);
            int popups = _director.Hud.Seats.Count(s => s.PickupPanel.activeInHierarchy);
            _report.peak_pulses = Mathf.Max(_report.peak_pulses, pulses); _report.peak_wall_debris = Mathf.Max(_report.peak_wall_debris, debris);
            _report.peak_pickups = Mathf.Max(_report.peak_pickups, pickups); _report.peak_personal_popups = Mathf.Max(_report.peak_personal_popups, popups);
            _report.peak_player_trail_points = Mathf.Max(_report.peak_player_trail_points, playerTrail); _report.peak_ball_trail_points = Mathf.Max(_report.peak_ball_trail_points, ballTrail);
            _report.effect_capacity_bounds_held &= views.Pulses.Length == _report.pulse_capacity && views.Walls.Length == _report.wall_debris_capacity && views.Pickups.Length == _report.pickup_capacity &&
                pulses <= _report.pulse_capacity && debris <= _report.wall_debris_capacity && pickups <= _report.pickup_capacity && playerTrail <= 18 && ballTrail <= 16 && popups <= (_mode == "local" ? 2 : 1);
        }
        void ObserveEvents(IReadOnlyList<DashArenaEvent> events) { foreach (DashArenaEvent item in events) _observedEvents.Add(item.Kind); }
        void ObserveLog(string message, string stack, LogType type)
        {
            if (type == LogType.Warning) _report.warnings++;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) _report.errors++;
            if (type == LogType.Log || _logs.Count >= 64) return;
            string entry = type + ": " + message + "\n" + stack;
            _logs.Add(entry.Length <= 4096 ? entry : entry.Substring(0, 4096));
        }
        IEnumerator CaptureActualFrame(string name)
        {
            string path = Path.Combine(_output, name + ".png");
            if (File.Exists(path)) throw new IOException("Refusing to overwrite screenshot.");
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(path);
            float timeout = Time.realtimeSinceStartup + 15f;
            while ((!File.Exists(path) || new FileInfo(path).Length <= 128) && Time.realtimeSinceStartup < timeout) yield return null;
            if (File.Exists(path) && new FileInfo(path).Length > 128) _screenshots.Add(path);
            else { _report.errors++; _logs.Add("Player screenshot was not written: " + path); }
        }
        IEnumerator Finish(bool passed, string message)
        {
            if (_finishing) yield break;
            _finishing = true;
            if (_createdOutput) yield return CaptureActualFrame(passed ? "player-after-sampling" : "player-partial-or-failure");
            WriteReport(passed && _report.errors == 0, message);
            if (_quitOnComplete) Application.Quit(_report.passed ? 0 : 2);
        }
        void WriteReport(bool passed, string message)
        {
            if (_written || !_createdOutput) return;
            _report.complete = _frames.Count == _frameGoal; _report.passed = passed && _report.complete && !Application.isEditor;
            if (_report.realtime_end == 0f)
            {
                _report.realtime_end = Time.realtimeSinceStartup;
                if (_director != null && _director.Sim != null) _report.tick_end = _director.Sim.Tick;
                RecordInputPacing();
            }
            _report.message = message; _report.utc_finished = DateTime.UtcNow.ToString("o"); _report.sampled_frames = _frames.Count;
            if (_frames.Count > 0)
            {
                float[] sorted = _frames.OrderBy(t => t).ToArray();
                _report.p50_frame_ms = Percentile(sorted, .5f); _report.p95_frame_ms = Percentile(sorted, .95f); _report.max_frame_ms = sorted[sorted.Length - 1];
            }
            _report.observed_confirmed_events = _observedEvents.Select(e => e.ToString()).OrderBy(s => s).ToArray();
            _report.retained_logs = _logs.ToArray(); _report.screenshots = _screenshots.ToArray(); _report.display_observations = _displayObservations.ToArray();
            try
            {
                WriteNew(Path.Combine(_output, "player-presentation-probe.json"), JsonUtility.ToJson(_report, true));
                var csv = new StringBuilder("sample,unscaled_frame_ms\n");
                for (int i = 0; i < _frames.Count; i++) csv.Append(i).Append(',').Append(_frames[i].ToString("F6", CultureInfo.InvariantCulture)).Append('\n');
                WriteNew(Path.Combine(_output, "frame-intervals.csv"), csv.ToString());
                _written = true;
                Debug.Log("Dash actual-Player presentation probe: " + _report.passed + "; " + _output);
            }
            catch (Exception error) { _report.passed = false; Debug.LogError("Could not retain Player probe report: " + error.Message); }
        }
        void OnApplicationQuit() { if (!_written) WriteReport(false, "Player quit before complete capture/report; partial evidence only."); }
        void RecordInputPacing()
        {
            if (!_samplingStarted) return;
            _report.pacing_wait_count_delta = Mathf.Max(0, (_director?.LanSession?.InputWaitCount ?? 0) - _waitCountAtSampleStart);
            _report.pacing_wait_seconds_delta = Mathf.Max(0f, (_director?.LanSession?.InputWaitSeconds ?? 0f) - _waitSecondsAtSampleStart);
        }
        void OnDestroy()
        {
            Application.logMessageReceived -= ObserveLog;
            if (_director != null) _director.ConfirmedEvents -= ObserveEvents;
        }
        DashArenaInput LocalInput(int seat, int tick, DashArenaState state) => _localRoute == "goals" ? GoalApproachInput(seat, state) : DashArenaInput.Unpack(new DashArenaInput
        { Move = Directions[(tick / 45 + seat * 2) % Directions.Length], DashPressed = tick >= 180 && tick % 240 == 200 }.Pack());
        public static DashArenaInput GoalApproachInput(int seat, DashArenaState state)
        {
            if (state == null || seat < 0 || seat > 1) throw new ArgumentException("A real state and valid seat are required.");
            Vector2 target = new Vector2(0f, (seat == 0 ? 1f : -1f) * (state.Rules.HalfHeight - 4f));
            Vector2 delta = target - state.Players[seat].Position - state.Players[seat].Velocity * .12f;
            return DashArenaInput.Unpack(new DashArenaInput { Move = Vector2.ClampMagnitude(delta, 1f) }.Pack());
        }
        public static bool GoalApproachesReached(DashArenaState state)
        {
            if (state == null) return false;
            for (int seat = 0; seat < 2; seat++)
            {
                Vector2 target = new Vector2(0f, (seat == 0 ? 1f : -1f) * (state.Rules.HalfHeight - 4f));
                if (!state.Players[seat].Alive || (target - state.Players[seat].Position).sqrMagnitude > 1f) return false;
            }
            return true;
        }
        static float Percentile(float[] sorted, float fraction)
        {
            float at = (sorted.Length - 1) * fraction; int lower = Mathf.FloorToInt(at), upper = Mathf.CeilToInt(at);
            return Mathf.Lerp(sorted[lower], sorted[upper], at - lower);
        }
        static void WriteNew(string path, string content)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes, 0, bytes.Length);
        }
        static int ReadInt(string prefix, int fallback, int min, int max)
        {
            string raw = Argument(prefix); if (raw.Length == 0) return fallback;
            if (!int.TryParse(raw, out int value) || value < min || value > max) throw new ArgumentException("Invalid " + prefix);
            return value;
        }
        static string Argument(string prefix) => Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?.Substring(prefix.Length) ?? "";
        static string ValueOr(string prefix, string fallback) { string value = Argument(prefix); return value.Length == 0 ? fallback : value; }
        static bool Has(string value) => Environment.GetCommandLineArgs().Any(a => string.Equals(a, value, StringComparison.OrdinalIgnoreCase));
    }
}
#endif
