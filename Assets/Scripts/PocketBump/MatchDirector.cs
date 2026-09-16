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

        readonly TickInputSampler _sampler = new TickInputSampler();
        MatchSimState _from;
        MatchSimState _to;
        RollbackSession _rollback;
        LanUdpPeer _lan;
        PlayKind _kind;
        int _localSeat;
        bool _initialized;
        bool _handshakeDone;
        bool _sentReady;
        bool _aborting;
        float _lastPacketTime;
        float _handshakeDeadline;
        int _matchId;
        int _lastAck = -1;
        string _statusMessage;

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
            FrameCamera();
            PlayerOne.Director = this;
            PlayerTwo.Director = this;
            Sim = MatchSim.Create(Settings);
            _from = Sim.Clone();
            _to = Sim.Clone();
            _kind = GameSession.Ensure().Kind;
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
            _sampler.LatchUpdate(net, _localSeat);
            if (net && !_handshakeDone)
                PumpHandshake();
            else if (net)
                PumpNet();
        }

        void FixedUpdate()
        {
            if (!_initialized || !_handshakeDone || _aborting || Sim == null) return;
            if (Sim.Phase == MatchState.MatchOver) return;

            _from.CopyFrom(_to);
            SimEvents events;
            if (_rollback != null)
            {
                // 联机只采样本机座位；远端座位由 RollbackSession 的真实或预测输入提供。
                PlayerInput local = _sampler.SampleNetplaySeat(_localSeat);
                int applyTick = Sim.Tick;
                events = _rollback.Advance(local);
                if (_rollback.LastRollbackTick >= 0)
                    // 被推翻 Tick 的瞬时特效不属于新历史，必须随状态一起撤销。
                    GameFx.DiscardNewerThan(_rollback.LastRollbackTick);
                if (_rollback.Status == RollbackStatus.InputHole)
                {
                    Abort("CONNECTION LOST");
                    return;
                }

                if (_rollback.Status == RollbackStatus.Desync)
                {
                    Abort("DESYNC");
                    return;
                }

                SendInput(local, applyTick);
            }
            else
            {
                // 本地双人也走同一个 MatchSim，不维护另一套 Physics2D 玩法路径。
                events = MatchSim.Step(Sim, _sampler.SampleLocal(0), _sampler.SampleLocal(1));
            }

            _to.CopyFrom(Sim);
            PlayEvents(events, Sim.Tick - 1);
            SyncHud(events);
            if (events.MatchOver)
                Hud.ShowResult(events.Winner);
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
            if (_lan != null)
            {
                _lan.Send(LanUdpPeer.EncodeGoodbye(_matchId));
                _lan.Dispose();
            }
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
                GameFx.Pulse(events.CollectAt0, VisualPalette.PlayerColor(0), 0.4f, 1.6f, 0.28f, tick);
            if (events.Collected1)
                GameFx.Pulse(events.CollectAt1, VisualPalette.PlayerColor(1), 0.4f, 1.6f, 0.28f, tick);
            if (events.Fired0)
                GameFx.Pulse(events.FireAt0, VisualPalette.Text, 0.5f, 2f, 0.32f, tick);
            if (events.Fired1)
                GameFx.Pulse(events.FireAt1, VisualPalette.Text, 0.5f, 2f, 0.32f, tick);
            if (events.DangerousImpact)
                GameFx.Pulse(events.ImpactAt, VisualPalette.Danger, 0.2f, 1.1f, 0.24f, tick);
            if (events.Scored)
            {
                GameFx.Pulse(events.ScoreAt, VisualPalette.PlayerColor(events.Scorer), 0.3f, 2.4f, 0.42f, tick);
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
                _matchId = Environment.TickCount;
                if (!_lan.Bind(ArenaLayout.LanPort))
                {
                    Abort("COULD NOT BIND PORT");
                    return;
                }
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
                }
                catch
                {
                    Abort("BAD ADDRESS");
                    return;
                }

                _lan.Send(LanUdpPeer.EncodeJoin(Settings.pointsToWin, Settings.marbleCount));
            }

            _lastPacketTime = Time.realtimeSinceStartup;
        }

        void PumpHandshake()
        {
            // 正式 Tick 0 前确认协议版本、规则、座位和双方 Ready，避免不同配置直接开局分叉。
            if (Time.realtimeSinceStartup > _handshakeDeadline)
            {
                Abort("LAN TIMEOUT");
                return;
            }

            while (_lan != null && _lan.TryRecv(out byte[] data, out IPEndPoint from))
            {
                _lastPacketTime = Time.realtimeSinceStartup;
                if (!LanUdpPeer.TryReadHeader(data, out byte type, out int offset)) continue;
                if (_kind == PlayKind.HostLan && type == NetplayProtocol.Join)
                {
                    int version = BitConverter.ToInt32(data, offset);
                    int points = BitConverter.ToInt32(data, offset + 4);
                    int marbles = BitConverter.ToInt32(data, offset + 8);
                    int hash = BitConverter.ToInt32(data, offset + 12);
                    bool ok = NetplayProtocol.VersionsMatch(version) &&
                              hash == NetplayProtocol.SettingsHash(Settings.pointsToWin, Settings.marbleCount) &&
                              points == Settings.pointsToWin &&
                              marbles == Settings.marbleCount;
                    _lan.SetRemote(from);
                    _lan.MatchId = _matchId;
                    _lan.Send(LanUdpPeer.EncodeJoinAck(ok, 1, _matchId, Settings.pointsToWin, Settings.marbleCount));
                    if (!ok)
                    {
                        Abort("SETTINGS MISMATCH");
                        return;
                    }

                    Hud.ShowBanner("JOINER FOUND  WAITING READY");
                }
                else if (type == NetplayProtocol.JoinAck && _kind == PlayKind.JoinLan)
                {
                    bool ok = data[offset] != 0;
                    _localSeat = data[offset + 1];
                    _matchId = BitConverter.ToInt32(data, offset + 2);
                    int version = BitConverter.ToInt32(data, offset + 6);
                    int points = BitConverter.ToInt32(data, offset + 10);
                    int marbles = BitConverter.ToInt32(data, offset + 14);
                    if (!ok || !NetplayProtocol.VersionsMatch(version))
                    {
                        Abort("SETTINGS MISMATCH");
                        return;
                    }

                    Settings.pointsToWin = points;
                    Settings.marbleCount = marbles;
                    Settings.Clamp();
                    Sim = MatchSim.Create(Settings);
                    _lan.MatchId = _matchId;
                    _lan.Send(LanUdpPeer.EncodeReady(_matchId));
                    _sentReady = true;
                    Hud.ShowBanner("CONNECTED  WAITING HOST");
                }
                else if (type == NetplayProtocol.Ready)
                {
                    if (_kind == PlayKind.HostLan)
                    {
                        _lan.Send(LanUdpPeer.EncodeReady(_matchId));
                        FinishHandshake();
                    }
                    else if (_sentReady)
                    {
                        FinishHandshake();
                    }
                }
            }
        }

        void FinishHandshake()
        {
            if (_handshakeDone) return;
            // 握手完成后才创建共同初态和回滚会话，确保两端从同一个比赛状态开始。
            MatchSim.StartMatch(Sim);
            _rollback = new RollbackSession(Sim, _localSeat);
            _from = Sim.Clone();
            _to = Sim.Clone();
            _handshakeDone = true;
            _statusMessage = "";
            Hud.ShowBanner("");
            SyncHud(default);
        }

        void PumpNet()
        {
            if (_lan == null) return;
            // UDP 无连接状态，因此用最后收包时间自行判断对端是否失联。
            if (Time.realtimeSinceStartup - _lastPacketTime > ArenaLayout.DisconnectSeconds)
            {
                Abort("CONNECTION LOST");
                return;
            }

            while (_lan.TryRecv(out byte[] data, out _))
            {
                _lastPacketTime = Time.realtimeSinceStartup;
                if (!LanUdpPeer.TryReadHeader(data, out byte type, out int offset)) continue;
                if (type == NetplayProtocol.Goodbye)
                {
                    Abort("PEER LEFT");
                    return;
                }

                if (type != NetplayProtocol.Input || data.Length < offset + 22) continue;
                int matchId = BitConverter.ToInt32(data, offset);
                if (matchId != _matchId) continue;
                int tick = BitConverter.ToInt32(data, offset + 4);
                byte inputBits = data[offset + 9];
                int ack = BitConverter.ToInt32(data, offset + 10);
                int checksumTick = BitConverter.ToInt32(data, offset + 14);
                int checksum = BitConverter.ToInt32(data, offset + 18);
                _lastAck = ack;
                // AckTick 当前仅被记录，协议尚未基于它实现重发；可靠性主要依靠预测和回滚窗口。
                if (_rollback != null)
                {
                    _rollback.ReceiveRemote(tick, PlayerInput.Unpack(inputBits));
                    if (checksumTick >= 0)
                    {
                        int local = _rollback.ChecksumAt(checksumTick);
                        if (local != 0 && local != checksum)
                        {
                            Abort("DESYNC");
                            return;
                        }
                    }
                }
            }
        }

        void SendInput(PlayerInput local, int tick)
        {
            if (_lan == null) return;
            // 发送未来应用 Tick 的紧凑输入，并附带已确认 Tick 与稍早状态哈希用于诊断分叉。
            int checksumTick = Mathf.Max(0, Sim.Tick - 2);
            _lan.Send(LanUdpPeer.EncodeInput(new NetInputPacket
            {
                MatchId = _matchId,
                Tick = tick + RollbackSession.DelayTicks,
                Seat = (byte)_localSeat,
                InputBits = local.Pack(),
                AckTick = _rollback != null ? _rollback.ConfirmedTick : tick,
                ChecksumTick = checksumTick,
                Checksum = _rollback != null ? _rollback.ChecksumAt(checksumTick) : Sim.Checksum()
            }));
        }

        void Abort(string message)
        {
            // 所有网络失败统一走此路径：停止推进、通知对端、释放 socket 并把错误带回菜单。
            if (_aborting) return;
            _aborting = true;
            _statusMessage = message;
            GameSession.Instance.ErrorMessage = message;
            if (_lan != null)
            {
                _lan.Send(LanUdpPeer.EncodeGoodbye(_matchId));
                _lan.Dispose();
                _lan = null;
            }

            if (Hud != null)
                Hud.ShowBanner(message);
            GameSession.Instance.ReturnToMainMenu();
        }
    }
}
