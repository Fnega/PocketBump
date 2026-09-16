using UnityEngine;

// 封装玩家惯性移动与 Boost 状态机的无场景计算。
// 固定输入和 deltaTime 会产生可重复结果，是本地与回滚联机共享的运动内核。
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
            // 显式状态机让蓄力、释放和恢复都由 Tick 决定，回滚时只恢复 phase/time 即可继续。
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
                // 无输入时按固定制动加速度靠近零，而非瞬间停下，以保留碰撞游戏需要的惯性。
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

            // 同向输入使用前进加速度，转向/反向使用独立参数；这样可分别调节提速和操控手感。
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
