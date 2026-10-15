using System;
using System.Collections.Generic;

namespace PocketBump
{
    // Owns connection progress only. The caller drains transport before Tick, and supplies
    // terminal evidence only after rollback reconciliation; no Unity clock or simulation lives here.
    public sealed class DashLanLifecycle
    {
        public const float RetrySeconds = 0.25f;
        public const float HeartbeatSeconds = 0.5f;
        public const float DisconnectSeconds = 5f;
        public const float TerminalInputSeconds = 5f;
        public const float AgreementSeconds = 15f;
        public const float RematchSeconds = 15f;

        readonly bool _host;
        readonly int _matchId;
        readonly int _settingsHash;
        readonly string _contentHash;
        readonly Func<uint> _nextSeedFactory;
        readonly Func<float> _clock;
        readonly Queue<DashLifecycleMessage> _outgoing = new Queue<DashLifecycleMessage>();
        float _nextHeartbeat;
        float _nextRetry;
        float _settlingSince = -1f;
        float _completeSince = -1f;
        float _transitionSince = -1f;
        bool _localComplete;
        bool _remoteFinishSeen;
        bool _finishAcknowledged;
        bool _consentAcknowledged;
        NetFinishTuple _localFinish;
        NetFinishTuple _remoteFinish;
        bool _transitionActive;
        int _fromEpoch;
        int _nextEpoch;
        uint _fromSeed;
        uint _nextSeed;
        NetFinishTuple _transitionFinish;
        NetLifecycleKind _retryKind;
        bool _committed;
        bool _startAuthorized;
        bool _initialStartup;
        float _initialStartupSince;
        int _pendingPreparedEpoch = -1;
        uint _pendingPreparedSeed;
        int _remoteConfirmedTick = DashRollbackSession.DelayTicks - 1;

        public int Epoch { get; private set; }
        public uint Seed { get; private set; }
        public string ContentHash => _contentHash;
        public int LocalSeat => _host ? 0 : 1;
        public int RematchCount => Epoch;
        public LanLifecyclePhase Phase { get; private set; } = LanLifecyclePhase.Playing;
        public string FailureReason { get; private set; } = "";
        public bool ResultConfirmed { get; private set; }
        public bool LocalConsent { get; private set; }
        public bool RemoteConsent { get; private set; }
        public float LastAcceptedPeerTime { get; private set; }
        public int HighestSentTick { get; private set; } = -1;
        public int LastPeerAck { get; private set; } = -1;
        public NetFinishTuple ConfirmedFinish => ResultConfirmed ? _localFinish : default;
        public NetFinishTuple FinalTuple => _localFinish;
        public NetFinishTuple PreviousFinish { get; private set; }

        public DashLanLifecycle(bool isHost, int matchId, ArenaRulesProfile rules, uint initialSeed,
            Func<float> clock, Func<uint> nextSeedFactory = null, bool initialStartup = false)
        {
            if (matchId == 0) throw new ArgumentOutOfRangeException(nameof(matchId));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (initialSeed == 0) throw new ArgumentOutOfRangeException(nameof(initialSeed));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _host = isHost;
            _matchId = matchId;
            _contentHash = rules.ContentHash;
            _settingsHash = DashNetcode.ContentTag(_contentHash);
            _nextSeedFactory = nextSeedFactory ?? NewHostSeed;
            Seed = initialSeed;
            LastAcceptedPeerTime = _clock();
            _nextHeartbeat = LastAcceptedPeerTime;
            _nextRetry = LastAcceptedPeerTime;
            if (initialStartup)
            {
                // A locally complete Joiner may still need to resend its final confirmation.
                // Use the same bounded input exchange as rematch before ordinary tick advance.
                _initialStartup = true;
                _initialStartupSince = LastAcceptedPeerTime;
                _startAuthorized = true;
                Phase = LanLifecyclePhase.StartupBarrier;
            }
        }

        public void UpdateTerminal(bool isMatchOver, bool inputComplete, NetFinishTuple finish)
        {
            if (Phase == LanLifecyclePhase.Failed || _initialStartup || _transitionActive || ResultConfirmed) return;
            if (!isMatchOver)
            {
                if (Phase == LanLifecyclePhase.SettlingResult)
                {
                    Phase = LanLifecyclePhase.Playing;
                    _settlingSince = _completeSince = -1f;
                    _localComplete = _finishAcknowledged = false;
                    _localFinish = default;
                    // A previously received terminal claim is retained until our corrected history
                    // reaches a terminal state; merely being behind is not a desync.
                }
                return;
            }

            if (Phase != LanLifecyclePhase.SettlingResult)
            {
                Phase = LanLifecyclePhase.SettlingResult;
                _settlingSince = _clock();
            }
            if (!inputComplete) return;
            if (!ValidFinish(finish)) throw new ArgumentException("Invalid local terminal evidence.", nameof(finish));
            if (_localComplete && _localFinish.Equals(finish)) return;
            _localComplete = true;
            _localFinish = finish;
            _finishAcknowledged = false;
            if (_completeSince < 0f) _completeSince = _clock();
            SendCurrent(NetLifecycleKind.Finish, finish);
            _nextRetry = _clock() + RetrySeconds;
            CompareFinish();
        }

        public bool RequestRematch()
        {
            if (!ResultConfirmed || Phase == LanLifecyclePhase.Failed || _transitionActive) return false;
            if (LocalConsent) return true;
            LocalConsent = true;
            Phase = LanLifecyclePhase.WaitingForPeerChoice;
            SendCurrent(NetLifecycleKind.RematchIntent, _localFinish);
            _nextRetry = _clock() + RetrySeconds;
            MaybeOffer();
            return true;
        }

        public bool Receive(DashLifecycleMessage message)
        {
            if (Phase == LanLifecyclePhase.Failed || message.MatchId != _matchId ||
                message.Seat != 1 - LocalSeat || message.Epoch < 0 ||
                !string.Equals(message.ContentHash, _contentHash, StringComparison.Ordinal) ||
                message.Seed == 0 || message.Kind < NetLifecycleKind.Heartbeat ||
                message.Kind > NetLifecycleKind.Start) return false;
            bool transition = message.Kind >= NetLifecycleKind.Offer;
            if (!transition && (message.Seed != Seed ||
                (message.Kind != NetLifecycleKind.Heartbeat &&
                 (message.NextEpoch != message.Epoch || message.NextSeed != 0)))) return false;
            bool accepted;
            switch (message.Kind)
            {
                case NetLifecycleKind.Heartbeat:
                    accepted = message.Epoch == Epoch;
                    break;
                case NetLifecycleKind.Finish:
                    accepted = message.Epoch == Epoch && !_transitionActive && ValidFinish(message.Finish);
                    if (accepted)
                    {
                        _remoteFinishSeen = true;
                        _remoteFinish = message.Finish;
                        CompareFinish();
                    }
                    break;
                case NetLifecycleKind.FinishAck:
                    accepted = message.Epoch == Epoch && !_transitionActive && _localComplete &&
                               message.Finish.Equals(_localFinish);
                    if (accepted)
                    {
                        _finishAcknowledged = true;
                        MaybeConfirmResult();
                    }
                    break;
                case NetLifecycleKind.RematchIntent:
                    accepted = message.Epoch == Epoch && ResultConfirmed && !_committed &&
                               message.Finish.Equals(_localFinish);
                    if (accepted)
                    {
                        RemoteConsent = true;
                        SendCurrent(NetLifecycleKind.RematchIntentAck, _localFinish);
                        MaybeOffer();
                    }
                    break;
                case NetLifecycleKind.RematchIntentAck:
                    accepted = message.Epoch == Epoch && ResultConfirmed && LocalConsent &&
                               message.Finish.Equals(_localFinish);
                    if (accepted) _consentAcknowledged = true;
                    break;
                default:
                    accepted = ReceiveTransition(message);
                    break;
            }
            // Cached replies from the old epoch permit convergence, but old traffic can never
            // keep the current match alive. Its current-epoch heartbeat or input must arrive.
            if (accepted && message.Epoch == Epoch) LastAcceptedPeerTime = _clock();
            return accepted;
        }

        public bool IsCurrentEpoch(int matchId, int epoch)
        {
            return Phase != LanLifecyclePhase.Failed && matchId == _matchId && epoch == Epoch;
        }

        public bool CanAcceptInput(int matchId, int epoch, int seat, int ackTick)
        {
            return IsCurrentEpoch(matchId, epoch) && seat == 1 - LocalSeat && ackTick >= -1 &&
                   ackTick <= Math.Max(HighestSentTick, DashRollbackSession.DelayTicks - 1);
        }

        // Invoke only after the rollback session has accepted real input from this packet.
        public bool AcceptInput(int matchId, int epoch, int seat, int newestTick, int ackTick,
            int confirmedRemoteTick)
        {
            if (!CanAcceptInput(matchId, epoch, seat, ackTick) || newestTick < 0) return false;
            LastAcceptedPeerTime = _clock();
            LastPeerAck = Math.Max(LastPeerAck, ackTick);
            _remoteConfirmedTick = Math.Max(_remoteConfirmedTick, confirmedRemoteTick);
            if (Phase == LanLifecyclePhase.StartupBarrier && _committed && !_host)
                _startAuthorized = true; // The committed host's real input is an implicit Start.
            MaybeReleaseStartup();
            return true;
        }

        public void RecordSentInput(int newestTick)
        {
            if (newestTick >= 0) HighestSentTick = Math.Max(HighestSentTick, newestTick);
        }

        public bool ReceiveGoodbye(int matchId, int epoch)
        {
            if (!IsCurrentEpoch(matchId, epoch)) return false;
            LastAcceptedPeerTime = _clock();
            Fail("PEER LEFT");
            return true;
        }

        public bool ReceiveFault(DashPeerFaultMessage message)
        {
            if (!IsCurrentEpoch(message.MatchId, message.Epoch) || message.Seat != 1 - LocalSeat ||
                !DashPeerFault.IsValid(message.Reason)) return false;
            LastAcceptedPeerTime = _clock();
            Fail("PEER FAULT");
            return true;
        }

        public bool TryTakePreparedEpoch(out int epoch, out uint seed)
        {
            epoch = _pendingPreparedEpoch;
            seed = epoch >= 0 ? _pendingPreparedSeed : 0;
            if (epoch < 0) return false;
            _pendingPreparedEpoch = -1;
            _pendingPreparedSeed = 0;
            return true;
        }

        public bool CanAdvance(int simulationTick)
        {
            if (Phase == LanLifecyclePhase.Playing) return true;
            if (Phase != LanLifecyclePhase.StartupBarrier || !_startAuthorized || simulationTick < 0)
                return false;
            if (simulationTick == 0) return true;
            // Only one additional safe tick may follow the first acknowledged exchange. The
            // second sample must itself arrive and be acknowledged before ordinary advancement.
            return simulationTick == 1 && LastPeerAck >= DashRollbackSession.DelayTicks &&
                   _remoteConfirmedTick >= DashRollbackSession.DelayTicks;
        }

        public void Tick()
        {
            if (Phase == LanLifecyclePhase.Failed) return;
            float now = _clock();
            if (Phase == LanLifecyclePhase.SettlingResult)
            {
                if (!_localComplete && now - _settlingSince >= TerminalInputSeconds)
                    Fail("INPUT HOLE");
                else if (_localComplete && now - _completeSince >= AgreementSeconds)
                    Fail("RESULT CONFIRMATION TIMEOUT");
            }
            if (_transitionActive && now - _transitionSince >= RematchSeconds)
                Fail("REMATCH TIMEOUT");
            if (_initialStartup && now - _initialStartupSince >= RematchSeconds)
                Fail("LAN HANDSHAKE TIMEOUT");
            if (Phase == LanLifecyclePhase.Failed) return;
            if (now - LastAcceptedPeerTime >= DisconnectSeconds)
            {
                Fail("PEER TIMEOUT");
                return;
            }
            if (now >= _nextHeartbeat)
            {
                SendCurrent(NetLifecycleKind.Heartbeat, default);
                _nextHeartbeat = now + HeartbeatSeconds;
            }
            if (now < _nextRetry) return;
            _nextRetry = now + RetrySeconds;
            if (_transitionActive)
                SendTransition(_retryKind);
            else
            {
                if (_localComplete) SendCurrent(NetLifecycleKind.Finish, _localFinish);
                if (LocalConsent && !_consentAcknowledged)
                    SendCurrent(NetLifecycleKind.RematchIntent, _localFinish);
            }
        }

        public bool TryDequeue(out DashLifecycleMessage message)
        {
            if (_outgoing.Count == 0) { message = default; return false; }
            message = _outgoing.Dequeue();
            return true;
        }

        bool ValidFinish(NetFinishTuple finish)
        {
            return finish.FinishTick >= 0 && finish.FinishTick <= DashNetcode.MaximumTick &&
                   finish.Score0 >= 0 && finish.Score1 >= 0 &&
                   (finish.Winner == 0 || finish.Winner == 1) && finish.SettingsHash == _settingsHash;
        }

        void CompareFinish()
        {
            if (!_localComplete || !_remoteFinishSeen) return;
            if (!_localFinish.Equals(_remoteFinish)) { Fail("DESYNC"); return; }
            SendCurrent(NetLifecycleKind.FinishAck, _localFinish);
            MaybeConfirmResult();
        }

        void MaybeConfirmResult()
        {
            if (ResultConfirmed || !_localComplete || !_remoteFinishSeen || !_finishAcknowledged ||
                !_localFinish.Equals(_remoteFinish)) return;
            ResultConfirmed = true;
            Phase = LanLifecyclePhase.ResultsReady;
            _settlingSince = _completeSince = -1f;
        }

        void MaybeOffer()
        {
            if (!_host || !LocalConsent || !RemoteConsent || _transitionActive) return;
            if (Epoch == int.MaxValue) { Fail("REMATCH EPOCH EXHAUSTED"); return; }
            // Only the host samples, exactly once when starting an offer. All retries and
            // subsequent controls reuse this cached proposal, including after local commit.
            uint nextSeed;
            try { nextSeed = _nextSeedFactory(); }
            catch (Exception) { Fail("REMATCH SEED ERROR"); return; }
            if (nextSeed == 0) { Fail("INVALID REMATCH SEED"); return; }
            BeginTransition(Epoch, Epoch + 1, Seed, nextSeed, _localFinish);
            _retryKind = NetLifecycleKind.Offer;
            SendTransition(_retryKind);
        }

        void BeginTransition(int fromEpoch, int nextEpoch, uint fromSeed, uint nextSeed, NetFinishTuple finish)
        {
            _transitionActive = true;
            _fromEpoch = fromEpoch;
            _nextEpoch = nextEpoch;
            _fromSeed = fromSeed;
            _nextSeed = nextSeed;
            _transitionFinish = finish;
            _transitionSince = _clock();
            _nextRetry = _clock() + RetrySeconds;
            Phase = LanLifecyclePhase.PreparingNextEpoch;
        }

        bool ReceiveTransition(DashLifecycleMessage message)
        {
            if (message.Kind < NetLifecycleKind.Offer || message.Kind > NetLifecycleKind.Start) return false;
            if (!_transitionActive)
            {
                if (_host || message.Kind != NetLifecycleKind.Offer || !ResultConfirmed || !LocalConsent ||
                    message.Epoch != Epoch || Epoch == int.MaxValue || message.NextEpoch != Epoch + 1 ||
                    message.Seed != Seed || message.NextSeed == 0 ||
                    !message.Finish.Equals(_localFinish)) return false;
                RemoteConsent = true;
                _consentAcknowledged = true;
                BeginTransition(message.Epoch, message.NextEpoch, message.Seed, message.NextSeed, message.Finish);
                _retryKind = NetLifecycleKind.Prepared;
            }
            if (message.Epoch != _fromEpoch || message.NextEpoch != _nextEpoch ||
                message.Seed != _fromSeed || message.NextSeed != _nextSeed ||
                !message.Finish.Equals(_transitionFinish)) return false;

            if (_host)
            {
                if (message.Kind == NetLifecycleKind.Prepared)
                {
                    if (!_committed)
                    {
                        PrepareEpoch();
                        _retryKind = NetLifecycleKind.Commit;
                    }
                    SendTransition(NetLifecycleKind.Commit);
                    return true;
                }
                if (message.Kind == NetLifecycleKind.CommitAck && _committed)
                {
                    _startAuthorized = true;
                    _retryKind = NetLifecycleKind.Start;
                    SendTransition(NetLifecycleKind.Start);
                    return true;
                }
            }
            else
            {
                if (message.Kind == NetLifecycleKind.Offer)
                {
                    SendTransition(NetLifecycleKind.Prepared);
                    return true;
                }
                if (message.Kind == NetLifecycleKind.Commit)
                {
                    if (!_committed)
                    {
                        PrepareEpoch();
                        _retryKind = NetLifecycleKind.CommitAck;
                    }
                    SendTransition(NetLifecycleKind.CommitAck);
                    return true;
                }
                if (message.Kind == NetLifecycleKind.Start && _committed)
                {
                    _startAuthorized = true;
                    return true;
                }
            }
            return false;
        }

        void PrepareEpoch()
        {
            _committed = true;
            PreviousFinish = _transitionFinish;
            Epoch = _nextEpoch;
            Seed = _nextSeed;
            _pendingPreparedEpoch = Epoch;
            _pendingPreparedSeed = Seed;
            HighestSentTick = LastPeerAck = -1;
            _remoteConfirmedTick = DashRollbackSession.DelayTicks - 1;
            _startAuthorized = false;
            _localComplete = _remoteFinishSeen = _finishAcknowledged = false;
            _localFinish = _remoteFinish = default;
            ResultConfirmed = LocalConsent = RemoteConsent = _consentAcknowledged = false;
            _settlingSince = _completeSince = -1f;
            Phase = LanLifecyclePhase.StartupBarrier;
            _nextHeartbeat = _clock();
            _outgoing.Clear(); // Never transmit stale terminal/consent messages after commitment.
        }

        void MaybeReleaseStartup()
        {
            if (Phase != LanLifecyclePhase.StartupBarrier || !_startAuthorized ||
                HighestSentTick < DashRollbackSession.DelayTicks + 1 ||
                LastPeerAck < DashRollbackSession.DelayTicks + 1 ||
                _remoteConfirmedTick < DashRollbackSession.DelayTicks + 1) return;
            Phase = LanLifecyclePhase.Playing;
            _initialStartup = false;
            _transitionActive = _committed = false;
            _transitionSince = -1f;
        }

        void SendCurrent(NetLifecycleKind kind, NetFinishTuple finish)
        {
            Enqueue(new DashLifecycleMessage
            {
                Kind = kind, MatchId = _matchId, Epoch = Epoch, NextEpoch = Epoch,
                Seat = (byte)LocalSeat, Finish = finish, ContentHash = _contentHash,
                Seed = Seed, NextSeed = 0
            });
        }

        void SendTransition(NetLifecycleKind kind)
        {
            Enqueue(new DashLifecycleMessage
            {
                Kind = kind, MatchId = _matchId, Epoch = _fromEpoch, NextEpoch = _nextEpoch,
                Seat = (byte)LocalSeat, Finish = _transitionFinish, ContentHash = _contentHash,
                Seed = _fromSeed, NextSeed = _nextSeed
            });
        }

        void Enqueue(DashLifecycleMessage message)
        {
            if (_outgoing.Count >= 64) _outgoing.Dequeue();
            _outgoing.Enqueue(message);
        }

        // Randomness is chosen by the host and explicitly transported. It never comes from
        // MatchId, wall clock, or a joiner's local simulation. Tests inject a counted factory.
        static uint NewHostSeed()
        {
            uint value = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0);
            return value == 0 ? ArenaRandom.DefaultSeed : value;
        }

        void Fail(string reason)
        {
            if (Phase == LanLifecyclePhase.Failed) return;
            FailureReason = reason;
            Phase = LanLifecyclePhase.Failed;
            _outgoing.Clear();
        }
    }
}
