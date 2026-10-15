using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace PocketBump
{
    // The only production clock owner. Views consume this world; neither viewport runs
    // another simulation. The retained v6 director is deliberately a separate component.
    public sealed class DashMatchDirector : MonoBehaviour
    {
        [SerializeField] DashArenaPresentation _presentation;
        [SerializeField] DashMatchHud _hud;
        [SerializeField] bool _automaticTicking = true;
        DashTickInputSampler _sampler;
        GameSession _gameSession;
        bool _initialized, _left, _failurePresented, _snapRender, _wasInputWaiting;
        int _boundEpoch = -1;
        string _failure = "";

        public MatchSettings Settings { get; private set; }
        public ArenaRulesProfile Rules { get; private set; }
        public DashArenaState Sim { get; private set; }
        public PlayKind Kind { get; private set; }
        public DashLanSession LanSession { get; private set; }
        public DashLanLifecycle Lifecycle => LanSession?.Lifecycle;
        public DashArenaPresentation Presentation => _presentation;
        public DashMatchHud Hud => _hud;
        public DashControlBindings Bindings { get; } = new DashControlBindings();
        public bool AutomaticTicking { get => _automaticTicking; set => _automaticTicking = value; }
        public bool Initialized => _initialized;
        public bool MenuOpen { get; private set; }
        public bool LocalMenuPaused => MenuOpen && Kind == PlayKind.Local;
        public int MatchId => LanSession?.MatchId ?? 0;
        public int MatchEpoch => Sim?.Epoch ?? 0;
        public int LocalSeat => LanSession?.LocalSeat ?? 0;
        public MatchState State => Sim?.Phase ?? MatchState.Pregame;
        public bool ReadyForNetplay => Kind != PlayKind.Local && LanSession != null && LanSession.Ready;
        public string FailureReason => _failure.Length != 0 ? _failure : LanSession?.FailureReason ?? "";
        public bool NetworkPaused => FailureReason.Length != 0;
        // Pacing is recoverable transport state, not a terminal network failure.
        public bool WaitingForRemoteInput => !_left && Kind != PlayKind.Local && !NetworkPaused &&
            (LanSession?.WaitingForRemoteInput ?? false);
        public bool ResultsReady => !_left && !NetworkPaused && State == MatchState.MatchOver &&
            (Kind == PlayKind.Local || (Lifecycle != null && Lifecycle.ResultConfirmed &&
                (Lifecycle.Phase == LanLifecyclePhase.ResultsReady || Lifecycle.Phase == LanLifecyclePhase.WaitingForPeerChoice)));
        public bool CanRequestRematch => ResultsReady && (Kind == PlayKind.Local || !Lifecycle.LocalConsent);
        public bool CanModifyRules => ResultsReady && Kind == PlayKind.Local;
        public int Winner => ResultsReady && MatchDirector.HasWinner(Sim.Scores[0], Sim.Scores[1], Rules.PointsToWin, out int winner) ? winner : -1;
        public int ConfirmedStateTick => Kind == PlayKind.Local ? (Sim?.Tick ?? 0) - 1 : LanSession?.ConfirmedStateTick ?? -1;
        public int EpochInitializationCount { get; private set; }
        public int PresentationErrorCount { get; private set; }
        public string LastPresentationError { get; private set; } = "";
        public string NetworkInitializationEndpoint { get; private set; } = "";
        public string NetworkInitializationError { get; private set; } = "";

        // Diagnostics supply intent, not a second stepper or an authority-changing API.
        // Parameters are assigned seat, current tick, and the existing authoritative world.
        public Func<int, int, DashArenaState, DashArenaInput> InputOverride { get; set; }
        public event Action<int> EpochInitialized;
        public event Action<IReadOnlyList<DashArenaEvent>> ConfirmedEvents;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        // Explicit command-line fixture hook; absent from release and null for ordinary play.
        internal static Func<PlayKind, string, ArenaRulesProfile, DashLanSession> DiagnosticSessionFactory;
#endif

        public string StatusMessage
        {
            get
            {
                if (NetworkPaused) return FailureReason;
                if (_left) return "LEFT MATCH";
                if (Kind == PlayKind.Local) return State == MatchState.Pregame ? Bindings.PregameBanner() : "";
                if (Lifecycle == null) return Kind == PlayKind.HostLan ? "HOSTING LAN - WAITING FOR JOINER" : "JOINING LAN...";
                if (Lifecycle.Phase == LanLifecyclePhase.StartupBarrier)
                    return MatchEpoch == 0 ? "STARTING MATCH" : "STARTING NEXT MATCH";
                if (Lifecycle.Phase == LanLifecyclePhase.PreparingNextEpoch) return "STARTING NEXT MATCH";
                if (Lifecycle.Phase == LanLifecyclePhase.SettlingResult) return "CONFIRMING RESULT";
                if (Lifecycle.LocalConsent) return "WAITING FOR PEER";
                if (Lifecycle.RemoteConsent) return "PEER REQUESTED REMATCH";
                return "";
            }
        }

        public static DashMatchDirector Build(MatchSettings settings = null, bool startImmediately = true)
        {
            // Fallback creates orchestration only. It never rebuilds/recolors authored art
            // or resurrects the removed basket world; production scenes provide their views.
            var director = new GameObject("Dash Match").AddComponent<DashMatchDirector>();
            director.InitializeAuthored(settings ?? new MatchSettings(), startImmediately);
            return director;
        }

        public void Configure(DashArenaPresentation presentation, DashMatchHud hud)
        {
            _presentation = presentation;
            _hud = hud;
            if (_initialized) BindViews();
        }

        public void InitializeAuthored(MatchSettings settings, bool startImmediately = true)
        {
            if (_initialized) return;
            MatchSettings copy = new MatchSettings
            {
                pointsToWin = settings?.pointsToWin ?? MatchSettings.DefaultPointsToWin,
                marbleCount = settings?.marbleCount ?? MatchSettings.DefaultMarbleCount
            };
            copy.Clamp();
            var rules = ArenaRulesProfile.CreateProductionV1(copy.pointsToWin, copy.marbleCount);
            _gameSession = GameSession.Ensure();
            if (_gameSession.Kind == PlayKind.Local)
            {
                InitializeLocal(rules, NewSeed(), startImmediately);
                return;
            }
            NetworkInitializationEndpoint = (_gameSession.Kind == PlayKind.HostLan ? "0.0.0.0" : _gameSession.JoinAddress) + ":" + ArenaLayout.LanPort;
            try
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if (DiagnosticSessionFactory != null)
                {
                    InitializeLan(DiagnosticSessionFactory(_gameSession.Kind, _gameSession.JoinAddress, rules));
                    return;
                }
#endif
                if (_gameSession.Kind == PlayKind.HostLan)
                {
                    int matchId = BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0);
                    if (matchId == 0) matchId = 1;
                    InitializeLan(DashLanSession.Host(rules, matchId, NewSeed(), () => Time.realtimeSinceStartup, ArenaLayout.LanPort));
                    return;
                }
                if (!IPAddress.TryParse(_gameSession.JoinAddress, out IPAddress address))
                {
                    FreezeInitialization(rules, PlayKind.JoinLan, "BAD ADDRESS", _gameSession.JoinAddress);
                    return;
                }
                InitializeLan(DashLanSession.Join(rules, new IPEndPoint(address, ArenaLayout.LanPort), () => Time.realtimeSinceStartup));
            }
            catch (SocketException error)
            {
                FreezeInitialization(rules, _gameSession.Kind,
                    _gameSession.Kind == PlayKind.HostLan ? "COULD NOT BIND PORT" : "COULD NOT OPEN SOCKET",
                    error.SocketErrorCode + ": " + error.Message);
            }
        }

        void FreezeInitialization(ArenaRulesProfile rules, PlayKind kind, string reason, string detail)
        {
            if (!_initialized) InitializeCore(rules, kind, null);
            Sim = Sim ?? DashArenaSimulation.Create(rules, 1u); // Non-playable pre-handshake display.
            _failure = reason;
            NetworkInitializationError = detail;
            BindViews(); RefreshFailure();
        }

        public void InitializeLocal(ArenaRulesProfile rules, uint seed = 1u, bool startImmediately = true,
            DashTickInputSampler sampler = null)
        {
            if (_initialized) return;
            if (seed == 0) throw new ArgumentOutOfRangeException(nameof(seed));
            InitializeCore(rules, PlayKind.Local, sampler);
            Sim = DashArenaSimulation.Create(rules, seed);
            if (startImmediately) DashArenaSimulation.StartMatch(Sim);
            InitializeEpoch(0);
        }

        // Ownership transfers to the director; Leave/OnDestroy closes only this session.
        public void InitializeLan(DashLanSession session, DashTickInputSampler sampler = null)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (_initialized) return;
            if (session.Left) throw new ArgumentException("Cannot attach a departed session.", nameof(session));
            InitializeCore(session.Rules, session.LocalSeat == 0 ? PlayKind.HostLan : PlayKind.JoinLan, sampler);
            LanSession = session;
            session.EpochInitialized += OnLanEpoch;
            session.ConfirmedEvents += PresentConfirmed;
            Sim = session.State ?? DashArenaSimulation.Create(Rules, session.Seed == 0 ? 1u : session.Seed);
            if (session.State != null) InitializeEpoch(session.Epoch);
            else BindViews();
            if (session.Frozen) NetworkInitializationError = session.LastSocketError.Length != 0 ? session.LastSocketError : session.FailureReason;
            RefreshFailure();
        }

        void InitializeCore(ArenaRulesProfile rules, PlayKind kind, DashTickInputSampler sampler)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (rules.IsProvisional) throw new ArgumentException("Production director requires approved rules.", nameof(rules));
            Rules = rules;
            Settings = new MatchSettings { pointsToWin = rules.PointsToWin, marbleCount = rules.BallCount };
            Kind = kind;
            _sampler = sampler ?? new DashTickInputSampler(Bindings);
            _sampler.Clear();
            _initialized = true;
            Time.fixedDeltaTime = 1f / 60f;
        }

        public void StartMatch()
        {
            if (!_initialized || _left || Kind != PlayKind.Local || State != MatchState.Pregame) return;
            DashArenaSimulation.StartMatch(Sim);
            _sampler.Clear();
            BindViews();
        }

        void Update()
        {
            if (_initialized && !_left && Input.GetKeyDown(KeyCode.Escape)) SetMenuOpen(!MenuOpen);
            if (_automaticTicking) PumpFrame();
        }

        // A personal LAN menu cannot stop the other computer's clock. Local play
        // pauses only this simulation, never the global clock or UI event system.
        public void SetMenuOpen(bool open)
        {
            if (!_initialized || _left || MenuOpen == open) return;
            MenuOpen = open;
            _sampler.Clear();
            _snapRender = true;
            Present(() => _hud?.RefreshFromDirector());
        }

        public void PumpFrame()
        {
            if (!_initialized || _left) return;
            if (LanSession != null)
            {
                LanSession.Pump();
                ReconcilePresentation();
            }
            RefreshFailure();
            RefreshInputWaiting();
            if (MenuOpen || NetworkPaused || State == MatchState.MatchOver || State == MatchState.Pregame)
                _sampler.Clear();
            else _sampler.LatchUpdate(Kind != PlayKind.Local, LocalSeat);
        }

        void FixedUpdate()
        {
            if (_automaticTicking) AdvanceOneTick();
        }

        public IReadOnlyList<DashArenaEvent> AdvanceOneTick()
        {
            if (!_initialized || _left) return Array.Empty<DashArenaEvent>();
            if (LanSession != null)
            {
                // Advance owns drain -> reconcile -> deadlines -> startup gate. Its sampler
                // runs only for an actual new tick, including while a player is disabled.
                IReadOnlyList<DashArenaEvent> events = LanSession.Advance((epoch, tick) =>
                {
                    Present(() => _presentation?.CapturePreviousPose(Sim));
                    return Sample(LocalSeat, tick);
                });
                ReconcilePresentation();
                Present(() => _presentation?.ApplyTransitions(events));
                RefreshFailure();
                RefreshInputWaiting();
                return events;
            }
            if (LocalMenuPaused || NetworkPaused || State == MatchState.MatchOver || State == MatchState.Pregame)
                return Array.Empty<DashArenaEvent>();
            Present(() => _presentation?.CapturePreviousPose(Sim));
            IReadOnlyList<DashArenaEvent> localEvents = DashArenaSimulation.Step(Sim, Sample(0, Sim.Tick), Sample(1, Sim.Tick));
            Present(() => _presentation?.ApplyTransitions(localEvents));
            PresentConfirmed(localEvents);
            return localEvents;
        }

        DashArenaInput Sample(int seat, int tick)
        {
            // Consume latched keyboard edges even when the diagnostic provider replaces
            // intent, so detaching a driver cannot release an old hidden Dash press.
            DashArenaInput keyboard = Kind == PlayKind.Local ? _sampler.SampleLocal(seat) : _sampler.SampleNetplaySeat(seat);
            if (MenuOpen) return default;
            DashArenaInput input = InputOverride != null ? InputOverride(seat, tick, Sim) : keyboard;
            return DashArenaInput.Unpack(input.Pack());
        }

        void LateUpdate()
        {
            if (!_initialized || Sim == null) return;
            float alpha = _snapRender || LocalMenuPaused || NetworkPaused || WaitingForRemoteInput || !_automaticTicking ? 1f :
                Mathf.Clamp01((Time.time - Time.fixedTime) / (1f / 60f));
            Present(() => _presentation?.Render(Sim, alpha));
            _snapRender = false;
        }

        void ReconcilePresentation()
        {
            if (LanSession.LastRollbackTick < 0) return;
            _snapRender = true;
            Present(() => _presentation?.ReconcileState(Sim));
        }

        void PresentConfirmed(IReadOnlyList<DashArenaEvent> events)
        {
            if (events.Count == 0) return;
            Present(() => _presentation?.PresentConfirmedEvents(events));
            Notify(ConfirmedEvents, events);
        }

        void OnLanEpoch(int epoch)
        {
            Sim = LanSession.State;
            InitializeEpoch(epoch);
        }

        void InitializeEpoch(int epoch)
        {
            if (_boundEpoch == epoch) return;
            _boundEpoch = epoch;
            MenuOpen = false;
            _wasInputWaiting = false;
            _sampler.Clear();
            EpochInitializationCount++;
            _snapRender = true;
            BindViews();
            Notify(EpochInitialized, epoch);
        }

        void BindViews()
        {
            if (Application.isPlaying)
            {
                DashNetworkReporter reporter = GetComponent<DashNetworkReporter>();
                if (reporter == null) reporter = gameObject.AddComponent<DashNetworkReporter>();
                reporter.Bind(this);
                Present(() => DashAudioFeedback.Attach(this));
            }
            Present(() => _presentation?.BindState(Sim, Kind != PlayKind.Local, LocalSeat));
            Present(() => _hud?.Bind(this));
        }

        void RefreshFailure()
        {
            if (!NetworkPaused || _failurePresented) return;
            _failurePresented = true;
            _sampler.Clear();
            _snapRender = true;
            if (_gameSession != null) _gameSession.ErrorMessage = FailureReason;
            Present(() => _presentation?.ReconcileState(Sim));
            Present(() => _hud?.RefreshFromDirector());
        }

        void RefreshInputWaiting()
        {
            bool waiting = WaitingForRemoteInput;
            if (waiting == _wasInputWaiting) return;
            _wasInputWaiting = waiting;
            _snapRender = true;
            // Do not clear/latch/sample input here. PumpFrame must keep latching fresh
            // Dash edges while Advance is waiting before its input-sampling callback.
            Present(() => _hud?.RefreshFromDirector());
        }

        public bool RequestRematch()
        {
            if (!_initialized || _left || NetworkPaused) return false;
            if (LanSession != null) return LanSession.RequestRematch();
            if (!ResultsReady) return false;
            if (MatchEpoch == int.MaxValue) return false;
            Sim = DashArenaSimulation.Create(Rules, NewSeed(), MatchEpoch + 1);
            DashArenaSimulation.StartMatch(Sim);
            InitializeEpoch(Sim.Epoch);
            return true;
        }

        public void LeaveNetwork()
        {
            if (_left) return;
            GetComponent<DashNetworkReporter>()?.CaptureNow("leave");
            _left = true;
            MenuOpen = false;
            _sampler?.Clear();
            LanSession?.Leave();
            Present(() => _presentation?.ClearTransientPresentation());
        }

        void OnDisable() { _sampler?.Clear(); }
        void OnDestroy()
        {
            if (LanSession != null)
            {
                LanSession.EpochInitialized -= OnLanEpoch;
                LanSession.ConfirmedEvents -= PresentConfirmed;
            }
            LeaveNetwork();
        }

        void Notify<T>(Action<T> subscribers, T value)
        {
            if (subscribers == null) return;
            foreach (Delegate subscriber in subscribers.GetInvocationList())
                Present(() => ((Action<T>)subscriber)(value));
        }

        void Present(Action callback)
        {
            try { callback(); }
            catch (Exception error)
            {
                PresentationErrorCount++;
                LastPresentationError = error.GetType().Name + ": " + error.Message;
            }
        }

        static uint NewSeed()
        {
            uint seed = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0);
            return seed == 0 ? ArenaRandom.DefaultSeed : seed;
        }
    }
}
