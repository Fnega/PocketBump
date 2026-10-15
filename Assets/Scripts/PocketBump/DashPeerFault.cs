namespace PocketBump
{
    // Wire values are stable and bounded. Never accept arbitrary strings from a peer.
    public enum DashPeerFaultReason : byte
    {
        PeerTimeout = 1,
        InputProgressTimeout = 2,
        Desync = 3,
        InputHole = 4,
        ConfirmedEventHistoryLost = 5,
        ConfirmedStateHistoryLost = 6,
        InvalidInitialState = 7,
        MatchTickExhausted = 8,
        HandshakeTimeout = 9,
        ResultConfirmationTimeout = 10,
        RematchTimeout = 11,
        RematchEpochExhausted = 12,
        RematchSeedError = 13,
        InvalidRematchSeed = 14,
        InternalError = 15
    }

    public struct DashPeerFaultMessage
    {
        public int MatchId, Epoch;
        public byte Seat;
        public DashPeerFaultReason Reason;
    }

    public static class DashPeerFault
    {
        public static bool IsValid(DashPeerFaultReason reason) =>
            reason >= DashPeerFaultReason.PeerTimeout && reason <= DashPeerFaultReason.InternalError;

        public static string ToFailureReason(DashPeerFaultReason reason)
        {
            switch (reason)
            {
                case DashPeerFaultReason.PeerTimeout: return "PEER TIMEOUT";
                case DashPeerFaultReason.InputProgressTimeout: return "INPUT PROGRESS TIMEOUT";
                case DashPeerFaultReason.Desync: return "DESYNC";
                case DashPeerFaultReason.InputHole: return "INPUT HOLE";
                case DashPeerFaultReason.ConfirmedEventHistoryLost: return "CONFIRMED EVENT HISTORY LOST";
                case DashPeerFaultReason.ConfirmedStateHistoryLost: return "CONFIRMED STATE HISTORY LOST";
                case DashPeerFaultReason.InvalidInitialState: return "INVALID INITIAL STATE";
                case DashPeerFaultReason.MatchTickExhausted: return "MATCH TICK EXHAUSTED";
                case DashPeerFaultReason.HandshakeTimeout: return "LAN HANDSHAKE TIMEOUT";
                case DashPeerFaultReason.ResultConfirmationTimeout: return "RESULT CONFIRMATION TIMEOUT";
                case DashPeerFaultReason.RematchTimeout: return "REMATCH TIMEOUT";
                case DashPeerFaultReason.RematchEpochExhausted: return "REMATCH EPOCH EXHAUSTED";
                case DashPeerFaultReason.RematchSeedError: return "REMATCH SEED ERROR";
                case DashPeerFaultReason.InvalidRematchSeed: return "INVALID REMATCH SEED";
                case DashPeerFaultReason.InternalError: return "INTERNAL ERROR";
                default: return "";
            }
        }

        public static DashPeerFaultReason FromFailureReason(string reason)
        {
            for (int value = (int)DashPeerFaultReason.PeerTimeout; value <= (int)DashPeerFaultReason.InternalError; value++)
                if (ToFailureReason((DashPeerFaultReason)value) == reason) return (DashPeerFaultReason)value;
            return DashPeerFaultReason.InternalError;
        }
    }
}
