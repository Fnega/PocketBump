using UnityEngine;

// 表示一局比赛可配置的规则，并在进入模拟前限制合法范围。
// 这样异常 UI 或网络数据不会产生无效胜利条件或超出弹珠数组容量。
namespace PocketBump
{
    [System.Serializable]
    public sealed class MatchSettings
    {
        public const int DefaultPointsToWin = 8;
        public const int DefaultMarbleCount = 1;
        public const int MinPointsToWin = 1;
        public const int MaxPointsToWin = 21;
        public const int MinMarbles = 1;
        public const int MaxMarbles = 8;

        public int pointsToWin = DefaultPointsToWin;
        public int marbleCount = DefaultMarbleCount;

        public void Clamp()
        {
            pointsToWin = Mathf.Clamp(pointsToWin, MinPointsToWin, MaxPointsToWin);
            marbleCount = Mathf.Clamp(marbleCount, MinMarbles, MaxMarbles);
        }
    }
}
