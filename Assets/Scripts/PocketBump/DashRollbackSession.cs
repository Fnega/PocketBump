using System;
using System.Collections.Generic;
using UnityEngine;

// 管理双人输入延迟、远端输入预测、逐 Tick 快照和迟到输入回滚。
// 会话只重演 DashArenaSimulation，不触碰场景对象，因此修正历史的成本可控且结果可校验。
namespace PocketBump
{
    public sealed class DashRollbackSession
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
        readonly DashArenaInput[] _local = new DashArenaInput[StoreTicks];
        readonly DashArenaInput[] _remote = new DashArenaInput[StoreTicks];
        readonly DashArenaInput[] _usedRemote = new DashArenaInput[StoreTicks];
        readonly int[] _localTick = new int[StoreTicks];
        readonly int[] _remoteTick = new int[StoreTicks];
        readonly int[] _usedRemoteTick = new int[StoreTicks];
        readonly int[] _afterTick = new int[SnapshotTicks];
        readonly DashArenaState[] _after = new DashArenaState[SnapshotTicks];
        readonly int[] _checksums = new int[SnapshotTicks];
        readonly IReadOnlyList<DashArenaEvent>[] _events = new IReadOnlyList<DashArenaEvent>[SnapshotTicks];
        readonly int[] _remoteChecksumTick = new int[StoreTicks];
        readonly int[] _remoteChecksums = new int[StoreTicks];
        readonly DashArenaState _initial;
        int _newestRemoteTick = -1;
        int _contiguousRemoteTick = DelayTicks - 1;
        int _confirmedStateTick = -1;

        public DashArenaState State { get; }
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

        public DashRollbackSession(DashArenaState initial, int localSeat)
        {
            if (initial == null) throw new ArgumentNullException(nameof(initial));
            if (localSeat < 0 || localSeat > 1) throw new ArgumentOutOfRangeException(nameof(localSeat));
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

        public void QueueLocal(int tick, DashArenaInput input)
        {
            // 本地输入也按 Tick 存储，而不是在重演时重新读键盘；历史重演必须使用原始输入。
            if (!HasStore(_localTick, tick)) Set(_local, _localTick, tick, input);
        }

        public void ReceiveRemote(int tick, DashArenaInput input)
        {
            TryReceiveRemote(tick, input);
        }

        public bool TryReceiveRemote(int tick, DashArenaInput input)
        {
            int oldest = Mathf.Max(DelayTicks, State.Tick - BufferTicks);
            long newest = (long)State.Tick + NetplayProtocol.FutureTickAllowance;
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

        // Validate the complete byte vocabulary and conflicting retransmissions before any
        // insertion. An expired oldest suffix is harmless; an invalid packet is not partial input.
        public int ReceiveRemoteHistory(int newestTick, byte[] newestToOldest, int count)
        {
            if (newestToOldest == null || count < 1 || count > NetplayProtocol.InputHistoryLength ||
                newestToOldest.Length < count || newestTick < count - 1)
                return 0;
            int oldest = Mathf.Max(DelayTicks, State.Tick - BufferTicks);
            long newest = (long)State.Tick + NetplayProtocol.FutureTickAllowance;
            if (newestTick < oldest || newestTick > newest) return 0;
            for (int i = 0; i < count; i++)
            {
                if (!DashArenaInput.TryUnpack(newestToOldest[i], out DashArenaInput input)) return 0;
                int tick = newestTick - i;
                if (tick < oldest) continue;
                int index = StoreIndex(tick);
                if (_remoteTick[index] == tick && !_remote[index].EqualsInput(input)) return 0;
            }

            int accepted = 0;
            // Oldest first lets the contiguous acknowledgement cross a newly repaired hole.
            for (int i = count - 1; i >= 0; i--)
                if (TryReceiveRemote(newestTick - i, DashArenaInput.Unpack(newestToOldest[i])))
                    accepted++;
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

        public DashArenaInput PredictedRemoteFor(int tick)
        {
            return RemoteFor(tick);
        }

        public bool QueueRemoteChecksum(int tick, int checksum)
        {
            int oldest = Mathf.Max(0, State.Tick - BufferTicks);
            long newest = (long)State.Tick + NetplayProtocol.FutureTickAllowance;
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

        public IReadOnlyList<DashArenaEvent> Advance(DashArenaInput sampledNow, int? remoteChecksumTick = null, int remoteChecksum = 0)
        {
            if (remoteChecksumTick.HasValue)
                QueueRemoteChecksum(remoteChecksumTick.Value, remoteChecksum);
            Reconcile();
            if (Status != RollbackStatus.Ok || State.Phase == MatchState.MatchOver || State.Phase == MatchState.Pregame)
                return Array.Empty<DashArenaEvent>();
            int applyTick = State.Tick;
            // 双方统一延迟 2 Tick：用少量固定操作延迟换取更多报文按时到达，减少回滚频率。
            QueueLocal(applyTick + DelayTicks, sampledNow);

            int fallingOut = applyTick - BufferTicks;
            if (fallingOut >= DelayTicks && !HasStore(_remoteTick, fallingOut))
            {
                // 必需输入一旦老到将被环形缓冲覆盖，就无法再安全修正，只能报告连接缺口。
                Status = RollbackStatus.InputHole;
                return Array.Empty<DashArenaEvent>();
            }

            IReadOnlyList<DashArenaEvent> events = StepAndRecord(applyTick);
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

        public bool TryGetConfirmedEvents(int tick, out IReadOnlyList<DashArenaEvent> events)
        {
            events = Array.Empty<DashArenaEvent>();
            if (tick > _confirmedStateTick || !HasBuf(_afterTick, tick)) return false;
            events = _events[BufIndex(tick)];
            return true;
        }

        public bool TryGetConfirmedState(int tick, out DashArenaState state)
        {
            state = null;
            if (tick > _confirmedStateTick || !HasBuf(_afterTick, tick)) return false;
            state = _after[BufIndex(tick)].Clone();
            return true;
        }

        public bool TryGetConfirmedInputs(int tick, out DashArenaInput seat0, out DashArenaInput seat1)
        {
            seat0 = seat1 = DashArenaInput.Neutral;
            if (tick < 0 || tick > _confirmedStateTick || !HasBuf(_afterTick, tick)) return false;
            if (tick < DelayTicks) return true; // The agreed two-tick neutral startup prefix.
            if (!HasStore(_localTick, tick) || !HasStore(_remoteTick, tick)) return false;
            DashArenaInput local = _local[StoreIndex(tick)], remote = _remote[StoreIndex(tick)];
            seat0 = _localSeat == 0 ? local : remote;
            seat1 = _localSeat == 0 ? remote : local;
            return true;
        }

        public int ChecksumAt(int tick)
        {
            if (!HasBuf(_afterTick, tick)) return 0;
            return _checksums[BufIndex(tick)];
        }

        IReadOnlyList<DashArenaEvent> StepAndRecord(int tick)
        {
            IReadOnlyList<DashArenaEvent> events = StepStored(tick);
            int index = BufIndex(tick);
            _after[index] = State.Clone();
            _afterTick[index] = tick;
            _checksums[index] = State.Checksum();
            // Publish immutable event evidence; views cannot mutate the retained history.
            var copy = new DashArenaEvent[events.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = events[i];
            _events[index] = Array.AsReadOnly(copy);
            return _events[index];
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

        IReadOnlyList<DashArenaEvent> StepStored(int tick)
        {
            DashArenaInput local = LocalFor(tick);
            DashArenaInput remote = RemoteFor(tick);
            _usedRemote[StoreIndex(tick)] = remote;
            // 记录“本次实际用了什么远端输入”，未来真实输入到达后才能知道预测是否错误。
            _usedRemoteTick[StoreIndex(tick)] = tick;
            DashArenaInput i0 = _localSeat == 0 ? local : remote;
            DashArenaInput i1 = _localSeat == 0 ? remote : local;
            return DashArenaSimulation.Step(State, i0, i1);
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

        DashArenaInput LocalFor(int tick)
        {
            return HasStore(_localTick, tick) ? _local[StoreIndex(tick)] : DashArenaInput.Neutral;
        }

        DashArenaInput RemoteFor(int tick)
        {
            if (HasStore(_remoteTick, tick))
                return _remote[StoreIndex(tick)];

            // Only prior accepted movement is predicted; Dash is always an unpredicted edge.
            int oldest = Mathf.Max(DelayTicks, tick - StoreTicks + 1);
            for (int prior = tick - 1; prior >= oldest; prior--)
            {
                if (!HasStore(_remoteTick, prior)) continue;
                return _remote[StoreIndex(prior)].Predict();
            }

            return DashArenaInput.Neutral;
        }

        static void Set(DashArenaInput[] arr, int[] stamps, int tick, DashArenaInput input)
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
