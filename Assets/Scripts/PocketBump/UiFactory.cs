using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 提供项目统一风格的 Canvas、面板、文本和按钮构建方法。
// 菜单/HUD 共用工厂，减少重复 RectTransform 配置并支持测试动态搭建场景。
namespace PocketBump
{
    public static class UiFactory
    {
        static TMP_FontAsset _font;

        public static void ReloadAssets()
        {
            _font = null;
        }

        // 创建屏幕空间 Canvas，并确保场景存在 EventSystem，保证动态 UI 立即可交互。
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

        // 按钮的背景、文字、锚点和回调在一处组装，维持菜单与结果面板的一致结构。
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

        // 所有 UI 元素都通过同一 RectTransform 初始化，减少锚点/尺寸的重复样板代码。
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

        // 优先复用缓存字体；Resources 缺失时使用 TMP 默认字体，让测试环境仍能构建 UI。
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
