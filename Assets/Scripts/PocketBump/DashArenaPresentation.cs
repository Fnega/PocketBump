using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using RobotView = PocketBump.DashArenaArtSample.RobotView;
using BallView = PocketBump.DashArenaArtSample.BallView;
using WallView = PocketBump.DashArenaArtSample.WallView;
using PickupView = PocketBump.DashArenaArtSample.PickupView;
using PulseView = PocketBump.DashArenaArtSample.PulseView;
using SceneReferences = PocketBump.DashArenaArtSample.SceneReferences;

namespace PocketBump
{
    // A view of ONE authoritative world. The director owns every tick and confirmation.
    // Baseline geometry/materials remain authored; no procedural art or physics is added here.
    public sealed class DashArenaPresentation : MonoBehaviour
    {
        [SerializeField] SceneReferences _views;
        [SerializeField] DashMatchHud _hud;
        [SerializeField, Range(7f, 18f)] float _followHalfHeight = 10.5f;
        [SerializeField] Renderer[] _blueGoalGeometry, _orangeGoalGeometry;
        static readonly Vector3 FollowOffset = new Vector3(0f, 20f, -12f);
        static readonly Quaternion FollowRotation = Quaternion.LookRotation(-FollowOffset, Vector3.up);
        const float GoalApproachDistance = 8f, GoalBlendDistance = 16f;
        readonly Renderer[][] _robotGeometry = new Renderer[2][];
        readonly Vector3[] _motionRest = new Vector3[2], _motionScale = new Vector3[2];
        readonly Quaternion[,] _limbRest = new Quaternion[2, 4];
        readonly Vector2[] _previousPlayers = new Vector2[2];
        readonly int[] _playerHitTicks = new int[2];
        readonly List<Vector3>[] _playerPaths = { new List<Vector3>(24), new List<Vector3>(24) };
        readonly HashSet<DashArenaEventIdentity> _seen = new HashSet<DashArenaEventIdentity>();
        readonly Queue<DashArenaEventIdentity> _seenOrder = new Queue<DashArenaEventIdentity>();
        int _lastConfirmedTick = -1;
        List<Vector3>[] _ballPaths;
        Vector2[] _previousBalls;
        bool[] _previousBallActive;
        Vector3[] _wallRest, _pillarRestScale, _pulseRestScale, _debrisRestScale;
        int[] _wallHitTicks, _wallBreakTicks, _pillarHitTicks, _pulseTicks;
        int _nextPulse, _lastRenderedTick = -1, _localSeat;
        bool _captured, _isLan;
        MatchState _previousPhase;
        MaterialPropertyBlock _pulseProperties;
        DashArenaState _state;
        readonly List<Renderer> _occluders = new List<Renderer>();
        readonly Dictionary<Material, Material> _faded = new Dictionary<Material, Material>();
        readonly Dictionary<Renderer, Material[]> _restored = new Dictionary<Renderer, Material[]>();

        public SceneReferences Views => _views;
        public bool IsLan => _isLan;
        public int LocalSeat => _localSeat;
        public DashArenaState State => _state;
        public long ConfirmedCueCount { get; private set; }
        public void Configure(SceneReferences references, DashMatchHud hud)
        {
            if (_captured) throw new InvalidOperationException("Do not replace authored references during a match.");
            _views = references ?? throw new ArgumentNullException(nameof(references));
            _hud = hud;
        }

        public void BindState(DashArenaState state, bool isLan, int localSeat)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (localSeat < 0 || localSeat > 1) throw new ArgumentOutOfRangeException(nameof(localSeat));
            if (_views == null || _views.Players?.Length != 2 || _views.GameplayCameras?.Length != 2 ||
                _views.Balls == null || _views.Balls.Length < state.Balls.Length)
                throw new InvalidOperationException("The authored Dash scene needs two robot/camera views and every scoring ball.");
            if (!_captured)
            {
                CaptureAuthoredPoses();
                ResolveGoalGeometry();
                for (int seat = 0; seat < 2; seat++)
                    _robotGeometry[seat] = _views.Players[seat].Motion.GetComponentsInChildren<Renderer>(true);
                _previousBalls = new Vector2[_views.Balls.Length];
                _previousBallActive = new bool[_views.Balls.Length];
                _debrisRestScale = new Vector3[_views.Walls.Length];
                for (int i = 0; i < _views.Walls.Length; i++) _debrisRestScale[i] = _views.Walls[i].Debris.localScale;
                foreach (var pillar in _views.Pillars) _occluders.AddRange(pillar.Root.GetComponentsInChildren<Renderer>(true));
                foreach (var wall in _views.Walls) _occluders.AddRange(wall.Solid.GetComponentsInChildren<Renderer>(true));
                _captured = true;
            }
            _state = state; _isLan = isLan; _localSeat = localSeat;
            _seen.Clear(); _seenOrder.Clear(); _lastConfirmedTick = -1; ConfirmedCueCount = 0;
            ClearTransientPresentation(); SnapAllPoses(); ConfigureCameras();
            _hud?.ClearPickupMessages();
            _lastRenderedTick = -1;
            Render(state, 1f);
        }

        void ConfigureCameras()
        {
            for (int i = 0; i < 2; i++)
            {
                Camera camera = _views.GameplayCameras[i];
                camera.orthographic = true;
                camera.enabled = !_isLan || i == 0;
                camera.rect = _isLan ? new Rect(0f, 0f, 1f, 1f) : new Rect(i * .5f, 0f, .5f, 1f);
                // Camera zero follows the assigned local seat in LAN; only it owns audio.
                foreach (AudioListener listener in camera.GetComponents<AudioListener>()) listener.enabled = i == 0;
            }
            if (_views.ShowcaseCamera != null) _views.ShowcaseCamera.enabled = false;
        }

        public void CapturePreviousPose(DashArenaState state)
        {
            if (state == null || !_captured) return;
            _state = state;
            for (int i = 0; i < 2; i++) _previousPlayers[i] = state.Players[i].Position;
            for (int i = 0; i < _previousBalls.Length; i++)
            {
                _previousBallActive[i] = i < state.Balls.Length && state.Balls[i].Active;
                if (i < state.Balls.Length) _previousBalls[i] = state.Balls[i].Position;
            }
            _previousPhase = state.Phase;
        }

        void SnapAllPoses()
        {
            CapturePreviousPose(_state);
            for (int i = 0; i < 2; i++) ClearPlayerTrail(i);
            for (int i = 0; i < _ballPaths.Length; i++) { _ballPaths[i].Clear(); _views.Balls[i].Trail.positionCount = 0; }
        }

        // Predicted transitions reset paths only; they never announce pickups or spawn cues.
        public void ApplyTransitions(IReadOnlyList<DashArenaEvent> events)
        {
            if (_state == null || events == null) return;
            foreach (var item in events)
            {
                if (item.Epoch != _state.Epoch) continue;
                if (item.Kind == DashArenaEventKind.Goal || item.Kind == DashArenaEventKind.GateOpened) SnapAllPoses();
                if (item.Seat >= 0 && item.Seat < 2 &&
                    (item.Kind == DashArenaEventKind.Teleport || item.Kind == DashArenaEventKind.Respawned || item.Kind == DashArenaEventKind.Fell))
                {
                    _previousPlayers[item.Seat] = _state.Players[item.Seat].Position;
                    ClearPlayerTrail(item.Seat);
                }
            }
        }

        public void ReconcileState(DashArenaState state)
        {
            if (state == null || !_captured) return;
            _state = state; SnapAllPoses(); _lastRenderedTick = -1;
        }

        public void PresentConfirmedEvents(IReadOnlyList<DashArenaEvent> events)
        {
            if (_state == null || events == null) return;
            foreach (var item in events)
            {
                // The ledger is monotonic. Reject stale epochs/ticks even after bounded dedup eviction.
                if (item.Epoch != _state.Epoch || item.Tick < _lastConfirmedTick || !_seen.Add(item.Identity)) continue;
                _lastConfirmedTick = item.Tick; _seenOrder.Enqueue(item.Identity);
                while (_seenOrder.Count > 512) _seen.Remove(_seenOrder.Dequeue());
                ConfirmedCueCount++;
                _hud?.ShowConfirmedTransition(item);
                if (item.Kind == DashArenaEventKind.Impact) ShowPulse(4, item.Position);
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
                if (item.Kind == DashArenaEventKind.Pickup && item.PickupKind.HasValue) ShowPulse((int)item.PickupKind.Value, item.Position);
                else if (item.Kind == DashArenaEventKind.Teleport) ShowPulse(3, item.Position);
                else if (item.Kind == DashArenaEventKind.Respawned) ShowPulse(4, item.Position);
                _hud?.ShowConfirmedPickup(item, _state.Rules);
            }
        }

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginSrpCamera;
            RenderPipelineManager.endCameraRendering += EndSrpCamera;
            Camera.onPreCull += BeginCameraOcclusion;
            Camera.onPostRender += EndCameraOcclusion;
        }
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginSrpCamera;
            RenderPipelineManager.endCameraRendering -= EndSrpCamera;
            Camera.onPreCull -= BeginCameraOcclusion;
            Camera.onPostRender -= EndCameraOcclusion;
            ClearTransientPresentation();
        }
        void OnDestroy()
        {
            RestoreOcclusion();
            foreach (var material in _faded.Values)
                if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
            _faded.Clear();
        }
        void BeginSrpCamera(ScriptableRenderContext context, Camera camera) => BeginCameraOcclusion(camera);
        void EndSrpCamera(ScriptableRenderContext context, Camera camera) => EndCameraOcclusion(camera);

        // Temporary per-camera materials: original assets and the other viewport remain untouched.
        public void BeginCameraOcclusion(Camera camera)
        {
            RestoreOcclusion();
            if (_state == null || camera == null || !camera.enabled) return;
            int cameraIndex = Array.IndexOf(_views.GameplayCameras, camera);
            if (cameraIndex < 0 || (_isLan && cameraIndex != 0)) return;
            int seat = _isLan ? _localSeat : cameraIndex;
            foreach (Renderer renderer in _occluders)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                bool covers = Covers(camera, renderer.bounds, _views.Players[seat].Root.position + Vector3.up);
                for (int i = 0; !covers && i < _state.Balls.Length; i++)
                    if (_state.Balls[i].Active) covers = Covers(camera, renderer.bounds, _views.Balls[i].Root.position);
                if (!covers) continue;
                Material[] original = renderer.sharedMaterials, temporary = new Material[original.Length];
                for (int i = 0; i < original.Length; i++) temporary[i] = Fade(original[i]);
                _restored[renderer] = original;
                renderer.sharedMaterials = temporary;
            }
        }
        public void EndCameraOcclusion(Camera camera) => RestoreOcclusion();
        void RestoreOcclusion()
        {
            foreach (var item in _restored) if (item.Key != null) item.Key.sharedMaterials = item.Value;
            _restored.Clear();
        }
        static bool Covers(Camera camera, Bounds bounds, Vector3 target)
        {
            Vector3 viewport = camera.WorldToViewportPoint(target);
            if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f) return false;
            Ray ray = camera.ViewportPointToRay(viewport);
            return bounds.IntersectRay(ray, out float distance) && distance < Vector3.Dot(target - ray.origin, ray.direction) - .15f;
        }
        Material Fade(Material original)
        {
            if (original == null) return null;
            if (!_faded.TryGetValue(original, out Material material))
            {
                material = new Material(original) { name = original.name + " (camera-only fade)", hideFlags = HideFlags.HideAndDontSave };
                _faded.Add(original, material);
            }
            material.CopyPropertiesFromMaterial(original);
            string colorProperty = material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
            if (material.HasProperty(colorProperty)) { Color color = material.GetColor(colorProperty); color.a *= .2f; material.SetColor(colorProperty, color); }
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent"); material.renderQueue = (int)RenderQueue.Transparent;
            return material;
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

        public void Render(DashArenaState state, float interpolation)
        {
            if (state == null || _state == null) return;
            if (state.Epoch != _state.Epoch) throw new InvalidOperationException("BindState must initialize a new epoch.");
            _state = state;
            if (_state.Phase == MatchState.Kickoff && _previousPhase != MatchState.Kickoff) SnapAllPoses();
            if (_state.Tick < _lastRenderedTick) ReconcileState(state);
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
                if (!_isLan) UpdateCamera(_views.GameplayCameras[seat], shown, seat);
                else if (seat == _localSeat) UpdateCamera(_views.GameplayCameras[0], shown, seat);
            }
            for (int i = 0; i < _views.Balls.Length; i++)
            {
                BallView view = _views.Balls[i];
                bool active = i < _state.Balls.Length && _state.Balls[i].Active;
                view.Root.gameObject.SetActive(active);
                if (!active) { view.Trail.positionCount = 0; _ballPaths[i].Clear(); continue; }
                DashBallState ball = _state.Balls[i];
                Vector2 shownBall = _previousBallActive[i] ? Vector2.Lerp(_previousBalls[i], ball.Position, t) : ball.Position;
                view.Root.position = Ground(shownBall, _state.Rules.BallRadius);
                bool danger = ball.Velocity.magnitude >= _state.Rules.DangerousBallSpeed;
                if (newTick) AddTrail(view.Trail, _ballPaths[i], Ground(shownBall, .12f), ball.Velocity.sqrMagnitude > 1f, danger ? 16 : 7, danger ? .26f : .06f);
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
                view.Debris.localScale = _debrisRestScale[i] * (brokenAge < 60 ? 1f : Mathf.Clamp01((90f - brokenAge) / 30f));
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
            _previousPhase = _state.Phase;
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

        public void ClearTransientPresentation()
        {
            RestoreOcclusion();
            if (!_captured) return;
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
                _views.Walls[i].Debris.localScale = _debrisRestScale[i];
            }
            for (int i = 0; i < _pillarHitTicks.Length; i++)
            {
                _pillarHitTicks[i] = -100000;
                _views.Pillars[i].Root.localScale = _pillarRestScale[i];
                _views.Pillars[i].Bell.gameObject.SetActive(false);
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

        // Existing authored scenes predate explicit goal references. Resolve only within
        // this production root, never across the scene or inside retired legacy roots.
        void ResolveGoalGeometry()
        {
            if (_blueGoalGeometry != null && _blueGoalGeometry.Length > 0 &&
                _orangeGoalGeometry != null && _orangeGoalGeometry.Length > 0) return;
            var blue = new List<Renderer>();
            var orange = new List<Renderer>();
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
            {
                for (Transform node = renderer.transform; node != null && node != transform; node = node.parent)
                {
                    if (IsGoalNode(node.name, 1)) { blue.Add(renderer); break; }
                    if (IsGoalNode(node.name, -1)) { orange.Add(renderer); break; }
                }
            }
            if (_blueGoalGeometry == null || _blueGoalGeometry.Length == 0) _blueGoalGeometry = blue.ToArray();
            if (_orangeGoalGeometry == null || _orangeGoalGeometry.Length == 0) _orangeGoalGeometry = orange.ToArray();
        }

        static bool IsGoalNode(string name, int sign) => name == "Goal mouth " + sign ||
            name == "Goal lintel " + sign || name.StartsWith("Goal post " + sign + " ", StringComparison.Ordinal);

        // Exposes the same authored geometry to projection/capture verification. It is
        // read-only and includes the whole entrance, not merely the goal centre.
        public Bounds GoalVisualBounds(int endSign)
        {
            if (endSign != -1 && endSign != 1) throw new ArgumentOutOfRangeException(nameof(endSign));
            if (TryGeometryBounds(endSign > 0 ? _blueGoalGeometry : _orangeGoalGeometry, out Bounds bounds)) return bounds;
            // Unnamed/custom minimal scenes remain usable. These dimensions match the
            // original authored posts/lintel and extend through the ground entrance.
            float halfHeight = _state != null ? _state.Rules.HalfHeight : 36f;
            float goalWidth = _state != null ? _state.Rules.GoalWidth : 3.4f;
            return new Bounds(new Vector3(0f, 1.2375f, endSign * (halfHeight + .65f)),
                new Vector3(goalWidth + .72f, 2.725f, 1.5f));
        }

        public Rect GoalSafeViewport(Camera camera) => _hud != null
            ? _hud.GetGoalSafeViewport(camera) : new Rect(.04f, .09f, .92f, .53f);

        static bool TryGeometryBounds(Renderer[] geometry, out Bounds bounds, bool includeInactive = false)
        {
            bounds = default;
            bool found = false;
            if (geometry == null) return false;
            foreach (Renderer renderer in geometry)
            {
                if (renderer == null || renderer is LineRenderer || !renderer.enabled ||
                    (!includeInactive && !renderer.gameObject.activeInHierarchy)) continue;
                Bounds next = renderer.bounds;
                if (next.size.sqrMagnitude < .0001f) continue;
                if (found) bounds.Encapsulate(next); else { bounds = next; found = true; }
            }
            return found;
        }

        void UpdateCamera(Camera camera, Vector2 at, int seat)
        {
            camera.orthographicSize = _followHalfHeight;
            camera.transform.rotation = FollowRotation;
            Vector3 up = camera.transform.up;
            float xMargin = Mathf.Min(_state.Rules.HalfWidth - 1f, _followHalfHeight * Mathf.Max(.1f, camera.aspect));
            // Orthographic height is measured in the tilted camera plane, not along
            // the ground. The old 1.2 approximation clipped the north goal even nearby.
            float yMargin = Mathf.Min(_state.Rules.HalfHeight - 1f, _followHalfHeight / up.z);
            Vector3 focus = Ground(new Vector2(Mathf.Clamp(at.x, -_state.Rules.HalfWidth + xMargin, _state.Rules.HalfWidth - xMargin),
                Mathf.Clamp(at.y, -_state.Rules.HalfHeight + yMargin, _state.Rules.HalfHeight - yMargin)), 0f);
            float endDistance = _state.Rules.HalfHeight - Mathf.Abs(at.y);
            if (endDistance < GoalBlendDistance)
            {
                Bounds goal = GoalVisualBounds(at.y >= 0f ? 1 : -1);
                float corridor = Mathf.Max(_state.Rules.GoalWidth * .5f, goal.extents.x) + _state.Rules.PlayerRadius;
                float lateral = Mathf.Abs(at.x - goal.center.x);
                float blend = Mathf.SmoothStep(0f, 1f, (GoalBlendDistance - endDistance) / (GoalBlendDistance - GoalApproachDistance)) *
                    (1f - Mathf.SmoothStep(0f, 1f, (lateral - corridor) / 4f));
                if (blend > 0f)
                {
                    Bounds subjects = goal;
                    // Keep the player's ground position and full authored body in view.
                    // Inactive-on-death body geometry still has its ground-based bounds:
                    // hiding the robot must not change framing or follow a fall height.
                    if (!TryGeometryBounds(_robotGeometry[seat], out Bounds robot, true))
                        robot = new Bounds(Ground(at, .95f), new Vector3(_state.Rules.PlayerRadius * 2f, 1.9f, _state.Rules.PlayerRadius * 2f));
                    subjects.Encapsulate(robot); subjects.Encapsulate(Ground(at, 0f));
                    Rect safe = GoalSafeViewport(camera);
                    float verticalMin = Vector3.Dot(subjects.center, up) - Vector3.Dot(subjects.extents, up);
                    float verticalMax = Vector3.Dot(subjects.center, up) + Vector3.Dot(subjects.extents, up);
                    float fullHeight = 2f * _followHalfHeight, fullWidth = fullHeight * camera.aspect;
                    Vector3 fitted = new Vector3(
                        ClampFraming(focus.x, subjects.max.x - (safe.xMax - .5f) * fullWidth,
                            subjects.min.x - (safe.xMin - .5f) * fullWidth), 0f,
                        ClampFraming(focus.z, (verticalMax - (safe.yMax - .5f) * fullHeight) / up.z,
                            (verticalMin - (safe.yMin - .5f) * fullHeight) / up.z));
                    // Spatial smoothstep is independent of framerate and has no camera
                    // history to drag across rollback, teleport, respawn or a new epoch.
                    focus = Vector3.Lerp(focus, fitted, blend);
                }
            }
            camera.transform.position = focus + FollowOffset;
        }

        // Extreme author edits can make a subject larger than the unchanged lens. Centre
        // that impossible interval rather than inverting Mathf.Clamp or changing zoom.
        static float ClampFraming(float value, float minimum, float maximum) => minimum <= maximum
            ? Mathf.Clamp(value, minimum, maximum) : (minimum + maximum) * .5f;

        static Transform Limb(RobotView view, int index) => index == 0 ? view.LeftArm : index == 1 ? view.RightArm : index == 2 ? view.LeftFoot : view.RightFoot;
        public static Vector3 Ground(Vector2 point, float height) => new Vector3(point.x, height, point.y);
        static string Seconds(int ticks) => (ticks / 60f).ToString("0.0");

    }
}
