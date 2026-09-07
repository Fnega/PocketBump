using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace PocketBump
{
    public static class NetplayProtocol
    {
        public const int Version = 1;
        public const int Magic = 0x50423031;
        public const byte Join = 1;
        public const byte JoinAck = 2;
        public const byte Ready = 3;
        public const byte Input = 4;
        public const byte Goodbye = 5;

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
            return VersionsMatch(version) &&
                   points == hostPoints &&
                   marbles == hostMarbles &&
                   hash == SettingsHash(hostPoints, hostMarbles);
        }
    }

    public struct NetInputPacket
    {
        public int MatchId;
        public int Tick;
        public byte Seat;
        public byte InputBits;
        public int AckTick;
        public int ChecksumTick;
        public int Checksum;
    }

    public sealed class LanUdpPeer : IDisposable
    {
        UdpClient _udp;
        IPEndPoint _remote;
        public bool Bound { get; private set; }
        public int MatchId { get; set; }

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
            catch
            {
                Bound = false;
                return false;
            }
        }

        public bool BindEphemeral()
        {
            Close();
            try
            {
                _udp = new UdpClient(0);
                _udp.Client.Blocking = false;
                Bound = true;
                return true;
            }
            catch
            {
                Bound = false;
                return false;
            }
        }

        public void SetRemote(string host, int port)
        {
            _remote = new IPEndPoint(IPAddress.Parse(host), port);
        }

        public void SetRemote(IPEndPoint endPoint)
        {
            _remote = endPoint;
        }

        public IPEndPoint Remote => _remote;

        public void Send(byte[] data)
        {
            if (_udp == null || _remote == null) return;
            try
            {
                _udp.Send(data, data.Length, _remote);
            }
            catch
            {
            }
        }

        public bool TryRecv(out byte[] data, out IPEndPoint from)
        {
            data = null;
            from = null;
            if (_udp == null) return false;
            try
            {
                if (_udp.Available <= 0) return false;
                IPEndPoint any = new IPEndPoint(IPAddress.Any, 0);
                data = _udp.Receive(ref any);
                from = any;
                return data != null && data.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public static byte[] EncodeJoin(int points, int marbles)
        {
            var w = new ByteWriter();
            w.Magic();
            w.WriteByte(NetplayProtocol.Join);
            w.WriteInt(NetplayProtocol.Version);
            w.WriteInt(points);
            w.WriteInt(marbles);
            w.WriteInt(NetplayProtocol.SettingsHash(points, marbles));
            return w.Bytes();
        }

        public static byte[] EncodeJoinAck(bool ok, int seat, int matchId, int points, int marbles)
        {
            var w = new ByteWriter();
            w.Magic();
            w.WriteByte(NetplayProtocol.JoinAck);
            w.WriteByte(ok ? (byte)1 : (byte)0);
            w.WriteByte((byte)seat);
            w.WriteInt(matchId);
            w.WriteInt(NetplayProtocol.Version);
            w.WriteInt(points);
            w.WriteInt(marbles);
            return w.Bytes();
        }

        public static byte[] EncodeReady(int matchId)
        {
            var w = new ByteWriter();
            w.Magic();
            w.WriteByte(NetplayProtocol.Ready);
            w.WriteInt(matchId);
            return w.Bytes();
        }

        public static byte[] EncodeInput(NetInputPacket packet)
        {
            var w = new ByteWriter();
            w.Magic();
            w.WriteByte(NetplayProtocol.Input);
            w.WriteInt(packet.MatchId);
            w.WriteInt(packet.Tick);
            w.WriteByte(packet.Seat);
            w.WriteByte(packet.InputBits);
            w.WriteInt(packet.AckTick);
            w.WriteInt(packet.ChecksumTick);
            w.WriteInt(packet.Checksum);
            return w.Bytes();
        }

        public static byte[] EncodeGoodbye(int matchId)
        {
            var w = new ByteWriter();
            w.Magic();
            w.WriteByte(NetplayProtocol.Goodbye);
            w.WriteInt(matchId);
            return w.Bytes();
        }

        public static bool TryReadHeader(byte[] data, out byte type, out int offset)
        {
            type = 0;
            offset = 0;
            if (data == null || data.Length < 5) return false;
            int magic = BitConverter.ToInt32(data, 0);
            if (magic != NetplayProtocol.Magic) return false;
            type = data[4];
            offset = 5;
            return true;
        }

        public void Close()
        {
            Bound = false;
            if (_udp == null) return;
            try { _udp.Close(); }
            catch { }
            _udp = null;
        }

        public void Dispose()
        {
            Close();
        }

        sealed class ByteWriter
        {
            readonly MemoryStream _ms = new MemoryStream(64);

            public void Magic()
            {
                WriteInt(NetplayProtocol.Magic);
            }

            public void WriteByte(byte v)
            {
                _ms.WriteByte(v);
            }

            public void WriteInt(int v)
            {
                _ms.Write(BitConverter.GetBytes(v), 0, 4);
            }

            public byte[] Bytes()
            {
                return _ms.ToArray();
            }
        }
    }
}
