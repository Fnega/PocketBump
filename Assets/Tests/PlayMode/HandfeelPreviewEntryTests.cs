#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PocketBump.Tests
{
    public sealed class HandfeelPreviewEntryTests
    {
        const string Key = "PocketBump.HandfeelCandidate.NextPlay";
        bool _selected, _sessionSelected;
        float _fixedDelta;
        GameObject _root;
        GameSession _existingSession;

        [SetUp]
        public void Setup()
        {
            _selected = DashHandfeelPreview.Selected;
            _sessionSelected = SessionState.GetBool(Key, false);
            _fixedDelta = Time.fixedDeltaTime;
            _existingSession = Object.FindObjectOfType<GameSession>();
        }

        [TearDown]
        public void Restore()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            if (_existingSession == null)
            {
                GameSession created = Object.FindObjectOfType<GameSession>();
                if (created != null) Object.DestroyImmediate(created.gameObject);
            }
            SessionState.SetBool(Key, _sessionSelected);
            DashHandfeelPreview.Selected = _selected;
            Time.fixedDeltaTime = _fixedDelta;
        }

        // Selection normally comes from the edit-mode-only menu before Play starts.
        // The menu itself is tested separately without constructing a GameSession.
        static void Select(bool candidate) => DashHandfeelPreview.Selected = candidate;

        [TestCase(false)]
        [TestCase(true)]
        public void ExplicitEditorSelectionControlsOnlyNextAuthoredInitialization(bool candidate)
        {
            Select(candidate);
            _root = new GameObject("Handfeel A B entry fixture");
            var director = _root.AddComponent<DashMatchDirector>();
            director.AutomaticTicking = false;
            Assert.That(GameSession.Ensure().Kind, Is.EqualTo(PlayKind.Local));
            director.InitializeAuthored(new MatchSettings { pointsToWin = 5, marbleCount = 3 }, false);
            ArenaRulesProfile expected = candidate
                ? ArenaRulesProfile.CreateHandfeelCandidate(5, 3)
                : ArenaRulesProfile.CreateProductionV1(5, 3);
            Assert.That(director.Rules.ContentHash, Is.EqualTo(expected.ContentHash));
            Assert.That(director.Rules.IsProvisional, Is.EqualTo(candidate));
            Assert.That(director.Sim.Tick, Is.Zero);
            // Changing next-play selection must never replace an initialized world.
            Select(!candidate);
            director.InitializeAuthored(new MatchSettings(), false);
            Assert.That(director.Rules.ContentHash, Is.EqualTo(expected.ContentHash));
        }

    }
}
#endif
