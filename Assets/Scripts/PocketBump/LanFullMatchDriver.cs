#if DEVELOPMENT_BUILD || UNITY_EDITOR
using UnityEngine;

namespace PocketBump
{
    // A cooperative input script, not a simulation fixture. All movement, collection,
    // charging, shots and scoring are produced by the same packed inputs as a keyboard.
    public sealed class LanFullMatchDriver
    {
        int _lastShotTick = -100;
        int _faceRightUntil = -1;
        bool _receiverReady;
        bool _fired;
        bool _collision;

        public void ObserveConfirmed(SimEvents events)
        {
            _fired |= events.Fired0 || events.Fired1;
            _collision |= events.DangerousImpact;
        }

        public PlayerInput Sample(int seat, int sampleTick, MatchSimState state)
        {
            if (state == null || state.Phase != MatchState.Live) return PlayerInput.Neutral;
            PlayerSim player = state.Players[seat];
            PlayerInput input = PlayerInput.Neutral;

            // After the actual shot/impact, P2 drives into the side pocket. This is a
            // normal legal goal under the existing opponent-awarded dunk rule.
            if (_fired && _collision || state.ScoreP1 + state.ScoreP2 > 0)
            {
                if (seat == 1)
                {
                    Vector2 target = Mathf.Abs(player.Position.y) > 1f
                        ? new Vector2(0f, 0f) : new Vector2(ArenaLayout.HalfWidth + 2f, 0f);
                    input.Move = Steer(player, target);
                }
                return Canonical(input);
            }

            if (seat == 1)
            {
                if (!_receiverReady)
                {
                    input.Move = Steer(player, new Vector2(0f, -4f));
                    if (Vector2.Distance(player.Position, new Vector2(0f, -4f)) < 0.22f && player.Velocity.magnitude < 0.8f)
                    {
                        _receiverReady = true;
                        _faceRightUntil = sampleTick + 8;
                    }
                }
                if (_receiverReady && sampleTick < _faceRightUntil) input.Move = Vector2.right;
                return Canonical(input);
            }

            if (player.Carried < 0)
            {
                if (_fired) return input;
                for (int i = 0; i < state.Marbles.Length; i++)
                {
                    MarbleSim marble = state.Marbles[i];
                    if (!marble.Active || marble.Carrier >= 0) continue;
                    Vector2 delta = marble.Position - player.Position;
                    input.Move = new Vector2(Mathf.Abs(delta.x) > 0.3f ? Mathf.Sign(delta.x) : 0f,
                        Mathf.Abs(delta.y) > 0.3f ? Mathf.Sign(delta.y) : 0f);
                    break;
                }
            }
            else if (sampleTick - _lastShotTick >= 4)
            {
                bool press = player.Charge != ChargePhase.Power || player.ChargeTime >= 0.20f;
                if (press)
                {
                    input.ShootPressed = true;
                    _lastShotTick = sampleTick;
                }
            }
            return Canonical(input);
        }

        static Vector2 Steer(PlayerSim player, Vector2 target)
        {
            Vector2 delta = target - player.Position;
            // Release early enough for the production braking acceleration to stop
            // the body; only the eight keyboard directions cross the wire.
            float stopX = Mathf.Sign(player.Velocity.x) * player.Velocity.x * player.Velocity.x /
                          (2f * ArenaLayout.BrakeAcceleration);
            float stopY = Mathf.Sign(player.Velocity.y) * player.Velocity.y * player.Velocity.y /
                          (2f * ArenaLayout.BrakeAcceleration);
            float x = Mathf.Abs(delta.x - stopX) > 0.12f ? Mathf.Sign(delta.x - stopX) : 0f;
            float y = Mathf.Abs(delta.y - stopY) > 0.12f ? Mathf.Sign(delta.y - stopY) : 0f;
            return new Vector2(x, y);
        }

        static PlayerInput Canonical(PlayerInput input) => PlayerInput.Unpack(input.Pack());
    }
}
#endif
