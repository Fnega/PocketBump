using System;
using UnityEngine;

namespace PocketBump
{
    // Keeping storage injectable lets migration tests avoid the player's actual preferences.
    public interface IDashBindingStore
    {
        bool HasKey(string key);
        int GetInt(string key, int fallback);
        void SetInt(string key, int value);
        void Save();
    }

    // New-rules bindings only. Loading reads legacy Boost keys but never rewrites
    // any legacy preference, so the retained production path remains unaffected.
    public sealed class DashControlBindings
    {
        public const KeyCode DefaultP1Dash = KeyCode.K;
        public const KeyCode DefaultP2Dash = KeyCode.Keypad2;
        public const string P1PreferenceKey = "PocketBump.P1Dash";
        public const string P2PreferenceKey = "PocketBump.P2Dash";
        const string LegacyP1Boost = "PocketBump.P1Boost";
        const string LegacyP2Boost = "PocketBump.P2Boost";

        readonly IDashBindingStore _store;
        bool _loaded;
        KeyCode _p1Dash = DefaultP1Dash;
        KeyCode _p2Dash = DefaultP2Dash;

        public DashControlBindings() : this(new PlayerPrefsStore()) { }
        public DashControlBindings(IDashBindingStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public KeyCode P1Dash { get { EnsureLoaded(); return _p1Dash; } }
        public KeyCode P2Dash { get { EnsureLoaded(); return _p2Dash; } }

        public KeyCode Dash(int bindingIndex)
        {
            RequireSeat(bindingIndex);
            return bindingIndex == 0 ? P1Dash : P2Dash;
        }

        public bool TrySet(int bindingIndex, KeyCode key)
        {
            RequireSeat(bindingIndex);
            EnsureLoaded();
            if (!IsAllowedDashKey(key) || key == Dash(1 - bindingIndex)) return false;
            if (bindingIndex == 0) _p1Dash = key;
            else _p2Dash = key;
            Save();
            return true;
        }

        public void ResetToDefaults(bool persist)
        {
            _p1Dash = DefaultP1Dash;
            _p2Dash = DefaultP2Dash;
            _loaded = true;
            if (persist) Save();
        }

        public void Reload()
        {
            // New Dash takes precedence for each seat; only missing new values
            // fall back to legacy Boost. Shoot does not participate in this table.
            _p1Dash = Read(P1PreferenceKey, LegacyP1Boost, DefaultP1Dash);
            _p2Dash = Read(P2PreferenceKey, LegacyP2Boost, DefaultP2Dash);
            if (!IsAllowedDashKey(_p1Dash) || !IsAllowedDashKey(_p2Dash) || _p1Dash == _p2Dash)
                ResetToDefaults(false);
            _loaded = true;
            // Loading and conflict recovery are read-only, including on first migration.
        }

        public static bool IsMovementKey(KeyCode key)
        {
            return key == KeyCode.W || key == KeyCode.A || key == KeyCode.S || key == KeyCode.D ||
                key == KeyCode.UpArrow || key == KeyCode.DownArrow ||
                key == KeyCode.LeftArrow || key == KeyCode.RightArrow;
        }

        public static bool IsAllowedDashKey(KeyCode key)
        {
            if (!Enum.IsDefined(typeof(KeyCode), key) || key == KeyCode.None || key == KeyCode.Escape || IsMovementKey(key))
                return false;
            int value = (int)key;
            return value < (int)KeyCode.Mouse0 || value > (int)KeyCode.Mouse6;
        }

        public static string Label(KeyCode key)
        {
            int value = (int)key;
            if (value >= (int)KeyCode.Alpha0 && value <= (int)KeyCode.Alpha9) return (value - (int)KeyCode.Alpha0).ToString();
            if (value >= (int)KeyCode.Keypad0 && value <= (int)KeyCode.Keypad9) return "Num" + (value - (int)KeyCode.Keypad0);
            if (key == KeyCode.LeftShift) return "LShift";
            if (key == KeyCode.RightShift) return "RShift";
            return key.ToString();
        }

        public string PregameBanner()
        {
            return "P1: WASD  " + Label(P1Dash) + " DASH     P2: ARROWS  " + Label(P2Dash) + " DASH";
        }

        void EnsureLoaded()
        {
            if (!_loaded) Reload();
        }

        KeyCode Read(string key, string legacy, KeyCode fallback)
        {
            if (_store.HasKey(key)) return (KeyCode)_store.GetInt(key, (int)fallback);
            if (_store.HasKey(legacy)) return (KeyCode)_store.GetInt(legacy, (int)fallback);
            return fallback;
        }

        void Save()
        {
            _store.SetInt(P1PreferenceKey, (int)_p1Dash);
            _store.SetInt(P2PreferenceKey, (int)_p2Dash);
            _store.Save();
        }

        static void RequireSeat(int seat)
        {
            if (seat < 0 || seat > 1) throw new ArgumentOutOfRangeException(nameof(seat));
        }

        sealed class PlayerPrefsStore : IDashBindingStore
        {
            public bool HasKey(string key) => PlayerPrefs.HasKey(key);
            public int GetInt(string key, int fallback) => PlayerPrefs.GetInt(key, fallback);
            public void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);
            public void Save() => PlayerPrefs.Save();
        }
    }
}
