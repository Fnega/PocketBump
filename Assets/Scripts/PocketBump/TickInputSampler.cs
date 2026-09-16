using UnityEngine;

// 将渲染帧中的键盘状态转换为固定 Tick 可消费的 PlayerInput。
// Shoot 使用锁存，避免短按落在两个 FixedUpdate 之间而丢失，并保证只消费一次。
namespace PocketBump
{
    public sealed class TickInputSampler
    {
        readonly bool[] _shootLatched = new bool[2];

        public void LatchUpdate(bool netplayLocalSeatOnly, int localSeat)
        {
            // GetKeyDown 只在渲染帧成立，先 OR 到锁存位；即使一帧内出现多个 FixedUpdate 也只消费一次。
            if (netplayLocalSeatOnly)
            {
                if (Input.GetKeyDown(ControlBindings.Shoot(0)))
                    _shootLatched[localSeat] = true;
                return;
            }

            for (int i = 0; i < 2; i++)
            {
                if (Input.GetKeyDown(ControlBindings.Shoot(i)))
                    _shootLatched[i] = true;
            }
        }

        public PlayerInput SampleLocal(int seat)
        {
            // 同机模式的两套键位分别映射到两个模拟座位。
            return SampleSeat(seat, seat);
        }

        public PlayerInput SampleNetplaySeat(int assignedSeat)
        {
            // 联机两台机器都使用本机 P1(WASD) 键位，但写入握手分配的逻辑座位。
            return SampleSeat(0, assignedSeat);
        }

        PlayerInput SampleSeat(int bindingIndex, int latchIndex)
        {
            Vector2 move = ReadMove(bindingIndex);
            bool boost = Input.GetKey(ControlBindings.Boost(bindingIndex));
            bool shoot = _shootLatched[latchIndex];
            _shootLatched[latchIndex] = false;
            return new PlayerInput
            {
                Move = move,
                BoostHeld = boost,
                ShootPressed = shoot
            };
        }

        public static Vector2 ReadMove(int bindingIndex)
        {
            if (bindingIndex == 0)
            {
                return Normalize(
                    (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
                    (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));
            }

            return Normalize(
                (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f),
                (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f));
        }

        static Vector2 Normalize(float x, float y)
        {
            // 对角线输入归一化，避免同时按两个方向获得 sqrt(2) 倍移动强度。
            var v = new Vector2(x, y);
            if (v.sqrMagnitude < 0.0001f) return Vector2.zero;
            return v.normalized;
        }
    }
}
