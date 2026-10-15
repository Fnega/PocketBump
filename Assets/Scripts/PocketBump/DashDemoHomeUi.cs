using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PocketBump
{
    public sealed class DashDemoHomeUi : MonoBehaviour
    {
        const string VersionCaption = "射球大战 / PocketBump 1.2 免费试玩\nFnega · 同电脑双人 / 同一局域网";
        [SerializeField] DashDemoGuide _guide;
        [SerializeField] Button _help;
        [SerializeField] Slider _volume;
        [SerializeField] TMP_Text _version;
        public bool HelpVisible => _guide != null && _guide.gameObject.activeSelf;

        public static DashDemoHomeUi Install(MainMenuController controller)
        {
            var existing = controller.GetComponent<DashDemoHomeUi>();
            if (existing != null) return existing;
            Transform canvas = controller.transform.Find("MainMenuCanvas");
            var home = controller.gameObject.AddComponent<DashDemoHomeUi>();
            TMP_FontAsset font = controller.Theme != null ? controller.Theme.Font : DashUiTheme.Load()?.Font;
            home._help = DemoUi.Button(canvas, "DemoHelp", "玩法说明 / HELP", font, new Vector2(1f, 0f), new Vector2(280f, 62f));
            ((RectTransform)home._help.transform).anchoredPosition = new Vector2(-190f, 65f);
            home._version = DemoUi.Label(canvas, "DemoVersion", VersionCaption, font, 19f, new Vector2(.22f, .035f), new Vector2(.42f, .06f));
            home._guide = DashDemoGuide.Create(canvas, font);
            Transform settings = canvas.Find("SettingsPanel");
            if (settings != null)
            {
                ((RectTransform)settings).sizeDelta = new Vector2(640f, 540f);
                Reposition(settings.Find("SettingsTitle"), .88f); Reposition(settings.Find("SettingsHint"), .76f);
                Reposition(settings.Find("Windowed"), .61f); Reposition(settings.Find("Fullscreen"), .47f);
                Reposition(settings.Find("SettingsBack"), .09f);
                home._volume = DemoUi.Volume(settings, font, new Vector2(.5f, .29f), new Vector2(390f, 80f));
            }
            return home;
        }
        static void Reposition(Transform target, float y)
        {
            if (!(target is RectTransform rect)) return;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, y);
        }
        public void Wire()
        {
            if (_version != null) _version.text = VersionCaption;
            if (_help == null) return;
            _help.onClick.RemoveAllListeners(); _help.onClick.AddListener(OpenHelp);
            _guide.Refresh(new DashControlBindings(), false);
            _guide.CloseButton.onClick.RemoveAllListeners(); _guide.CloseButton.onClick.AddListener(CloseHelp);
            if (_volume != null)
            {
                _volume.onValueChanged.RemoveAllListeners(); _volume.onValueChanged.AddListener(DashAudioSettings.SetVolume);
                _volume.SetValueWithoutNotify(DashAudioSettings.Volume);
            }
        }
        public void OpenHelp()
        {
            _guide.Refresh(new DashControlBindings(), false); _guide.gameObject.SetActive(true); _guide.transform.SetAsLastSibling();
        }
        public void CloseHelp() { if (_guide != null) _guide.gameObject.SetActive(false); }
    }
}
