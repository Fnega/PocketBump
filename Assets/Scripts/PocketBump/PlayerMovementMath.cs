using UnityEngine;

namespace PocketBump
{
    public enum BoostPhase
    {
        Normal,
        Windup,
        Boosting,
        Recovery
    }

    public static class PlayerMovementMath
    {
        public static void AdvanceBoost(
            ref BoostPhase phase,
            ref float phaseTime,
            bool boostHeld,
            float deltaTime)
        {
            deltaTime = Mathf.Max(0f, deltaTime);

            switch (phase)
            {
                case BoostPhase.Normal:
                    if (boostHeld)
                    {
                        phase = BoostPhase.Windup;
                        phaseTime = 0f;
                    }
                    break;

                case BoostPhase.Windup:
                    if (!boostHeld)
                    {
                        phase = BoostPhase.Recovery;
                        phaseTime = 0f;
                        break;
                    }

                    phaseTime += deltaTime;
                    if (phaseTime >= ArenaLayout.BoostWindupSeconds)
                    {
                        phase = BoostPhase.Boosting;
                        phaseTime = 0f;
                    }
                    break;

                case BoostPhase.Boosting:
                    if (!boostHeld)
                    {
                        phase = BoostPhase.Recovery;
                        phaseTime = 0f;
                    }
                    break;

                case BoostPhase.Recovery:
                    phaseTime += deltaTime;
                    if (phaseTime >= ArenaLayout.BoostRecoverySeconds)
                    {
                        phase = boostHeld ? BoostPhase.Windup : BoostPhase.Normal;
                        phaseTime = 0f;
                    }
                    break;
            }
        }

        public static Vector2 StepVelocity(
            Vector2 velocity,
            Vector2 input,
            bool boosting,
            float deltaTime)
        {
            deltaTime = Mathf.Max(0f, deltaTime);
            if (input.sqrMagnitude < 0.0001f)
            {
                return Vector2.MoveTowards(
                    velocity,
                    Vector2.zero,
                    ArenaLayout.BrakeAcceleration * deltaTime);
            }

            Vector2 direction = input.normalized;
            float maxSpeed = boosting
                ? ArenaLayout.BoostMaxSpeed
                : ArenaLayout.NormalMaxSpeed;
            float speed = velocity.magnitude;
            float alignment = speed > 0.01f
                ? Vector2.Dot(velocity / speed, direction)
                : 1f;

            float acceleration;
            if (alignment >= 0.75f)
            {
                acceleration = boosting
                    ? ArenaLayout.BoostAcceleration
                    : ArenaLayout.NormalAcceleration;
            }
            else
            {
                acceleration = ArenaLayout.TurnAcceleration;
                if (boosting)
                    acceleration *= ArenaLayout.BoostSteeringMultiplier;
            }

            Vector2 desiredVelocity = direction * maxSpeed;
            Vector2 next = Vector2.MoveTowards(
                velocity,
                desiredVelocity,
                acceleration * deltaTime);
            return Vector2.ClampMagnitude(next, maxSpeed);
        }
    }
}
