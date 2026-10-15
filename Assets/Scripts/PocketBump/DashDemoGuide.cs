using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PocketBump
{
    // Shared copy and authored UI primitives. Opening instructions never changes
    // match rules, key bindings or the simulation clock.
    public sealed class DashDemoGuide : MonoBehaviour
    {
        public const string Title = "射球大战 · 玩法说明";
        public const string LeftRules = "<b>撞球进门</b>\n将白球撞进对方球门。达到目标分数，并领先 2 分获胜。\n\n<b>Dash 冲刺</b>\n按下后沿当前输入方向冲刺 0.3 秒，期间不能转向；无方向输入则沿朝向。结束后冷却 3 秒，再次按键触发。\n\n<b>掉落与复活</b>\n掉落不给对手加分，3 秒后在己方区域复活。进球后双方归位，球回中场重新开球。";
        public const string RightRules = "<b>障碍物</b>\n撞圆柱会短暂眩晕，头上出现“晕”。普通撞墙会反弹，高速 Dash 可撞碎墙壁；10 秒后等待位置空出再修复。\n\n<b>三色 Buff</b>\n绿色：强化撞击玩家与球的力量。\n蓝色：随机传送到安全位置。\n紫色：免击退、免眩晕，仍可掉落。\n\n<b>认准球门</b>\n蓝方 P1 向南进攻橙门 ↓\n橙方 P2 向北进攻蓝门 ↑\n右上角小地图固定北朝上。";
        public const string Scope = "免费试玩 1.2 · 同电脑双人分屏 / 同一局域网对战";
        public const string ExtraGlyphs = "玩法说明撞球进门将白对方达到目标分数并领先获胜按下沿当前方向刺期间不能转无输入则朝结束冷却再次键触发掉落不给手加区域双方归位回中场重新开障碍物柱短暂头上出现普通墙反弹高速碎壁后位置空修复三色力量随机认准南橙北右角小地图固定电脑双人屏同一局域网对战本机移动数字行使用自己的无需盘设置改键返回关闭继续比赛暂停菜单仍在进行角色可能受击音效音量静打开报告目录保存失败免费试玩开发者个人本地使用，。；：、“”↑↓";
        [SerializeField] TMP_Text _controls;
        public Button CloseButton { get; private set; }
        public string ControlsText => _controls != null ? _controls.text : "";

        public static DashDemoGuide Create(Transform parent, TMP_FontAsset font)
        {
            RectTransform shade = DemoUi.Panel(parent, "QuickStartGuide", new Color(0f, 0f, 0f, .55f), Vector2.zero, Vector2.one);
            shade.GetComponent<Image>().raycastTarget = true;
            var guide = shade.gameObject.AddComponent<DashDemoGuide>();
            RectTransform card = DemoUi.Panel(shade, "GuideCard", DemoUi.PanelColor, new Vector2(.05f, .055f), new Vector2(.95f, .945f));
            DemoUi.Label(card, "GuideTitle", Title, font, 34f, new Vector2(.5f, .92f), new Vector2(.92f, .08f));
            DemoUi.Label(card, "GuideScope", Scope, font, 21f, new Vector2(.5f, .84f), new Vector2(.94f, .06f));
            guide._controls = DemoUi.Label(card, "CurrentControls", "", font, 23f, new Vector2(.5f, .74f), new Vector2(.94f, .12f));
            DemoUi.Label(card, "CoreRules", LeftRules, font, 24f, new Vector2(.265f, .405f), new Vector2(.43f, .52f), TextAlignmentOptions.TopLeft);
            DemoUi.Label(card, "WorldRules", RightRules, font, 24f, new Vector2(.745f, .405f), new Vector2(.43f, .52f), TextAlignmentOptions.TopLeft);
            guide.CloseButton = DemoUi.Button(card, "CloseGuide", "返回 / ESC", font, new Vector2(.5f, .07f), new Vector2(270f, 57f));
            guide.Refresh(new DashControlBindings(), false);
            shade.gameObject.SetActive(false);
            return guide;
        }

        public void Refresh(DashControlBindings bindings, bool lan)
        {
            TMP_Text scope = transform.Find("GuideCard/GuideScope")?.GetComponent<TMP_Text>();
            if (scope != null) scope.text = Scope;
            if (CloseButton == null) CloseButton = transform.Find("GuideCard/CloseGuide")?.GetComponent<Button>();
            if (_controls == null) return;
            _controls.text = lan
                ? "本机：WASD 移动 + " + DashControlBindings.Label(bindings.P1Dash) + " 冲刺\n双方都使用自己电脑的 P1 键位；ESC 打开个人菜单"
                : "P1：WASD + " + DashControlBindings.Label(bindings.P1Dash) + " 冲刺    P2：方向键 + " + DashControlBindings.Label(bindings.P2Dash) + " 冲刺\n2 = 键盘上方数字行，Num2 = 小键盘；可在规则设置中改键";
        }
    }

    internal static class DemoUi
    {
        internal static Color Ink => DashUiTheme.Load() != null ? DashUiTheme.Load().Text : new Color(.1f, .23f, .23f);
        internal static Color PanelColor => DashUiTheme.Load() != null ? DashUiTheme.Load().Panel : new Color(.83f, .93f, .85f);
        internal static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero; return rect;
        }
        internal static RectTransform Panel(Transform parent, string name, Color color, Vector2 min, Vector2 max)
        {
            RectTransform rect = Rect(parent, name, min, max);
            Image image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return rect;
        }
        internal static TMP_Text Label(Transform parent, string name, string text, TMP_FontAsset font, float size, Vector2 anchor, Vector2 fraction, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            RectTransform rect = Rect(parent, name, anchor - fraction * .5f, anchor + fraction * .5f);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>(); label.font = font; label.text = text; label.color = Ink;
            label.fontSize = size; label.enableAutoSizing = true; label.fontSizeMin = size * .72f; label.fontSizeMax = size;
            label.alignment = alignment; label.enableWordWrapping = true; label.raycastTarget = false;
            return label;
        }
        internal static Button Button(Transform parent, string name, string caption, TMP_FontAsset font, Vector2 anchor, Vector2 size)
        {
            RectTransform rect = Panel(parent, name, new Color(.57f, .83f, .66f), anchor, anchor); rect.sizeDelta = size;
            Image image = rect.GetComponent<Image>(); image.raycastTarget = true;
            Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
            Label(rect, "Label", caption, font, 24f, Vector2.one * .5f, new Vector2(.95f, .92f)); return button;
        }
        internal static Slider Volume(Transform parent, TMP_FontAsset font, Vector2 anchor, Vector2 size)
        {
            RectTransform root = Rect(parent, "DemoVolume", anchor, anchor); root.sizeDelta = size;
            Label(root, "VolumeLabel", "音效音量（0 = 静音）", font, 22f, new Vector2(.5f, .87f), new Vector2(1f, .4f));
            RectTransform bar = Panel(root, "Slider", new Color(.33f, .48f, .41f), new Vector2(.05f, .14f), new Vector2(.95f, .44f));
            bar.GetComponent<Image>().raycastTarget = true;
            Slider slider = bar.gameObject.AddComponent<Slider>(); slider.minValue = 0f; slider.maxValue = 1f;
            RectTransform fill = Panel(bar, "Fill", new Color(.2f, .68f, .69f), Vector2.zero, Vector2.one);
            RectTransform handle = Panel(bar, "Handle", Color.white, new Vector2(0f, .5f), new Vector2(0f, .5f)); handle.sizeDelta = new Vector2(24f, 30f);
            handle.GetComponent<Image>().raycastTarget = true;
            slider.fillRect = fill; slider.handleRect = handle; slider.targetGraphic = handle.GetComponent<Image>();
            var navigation = slider.navigation; navigation.mode = Navigation.Mode.None; slider.navigation = navigation;
            slider.SetValueWithoutNotify(DashAudioSettings.Volume); return slider;
        }
    }
}
