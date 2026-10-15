using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class DashAudioFeedbackTests
    {
        GameObject _root;
        DashMatchDirector _director;
        DashAudioFeedback _audio;
        float? _savedVolume;
        float _savedFixedDeltaTime;

        [SetUp]
        public void SetUp()
        {
            _savedVolume = PlayerPrefs.HasKey(DashAudioSettings.VolumeKey) ? (float?)PlayerPrefs.GetFloat(DashAudioSettings.VolumeKey) : null;
            _savedFixedDeltaTime = Time.fixedDeltaTime;
            DashAudioSettings.SetVolume(.65f);
            _root = new GameObject("Confirmed audio integration fixture");
            _director = _root.AddComponent<DashMatchDirector>();
            _director.AutomaticTicking = false;
            _director.InitializeLocal(ArenaRulesProfile.CreateProductionV1(), 19);
            _director.Sim.Phase = MatchState.Live; _director.Sim.PhaseTicks = 0; _director.Sim.GateClosed = false;
            _audio = DashAudioFeedback.Attach(_director);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            if (_savedVolume.HasValue) DashAudioSettings.SetVolume(_savedVolume.Value);
            else { PlayerPrefs.DeleteKey(DashAudioSettings.VolumeKey); PlayerPrefs.Save(); }
            Time.fixedDeltaTime = _savedFixedDeltaTime;
        }

        [Test]
        public void DirectorAttachesOnlyOnePoolAndEmitsOneSoundForConfirmedDash()
        {
            Assert.That(DashAudioFeedback.Attach(_director), Is.SameAs(_audio));
            Assert.That(_root.GetComponents<DashAudioFeedback>().Length, Is.EqualTo(1));
            Assert.That(_root.GetComponentsInChildren<AudioSource>().Length, Is.EqualTo(DashAudioFeedback.MaximumVoices));
            _director.InputOverride = (seat, tick, state) => new DashArenaInput { DashPressed = seat == 0 && tick == 0 };
            var events = _director.AdvanceOneTick();
            Assert.That(_audio.PlayedSoundCount, Is.EqualTo(1));
            Assert.That(_audio.LastSound, Is.EqualTo(DashSoundCue.Dash));
            int checksum = _director.Sim.Checksum();
            _audio.PresentConfirmedEvents(events);
            Assert.That(_audio.PlayedSoundCount, Is.EqualTo(1));
            Assert.That(_director.Sim.Checksum(), Is.EqualTo(checksum));
        }

        [Test]
        public void MutedEventIsConsumedAndUnmutingDoesNotReplayIt()
        {
            var events = new[] { new DashArenaEvent(0, 0, 0, 0, DashArenaEventKind.Goal, Vector2.zero) };
            DashAudioSettings.SetVolume(0);
            _audio.PresentConfirmedEvents(events);
            Assert.That(_audio.PlayedSoundCount, Is.Zero);
            Assert.That(PlayerPrefs.GetFloat(DashAudioSettings.VolumeKey, -1), Is.Zero);
            Assert.That(DashAudioSettings.Volume, Is.Zero);
            DashAudioSettings.SetVolume(.4f);
            _audio.PresentConfirmedEvents(events);
            Assert.That(_audio.PlayedSoundCount, Is.Zero);
            _audio.PresentConfirmedEvents(new[] { new DashArenaEvent(1, 0, 0, 0, DashArenaEventKind.Goal, Vector2.zero) });
            Assert.That(_audio.PlayedSoundCount, Is.EqualTo(1));
            foreach (var source in _root.GetComponentsInChildren<AudioSource>())
                Assert.That(source.volume, Is.EqualTo(.4f).Within(.0001f));
        }

        [Test]
        public void StoredMuteRestoresOnNewAudioComponent()
        {
            DashAudioSettings.SetVolume(0);
            Object.DestroyImmediate(_audio);
            _audio = DashAudioFeedback.Attach(_director);
            Assert.That(DashAudioSettings.Volume, Is.Zero);
            foreach (var source in _root.GetComponentsInChildren<AudioSource>()) Assert.That(source.volume, Is.Zero);
        }

        [Test]
        public void DisabledComponentUnsubscribesFromConfirmedStream()
        {
            _audio.enabled = false;
            _director.InputOverride = (seat, tick, state) => new DashArenaInput { DashPressed = seat == 0 && tick == 0 };
            _director.AdvanceOneTick();
            Assert.That(_audio.PlayedSoundCount, Is.Zero);
        }

        [Test]
        public void InvalidSavedVolumeCannotFeedNaNOrInfinityIntoAudioSources()
        {
            PlayerPrefs.SetFloat(DashAudioSettings.VolumeKey, float.NaN);
            Assert.That(DashAudioSettings.Volume, Is.EqualTo(DashAudioSettings.DefaultVolume));
            DashAudioSettings.SetVolume(float.PositiveInfinity);
            foreach (var source in _root.GetComponentsInChildren<AudioSource>())
                Assert.That(source.volume, Is.EqualTo(DashAudioSettings.DefaultVolume).Within(.0001f));
            DashAudioSettings.SetVolume(2);
            Assert.That(DashAudioSettings.Volume, Is.EqualTo(1));
        }
    }
}
