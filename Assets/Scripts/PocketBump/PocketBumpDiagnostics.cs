using UnityEngine;

// 编辑器启动时运行轻量级不变量检查，尽早暴露关键参数或规则被改坏的问题。
// 它只提供快速反馈，完整行为仍由 EditMode/PlayMode 测试覆盖。
namespace PocketBump
{
    public static class PocketBumpDiagnostics
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void RunOnStartup()
        {
            if (!RunChecks(out string failure))
                Debug.LogError("Pocket Bump diagnostics failed: " + failure);
        }
#endif

        public static bool RunChecks(out string failure)
        {
            const float dt = 0.02f;
            Vector2 first = PlayerMovementMath.StepVelocity(Vector2.zero, Vector2.up, false, dt);
            Vector2 accelerated = first;
            for (int i = 0; i < 4; i++)
                accelerated = PlayerMovementMath.StepVelocity(accelerated, Vector2.up, false, dt);
            if (accelerated.magnitude <= first.magnitude ||
                accelerated.magnitude >= ArenaLayout.NormalMaxSpeed)
                return Fail("normal movement must accelerate over time", out failure);

            Vector2 reversing = PlayerMovementMath.StepVelocity(
                Vector2.up * ArenaLayout.NormalMaxSpeed,
                Vector2.down,
                false,
                dt);
            if (reversing.y <= 0f)
                return Fail("one physics step must not instantly reverse velocity", out failure);

            if (PlayerMotor.BoostKeyForPlayer(0) == PlayerMotor.BoostKeyForPlayer(1))
                return Fail("players must use independent boost keys", out failure);

            BoostPhase phase = BoostPhase.Normal;
            float phaseTime = 0f;
            for (int i = 0; i < 30; i++)
                PlayerMovementMath.AdvanceBoost(ref phase, ref phaseTime, true, dt);
            if (phase != BoostPhase.Boosting)
                return Fail("held boost must finish wind-up", out failure);
            for (int i = 0; i < 30; i++)
                PlayerMovementMath.AdvanceBoost(ref phase, ref phaseTime, false, dt);
            if (phase != BoostPhase.Normal)
                return Fail("released boost must return to normal after recovery", out failure);

            if (MarbleMotor.CalculateAdditionalImpulse(5f) != 0f ||
                MarbleMotor.CalculateAdditionalImpulse(12f) <=
                MarbleMotor.CalculateAdditionalImpulse(8f))
                return Fail("marble impact bands must preserve slow contact and scale danger", out failure);

            if (MatchDirector.HasWinner(8, 7, 8, out _) ||
                !MatchDirector.HasWinner(9, 7, 8, out int winner) ||
                winner != 0)
                return Fail("win-by-two scoring must remain intact", out failure);

            failure = "";
            return true;
        }

        static bool Fail(string message, out string failure)
        {
            failure = message;
            return false;
        }
    }
}
