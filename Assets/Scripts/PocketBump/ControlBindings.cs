using UnityEngine;

namespace PocketBump
{
    public enum SkillBinding
    {
        P1Shoot,
        P1Boost,
        P2Shoot,
        P2Boost
    }

    public static class ControlBindings
    {
        public const KeyCode DefaultP1Shoot = KeyCode.J;
        public const KeyCode DefaultP1Boost = KeyCode.K;
        public const KeyCode DefaultP2Shoot = KeyCode.Alpha1;
        public const KeyCode DefaultP2Boost = KeyCode.Alpha2;

        const string PrefP1Shoot = "PocketBump.P1Shoot";
        const string PrefP1Boost = "PocketBump.P1Boost";
        const string PrefP2Shoot = "PocketBump.P2Shoot";
        const string PrefP2Boost = "PocketBump.P2Boost";

        static bool _loaded;
        static KeyCode _p1Shoot = DefaultP1Shoot;
        static KeyCode _p1Boost = DefaultP1Boost;
        static KeyCode _p2Shoot = DefaultP2Shoot;
        static KeyCode _p2Boost = DefaultP2Boost;

        public static KeyCode P1Shoot { get { EnsureLoaded(); return _p1Shoot; } }
        public static KeyCode P1Boost { get { EnsureLoaded(); return _p1Boost; } }
        public static KeyCode P2Shoot { get { EnsureLoaded(); return _p2Shoot; } }
        public static KeyCode P2Boost { get { EnsureLoaded(); return _p2Boost; } }

        public static KeyCode Shoot(int playerIndex)
        {
            return playerIndex == 0 ? P1Shoot : P2Shoot;
        }

        public static KeyCode Boost(int playerIndex)
        {
            return playerIndex == 0 ? P1Boost : P2Boost;
        }

        public static void ResetToDefaults(bool persist)
        {
            _p1Shoot = DefaultP1Shoot;
            _p1Boost = DefaultP1Boost;
            _p2Shoot = DefaultP2Shoot;
            _p2Boost = DefaultP2Boost;
            _loaded = true;
            if (persist) Save();
        }

        public static bool TrySet(SkillBinding slot, KeyCode key)
        {
            EnsureLoaded();
            if (!IsAllowedSkillKey(key)) return false;
            if (IsMovementKey(key)) return false;
            if (UsedByOtherSlot(slot, key)) return false;

            SetSlot(slot, key);
            Save();
            return true;
        }

        public static bool IsMovementKey(KeyCode key)
        {
            return key == KeyCode.W || key == KeyCode.A || key == KeyCode.S || key == KeyCode.D ||
                   key == KeyCode.UpArrow || key == KeyCode.DownArrow ||
                   key == KeyCode.LeftArrow || key == KeyCode.RightArrow;
        }

        public static bool IsAllowedSkillKey(KeyCode key)
        {
            if (key == KeyCode.None || key == KeyCode.Escape) return false;
            int code = (int)key;
            if (code >= (int)KeyCode.Mouse0 && code <= (int)KeyCode.Mouse6) return false;
            return true;
        }

        public static string Label(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.Alpha0: return "0";
                case KeyCode.Alpha1: return "1";
                case KeyCode.Alpha2: return "2";
                case KeyCode.Alpha3: return "3";
                case KeyCode.Alpha4: return "4";
                case KeyCode.Alpha5: return "5";
                case KeyCode.Alpha6: return "6";
                case KeyCode.Alpha7: return "7";
                case KeyCode.Alpha8: return "8";
                case KeyCode.Alpha9: return "9";
                case KeyCode.Keypad0: return "Num0";
                case KeyCode.Keypad1: return "Num1";
                case KeyCode.Keypad2: return "Num2";
                case KeyCode.Keypad3: return "Num3";
                case KeyCode.Keypad4: return "Num4";
                case KeyCode.Keypad5: return "Num5";
                case KeyCode.Keypad6: return "Num6";
                case KeyCode.Keypad7: return "Num7";
                case KeyCode.Keypad8: return "Num8";
                case KeyCode.Keypad9: return "Num9";
                case KeyCode.LeftShift: return "LShift";
                case KeyCode.RightShift: return "RShift";
                case KeyCode.Space: return "Space";
                default: return key.ToString();
            }
        }

        public static string PregameBanner()
        {
            EnsureLoaded();
            return
                "P1: WASD  " + Label(_p1Shoot) + " SHOOT  " + Label(_p1Boost) + " BOOST     " +
                "P2: ARROWS  " + Label(_p2Shoot) + " SHOOT  " + Label(_p2Boost) + " BOOST";
        }

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _p1Shoot = ReadPref(PrefP1Shoot, DefaultP1Shoot);
            _p1Boost = ReadPref(PrefP1Boost, DefaultP1Boost);
            _p2Shoot = ReadPref(PrefP2Shoot, DefaultP2Shoot);
            _p2Boost = ReadPref(PrefP2Boost, DefaultP2Boost);
            if (!BindingsValid())
                ResetToDefaults(true);
            _loaded = true;
        }

        static bool BindingsValid()
        {
            KeyCode[] keys = { _p1Shoot, _p1Boost, _p2Shoot, _p2Boost };
            for (int i = 0; i < keys.Length; i++)
            {
                if (!IsAllowedSkillKey(keys[i]) || IsMovementKey(keys[i])) return false;
                for (int j = i + 1; j < keys.Length; j++)
                {
                    if (keys[i] == keys[j]) return false;
                }
            }

            return true;
        }

        static bool UsedByOtherSlot(SkillBinding slot, KeyCode key)
        {
            if (slot != SkillBinding.P1Shoot && _p1Shoot == key) return true;
            if (slot != SkillBinding.P1Boost && _p1Boost == key) return true;
            if (slot != SkillBinding.P2Shoot && _p2Shoot == key) return true;
            if (slot != SkillBinding.P2Boost && _p2Boost == key) return true;
            return false;
        }

        static void SetSlot(SkillBinding slot, KeyCode key)
        {
            switch (slot)
            {
                case SkillBinding.P1Shoot: _p1Shoot = key; break;
                case SkillBinding.P1Boost: _p1Boost = key; break;
                case SkillBinding.P2Shoot: _p2Shoot = key; break;
                case SkillBinding.P2Boost: _p2Boost = key; break;
            }
        }

        static KeyCode ReadPref(string key, KeyCode fallback)
        {
            if (!PlayerPrefs.HasKey(key)) return fallback;
            return (KeyCode)PlayerPrefs.GetInt(key, (int)fallback);
        }

        static void Save()
        {
            PlayerPrefs.SetInt(PrefP1Shoot, (int)_p1Shoot);
            PlayerPrefs.SetInt(PrefP1Boost, (int)_p1Boost);
            PlayerPrefs.SetInt(PrefP2Shoot, (int)_p2Shoot);
            PlayerPrefs.SetInt(PrefP2Boost, (int)_p2Boost);
            PlayerPrefs.Save();
        }
    }
}
