using UnityEngine;

namespace PocketBump
{
    public enum RollbackStatus
    {
        Ok,
        InputHole,
        Desync
    }

    public sealed class RollbackSession
    {
        public const int DelayTicks = ArenaLayout.InputDelayTicks;
        public const int BufferTicks = ArenaLayout.RollbackBufferTicks;
        // Inputs arrive DelayTicks ahead of applyTick; keep those slots off the rollback window.
        const int StoreTicks = BufferTicks + DelayTicks + 1;

        readonly int _localSeat;
        readonly PlayerInput[] _local = new PlayerInput[StoreTicks];
        readonly PlayerInput[] _remote = new PlayerInput[StoreTicks];
        readonly PlayerInput[] _usedRemote = new PlayerInput[StoreTicks];
        readonly int[] _localTick = new int[StoreTicks];
        readonly int[] _remoteTick = new int[StoreTicks];
        readonly int[] _usedRemoteTick = new int[StoreTicks];
        readonly int[] _afterTick = new int[BufferTicks];
        readonly MatchSimState[] _after = new MatchSimState[BufferTicks];
        readonly int[] _checksums = new int[BufferTicks];
        readonly MatchSimState _initial;
        PlayerInput _lastRemote = PlayerInput.Neutral;
        bool _haveRemote;
        int _confirmedRemoteTick = -1;

        public MatchSimState State { get; }
        public int LocalSeat => _localSeat;
        public int ConfirmedTick => Mathf.Min(Mathf.Max(0, State.Tick - 1), _confirmedRemoteTick);
        public RollbackStatus Status { get; private set; }
        public int LastRollbackTick { get; private set; } = -1;

        public RollbackSession(MatchSimState initial, int localSeat)
        {
            _initial = initial.Clone();
            State = initial;
            _localSeat = localSeat;
            for (int i = 0; i < StoreTicks; i++)
            {
                _localTick[i] = -1;
                _remoteTick[i] = -1;
                _usedRemoteTick[i] = -1;
            }

            for (int i = 0; i < BufferTicks; i++)
                _afterTick[i] = -1;
        }

        public void QueueLocal(int tick, PlayerInput input)
        {
            Set(_local, _localTick, tick, input);
        }

        public void ReceiveRemote(int tick, PlayerInput input)
        {
            Set(_remote, _remoteTick, tick, input);
            _lastRemote = input;
            _haveRemote = true;
            if (tick > _confirmedRemoteTick)
                _confirmedRemoteTick = tick;
        }

        public SimEvents Advance(PlayerInput sampledNow, int? remoteChecksumTick = null, int remoteChecksum = 0)
        {
            Status = RollbackStatus.Ok;
            LastRollbackTick = -1;
            int applyTick = State.Tick;
            QueueLocal(applyTick + DelayTicks, sampledNow);

            int fallingOut = applyTick - BufferTicks;
            if (fallingOut >= DelayTicks && !HasStore(_remoteTick, fallingOut))
            {
                Status = RollbackStatus.InputHole;
                return default;
            }

            int rollbackFrom = -1;
            int minCheck = Mathf.Max(0, applyTick - BufferTicks + 1);
            for (int t = minCheck; t < applyTick; t++)
            {
                if (!HasStore(_remoteTick, t) || !HasStore(_usedRemoteTick, t)) continue;
                if (!_usedRemote[StoreIndex(t)].EqualsInput(_remote[StoreIndex(t)]))
                {
                    rollbackFrom = t;
                    break;
                }
            }

            if (rollbackFrom >= 0)
            {
                LastRollbackTick = rollbackFrom;
                RestoreBefore(rollbackFrom);
                for (int t = rollbackFrom; t < applyTick; t++)
                    StepStored(t);
            }

            SimEvents events = StepStored(applyTick);
            _after[BufIndex(applyTick)] = State.Clone();
            _afterTick[BufIndex(applyTick)] = applyTick;
            _checksums[BufIndex(applyTick)] = State.Checksum();

            if (remoteChecksumTick.HasValue &&
                remoteChecksumTick.Value >= 0 &&
                HasBuf(_afterTick, remoteChecksumTick.Value) &&
                _checksums[BufIndex(remoteChecksumTick.Value)] != remoteChecksum)
            {
                Status = RollbackStatus.Desync;
            }

            return events;
        }

        public int ChecksumAt(int tick)
        {
            if (!HasBuf(_afterTick, tick)) return 0;
            return _checksums[BufIndex(tick)];
        }

        SimEvents StepStored(int tick)
        {
            PlayerInput local = LocalFor(tick);
            PlayerInput remote = RemoteFor(tick);
            _usedRemote[StoreIndex(tick)] = remote;
            _usedRemoteTick[StoreIndex(tick)] = tick;
            PlayerInput i0 = _localSeat == 0 ? local : remote;
            PlayerInput i1 = _localSeat == 0 ? remote : local;
            return MatchSim.Step(State, i0, i1);
        }

        void RestoreBefore(int tick)
        {
            if (tick <= 0)
            {
                State.CopyFrom(_initial);
                return;
            }

            int prev = tick - 1;
            if (!HasBuf(_afterTick, prev))
            {
                State.CopyFrom(_initial);
                for (int t = 0; t < tick; t++)
                    StepStored(t);
                return;
            }

            State.CopyFrom(_after[BufIndex(prev)]);
        }

        PlayerInput LocalFor(int tick)
        {
            return HasStore(_localTick, tick) ? _local[StoreIndex(tick)] : PlayerInput.Neutral;
        }

        PlayerInput RemoteFor(int tick)
        {
            if (HasStore(_remoteTick, tick))
                return _remote[StoreIndex(tick)];
            return _haveRemote ? _lastRemote : PlayerInput.Neutral;
        }

        static void Set(PlayerInput[] arr, int[] stamps, int tick, PlayerInput input)
        {
            if (tick < 0) return;
            int i = StoreIndex(tick);
            arr[i] = input;
            stamps[i] = tick;
        }

        static bool HasStore(int[] stamps, int tick)
        {
            return tick >= 0 && stamps[StoreIndex(tick)] == tick;
        }

        static bool HasBuf(int[] stamps, int tick)
        {
            return tick >= 0 && stamps[BufIndex(tick)] == tick;
        }

        static int StoreIndex(int tick)
        {
            int i = tick % StoreTicks;
            return i < 0 ? i + StoreTicks : i;
        }

        static int BufIndex(int tick)
        {
            int i = tick % BufferTicks;
            return i < 0 ? i + BufferTicks : i;
        }
    }
}
