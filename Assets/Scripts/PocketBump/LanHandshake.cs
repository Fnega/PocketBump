using System;
using System.Net;

// 可重试、幂等的三阶段 LAN 握手。时间由调用方注入，便于稳定模拟丢包与乱序。
namespace PocketBump
{
    public enum LanHandshakeRole
    {
        Host,
        Joiner
    }

    public enum LanHandshakeState
    {
        HostWaitingJoin,
        HostWaitingJoinerReady,
        HostWaitingConfirmation,
        JoinerWaitingCapability,
        JoinerWaitingAck,
        JoinerWaitingHostReady,
        Complete,
        Failed
    }

    public sealed class LanHandshake
    {
        readonly LanHandshakeRole _role;
        readonly int _hostMatchId;
        float _nextRetryTime;
        byte[] _failurePacket;

        public LanHandshakeState State { get; private set; }
        public IPEndPoint RemoteEndpoint { get; private set; }
        public int MatchId { get; private set; }
        public int LocalSeat { get; private set; }
        public int Points { get; private set; }
        public int Marbles { get; private set; }
        public string FailureMessage { get; private set; }
        public ulong JoinNonce { get; private set; }
        public bool IsComplete => State == LanHandshakeState.Complete;
        public bool IsFailed => State == LanHandshakeState.Failed;

        public LanHandshake(LanHandshakeRole role, int points, int marbles, int hostMatchId, IPEndPoint remote = null,
            ulong joinNonce = 0)
        {
            _role = role;
            _hostMatchId = hostMatchId;
            Points = points;
            Marbles = marbles;
            RemoteEndpoint = Copy(remote);
            LocalSeat = role == LanHandshakeRole.Host ? 0 : 1;
            MatchId = role == LanHandshakeRole.Host ? hostMatchId : 0;
            if (role == LanHandshakeRole.Joiner)
            {
                JoinNonce = joinNonce;
                while (JoinNonce == 0) JoinNonce = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
            }
            State = role == LanHandshakeRole.Host
                ? LanHandshakeState.HostWaitingJoin
                : LanHandshakeState.JoinerWaitingCapability;
        }

        public bool TryBuildRetry(float now, out byte[] packet)
        {
            packet = null;
            if (State == LanHandshakeState.Complete || now < _nextRetryTime)
                return false;
            _nextRetryTime = now + NetplayProtocol.HandshakeRetrySeconds;

            if (State == LanHandshakeState.Failed)
            {
                packet = _failurePacket;
                return packet != null;
            }

            switch (State)
            {
                case LanHandshakeState.HostWaitingJoinerReady:
                    packet = LanUdpPeer.EncodeJoinAck(true, 1, MatchId, Points, Marbles, JoinNonce);
                    return true;
                case LanHandshakeState.HostWaitingConfirmation:
                    packet = LanUdpPeer.EncodeReady(MatchId, ReadyPhase.HostReady);
                    return true;
                case LanHandshakeState.JoinerWaitingCapability:
                    packet = LanUdpPeer.EncodeJoin(Points, Marbles);
                    return true;
                case LanHandshakeState.JoinerWaitingAck:
                    packet = LanUdpPeer.EncodeJoin(Points, Marbles, JoinNonce);
                    return true;
                case LanHandshakeState.JoinerWaitingHostReady:
                    packet = LanUdpPeer.EncodeReady(MatchId, ReadyPhase.JoinerReady);
                    return true;
                default:
                    return false;
            }
        }

        // 返回 true 表示该数据报属于当前握手；response 可立即发送。
        public bool Process(byte[] data, IPEndPoint from, float now, out byte[] response)
        {
            response = null;
            if (data == null || from == null) return false;
            if (RemoteEndpoint != null && !LanUdpPeer.EndpointsEqual(RemoteEndpoint, from)) return false;

            if (_role == LanHandshakeRole.Host)
                return ProcessHost(data, from, now, out response);
            return ProcessJoiner(data, from, now, out response);
        }

        bool ProcessHost(byte[] data, IPEndPoint from, float now, out byte[] response)
        {
            response = null;
            if (LanUdpPeer.TryDecodeJoin(data, out NetJoinMessage join))
            {
                if (State == LanHandshakeState.Failed)
                {
                    response = _failurePacket;
                    return response != null;
                }
                if (join.IsCompatibilityProbe && NetplayProtocol.VersionsMatch(join.Version))
                {
                    if (State != LanHandshakeState.HostWaitingJoin) return false;
                    // A capability probe never assigns a participant, nonce, or playable session.
                    response = LanUdpPeer.EncodeCapability();
                    return true;
                }
                if (State != LanHandshakeState.HostWaitingJoin &&
                    (join.IsCompatibilityProbe || join.Nonce != JoinNonce)) return false;
                bool ok = NetplayProtocol.CanAcceptJoin(
                    Points, Marbles, join.Version, join.Points, join.Marbles, join.SettingsHash);
                ok = ok && !join.IsCompatibilityProbe && _hostMatchId != 0;
                if (State != LanHandshakeState.HostWaitingJoin && !ok) return false;
                if (RemoteEndpoint == null) RemoteEndpoint = Copy(from);
                response = join.IsCompatibilityProbe
                    ? LanUdpPeer.EncodeJoinAck(false, 1, _hostMatchId, Points, Marbles)
                    : LanUdpPeer.EncodeJoinAck(ok, 1, _hostMatchId, Points, Marbles, join.Nonce);
                _nextRetryTime = now + NetplayProtocol.HandshakeRetrySeconds;
                if (!ok)
                {
                    _failurePacket = response;
                    Fail("SETTINGS MISMATCH");
                    return true;
                }

                MatchId = _hostMatchId;
                JoinNonce = join.Nonce;
                if (State == LanHandshakeState.HostWaitingJoin)
                    State = LanHandshakeState.HostWaitingJoinerReady;
                return true;
            }

            if (RemoteEndpoint == null || !LanUdpPeer.TryDecodeReady(data, out NetReadyMessage ready) ||
                ready.MatchId != MatchId || ready.Epoch != 0)
                return false;

            if (ready.Phase == ReadyPhase.JoinerReady &&
                (State == LanHandshakeState.HostWaitingJoinerReady || State == LanHandshakeState.HostWaitingConfirmation))
            {
                State = LanHandshakeState.HostWaitingConfirmation;
                response = LanUdpPeer.EncodeReady(MatchId, ReadyPhase.HostReady);
                _nextRetryTime = now + NetplayProtocol.HandshakeRetrySeconds;
                return true;
            }

            if (ready.Phase == ReadyPhase.JoinerConfirm &&
                (State == LanHandshakeState.HostWaitingConfirmation || State == LanHandshakeState.Complete))
            {
                State = LanHandshakeState.Complete;
                response = LanUdpPeer.EncodeReady(MatchId, ReadyPhase.HostReady);
                return true;
            }

            return false;
        }

        bool ProcessJoiner(byte[] data, IPEndPoint from, float now, out byte[] response)
        {
            response = null;
            if (State == LanHandshakeState.Failed) return false;
            if (LanUdpPeer.TryDecodeCapability(data, out NetCapabilityMessage capability))
            {
                if (State != LanHandshakeState.JoinerWaitingCapability && State != LanHandshakeState.JoinerWaitingAck)
                    return false;
                if (!NetplayProtocol.VersionsMatch(capability.Version))
                {
                    Fail("SETTINGS MISMATCH");
                    return true;
                }
                State = LanHandshakeState.JoinerWaitingAck;
                response = LanUdpPeer.EncodeJoin(Points, Marbles, JoinNonce);
                _nextRetryTime = now + NetplayProtocol.HandshakeRetrySeconds;
                return true;
            }
            if (LanUdpPeer.TryDecodeJoinAck(data, out NetJoinAckMessage ack))
            {
                if (ack.IsLegacy)
                {
                    if (State != LanHandshakeState.JoinerWaitingCapability) return false;
                    // Even a positive legacy acknowledgement cannot initialize a v6 session.
                    Fail("SETTINGS MISMATCH");
                    return true;
                }
                if (ack.Nonce != JoinNonce || State == LanHandshakeState.JoinerWaitingCapability) return false;
                if (!ack.Accepted || !NetplayProtocol.VersionsMatch(ack.Version))
                {
                    if (State == LanHandshakeState.Complete) return false;
                    Fail("SETTINGS MISMATCH");
                    return true;
                }
                if (ack.Seat != 1 || ack.MatchId == 0 || ack.Points != Points || ack.Marbles != Marbles) return false;
                if (State == LanHandshakeState.Complete)
                {
                    if (ack.MatchId != MatchId) return false;
                    response = LanUdpPeer.EncodeReady(MatchId, ReadyPhase.JoinerConfirm);
                    return true;
                }
                if (State != LanHandshakeState.JoinerWaitingAck && State != LanHandshakeState.JoinerWaitingHostReady)
                    return false;
                if (MatchId != 0 && ack.MatchId != MatchId) return false;
                MatchId = ack.MatchId;
                LocalSeat = ack.Seat;
                Points = ack.Points;
                Marbles = ack.Marbles;
                State = LanHandshakeState.JoinerWaitingHostReady;
                response = LanUdpPeer.EncodeReady(MatchId, ReadyPhase.JoinerReady);
                _nextRetryTime = now + NetplayProtocol.HandshakeRetrySeconds;
                return true;
            }

            if (!LanUdpPeer.TryDecodeReady(data, out NetReadyMessage ready) ||
                ready.MatchId != MatchId || ready.Epoch != 0)
                return false;

            if (ready.Phase == ReadyPhase.HostReady &&
                (State == LanHandshakeState.JoinerWaitingHostReady || State == LanHandshakeState.Complete))
            {
                State = LanHandshakeState.Complete;
                response = LanUdpPeer.EncodeReady(MatchId, ReadyPhase.JoinerConfirm);
                return true;
            }

            return false;
        }

        void Fail(string message)
        {
            FailureMessage = message;
            State = LanHandshakeState.Failed;
        }

        static IPEndPoint Copy(IPEndPoint endpoint)
        {
            return endpoint == null ? null : new IPEndPoint(endpoint.Address, endpoint.Port);
        }
    }
}
