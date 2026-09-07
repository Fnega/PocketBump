using UnityEngine;

namespace PocketBump
{
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
            Pulse(position, color, startScale, endScale, duration, -1);
        }

        public static void Pulse(Vector2 position, Color color, float startScale, float endScale, float duration, int tick)
        {
            if (!Application.isPlaying) return;
            var go = new GameObject("PulseFx");
            go.transform.position = position;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = SpriteFactory.Circle();
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
