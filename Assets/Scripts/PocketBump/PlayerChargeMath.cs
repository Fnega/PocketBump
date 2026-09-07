using UnityEngine;

namespace PocketBump
{
    public enum ChargePhase
    {
        None,
        Direction,
        Power
    }

    public static class PlayerChargeMath
    {
        public static bool ShouldCancelPower(ChargePhase phase, Vector2 input)
        {
            return phase == ChargePhase.Power && input.sqrMagnitude > 0.0001f;
        }

        public static Vector2 SpawnFacing(int playerIndex)
        {
            return playerIndex == 0 ? Vector2.down : Vector2.up;
        }

        public static Vector2 StepFacing(Vector2 current, Vector2 input, float deltaTime)
        {
            if (current.sqrMagnitude < 0.0001f) current = Vector2.down;
            else current.Normalize();

            if (input.sqrMagnitude < 0.0001f)
                return current;

            float maxRadians = ArenaLayout.FacingTurnDegreesPerSecond * Mathf.Deg2Rad * Mathf.Max(0f, deltaTime);
            return RotateTowards(current, input.normalized, maxRadians);
        }

        static Vector2 RotateTowards(Vector2 current, Vector2 target, float maxRadians)
        {
            float from = Mathf.Atan2(current.y, current.x);
            float to = Mathf.Atan2(target.y, target.x);
            float delta = Mathf.DeltaAngle(from * Mathf.Rad2Deg, to * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            if (Mathf.Abs(delta) <= maxRadians)
                return target;
            float next = from + Mathf.Sign(delta) * maxRadians;
            return new Vector2(Mathf.Cos(next), Mathf.Sin(next));
        }

        public static bool CanStartCharge(bool carrying, ChargePhase phase)
        {
            return carrying && phase == ChargePhase.None;
        }

        public static float DirectionOffsetDegrees(float time)
        {
            float period = Mathf.Max(0.01f, ArenaLayout.ChargeNeedlePeriod);
            return Mathf.Sin(time * Mathf.PI * 2f / period) * ArenaLayout.ChargeDirectionMaxDegrees;
        }

        public static float Power01(float time)
        {
            float period = Mathf.Max(0.01f, ArenaLayout.ChargeNeedlePeriod);
            return (Mathf.Sin(time * Mathf.PI * 2f / period) + 1f) * 0.5f;
        }

        public static Vector2 LockedShotDirection(Vector2 facing, float offsetDegrees)
        {
            Vector2 dir = facing.sqrMagnitude < 0.0001f ? Vector2.up : facing.normalized;
            float rad = offsetDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            return new Vector2(dir.x * cos - dir.y * sin, dir.x * sin + dir.y * cos);
        }

        public static float ShotSpeed(float power01)
        {
            return Mathf.Lerp(ArenaLayout.ShotMinSpeed, ArenaLayout.ShotMaxSpeed, Mathf.Clamp01(power01));
        }

        public static Vector2 RecoilImpulse(Vector2 shotDirection, float power01)
        {
            Vector2 dir = shotDirection.sqrMagnitude < 0.0001f ? Vector2.up : shotDirection.normalized;
            float magnitude = Mathf.Lerp(
                ArenaLayout.RecoilImpulseMin,
                ArenaLayout.RecoilImpulseMax,
                Mathf.Clamp01(power01));
            return -dir * magnitude;
        }
    }
}
