using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

// 定义局域网对战的应用层 UDP 报文，并封装非阻塞收发。
// 所有解码入口先验证长度和类型，网络数据不会以裸偏移形式进入游戏循环。
namespace PocketBump
{
    public enum ReadyPhase : byte
    {
        JoinerReady = 1,
        HostReady = 2,
        JoinerConfirm = 3
    }

    public static class NetplayProtocol
    {
        // v6 retains v5 physics and adds nonce-bound connections and per-match epochs.
        public const int Version = 6;
        public const int Magic = 0x50423031;
        public const byte Join = 1;
        public const byte JoinAck = 2;
        public const byte Ready = 3;
        public const byte Input = 4;
        public const byte Goodbye = 5;
        public const byte Capability = 6;
        public const byte Lifecycle = 7;
        public const int InputHistoryLength = 8;
        public const float HandshakeRetrySeconds = 0.25f;
        public const float HandshakeGraceSeconds = 1f;
        public const int FutureTickAllowance = 32;

        public static int SettingsHash(int pointsToWin, int marbleCount)
        {
            unchecked
            {
                return (pointsToWin * 397) ^ marbleCount ^ (Version * 17);
            }
        }

        public static bool VersionsMatch(int remoteVersion)
        {
            return remoteVersion == Version;
        }

        public static bool CanAcceptJoin(int hostPoints, int hostMarbles, int version, int points, int marbles, int hash)
        {
            return VersionsMatch(version) && points == hostPoints && marbles == hostMarbles &&
                   hash == SettingsHash(hostPoints, hostMarbles);
        }
    }

    public struct NetJoinMessage
    {
        public int Version;
        public int Points;
        public int Marbles;
        public int SettingsHash;
        public ulong Nonce;
        public bool IsCompatibilityProbe;
    }

    public struct NetJoinAckMessage
    {
        public bool Accepted;
        public byte Seat;
        public int MatchId;
        public int Version;
        public int Points;
        public int Marbles;
        public ulong Nonce;
        public bool IsLegacy;
    }

    public struct NetCapabilityMessage
    {
        public int Version;
    }

    public struct NetReadyMessage
    {
        public int MatchId;
        public int Epoch;
        public ReadyPhase Phase;
    }

    public struct NetGoodbyeMessage
    {
        public int MatchId;
        public int Epoch;
    }

    public struct NetInputPacket
    {
        public int MatchId;
        public int Epoch;
        public int NewestTick;
        public byte Seat;
        public byte HistoryCount;
        // Newest-to-oldest: InputBits[0] belongs to NewestTick.
        public byte[] InputBits;
        public int AckTick;
        public int ChecksumTick;
        public int Checksum;
    }

    public enum NetLifecycleKind : byte
    {
        Heartbeat = 1,
        Finish = 2,
        FinishAck = 3,
        RematchIntent = 4,
        RematchIntentAck = 5,
        Offer = 6,
        Prepared = 7,
        Commit = 8,
        CommitAck = 9,
        Start = 10
    }

    public struct NetFinishTuple : IEquatable<NetFinishTuple>
    {
        public int FinishTick;
        public int Checksum;
        public int Score0;
        public int Score1;
        public int Winner;
        public int SettingsHash;

        public bool Equals(NetFinishTuple other)
        {
            return FinishTick == other.FinishTick && Checksum == other.Checksum &&
                   Score0 == other.Score0 && Score1 == other.Score1 &&
                   Winner == other.Winner && SettingsHash == other.SettingsHash;
        }

        public override bool Equals(object obj) { return obj is NetFinishTuple other && Equals(other); }
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (FinishTick * 397) ^ Checksum;
                hash = (hash * 397) ^ Score0;
                hash = (hash * 397) ^ Score1;
                hash = (hash * 397) ^ Winner;
                return (hash * 397) ^ SettingsHash;
            }
        }
    }

    public struct NetLifecycleMessage
    {
        public NetLifecycleKind Kind;
        public int MatchId;
        public int Epoch;
        public byte Seat;
        public int NextEpoch;
        public NetFinishTuple Finish;
    }

    public sealed class LanUdpPeer : IDisposable
    {
        const int HeaderSize = 5;
        const int LegacyJoinSize = HeaderSize + 16;
        const int JoinSize = LegacyJoinSize + 8;
        const int LegacyJoinAckSize = HeaderSize + 2 + 16;
        const int JoinAckSize = LegacyJoinAckSize + 8;
        const int SessionHeaderSize = HeaderSize + 12;
        const int ReadySize = SessionHeaderSize + 1;
        const int GoodbyeSize = SessionHeaderSize;
        const int InputFixedSize = SessionHeaderSize + 6 + 12;
        const int LifecycleHeaderSize = SessionHeaderSize + 2;
        const int LifecycleSize = LifecycleHeaderSize + 28;

        UdpClient _udp;
        IPEndPoint _remote;
        public bool Bound { get; private set; }
        public int MatchId { get; set; }
        public int SendAttempts { get; private set; }
        public int SentDatagrams { get; private set; }
        public int SendFailures { get; private set; }
        public string LastSocketError { get; private set; } = "";
        public int ReceivePolls { get; private set; }
        public int EmptyReceivePolls { get; private set; }
        public int ReceivedDatagrams { get; private set; }
        public int ReceiveFailures { get; private set; }
        public string LastReceiveError { get; private set; } = "";
        public IPEndPoint Remote => _remote;
        public IPEndPoint LocalEndPoint => _udp != null ? _udp.Client.LocalEndPoint as IPEndPoint : null;
        public static int InjectedFaults => DevelopmentLanFaults.InjectedFaults;
        public static void ActivatePeerSilence() { DevelopmentLanFaults.ActivatePeerSilence(); }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        public static void ActivateDirectionalSilence() { DevelopmentLanFaults.ActivateDirectionalSilence(); }
#endif

        public bool Bind(int port)
        {
            Close();
            try
            {
                _udp = new UdpClient(port);
                _udp.Client.Blocking = false;
                Bound = true;
                return true;
            }
            catch (SocketException error)
            {
                Bound = false;
                LastSocketError = "BIND: " + error.SocketErrorCode + "(" + error.ErrorCode + ")";
                return false;
            }
        }

        public bool BindEphemeral()
        {
            return Bind(0);
        }

        public void SetRemote(string host, int port)
        {
            _remote = new IPEndPoint(IPAddress.Parse(host), port);
        }

        public void SetRemote(IPEndPoint endPoint)
        {
            _remote = endPoint == null ? null : new IPEndPoint(endPoint.Address, endPoint.Port);
        }

        public bool IsRemote(IPEndPoint endPoint)
        {
            return EndpointsEqual(_remote, endPoint);
        }

        public static bool EndpointsEqual(IPEndPoint a, IPEndPoint b)
        {
            return a != null && b != null && a.Port == b.Port && Equals(a.Address, b.Address);
        }

        public bool Send(byte[] data)
        {
            return SendTo(data, _remote);
        }

        // Capability probes must be answered without binding a playable participant.
        public bool SendTo(byte[] data, IPEndPoint endpoint)
        {
            SendAttempts++;
            if (_udp == null || endpoint == null || data == null)
            {
                SendFailures++;
                LastSocketError = _udp == null ? "SOCKET_NOT_BOUND" : endpoint == null ? "REMOTE_NOT_SET" : "EMPTY_DATAGRAM";
                return false;
            }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (DevelopmentLanFaults.ProcessSend(this, data, endpoint, out bool delivered))
            {
                if (!delivered) LastSocketError = "FAULT_INJECTED_DROP";
                return delivered;
            }
#endif
            return SendDirect(data, endpoint);
        }

        internal bool SendDirect(byte[] data, IPEndPoint endpoint)
        {
            try
            {
                int sent = _udp.Send(data, data.Length, endpoint);
                if (sent == data.Length)
                {
                    SentDatagrams++;
                    return true;
                }
                SendFailures++;
                LastSocketError = "PARTIAL_DATAGRAM_" + sent;
                return false;
            }
            catch (SocketException ex)
            {
                SendFailures++;
                LastSocketError = ex.SocketErrorCode + "(" + ex.ErrorCode + ")";
                return false;
            }
            catch (ObjectDisposedException)
            {
                SendFailures++;
                LastSocketError = "SOCKET_DISPOSED";
                return false;
            }
        }

        public bool TryRecv(out byte[] data, out IPEndPoint from)
        {
            ReceivePolls++;
            data = null;
            from = null;
            // Closing our own socket is intentional; an unexpectedly disposed live
            // socket, in contrast, is retained as a receive-path failure below.
            if (_udp == null) { EmptyReceivePolls++; return false; }
            try
            {
                Socket socket = _udp.Client;
                if (socket == null)
                {
                    ReceiveFailures++;
                    LastReceiveError = "SOCKET_DISPOSED";
                    return false;
                }
                // Poll also sees queued zero-byte datagrams. Avoid allocating a
                // WouldBlock exception on every normal empty game-loop poll.
                if (!socket.Poll(0, SelectMode.SelectRead)) { EmptyReceivePolls++; return false; }
                IPEndPoint any = new IPEndPoint(IPAddress.Any, 0);
                data = _udp.Receive(ref any);
                from = any;
                ReceivedDatagrams++;
                // Even an empty UDP datagram is raw traffic. The application codec
                // rejects it, without treating it as accepted peer activity.
                return true;
            }
            catch (SocketException error)
            {
                if (error.SocketErrorCode == SocketError.WouldBlock)
                    EmptyReceivePolls++;
                else
                {
                    ReceiveFailures++;
                    LastReceiveError = error.SocketErrorCode + "(" + error.ErrorCode + ")";
                }
                return false;
            }
            catch (ObjectDisposedException)
            {
                ReceiveFailures++;
                LastReceiveError = "SOCKET_DISPOSED";
                return false;
            }
        }

        public static byte[] EncodeJoin(int points, int marbles)
        {
            return EncodeJoinCore(points, marbles, 0);
        }

        public static byte[] EncodeJoin(int points, int marbles, ulong nonce)
        {
            if (nonce == 0) throw new ArgumentOutOfRangeException(nameof(nonce));
            return EncodeJoinCore(points, marbles, nonce);
        }

        static byte[] EncodeJoinCore(int points, int marbles, ulong nonce)
        {
            byte[] data = Begin(NetplayProtocol.Join, nonce == 0 ? LegacyJoinSize : JoinSize);
            int o = HeaderSize;
            WriteInt(data, ref o, DevelopmentLanFaults.AdvertisedVersion(NetplayProtocol.Version));
            WriteInt(data, ref o, points);
            WriteInt(data, ref o, marbles);
            WriteInt(data, ref o, NetplayProtocol.SettingsHash(points, marbles));
            if (nonce != 0) WriteNonce(data, ref o, nonce);
            return data;
        }

        public static bool TryDecodeJoin(byte[] data, out NetJoinMessage message)
        {
            message = default;
            if (!HasExactTypeAndLength(data, NetplayProtocol.Join, JoinSize) &&
                !HasExactTypeAndLength(data, NetplayProtocol.Join, LegacyJoinSize)) return false;
            int o = HeaderSize;
            message.Version = ReadInt(data, ref o);
            message.Points = ReadInt(data, ref o);
            message.Marbles = ReadInt(data, ref o);
            message.SettingsHash = ReadInt(data, ref o);
            message.IsCompatibilityProbe = data.Length == LegacyJoinSize;
            if (!message.IsCompatibilityProbe) message.Nonce = ReadNonce(data, ref o);
            return ValidSettings(message.Points, message.Marbles) &&
                   (message.IsCompatibilityProbe || message.Nonce != 0);
        }

        public static byte[] EncodeJoinAck(bool ok, int seat, int matchId, int points, int marbles)
        {
            return EncodeJoinAckCore(ok, seat, matchId, points, marbles, 0);
        }

        public static byte[] EncodeJoinAck(bool ok, int seat, int matchId, int points, int marbles, ulong nonce)
        {
            if (nonce == 0) throw new ArgumentOutOfRangeException(nameof(nonce));
            return EncodeJoinAckCore(ok, seat, matchId, points, marbles, nonce);
        }

        static byte[] EncodeJoinAckCore(bool ok, int seat, int matchId, int points, int marbles, ulong nonce)
        {
            byte[] data = Begin(NetplayProtocol.JoinAck, nonce == 0 ? LegacyJoinAckSize : JoinAckSize);
            int o = HeaderSize;
            data[o++] = ok ? (byte)1 : (byte)0;
            data[o++] = (byte)seat;
            WriteInt(data, ref o, matchId);
            WriteInt(data, ref o, NetplayProtocol.Version);
            WriteInt(data, ref o, points);
            WriteInt(data, ref o, marbles);
            if (nonce != 0) WriteNonce(data, ref o, nonce);
            return data;
        }

        public static bool TryDecodeJoinAck(byte[] data, out NetJoinAckMessage message)
        {
            message = default;
            if (!HasExactTypeAndLength(data, NetplayProtocol.JoinAck, JoinAckSize) &&
                !HasExactTypeAndLength(data, NetplayProtocol.JoinAck, LegacyJoinAckSize)) return false;
            int o = HeaderSize;
            byte accepted = data[o++];
            if (accepted > 1) return false;
            message.Accepted = accepted == 1;
            message.Seat = data[o++];
            message.MatchId = ReadInt(data, ref o);
            message.Version = ReadInt(data, ref o);
            message.Points = ReadInt(data, ref o);
            message.Marbles = ReadInt(data, ref o);
            message.IsLegacy = data.Length == LegacyJoinAckSize;
            if (!message.IsLegacy) message.Nonce = ReadNonce(data, ref o);
            return message.Seat <= 1 && ValidSettings(message.Points, message.Marbles) &&
                   (message.IsLegacy || message.Nonce != 0);
        }

        public static byte[] EncodeCapability()
        {
            byte[] data = Begin(NetplayProtocol.Capability, HeaderSize + 4);
            int o = HeaderSize;
            WriteInt(data, ref o, NetplayProtocol.Version);
            return data;
        }

        public static bool TryDecodeCapability(byte[] data, out NetCapabilityMessage message)
        {
            message = default;
            if (!HasExactTypeAndLength(data, NetplayProtocol.Capability, HeaderSize + 4)) return false;
            int o = HeaderSize;
            message.Version = ReadInt(data, ref o);
            return true;
        }

        public static byte[] EncodeReady(int matchId, ReadyPhase phase, int epoch = 0)
        {
            byte[] data = Begin(NetplayProtocol.Ready, ReadySize);
            int o = HeaderSize;
            WriteSession(data, ref o, matchId, epoch);
            data[o] = (byte)phase;
            return data;
        }

        public static byte[] EncodeReady(int matchId)
        {
            return EncodeReady(matchId, ReadyPhase.JoinerReady);
        }

        public static bool TryDecodeReady(byte[] data, out NetReadyMessage message)
        {
            message = default;
            if (!HasExactTypeAndLength(data, NetplayProtocol.Ready, ReadySize)) return false;
            int o = HeaderSize;
            if (!ReadSession(data, ref o, out message.MatchId, out message.Epoch)) return false;
            message.Phase = (ReadyPhase)data[o];
            return message.Phase >= ReadyPhase.JoinerReady && message.Phase <= ReadyPhase.JoinerConfirm;
        }

        public static int EncodedInputSize(int historyCount)
        {
            return InputFixedSize + historyCount;
        }

        public static byte[] EncodeInput(NetInputPacket packet)
        {
            int count = packet.HistoryCount;
            if (count < 1 || count > NetplayProtocol.InputHistoryLength || packet.InputBits == null || packet.InputBits.Length < count)
                throw new ArgumentException("Input history count is outside the protocol bounds.", nameof(packet));

            byte[] data = Begin(NetplayProtocol.Input, EncodedInputSize(count));
            int o = HeaderSize;
            WriteSession(data, ref o, packet.MatchId, packet.Epoch);
            WriteInt(data, ref o, packet.NewestTick);
            data[o++] = packet.Seat;
            data[o++] = (byte)count;
            for (int i = 0; i < count; i++) data[o++] = packet.InputBits[i];
            WriteInt(data, ref o, packet.AckTick);
            WriteInt(data, ref o, packet.ChecksumTick);
            WriteInt(data, ref o, packet.Checksum);
            return data;
        }

        public static bool TryDecodeInput(byte[] data, out NetInputPacket packet)
        {
            packet = default;
            if (!TryReadHeader(data, out byte type, out int o) || type != NetplayProtocol.Input) return false;
            if (data.Length < InputFixedSize + 1) return false;
            if (!ReadSession(data, ref o, out packet.MatchId, out packet.Epoch)) return false;
            packet.NewestTick = ReadInt(data, ref o);
            packet.Seat = data[o++];
            int count = data[o++];
            if (packet.Seat > 1 || count < 1 || count > NetplayProtocol.InputHistoryLength ||
                packet.NewestTick < count - 1) return false;
            if (data.Length != EncodedInputSize(count)) return false;
            packet.HistoryCount = (byte)count;
            packet.InputBits = new byte[count];
            for (int i = 0; i < count; i++)
            {
                byte bits = data[o++];
                if ((bits & 0xC0) != 0) return false;
                packet.InputBits[i] = bits;
            }
            packet.AckTick = ReadInt(data, ref o);
            packet.ChecksumTick = ReadInt(data, ref o);
            packet.Checksum = ReadInt(data, ref o);
            return packet.AckTick >= -1 && packet.ChecksumTick >= -1;
        }

        public static byte[] EncodeGoodbye(int matchId, int epoch = 0)
        {
            byte[] data = Begin(NetplayProtocol.Goodbye, GoodbyeSize);
            int o = HeaderSize;
            WriteSession(data, ref o, matchId, epoch);
            return data;
        }

        public static bool TryDecodeGoodbye(byte[] data, out NetGoodbyeMessage message)
        {
            message = default;
            if (!HasExactTypeAndLength(data, NetplayProtocol.Goodbye, GoodbyeSize)) return false;
            int o = HeaderSize;
            return ReadSession(data, ref o, out message.MatchId, out message.Epoch);
        }

        public static byte[] EncodeLifecycle(NetLifecycleMessage message)
        {
            bool heartbeat = message.Kind == NetLifecycleKind.Heartbeat;
            byte[] data = Begin(NetplayProtocol.Lifecycle, heartbeat ? LifecycleHeaderSize : LifecycleSize);
            int o = HeaderSize;
            WriteSession(data, ref o, message.MatchId, message.Epoch);
            data[o++] = message.Seat;
            data[o++] = (byte)message.Kind;
            if (!heartbeat)
            {
                WriteInt(data, ref o, message.NextEpoch);
                WriteInt(data, ref o, message.Finish.FinishTick);
                WriteInt(data, ref o, message.Finish.Checksum);
                WriteInt(data, ref o, message.Finish.Score0);
                WriteInt(data, ref o, message.Finish.Score1);
                WriteInt(data, ref o, message.Finish.Winner);
                WriteInt(data, ref o, message.Finish.SettingsHash);
            }
            return data;
        }

        public static bool TryDecodeLifecycle(byte[] data, out NetLifecycleMessage message)
        {
            message = default;
            if (!TryReadHeader(data, out byte type, out int o) || type != NetplayProtocol.Lifecycle ||
                data.Length < LifecycleHeaderSize) return false;
            if (!ReadSession(data, ref o, out message.MatchId, out message.Epoch)) return false;
            message.Seat = data[o++];
            message.Kind = (NetLifecycleKind)data[o++];
            if (message.Seat > 1 || message.Kind < NetLifecycleKind.Heartbeat || message.Kind > NetLifecycleKind.Start)
                return false;
            if (message.Kind == NetLifecycleKind.Heartbeat) return data.Length == LifecycleHeaderSize;
            if (data.Length != LifecycleSize) return false;
            message.NextEpoch = ReadInt(data, ref o);
            message.Finish.FinishTick = ReadInt(data, ref o);
            message.Finish.Checksum = ReadInt(data, ref o);
            message.Finish.Score0 = ReadInt(data, ref o);
            message.Finish.Score1 = ReadInt(data, ref o);
            message.Finish.Winner = ReadInt(data, ref o);
            message.Finish.SettingsHash = ReadInt(data, ref o);
            return message.NextEpoch >= 0 && message.Finish.FinishTick >= 0 &&
                   message.Finish.Score0 >= 0 && message.Finish.Score1 >= 0 &&
                   message.Finish.Winner >= 0 && message.Finish.Winner <= 1;
        }

        public static bool TryReadHeader(byte[] data, out byte type, out int offset)
        {
            type = 0;
            offset = 0;
            if (data == null || data.Length < HeaderSize) return false;
            int o = 0;
            if (ReadInt(data, ref o) != NetplayProtocol.Magic) return false;
            type = data[o++];
            offset = o;
            return type >= NetplayProtocol.Join && type <= NetplayProtocol.Lifecycle;
        }

        public void Close()
        {
            Bound = false;
            if (_udp == null) return;
            try { _udp.Close(); }
            catch (SocketException) { }
            _udp = null;
        }

        public void Dispose()
        {
            Close();
        }

        static bool HasExactTypeAndLength(byte[] data, byte expectedType, int length)
        {
            return data != null && data.Length == length &&
                   TryReadHeader(data, out byte type, out _) && type == expectedType;
        }

        static byte[] Begin(byte type, int length)
        {
            byte[] data = new byte[length];
            int o = 0;
            WriteInt(data, ref o, NetplayProtocol.Magic);
            data[o] = type;
            return data;
        }

        static bool ValidSettings(int points, int marbles)
        {
            return points >= MatchSettings.MinPointsToWin && points <= MatchSettings.MaxPointsToWin &&
                   marbles >= MatchSettings.MinMarbles && marbles <= MatchSettings.MaxMarbles;
        }

        static void WriteSession(byte[] data, ref int offset, int matchId, int epoch)
        {
            WriteInt(data, ref offset, NetplayProtocol.Version);
            WriteInt(data, ref offset, matchId);
            WriteInt(data, ref offset, epoch);
        }

        static bool ReadSession(byte[] data, ref int offset, out int matchId, out int epoch)
        {
            int version = ReadInt(data, ref offset);
            matchId = ReadInt(data, ref offset);
            epoch = ReadInt(data, ref offset);
            return version == NetplayProtocol.Version && matchId != 0 && epoch >= 0;
        }

        static void WriteNonce(byte[] data, ref int offset, ulong value)
        {
            WriteInt(data, ref offset, unchecked((int)value));
            WriteInt(data, ref offset, unchecked((int)(value >> 32)));
        }

        static ulong ReadNonce(byte[] data, ref int offset)
        {
            uint low = unchecked((uint)ReadInt(data, ref offset));
            uint high = unchecked((uint)ReadInt(data, ref offset));
            return low | ((ulong)high << 32);
        }

        static void WriteInt(byte[] data, ref int offset, int value)
        {
            unchecked
            {
                data[offset++] = (byte)value;
                data[offset++] = (byte)(value >> 8);
                data[offset++] = (byte)(value >> 16);
                data[offset++] = (byte)(value >> 24);
            }
        }

        static int ReadInt(byte[] data, ref int offset)
        {
            unchecked
            {
                int value = data[offset] |
                            data[offset + 1] << 8 |
                            data[offset + 2] << 16 |
                            data[offset + 3] << 24;
                offset += 4;
                return value;
            }
        }
    }

    // Development-build-only fault switches used by the two-player executable smoke test.
    // They are inert unless explicit --lan-smoke-* command-line flags are present.
    static class DevelopmentLanFaults
    {
        public static int InjectedFaults { get; private set; }
        public static void ActivatePeerSilence()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (Profile == "peer-silence") _silent = true;
#endif
        }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        static bool _directionalSilent;
        public static void ActivateDirectionalSilence()
        {
            // Only the explicit isolated scenario may arm this process-wide sender gate.
            if (ArgumentValue("--lan-scenario") != "fault-evidence" ||
                ArgumentValue("--lan-smoke") != ArgumentValue("--lan-silence-role"))
                throw new InvalidOperationException("Directional silence requires the explicitly selected diagnostic sender.");
            _directionalSilent = true;
        }
        static readonly HashSet<string> Dropped = new HashSet<string>();
        static readonly List<byte[]> RetiredPackets = new List<byte[]>();
        static readonly HashSet<string> RetiredKinds = new HashSet<string>();
        static byte[] _delayed;
        static IPEndPoint _delayedEndpoint;
        static bool _injectedRetired;
        static bool _silent;
        static bool DropFirst => HasArgument("--lan-smoke-drop-first");
        static bool AdvertiseOld => HasArgument("--lan-smoke-old-version");
        static readonly string Profile = ArgumentValue("--lan-fault-profile");

        public static int AdvertisedVersion(int current)
        {
            return AdvertiseOld ? current - 1 : current;
        }

        public static bool ProcessSend(LanUdpPeer peer, byte[] data, IPEndPoint endpoint, out bool delivered)
        {
            delivered = false;
            if (_directionalSilent) { InjectedFaults++; return true; }
            if (!DashNetcode.TryReadMessageType(data, out byte type)) return false;
            LanFaultPacketInfo packet = LanFaultPacketInfo.Read(data);
            bool input = packet.IsInput, control = packet.IsControl;
            int epoch = packet.Epoch;
            string key = control ? "lifecycle-" + packet.Kind + "-" + epoch : "input-" + epoch;

            if (Profile == "peer-silence")
            {
                if (_silent) { InjectedFaults++; return true; }
            }
            if (Profile == "stale-epoch")
            {
                bool retainKind = input || (control && (packet.Kind == NetLifecycleKind.Heartbeat ||
                                  packet.Kind == NetLifecycleKind.Finish));
                if (epoch == 0 && retainKind && RetiredPackets.Count < 4 && RetiredKinds.Add(key))
                {
                    RetiredPackets.Add((byte[])data.Clone());
                    if (RetiredPackets.Count == 1)
                        RetiredPackets.Add(packet.Protocol == DashNetcode.Version
                            ? DashNetcode.EncodeGoodbye(packet.MatchId, 0) : LanUdpPeer.EncodeGoodbye(packet.MatchId, 0));
                }
                if (!_injectedRetired && input && epoch > 0 && packet.NewestTick >= 8)
                {
                    foreach (byte[] retired in RetiredPackets) peer.SendDirect(retired, endpoint);
                    InjectedFaults += RetiredPackets.Count;
                    _injectedRetired = true;
                }
            }
            if (Profile == "duplicate-reorder")
            {
                if (_delayed != null)
                {
                    delivered = peer.SendDirect(data, endpoint);
                    peer.SendDirect(_delayed, _delayedEndpoint);
                    peer.SendDirect(_delayed, _delayedEndpoint);
                    _delayed = null;
                    InjectedFaults += 2;
                    return true;
                }
                if (control && packet.Kind != NetLifecycleKind.Heartbeat && Dropped.Add("reorder-" + key))
                {
                    _delayed = (byte[])data.Clone();
                    _delayedEndpoint = new IPEndPoint(endpoint.Address, endpoint.Port);
                    return true;
                }
            }

            bool selected = control && ProfileMatches(packet.Kind);
            selected |= input && epoch > 0 && (Profile == "drop-startup-input" ||
                        (Profile == "drop-startup-ack" && packet.AckTick >= RollbackSession.DelayTicks));
            if (selected && Dropped.Add("profile-" + key)) { InjectedFaults++; return true; }
            if (ShouldDropFirst(data, type)) { InjectedFaults++; return true; }
            return false;
        }

        static bool ProfileMatches(NetLifecycleKind kind)
        {
            switch (kind)
            {
                case NetLifecycleKind.Finish: return Profile == "drop-finish";
                case NetLifecycleKind.FinishAck: return Profile == "drop-finish-ack";
                case NetLifecycleKind.RematchIntent: return Profile == "drop-consent";
                case NetLifecycleKind.RematchIntentAck: return Profile == "drop-consent-ack";
                case NetLifecycleKind.Offer: return Profile == "drop-offer";
                case NetLifecycleKind.Prepared: return Profile == "drop-prepared";
                case NetLifecycleKind.Commit: return Profile == "drop-commit";
                case NetLifecycleKind.CommitAck: return Profile == "drop-commit-ack";
                case NetLifecycleKind.Start: return Profile == "drop-start";
                default: return false;
            }
        }

        static bool ShouldDropFirst(byte[] data, byte type)
        {
            if (!DropFirst || type > NetplayProtocol.Goodbye) return false;
            string key;
            if (type == NetplayProtocol.Ready && DashNetcode.TryDecodeReady(data, out DashReadyMessage dashReady))
                key = "ready-" + (byte)dashReady.Phase;
            else if (type == NetplayProtocol.Ready && LanUdpPeer.TryDecodeReady(data, out NetReadyMessage ready))
                key = "ready-" + (byte)ready.Phase;
            else
                key = "type-" + type;
            if (Dropped.Contains(key)) return false;
            Dropped.Add(key);
            return true;
        }

        static string ArgumentValue(string expected)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith(expected + "=", StringComparison.OrdinalIgnoreCase))
                    return args[i].Substring(expected.Length + 1).ToLowerInvariant();
                if (string.Equals(args[i], expected, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    return args[i + 1].ToLowerInvariant();
            }
            return "";
        }

        static bool HasArgument(string expected)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], expected, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
#else
        public static int AdvertisedVersion(int current) { return current; }
#endif
    }
}
