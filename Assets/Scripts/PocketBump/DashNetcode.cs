using System;

namespace PocketBump
{
    public struct DashJoinMessage
    {
        public int Version, Points, Balls, SeedPolicy;
        public ulong Nonce;
        public string ContentHash;
    }

    public struct DashJoinAckMessage
    {
        public bool Accepted;
        public byte Seat;
        public int MatchId, Version, Points, Balls, SeedPolicy;
        public ulong Nonce;
        public uint Seed;
        public string ContentHash;
    }

    public struct DashReadyMessage
    {
        public int MatchId, Epoch;
        public ulong Nonce;
        public ReadyPhase Phase;
    }

    public struct DashLifecycleMessage
    {
        public NetLifecycleKind Kind;
        public int MatchId, Epoch, NextEpoch;
        public byte Seat;
        public NetFinishTuple Finish;
        public string ContentHash;
        public uint Seed, NextSeed;
    }

    // The v6 codec remains isolated until the new director is switched atomically.
    // v8 retains the canonical content hash and distinguishes faults from voluntary leave.
    public static class DashNetcode
    {
        public const int Version = 8;
        public const byte Fault = 8;
        public const int SeedPolicy = 1; // Host-selected nonzero xorshift32 seed per epoch.
        public const int HistoryLength = 8;
        public const int FutureTickAllowance = 32;
        public const int MaximumTick = int.MaxValue - 64;
        const int Header = 5, SessionHeader = 17;
        const int JoinSize = 65, AckSize = 71, ReadySize = 26, InputFixedSize = 35;
        const int HeartbeatSize = 55, LifecycleSize = 87;

        public static int ContentTag(string contentHash)
        {
            if (!ValidHash(contentHash)) throw new ArgumentException("Expected canonical SHA256.", nameof(contentHash));
            return unchecked((int)Convert.ToUInt32(contentHash.Substring(0, 8), 16));
        }

        public static byte[] EncodeProbe(int points, int balls)
        {
            RequireSettings(points, balls);
            byte[] bytes = Begin(NetplayProtocol.Join, 21);
            int offset = Header;
            int advertised = DevelopmentLanFaults.AdvertisedVersion(Version);
            Write(bytes, ref offset, advertised); Write(bytes, ref offset, points);
            Write(bytes, ref offset, balls); Write(bytes, ref offset, (points * 397) ^ balls ^ (advertised * 17));
            return bytes;
        }

        public static byte[] EncodeCapability()
        {
            byte[] bytes = Begin(NetplayProtocol.Capability, 9);
            int offset = Header; Write(bytes, ref offset, DevelopmentLanFaults.AdvertisedVersion(Version)); return bytes;
        }

        // A known legacy peer must receive a legacy-sized rejection it can actually parse.
        public static byte[] EncodeLegacyRejection(int matchId, int points, int balls, ulong nonce = 0)
        {
            byte[] bytes = nonce == 0
                ? LanUdpPeer.EncodeJoinAck(false, 1, matchId, points, balls)
                : LanUdpPeer.EncodeJoinAck(false, 1, matchId, points, balls, nonce);
            int offset = 11; Write(bytes, ref offset, Version); return bytes;
        }

        public static byte[] EncodeJoin(ArenaRulesProfile rules, ulong nonce)
        {
            if (rules == null || nonce == 0) throw new ArgumentException("Rules and a fresh nonce are required.");
            byte[] bytes = Begin(NetplayProtocol.Join, JoinSize);
            int offset = Header;
            Write(bytes, ref offset, Version); Write(bytes, ref offset, rules.PointsToWin);
            Write(bytes, ref offset, rules.BallCount); Write(bytes, ref offset, ContentTag(rules.ContentHash));
            WriteNonce(bytes, ref offset, nonce); WriteHash(bytes, ref offset, rules.ContentHash);
            Write(bytes, ref offset, SeedPolicy); return bytes;
        }

        public static bool TryDecodeJoin(byte[] bytes, out DashJoinMessage message)
        {
            message = default;
            if (!Exact(bytes, NetplayProtocol.Join, JoinSize)) return false;
            int offset = Header;
            message.Version = Read(bytes, ref offset); message.Points = Read(bytes, ref offset);
            message.Balls = Read(bytes, ref offset); int tag = Read(bytes, ref offset);
            message.Nonce = ReadNonce(bytes, ref offset); message.ContentHash = ReadHash(bytes, ref offset);
            message.SeedPolicy = Read(bytes, ref offset);
            return message.Version == Version && ValidSettings(message.Points, message.Balls) &&
                message.Nonce != 0 && message.SeedPolicy == SeedPolicy && tag == ContentTag(message.ContentHash);
        }

        public static byte[] EncodeJoinAck(bool accepted, int matchId, ArenaRulesProfile rules, ulong nonce, uint seed)
        {
            if (rules == null || matchId == 0 || nonce == 0 || seed == 0) throw new ArgumentException("Invalid initialization.");
            byte[] bytes = Begin(NetplayProtocol.JoinAck, AckSize);
            int offset = Header;
            bytes[offset++] = accepted ? (byte)1 : (byte)0; bytes[offset++] = 1;
            Write(bytes, ref offset, matchId); Write(bytes, ref offset, Version);
            Write(bytes, ref offset, rules.PointsToWin); Write(bytes, ref offset, rules.BallCount);
            WriteNonce(bytes, ref offset, nonce); WriteHash(bytes, ref offset, rules.ContentHash);
            Write(bytes, ref offset, unchecked((int)seed)); Write(bytes, ref offset, SeedPolicy); return bytes;
        }

        public static bool TryDecodeJoinAck(byte[] bytes, out DashJoinAckMessage message)
        {
            message = default;
            if (!Exact(bytes, NetplayProtocol.JoinAck, AckSize)) return false;
            int offset = Header;
            byte accepted = bytes[offset++]; message.Accepted = accepted == 1; message.Seat = bytes[offset++];
            message.MatchId = Read(bytes, ref offset); message.Version = Read(bytes, ref offset);
            message.Points = Read(bytes, ref offset); message.Balls = Read(bytes, ref offset);
            message.Nonce = ReadNonce(bytes, ref offset); message.ContentHash = ReadHash(bytes, ref offset);
            message.Seed = unchecked((uint)Read(bytes, ref offset)); message.SeedPolicy = Read(bytes, ref offset);
            return accepted <= 1 && message.Seat == 1 && message.MatchId != 0 && message.Version == Version &&
                ValidSettings(message.Points, message.Balls) && message.Nonce != 0 && message.Seed != 0 &&
                message.SeedPolicy == SeedPolicy;
        }

        public static byte[] EncodeReady(int matchId, ulong nonce, ReadyPhase phase)
        {
            if (nonce == 0 || phase < ReadyPhase.JoinerReady || phase > ReadyPhase.JoinerConfirm)
                throw new ArgumentException("Invalid readiness confirmation.");
            byte[] bytes = BeginSession(NetplayProtocol.Ready, ReadySize, matchId, 0);
            int offset = SessionHeader; WriteNonce(bytes, ref offset, nonce); bytes[offset] = (byte)phase; return bytes;
        }

        public static bool TryDecodeReady(byte[] bytes, out DashReadyMessage message)
        {
            message = default;
            if (!Exact(bytes, NetplayProtocol.Ready, ReadySize) ||
                !ReadSession(bytes, out message.MatchId, out message.Epoch) || message.Epoch != 0) return false;
            int offset = SessionHeader; message.Nonce = ReadNonce(bytes, ref offset); message.Phase = (ReadyPhase)bytes[offset];
            return message.Nonce != 0 && message.Phase >= ReadyPhase.JoinerReady && message.Phase <= ReadyPhase.JoinerConfirm;
        }

        public static byte[] EncodeInput(NetInputPacket packet)
        {
            int count = packet.HistoryCount;
            if (packet.InputBits == null || packet.InputBits.Length < count || !ValidInputHeader(packet))
                throw new ArgumentException("Invalid input packet.", nameof(packet));
            for (int i = 0; i < count; i++)
                if (!DashArenaInput.TryUnpack(packet.InputBits[i], out _)) throw new ArgumentException("Invalid Dash input.");
            byte[] bytes = BeginSession(NetplayProtocol.Input, InputFixedSize + count, packet.MatchId, packet.Epoch);
            int offset = SessionHeader; Write(bytes, ref offset, packet.NewestTick);
            bytes[offset++] = packet.Seat; bytes[offset++] = (byte)count;
            Array.Copy(packet.InputBits, 0, bytes, offset, count); offset += count;
            Write(bytes, ref offset, packet.AckTick); Write(bytes, ref offset, packet.ChecksumTick);
            Write(bytes, ref offset, packet.Checksum); return bytes;
        }

        public static bool TryDecodeInput(byte[] bytes, out NetInputPacket packet)
        {
            packet = default;
            if (!Type(bytes, NetplayProtocol.Input) || bytes.Length < InputFixedSize + 1 ||
                !ReadSession(bytes, out packet.MatchId, out packet.Epoch)) return false;
            int offset = SessionHeader; packet.NewestTick = Read(bytes, ref offset);
            packet.Seat = bytes[offset++]; packet.HistoryCount = bytes[offset++];
            int count = packet.HistoryCount;
            if (count < 1 || count > HistoryLength || bytes.Length != InputFixedSize + count) return false;
            int historyOffset = offset; offset += count;
            packet.AckTick = Read(bytes, ref offset); packet.ChecksumTick = Read(bytes, ref offset);
            packet.Checksum = Read(bytes, ref offset);
            if (!ValidInputHeader(packet)) return false;
            for (int i = 0; i < count; i++)
                if (!DashArenaInput.TryUnpack(bytes[historyOffset + i], out _)) return false;
            packet.InputBits = new byte[count]; Array.Copy(bytes, historyOffset, packet.InputBits, 0, count); return true;
        }

        static bool ValidInputHeader(NetInputPacket packet) => packet.Seat <= 1 &&
            packet.HistoryCount >= 1 && packet.HistoryCount <= HistoryLength &&
            packet.NewestTick >= packet.HistoryCount - 1 && packet.NewestTick <= MaximumTick &&
            packet.AckTick >= -1 && packet.AckTick <= MaximumTick &&
            packet.ChecksumTick >= -1 && packet.ChecksumTick <= MaximumTick &&
            (packet.ChecksumTick != -1 || packet.Checksum == 0);

        public static byte[] EncodeGoodbye(int matchId, int epoch)
            => BeginSession(NetplayProtocol.Goodbye, SessionHeader, matchId, epoch);

        public static bool TryDecodeGoodbye(byte[] bytes, out NetGoodbyeMessage message)
        {
            message = default;
            return Exact(bytes, NetplayProtocol.Goodbye, SessionHeader) &&
                ReadSession(bytes, out message.MatchId, out message.Epoch);
        }

        public static byte[] EncodeFault(DashPeerFaultMessage message)
        {
            if (message.Seat > 1 || !DashPeerFault.IsValid(message.Reason))
                throw new ArgumentException("Invalid peer fault.", nameof(message));
            byte[] bytes = BeginSession(Fault, SessionHeader + 2, message.MatchId, message.Epoch);
            bytes[SessionHeader] = message.Seat; bytes[SessionHeader + 1] = (byte)message.Reason;
            return bytes;
        }

        public static bool TryDecodeFault(byte[] bytes, out DashPeerFaultMessage message)
        {
            message = default;
            if (!Exact(bytes, Fault, SessionHeader + 2) ||
                !ReadSession(bytes, out message.MatchId, out message.Epoch)) return false;
            message.Seat = bytes[SessionHeader]; message.Reason = (DashPeerFaultReason)bytes[SessionHeader + 1];
            return message.Seat <= 1 && DashPeerFault.IsValid(message.Reason);
        }

        public static byte[] EncodeLifecycle(DashLifecycleMessage message)
        {
            if (!ValidLifecycle(message)) throw new ArgumentException("Invalid lifecycle evidence.", nameof(message));
            bool heartbeat = message.Kind == NetLifecycleKind.Heartbeat;
            byte[] bytes = BeginSession(NetplayProtocol.Lifecycle, heartbeat ? HeartbeatSize : LifecycleSize, message.MatchId, message.Epoch);
            int offset = SessionHeader; bytes[offset++] = message.Seat; bytes[offset++] = (byte)message.Kind;
            WriteHash(bytes, ref offset, message.ContentHash); Write(bytes, ref offset, unchecked((int)message.Seed));
            if (!heartbeat)
            {
                Write(bytes, ref offset, message.NextEpoch); Write(bytes, ref offset, unchecked((int)message.NextSeed));
                Write(bytes, ref offset, message.Finish.FinishTick); Write(bytes, ref offset, message.Finish.Checksum);
                Write(bytes, ref offset, message.Finish.Score0); Write(bytes, ref offset, message.Finish.Score1);
                Write(bytes, ref offset, message.Finish.Winner); Write(bytes, ref offset, message.Finish.SettingsHash);
            }
            return bytes;
        }

        public static bool TryDecodeLifecycle(byte[] bytes, out DashLifecycleMessage message)
        {
            message = default;
            if (!Type(bytes, NetplayProtocol.Lifecycle) || bytes.Length < HeartbeatSize ||
                !ReadSession(bytes, out message.MatchId, out message.Epoch)) return false;
            int offset = SessionHeader; message.Seat = bytes[offset++]; message.Kind = (NetLifecycleKind)bytes[offset++];
            bool heartbeat = message.Kind == NetLifecycleKind.Heartbeat;
            if (bytes.Length != (heartbeat ? HeartbeatSize : LifecycleSize)) return false;
            message.ContentHash = ReadHash(bytes, ref offset); message.Seed = unchecked((uint)Read(bytes, ref offset));
            if (!heartbeat)
            {
                message.NextEpoch = Read(bytes, ref offset); message.NextSeed = unchecked((uint)Read(bytes, ref offset));
                message.Finish.FinishTick = Read(bytes, ref offset); message.Finish.Checksum = Read(bytes, ref offset);
                message.Finish.Score0 = Read(bytes, ref offset); message.Finish.Score1 = Read(bytes, ref offset);
                message.Finish.Winner = Read(bytes, ref offset); message.Finish.SettingsHash = Read(bytes, ref offset);
            }
            return ValidLifecycle(message);
        }

        static bool ValidLifecycle(DashLifecycleMessage message)
        {
            if (message.Seat > 1 || message.Kind < NetLifecycleKind.Heartbeat || message.Kind > NetLifecycleKind.Start ||
                !ValidHash(message.ContentHash) || message.Seed == 0) return false;
            if (message.Kind == NetLifecycleKind.Heartbeat) return true;
            bool transition = message.Kind >= NetLifecycleKind.Offer;
            return message.Finish.FinishTick >= 0 && message.Finish.FinishTick <= MaximumTick &&
                message.Finish.Score0 >= 0 && message.Finish.Score1 >= 0 && message.Finish.Winner >= 0 && message.Finish.Winner <= 1 &&
                message.Finish.SettingsHash == ContentTag(message.ContentHash) &&
                (transition ? message.Epoch < int.MaxValue && message.NextEpoch == message.Epoch + 1 && message.NextSeed != 0
                            : message.NextEpoch == message.Epoch && message.NextSeed == 0);
        }

        static byte[] BeginSession(byte type, int length, int matchId, int epoch)
        {
            if (matchId == 0 || epoch < 0) throw new ArgumentException("Invalid session identity.");
            byte[] bytes = Begin(type, length); int offset = Header;
            Write(bytes, ref offset, Version); Write(bytes, ref offset, matchId); Write(bytes, ref offset, epoch); return bytes;
        }

        static bool ReadSession(byte[] bytes, out int matchId, out int epoch)
        {
            int offset = Header; int version = Read(bytes, ref offset);
            matchId = Read(bytes, ref offset); epoch = Read(bytes, ref offset);
            return version == Version && matchId != 0 && epoch >= 0;
        }

        static byte[] Begin(byte type, int length)
        {
            byte[] bytes = new byte[length]; int offset = 0;
            Write(bytes, ref offset, NetplayProtocol.Magic); bytes[offset] = type; return bytes;
        }
        internal static bool TryReadMessageType(byte[] bytes, out byte type)
        {
            type = 0;
            if (bytes == null || bytes.Length < Header) return false;
            int offset = 0;
            if (Read(bytes, ref offset) != NetplayProtocol.Magic) return false;
            type = bytes[offset];
            return type >= NetplayProtocol.Join && type <= Fault;
        }
        static bool Type(byte[] bytes, byte type) => TryReadMessageType(bytes, out byte found) && found == type;
        static bool Exact(byte[] bytes, byte type, int length) => bytes != null && bytes.Length == length && Type(bytes, type);
        static bool ValidSettings(int points, int balls) => points >= 1 && points <= 21 && balls >= 1 && balls <= 8;
        static void RequireSettings(int points, int balls)
        { if (!ValidSettings(points, balls)) throw new ArgumentException("Invalid match settings."); }
        static bool ValidHash(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char ch in value) if (!(ch >= '0' && ch <= '9') && !(ch >= 'a' && ch <= 'f')) return false;
            return true;
        }
        static void WriteHash(byte[] bytes, ref int offset, string value)
        { for (int i = 0; i < 64; i += 2) bytes[offset++] = Convert.ToByte(value.Substring(i, 2), 16); }
        static string ReadHash(byte[] bytes, ref int offset)
        { string hash = BitConverter.ToString(bytes, offset, 32).Replace("-", "").ToLowerInvariant(); offset += 32; return hash; }
        static void WriteNonce(byte[] bytes, ref int offset, ulong value)
        { Write(bytes, ref offset, unchecked((int)value)); Write(bytes, ref offset, unchecked((int)(value >> 32))); }
        static ulong ReadNonce(byte[] bytes, ref int offset)
        { uint low = unchecked((uint)Read(bytes, ref offset)); return low | ((ulong)unchecked((uint)Read(bytes, ref offset)) << 32); }
        static void Write(byte[] bytes, ref int offset, int value)
        { unchecked { for (int i = 0; i < 4; i++) bytes[offset++] = (byte)(value >> (8 * i)); } }
        static int Read(byte[] bytes, ref int offset)
        { int value = bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24; offset += 4; return value; }
    }
}
