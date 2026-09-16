using UnityEngine;

// 集中保存竞技场尺寸、运动手感和网络时序参数。
// 模拟与表现共享同一组常量，避免规则在多处硬编码后出现逻辑/画面偏差。
namespace PocketBump
{
    public static class ArenaLayout
    {
        public const float HalfWidth = 16f;
        public const float HalfHeight = 24f;
        public const float Wall = 0.45f;
        public const float MarblePocketWidth = 3.4f;
        public const float DunkPocketHeight = 6.45f;
        public const float PlayerRadius = 0.78f;
        public const float MarbleRadius = 0.3f;
        public const float PlayerMass = 2.8f;
        public const float MarbleMass = 0.38f;
        public const float NormalMaxSpeed = 12f;
        public const float NormalAcceleration = 36f;
        public const float TurnAcceleration = 36f;
        public const float BrakeAcceleration = 21f;
        public const float PlayerLinearDrag = 0.35f;
        public const float BoostWindupSeconds = 0.35f;
        public const float BoostMaxSpeed = 19.5f;
        public const float BoostAcceleration = 57f;
        public const float BoostSteeringMultiplier = 1f;
        public const float BoostRecoverySeconds = 0.4f;
        public const float ImpactCoastSeconds = 0.28f;
        public const float Bounciness = 0.58f;
        public const float PlayerPlayerBounciness = 0.90f;
        public const float MarbleLinearDrag = 2f;
        public const float MarbleMaxSpeed = 16f;
        public const float MarbleMinRunSpeed = 0.35f;
        public const float MarbleMildImpactSpeed = 6f;
        public const float MarbleDangerousSpeed = 10f;
        public const float MarbleMildImpulseScale = 0.45f;
        public const float MarbleDangerousImpulseScale = 1.15f;
        public const float MarbleImpactEnergyRetention = 0.75f;
        public const float MarbleImpactCooldown = 0.15f;
        public const float PlayerTrailSpeed = 0.65f;
        public const float MarbleTrailSpeed = 2.5f;
        public const float FreezeSeconds = 1f;
        public const float KickoffSeconds = 3f;
        public const float BasketOffset = 1.08f;
        public const float FacingTurnDegreesPerSecond = 720f;
        public const float BasketWidth = 1.11f;
        public const float BasketLength = 0.56f;
        public const float BasketRim = 0.08f;
        public const float ShotMinSpeed = 8f;
        public const float ShotMaxSpeed = 22f;
        public const float RecoilImpulseMin = 12f;
        public const float RecoilImpulseMax = 32f;
        public const float ChargeNeedlePeriod = 1f;
        public const float ChargeDirectionMaxDegrees = 18f;
        public const float CameraMargin = 2.4f;
        public const float CameraFov = 50f;
        public const float TableThickness = 0.4f;
        public const float WallHeight = 0.9f;
        public const float NeedleLift = 0.28f;
        public const float NeedlePowerLift = 0.4f;
        public const float NeedleThickness = 0.12f;
        public const float ChargeDirectionLength = 1.8f;
        public const float ChargeDirectionWidth = 0.28f;
        public const float ChargePowerWidth = 0.36f;
        public const float ChargePowerLengthMin = 1f;
        public const float ChargePowerLengthRange = 1.2f;
        public const float BasketThickness = 0.16f;
        public const int SimTicksPerSecond = 60;
        public const float SimTickDt = 1f / 60f;
        public const int InputDelayTicks = 2;
        public const int RollbackBufferTicks = 12;
        public const float CollectLockSeconds = 0.15f;
        public const int LanPort = 7777;
        public const float DisconnectSeconds = 5f;

        public static float CameraHalfExtent => HalfHeight + CameraMargin;

        public static float CameraDistance =>
            CameraHalfExtent / Mathf.Tan(CameraFov * 0.5f * Mathf.Deg2Rad);

        public static Vector2 PlayerOneSpawn => new Vector2(0f, HalfHeight * 0.5f);
        public static Vector2 PlayerTwoSpawn => new Vector2(0f, -HalfHeight * 0.5f);
        public static Vector2 MarbleCenter => Vector2.zero;
    }
}
