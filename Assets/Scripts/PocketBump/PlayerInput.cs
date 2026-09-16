using UnityEngine;

// 一个模拟 Tick 所需的完整玩家意图。
// MatchSim 不直接读取 Unity Input，因此同一输入序列能够被测试和回滚重演。
namespace PocketBump
{
    public struct PlayerInput
    {
        public Vector2 Move;
        public bool BoostHeld;
        public bool ShootPressed;

        public static PlayerInput Neutral => default;

        public bool EqualsInput(PlayerInput other)
        {
            return Move == other.Move && BoostHeld == other.BoostHeld && ShootPressed == other.ShootPressed;
        }

        public byte Pack()
        {
            // 键盘方向只有离散组合，6 个 bit 足以表示方向、Boost 与 Shoot，适合逐 Tick UDP 包。
            byte bits = 0;
            if (Move.x > 0.5f) bits |= 1;
            else if (Move.x < -0.5f) bits |= 2;
            if (Move.y > 0.5f) bits |= 4;
            else if (Move.y < -0.5f) bits |= 8;
            if (BoostHeld) bits |= 16;
            if (ShootPressed) bits |= 32;
            return bits;
        }

        public static PlayerInput Unpack(byte bits)
        {
            // 对角方向重新归一化，保证网络解包和本地采样得到相同移动向量。
            float x = ((bits & 1) != 0 ? 1f : 0f) - ((bits & 2) != 0 ? 1f : 0f);
            float y = ((bits & 4) != 0 ? 1f : 0f) - ((bits & 8) != 0 ? 1f : 0f);
            var move = new Vector2(x, y);
            if (move.sqrMagnitude > 0.0001f) move.Normalize();
            return new PlayerInput
            {
                Move = move,
                BoostHeld = (bits & 16) != 0,
                ShootPressed = (bits & 32) != 0
            };
        }
    }
}
