#if DEVELOPMENT_BUILD || UNITY_EDITOR
using UnityEngine;

namespace PocketBump
{
    // Cooperative ordinary play, not a fixture. No authoritative state is ever written.
    // Both seats score once, then seat zero finishes the normal win-by-two match.
    public sealed class DashLanFullMatchDriver
    {
        public DashArenaInput Sample(int seat, int sampleTick, DashArenaState state)
        {
            if (state == null || seat < 0 || seat > 1 ||
                state.Phase != MatchState.Kickoff && state.Phase != MatchState.Live)
                return DashArenaInput.Neutral;
            DashPlayerState player = state.Players[seat];
            if (!player.Alive || player.StunTicks > 0) return DashArenaInput.Neutral;
            int shooter = state.Scores[0] == 0 ? 0 : state.Scores[1] == 0 ? 1 : 0;
            float side = seat == 0 ? 1f : -1f;
            Vector2 target = seat == shooter ? new Vector2(0f, side * 3f) : new Vector2(3f, side * 6f);
            var input = new DashArenaInput();
            if (state.Phase == MatchState.Kickoff || seat != shooter)
                input.Move = Steer(player, target, state.Rules.Braking);
            else
            {
                // The production one-ball baseline has a free ball at the center. Aim
                // from the own half through it, then let its actual velocity cross the goal.
                DashBallState ball = state.Balls[0];
                if (ball.Active && Mathf.Abs(ball.Position.y) < 1f && Mathf.Abs(ball.Position.x) < .35f)
                {
                    if (Mathf.Abs(player.Position.x) < .2f && side * player.Position.y > 1.15f &&
                        side * player.Position.y < 5.5f && player.DashCooldownTicks == 0 && player.DashTicks == 0)
                    {
                        input.Move = new Vector2(0f, -side);
                        input.DashPressed = true;
                    }
                    else input.Move = Steer(player, target, state.Rules.Braking);
                }
            }
            return DashArenaInput.Unpack(input.Pack());
        }

        static Vector2 Steer(DashPlayerState player, Vector2 target, float braking)
        {
            Vector2 delta = target - player.Position;
            Vector2 velocity = player.Velocity;
            float stopX = Mathf.Sign(velocity.x) * velocity.x * velocity.x / (2f * braking);
            float stopY = Mathf.Sign(velocity.y) * velocity.y * velocity.y / (2f * braking);
            return new Vector2(Mathf.Abs(delta.x - stopX) > .15f ? Mathf.Sign(delta.x - stopX) : 0f,
                Mathf.Abs(delta.y - stopY) > .15f ? Mathf.Sign(delta.y - stopY) : 0f);
        }
    }
}
#endif
