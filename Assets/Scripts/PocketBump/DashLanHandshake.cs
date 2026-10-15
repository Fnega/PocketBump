using System;
using System.Net;

namespace PocketBump
{
    // Negotiates immutable content and an explicit gameplay seed before tick zero.
    // Completion still requires the lifecycle's two-way real input/ack startup barrier.
    public sealed class DashLanHandshake
    {
        readonly LanHandshakeRole role;
        readonly ArenaRulesProfile rules;
        readonly int hostMatchId;
        readonly uint hostSeed;
        float nextRetry;
        byte[] failurePacket;

        public LanHandshakeState State { get; private set; }
        public IPEndPoint RemoteEndpoint { get; private set; }
        public int MatchId { get; private set; }
        public int LocalSeat => role == LanHandshakeRole.Host ? 0 : 1;
        public ulong JoinNonce { get; private set; }
        public uint Seed { get; private set; }
        public ArenaRulesProfile Rules => rules;
        public string FailureMessage { get; private set; } = "";
        public bool IsComplete => State == LanHandshakeState.Complete;
        public bool IsFailed => State == LanHandshakeState.Failed;

        public DashLanHandshake(LanHandshakeRole role, ArenaRulesProfile rules, int hostMatchId, uint hostSeed,
            IPEndPoint remote = null, ulong joinNonce = 0)
        {
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            if (rules.IsProvisional) throw new ArgumentException("LAN requires a frozen production profile.", nameof(rules));
            if (role != LanHandshakeRole.Host && role != LanHandshakeRole.Joiner) throw new ArgumentOutOfRangeException(nameof(role));
            if (role == LanHandshakeRole.Host && (hostMatchId == 0 || hostSeed == 0))
                throw new ArgumentException("Host must choose a connection identity and a nonzero gameplay seed.");
            if (role == LanHandshakeRole.Joiner && remote == null) throw new ArgumentNullException(nameof(remote));
            this.role = role; this.hostMatchId = hostMatchId; this.hostSeed = hostSeed;
            RemoteEndpoint = Copy(remote);
            if (role == LanHandshakeRole.Host)
            {
                MatchId = hostMatchId; Seed = hostSeed; State = LanHandshakeState.HostWaitingJoin;
            }
            else
            {
                JoinNonce = joinNonce;
                while (JoinNonce == 0) JoinNonce = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
                State = LanHandshakeState.JoinerWaitingCapability;
            }
        }

        public bool TryBuildRetry(float now, out byte[] packet)
        {
            packet = null;
            if (IsComplete || now < nextRetry) return false;
            nextRetry = now + NetplayProtocol.HandshakeRetrySeconds;
            switch (State)
            {
                case LanHandshakeState.Failed: packet = failurePacket; break;
                case LanHandshakeState.HostWaitingJoinerReady:
                    packet = DashNetcode.EncodeJoinAck(true, MatchId, rules, JoinNonce, Seed); break;
                case LanHandshakeState.HostWaitingConfirmation:
                    packet = Ready(ReadyPhase.HostReady); break;
                case LanHandshakeState.JoinerWaitingCapability:
                    packet = DashNetcode.EncodeProbe(rules.PointsToWin, rules.BallCount); break;
                case LanHandshakeState.JoinerWaitingAck:
                    packet = DashNetcode.EncodeJoin(rules, JoinNonce); break;
                case LanHandshakeState.JoinerWaitingHostReady:
                    packet = Ready(ReadyPhase.JoinerReady); break;
            }
            return packet != null;
        }

        public bool Process(byte[] bytes, IPEndPoint from, float now, out byte[] response)
        {
            response = null;
            if (bytes == null || from == null || (RemoteEndpoint != null && !LanUdpPeer.EndpointsEqual(RemoteEndpoint, from)))
                return false;
            return role == LanHandshakeRole.Host
                ? ProcessHost(bytes, from, now, out response) : ProcessJoiner(bytes, now, out response);
        }

        bool ProcessHost(byte[] bytes, IPEndPoint from, float now, out byte[] response)
        {
            response = null;
            if (LanUdpPeer.TryDecodeJoin(bytes, out NetJoinMessage legacy))
            {
                if (State != LanHandshakeState.HostWaitingJoin) return false;
                if (legacy.Version == DashNetcode.Version && legacy.IsCompatibilityProbe)
                {
                    response = DashNetcode.EncodeCapability(); return true;
                }
                // A legacy-size packet is never a playable v7 Join, even if its version was patched.
                response = DashNetcode.EncodeLegacyRejection(hostMatchId, rules.PointsToWin, rules.BallCount, legacy.Nonce);
                RemoteEndpoint = Copy(from); Fail(response); return true;
            }
            if (DashNetcode.TryDecodeJoin(bytes, out DashJoinMessage join))
            {
                if (IsFailed) { response = failurePacket; return response != null; }
                if (State != LanHandshakeState.HostWaitingJoin && join.Nonce != JoinNonce) return false;
                bool matches = join.Points == rules.PointsToWin && join.Balls == rules.BallCount && join.ContentHash == rules.ContentHash;
                if (State != LanHandshakeState.HostWaitingJoin && !matches) return false;
                response = DashNetcode.EncodeJoinAck(matches, hostMatchId, rules, join.Nonce, hostSeed);
                RemoteEndpoint = Copy(from); nextRetry = now + NetplayProtocol.HandshakeRetrySeconds;
                if (!matches) { Fail(response); return true; }
                JoinNonce = join.Nonce;
                if (State == LanHandshakeState.HostWaitingJoin) State = LanHandshakeState.HostWaitingJoinerReady;
                return true;
            }
            if (RemoteEndpoint == null || !ValidReady(bytes, out DashReadyMessage ready)) return false;
            if (ready.Phase == ReadyPhase.JoinerReady &&
                (State == LanHandshakeState.HostWaitingJoinerReady || State == LanHandshakeState.HostWaitingConfirmation))
            {
                State = LanHandshakeState.HostWaitingConfirmation; response = Ready(ReadyPhase.HostReady);
                nextRetry = now + NetplayProtocol.HandshakeRetrySeconds; return true;
            }
            if (ready.Phase == ReadyPhase.JoinerConfirm &&
                (State == LanHandshakeState.HostWaitingConfirmation || IsComplete))
            {
                // HostReady -> JoinerConfirm is the final exchange. Answering a confirmation
                // with another HostReady makes two completed peers echo forever.
                State = LanHandshakeState.Complete; return true;
            }
            return false;
        }

        bool ProcessJoiner(byte[] bytes, float now, out byte[] response)
        {
            response = null;
            if (IsFailed) return false;
            if (LanUdpPeer.TryDecodeCapability(bytes, out NetCapabilityMessage capability))
            {
                if (State != LanHandshakeState.JoinerWaitingCapability && State != LanHandshakeState.JoinerWaitingAck) return false;
                if (capability.Version != DashNetcode.Version) { Fail(null); return true; }
                State = LanHandshakeState.JoinerWaitingAck; response = DashNetcode.EncodeJoin(rules, JoinNonce);
                nextRetry = now + NetplayProtocol.HandshakeRetrySeconds; return true;
            }
            if (LanUdpPeer.TryDecodeJoinAck(bytes, out NetJoinAckMessage legacy))
            {
                if (State != LanHandshakeState.JoinerWaitingCapability) return false;
                Fail(null); return true; // Legacy acceptance also cannot initialize this simulation.
            }
            if (DashNetcode.TryDecodeJoinAck(bytes, out DashJoinAckMessage ack))
            {
                if (State == LanHandshakeState.JoinerWaitingCapability || ack.Nonce != JoinNonce) return false;
                bool matches = ack.Accepted && ack.Points == rules.PointsToWin && ack.Balls == rules.BallCount && ack.ContentHash == rules.ContentHash;
                if (!matches) { if (IsComplete) return false; Fail(null); return true; }
                if (MatchId != 0 && (ack.MatchId != MatchId || ack.Seed != Seed)) return false;
                if (IsComplete) { response = Ready(ReadyPhase.JoinerConfirm); return true; }
                if (State != LanHandshakeState.JoinerWaitingAck && State != LanHandshakeState.JoinerWaitingHostReady) return false;
                MatchId = ack.MatchId; Seed = ack.Seed;
                State = LanHandshakeState.JoinerWaitingHostReady; response = Ready(ReadyPhase.JoinerReady);
                nextRetry = now + NetplayProtocol.HandshakeRetrySeconds; return true;
            }
            if (!ValidReady(bytes, out DashReadyMessage ready)) return false;
            if (ready.Phase == ReadyPhase.HostReady && (State == LanHandshakeState.JoinerWaitingHostReady || IsComplete))
            {
                State = LanHandshakeState.Complete; response = Ready(ReadyPhase.JoinerConfirm); return true;
            }
            return false;
        }

        byte[] Ready(ReadyPhase phase) => DashNetcode.EncodeReady(MatchId, JoinNonce, phase);
        bool ValidReady(byte[] bytes, out DashReadyMessage ready) => DashNetcode.TryDecodeReady(bytes, out ready) &&
            ready.MatchId == MatchId && ready.Nonce == JoinNonce;
        void Fail(byte[] packet)
        { failurePacket = packet; FailureMessage = "SETTINGS MISMATCH"; State = LanHandshakeState.Failed; }
        static IPEndPoint Copy(IPEndPoint endpoint) => endpoint == null ? null : new IPEndPoint(endpoint.Address, endpoint.Port);
    }
}
