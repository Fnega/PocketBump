using UnityEngine;

// 当场景没有预置 MatchDirector 时动态创建整场比赛。
// 这是测试场景和早期原型场景的启动兜底，避免手工维护完整对象层级。
namespace PocketBump
{
    public sealed class MatchBootstrap : MonoBehaviour
    {
        void Awake()
        {
            if (FindObjectOfType<DashMatchDirector>() != null)
            {
                foreach (MatchDirector legacy in FindObjectsOfType<MatchDirector>()) legacy.enabled = false;
                return;
            }
            if (FindObjectOfType<MatchDirector>() != null) return;
            DashMatchDirector.Build(GameSession.Ensure().CopySettings(), true);
        }
    }
}
