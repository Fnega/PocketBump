using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PocketBump
{
    // Applied only by an explicit authoring operation, never by a saved scene's Awake.
    [CreateAssetMenu(menuName = "Pocket Bump/Dash UI Theme")]
    public sealed class DashUiTheme : ScriptableObject
    {
        public TMP_FontAsset Font;
        public Color Background = new Color(.96f, .95f, .88f);
        public Color Panel = new Color(.83f, .93f, .85f);
        public Color Text = new Color(.10f, .23f, .23f);
        public Color Muted = new Color(.27f, .39f, .36f);
        public Color Cyan = new Color(.28f, .78f, .83f);
        public Color Orange = new Color(1f, .67f, .36f);
        public Color Mint = new Color(.57f, .83f, .66f);
        public Color Danger = new Color(.86f, .29f, .30f);
        public Color Yellow = new Color(1f, .84f, .32f);

        public static DashUiTheme Load() => Resources.Load<DashUiTheme>("PocketBump/DashUiTheme");

        public void ApplyTo(Transform root)
        {
            if (root == null) return;
            foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (Font != null) label.font = Font;
                label.color = label.name.Contains("Error") ? Danger : Text;
            }
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                // Keycap and icon imagery has semantic meaning; retain its sprite.
                if (image.name.Contains("Key") || image.name.Contains("Icon")) continue;
                Button button = image.GetComponent<Button>();
                if (button != null)
                {
                    image.sprite = null;
                    image.type = Image.Type.Simple;
                    image.color = image.name.Contains("P1") || image.name == "Start" ? Cyan
                        : image.name.Contains("P2") || image.name == "JoinLan" ? Orange : Mint;
                    ColorBlock colors = button.colors;
                    colors.normalColor = Color.white;
                    colors.highlightedColor = new Color(.92f, 1f, .94f);
                    colors.pressedColor = new Color(.74f, .89f, .78f);
                    colors.selectedColor = colors.highlightedColor;
                    colors.disabledColor = new Color(.7f, .7f, .7f, .6f);
                    button.colors = colors;
                }
                else if (image.name == "Background" || image.name.Contains("Panel") || image.name.Contains("Region") ||
                    image.GetComponent<TMP_InputField>() != null)
                {
                    image.sprite = null;
                    image.type = Image.Type.Simple;
                    image.color = image.name == "MainPanel" ? Color.clear : image.name == "Background" ? Background : Panel;
                }
            }
            foreach (TMP_InputField input in root.GetComponentsInChildren<TMP_InputField>(true))
            {
                input.customCaretColor = true;
                input.caretColor = Text;
                input.selectionColor = new Color(Cyan.r, Cyan.g, Cyan.b, .4f);
            }
        }
    }
}
