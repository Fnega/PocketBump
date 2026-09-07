using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PocketBump
{
    public static class UiFactory
    {
        static TMP_FontAsset _font;

        public static void ReloadAssets()
        {
            _font = null;
        }

        public static Transform CreateCanvas(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();
            return go.transform;
        }

        public static GameObject Panel(
            Transform parent,
            string name,
            Vector2 anchor,
            Vector2 offset,
            Vector2 size,
            Color color)
        {
            var go = RectObject(parent, name, anchor, offset, size);
            go.AddComponent<Image>().color = color;
            return go;
        }

        public static TextMeshProUGUI Text(
            Transform parent,
            string name,
            string value,
            float fontSize,
            TextAlignmentOptions alignment,
            Vector2 anchor,
            Vector2 offset,
            Vector2 size,
            Color color)
        {
            var go = RectObject(parent, name, anchor, offset, size);
            var text = go.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset font = Font();
            if (font != null) text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
        }

        public static Button Button(
            Transform parent,
            string name,
            string label,
            Vector2 anchor,
            Vector2 offset,
            Vector2 size,
            Color color,
            UnityAction onClick)
        {
            var go = RectObject(parent, name, anchor, offset, size);
            var image = go.AddComponent<Image>();
            image.color = VisualPalette.WithAlpha(color, 0.82f);
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var text = Text(
                go.transform,
                "Label",
                label,
                30f,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                size,
                VisualPalette.Text);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        static GameObject RectObject(
            Transform parent,
            string name,
            Vector2 anchor,
            Vector2 offset,
            Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
            return go;
        }

        static TMP_FontAsset Font()
        {
            if (_font != null) return _font;
            _font = TMP_Settings.defaultFontAsset;
            if (_font != null) return _font;

            UnityEngine.Font source = UnityEngine.Font.CreateDynamicFontFromOSFont(
                new[] { "Arial", "Segoe UI", "Liberation Sans" },
                40);
            if (source == null) return null;
            _font = TMP_FontAsset.CreateFontAsset(source);
            if (_font != null)
                _font.name = "PocketBump Runtime Font";
            return _font;
        }

        static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }
    }
}
