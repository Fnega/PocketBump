namespace PocketBump
{
    // Diagnostic classification only; never translates or rewrites wire payloads.
    public struct LanFaultPacketInfo
    {
        public int Protocol, Epoch, MatchId, NewestTick, AckTick;
        public bool IsInput, IsControl;
        public NetLifecycleKind Kind;

        public static LanFaultPacketInfo Read(byte[] data)
        {
            if (DashNetcode.TryDecodeInput(data, out NetInputPacket input))
                return Input(input, DashNetcode.Version);
            if (DashNetcode.TryDecodeLifecycle(data, out DashLifecycleMessage control))
                return new LanFaultPacketInfo { Protocol = DashNetcode.Version, IsControl = true,
                    Epoch = control.Epoch, MatchId = control.MatchId, Kind = control.Kind };
            if (LanUdpPeer.TryDecodeInput(data, out input)) return Input(input, NetplayProtocol.Version);
            if (LanUdpPeer.TryDecodeLifecycle(data, out NetLifecycleMessage legacy))
                return new LanFaultPacketInfo { Protocol = NetplayProtocol.Version, IsControl = true,
                    Epoch = legacy.Epoch, MatchId = legacy.MatchId, Kind = legacy.Kind };
            return default;
        }

        static LanFaultPacketInfo Input(NetInputPacket input, int version) => new LanFaultPacketInfo
        {
            Protocol = version, IsInput = true, Epoch = input.Epoch, MatchId = input.MatchId,
            NewestTick = input.NewestTick, AckTick = input.AckTick
        };
    }
}
