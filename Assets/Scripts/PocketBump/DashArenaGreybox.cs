using System;
using System.Collections.Generic;
using UnityEngine;

namespace PocketBump
{
    // Isolated local playtest harness. This is not the approved robot-art sample, the
    // production scene controller, or a LAN implementation. All geometry is display only.
    [ExecuteAlways]
    public sealed class DashArenaGreybox : MonoBehaviour
    {
        [Serializable]
        struct AuthoredTint
        {
            public Renderer Renderer;
            public Color Color;
        }

        [SerializeField, Range(1, 8), Tooltip("Applied only when Restart preview starts a new match.")] int _ballCount = 1;
        [SerializeField] bool _automaticTicking = true;
        [SerializeField] bool _showHud = true;
        [SerializeField, Range(8f, 24f)] float _viewHalfHeight = 13f;
        [SerializeField] Transform[] _players;
        [SerializeField] Transform[] _balls;
        [SerializeField] Transform[] _walls;
        [SerializeField] Transform[] _repairOutlines;
        [SerializeField] Transform[] _pickups;
        [SerializeField] Camera[] _cameras;
        [SerializeField] Transform _gate;
        [SerializeField] AuthoredTint[] _authoredTints;

        readonly bool[] _dashEdges = new bool[2];
        readonly Vector2[] _previousPlayers = new Vector2[2];
        readonly string[] _messages = new string[2];
        readonly float[] _messageUntil = new float[2];
        readonly Color[] _playerColors = { new Color(0.08f, 0.65f, 0.79f), new Color(1f, 0.42f, 0.1f) };
        Vector2[] _previousBalls;
        ArenaRulesProfile _rules;
        DashArenaState _state;
        MaterialPropertyBlock _properties;
        Font _font;
        GUIStyle _labelStyle;
        float _tickRemainder;

        public DashArenaState State => _state;
        public IReadOnlyList<Camera> PreviewCameras => _cameras;
        public bool AutomaticTicking { get => _automaticTicking; set => _automaticTicking = value; }
        public bool ShowHud { get => _showHud; set => _showHud = value; }
        public int BallCountForNextMatch
        {
            get => Mathf.Clamp(_ballCount, 1, ArenaRulesProfile.MaximumBalls);
            set => _ballCount = Mathf.Clamp(value, 1, ArenaRulesProfile.MaximumBalls);
        }
        public string MessageForSeat(int seat)
        {
            if (seat < 0 || seat > 1) throw new ArgumentOutOfRangeException(nameof(seat));
            return _messages[seat];
        }
        ArenaRulesProfile Rules
        {
            get
            {
                // Inspector changes are pending settings, never mid-match rule changes.
                if (_state != null) return _state.Rules;
                if (_rules == null || _rules.BallCount != BallCountForNextMatch)
                    _rules = ArenaRulesProfile.CreateGreybox(8, BallCountForNextMatch);
                return _rules;
            }
        }
        const float TickDt = 1f / 60f;

        void OnEnable()
        {
            ApplyAuthoredTints();
            if (!Application.isPlaying) return;
            if (_players == null || _players.Length != 2) BuildAuthoredPreview();
            if (_state == null) RestartPreview();
        }

        void OnDisable()
        {
            _dashEdges[0] = _dashEdges[1] = false;
            _tickRemainder = 0f;
        }

        void OnDestroy()
        {
            if (_font != null)
            {
                if (Application.isPlaying) Destroy(_font);
                else DestroyImmediate(_font);
            }
        }

        // Explicit authoring entry only. It never clears/replaces an existing hierarchy.
        public void BuildAuthoredPreview()
        {
            if (transform.Find("GreyboxWorld") != null)
                throw new InvalidOperationException("Greybox already authored; refusing to replace its hierarchy.");
            var world = new GameObject("GreyboxWorld").transform;
            world.SetParent(transform, false);
            var tints = new List<AuthoredTint>();
            ArenaRulesProfile rules = Rules;
            Color ground = new Color(0.69f, 0.72f, 0.7f);
            Color solid = new Color(0.38f, 0.43f, 0.46f);
            Color danger = new Color(0.85f, 0.2f, 0.18f);
            Make(world, "Ground - provisional 48 x 72", PrimitiveType.Cube,
                new Vector3(0f, -0.2f, 0f), new Vector3(rules.Width, 0.4f, rules.Height), ground, tints);

            float goalHalf = rules.GoalWidth * 0.5f;
            float deathHalf = rules.DeathOpening * 0.5f;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Make(world, "GoalBoundary " + sign + " " + side, PrimitiveType.Cube,
                        new Vector3(side * (rules.HalfWidth + goalHalf) * 0.5f, 0.4f, sign * (rules.HalfHeight + rules.WallThickness * 0.5f)),
                        new Vector3(rules.HalfWidth - goalHalf, 0.8f, rules.WallThickness), solid, tints);
                    Make(world, "SideBoundary " + sign + " " + side, PrimitiveType.Cube,
                        new Vector3(sign * (rules.HalfWidth + rules.WallThickness * 0.5f), 0.4f, side * (rules.HalfHeight + deathHalf) * 0.5f),
                        new Vector3(rules.WallThickness, 0.8f, rules.HalfHeight - deathHalf), solid, tints);
                }
                Make(world, "DeathOpening " + sign, PrimitiveType.Cube,
                    new Vector3(sign * (rules.HalfWidth + 1f), -0.35f, 0f),
                    new Vector3(2f, 0.15f, rules.DeathOpening), danger, tints);
                Make(world, "Goal " + sign, PrimitiveType.Cube,
                    new Vector3(0f, -0.08f, sign * (rules.HalfHeight + 0.8f)),
                    new Vector3(rules.GoalWidth, 0.12f, 1.6f), _playerColors[sign > 0 ? 0 : 1], tints);
            }

            _gate = Make(world, "KickoffGate", PrimitiveType.Cube, new Vector3(0f, 0.3f, 0f),
                new Vector3(rules.Width, 0.6f, rules.KickoffHalfDepth * 2f), new Color(0.95f, 0.8f, 0.18f), tints);
            for (int i = 0; i < rules.Pillars.Count; i++)
            {
                ArenaPillarDefinition p = rules.Pillars[i];
                Make(world, "Pillar " + p.Id, PrimitiveType.Cylinder, Ground(p.Position, 0.6f),
                    new Vector3(p.Radius * 2f, 0.6f, p.Radius * 2f), solid, tints);
            }
            _walls = new Transform[rules.Walls.Count];
            for (int i = 0; i < rules.Walls.Count; i++)
            {
                ArenaWallDefinition wall = rules.Walls[i];
                _walls[i] = Make(world, "BreakableWall " + wall.Id, PrimitiveType.Cube, Ground(wall.Position, 0.5f),
                    new Vector3(wall.HalfSize.x * 2f, 1f, wall.HalfSize.y * 2f), new Color(0.66f, 0.48f, 0.31f), tints);
            }
            BuildRepairOutlines(world, tints);
            _players = new Transform[2];
            for (int seat = 0; seat < 2; seat++)
            {
                _players[seat] = Make(world, "Player " + (seat + 1) + " - placeholder", PrimitiveType.Sphere,
                    Ground(rules.SpawnForSeat(seat), rules.PlayerRadius), Vector3.one * (rules.PlayerRadius * 2f),
                    _playerColors[seat], tints);
                Make(_players[seat], "FacingMarker", PrimitiveType.Cube,
                    new Vector3(0f, 0.15f, 0.42f), new Vector3(0.2f, 0.16f, 0.24f), Color.white, tints);
            }
            _balls = new Transform[ArenaRulesProfile.MaximumBalls];
            AddMissingBallVisuals(world, tints);
            _pickups = new Transform[rules.PickupLimit];
            for (int i = 0; i < _pickups.Length; i++)
            {
                _pickups[i] = Make(world, "PickupSlot " + i, PrimitiveType.Cube, new Vector3(0f, 0.65f, 0f),
                    Vector3.one * 0.65f, Color.green, tints);
                _pickups[i].gameObject.SetActive(false);
            }

            _cameras = new Camera[2];
            for (int seat = 0; seat < 2; seat++)
            {
                var cameraObject = new GameObject("Greybox P" + (seat + 1) + " Camera");
                cameraObject.transform.SetParent(transform, false);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.15f, 0.18f);
                camera.orthographic = true;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 160f;
                camera.rect = new Rect(seat * 0.5f, 0f, 0.5f, 1f);
                _cameras[seat] = camera;
                if (seat == 0) cameraObject.AddComponent<AudioListener>();
                UpdateCamera(seat, rules.SpawnForSeat(seat));
            }
            var lamp = new GameObject("Greybox light");
            lamp.transform.SetParent(transform, false);
            Light light = lamp.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.None;
            lamp.transform.rotation = Quaternion.Euler(50f, -25f, 0f);
            _authoredTints = tints.ToArray();
            ApplyAuthoredTints();
        }

        static Transform Make(Transform parent, string name, PrimitiveType primitive, Vector3 position,
            Vector3 scale, Color color, List<AuthoredTint> tints)
        {
            GameObject go = GameObject.CreatePrimitive(primitive);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }
            Renderer renderer = go.GetComponent<Renderer>();
            // Shared shader template is read-only; per-object color uses property blocks.
            Material template = Resources.Load<Material>("PocketBump/NeonLit");
            if (template != null) renderer.sharedMaterial = template;
            tints.Add(new AuthoredTint { Renderer = renderer, Color = color });
            return go.transform;
        }

        void BuildRepairOutlines(Transform world, List<AuthoredTint> tints)
        {
            _repairOutlines = new Transform[Rules.Walls.Count];
            Color pending = new Color(1f, 0.72f, 0.12f);
            for (int i = 0; i < Rules.Walls.Count; i++)
            {
                ArenaWallDefinition wall = Rules.Walls[i];
                var outline = new GameObject("RepairPending " + wall.Id).transform;
                outline.SetParent(world, false);
                outline.localPosition = Ground(wall.Position, 0.045f);
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Make(outline, "Edge X " + sign, PrimitiveType.Cube,
                        new Vector3(0f, 0f, sign * wall.HalfSize.y),
                        new Vector3(wall.HalfSize.x * 2f, 0.07f, 0.09f), pending, tints);
                    Make(outline, "Edge Z " + sign, PrimitiveType.Cube,
                        new Vector3(sign * wall.HalfSize.x, 0f, 0f),
                        new Vector3(0.09f, 0.07f, wall.HalfSize.y * 2f), pending, tints);
                }
                _repairOutlines[i] = outline;
                outline.gameObject.SetActive(false);
            }
        }

        void EnsureRepairOutlines()
        {
            if (_repairOutlines != null && _repairOutlines.Length == Rules.Walls.Count) return;
            Transform world = transform.Find("GreyboxWorld");
            if (world == null) throw new InvalidOperationException("Greybox world has not been authored.");
            // Compatibility for an already-saved early greybox, not a production-scene repair.
            var tints = _authoredTints != null ? new List<AuthoredTint>(_authoredTints) : new List<AuthoredTint>();
            BuildRepairOutlines(world, tints);
            _authoredTints = tints.ToArray();
            ApplyAuthoredTints();
        }

        // Explicit, incremental compatibility upgrade for the isolated greybox only.
        // Existing objects, transforms, materials and tint entries are preserved.
        public bool EnsureBallVisualPool()
        {
            Transform world = transform.Find("GreyboxWorld");
            if (world == null) throw new InvalidOperationException("Greybox world has not been authored.");
            var tints = _authoredTints != null ? new List<AuthoredTint>(_authoredTints) : new List<AuthoredTint>();
            if (!AddMissingBallVisuals(world, tints)) return false;
            _authoredTints = tints.ToArray();
            return true;
        }

        bool AddMissingBallVisuals(Transform world, List<AuthoredTint> tints)
        {
            if (_balls != null && _balls.Length > ArenaRulesProfile.MaximumBalls)
                throw new InvalidOperationException("Unexpected ball visual pool size; refusing to discard authored references.");
            bool changed = _balls == null || _balls.Length != ArenaRulesProfile.MaximumBalls;
            if (changed) Array.Resize(ref _balls, ArenaRulesProfile.MaximumBalls);
            ArenaRulesProfile rules = Rules;
            for (int i = 0; i < _balls.Length; i++)
            {
                if (_balls[i] != null) continue;
                // Recover an existing named slot before adding anything; never duplicate it.
                _balls[i] = world.Find("ScoringBall " + i);
                if (_balls[i] == null)
                {
                    Vector2 at = i < rules.BallCount ? rules.BallSpawn(i) : Vector2.zero;
                    _balls[i] = Make(world, "ScoringBall " + i, PrimitiveType.Sphere, Ground(at, rules.BallRadius),
                        Vector3.one * (rules.BallRadius * 2f), Color.white, tints);
                    _balls[i].gameObject.SetActive(_state == null ? i < rules.BallCount
                        : i < _state.Balls.Length && _state.Balls[i].Active);
                    Tint(_balls[i].GetComponent<Renderer>(), Color.white);
                }
                changed = true;
            }
            return changed;
        }

        // Explicit restart also supports a temporary editor preview scene used for a
        // real camera capture. Simply opening or inspecting the saved scene never runs it.
        public void RestartPreview()
        {
            if (_players == null || _players.Length != 2)
                throw new InvalidOperationException("Author the isolated preview before restarting it.");
            EnsureRepairOutlines();
            EnsureBallVisualPool();
            _rules = ArenaRulesProfile.CreateGreybox(8, BallCountForNextMatch);
            _state = DashArenaSimulation.Create(_rules, 1u);
            DashArenaSimulation.StartMatch(_state);
            _previousBalls = new Vector2[_state.Balls.Length];
            CapturePreviousPositions();
            _dashEdges[0] = _dashEdges[1] = false;
            _messages[0] = _messages[1] = "";
            _tickRemainder = 0f;
            RenderPreview(1f);
        }

        void Update()
        {
            if (!Application.isPlaying || !_automaticTicking || _state == null) return;
            _dashEdges[0] |= Input.GetKeyDown(KeyCode.K);
            _dashEdges[1] |= Input.GetKeyDown(KeyCode.Keypad2);
            _tickRemainder += Time.deltaTime;
            // Catch-up is bounded per rendered frame but leftover time is not discarded.
            for (int step = 0; _tickRemainder >= TickDt && step < 8; step++)
            {
                _tickRemainder -= TickDt;
                StepPreview(Sample(0), Sample(1));
            }
        }

        DashArenaInput Sample(int seat)
        {
            Vector2 move = seat == 0
                ? new Vector2((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0),
                    (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0))
                : new Vector2((Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0),
                    (Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.DownArrow) ? 1 : 0));
            bool edge = _dashEdges[seat];
            _dashEdges[seat] = false;
            return new DashArenaInput { Move = move.sqrMagnitude > 0f ? move.normalized : Vector2.zero, DashPressed = edge };
        }

        // Also used by deterministic scene checks; never reads keyboard input here.
        public void StepPreview(DashArenaInput playerOne, DashArenaInput playerTwo)
        {
            if (_state == null) throw new InvalidOperationException("Preview has not started.");
            CapturePreviousPositions();
            IReadOnlyList<DashArenaEvent> events = DashArenaSimulation.Step(_state, playerOne, playerTwo);
            for (int i = 0; i < events.Count; i++) ShowEvent(events[i]);
        }

        void CapturePreviousPositions()
        {
            for (int seat = 0; seat < 2; seat++) _previousPlayers[seat] = _state.Players[seat].Position;
            for (int i = 0; i < _state.Balls.Length; i++) _previousBalls[i] = _state.Balls[i].Position;
        }

        void LateUpdate()
        {
            if (Application.isPlaying && _state != null)
                RenderPreview(_automaticTicking ? Mathf.Clamp01(_tickRemainder / TickDt) : 1f);
        }

        public void RenderPreview(float interpolation)
        {
            if (_state == null) return;
            float t = Mathf.Clamp01(interpolation);
            for (int seat = 0; seat < 2; seat++)
            {
                DashPlayerState player = _state.Players[seat];
                Vector2 before = _previousPlayers[seat];
                if ((player.Position - before).sqrMagnitude > 9f) before = player.Position;
                Vector2 shown = Vector2.Lerp(before, player.Position, t);
                _players[seat].gameObject.SetActive(player.Alive);
                _players[seat].position = Ground(shown, Rules.PlayerRadius);
                Vector3 facing = Ground(player.Facing, 0f);
                _players[seat].rotation = Quaternion.LookRotation(facing.sqrMagnitude > 0.0001f ? facing : Vector3.forward, Vector3.up);
                Color color = player.StunTicks > 0 ? new Color(1f, 0.83f, 0.2f) : _playerColors[seat];
                if (player.PurpleTicks > 0) color = Color.Lerp(color, new Color(0.65f, 0.2f, 1f), 0.55f);
                else if (player.GreenTicks > 0) color = Color.Lerp(color, Color.green, 0.4f);
                if (player.DashTicks > 0) color *= 0.72f;
                Tint(_players[seat].GetComponent<Renderer>(), color);
                UpdateCamera(seat, shown);
            }
            for (int i = 0; i < _balls.Length; i++)
            {
                bool active = i < _state.Balls.Length && _state.Balls[i].Active;
                _balls[i].gameObject.SetActive(active);
                if (!active) continue;
                Vector2 before = _previousBalls[i];
                if ((_state.Balls[i].Position - before).sqrMagnitude > 9f) before = _state.Balls[i].Position;
                _balls[i].position = Ground(Vector2.Lerp(before, _state.Balls[i].Position, t), Rules.BallRadius);
            }
            for (int i = 0; i < _walls.Length; i++)
            {
                _walls[i].gameObject.SetActive(!_state.Walls[i].Broken);
                _repairOutlines[i].gameObject.SetActive(_state.Walls[i].Broken && _state.Walls[i].RepairPending);
            }
            for (int i = 0; i < _pickups.Length; i++)
            {
                DashPickupState pickup = _state.Pickups[i];
                _pickups[i].gameObject.SetActive(pickup.Active);
                _pickups[i].position = Ground(pickup.Position, 0.6f);
                Color color = pickup.Kind == DashPickupKind.Green ? Color.green
                    : pickup.Kind == DashPickupKind.Blue ? new Color(0.12f, 0.35f, 1f) : new Color(0.7f, 0.18f, 0.9f);
                Tint(_pickups[i].GetComponent<Renderer>(), color);
            }
            _gate.gameObject.SetActive(_state.GateClosed);
        }

        void UpdateCamera(int seat, Vector2 position)
        {
            Camera camera = _cameras[seat];
            camera.orthographicSize = _viewHalfHeight;
            float horizontalMargin = Mathf.Min(Rules.HalfWidth - 1f, _viewHalfHeight * Mathf.Max(0.1f, camera.aspect));
            float verticalMargin = Mathf.Min(Rules.HalfHeight - 1f, _viewHalfHeight * 1.2f);
            Vector3 focus = Ground(new Vector2(
                Mathf.Clamp(position.x, -Rules.HalfWidth + horizontalMargin, Rules.HalfWidth - horizontalMargin),
                Mathf.Clamp(position.y, -Rules.HalfHeight + verticalMargin, Rules.HalfHeight - verticalMargin)), 0f);
            camera.transform.position = focus + new Vector3(0f, 20f, -12f);
            camera.transform.rotation = Quaternion.LookRotation(focus - camera.transform.position, Vector3.up);
        }

        void ShowEvent(DashArenaEvent item)
        {
            if (item.Seat < 0 || item.Seat >= 2) return;
            string message = null;
            switch (item.Kind)
            {
                case DashArenaEventKind.Pickup:
                    // Blue emits its result before Pickup; do not replace that success/
                    // failure explanation with a generic "attempting" notification.
                    if (item.PickupKind == DashPickupKind.Blue) break;
                    message = item.PickupKind == DashPickupKind.Green ? "GREEN: x1.5 impact strength; refreshes for 5s"
                        : item.PickupKind == DashPickupKind.Purple ? "PURPLE: knockback + stun immunity for 5s; can still fall"
                        : "PICKUP";
                    break;
                case DashArenaEventKind.Teleport: message = "BLUE: safe teleport; velocity reset"; break;
                case DashArenaEventKind.TeleportFailed: message = "BLUE: no safe destination; pickup consumed"; break;
                case DashArenaEventKind.Stunned: message = "晕 / STUN: 0.6s; inertia still applies"; break;
                case DashArenaEventKind.PillarHit:
                    if (_state.Players[item.Seat].PurpleTicks > 0)
                        message = "PILLAR CONTACT: purple armor prevents stun; pillar is still solid";
                    break;
                case DashArenaEventKind.Fell: message = "FELL: no points; respawn in 3s when safe"; break;
                case DashArenaEventKind.WallBroken: message = "WALL BROKEN: repairs after 10s when clear"; break;
            }
            if (message == null) return;
            _messages[item.Seat] = message;
            _messageUntil[item.Seat] = Time.unscaledTime + 2.5f;
        }

        void ApplyAuthoredTints()
        {
            if (_authoredTints == null) return;
            foreach (AuthoredTint tint in _authoredTints) Tint(tint.Renderer, tint.Color);
        }

        void Tint(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            if (_properties == null) _properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_properties);
            _properties.SetColor("_BaseColor", color);
            _properties.SetColor("_Color", color);
            _properties.SetColor("_EmissionColor", Color.black);
            renderer.SetPropertyBlock(_properties);
        }

        static Vector3 Ground(Vector2 position, float height) => new Vector3(position.x, height, position.y);

        void OnGUI()
        {
            if (!Application.isPlaying || !_showHud || _state == null) return;
            EnsureGui();
            float half = Screen.width * 0.5f;
            DrawPanel(new Rect(0f, 0f, Screen.width, 62f));
            GUI.Label(new Rect(10f, 3f, Screen.width - 20f, 24f),
                "PROVISIONAL GREYBOX / LOCAL ONLY / NOT FINAL ART   P1 " + _state.Scores[0] + " : " + _state.Scores[1] + " P2", _labelStyle);
            GUI.Label(new Rect(10f, 29f, Screen.width - 150f, 30f),
                _state.Phase + (_state.PhaseTicks > 0 ? "  " + Seconds(_state.PhaseTicks) + "s" : "") +
                " | Dash " + Rules.DashSpeed.ToString("0.0") + " u/s, " + (Rules.DashSpeed * 0.3f).ToString("0.0") + "u unobstructed; tuning pending", _labelStyle);
            if (GUI.Button(new Rect(Screen.width - 122f, 30f, 112f, 25f), "Restart preview")) RestartPreview();
            DrawPanel(new Rect(half - 1f, 62f, 2f, Screen.height - 62f));
            for (int seat = 0; seat < 2; seat++)
            {
                GUI.BeginGroup(new Rect(seat * half, 64f, half, Screen.height - 64f));
                DashPlayerState p = _state.Players[seat];
                DrawPanel(new Rect(6f, 0f, half - 12f, 94f));
                string action = !p.Alive ? "RESPAWN " + Seconds(p.RespawnTicks) + "s / wait until safe"
                    : p.StunTicks > 0 ? "晕 / STUN " + Seconds(p.StunTicks) + "s"
                    : p.DashTicks > 0 ? "DASH ACTIVE"
                    : p.DashCooldownTicks > 0 ? "DASH COOLDOWN " + Seconds(p.DashCooldownTicks) + "s" : "DASH READY";
                GUI.Label(new Rect(12f, 4f, half - 24f, 24f), seat == 0 ? "P1  WASD + K" : "P2  ARROWS + NUM2", _labelStyle);
                GUI.Label(new Rect(12f, 30f, half - 24f, 24f), action, _labelStyle);
                GUI.Label(new Rect(12f, 56f, half - 24f, 30f),
                    "GREEN x1.5: " + Seconds(p.GreenTicks) + "s   PURPLE: " + Seconds(p.PurpleTicks) + "s", _labelStyle);
                if (_messageUntil[seat] > Time.unscaledTime)
                {
                    DrawPanel(new Rect(6f, 102f, half - 12f, 58f));
                    GUI.Label(new Rect(12f, 106f, half - 24f, 52f), _messages[seat], _labelStyle);
                }
                for (int i = 0; i < _state.Balls.Length; i++)
                {
                    if (!_state.Balls[i].Active) continue;
                    Vector2 delta = _state.Balls[i].Position - p.Position;
                    Vector3 screen = _cameras[seat].WorldToViewportPoint(Ground(_state.Balls[i].Position, 0f));
                    if (screen.x < 0.04f || screen.x > 0.96f || screen.y < 0.04f || screen.y > 0.96f)
                        GUI.Label(new Rect(12f, Screen.height - 102f, half - 24f, 30f),
                            "BALL " + (Mathf.Abs(delta.x) > Mathf.Abs(delta.y) ? (delta.x > 0 ? "EAST >" : "< WEST") : (delta.y > 0 ? "NORTH ^" : "SOUTH v")) +
                            "  " + delta.magnitude.ToString("0.0") + "u", _labelStyle);
                    break;
                }
                if (p.Alive && p.StunTicks > 0)
                {
                    Vector3 screen = _cameras[seat].WorldToScreenPoint(_players[seat].position + Vector3.up * 1.4f);
                    float localX = Mathf.Clamp(screen.x - seat * half - 25f, 4f, half - 104f);
                    float localY = Mathf.Clamp(Screen.height - screen.y - 80f, 0f, Screen.height - 100f);
                    GUI.Label(new Rect(localX, localY, 100f, 32f), "晕 / STUN", _labelStyle);
                }
                GUI.EndGroup();
            }
        }

        static string Seconds(int ticks) => (ticks / 60f).ToString("0.0");

        void EnsureGui()
        {
            if (_labelStyle == null)
            {
                _font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 20);
                _labelStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, font = _font };
                _labelStyle.normal.textColor = Color.white;
            }
            _labelStyle.fontSize = Mathf.Clamp(Screen.width / 88, 12, 20);
        }

        static void DrawPanel(Rect area)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0.03f, 0.05f, 0.07f, 0.86f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        void OnDrawGizmosSelected()
        {
            if (Application.isPlaying) return;
            ArenaRulesProfile rules = Rules;
            Gizmos.color = new Color(0.8f, 0.8f, 0.8f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(rules.Width, 0.05f, rules.Height));
            for (int i = 0; i < rules.Pillars.Count; i++)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(Ground(rules.Pillars[i].Position, 0f), rules.Pillars[i].Radius);
            }
            for (int seat = 0; seat < 2; seat++)
            {
                Gizmos.color = _playerColors[seat];
                Gizmos.DrawWireSphere(Ground(rules.SpawnForSeat(seat), 0f), rules.PlayerRadius);
            }
        }
    }
}
