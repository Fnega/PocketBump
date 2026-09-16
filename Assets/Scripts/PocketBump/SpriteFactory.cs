using UnityEngine;

// 延迟加载 Resources 中的基础方形/圆形 Sprite，并提供运行时回退资源。
// 集中加载让 UI 和特效共享引用，也避免每个组件自行查找资源。
namespace PocketBump
{
    public static class SpriteFactory
    {
        static Sprite _box;
        static Sprite _circle;

        public static void ReloadAssets()
        {
            _box = null;
            _circle = null;
        }

        public static Sprite Box()
        {
            if (_box != null) return _box;
            _box = Resources.Load<Sprite>("PocketBump/Box");
            if (_box != null) return _box;
            var tex = Texture2D.whiteTexture;
            _box = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
            return _box;
        }

        public static Sprite Circle()
        {
            if (_circle != null) return _circle;
            _circle = Resources.Load<Sprite>("PocketBump/Circle");
            if (_circle != null) return _circle;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            float c = (size - 1) * 0.5f;
            float r = size * 0.5f - 1.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c));
                    tex.SetPixel(x, y, d <= r ? Color.white : Color.clear);
                }
            }

            tex.Apply();
            _circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return _circle;
        }
    }
}
