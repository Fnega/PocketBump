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
        // 额外保留窗口起点的前一帧，确保恰好迟到 BufferTicks 的输入仍可恢复。
        const int SnapshotTicks = BufferTicks + 1;
        // 输入会提前 DelayTicks 写入未来 Tick，因此输入环形表要比快照窗口多留这些槽位，
        // 否则未来输入可能覆盖仍处在回滚窗口内的旧输入。
        const int StoreTicks = BufferTicks + DelayTicks + NetplayProtocol.FutureTickAllowance +
                               NetplayProtocol.InputHistoryLength + 1;

        readonly int _localSeat;
        readonly PlayerInput[] _local = new PlayerInput[StoreTicks];
        readonly PlayerInput[] _remote = new PlayerInput[StoreTicks];
        readonly PlayerInput[] _usedRemote = new PlayerInput[StoreTicks];
        readonly int[] _localTick = new int[StoreTicks];
        readonly int[] _remoteTick = new int[StoreTicks];
        readonly int[] _usedRemoteTick = new int[StoreTicks];
        readonly int[] _afterTick = new int[SnapshotTicks];
        readonly MatchSimState[] _after = new MatchSimState[SnapshotTicks];
        readonly int[] _checksums = new int[SnapshotTicks];
        readonly SimEvents[] _events = new SimEvents[SnapshotTicks];
        readonly int[] _remoteChecksumTick = new int[StoreTicks];
        readonly int[] _remoteChecksums = new int[StoreTicks];
        readonly MatchSimState _initial;
        int _newestRemoteTick = -1;
        int _contiguousRemoteTick = DelayTicks - 1;
        int _confirmedStateTick = -1;

        public MatchSimState State { get; }
        public int LocalSeat => _localSeat;
        public int ConfirmedTick => _contiguousRemoteTick;
        public int ConfirmedStateTick => _confirmedStateTick;
        public int NewestRemoteTick => _newestRemoteTick;
        public RollbackStatus Status { get; private set; }
        public int LastRollbackTick { get; private set; } = -1;
        public int ValidatedChecksumCount { get; private set; }
        public int DesyncTick { get; private set; } = -1;
        public int DesyncLocalChecksum { get; private set; }
        public int DesyncRemoteChecksum { get; private set; }

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
                _remoteChecksumTick[i] = -1;
            }

            for (int i = 0; i < SnapshotTicks; i++)
            {
                _afterTick[i] = -1;
            }
        }

        public void QueueLocal(int tick, PlayerInput input)
        {
            // 本地输入也按 Tick 存储，而不是在重演时重新读键盘；历史重演必须使用原始输入。
            if (!HasStore(_localTick, tick)) Set(_local, _localTick, tick, input);
        }

        public void ReceiveRemote(int tick, PlayerInput input)
        {
            TryReceiveRemote(tick, input);
        }

        public bool TryReceiveRemote(int tick, PlayerInput input)
        {
            int oldest = Mathf.Max(DelayTicks, State.Tick - BufferTicks);
            int newest = State.Tick + NetplayProtocol.FutureTickAllowance;
            if (tick < oldest || tick > newest) return false;

            int index = StoreIndex(tick);
            if (_remoteTick[index] == tick)
                return _remote[index].EqualsInput(input);

            Set(_remote, _remoteTick, tick, input);
            if (tick > _newestRemoteTick) _newestRemoteTick = tick;
            while (HasStore(_remoteTick, _contiguousRemoteTick + 1))
                _contiguousRemoteTick++;
            return true;
        }

        public int ReceiveRemoteHistory(int newestTick, byte[] newestToOldest, int count)
        {
            if (newestToOldest == null || count < 1 || count > NetplayProtocol.InputHistoryLength ||
                newestToOldest.Length < count)
                return 0;

            int accepted = 0;
            // 先老后新写入，连续确认可一次跨过刚补齐的缺口。
            for (int i = count - 1; i >= 0; i--)
            {
                int tick = newestTick - i;
                if (TryReceiveRemote(tick, PlayerInput.Unpack(newestToOldest[i])))
                    accepted++;
            }

            return accepted;
        }

        public int CopyLocalHistory(int newestTick, byte[] newestToOldest)
        {
            if (newestToOldest == null) return 0;
            int limit = Mathf.Min(newestToOldest.Length, NetplayProtocol.InputHistoryLength);
            int count = 0;
            for (int i = 0; i < limit; i++)
            {
                int tick = newestTick - i;
                if (!HasStore(_localTick, tick)) break;
                newestToOldest[count++] = _local[StoreIndex(tick)].Pack();
            }

            return count;
        }

        public PlayerInput PredictedRemoteFor(int tick)
        {
            return RemoteFor(tick);
        }

        public bool QueueRemoteChecksum(int tick, int checksum)
        {
            int oldest = Mathf.Max(0, State.Tick - BufferTicks);
            int newest = State.Tick + NetplayProtocol.FutureTickAllowance;
            if (tick < oldest || tick > newest) return false;
            int index = StoreIndex(tick);
            if (_remoteChecksumTick[index] == tick)
                return _remoteChecksums[index] == checksum;
            _remoteChecksumTick[index] = tick;
            _remoteChecksums[index] = checksum;
            return true;
        }

        public bool TryGetConfirmedChecksum(int preferredTick, out int tick, out int checksum)
        {
            int oldest = Mathf.Max(0, State.Tick - BufferTicks);
            // 输入刚收到时 ConfirmedTick 已前进，但历史尚未重演；只能发送已修正的状态。
            for (int candidate = Mathf.Min(preferredTick, _confirmedStateTick); candidate >= oldest; candidate--)
            {
                if (!HasBuf(_afterTick, candidate)) continue;
                tick = candidate;
                checksum = _checksums[BufIndex(candidate)];
                return true;
            }

            tick = -1;
            checksum = 0;
            return false;
        }

        public SimEvents Advance(PlayerInput sampledNow, int? remoteChecksumTick = null, int remoteChecksum = 0)
        {
            if (remoteChecksumTick.HasValue)
                QueueRemoteChecksum(remoteChecksumTick.Value, remoteChecksum);
            Reconcile();
            if (Status != RollbackStatus.Ok || State.Phase == MatchState.MatchOver || State.Phase == MatchState.Pregame)
                return default;
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

            SimEvents events = StepAndRecord(applyTick);
            _confirmedStateTick = Mathf.Min(ConfirmedTick, State.Tick - 1);
            ValidatePendingRemoteChecksums();
            return events;
        }

        // Receiving input and settling a finish must not sample or execute another tick.
        public void Reconcile()
        {
            LastRollbackTick = -1;
            if (Status != RollbackStatus.Ok) return;
            int applyTick = State.Tick;

            int rollbackFrom = -1;
            int minCheck = Mathf.Max(0, applyTick - BufferTicks);
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
                for (int i = 0; i < SnapshotTicks; i++)
                    if (_afterTick[i] >= rollbackFrom) _afterTick[i] = -1;
                for (int i = 0; i < StoreTicks; i++)
                    if (_usedRemoteTick[i] >= rollbackFrom) _usedRemoteTick[i] = -1;
                for (int t = rollbackFrom; t < applyTick && State.Phase != MatchState.MatchOver; t++)
                    StepAndRecord(t);
            }

            _confirmedStateTick = Mathf.Min(ConfirmedTick, State.Tick - 1);
            // 先校验历史，再复用快照槽位，避免窗口边缘的证据被本帧覆盖。
            ValidatePendingRemoteChecksums();
        }

        public bool TryGetConfirmedEvents(int tick, out SimEvents events)
        {
            events = default;
            if (tick > _confirmedStateTick || !HasBuf(_afterTick, tick)) return false;
            events = _events[BufIndex(tick)];
            return true;
        }

        public bool TryGetConfirmedState(int tick, out MatchSimState state)
        {
            state = null;
            if (tick > _confirmedStateTick || !HasBuf(_afterTick, tick)) return false;
            state = _after[BufIndex(tick)].Clone();
            return true;
        }

        public int ChecksumAt(int tick)
        {
            if (!HasBuf(_afterTick, tick)) return 0;
            return _checksums[BufIndex(tick)];
        }

        SimEvents StepAndRecord(int tick)
        {
            SimEvents events = StepStored(tick);
            int index = BufIndex(tick);
            _after[index] = State.Clone();
            _afterTick[index] = tick;
            _checksums[index] = State.Checksum();
            _events[index] = events;
            return events;
        }

        void ValidatePendingRemoteChecksums()
        {
            for (int i = 0; i < StoreTicks; i++)
            {
                int tick = _remoteChecksumTick[i];
                if (tick >= 0 && tick < State.Tick - SnapshotTicks)
                {
                    _remoteChecksumTick[i] = -1;
                    continue;
                }
                // 只有真实远端输入连续覆盖到该 Tick 后，本地状态才是可比较的最终历史。
                if (tick < 0 || tick > _confirmedStateTick || !HasBuf(_afterTick, tick)) continue;
                int remote = _remoteChecksums[i];
                _remoteChecksumTick[i] = -1;
                ValidatedChecksumCount++;
                if (_checksums[BufIndex(tick)] == remote) continue;
                DesyncTick = tick;
                DesyncLocalChecksum = _checksums[BufIndex(tick)];
                DesyncRemoteChecksum = remote;
                Status = RollbackStatus.Desync;
                return;
            }
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
                    StepAndRecord(t);
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

            // 只沿用该 Tick 之前最新的 held 状态；ShootPressed 是边沿，预测时必须清除。
            int oldest = Mathf.Max(DelayTicks, tick - StoreTicks + 1);
            for (int prior = tick - 1; prior >= oldest; prior--)
            {
                if (!HasStore(_remoteTick, prior)) continue;
                PlayerInput predicted = _remote[StoreIndex(prior)];
                predicted.ShootPressed = false;
                return predicted;
            }

            return PlayerInput.Neutral;
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
            int i = tick % SnapshotTicks;
            return i < 0 ? i + SnapshotTicks : i;
        }
    }
}
