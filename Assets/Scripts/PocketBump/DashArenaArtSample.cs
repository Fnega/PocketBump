using System;
using System.Collections.Generic;
using UnityEngine;

namespace PocketBump
{
    // Isolated, silent G3 art review. Nothing is generated or recolored on scene load.
    // Authored materials/prefabs are the baseline; only visual children animate in play.
    public sealed class DashArenaArtSample : MonoBehaviour
    {
        [Serializable] public sealed class RobotView
        {
            public Transform Root, Motion, LeftArm, RightArm, LeftFoot, RightFoot;
            public Transform GreenAura, PurpleAura, Stars;
            public LineRenderer Trail;
        }
        [Serializable] public sealed class BallView { public Transform Root; public LineRenderer Trail; }
        [Serializable] public sealed class PillarView { public int Id; public Transform Root, Bell; }
        [Serializable] public sealed class WallView
        {
            public int Id;
            public Transform Root, Solid, Outline, Debris;
        }
        [Serializable] public sealed class PickupView { public Transform Root; public Transform[] Icons; }
        [Serializable] public sealed class PulseView { public Transform Root; public Transform[] Variants; }
        [Serializable] public sealed class SceneReferences
        {
            public RobotView[] Players;
            public BallView[] Balls;
            public PillarView[] Pillars;
            public WallView[] Walls;
            public PickupView[] Pickups;
            public Transform Gate;
            public Camera[] GameplayCameras;
            public Camera ShowcaseCamera;
            public Material[] ThemeMaterials;
            public PulseView[] Pulses;
        }

        // Checked against the actual approved Font by the explicit sample baker.
        public const string RequiredChineseGlyphs = "晕中文仅本机预览绿蓝紫冲击强化霸体安全传送无位置撞击玩家与球持续秒免击退眩晕仍可掉落成功速度清零未找到拾取已消耗，、·×";

        [SerializeField] SceneReferences _views;
        [SerializeField, Range(1, 8)] int _ballCount = 1;
        [SerializeField, Range(7f, 18f)] float _followHalfHeight = 10.5f;
        [SerializeField] bool _automaticTicking = true;
        [SerializeField] bool _showHud = true;
        [SerializeField] Font _previewFont;
        readonly Vector3[] _motionRest = new Vector3[2];
        readonly Vector3[] _motionScale = new Vector3[2];
        readonly Quaternion[,] _limbRest = new Quaternion[2, 4];
        readonly Vector2[] _previousPlayers = new Vector2[2];
        readonly string[] _messages = new string[2];
        readonly float[] _messageUntil = new float[2];
        readonly DashPickupKind[] _messageKinds = new DashPickupKind[2];
        readonly int[] _playerHitTicks = new int[2];
        readonly HashSet<DashArenaEventIdentity> _seen = new HashSet<DashArenaEventIdentity>();
        readonly Queue<DashArenaEventIdentity> _seenOrder = new Queue<DashArenaEventIdentity>();
        readonly List<Vector3>[] _playerPaths = { new List<Vector3>(24), new List<Vector3>(24) };
        List<Vector3>[] _ballPaths;
        Vector3[] _wallRest;
        int[] _wallHitTicks, _wallBreakTicks, _pillarHitTicks;
        int[] _pulseTicks;
        Vector3[] _pillarRestScale, _pulseRestScale;
        int _nextPulse;
        MaterialPropertyBlock _pulseProperties;
        int _lastRenderedTick = -1;
        float _accumulator;
        DashArenaState _state;
        DashControlBindings _bindings;
        DashTickInputSampler _input;
        Font _font;
        bool _ownsFont;
        GUIStyle _label;

        public DashArenaState State => _state;
        public SceneReferences Views => _views;
        public bool AutomaticTicking { get => _automaticTicking; set => _automaticTicking = value; }
        public bool ShowHud { get => _showHud; set => _showHud = value; }
        public Font PreviewFont { get => _previewFont; set => _previewFont = value; }
        public bool IsConfigured => _views != null && _views.Players != null && _views.Players.Length == 2;
        public string MessageForSeat(int seat)
        {
            if (seat < 0 || seat > 1) throw new ArgumentOutOfRangeException(nameof(seat));
            return _messages[seat];
        }
        public string StatusForSeat(int seat)
        {
            if (seat < 0 || seat > 1) throw new ArgumentOutOfRangeException(nameof(seat));
            if (_state == null) return "NOT STARTED";
            DashPlayerState player = _state.Players[seat];
            if (!player.Alive) return "RESPAWN " + Seconds(player.RespawnTicks) + "s";
            if (_state.Phase == MatchState.Kickoff) return "KICKOFF " + Seconds(_state.PhaseTicks) + "s / DASH LOCKED";
            if (_state.Phase != MatchState.Live) return _state.Phase + " / DASH LOCKED";
            return player.StunTicks > 0 ? "晕 / STUN " + Seconds(player.StunTicks) + "s"
                : player.DashTicks > 0 ? "DASH" : player.DashCooldownTicks > 0 ? "COOLDOWN " + Seconds(player.DashCooldownTicks) + "s" : "DASH READY";
        }

        public void Configure(SceneReferences references)
        {
            if (_state != null) throw new InvalidOperationException("Cannot replace sample references during a running preview.");
            _views = references ?? throw new ArgumentNullException(nameof(references));
        }

        void OnEnable()
        {
            if (Application.isPlaying && IsConfigured && _state == null) RestartPreview();
        }

        void OnDisable()
        {
            _input?.Clear();
            _accumulator = 0f;
            if (_state != null) ClearTransientPresentation();
        }

        void OnDestroy()
        {
            if (!_ownsFont || _font == null) return;
            if (Application.isPlaying) Destroy(_font);
            else DestroyImmediate(_font);
        }

        public void RestartPreview()
        {
            if (!IsConfigured || _views.Balls == null || _views.Balls.Length != ArenaRulesProfile.MaximumBalls)
                throw new InvalidOperationException("The authored art sample is incomplete; use its explicit authoring command first.");
            if (_state == null) CaptureAuthoredPoses();
            _bindings = _bindings ?? new DashControlBindings();
            _input = _input ?? new DashTickInputSampler(_bindings);
            _input.Clear();
            _state = DashArenaSimulation.Create(ArenaRulesProfile.CreateProductionV1(8, Mathf.Clamp(_ballCount, 1, 8)), 1u);
            DashArenaSimulation.StartMatch(_state);
            _accumulator = 0f;
            _lastRenderedTick = -1;
            _seen.Clear();
            _seenOrder.Clear();
            _messages[0] = _messages[1] = "";
            _messageUntil[0] = _messageUntil[1] = 0f;
            for (int seat = 0; seat < 2; seat++) _previousPlayers[seat] = _state.Players[seat].Position;
            ClearTransientPresentation();
            RenderPreview(1f);
        }

        void CaptureAuthoredPoses()
        {
            for (int seat = 0; seat < 2; seat++)
            {
                RobotView robot = _views.Players[seat];
                _motionRest[seat] = robot.Motion.localPosition;
                _motionScale[seat] = robot.Motion.localScale;
                for (int limb = 0; limb < 4; limb++) _limbRest[seat, limb] = Limb(robot, limb).localRotation;
            }
            _wallRest = new Vector3[_views.Walls.Length];
            _wallHitTicks = new int[_views.Walls.Length];
            _wallBreakTicks = new int[_views.Walls.Length];
            for (int i = 0; i < _views.Walls.Length; i++) _wallRest[i] = _views.Walls[i].Solid.localPosition;
            _pillarHitTicks = new int[_views.Pillars.Length];
            _pillarRestScale = new Vector3[_views.Pillars.Length];
            for (int i = 0; i < _views.Pillars.Length; i++) _pillarRestScale[i] = _views.Pillars[i].Root.localScale;
            _views.Pulses = _views.Pulses ?? Array.Empty<PulseView>();
            _pulseTicks = new int[_views.Pulses.Length];
            _pulseRestScale = new Vector3[_views.Pulses.Length];
            for (int i = 0; i < _views.Pulses.Length; i++) _pulseRestScale[i] = _views.Pulses[i].Root.localScale;
            _ballPaths = new List<Vector3>[_views.Balls.Length];
            for (int i = 0; i < _ballPaths.Length; i++) _ballPaths[i] = new List<Vector3>(18);
        }

        void Update()
        {
            if (!_automaticTicking || _state == null) return;
            _input.LatchUpdate(false, 0);
            _accumulator += Time.deltaTime;
            for (int steps = 0; _accumulator >= 1f / 60f && steps < 8; steps++)
            {
                _accumulator -= 1f / 60f;
                StepPreview(_input.SampleLocal(0), _input.SampleLocal(1));
            }
        }

        void LateUpdate()
        {
            if (_state != null) RenderPreview(_automaticTicking ? Mathf.Clamp01(_accumulator * 60f) : 1f);
        }

        public void StepPreview(DashArenaInput one, DashArenaInput two)
        {
            if (_state == null) throw new InvalidOperationException("Start the art preview before stepping.");
            for (int i = 0; i < 2; i++) _previousPlayers[i] = _state.Players[i].Position;
            PresentEvents(DashArenaSimulation.Step(_state, one, two));
        }

        // This isolated local world commits every executed event. Future LAN integration
        // must supply reconciled events; this sample is not evidence of LAN art reconciliation.
        public void PresentEvents(IReadOnlyList<DashArenaEvent> events)
        {
            foreach (DashArenaEvent item in events)
            {
                if (!_seen.Add(item.Identity)) continue;
                _seenOrder.Enqueue(item.Identity);
                while (_seenOrder.Count > 512) _seen.Remove(_seenOrder.Dequeue());
                if (item.Kind == DashArenaEventKind.PillarHit || item.Kind == DashArenaEventKind.Stunned)
                    for (int i = 0; i < _views.Pillars.Length; i++)
                        if (_views.Pillars[i].Id == item.EntityId) _pillarHitTicks[i] = _state.Tick;
                if (item.Kind == DashArenaEventKind.WallHit || item.Kind == DashArenaEventKind.WallBroken)
                    for (int i = 0; i < _views.Walls.Length; i++)
                        if (_views.Walls[i].Id == item.EntityId)
                        {
                            _wallHitTicks[i] = _state.Tick;
                            if (item.Kind == DashArenaEventKind.WallBroken) _wallBreakTicks[i] = _state.Tick;
                        }
                if (item.Seat < 0 || item.Seat > 1) continue;
                if (item.Kind == DashArenaEventKind.PillarHit || item.Kind == DashArenaEventKind.Stunned)
                    _playerHitTicks[item.Seat] = _state.Tick;
                if (item.Kind == DashArenaEventKind.Teleport || item.Kind == DashArenaEventKind.Respawned || item.Kind == DashArenaEventKind.Fell)
                    ClearPlayerTrail(item.Seat);
                if (item.Kind == DashArenaEventKind.Pickup && item.PickupKind.HasValue)
                    ShowPulse((int)item.PickupKind.Value, item.Position);
                else if (item.Kind == DashArenaEventKind.Teleport)
                {
                    ShowPulse(3, _previousPlayers[item.Seat]);
                    ShowPulse(3, item.Position);
                }
                else if (item.Kind == DashArenaEventKind.Respawned) ShowPulse(4, item.Position);
                // Even a sub-three-unit discontinuity is a snap, not movement to interpolate.
                // Keep the departure position until its teleport pulse has been placed above.
                if (item.Kind == DashArenaEventKind.Teleport || item.Kind == DashArenaEventKind.Respawned)
                    _previousPlayers[item.Seat] = _state.Players[item.Seat].Position;
                string message = null;
                if (item.Kind == DashArenaEventKind.Pickup && item.PickupKind == DashPickupKind.Green)
                    message = "绿·冲击强化 / GREEN\n撞击玩家与球 ×" + _state.Rules.GreenImpactMultiplier.ToString("0.0") + "，持续 " + Seconds(_state.Rules.GreenTicks) + " 秒";
                else if (item.Kind == DashArenaEventKind.Pickup && item.PickupKind == DashPickupKind.Purple)
                    message = "紫·霸体 / PURPLE\n免击退、免眩晕 " + Seconds(_state.Rules.PurpleTicks) + " 秒，仍可掉落";
                else if (item.Kind == DashArenaEventKind.Teleport) message = "蓝·安全传送 / BLUE\n传送成功，速度清零";
                else if (item.Kind == DashArenaEventKind.TeleportFailed) message = "蓝·安全传送 / BLUE\n未找到安全位置，拾取已消耗";
                if (message != null)
                {
                    _messages[item.Seat] = message;
                    _messageKinds[item.Seat] = item.PickupKind ?? DashPickupKind.Blue;
                    _messageUntil[item.Seat] = Time.unscaledTime + 2.8f;
                }
            }
        }

        public void RenderPreview(float interpolation)
        {
            if (_state == null) return;
            if (_state.Tick < _lastRenderedTick) ClearTransientPresentation();
            bool newTick = _state.Tick != _lastRenderedTick;
            float t = Mathf.Clamp01(interpolation);
            float clock = _state.Tick / 60f;
            for (int seat = 0; seat < 2; seat++)
            {
                RobotView view = _views.Players[seat];
                DashPlayerState player = _state.Players[seat];
                Vector2 prior = _previousPlayers[seat];
                if ((player.Position - prior).sqrMagnitude > 9f) { prior = player.Position; ClearPlayerTrail(seat); }
                Vector2 shown = Vector2.Lerp(prior, player.Position, t);
                view.Root.gameObject.SetActive(player.Alive);
                view.Root.position = Ground(shown, 0f);
                Vector3 facing = Ground(player.Facing, 0f);
                view.Root.rotation = Quaternion.LookRotation(facing.sqrMagnitude > .001f ? facing : Vector3.forward, Vector3.up);
                float moving = Mathf.Clamp01(player.Velocity.magnitude / _state.Rules.NormalSpeed);
                float gait = Mathf.Sin(clock * 20f) * moving;
                view.Motion.localPosition = _motionRest[seat] + Vector3.up * (Mathf.Abs(gait) * .045f);
                int hitAge = _state.Tick - _playerHitTicks[seat];
                float rebound = hitAge >= 0 && hitAge < 10 ? Mathf.Sin(hitAge / 10f * Mathf.PI) : 0f;
                Vector3 motionScale = player.DashTicks > 0 ? new Vector3(1.04f, .93f, 1.04f) : Vector3.one;
                motionScale = Vector3.Scale(motionScale, new Vector3(1f + rebound * .06f, 1f - rebound * .12f, 1f + rebound * .06f));
                view.Motion.localScale = Vector3.Scale(_motionScale[seat], motionScale);
                for (int limb = 0; limb < 4; limb++)
                    Limb(view, limb).localRotation = _limbRest[seat, limb] * Quaternion.Euler(gait * (limb % 2 == 0 ? 14f : -14f), 0f, 0f);
                view.GreenAura.gameObject.SetActive(player.Alive && player.GreenTicks > 0);
                view.PurpleAura.gameObject.SetActive(player.Alive && player.PurpleTicks > 0);
                view.Stars.gameObject.SetActive(player.Alive && player.StunTicks > 0);
                view.Stars.localRotation = Quaternion.Euler(0f, clock * 160f, 0f);
                if (newTick) AddTrail(view.Trail, _playerPaths[seat], Ground(shown, .16f),
                    player.Alive && moving > .12f, player.DashTicks > 0 ? 18 : 6, player.DashTicks > 0 ? .6f : .085f);
                UpdateCamera(_views.GameplayCameras[seat], shown);
            }
            for (int i = 0; i < _views.Balls.Length; i++)
            {
                BallView view = _views.Balls[i];
                bool active = i < _state.Balls.Length && _state.Balls[i].Active;
                view.Root.gameObject.SetActive(active);
                if (!active) { view.Trail.positionCount = 0; _ballPaths[i].Clear(); continue; }
                DashBallState ball = _state.Balls[i];
                view.Root.position = Ground(ball.Position, _state.Rules.BallRadius);
                bool danger = ball.Velocity.magnitude >= _state.Rules.DangerousBallSpeed;
                if (newTick) AddTrail(view.Trail, _ballPaths[i], Ground(ball.Position, .12f), ball.Velocity.sqrMagnitude > 1f, danger ? 16 : 7, danger ? .26f : .06f);
            }
            for (int i = 0; i < _views.Pillars.Length; i++)
            {
                int age = _state.Tick - _pillarHitTicks[i];
                _views.Pillars[i].Bell.gameObject.SetActive(age < 36);
                float squash = age < 18 ? 1f - Mathf.Sin(age / 18f * Mathf.PI) * .07f : 1f;
                _views.Pillars[i].Root.localScale = Vector3.Scale(_pillarRestScale[i], new Vector3(1f, squash, 1f));
            }
            for (int i = 0; i < _views.Walls.Length; i++)
            {
                WallView view = _views.Walls[i];
                DashWallState wall = _state.Walls[i];
                view.Solid.gameObject.SetActive(!wall.Broken);
                view.Outline.gameObject.SetActive(wall.Broken && wall.RepairPending);
                int age = _state.Tick - _wallHitTicks[i];
                view.Solid.localPosition = _wallRest[i] + Vector3.right * (age < 16 ? Mathf.Sin(age * 2.7f) * .09f * (1f - age / 16f) : 0f);
                int brokenAge = _state.Tick - _wallBreakTicks[i];
                view.Debris.gameObject.SetActive(wall.Broken && brokenAge < 90);
                view.Debris.localScale = Vector3.one * (brokenAge < 60 ? 1f : Mathf.Clamp01((90f - brokenAge) / 30f));
            }
            for (int i = 0; i < _views.Pickups.Length; i++)
            {
                PickupView view = _views.Pickups[i];
                DashPickupState pickup = _state.Pickups[i];
                view.Root.gameObject.SetActive(pickup.Active);
                view.Root.position = Ground(pickup.Position, .65f + Mathf.Sin(clock * 2.4f + i) * .1f);
                for (int kind = 0; kind < view.Icons.Length; kind++) view.Icons[kind].gameObject.SetActive(kind == (int)pickup.Kind);
            }
            _views.Gate.gameObject.SetActive(_state.GateClosed);
            for (int i = 0; i < _views.Pulses.Length; i++)
            {
                int age = _state.Tick - _pulseTicks[i];
                bool active = age >= 0 && age < 36;
                PulseView pulse = _views.Pulses[i];
                pulse.Root.gameObject.SetActive(active);
                if (!active) continue;
                float life = age / 36f;
                pulse.Root.localScale = _pulseRestScale[i] * (.35f + life * 1.3f);
                if (_pulseProperties == null) _pulseProperties = new MaterialPropertyBlock();
                foreach (Renderer renderer in pulse.Root.GetComponentsInChildren<Renderer>())
                {
                    Material authored = renderer.sharedMaterial;
                    if (authored == null || !authored.HasProperty("_BaseColor")) continue;
                    Color color = authored.GetColor("_BaseColor"); color.a *= 1f - life;
                    renderer.GetPropertyBlock(_pulseProperties); _pulseProperties.SetColor("_BaseColor", color);
                    renderer.SetPropertyBlock(_pulseProperties);
                }
            }
            _lastRenderedTick = _state.Tick;
        }

        void ShowPulse(int kind, Vector2 at)
        {
            if (_views.Pulses.Length == 0) return;
            int slot = _nextPulse++ % _views.Pulses.Length;
            PulseView view = _views.Pulses[slot];
            _pulseTicks[slot] = _state.Tick;
            view.Root.position = Ground(at, .055f);
            for (int i = 0; i < view.Variants.Length; i++) view.Variants[i].gameObject.SetActive(i == kind);
        }

        void ClearTransientPresentation()
        {
            for (int seat = 0; seat < 2; seat++)
            {
                RobotView robot = _views.Players[seat];
                ClearPlayerTrail(seat);
                _playerHitTicks[seat] = -100000;
                robot.Motion.localPosition = _motionRest[seat];
                robot.Motion.localScale = _motionScale[seat];
                for (int limb = 0; limb < 4; limb++) Limb(robot, limb).localRotation = _limbRest[seat, limb];
                robot.Stars.gameObject.SetActive(false);
                robot.GreenAura.gameObject.SetActive(false);
                robot.PurpleAura.gameObject.SetActive(false);
            }
            for (int i = 0; i < _ballPaths.Length; i++) { _ballPaths[i].Clear(); _views.Balls[i].Trail.positionCount = 0; }
            for (int i = 0; i < _wallHitTicks.Length; i++)
            {
                _wallHitTicks[i] = _wallBreakTicks[i] = -100000;
                _views.Walls[i].Solid.localPosition = _wallRest[i];
                _views.Walls[i].Debris.gameObject.SetActive(false);
            }
            for (int i = 0; i < _pillarHitTicks.Length; i++)
            {
                _pillarHitTicks[i] = -100000;
                _views.Pillars[i].Root.localScale = _pillarRestScale[i];
            }
            _nextPulse = 0;
            for (int i = 0; i < _pulseTicks.Length; i++)
            {
                _pulseTicks[i] = -100000;
                _views.Pulses[i].Root.gameObject.SetActive(false);
                _views.Pulses[i].Root.localScale = _pulseRestScale[i];
            }
        }

        void ClearPlayerTrail(int seat)
        {
            _playerPaths[seat].Clear();
            _views.Players[seat].Trail.positionCount = 0;
        }

        static void AddTrail(LineRenderer line, List<Vector3> points, Vector3 point, bool moving, int limit, float width)
        {
            if (moving)
            {
                if (points.Count > 0 && (points[points.Count - 1] - point).sqrMagnitude > 9f) points.Clear();
                points.Add(point);
            }
            else if (points.Count > 0) points.RemoveAt(0);
            while (points.Count > limit) points.RemoveAt(0);
            line.widthMultiplier = width;
            line.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++) line.SetPosition(i, points[i]);
        }

        void UpdateCamera(Camera camera, Vector2 at)
        {
            camera.orthographicSize = _followHalfHeight;
            float xMargin = Mathf.Min(_state.Rules.HalfWidth - 1f, _followHalfHeight * Mathf.Max(.1f, camera.aspect));
            float yMargin = _followHalfHeight * 1.15f;
            Vector3 focus = Ground(new Vector2(Mathf.Clamp(at.x, -_state.Rules.HalfWidth + xMargin, _state.Rules.HalfWidth - xMargin),
                Mathf.Clamp(at.y, -_state.Rules.HalfHeight + yMargin, _state.Rules.HalfHeight - yMargin)), 0f);
            camera.transform.position = focus + new Vector3(0f, 20f, -12f);
            camera.transform.rotation = Quaternion.LookRotation(focus - camera.transform.position, Vector3.up);
        }

        static Transform Limb(RobotView view, int index) => index == 0 ? view.LeftArm : index == 1 ? view.RightArm : index == 2 ? view.LeftFoot : view.RightFoot;
        public static Vector3 Ground(Vector2 point, float height) => new Vector3(point.x, height, point.y);
        static string Seconds(int ticks) => (ticks / 60f).ToString("0.0");

        void OnGUI()
        {
            if (!_showHud || _state == null) return;
            if (_label == null)
            {
                _font = _previewFont;
                _ownsFont = _font == null;
                // The optional OS fallback is preview-only, never copied or distributed.
                if (_ownsFont) _font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
                _label = new GUIStyle(GUI.skin.label) { font = _font, wordWrap = true };
                _label.normal.textColor = new Color(.12f, .2f, .24f);
            }
            _label.fontSize = Mathf.Clamp(Screen.width / 92, 12, 20);
            Panel(new Rect(0f, 0f, Screen.width, 59f));
            GUI.Label(new Rect(12f, 4f, Screen.width - 140f, 28f), "SILENT ART SAMPLE / NOT FINAL   P1 " + _state.Scores[0] + " : " + _state.Scores[1] + " P2", _label);
            GUI.Label(new Rect(12f, 30f, Screen.width - 140f, 25f), _state.Phase +
                (_previewFont != null ? " / approved embedded Chinese font / independent local sample" : " / 中文仅本机预览 / independent local sample"), _label);
            if (GUI.Button(new Rect(Screen.width - 123f, 16f, 113f, 30f), "Restart")) RestartPreview();
            float half = Screen.width * .5f;
            Panel(new Rect(half - 1f, 59f, 2f, Screen.height - 59f));
            for (int seat = 0; seat < 2; seat++)
            {
                GUI.BeginGroup(new Rect(seat * half, 61f, half, Screen.height - 61f));
                DashPlayerState player = _state.Players[seat];
                Panel(new Rect(8f, 4f, half - 16f, 65f));
                string status = StatusForSeat(seat);
                GUI.Label(new Rect(15f, 7f, half - 30f, 30f), "P" + (seat + 1) + (seat == 0 ? " WASD + " : " ARROWS + ") + DashControlBindings.Label(_bindings.Dash(seat)) + "   " + status, _label);
                GUI.Label(new Rect(15f, 36f, half - 30f, 28f), "GREEN " + Seconds(player.GreenTicks) + "s   PURPLE " + Seconds(player.PurpleTicks) + "s", _label);
                if (_messageUntil[seat] > Time.unscaledTime)
                {
                    Panel(new Rect(8f, 76f, half - 16f, 66f));
                    DrawBuffIcon(new Rect(15f, 88f, 39f, 39f), _messageKinds[seat]);
                    GUI.Label(new Rect(66f, 80f, half - 81f, 58f), _messages[seat], _label);
                }
                if (player.Alive && player.StunTicks > 0)
                {
                    Vector3 head = _views.GameplayCameras[seat].WorldToScreenPoint(_views.Players[seat].Root.position + Vector3.up * 2.1f);
                    GUI.Label(new Rect(Mathf.Clamp(head.x - seat * half - 16f, 4f, half - 76f),
                        Mathf.Clamp(Screen.height - head.y - 77f, 0f, Screen.height - 98f), 72f, 34f), "晕", _label);
                }
                GUI.EndGroup();
            }
        }

        static void Panel(Rect area)
        {
            Color previous = GUI.color;
            GUI.color = new Color(.96f, .95f, .86f, .9f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        // The same star / spiral / shield outlines as the authored 3D pickup symbols.
        // GUI drawing introduces no downloaded icon asset, click target or input capture.
        static void DrawBuffIcon(Rect area, DashPickupKind kind)
        {
            var points = new List<Vector2>();
            if (kind == DashPickupKind.Green)
                for (int i = 0; i <= 10; i++)
                {
                    float angle = -Mathf.PI * .5f + i * Mathf.PI / 5f, radius = i % 2 == 0 ? .45f : .2f;
                    points.Add(new Vector2(.5f + Mathf.Cos(angle) * radius, .5f + Mathf.Sin(angle) * radius));
                }
            else if (kind == DashPickupKind.Blue)
                for (int i = 0; i < 42; i++)
                {
                    float t = i / 41f, angle = t * Mathf.PI * 4.3f;
                    points.Add(new Vector2(.5f + Mathf.Cos(angle) * (.04f + .40f * t), .5f + Mathf.Sin(angle) * (.04f + .40f * t)));
                }
            else points.AddRange(new[] { new Vector2(.15f, .15f), new Vector2(.5f, .23f), new Vector2(.85f, .15f),
                new Vector2(.82f, .52f), new Vector2(.7f, .78f), new Vector2(.5f, .92f), new Vector2(.18f, .55f), new Vector2(.15f, .15f) });
            Color color = GUI.color; Matrix4x4 matrix = GUI.matrix;
            GUI.color = new Color(.12f, .23f, .23f);
            for (int i = 1; i < points.Count; i++)
            {
                Vector2 start = area.position + Vector2.Scale(points[i - 1], area.size);
                Vector2 end = area.position + Vector2.Scale(points[i], area.size), delta = end - start;
                GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
                GUI.DrawTexture(new Rect(start.x, start.y - 1f, delta.magnitude, 2f), Texture2D.whiteTexture);
                GUI.matrix = matrix;
            }
            GUI.color = color;
        }
    }
}
