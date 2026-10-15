using UnityEngine;

// 生成只属于表现层的短生命周期脉冲特效。
// 联机特效记录来源 Tick，以便回滚时移除已被修正历史中的“幽灵特效”。
namespace PocketBump
{
    public enum GameFxKind
    {
        Ring,
        Collect,
        Fire,
        Impact,
        Score,
        Gate
    }

    public sealed class GameFx : MonoBehaviour
    {
        SpriteRenderer _renderer;
        Color _color;
        float _duration;
        float _age;
        float _startScale;
        float _endScale;
        int _tick = -1;

        public static void Pulse(Vector2 position, Color color, float startScale, float endScale, float duration)
        {
            Pulse(position, color, startScale, endScale, duration, -1, GameFxKind.Ring);
        }

        // tick=-1 表示普通本地表现；联机事件传入来源 Tick，供回滚定向清理。
        public static void Pulse(Vector2 position, Color color, float startScale, float endScale, float duration, int tick)
        {
            Pulse(position, color, startScale, endScale, duration, tick, GameFxKind.Ring);
        }

        public static void Pulse(
            Vector2 position,
            Color color,
            float startScale,
            float endScale,
            float duration,
            int tick,
            GameFxKind kind)
        {
            if (!Application.isPlaying) return;
            var go = new GameObject("PulseFx");
            go.transform.position = position;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = ArtCatalog.FxSprite(kind);
            if (renderer.sprite == null) renderer.sprite = SpriteFactory.Circle();
            renderer.material = VisualPalette.SpriteMaterial();
            renderer.sortingOrder = 30;
            var fx = go.AddComponent<GameFx>();
            fx._renderer = renderer;
            fx._color = color;
            fx._duration = Mathf.Max(0.05f, duration);
            fx._startScale = startScale;
            fx._endScale = endScale;
            fx._tick = tick;
            go.transform.localScale = Vector3.one * startScale;
        }

        public static void DiscardNewerThan(int tick)
        {
            GameFx[] all = Object.FindObjectsOfType<GameFx>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i]._tick >= 0 && all[i]._tick >= tick)
                    Object.Destroy(all[i].gameObject);
            }
        }

        // 特效用渲染时间平滑播放，不进入 MatchSim，因此不会影响任何权威结果。
        void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / _duration);
            transform.localScale = Vector3.one * Mathf.Lerp(_startScale, _endScale, t);
            _renderer.color = VisualPalette.WithAlpha(_color, (1f - t) * 0.85f);
            if (t >= 1f)
                Destroy(gameObject);
        }
    }
}
