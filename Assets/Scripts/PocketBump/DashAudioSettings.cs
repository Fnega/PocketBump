using System;
using UnityEngine;

namespace PocketBump
{
    // Local presentation preference only. It never participates in match settings/hashes.
    public static class DashAudioSettings
    {
        public const string VolumeKey = "PocketBump.Audio.SfxVolume";
        public const float DefaultVolume = .65f;
        public static event Action<float> Changed;
        public static float Volume => Normalize(PlayerPrefs.GetFloat(VolumeKey, DefaultVolume));

        public static void SetVolume(float value)
        {
            float volume = Normalize(value);
            PlayerPrefs.SetFloat(VolumeKey, volume);
            PlayerPrefs.Save();
            Changed?.Invoke(volume);
        }

        static float Normalize(float value) => float.IsNaN(value) || float.IsInfinity(value)
            ? DefaultVolume : Mathf.Clamp01(value);
    }
}
