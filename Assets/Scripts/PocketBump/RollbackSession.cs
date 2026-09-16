using UnityEngine;

// 管理双人输入延迟、远端输入预测、逐 Tick 快照和迟到输入回滚。
// 会话只重演 MatchSim，不触碰场景对象，因此修正历史的成本可控且结果可校验。
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
        // 输入会提前 DelayTicks 写入未来 Tick，因此输入环形表要比快照窗口多留这些槽位，
        // 否则未来输入可能覆盖仍处在回滚窗口内的旧输入。
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
            // initial 永久保留 Tick 0 前的基线；当最早 Tick 回滚且没有前一帧快照时从这里恢复。
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
            // 本地输入也按 Tick 存储，而不是在重演时重新读键盘；历史重演必须使用原始输入。
            Set(_local, _localTick, tick, input);
        }

        public void ReceiveRemote(int tick, PlayerInput input)
        {
            // 迟到输入先入表，Advance 会把它和当时实际使用的预测输入比较。
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
            // 双方统一延迟 2 Tick：用少量固定操作延迟换取更多报文按时到达，减少回滚频率。
            QueueLocal(applyTick + DelayTicks, sampledNow);

            int fallingOut = applyTick - BufferTicks;
            if (fallingOut >= DelayTicks && !HasStore(_remoteTick, fallingOut))
            {
                // 必需输入一旦老到将被环形缓冲覆盖，就无法再安全修正，只能报告连接缺口。
                Status = RollbackStatus.InputHole;
                return default;
            }

            int rollbackFrom = -1;
            int minCheck = Mathf.Max(0, applyTick - BufferTicks + 1);
            for (int t = minCheck; t < applyTick; t++)
            {
                // 只有真实输入和当时使用的输入都存在时才比较；最早的不一致 Tick 是回滚起点。
                if (!HasStore(_remoteTick, t) || !HasStore(_usedRemoteTick, t)) continue;
                if (!_usedRemote[StoreIndex(t)].EqualsInput(_remote[StoreIndex(t)]))
                {
                    rollbackFrom = t;
                    break;
                }
            }

            if (rollbackFrom >= 0)
            {
                // 恢复到错误发生前，然后按原本地输入 + 最新真实/预测远端输入重演到当前 Tick。
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
                // 相同 Tick 的权威状态哈希不同说明双方已分叉；宁可显式中止也不静默错局。
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
            // 记录“本次实际用了什么远端输入”，未来真实输入到达后才能知道预测是否错误。
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
            // 当前 Tick 未到达时沿用最近远端意图；连续移动比每帧回到 Neutral 更符合玩家行为。
            return _haveRemote ? _lastRemote : PlayerInput.Neutral;
        }

        static void Set(PlayerInput[] arr, int[] stamps, int tick, PlayerInput input)
        {
            if (tick < 0) return;
            int i = StoreIndex(tick);
            arr[i] = input;
            // 环形数组会重复使用槽位，stamp 用绝对 Tick 区分“当前数据”与旧周期残留。
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
