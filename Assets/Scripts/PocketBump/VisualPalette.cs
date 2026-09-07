using UnityEngine;

namespace PocketBump
{
    public static class VisualPalette
    {
        public static readonly Color Tabletop = Hex("07131F");
        public static readonly Color TabletopAccent = Hex("0E2B3D");
        public static readonly Color Wall = Hex("27465A");
        public static readonly Color PlayerOne = Hex("26E6FF");
        public static readonly Color PlayerTwo = Hex("FF8A2A");
        public static readonly Color Marble = Hex("EAF7FF");
        public static readonly Color Danger = Hex("FF385D");
        public static readonly Color Gate = Hex("FFE45C");
        public static readonly Color Text = Hex("EAF7FF");

        static Material _spriteMaterial;

        public static void ReloadAssets()
        {
            _spriteMaterial = null;
        }

        public static Color PlayerColor(int playerIndex)
        {
            return playerIndex == 0 ? PlayerOne : PlayerTwo;
        }

        public static Material SpriteMaterial()
        {
            if (_spriteMaterial != null) return _spriteMaterial;
            _spriteMaterial = Resources.Load<Material>("PocketBump/NeonSprite");
            if (_spriteMaterial != null) return _spriteMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            _spriteMaterial = new Material(shader) { name = "PocketBump Neon Sprite" };
            return _spriteMaterial;
        }

        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        static Color Hex(string value)
        {
            return ColorUtility.TryParseHtmlString("#" + value, out Color color)
                ? color
                : Color.white;
        }
    }
}
