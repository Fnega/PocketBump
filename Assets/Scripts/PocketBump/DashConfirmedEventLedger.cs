using System;
using System.Collections.Generic;

namespace PocketBump
{
    // A cursor over corrected, confirmed ticks. No speculative pickup or sound can enter
    // this ledger. Call at every simulation/receive pump; a missed history window is explicit.
    public sealed class DashConfirmedEventLedger
    {
        static readonly IReadOnlyList<DashArenaEvent> Empty = Array.AsReadOnly(new DashArenaEvent[0]);
        public int Epoch { get; private set; } = -1;
        public int NextTick { get; private set; }
        public int MissingTick { get; private set; } = -1;
        public long CommittedEventCount { get; private set; }

        public void ResetForEpoch(int epoch)
        {
            if (epoch < 0) throw new ArgumentOutOfRangeException(nameof(epoch));
            Epoch = epoch; NextTick = 0; MissingTick = -1; CommittedEventCount = 0;
        }

        public bool TryCommit(DashRollbackSession session, out IReadOnlyList<DashArenaEvent> events)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            events = Empty;
            if (Epoch < 0) ResetForEpoch(session.State.Epoch);
            if (session.State.Epoch != Epoch)
                throw new InvalidOperationException("Reset presentation explicitly before consuming a new epoch.");
            if (session.Status != RollbackStatus.Ok) return false;
            int confirmed = session.ConfirmedStateTick;
            if (NextTick > confirmed) return true;
            var pending = new List<DashArenaEvent>();
            for (int tick = NextTick; tick <= confirmed; tick++)
            {
                if (!session.TryGetConfirmedEvents(tick, out var tickEvents))
                {
                    MissingTick = tick; return false; // No partial commit on a history gap.
                }
                foreach (DashArenaEvent item in tickEvents)
                {
                    if (item.Epoch != Epoch || item.Tick != tick)
                        throw new InvalidOperationException("Event identity does not match its confirmed history slot.");
                    pending.Add(item);
                }
            }
            NextTick = confirmed + 1; MissingTick = -1;
            CommittedEventCount += pending.Count;
            if (pending.Count != 0) events = pending.AsReadOnly();
            return true;
        }
    }
}
