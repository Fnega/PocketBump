using System;
using UnityEngine;

// 定义可完整复制的权威模拟状态，以及每 Tick 产生的一次性事件。
// 回滚只保存这些数据；Transform、粒子和 UI 都不属于游戏事实。
namespace PocketBump
{
    public struct PlayerSim
    {
        // 仅保存会改变未来规则结果的数据；颜色、网格和拖尾等表现信息不进入快照。
        public Vector2 Position;
        public Vector2 Velocity;
        public Vector2 Facing;
        public BoostPhase Boost;
        public float BoostTime;
        public float ImpactCoast;
        public ChargePhase Charge;
        public float ChargeTime;
        public Vector2 LockedShotDir;
        public int Carried;
        public float CollectLock;
    }

    public struct MarbleSim
    {
        // Carrier=-1 表示自由弹珠；被持有时位置由篮筐焊接，不参与自由碰撞与口袋得分。
        public bool Active;
        public Vector2 Position;
        public Vector2 Velocity;
        public int Carrier;
        public float ImpactLock0;
        public float ImpactLock1;
        public int DangerousImpactCount;
    }

    public sealed class MatchSimState
    {
        public int Tick;
        public MatchState Phase;
        public int PhaseTicks;
        public bool GateClosed;
        public int ScoreP1;
        public int ScoreP2;
        public int PointsToWin;
        public int MarbleCountSetting;
        public int LastScorer;
        public string LastScoreReason;
        public PlayerSim[] Players = new PlayerSim[2];
        public MarbleSim[] Marbles = new MarbleSim[MatchSettings.MaxMarbles];

        public MatchSimState Clone()
        {
            // 每 Tick 快照必须深复制数组，不能让历史记录与当前状态共享可变元素。
            var copy = new MatchSimState
            {
                Tick = Tick,
                Phase = Phase,
                PhaseTicks = PhaseTicks,
                GateClosed = GateClosed,
                ScoreP1 = ScoreP1,
                ScoreP2 = ScoreP2,
                PointsToWin = PointsToWin,
                MarbleCountSetting = MarbleCountSetting,
                LastScorer = LastScorer,
                LastScoreReason = LastScoreReason
            };
            for (int i = 0; i < 2; i++)
                copy.Players[i] = Players[i];
            for (int i = 0; i < Marbles.Length; i++)
                copy.Marbles[i] = Marbles[i];
            return copy;
        }

        public void CopyFrom(MatchSimState other)
        {
            // 复用目标对象和数组进行恢复，避免回滚时替换外部持有的 State 引用。
            Tick = other.Tick;
            Phase = other.Phase;
            PhaseTicks = other.PhaseTicks;
            GateClosed = other.GateClosed;
            ScoreP1 = other.ScoreP1;
            ScoreP2 = other.ScoreP2;
            PointsToWin = other.PointsToWin;
            MarbleCountSetting = other.MarbleCountSetting;
            LastScorer = other.LastScorer;
            LastScoreReason = other.LastScoreReason;
            for (int i = 0; i < 2; i++)
                Players[i] = other.Players[i];
            for (int i = 0; i < Marbles.Length; i++)
                Marbles[i] = other.Marbles[i];
        }

        public int Checksum()
        {
            // 按固定字段和数组顺序混合原始浮点位；相同 Tick 的哈希不同即可判定两端状态分叉。
            // 这是开发期一致性检测，不是密码学哈希，也不保证跨架构浮点一致。
            unchecked
            {
                int h = Tick;
                h = Mix(h, (int)Phase);
                h = Mix(h, PhaseTicks);
                h = Mix(h, GateClosed ? 1 : 0);
                h = Mix(h, ScoreP1);
                h = Mix(h, ScoreP2);
                h = Mix(h, LastScorer);
                for (int i = 0; i < 2; i++)
                {
                    h = Mix(h, Players[i].Position);
                    h = Mix(h, Players[i].Velocity);
                    h = Mix(h, Players[i].Facing);
                    h = Mix(h, (int)Players[i].Boost);
                    h = Mix(h, Players[i].BoostTime);
                    h = Mix(h, (int)Players[i].Charge);
                    h = Mix(h, Players[i].ChargeTime);
                    h = Mix(h, Players[i].Carried);
                }

                for (int i = 0; i < Marbles.Length; i++)
                {
                    h = Mix(h, Marbles[i].Active ? 1 : 0);
                    h = Mix(h, Marbles[i].Position);
                    h = Mix(h, Marbles[i].Velocity);
                    h = Mix(h, Marbles[i].Carrier);
                    h = Mix(h, Marbles[i].DangerousImpactCount);
                }

                return h;
            }
        }

        static int Mix(int h, int v)
        {
            unchecked
            {
                return (h * 16777619) ^ v;
            }
        }

        static int Mix(int h, Vector2 v)
        {
            return Mix(Mix(h, FloatBits(v.x)), FloatBits(v.y));
        }

        static int Mix(int h, float v)
        {
            return Mix(h, FloatBits(v));
        }

        static int FloatBits(float v)
        {
            // 使用位表示而不是格式化字符串，避免区域设置和小数舍入掩盖状态差异。
            byte[] bytes = BitConverter.GetBytes(v);
            return BitConverter.ToInt32(bytes, 0);
        }
    }

    public struct SimEvents
    {
        // 事件是本 Tick 状态变化的表现提示，不参与下一 Tick 运算，因此不写入快照校验和。
        public bool GateOpened;
        public bool Scored;
        public int Scorer;
        public string ScoreReason;
        public Vector2 ScoreAt;
        public bool MatchOver;
        public int Winner;
        public bool Collected0;
        public bool Collected1;
        public Vector2 CollectAt0;
        public Vector2 CollectAt1;
        public bool Fired0;
        public bool Fired1;
        public Vector2 FireAt0;
        public Vector2 FireAt1;
        public bool DangerousImpact;
        public Vector2 ImpactAt;
    }
}
