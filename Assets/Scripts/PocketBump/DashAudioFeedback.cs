using System;
using System.Collections.Generic;
using UnityEngine;

namespace PocketBump
{
    // One instance per match, not per viewport. Only the director's confirmed stream
    // enters this component; predicted transitions and rollback replays stay silent.
    [DisallowMultipleComponent]
    public sealed class DashAudioFeedback : MonoBehaviour
    {
        public const int MaximumVoices = 8;
        readonly DashSoundEventGate _events = new DashSoundEventGate();
        readonly AudioSource[] _voices = new AudioSource[MaximumVoices];
        readonly long[] _voiceOrder = new long[MaximumVoices];
        AudioClip[] _clips;
        DashMatchDirector _director;
        bool _subscribed;
        long _sequence;

        // Useful to presentation acceptance without depending on the machine's speakers.
        public long PlayedSoundCount { get; private set; }
        public DashSoundCue LastSound { get; private set; }

        public static DashAudioFeedback Attach(DashMatchDirector director)
        {
            if (director == null) throw new ArgumentNullException(nameof(director));
            var audio = director.GetComponent<DashAudioFeedback>();
            if (audio == null) audio = director.gameObject.AddComponent<DashAudioFeedback>();
            audio.Bind(director);
            return audio;
        }

        public void Bind(DashMatchDirector director)
        {
            if (director == null) throw new ArgumentNullException(nameof(director));
            if (_director == director) return;
            Unsubscribe(); StopVoices();
            _director = director;
            EnsureAudio();
            ResetForEpoch(director.MatchEpoch);
            if (isActiveAndEnabled) Subscribe();
        }

        void EnsureAudio()
        {
            if (_clips != null) return;
            _clips = new AudioClip[Enum.GetValues(typeof(DashSoundCue)).Length];
            for (int i = 0; i < _clips.Length; i++)
            {
                var cue = (DashSoundCue)i;
                float[] samples = DashSoundSynthesis.Generate(cue);
                AudioClip clip = AudioClip.Create("PocketBump original " + cue, samples.Length, 1, DashSoundSynthesis.SampleRate, false);
                clip.hideFlags = HideFlags.HideAndDontSave;
                clip.SetData(samples, 0);
                _clips[i] = clip;
            }
            for (int i = 0; i < _voices.Length; i++)
            {
                var child = new GameObject("Confirmed SFX voice " + i);
                child.transform.SetParent(transform, false);
                AudioSource source = child.AddComponent<AudioSource>();
                source.playOnAwake = false; source.loop = false; source.spatialBlend = 0;
                source.dopplerLevel = 0; source.volume = DashAudioSettings.Volume;
                _voices[i] = source;
            }
        }

        void Subscribe()
        {
            if (_subscribed || _director == null) return;
            _director.ConfirmedEvents += PresentConfirmedEvents;
            _director.EpochInitialized += ResetForEpoch;
            DashAudioSettings.Changed += SetSourceVolume;
            _subscribed = true;
            SetSourceVolume(DashAudioSettings.Volume);
        }

        void Unsubscribe()
        {
            if (!_subscribed) return;
            if (_director != null)
            {
                _director.ConfirmedEvents -= PresentConfirmedEvents;
                _director.EpochInitialized -= ResetForEpoch;
            }
            DashAudioSettings.Changed -= SetSourceVolume;
            _subscribed = false;
        }

        public void ResetForEpoch(int epoch)
        {
            _events.ResetForEpoch(epoch);
            StopVoices();
        }

        public void PresentConfirmedEvents(IReadOnlyList<DashArenaEvent> events)
        {
            if (!isActiveAndEnabled || _director == null || _director.NetworkPaused || events == null) return;
            foreach (DashArenaEvent item in events)
            {
                if (!_events.TryAccept(item, out DashSoundCue cue)) continue;
                // Muted events are consumed: changing volume cannot replay past feedback.
                if (DashAudioSettings.Volume <= 0) continue;
                int slot = 0;
                for (int i = 0; i < _voices.Length; i++)
                {
                    if (!_voices[i].isPlaying) { slot = i; break; }
                    if (_voiceOrder[i] < _voiceOrder[slot]) slot = i;
                }
                AudioSource source = _voices[slot];
                source.Stop();
                source.clip = _clips[(int)cue];
                source.priority = cue == DashSoundCue.Goal ? 64 : cue == DashSoundCue.Impact ? 192 : 128;
                source.Play();
                _voiceOrder[slot] = ++_sequence;
                PlayedSoundCount++; LastSound = cue;
            }
        }

        void SetSourceVolume(float value)
        {
            foreach (AudioSource voice in _voices) if (voice != null) voice.volume = value;
            if (value <= 0) StopVoices();
        }

        void StopVoices()
        {
            foreach (AudioSource voice in _voices) if (voice != null) voice.Stop();
        }

        void OnEnable() => Subscribe();
        void LateUpdate() { if (_director != null && _director.NetworkPaused) StopVoices(); }
        void OnDisable() { Unsubscribe(); StopVoices(); }
        void OnDestroy()
        {
            Unsubscribe(); StopVoices();
            if (_clips != null)
                foreach (AudioClip clip in _clips)
                    if (clip != null) { if (Application.isPlaying) Destroy(clip); else DestroyImmediate(clip); }
            foreach (AudioSource voice in _voices)
                if (voice != null) { if (Application.isPlaying) Destroy(voice.gameObject); else DestroyImmediate(voice.gameObject); }
        }
    }
}
