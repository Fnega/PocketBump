using System;
using System.Net;
using UnityEngine;

// 比赛总调度器：连接输入、确定性模拟、UDP/回滚会话、场景表现与 HUD。
// 它负责“何时推进和如何展示”，具体物理与计分规则集中在 MatchSim 中。
namespace PocketBump
{
    public enum MatchState
    {
        Pregame,
        Kickoff,
        Live,
        Scored,
        MatchOver
    }

    public sealed class MatchDirector : MonoBehaviour
    {
        public MatchSettings Settings = new MatchSettings();
        public Arena Arena;
        public PlayerMotor PlayerOne;
        public PlayerMotor PlayerTwo;
        public MatchHud Hud;

        public MatchSimState Sim { get; private set; }
        public MatchState State => Sim != null ? Sim.Phase : MatchState.Pregame;
        public int ScoreP1 => Sim != null ? Sim.ScoreP1 : 0;
        public int ScoreP2 => Sim != null ? Sim.ScoreP2 : 0;
        public bool NetworkPaused => _networkPaused;
        public LanMatchLifecycle Lifecycle => _lifecycle;
        public int MatchId => _matchId;
        public int MatchEpoch => _lifecycle != null ? _lifecycle.Epoch : 0;
        public int LocalSeat => _localSeat;
        public bool ReadyForNetplay => _handshakeDone && !_aborting && _lifecycle != null;
        public int ConfirmedStateTick => _rollback != null ? _rollback.ConfirmedStateTick : -1;

        public bool TryGetConfirmedEvents(int tick, out SimEvents events)
        {
            events = default;
            return _rollback != null && _rollback.TryGetConfirmedEvents(tick, out events);
        }

        public bool TryGetConfirmedState(int tick, out MatchSimState state)
        {
            state = null;
            return _rollback != null && _rollback.TryGetConfirmedState(tick, out state);
        }

        readonly TickInputSampler _sampler = new TickInputSampler();
        MatchSimState _from;
        MatchSimState _to;
        RollbackSession _rollback;
        LanUdpPeer _lan;
        LanHandshake _handshake;
        LanMatchLifecycle _lifecycle;
        PlayKind _kind;
        int _localSeat;
        bool _initialized;
        bool _handshakeDone;
        bool _aborting;
        bool _networkPaused;
        float _lastPacketTime;
        float _handshakeDeadline;
        float _handshakeFailureDeadline = -1f;
        float _handshakeGraceUntil;
        int _matchId;
        int _lastAck = -1;
        string _statusMessage;
        int _newestLocalInput = -1;
        float _nextHistoryRetry;
        float _resultConfirmedAt = -1f;
        LanEpochDiagnosticSummary _epochSummary;
        readonly byte[] _inputHistory = new byte[NetplayProtocol.InputHistoryLength];
        readonly LanDiagnosticSnapshot _diagnostics = new LanDiagnosticSnapshot();

        public LanDiagnosticSnapshot Diagnostics
        {
            get
            {
                _diagnostics.SimulationTick = Sim != null ? Sim.Tick : -1;
                _diagnostics.MatchId = _matchId;
                _diagnostics.Epoch = MatchEpoch;
                _diagnostics.LifecyclePhase = _lifecycle != null ? _lifecycle.Phase.ToString() : "Handshake";
                _diagnostics.SettingsHash = NetplayProtocol.SettingsHash(Settings.pointsToWin, Settings.marbleCount);
                if (_lifecycle != null && _lifecycle.ResultConfirmed)
                {
                    NetFinishTuple finish = _lifecycle.ConfirmedFinish;
                    _diagnostics.FinishTick = finish.FinishTick;
                    _diagnostics.FinishChecksum = finish.Checksum;
                    _diagnostics.FinalScore0 = finish.Score0;
                    _diagnostics.FinalScore1 = finish.Score1;
                    _diagnostics.Winner = finish.Winner;
                }
                _diagnostics.LastRemoteTick = _rollback != null ? _rollback.NewestRemoteTick : -1;
                _diagnostics.LastAckTick = _lastAck;
                _diagnostics.ValidatedChecksums = _rollback != null ? _rollback.ValidatedChecksumCount : 0;
                _diagnostics.DesyncTick = _rollback != null ? _rollback.DesyncTick : -1;
                _diagnostics.DesyncLocalChecksum = _rollback != null ? _rollback.DesyncLocalChecksum : 0;
                _diagnostics.DesyncRemoteChecksum = _rollback != null ? _rollback.DesyncRemoteChecksum : 0;
                _diagnostics.LastAcceptedPacketAgeSeconds = _lastPacketTime > 0f
                    ? Time.realtimeSinceStartup - _lastPacketTime : -1f;
                // Abort 关闭 socket 后仍保留终止瞬间的传输证据，不让 HUD/报告读取清零。
                if (_lan != null)
                {
                    _diagnostics.LocalEndpoint = _lan.LocalEndPoint != null ? _lan.LocalEndPoint.ToString() : "";
                    _diagnostics.RemoteEndpoint = _lan.Remote != null ? _lan.Remote.ToString() : "";
                    _diagnostics.SendAttempts = _lan.SendAttempts;
                    _diagnostics.SentDatagrams = _lan.SentDatagrams;
                    _diagnostics.SendFailures = _lan.SendFailures;
                    _diagnostics.LastSocketError = _lan.LastSocketError;
                }
                return _diagnostics;
            }
        }

        public static MatchDirector Build(MatchSettings settings = null, bool startImmediately = true)
        {
            GameLayers.ApplyCollisionMatrix();
            var root = new GameObject("Match");
            var director = root.AddComponent<MatchDirector>();
            director.Arena = Arena.Build(root.transform);
            director.PlayerOne = CreatePlayer(root.transform, 0, ArenaLayout.PlayerOneSpawn, director.Arena.BounceMat);
            director.PlayerTwo = CreatePlayer(root.transform, 1, ArenaLayout.PlayerTwoSpawn, director.Arena.BounceMat);
            director.Hud = MatchHud.Build(root.transform);
            director.InitializeAuthored(settings ?? new MatchSettings(), startImmediately);
            return director;
        }

        public static PlayerMotor CreatePlayer(Transform parent, int index, Vector2 spawn, PhysicsMaterial2D bounce)
        {
            var go = new GameObject(index == 0 ? "PlayerOne" : "PlayerTwo");
            go.transform.SetParent(parent, false);
            go.transform.position = spawn;
            go.layer = GameLayers.Player;
            go.tag = "Player";
            go.transform.localScale = Vector3.one * (ArenaLayout.PlayerRadius * 2f);
            MeshFactory.AddSphere(go, VisualPalette.PlayerColor(index));
            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;
            col.enabled = false;
            col.sharedMaterial = bounce;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.mass = ArenaLayout.PlayerMass;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.simulated = false;
            var motor = go.AddComponent<PlayerMotor>();
            motor.ApplyPlayerIndex(index);
            go.AddComponent<PlayerPresentation>().Initialize(index);
            return motor;
        }

        public void InitializeAuthored(MatchSettings settings, bool startImmediately = true)
        {
            // 编辑器烘焙和运行时创建的场景都汇合到这里，避免产生两套初始化行为。
            if (_initialized) return;
            if (Arena == null || PlayerOne == null || PlayerTwo == null || Hud == null)
            {
                Debug.LogError("MatchDirector authored references are incomplete.", this);
                return;
            }

            _initialized = true;
            Settings = settings ?? new MatchSettings();
            Settings.Clamp();
            GameLayers.ApplyCollisionMatrix();
            // Unity FixedUpdate 与 MatchSim 的 60 Hz 对齐；模拟内部仍使用自己的固定 dt。
            Time.fixedDeltaTime = ArenaLayout.SimTickDt;
            // 禁用自动 Physics2D 推进，防止 Rigidbody 与自定义模拟同时改变玩法对象。
            Physics2D.simulationMode = SimulationMode2D.Script;
            Arena.ApplyArtTheme();
            UiFactory.ApplyTheme(Hud.transform);
            FrameCamera();
            PlayerOne.Director = this;
            PlayerTwo.Director = this;
            Sim = MatchSim.Create(Settings);
            _from = Sim.Clone();
            _to = Sim.Clone();
            _kind = GameSession.Ensure().Kind;
            _diagnostics.Role = _kind == PlayKind.HostLan ? "host" : _kind == PlayKind.JoinLan ? "join" : "local";
            _diagnostics.AddEvent("director-initialized");
            if (_kind != PlayKind.Local)
            {
                _handshakeDeadline = Time.realtimeSinceStartup + 15f;
                Hud.ShowBanner(_kind == PlayKind.HostLan ? "HOSTING LAN  WAITING FOR JOINER" : "JOINING LAN...");
                BeginLan();
            }
            else if (startImmediately)
            {
                MatchSim.StartMatch(Sim);
                _from = Sim.Clone();
                _to = Sim.Clone();
                _handshakeDone = true;
                SyncHud(default);
            }
        }

        public static void FrameCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }

            ApplyMatchCamera(cam);
            EnsureMatchLight();
        }

        public static void ApplyMatchCamera(Camera cam)
        {
            if (cam == null) return;
            cam.orthographic = false;
            cam.fieldOfView = ArenaLayout.CameraFov;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = ArenaLayout.CameraDistance + 40f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = VisualPalette.Tabletop;
            cam.transform.SetPositionAndRotation(
                new Vector3(0f, 0f, -ArenaLayout.CameraDistance),
                Quaternion.identity);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.22f, 0.28f, 0.34f);
        }

        public static void EnsureMatchLight()
        {
            Light light = FindObjectOfType<Light>();
            if (light == null)
            {
                var go = new GameObject("Directional Light");
                light = go.AddComponent<Light>();
            }

            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = Color.white;
            light.shadows = LightShadows.None;
            light.transform.rotation = Quaternion.Euler(28f, -32f, 0f);
        }

        public static bool HasWinner(int scoreP1, int scoreP2, int pointsToWin, out int winner)
        {
            winner = -1;
            // 达到目标分且领先至少 2 分才结束，避免 8:7 这类一分优势提前终局。
            int need = Mathf.Clamp(pointsToWin, MatchSettings.MinPointsToWin, MatchSettings.MaxPointsToWin);
            if (scoreP1 >= need && scoreP1 - scoreP2 >= 2)
            {
                winner = 0;
                return true;
            }

            if (scoreP2 >= need && scoreP2 - scoreP1 >= 2)
            {
                winner = 1;
                return true;
            }

            return false;
        }

        public void StartMatch()
        {
            if (_kind != PlayKind.Local) return;
            if (Sim == null) return;
            if (Sim.Phase != MatchState.Pregame && Sim.Phase != MatchState.MatchOver) return;
            MatchSim.StartMatch(Sim);
            _from = Sim.Clone();
            _to = Sim.Clone();
            SyncHud(default);
        }

        void Update()
        {
            if (!_initialized || _aborting) return;
            bool net = _kind != PlayKind.Local;
            // 按键边沿先在渲染帧锁存，防止短按完整落在两个 FixedUpdate 之间。
            if (!net || (_handshakeDone && Sim.Phase != MatchState.MatchOver))
                _sampler.LatchUpdate(net, _localSeat);
            else
                _sampler.Clear();
            if (net && !_handshakeDone)
                PumpHandshake();
            else if (net)
                PumpNet();
        }

        void FixedUpdate()
        {
            if (!_initialized || !_handshakeDone || _aborting || Sim == null) return;
            // A slow rendered frame can schedule more catch-up ticks than the rollback window.
            // Drain already-arrived input before each step, not only in the later Update.
            if (_kind != PlayKind.Local)
            {
                PumpNet();
                if (!_handshakeDone || _aborting || Sim == null) return;
            }
            if (Sim.Phase == MatchState.MatchOver) return;
            if (_lifecycle != null && !_lifecycle.CanAdvance(Sim.Tick)) return;

            _from.CopyFrom(_to);
            SimEvents events;
            if (_rollback != null)
            {
                // 联机只采样本机座位；远端座位由 RollbackSession 的真实或预测输入提供。
                PlayerInput local = _sampler.SampleNetplaySeat(_localSeat);
                int applyTick = Sim.Tick;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if (LanSmokeRunner.TrySampleInput(_localSeat, applyTick, Sim, out PlayerInput smokeInput))
                    local = smokeInput;
#endif
                events = _rollback.Advance(local);
                if (_rollback.LastRollbackTick >= 0)
                {
                    _diagnostics.RollbackCount++;
                    // 被推翻 Tick 的瞬时特效不属于新历史，必须随状态一起撤销。
                    GameFx.DiscardNewerThan(_rollback.LastRollbackTick);
                }
                if (_rollback.Status == RollbackStatus.InputHole)
                {
                    Abort("INPUT HOLE");
                    return;
                }

                if (_rollback.Status == RollbackStatus.Desync)
                {
                    Abort("DESYNC");
                    return;
                }

                SendInput(applyTick);
            }
            else
            {
                // 本地双人也走同一个 MatchSim，不维护另一套 Physics2D 玩法路径。
                events = MatchSim.Step(Sim, _sampler.SampleLocal(0), _sampler.SampleLocal(1));
            }

            _to.CopyFrom(Sim);
            PlayEvents(events, Sim.Tick - 1);
            SyncHud(events);
            if (events.MatchOver && _kind == PlayKind.Local)
                Hud.ShowResult(events.Winner);
            else if (_lifecycle != null)
                RefreshLifecycle();
        }

        void LateUpdate()
        {
            if (!_initialized || Sim == null) return;
            // 渲染帧在前后两个 Tick 状态之间插值，隐藏固定步长带来的位置跳跃。
            float t = Time.fixedDeltaTime <= 0f
                ? 1f
                : Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            if (_rollback != null && _rollback.LastRollbackTick >= 0)
                // 修正历史时立即显示权威结果，避免继续插值一条已经无效的轨迹。
                t = 1f;
            ApplyViews(t);
        }

        void OnDestroy()
        {
            if (_networkPaused)
                Time.timeScale = 1f;
            LeaveNetwork();
        }

        public void LeaveNetwork()
        {
            if (_lan != null)
            {
                _lan.Send(LanUdpPeer.EncodeGoodbye(_matchId, MatchEpoch));
                _lan.Dispose();
                _lan = null;
            }
            if (_networkPaused) Time.timeScale = 1f;
            _networkPaused = false;
            _handshakeDone = false;
            _handshake = null;
            _lifecycle = null;
            _rollback = null;
            _newestLocalInput = -1;
            _lastAck = -1;
            _resultConfirmedAt = -1f;
            _sampler.Clear();
        }

        public void RequestRematch()
        {
            if (_aborting || _lifecycle == null) return;
            _lifecycle.RequestRematch();
            RefreshLifecycle();
        }

        void ApplyViews(float t)
        {
            // View 只读取状态；Transform、动画和 Rigidbody 镜像不能反向成为规则依据。
            if (_from == null || _to == null) return;
            ApplyPlayer(PlayerOne, 0, t);
            ApplyPlayer(PlayerTwo, 1, t);
            Arena.SetGateOpen(!_to.GateClosed);
            Arena.EnsureMarblePool(_to.MarbleCountSetting);
            for (int i = 0; i < Arena.MarbleRoot.childCount; i++)
            {
                Transform child = Arena.MarbleRoot.GetChild(i);
                bool active = i < _to.Marbles.Length && _to.Marbles[i].Active;
                child.gameObject.SetActive(active);
                if (!active) continue;
                Vector2 pos = Vector2.Lerp(_from.Marbles[i].Position, _to.Marbles[i].Position, t);
                child.position = pos;
                var motor = child.GetComponent<MarbleMotor>();
                if (motor != null)
                    motor.ApplySim(_to.Marbles[i], PlayerForCarrier(_to.Marbles[i].Carrier));
            }
        }

        void ApplyPlayer(PlayerMotor motor, int index, float t)
        {
            Vector2 pos = Vector2.Lerp(_from.Players[index].Position, _to.Players[index].Position, t);
            motor.ApplySim(_to.Players[index], pos);
        }

        PlayerMotor PlayerForCarrier(int carrier)
        {
            if (carrier == 0) return PlayerOne;
            if (carrier == 1) return PlayerTwo;
            return null;
        }

        void PlayEvents(SimEvents events, int tick)
        {
            // MatchSim 只产出语义事件；这里才创建 Unity 特效，使表现对象远离可回滚规则层。
            if (events.Collected0)
                GameFx.Pulse(events.CollectAt0, VisualPalette.PlayerColor(0), 0.4f, 1.6f, 0.28f, tick, GameFxKind.Collect);
            if (events.Collected1)
                GameFx.Pulse(events.CollectAt1, VisualPalette.PlayerColor(1), 0.4f, 1.6f, 0.28f, tick, GameFxKind.Collect);
            if (events.Fired0)
                GameFx.Pulse(events.FireAt0, VisualPalette.Text, 0.5f, 2f, 0.32f, tick, GameFxKind.Fire);
            if (events.Fired1)
                GameFx.Pulse(events.FireAt1, VisualPalette.Text, 0.5f, 2f, 0.32f, tick, GameFxKind.Fire);
            if (events.DangerousImpact)
                GameFx.Pulse(events.ImpactAt, VisualPalette.Danger, 0.2f, 1.1f, 0.24f, tick, GameFxKind.Impact);
            if (events.Scored)
            {
                GameFx.Pulse(events.ScoreAt, VisualPalette.PlayerColor(events.Scorer), 0.3f, 2.4f, 0.42f, tick, GameFxKind.Score);
            }
        }

        void SyncHud(SimEvents events)
        {
            Hud.ShowScores(Sim.ScoreP1, Sim.ScoreP2);
            if (Sim.Phase == MatchState.Kickoff)
                Hud.ShowCountdown(Sim.PhaseTicks * ArenaLayout.SimTickDt);
            else
                Hud.ShowCountdown(-1f);

            if (!string.IsNullOrEmpty(_statusMessage))
                Hud.ShowBanner(_statusMessage);
            else if (Sim.Phase == MatchState.Pregame)
                Hud.ShowBanner(ControlBindings.PregameBanner());
            else if (events.Scored)
                Hud.ShowScoreEvent(events.Scorer, events.ScoreReason);
            else if (Sim.Phase == MatchState.Live || Sim.Phase == MatchState.Kickoff)
                Hud.ShowBanner("");
        }

        public void DebugPlacePlayer(int index, Vector2 position, Vector2 facing)
        {
            MatchSim.DebugPlacePlayer(Sim, index, position, facing);
            _from.CopyFrom(Sim);
            _to.CopyFrom(Sim);
            ApplyViews(1f);
        }

        public void DebugSetMarble(int index, Vector2 position, Vector2 velocity)
        {
            MatchSim.DebugSetMarble(Sim, index, position, velocity);
            _from.CopyFrom(Sim);
            _to.CopyFrom(Sim);
            ApplyViews(1f);
        }

        public bool DebugCollect(int player, int marble)
        {
            bool ok = MatchSim.DebugCollect(Sim, player, marble);
            _from.CopyFrom(Sim);
            _to.CopyFrom(Sim);
            ApplyViews(1f);
            return ok;
        }

        public bool DebugShoot(int player)
        {
            bool ok = MatchSim.DebugShoot(Sim, player);
            _from.CopyFrom(Sim);
            _to.CopyFrom(Sim);
            ApplyViews(1f);
            return ok;
        }

        void BeginLan()
        {
            // Host 固定为座位 0 并监听约定端口；Joiner 使用临时端口主动连接 Host。
            _lan = new LanUdpPeer();
            if (_kind == PlayKind.HostLan)
            {
                _localSeat = 0;
                _matchId = Guid.NewGuid().GetHashCode();
                if (_matchId == 0) _matchId = 1;
                if (!_lan.Bind(ArenaLayout.LanPort))
                {
                    Abort("COULD NOT BIND PORT");
                    return;
                }

                _handshake = new LanHandshake(
                    LanHandshakeRole.Host, Settings.pointsToWin, Settings.marbleCount, _matchId);
            }
            else
            {
                _localSeat = 1;
                if (!_lan.BindEphemeral())
                {
                    Abort("COULD NOT OPEN SOCKET");
                    return;
                }

                try
                {
                    _lan.SetRemote(GameSession.Instance.JoinAddress, ArenaLayout.LanPort);
                    _handshake = new LanHandshake(
                        LanHandshakeRole.Joiner,
                        Settings.pointsToWin,
                        Settings.marbleCount,
                        0,
                        _lan.Remote);
                }
                catch
                {
                    Abort("BAD ADDRESS");
                    return;
                }
            }

            _lastPacketTime = Time.realtimeSinceStartup;
            _diagnostics.AddEvent("socket-bound");
        }

        void PumpHandshake()
        {
            float now = Time.realtimeSinceStartup;
            if (_handshake != null && _handshake.IsFailed && _kind == PlayKind.HostLan &&
                _handshakeFailureDeadline >= 0f && now >= _handshakeFailureDeadline)
            {
                Abort(_handshake.FailureMessage ?? "LAN HANDSHAKE FAILED");
                return;
            }
            if (now > _handshakeDeadline)
            {
                Abort("LAN HANDSHAKE TIMEOUT");
                return;
            }

            if (_handshake != null && _handshake.TryBuildRetry(now, out byte[] retry))
                _lan.Send(retry);

            while (_lan != null && _lan.TryRecv(out byte[] data, out IPEndPoint from))
            {
                if (_handshake == null || !_handshake.Process(data, from, now, out byte[] response))
                {
                    _diagnostics.RejectedDatagrams++;
                    continue;
                }

                if (_handshake.RemoteEndpoint != null && !_lan.IsRemote(_handshake.RemoteEndpoint))
                    _lan.SetRemote(_handshake.RemoteEndpoint);
                if (response != null) _lan.SendTo(response, from);
                _lastPacketTime = now;
                _diagnostics.ReceivedDatagrams++;

                if (_handshake.IsFailed)
                {
                    if (_kind == PlayKind.JoinLan)
                    {
                        Abort(_handshake.FailureMessage ?? "LAN HANDSHAKE FAILED");
                        return;
                    }

                    if (_handshakeFailureDeadline < 0f)
                    {
                        _handshakeFailureDeadline = now + NetplayProtocol.HandshakeGraceSeconds;
                        Hud.ShowBanner(_handshake.FailureMessage ?? "LAN HANDSHAKE FAILED");
                    }
                    continue;
                }

                if (_handshake.State == LanHandshakeState.HostWaitingJoinerReady ||
                    _handshake.State == LanHandshakeState.HostWaitingConfirmation)
                    Hud.ShowBanner("JOINER FOUND  WAITING READY");
                else if (_handshake.State == LanHandshakeState.JoinerWaitingHostReady)
                    Hud.ShowBanner("CONNECTED  WAITING HOST");

                if (_handshake.IsComplete)
                {
                    FinishHandshake();
                    return;
                }
            }
        }

        void FinishHandshake()
        {
            if (_handshakeDone) return;
            // 握手完成后才创建共同初态和回滚会话，确保两端从同一个比赛状态开始。
            _localSeat = _handshake.LocalSeat;
            _matchId = _handshake.MatchId;
            Settings.pointsToWin = _handshake.Points;
            Settings.marbleCount = _handshake.Marbles;
            Settings.Clamp();
            if (_kind == PlayKind.JoinLan)
                Sim = MatchSim.Create(Settings);
            _lan.MatchId = _matchId;
            MatchSim.StartMatch(Sim);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            LanSmokeRunner.ApplyInitialFixture(Sim, 0);
#endif
            _rollback = new RollbackSession(Sim, _localSeat);
            _lifecycle = new LanMatchLifecycle(_kind == PlayKind.HostLan, _matchId,
                NetplayProtocol.SettingsHash(Settings.pointsToWin, Settings.marbleCount),
                () => Time.realtimeSinceStartup, initialStartup: true);
            _newestLocalInput = -1;
            _from = Sim.Clone();
            _to = Sim.Clone();
            _handshakeDone = true;
            _diagnostics.AddEvent("handshake-complete");
            _handshakeGraceUntil = Time.realtimeSinceStartup + NetplayProtocol.HandshakeGraceSeconds;
            _statusMessage = "";
            Hud.ShowBanner("");
            SyncHud(default);
        }

        void PumpNet()
        {
            if (_lan == null) return;
            // Drain valid traffic first. A stopped simulation still has a live connection.
            while (_lan.TryRecv(out byte[] data, out IPEndPoint from))
            {
                float now = Time.realtimeSinceStartup;
                _diagnostics.ReceivedDatagrams++;
                if (!_lan.IsRemote(from)) { _diagnostics.RejectedDatagrams++; continue; }

                if ((now <= _handshakeGraceUntil || MatchEpoch == 0 && _lifecycle.Phase == LanLifecyclePhase.StartupBarrier) && _handshake != null &&
                    _handshake.Process(data, from, now, out byte[] handshakeResponse))
                {
                    if (handshakeResponse != null) _lan.Send(handshakeResponse);
                    _lastPacketTime = now;
                    continue;
                }

                if (LanUdpPeer.TryDecodeGoodbye(data, out NetGoodbyeMessage goodbye) &&
                    _lifecycle.ReceiveGoodbye(goodbye.MatchId, goodbye.Epoch))
                {
                    _lastPacketTime = now;
                    Abort("PEER LEFT");
                    return;
                }

                if (LanUdpPeer.TryDecodeLifecycle(data, out NetLifecycleMessage control))
                {
                    if (!_lifecycle.Receive(control)) { _diagnostics.RejectedDatagrams++; continue; }
                    if (control.Kind == NetLifecycleKind.Heartbeat) _diagnostics.HeartbeatsReceived++;
                    else _diagnostics.ControlsReceived++;
                    ApplyPreparedEpoch();
                    continue;
                }

                if (!LanUdpPeer.TryDecodeInput(data, out NetInputPacket packet)) { _diagnostics.RejectedDatagrams++; continue; }
                if (_rollback == null || !_lifecycle.CanAcceptInput(packet.MatchId, packet.Epoch, packet.Seat, packet.AckTick))
                { _diagnostics.RejectedDatagrams++; continue; }
                int accepted = _rollback.ReceiveRemoteHistory(
                    packet.NewestTick, packet.InputBits, packet.HistoryCount);
                if (accepted <= 0) { _diagnostics.RejectedDatagrams++; continue; }

                _lastPacketTime = now;
                _diagnostics.AcceptedInputDatagrams++;
                _lifecycle.AcceptInput(packet.MatchId, packet.Epoch, packet.Seat,
                    packet.NewestTick, packet.AckTick, _rollback.ConfirmedTick);
                _lastAck = _lifecycle.LastPeerAck;
                if (packet.ChecksumTick >= 0 && packet.ChecksumTick <= packet.AckTick)
                    // 校验值先排队；下一次 Advance 会先应用迟到输入并完成回滚，再比较已确认历史。
                    _rollback.QueueRemoteChecksum(packet.ChecksumTick, packet.Checksum);
            }
            _rollback.Reconcile();
            if (_rollback.LastRollbackTick >= 0)
            {
                _diagnostics.RollbackCount++;
                GameFx.DiscardNewerThan(_rollback.LastRollbackTick);
                _from.CopyFrom(Sim);
                _to.CopyFrom(Sim);
                SyncHud(default);
            }
            if (_rollback.Status != RollbackStatus.Ok)
            {
                Abort(_rollback.Status == RollbackStatus.Desync ? "DESYNC" : "INPUT HOLE");
                return;
            }
            RefreshLifecycle(true);
            if (_aborting) return;
            float time = Time.realtimeSinceStartup;
            if ((Sim.Phase == MatchState.MatchOver || _lifecycle.Phase == LanLifecyclePhase.StartupBarrier) &&
                time >= _nextHistoryRetry)
            {
                _nextHistoryRetry = time + 0.05f;
                // Cover the whole retained suffix, not just the final eight samples.
                int oldest = Mathf.Max(RollbackSession.DelayTicks, Sim.Tick - RollbackSession.BufferTicks);
                for (int newest = _newestLocalInput; newest >= oldest; newest -= NetplayProtocol.InputHistoryLength)
                    SendHistory(newest);
            }
        }

        void RefreshLifecycle(bool evaluateDeadlines = false)
        {
            if (_lifecycle == null || _aborting) return;
            bool terminal = Sim.Phase == MatchState.MatchOver;
            bool complete = terminal && _rollback.ConfirmedStateTick >= Sim.Tick - 1;
            MatchDirector.HasWinner(Sim.ScoreP1, Sim.ScoreP2, Sim.PointsToWin, out int winner);
            _lifecycle.UpdateTerminal(terminal, complete, new NetFinishTuple
            {
                FinishTick = Sim.Tick - 1,
                Checksum = complete ? _rollback.ChecksumAt(Sim.Tick - 1) : 0,
                Score0 = Sim.ScoreP1, Score1 = Sim.ScoreP2, Winner = winner,
                SettingsHash = NetplayProtocol.SettingsHash(Settings.pointsToWin, Settings.marbleCount)
            });
            // Only the receive pump evaluates clocks, after all queued packets were drained.
            // Post-step presentation and button callbacks must not race liveness.
            if (evaluateDeadlines) _lifecycle.Tick();
            ApplyPreparedEpoch();
            while (_lifecycle.TryDequeue(out NetLifecycleMessage outgoing))
            {
                if (!_lan.Send(LanUdpPeer.EncodeLifecycle(outgoing))) continue;
                if (outgoing.Kind == NetLifecycleKind.Heartbeat) _diagnostics.HeartbeatsSent++;
                else _diagnostics.ControlsSent++;
            }
            _lastPacketTime = _lifecycle.LastAcceptedPeerTime;
            if (!string.IsNullOrEmpty(_lifecycle.FailureReason))
            {
                Abort(_lifecycle.FailureReason);
                return;
            }
            if (_lifecycle.ResultConfirmed)
            {
                if (_resultConfirmedAt < 0f)
                {
                    _resultConfirmedAt = Time.realtimeSinceStartup;
                    NetFinishTuple finish = _lifecycle.ConfirmedFinish;
                    _epochSummary = new LanEpochDiagnosticSummary
                    {
                        MatchId = _matchId, Epoch = MatchEpoch,
                        FinishTick = finish.FinishTick, Checksum = finish.Checksum,
                        Score0 = finish.Score0, Score1 = finish.Score1, Winner = finish.Winner,
                        SettingsHash = finish.SettingsHash,
                        ConfirmedTicks = _rollback.ConfirmedStateTick + 1
                    };
                    _diagnostics.RetainEpoch(_epochSummary);
                }
                _diagnostics.ResultHoldSeconds = Time.realtimeSinceStartup - _resultConfirmedAt;
                _epochSummary.HoldSeconds = _diagnostics.ResultHoldSeconds;
                _epochSummary.ValidatedChecksums = _rollback.ValidatedChecksumCount;
            }
            Hud.ShowLanLifecycle(_lifecycle);
        }

        void ApplyPreparedEpoch()
        {
            if (!_lifecycle.TryTakePreparedEpoch(out int epoch)) return;
            _diagnostics.AddEvent("rematch-epoch-" + epoch);
            Sim = MatchSim.Create(Settings);
            MatchSim.StartMatch(Sim);
            _rollback = new RollbackSession(Sim, _localSeat);
            _sampler.Clear();
            _from = Sim.Clone();
            _to = Sim.Clone();
            _newestLocalInput = -1;
            _lastAck = -1;
            _nextHistoryRetry = 0f;
            _statusMessage = "";
            _handshakeGraceUntil = -1f;
            _diagnostics.RollbackCount = 0;
            _diagnostics.FinishTick = -1;
            _diagnostics.FinishChecksum = 0;
            _diagnostics.FinalScore0 = 0;
            _diagnostics.FinalScore1 = 0;
            _diagnostics.Winner = -1;
            _diagnostics.ResultHoldSeconds = 0f;
            _resultConfirmedAt = -1f;
            _epochSummary = null;
            GameFx.DiscardNewerThan(0);
            Hud.ResetForMatch();
            SyncHud(default);
            ApplyViews(1f);
        }

        void SendInput(int tick)
        {
            if (_lan == null) return;
            int newestTick = tick + RollbackSession.DelayTicks;
            _newestLocalInput = Mathf.Max(_newestLocalInput, newestTick);
            SendHistory(newestTick);
        }

        void SendHistory(int newestTick)
        {
            if (_lan == null || _rollback == null || newestTick < RollbackSession.DelayTicks) return;
            int historyCount = _rollback.CopyLocalHistory(newestTick, _inputHistory);
            if (historyCount <= 0) return;
            int checksumTick = -1;
            int checksum = 0;
            _rollback.TryGetConfirmedChecksum(Sim.Tick - 1, out checksumTick, out checksum);
            bool sent = _lan.Send(LanUdpPeer.EncodeInput(new NetInputPacket
            {
                MatchId = _matchId,
                Epoch = MatchEpoch,
                NewestTick = newestTick,
                Seat = (byte)_localSeat,
                HistoryCount = (byte)historyCount,
                InputBits = _inputHistory,
                AckTick = _rollback.ConfirmedTick,
                ChecksumTick = checksumTick,
                Checksum = checksum
            }));
            if (sent) _lifecycle.RecordSentInput(newestTick);
        }

        void Abort(string message)
        {
            // 所有网络失败统一走此路径：停止推进、通知对端、释放 socket，并在当前画面显示原因。
            // 终止性输入缺口与反同步不能安全自动恢复，因此由玩家确认后再返回菜单。
            if (_aborting) return;
            _aborting = true;
            _networkPaused = true;
            _statusMessage = message;
            _diagnostics.TerminalReason = message;
            _diagnostics.AddEvent("abort-" + message);
            GameSession.Instance.LastLanDiagnostics = Diagnostics;
            GameSession.Instance.ErrorMessage = message;
            if (_lan != null)
            {
                _lan.Send(LanUdpPeer.EncodeGoodbye(_matchId, MatchEpoch));
                _lan.Dispose();
                _lan = null;
            }

            if (Hud != null)
                Hud.ShowNetworkIssue(message);
            Time.timeScale = 0f;
        }
    }
}
