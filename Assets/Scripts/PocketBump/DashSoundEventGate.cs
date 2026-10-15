using System;
using System.Collections.Generic;

namespace PocketBump
{
    // A second presentation-only guard after the authoritative confirmed-event ledger.
    // Confirmed batches are tick-ordered. Keep identities for just the latest tick;
    // older ticks can never replay, so memory does not grow during a long match.
    public sealed class DashSoundEventGate
    {
        readonly HashSet<DashArenaEventIdentity> _seen = new HashSet<DashArenaEventIdentity>();
        int _epoch = -1, _latestTick = -1, _lastImpactTick = -3;

        public void ResetForEpoch(int epoch)
        {
            if (epoch < 0) throw new ArgumentOutOfRangeException(nameof(epoch));
            _epoch = epoch; _latestTick = -1; _lastImpactTick = -3; _seen.Clear();
        }

        public bool TryAccept(DashArenaEvent item, out DashSoundCue cue)
        {
            cue = default;
            if (item.Epoch != _epoch || item.Tick < 0 || item.Tick < _latestTick) return false;
            if (item.Tick > _latestTick) { _latestTick = item.Tick; _seen.Clear(); }
            if (!_seen.Add(item.Identity)) return false;
            switch (item.Kind)
            {
                case DashArenaEventKind.DashStarted: cue = DashSoundCue.Dash; return true;
                case DashArenaEventKind.Impact:
                case DashArenaEventKind.WallHit:
                case DashArenaEventKind.PillarHit:
                    // Dense contact sets can otherwise produce many simultaneous clicks.
                    if ((long)item.Tick - _lastImpactTick < 3) return false;
                    _lastImpactTick = item.Tick; cue = DashSoundCue.Impact; return true;
                case DashArenaEventKind.WallBroken: cue = DashSoundCue.WallBreak; return true;
                case DashArenaEventKind.Goal: cue = DashSoundCue.Goal; return true;
                case DashArenaEventKind.Respawned: cue = DashSoundCue.Respawn; return true;
                case DashArenaEventKind.Pickup:
                    switch (item.PickupKind)
                    {
                        case DashPickupKind.Green: cue = DashSoundCue.GreenPickup; return true;
                        case DashPickupKind.Blue: cue = DashSoundCue.BluePickup; return true;
                        case DashPickupKind.Purple: cue = DashSoundCue.PurplePickup; return true;
                        default: return false;
                    }
                default: return false;
            }
        }
    }
}
