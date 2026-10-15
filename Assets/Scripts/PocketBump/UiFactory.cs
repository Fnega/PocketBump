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
            var image = go.AddComponent<Image>();
            image.color = color;
            StylePanel(image, size);
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
            image.sprite = ArtCatalog.Sprite("UI/button_rectangle", 100f, new Vector4(9f, 9f, 9f, 9f));
            image.type = Image.Type.Sliced;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            colors.pressedColor = new Color(0.72f, 0.78f, 0.84f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;

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

        public static Image KeyPrompt(
            Transform parent,
            string name,
            KeyCode key,
            Vector2 anchor,
            Vector2 offset,
            Vector2 size,
            Color color)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null ? existing.gameObject : RectObject(parent, name, anchor, offset, size);
            if (existing != null)
            {
                RectTransform rect = go.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = anchor;
                rect.anchoredPosition = offset;
                rect.sizeDelta = size;
            }
            Image image = go.GetComponent<Image>();
            if (image == null) image = go.AddComponent<Image>();
            image.sprite = ArtCatalog.KeySprite(key);
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = image.sprite != null;
            return image;
        }

        public static bool SetKeyPrompt(Transform button, KeyCode key)
        {
            if (button == null) return false;
            Sprite sprite = ArtCatalog.KeySprite(key);
            Transform existing = button.Find("KeyPrompt");
            GameObject go = existing != null ? existing.gameObject : RectObject(
                button, "KeyPrompt", new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(30f, 30f));
            Image image = go.GetComponent<Image>();
            if (image == null) image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = VisualPalette.Text;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = sprite != null;
            return sprite != null;
        }

        public static void ApplyTheme(Transform root)
        {
            if (root == null) return;
            Image[] images = root.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                Button button = image.GetComponent<Button>();
                if (button != null)
                {
                    image.sprite = ArtCatalog.Sprite("UI/button_rectangle", 100f, new Vector4(9f, 9f, 9f, 9f));
                    image.type = Image.Type.Sliced;
                    continue;
                }

                if (image.color.a <= 0.01f) continue;
                RectTransform rect = image.rectTransform;
                if (image.name.Contains("Panel") || image.name.Contains("Region") || rect.sizeDelta.x >= 1000f)
                    StylePanel(image, rect.sizeDelta);
            }
        }

        static void StylePanel(Image image, Vector2 size)
        {
            if (size.x >= 1000f || size.y >= 900f)
            {
                image.sprite = ArtCatalog.Sprite("Patterns/menu_diamonds", 256f);
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                Color tint = image.color;
                image.color = new Color(
                    Mathf.Lerp(VisualPalette.Tabletop.r, tint.r, 0.35f),
                    Mathf.Lerp(VisualPalette.Tabletop.g, tint.g, 0.35f),
                    Mathf.Lerp(VisualPalette.Tabletop.b, tint.b, 0.35f),
                    Mathf.Min(0.28f, tint.a));
            }
            else
            {
                image.sprite = ArtCatalog.Sprite(
                    "UI/panel_rectangle_screws", 100f, new Vector4(12f, 12f, 12f, 12f));
                image.type = Image.Type.Sliced;
            }
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
