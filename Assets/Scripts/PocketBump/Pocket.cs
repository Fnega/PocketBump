using System;
using UnityEngine;

// 旧 Physics2D 场景触发器的口袋类型标记与兼容入口。
// 正式得分由 MatchSim 的确定性区域检测决定，组件不直接修改比赛状态。
namespace PocketBump
{
    public enum PocketKind
    {
        MarbleGoal,
        Dunk
    }

    public sealed class Pocket : MonoBehaviour
    {
        public PocketKind Kind;
        public int OwnerIndex = -1;

        public event Action<Pocket, Collider2D> Entered;

        void OnTriggerEnter2D(Collider2D other)
        {
            Entered?.Invoke(this, other);
        }
    }
}
