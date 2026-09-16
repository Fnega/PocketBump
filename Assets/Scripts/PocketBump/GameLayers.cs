using UnityEngine;

// 统一定义 Physics2D 图层和过滤关系。
// 玩法由 MatchSim 裁决，但场景仍保留碰撞体，因此要关闭无意义的物理组合。
namespace PocketBump
{
    public static class GameLayers
    {
        public const int Player = 8;
        public const int Marble = 9;
        public const int GoalNS = 10;
        public const int DunkEW = 11;
        public const int BlockPlayers = 12;
        public const int BlockMarbles = 13;
        public const int Gate = 14;
        public const int Wall = 15;

        public static void ApplyCollisionMatrix()
        {
            for (int a = Player; a <= Wall; a++)
            {
                for (int b = a; b <= Wall; b++)
                    Physics2D.IgnoreLayerCollision(a, b, false);
            }

            Ignore(Player, GoalNS);
            Ignore(Player, BlockMarbles);
            Ignore(Marble, DunkEW);
            Ignore(Marble, BlockPlayers);
            Ignore(GoalNS, DunkEW);
            Ignore(GoalNS, BlockPlayers);
            Ignore(GoalNS, BlockMarbles);
            Ignore(GoalNS, Gate);
            Ignore(GoalNS, Wall);
            Ignore(DunkEW, BlockPlayers);
            Ignore(DunkEW, BlockMarbles);
            Ignore(DunkEW, Gate);
            Ignore(DunkEW, Wall);
        }

        static void Ignore(int a, int b) => Physics2D.IgnoreLayerCollision(a, b, true);
    }
}
