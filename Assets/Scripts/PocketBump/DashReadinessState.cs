using UnityEngine;

namespace PocketBump
{
    public enum DashReadinessKind { Hidden, Disabled, Dashing, Cooling, Ready }

    // A view model, never an extra clock. Coarse states deliberately discard both
    // precise progress and the reason for being unavailable before reaching a view.
    public readonly struct DashReadinessState
    {
        public readonly DashReadinessKind Kind;
        public readonly bool Detailed;
        public readonly float Progress;
        public DashReadinessState(DashReadinessKind kind, bool detailed, float progress)
        { Kind = kind; Detailed = detailed; Progress = progress; }

        public static DashReadinessState Evaluate(DashArenaState state, int actor, int viewingSeat)
        {
            bool detailed = actor == viewingSeat;
            if (state == null || actor < 0 || actor >= state.Players.Length || !state.Players[actor].Alive)
                return new DashReadinessState(DashReadinessKind.Hidden, detailed, 0f);
            DashPlayerState player = state.Players[actor];
            DashReadinessKind kind;
            float progress = 0f;
            if (state.Phase != MatchState.Live || player.StunTicks > 0) kind = DashReadinessKind.Disabled;
            else if (player.DashTicks > 0) kind = DashReadinessKind.Dashing;
            else if (player.DashCooldownTicks > 0)
            {
                kind = DashReadinessKind.Cooling;
                if (detailed) progress = Mathf.Clamp01(1f - player.DashCooldownTicks / (float)state.Rules.DashCooldownTicks);
            }
            else { kind = DashReadinessKind.Ready; progress = 1f; }
            if (!detailed && kind != DashReadinessKind.Ready) kind = DashReadinessKind.Disabled;
            return new DashReadinessState(kind, detailed, progress);
        }
    }
}
