using System;
using System.Collections.Generic;
using System.Net;

namespace PocketBump
{
    public readonly struct DashConfirmedFrame
    {
        public readonly DashArenaState State;
        public readonly IReadOnlyList<DashArenaEvent> Events;
        public readonly DashArenaInput Input0, Input1;
        public DashConfirmedFrame(DashArenaState state, IReadOnlyList<DashArenaEvent> events,
            DashArenaInput input0, DashArenaInput input1)
        { State = state; Events = events; Input0 = input0; Input1 = input1; }
    }
    public readonly struct DashLanEpochEvidence
    {
        public readonly int Epoch;
        public readonly uint Seed;
        public readonly string ContentHash;
        public readonly NetFinishTuple Finish;
        public DashLanEpochEvidence(int epoch, uint seed, string contentHash, NetFinishTuple finish)
        { Epoch = epoch; Seed = seed; ContentHash = contentHash; Finish = finish; }
    }

    // Pure caller-driven orchestration. Owns exactly one socket, never a scene, keyboard,
    // Unity clock, timeScale or menu. Call Pump even while simulation/results are stopped.
    public sealed class DashLanSession : IDisposable
    {
        public const float HandshakeSeconds = 15f;
        public const float HistoryRetrySeconds = .05f;
        public const float InputProgressTimeoutSeconds = 5f;
        public const int MaximumInputLeadTicks = DashRollbackSession.BufferTicks - DashRollbackSession.DelayTicks;
        const int RetainedResultLimit = 16;
        readonly LanUdpPeer _peer;
        readonly bool _host;
        readonly Func<float> _clock;
        readonly Func<uint> _nextSeedFactory;
        readonly Func<ArenaRulesProfile, uint, int, DashArenaState> _initialStateFactory;
        readonly float _handshakeDeadline;
        readonly byte[] _history = new byte[DashNetcode.HistoryLength];
        readonly DashConfirmedEventLedger _ledger = new DashConfirmedEventLedger();
        readonly List<DashLanEpochEvidence> _results = new List<DashLanEpochEvidence>();
        readonly IReadOnlyList<DashLanEpochEvidence> _resultView;
        float _handshakeFailureRetryUntil = -1f;
        float _handshakeGraceUntil;
        float _nextHistoryRetry;
        int _newestLocalInput = -1;
        int _lastRetainedEpoch = -1;
        float _lastInputProgressAt;
        float _inputProgressStoppedAt = -1f;
        float _inputWaitSince = -1f;
        float _completedInputWaitSeconds;
        float _completedInputWaitMaxSeconds;
        float _lastPumpAt = -1f;
        float _lastRawReceiveAt = -1f;
        float _observationsStoppedAt = -1f;

        public ArenaRulesProfile Rules { get; }
        public DashLanHandshake Handshake { get; }
        public DashRollbackSession Rollback { get; private set; }
        public DashLanLifecycle Lifecycle { get; private set; }
        public DashArenaState State => Rollback?.State;
        public int LocalSeat => Handshake.LocalSeat;
        public int MatchId => Handshake.MatchId;
        public int Epoch => Lifecycle != null ? Lifecycle.Epoch : 0;
        public uint Seed => Lifecycle != null ? Lifecycle.Seed : Handshake.Seed;
        public bool Ready => !Left && !Frozen && Lifecycle != null;
        public bool Frozen => FailureReason.Length != 0;
        public bool Left { get; private set; }
        public string FailureReason { get; private set; } = "";
        public string FailureOrigin { get; private set; } = "none";
        public string PeerFailureReason { get; private set; } = "";
        public string InitializationError { get; private set; } = "";
        public int InitializationCount { get; private set; }
        public int LastRollbackTick { get; private set; } = -1;
        public int ConfirmedStateTick => Rollback != null ? Rollback.ConfirmedStateTick : -1;
        public int MissingConfirmedEventTick => _ledger.MissingTick;
        public long CommittedEventCount { get; private set; }
        public int PresentationErrorCount { get; private set; }
        public string LastPresentationError { get; private set; } = "";
        public int ReceivedDatagrams { get; private set; }
        public int RejectedDatagrams { get; private set; }
        public int RejectedEndpointDatagrams { get; private set; }
        public int RejectedHandshakeDatagrams { get; private set; }
        public int RejectedControlDatagrams { get; private set; }
        public int RejectedInputDatagrams { get; private set; }
        public int RejectedHistoryDatagrams { get; private set; }
        public int AcceptedInputDatagrams { get; private set; }
        public int AcceptedControls { get; private set; }
        public int ControlsSent { get; private set; }
        public int HeartbeatsSent { get; private set; }
        public int HeartbeatsReceived { get; private set; }
        public int SendAttempts => _peer.SendAttempts;
        public int SentDatagrams => _peer.SentDatagrams;
        public int SendFailures => _peer.SendFailures;
        public string LastSocketError => _peer.LastSocketError;
        public int ReceivePolls => _peer.ReceivePolls;
        public int EmptyReceivePolls => _peer.EmptyReceivePolls;
        public int ReceiveFailures => _peer.ReceiveFailures;
        public string LastReceiveError => _peer.LastReceiveError;
        public int PumpCount { get; private set; }
        public float LastPumpGapSeconds { get; private set; }
        public float MaxPumpGapSeconds { get; private set; }
        float ObservationTime => _observationsStoppedAt >= 0f ? _observationsStoppedAt : _clock();
        public float RawReceiveAgeSeconds => _lastRawReceiveAt < 0f ? -1f : Math.Max(0f, ObservationTime - _lastRawReceiveAt);
        public float AcceptedPeerAgeSeconds => Lifecycle == null ? -1f : Math.Max(0f, ObservationTime - Lifecycle.LastAcceptedPeerTime);
        public bool SocketBound => _peer.Bound;
        public IPEndPoint LocalEndpoint { get; }
        public IPEndPoint RemoteEndpoint => Copy(Handshake.RemoteEndpoint);
        public IReadOnlyList<DashLanEpochEvidence> RetainedResults => _resultView;
        // Pacing metrics are per epoch. Completed diagnostics retain their own snapshot
        // before a rematch resets them. Temporary wait is not a terminal network failure.
        public bool WaitingForRemoteInput => _inputWaitSince >= 0f;
        public int InputWaitCount { get; private set; }
        public float InputWaitCurrentSeconds => WaitingForRemoteInput ? Math.Max(0f, _clock() - _inputWaitSince) : 0f;
        public float InputWaitSeconds => _completedInputWaitSeconds + InputWaitCurrentSeconds;
        public float InputWaitMaxSeconds => Math.Max(_completedInputWaitMaxSeconds, InputWaitCurrentSeconds);
        public float InputProgressAgeSeconds => Rollback == null ? 0f :
            Math.Max(0f, (_inputProgressStoppedAt >= 0f ? _inputProgressStoppedAt : _clock()) - _lastInputProgressAt);
        public int LatestLocalInputTick => _newestLocalInput;
        public int RemoteContiguousInputTick => Rollback?.ConfirmedTick ?? -1;
        public int LatestRemoteInputTick => Rollback?.NewestRemoteTick ?? -1;
        public int InputLagTicks => State == null ? 0 : Math.Max(0, State.Tick - RemoteContiguousInputTick);

        // Consumers run synchronously, before another tick can overwrite retained history.
        // Each callback is isolated: one broken view must neither skip other consumers nor
        // interrupt network progress. Errors stay separate from connection failure evidence.
        public event Action<int> EpochInitialized;
        public event Action<IReadOnlyList<DashArenaEvent>> ConfirmedEvents;
        // Each subscriber owns its independent clone; no diagnostic or presentation
        // callback can mutate rollback authority or another subscriber's evidence.
        public event Action<DashConfirmedFrame> ConfirmedFrame;

        public static DashLanSession Host(ArenaRulesProfile rules, int matchId, uint seed,
            Func<float> clock, int port = 0, Func<uint> nextSeedFactory = null,
            Func<ArenaRulesProfile, uint, int, DashArenaState> initialStateFactory = null) =>
            new DashLanSession(true, rules, clock, port, null, matchId, seed, 0, nextSeedFactory, initialStateFactory);

        public static DashLanSession Join(ArenaRulesProfile rules, IPEndPoint endpoint,
            Func<float> clock, ulong joinNonce = 0, int localPort = 0,
            Func<ArenaRulesProfile, uint, int, DashArenaState> initialStateFactory = null) =>
            new DashLanSession(false, rules, clock, localPort, endpoint, 0, 0, joinNonce, null, initialStateFactory);

        DashLanSession(bool host, ArenaRulesProfile rules, Func<float> clock, int port,
            IPEndPoint remote, int matchId, uint seed, ulong nonce, Func<uint> nextSeedFactory,
            Func<ArenaRulesProfile, uint, int, DashArenaState> initialStateFactory)
        {
            if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _host = host; _nextSeedFactory = nextSeedFactory;
            _initialStateFactory = initialStateFactory;
            Handshake = new DashLanHandshake(host ? LanHandshakeRole.Host : LanHandshakeRole.Joiner,
                rules, matchId, seed, remote, nonce);
            _handshakeDeadline = _clock() + HandshakeSeconds;
            _resultView = _results.AsReadOnly();
            _peer = new LanUdpPeer();
            if (!_peer.Bind(port))
            {
                RecordFailure(host ? "COULD NOT BIND PORT" : "COULD NOT OPEN SOCKET", "local", "");
                return;
            }
            LocalEndpoint = Copy(_peer.LocalEndPoint);
            if (!host) _peer.SetRemote(remote);
        }

        public void Pump()
        {
            LastRollbackTick = -1;
            if (Left || (Frozen && _handshakeFailureRetryUntil < 0f)) return;
            if (!Frozen)
            {
                float pumpTime = _clock();
                LastPumpGapSeconds = _lastPumpAt < 0f ? 0f : Math.Max(0f, pumpTime - _lastPumpAt);
                MaxPumpGapSeconds = Math.Max(MaxPumpGapSeconds, LastPumpGapSeconds);
                _lastPumpAt = pumpTime; PumpCount++;
            }
            // Do not evaluate timeouts before draining the actual socket queue. This same
            // path runs before EVERY catch-up tick, not only once per rendered frame.
            while (_peer.Bound && _peer.TryRecv(out byte[] bytes, out IPEndPoint from))
            {
                ReceivedDatagrams++;
                if (!Frozen) _lastRawReceiveAt = _clock();
                if (Lifecycle == null) ReceiveHandshake(bytes, from);
                else ReceiveEstablished(bytes, from);
                if (Frozen && _handshakeFailureRetryUntil < 0f) return;
            }

            float now = _clock();
            if (Frozen)
            {
                if (now >= _handshakeFailureRetryUntil)
                { _handshakeFailureRetryUntil = -1f; _peer.Dispose(); return; }
                RetryHandshake(now);
                return;
            }
            if (Lifecycle == null)
            {
                if (now >= _handshakeDeadline) { Fail("LAN HANDSHAKE TIMEOUT"); return; }
                RetryHandshake(now);
                return;
            }

            Rollback.Reconcile();
            CaptureRollback();
            Settle(true);
            if (!Ready) return;
            UpdateInputPacing(now);
            if (!Ready) return;
            if ((State.Phase == MatchState.MatchOver || Lifecycle.Phase == LanLifecyclePhase.StartupBarrier || WaitingForRemoteInput) &&
                now >= _nextHistoryRetry)
            {
                _nextHistoryRetry = now + HistoryRetrySeconds;
                int oldest = Math.Max(DashRollbackSession.DelayTicks, State.Tick - DashRollbackSession.BufferTicks);
                for (int newest = _newestLocalInput; newest >= oldest; newest -= DashNetcode.HistoryLength)
                    SendHistory(newest);
            }
        }

        // The delegate receives epoch and current simulation tick. It is invoked once only
        // after Pump and the advance gate, so blocked startup cannot consume input edges.
        public IReadOnlyList<DashArenaEvent> Advance(Func<int, int, DashArenaInput> sampleLocal)
        {
            if (sampleLocal == null) throw new ArgumentNullException(nameof(sampleLocal));
            Pump();
            if (!Ready || State.Phase == MatchState.Pregame || State.Phase == MatchState.MatchOver ||
                !Lifecycle.CanAdvance(State.Tick) || WaitingForRemoteInput) return Array.Empty<DashArenaEvent>();
            if (State.Tick > DashNetcode.MaximumTick - DashRollbackSession.DelayTicks)
            { Fail("MATCH TICK EXHAUSTED"); return Array.Empty<DashArenaEvent>(); }
            int tick = State.Tick;
            // Store exactly the input represented on the wire, even for a diagnostic or
            // future analogue caller. Sampling still happens once, never during retries.
            DashArenaInput sampled = sampleLocal(Epoch, tick);
            DashArenaInput canonical = DashArenaInput.Unpack(sampled.Pack());
            IReadOnlyList<DashArenaEvent> events = Rollback.Advance(canonical);
            CaptureRollback();
            if (Rollback.Status != RollbackStatus.Ok)
            { Fail(Rollback.Status == RollbackStatus.Desync ? "DESYNC" : "INPUT HOLE"); return Array.Empty<DashArenaEvent>(); }
            _newestLocalInput = Math.Max(_newestLocalInput, tick + DashRollbackSession.DelayTicks);
            SendHistory(_newestLocalInput);
            Settle(false); // UI and post-step handling do not race the receive-pump clocks.
            return events;
        }

        public bool RequestRematch()
        {
            Pump();
            if (!Ready || !Lifecycle.RequestRematch()) return false;
            Settle(false);
            return true;
        }

        void ReceiveHandshake(byte[] bytes, IPEndPoint from)
        {
            if (!Handshake.Process(bytes, from, _clock(), out byte[] response))
            {
                RejectedDatagrams++;
                if (Handshake.RemoteEndpoint != null && !LanUdpPeer.EndpointsEqual(Handshake.RemoteEndpoint, from))
                    RejectedEndpointDatagrams++;
                else RejectedHandshakeDatagrams++;
                return;
            }
            if (Handshake.RemoteEndpoint != null) _peer.SetRemote(Handshake.RemoteEndpoint);
            if (response != null) _peer.SendTo(response, from);
            if (Handshake.IsFailed)
            {
                RecordFailure(Handshake.FailureMessage, "local", "");
                if (_host)
                {
                    if (_handshakeFailureRetryUntil < 0f)
                        _handshakeFailureRetryUntil = _clock() + NetplayProtocol.HandshakeGraceSeconds;
                }
                else _peer.Dispose();
                return;
            }
            if (!Handshake.IsComplete || Lifecycle != null) return;
            _peer.MatchId = MatchId;
            Lifecycle = new DashLanLifecycle(_host, MatchId, Rules, Handshake.Seed,
                _clock, _nextSeedFactory, initialStartup: true);
            _handshakeGraceUntil = _clock() + NetplayProtocol.HandshakeGraceSeconds;
            InitializeEpoch(0, Handshake.Seed);
        }

        void ReceiveEstablished(byte[] bytes, IPEndPoint from)
        {
            if (!_peer.IsRemote(from)) { RejectedDatagrams++; RejectedEndpointDatagrams++; return; }
            bool initialBarrier = Epoch == 0 && Lifecycle.Phase == LanLifecyclePhase.StartupBarrier;
            if ((_clock() <= _handshakeGraceUntil || initialBarrier) &&
                Handshake.Process(bytes, from, _clock(), out byte[] response))
            {
                if (response != null) _peer.Send(response);
                return;
            }
            if (!DashNetcode.TryReadMessageType(bytes, out byte type))
            { RejectedDatagrams++; RejectedControlDatagrams++; return; }
            if (type == DashNetcode.Fault)
            {
                if (!DashNetcode.TryDecodeFault(bytes, out DashPeerFaultMessage fault) || !Lifecycle.ReceiveFault(fault))
                { RejectedDatagrams++; RejectedControlDatagrams++; return; }
                AcceptedControls++;
                FailPeer("PEER FAULT", DashPeerFault.ToFailureReason(fault.Reason));
                return;
            }
            if (type == NetplayProtocol.Goodbye)
            {
                if (!DashNetcode.TryDecodeGoodbye(bytes, out NetGoodbyeMessage goodbye) ||
                    !Lifecycle.ReceiveGoodbye(goodbye.MatchId, goodbye.Epoch))
                { RejectedDatagrams++; RejectedControlDatagrams++; return; }
                AcceptedControls++;
                FailPeer("PEER LEFT", "");
                return;
            }
            if (type == NetplayProtocol.Lifecycle)
            {
                if (!DashNetcode.TryDecodeLifecycle(bytes, out DashLifecycleMessage control) || !Lifecycle.Receive(control))
                { RejectedDatagrams++; RejectedControlDatagrams++; return; }
                AcceptedControls++;
                if (control.Kind == NetLifecycleKind.Heartbeat) HeartbeatsReceived++;
                RetainResult();
                ApplyPreparedEpoch();
                // A valid control can reveal a local DESYNC. Preserve that cause before
                // another queued packet is processed (including a peer's later fault).
                if (Lifecycle.FailureReason.Length != 0) Fail(Lifecycle.FailureReason);
                return;
            }
            if (type != NetplayProtocol.Input)
            { RejectedDatagrams++; RejectedHandshakeDatagrams++; return; }
            if (!DashNetcode.TryDecodeInput(bytes, out NetInputPacket packet) ||
                !Lifecycle.CanAcceptInput(packet.MatchId, packet.Epoch, packet.Seat, packet.AckTick))
            { RejectedDatagrams++; RejectedInputDatagrams++; return; }
            int previousFrontier = Rollback.ConfirmedTick;
            int accepted = Rollback.ReceiveRemoteHistory(packet.NewestTick, packet.InputBits, packet.HistoryCount);
            if (accepted <= 0) { RejectedDatagrams++; RejectedHistoryDatagrams++; return; }
            AcceptedInputDatagrams++;
            // Packet receipt, Ack changes and later samples beyond a hole are not
            // usable contiguous progress. Retransmitted identical prefixes never renew it.
            if (Rollback.ConfirmedTick > previousFrontier) _lastInputProgressAt = _clock();
            Lifecycle.AcceptInput(packet.MatchId, packet.Epoch, packet.Seat, packet.NewestTick, packet.AckTick, Rollback.ConfirmedTick);
            if (packet.ChecksumTick >= 0 && packet.ChecksumTick <= packet.AckTick)
                Rollback.QueueRemoteChecksum(packet.ChecksumTick, packet.Checksum);
        }

        void Settle(bool deadlines)
        {
            if (Frozen || Left || Lifecycle == null) return;
            if (Rollback.Status != RollbackStatus.Ok)
            { Fail(Rollback.Status == RollbackStatus.Desync ? "DESYNC" : "INPUT HOLE"); return; }
            int firstConfirmedTick = _ledger.NextTick;
            if (!_ledger.TryCommit(Rollback, out IReadOnlyList<DashArenaEvent> confirmed))
            { Fail("CONFIRMED EVENT HISTORY LOST"); return; }
            CommittedEventCount += confirmed.Count;
            if (confirmed.Count != 0) NotifySubscribers(ConfirmedEvents, confirmed, nameof(ConfirmedEvents));
            Action<DashConfirmedFrame> frameSubscribers = ConfirmedFrame;
            if (frameSubscribers != null)
            {
                for (int tick = firstConfirmedTick; tick < _ledger.NextTick; tick++)
                {
                    if (!Rollback.TryGetConfirmedEvents(tick, out var events) ||
                        !Rollback.TryGetConfirmedInputs(tick, out var input0, out var input1))
                    { Fail("CONFIRMED EVENT HISTORY LOST"); return; }
                    foreach (Delegate subscriber in frameSubscribers.GetInvocationList())
                    {
                        if (!Rollback.TryGetConfirmedState(tick, out DashArenaState snapshot))
                        { Fail("CONFIRMED STATE HISTORY LOST"); return; }
                        try { ((Action<DashConfirmedFrame>)subscriber)(new DashConfirmedFrame(snapshot, events, input0, input1)); }
                        catch (Exception error)
                        {
                            PresentationErrorCount++;
                            LastPresentationError = nameof(ConfirmedFrame) + ": " + error.GetType().Name + ": " + error.Message;
                        }
                    }
                }
            }
            bool terminal = State.Phase == MatchState.MatchOver;
            bool complete = terminal && Rollback.ConfirmedStateTick >= State.Tick - 1;
            MatchDirector.HasWinner(State.Scores[0], State.Scores[1], Rules.PointsToWin, out int winner);
            Lifecycle.UpdateTerminal(terminal, complete, new NetFinishTuple
            {
                FinishTick = State.Tick - 1,
                Checksum = complete ? Rollback.ChecksumAt(State.Tick - 1) : 0,
                Score0 = State.Scores[0], Score1 = State.Scores[1], Winner = winner,
                SettingsHash = DashNetcode.ContentTag(Rules.ContentHash)
            });
            if (deadlines) Lifecycle.Tick();
            RetainResult();
            ApplyPreparedEpoch();
            if (Frozen) return;
            if (Lifecycle.FailureReason.Length != 0) { Fail(Lifecycle.FailureReason); return; }
            while (Lifecycle.TryDequeue(out DashLifecycleMessage outgoing))
                if (_peer.Send(DashNetcode.EncodeLifecycle(outgoing)))
                {
                    ControlsSent++;
                    if (outgoing.Kind == NetLifecycleKind.Heartbeat) HeartbeatsSent++;
                }
        }

        void ApplyPreparedEpoch()
        {
            if (!Lifecycle.TryTakePreparedEpoch(out int epoch, out uint seed)) return;
            _handshakeGraceUntil = -1f;
            InitializeEpoch(epoch, seed);
        }

        void InitializeEpoch(int epoch, uint seed)
        {
            DashArenaState state;
            if (_initialStateFactory == null)
            {
                state = DashArenaSimulation.Create(Rules, seed, epoch);
                DashArenaSimulation.StartMatch(state);
            }
            else
            {
                try
                {
                    state = _initialStateFactory(Rules, seed, epoch);
                    if (state == null || state.Tick != 0 || state.Epoch != epoch || state.InitialSeed != seed ||
                        state.Rules.ContentHash != Rules.ContentHash)
                        throw new InvalidOperationException("Initial-state factory must preserve agreed profile/seed/epoch and tick-zero baseline.");
                }
                catch (Exception error)
                {
                    InitializationError = error.GetType().Name + ": " + error.Message;
                    Fail("INVALID INITIAL STATE");
                    return;
                }
            }
            Rollback = new DashRollbackSession(state, LocalSeat);
            _ledger.ResetForEpoch(epoch);
            _newestLocalInput = -1; _nextHistoryRetry = 0f; LastRollbackTick = -1;
            _inputWaitSince = -1f; InputWaitCount = 0;
            _completedInputWaitSeconds = _completedInputWaitMaxSeconds = 0f;
            _lastInputProgressAt = _clock(); _inputProgressStoppedAt = -1f;
            InitializationCount++;
            NotifySubscribers(EpochInitialized, epoch, nameof(EpochInitialized));
        }

        void NotifySubscribers<T>(Action<T> subscribers, T value, string notification)
        {
            if (subscribers == null) return;
            foreach (Delegate subscriber in subscribers.GetInvocationList())
            {
                try { ((Action<T>)subscriber)(value); }
                catch (Exception error)
                {
                    PresentationErrorCount++;
                    LastPresentationError = notification + ": " + error.GetType().Name + ": " + error.Message;
                }
            }
        }

        void RetainResult()
        {
            if (!Lifecycle.ResultConfirmed || _lastRetainedEpoch == Epoch) return;
            _lastRetainedEpoch = Epoch;
            _results.Add(new DashLanEpochEvidence(Epoch, Seed, Rules.ContentHash, Lifecycle.ConfirmedFinish));
            if (_results.Count > RetainedResultLimit) _results.RemoveAt(0);
        }

        void SendHistory(int newest)
        {
            if (Left || Frozen || newest < DashRollbackSession.DelayTicks) return;
            int count = Rollback.CopyLocalHistory(newest, _history);
            if (count == 0) return;
            Rollback.TryGetConfirmedChecksum(State.Tick - 1, out int checksumTick, out int checksum);
            bool sent = _peer.Send(DashNetcode.EncodeInput(new NetInputPacket
            {
                MatchId = MatchId, Epoch = Epoch, Seat = (byte)LocalSeat, NewestTick = newest,
                HistoryCount = (byte)count, InputBits = _history, AckTick = Rollback.ConfirmedTick,
                ChecksumTick = checksumTick, Checksum = checksum
            }));
            if (sent) Lifecycle.RecordSentInput(newest);
        }

        void RetryHandshake(float now)
        {
            if (Handshake.TryBuildRetry(now, out byte[] retry)) _peer.Send(retry);
        }

        void CaptureRollback()
        {
            if (Rollback.LastRollbackTick >= 0)
                LastRollbackTick = LastRollbackTick < 0 ? Rollback.LastRollbackTick : Math.Min(LastRollbackTick, Rollback.LastRollbackTick);
        }

        void UpdateInputPacing(float now)
        {
            // The remote input frontier is DelayTicks ahead of its executed state.
            // If the slow peer's next tick is P, it has input through P + delay - 1
            // but a checksum only through P - 1. Stop at S <= P + buffer - 1:
            // P - 1 must remain inside the inclusive checksum window [S - buffer, S).
            // Using the full input-history window as the lead cap silently discards
            // every new slow-peer checksum during sustained asymmetric stepping.
            // A newest sample beyond the contiguous frontier proves a real interior hole:
            // never pace that case away; the unchanged rollback bound still rejects it.
            bool shouldWait = Lifecycle.Phase == LanLifecyclePhase.Playing &&
                State.Phase != MatchState.MatchOver && State.Phase != MatchState.Pregame &&
                Rollback.NewestRemoteTick == Rollback.ConfirmedTick &&
                State.Tick - Rollback.ConfirmedTick >= MaximumInputLeadTicks;
            if (!shouldWait) { EndInputWait(now); return; }
            if (!WaitingForRemoteInput)
            {
                _inputWaitSince = now; InputWaitCount++;
                _nextHistoryRetry = Math.Min(_nextHistoryRetry, now);
            }
            if (now - _lastInputProgressAt >= InputProgressTimeoutSeconds)
                Fail("INPUT PROGRESS TIMEOUT");
        }

        void EndInputWait(float now)
        {
            if (!WaitingForRemoteInput) return;
            float seconds = Math.Max(0f, now - _inputWaitSince);
            _completedInputWaitSeconds += seconds;
            _completedInputWaitMaxSeconds = Math.Max(_completedInputWaitMaxSeconds, seconds);
            _inputWaitSince = -1f;
        }

        bool RecordFailure(string reason, string origin, string peerReason)
        {
            if (Frozen || Left) return false;
            StopObservations();
            FailureReason = reason;
            FailureOrigin = origin; PeerFailureReason = peerReason;
            return true;
        }

        void StopObservations()
        {
            if (_observationsStoppedAt >= 0f) return;
            _observationsStoppedAt = _clock();
            EndInputWait(_observationsStoppedAt); _inputProgressStoppedAt = _observationsStoppedAt;
        }

        void FailPeer(string reason, string peerReason)
        {
            if (!RecordFailure(reason, "peer", peerReason)) return;
            _peer.Dispose();
        }

        void Fail(string reason)
        {
            if (!RecordFailure(reason, "local", "")) return;
            if (Lifecycle != null && _peer.Bound && _peer.Remote != null)
                _peer.Send(DashNetcode.EncodeFault(new DashPeerFaultMessage
                {
                    MatchId = MatchId, Epoch = Epoch, Seat = (byte)LocalSeat,
                    Reason = DashPeerFault.FromFailureReason(reason)
                }));
            _peer.Dispose();
        }

        void SendGoodbye()
        {
            if (_peer.Bound && MatchId != 0 && _peer.Remote != null)
                _peer.Send(DashNetcode.EncodeGoodbye(MatchId, Epoch));
        }

        public void Leave()
        {
            if (Left) return;
            StopObservations();
            if (!Frozen) SendGoodbye();
            Left = true; _handshakeFailureRetryUntil = -1f;
            _peer.Dispose();
        }

        public void Dispose() => Leave();
        static IPEndPoint Copy(IPEndPoint endpoint) => endpoint == null ? null : new IPEndPoint(endpoint.Address, endpoint.Port);
    }
}
