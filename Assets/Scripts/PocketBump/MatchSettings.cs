using UnityEngine;

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
