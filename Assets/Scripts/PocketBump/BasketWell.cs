using UnityEngine;

// 独立脚本资源确保烘焙到 Arena 场景后能保留有效的 MonoScript 引用。
// 权威收集逻辑仍由 MatchSim 处理；该触发器仅转发旧场景兼容事件。
namespace PocketBump
{
    public sealed class BasketWell : MonoBehaviour
    {
        public PlayerBasket Basket;

        void OnTriggerEnter2D(Collider2D other)
        {
            if (Basket != null) Basket.HandleWellOverlap(other);
        }

        void OnTriggerStay2D(Collider2D other)
        {
            if (Basket != null) Basket.HandleWellOverlap(other);
        }
    }
}
