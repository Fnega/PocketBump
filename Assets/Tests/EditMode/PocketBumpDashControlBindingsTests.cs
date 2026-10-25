using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class PocketBumpDashControlBindingsTests
    {
        MemoryDashBindingStore _store;
        DashControlBindings _bindings;

        [SetUp]
        public void Setup()
        {
            _store = new MemoryDashBindingStore();
            _bindings = new DashControlBindings(_store);
        }

        [Test]
        public void DefaultsAreTwoDashActionsAndLoadingDoesNotWrite()
        {
            Assert.That(_bindings.P1Dash, Is.EqualTo(KeyCode.K));
            Assert.That(_bindings.P2Dash, Is.EqualTo(KeyCode.Keypad2));
            Assert.That(_bindings.MinimapToggle, Is.EqualTo(KeyCode.M));
            Assert.That(_bindings.PregameBanner(), Does.Contain("P2: ARROWS  Num2 DASH"));
            Assert.That(_bindings.PregameBanner(), Does.Not.Contain("SHOOT").And.Not.Contain("BOOST"));
            Assert.That(_store.WrittenKeys, Is.Empty);
            Assert.That(_store.SaveCount, Is.Zero);
        }

        [TestCase(DashControlBindings.P2PreferenceKey)]
        [TestCase("PocketBump.P2Boost")]
        public void SavedNumberRowBindingRemainsDistinctFromNewKeypadDefault(string preferenceKey)
        {
            _store.Values[preferenceKey] = (int)KeyCode.Alpha2;
            Assert.That(_bindings.P2Dash, Is.EqualTo(KeyCode.Alpha2));
            Assert.That(DashControlBindings.Label(_bindings.P2Dash), Is.EqualTo("2"));
            Assert.That(DashControlBindings.Label(DashControlBindings.DefaultP2Dash), Is.EqualTo("Num2"));
            Assert.That(_bindings.PregameBanner(), Does.Contain("P2: ARROWS  2 DASH").And.Not.Contain("Num2"));
            Assert.That(_store.Values[preferenceKey], Is.EqualTo((int)KeyCode.Alpha2));
            Assert.That(_store.WrittenKeys, Is.Empty);
            Assert.That(_store.SaveCount, Is.Zero);
        }

        [Test]
        public void ValidLegacyBoostPairMigratesWithoutReadingOrReservingShoot()
        {
            _store.Values["PocketBump.P1Boost"] = (int)KeyCode.Q;
            _store.Values["PocketBump.P2Boost"] = (int)KeyCode.Keypad3;
            _store.Values["PocketBump.P1Shoot"] = (int)KeyCode.Q;
            _store.Values["PocketBump.P2Shoot"] = (int)KeyCode.Keypad3;
            Assert.That(_bindings.P1Dash, Is.EqualTo(KeyCode.Q));
            Assert.That(_bindings.P2Dash, Is.EqualTo(KeyCode.Keypad3));
            Assert.That(_store.ReadKeys, Does.Not.Contain("PocketBump.P1Shoot"));
            Assert.That(_store.ReadKeys, Does.Not.Contain("PocketBump.P2Shoot"));
            Assert.That(_store.WrittenKeys, Is.Empty);
            Assert.That(_store.SaveCount, Is.Zero);
        }

        [Test]
        public void NewDashPreferencesTakePerSeatPrecedenceOverLegacyBoost()
        {
            _store.Values[DashControlBindings.P1PreferenceKey] = (int)KeyCode.J;
            _store.Values["PocketBump.P1Boost"] = (int)KeyCode.Q;
            _store.Values["PocketBump.P2Boost"] = (int)KeyCode.Keypad1;
            Assert.That(_bindings.P1Dash, Is.EqualTo(KeyCode.J));
            Assert.That(_bindings.P2Dash, Is.EqualTo(KeyCode.Keypad1));
            Assert.That(_store.ReadKeys, Does.Not.Contain("PocketBump.P1Boost"));
            Assert.That(_store.WrittenKeys, Is.Empty);
        }

        [TestCase((int)KeyCode.None)]
        [TestCase((int)KeyCode.Escape)]
        [TestCase((int)KeyCode.W)]
        [TestCase((int)KeyCode.LeftArrow)]
        [TestCase((int)KeyCode.Mouse0)]
        [TestCase((int)KeyCode.Mouse6)]
        [TestCase(-1)]
        [TestCase(99999)]
        public void InvalidStoredKeyFallsBackAsAWholePairWithoutRepairingDisk(int invalid)
        {
            _store.Values[DashControlBindings.P1PreferenceKey] = invalid;
            _store.Values["PocketBump.P1Boost"] = (int)KeyCode.Q;
            _store.Values["PocketBump.P2Boost"] = (int)KeyCode.Keypad3;
            Assert.That(_bindings.P1Dash, Is.EqualTo(KeyCode.K));
            Assert.That(_bindings.P2Dash, Is.EqualTo(KeyCode.Keypad2));
            Assert.That(_store.Values[DashControlBindings.P1PreferenceKey], Is.EqualTo(invalid));
            Assert.That(_store.WrittenKeys, Is.Empty);
            Assert.That(_store.SaveCount, Is.Zero);
        }

        [Test]
        public void DuplicateMigratedKeysFallBackToTheValidDefaultPair()
        {
            _store.Values["PocketBump.P1Boost"] = (int)KeyCode.Q;
            _store.Values["PocketBump.P2Boost"] = (int)KeyCode.Q;
            Assert.That(_bindings.P1Dash, Is.EqualTo(KeyCode.K));
            Assert.That(_bindings.P2Dash, Is.EqualTo(KeyCode.Keypad2));
            Assert.That(_store.WrittenKeys, Is.Empty);
        }

        [Test]
        public void PartialMigrationThatConflictsWithMissingSeatDefaultAlsoRecovers()
        {
            _store.Values["PocketBump.P1Boost"] = (int)KeyCode.Keypad2;
            Assert.That(_bindings.P1Dash, Is.EqualTo(KeyCode.K));
            Assert.That(_bindings.P2Dash, Is.EqualTo(KeyCode.Keypad2));
            Assert.That(_store.WrittenKeys, Is.Empty);
        }

        [Test]
        public void ExplicitRebindAllowsRetiredShootKeysAndOnlyWritesNewDashPreferences()
        {
            _store.Values["PocketBump.P1Shoot"] = (int)KeyCode.J;
            _store.Values["PocketBump.P1Boost"] = (int)KeyCode.K;
            _store.Values["PocketBump.P2Shoot"] = (int)KeyCode.Alpha1;
            _store.Values["PocketBump.P2Boost"] = (int)KeyCode.Alpha2;
            Assert.That(_bindings.TrySet(0, KeyCode.J), Is.True);
            Assert.That(_bindings.TrySet(1, KeyCode.Alpha1), Is.True);
            CollectionAssert.AreEquivalent(new[] { DashControlBindings.P1PreferenceKey, DashControlBindings.P2PreferenceKey, DashControlBindings.MinimapPreferenceKey },
                new HashSet<string>(_store.WrittenKeys));
            Assert.That(_store.Values["PocketBump.P1Shoot"], Is.EqualTo((int)KeyCode.J));
            Assert.That(_store.Values["PocketBump.P1Boost"], Is.EqualTo((int)KeyCode.K));
            Assert.That(_store.Values["PocketBump.P2Shoot"], Is.EqualTo((int)KeyCode.Alpha1));
            Assert.That(_store.Values["PocketBump.P2Boost"], Is.EqualTo((int)KeyCode.Alpha2));
            var reloaded = new DashControlBindings(_store);
            Assert.That(reloaded.P1Dash, Is.EqualTo(KeyCode.J));
            Assert.That(reloaded.P2Dash, Is.EqualTo(KeyCode.Alpha1));
            Assert.That(_store.SaveCount, Is.EqualTo(2));
        }

        [TestCase(KeyCode.W)]
        [TestCase(KeyCode.A)]
        [TestCase(KeyCode.S)]
        [TestCase(KeyCode.D)]
        [TestCase(KeyCode.UpArrow)]
        [TestCase(KeyCode.DownArrow)]
        [TestCase(KeyCode.LeftArrow)]
        [TestCase(KeyCode.RightArrow)]
        [TestCase(KeyCode.Escape)]
        [TestCase(KeyCode.None)]
        [TestCase(KeyCode.Mouse0)]
        [TestCase(KeyCode.Mouse6)]
        public void RejectedBindingDoesNotMutateOrPersistTheTable(KeyCode key)
        {
            Assert.That(_bindings.TrySet(0, key), Is.False);
            Assert.That(_bindings.P1Dash, Is.EqualTo(KeyCode.K));
            Assert.That(_bindings.P2Dash, Is.EqualTo(KeyCode.Keypad2));
            Assert.That(_store.WrittenKeys, Is.Empty);
            Assert.That(_store.SaveCount, Is.Zero);
        }

        [Test]
        public void DashConflictAndUndefinedKeyAreRejected()
        {
            Assert.That(_bindings.TrySet(1, KeyCode.K), Is.False);
            Assert.That(_bindings.TrySet(0, (KeyCode)99999), Is.False);
            Assert.That(_store.WrittenKeys, Is.Empty);
        }

        [Test]
        public void ResetAndReloadHaveExplicitPersistenceBoundaries()
        {
            _store.Values["PocketBump.P1Boost"] = (int)KeyCode.Q;
            _store.Values[DashControlBindings.P2PreferenceKey] = (int)KeyCode.Alpha2;
            Assert.That(_bindings.P1Dash, Is.EqualTo(KeyCode.Q));
            Assert.That(_bindings.P2Dash, Is.EqualTo(KeyCode.Alpha2));
            _bindings.ResetToDefaults(false);
            Assert.That(_bindings.P1Dash, Is.EqualTo(KeyCode.K));
            Assert.That(_bindings.P2Dash, Is.EqualTo(KeyCode.Keypad2));
            Assert.That(_store.WrittenKeys, Is.Empty);
            _bindings.Reload();
            Assert.That(_bindings.P1Dash, Is.EqualTo(KeyCode.Q));
            Assert.That(_bindings.P2Dash, Is.EqualTo(KeyCode.Alpha2));
            _bindings.ResetToDefaults(true);
            Assert.That(_store.SaveCount, Is.EqualTo(1));
            Assert.That(_store.Values["PocketBump.P1Boost"], Is.EqualTo((int)KeyCode.Q));
            Assert.That(_store.Values[DashControlBindings.P2PreferenceKey], Is.EqualTo((int)KeyCode.Keypad2));
            var reloaded = new DashControlBindings(_store);
            Assert.That(reloaded.P1Dash, Is.EqualTo(KeyCode.K));
            Assert.That(reloaded.P2Dash, Is.EqualTo(KeyCode.Keypad2));
        }

        [Test]
        public void InvalidSeatCannotSilentlyOverwritePlayerTwo()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _bindings.Dash(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _bindings.TrySet(2, KeyCode.J));
            Assert.That(_store.WrittenKeys, Is.Empty);
        }

        [Test]
        public void MapBindingPersistsAndOnlyItsCurrentKeyIsReservedFromDash()
        {
            Assert.That(_bindings.TrySet(0, KeyCode.M), Is.False);
            Assert.That(_bindings.TrySetMinimapToggle(KeyCode.Tab), Is.True);
            Assert.That(_bindings.TrySet(0, KeyCode.M), Is.True);
            Assert.That(_bindings.TrySet(1, KeyCode.Tab), Is.False);
            var reloaded = new DashControlBindings(_store);
            Assert.That(reloaded.MinimapToggle, Is.EqualTo(KeyCode.Tab));
            Assert.That(reloaded.P1Dash, Is.EqualTo(KeyCode.M));
            Assert.That(reloaded.P2Dash, Is.EqualTo(KeyCode.Keypad2));
            Assert.That(reloaded.PregameBanner(), Does.Contain("MAP Tab"));
            reloaded.ResetToDefaults(true);
            Assert.That(new DashControlBindings(_store).MinimapToggle, Is.EqualTo(KeyCode.M));
        }

        [TestCase(KeyCode.K)] [TestCase(KeyCode.Keypad2)] [TestCase(KeyCode.W)]
        [TestCase(KeyCode.UpArrow)] [TestCase(KeyCode.Escape)] [TestCase(KeyCode.None)]
        [TestCase(KeyCode.Mouse0)] [TestCase((KeyCode)99999)]
        public void MapRejectsMovementReservedOrDashKeysWithoutSaving(KeyCode key)
        {
            Assert.That(_bindings.TrySetMinimapToggle(key), Is.False);
            Assert.That(_bindings.MinimapToggle, Is.EqualTo(KeyCode.M));
            Assert.That(_store.WrittenKeys, Is.Empty);
            Assert.That(_store.SaveCount, Is.Zero);
        }

        [TestCase(0, KeyCode.Q, KeyCode.K)]
        [TestCase(1, KeyCode.Q, KeyCode.Keypad2)]
        [TestCase(0, KeyCode.K, KeyCode.Keypad2)]
        [TestCase(1, KeyCode.Keypad2, KeyCode.K)]
        public void OldDashOnMapKeyRepairsOnlyThatSeatWithoutWriting(int seat, KeyCode other, KeyCode repaired)
        {
            _store.Values[seat == 0 ? DashControlBindings.P1PreferenceKey : DashControlBindings.P2PreferenceKey] = (int)KeyCode.M;
            _store.Values[seat == 0 ? DashControlBindings.P2PreferenceKey : DashControlBindings.P1PreferenceKey] = (int)other;
            Assert.That(_bindings.MinimapToggle, Is.EqualTo(KeyCode.M));
            Assert.That(_bindings.Dash(seat), Is.EqualTo(repaired));
            Assert.That(_bindings.Dash(1 - seat), Is.EqualTo(other));
            Assert.That(_store.WrittenKeys, Is.Empty);
            Assert.That(_store.SaveCount, Is.Zero);
        }

        [Test]
        public void InvalidStoredMapFallsBackWithoutResettingUnrelatedDashOrWriting()
        {
            _store.Values[DashControlBindings.MinimapPreferenceKey] = (int)KeyCode.Escape;
            _store.Values[DashControlBindings.P1PreferenceKey] = (int)KeyCode.Q;
            _store.Values[DashControlBindings.P2PreferenceKey] = (int)KeyCode.M;
            Assert.That(_bindings.MinimapToggle, Is.EqualTo(KeyCode.M));
            Assert.That(_bindings.P1Dash, Is.EqualTo(KeyCode.Q));
            Assert.That(_bindings.P2Dash, Is.EqualTo(KeyCode.Keypad2));
            Assert.That(_store.Values[DashControlBindings.MinimapPreferenceKey], Is.EqualTo((int)KeyCode.Escape));
            Assert.That(_store.WrittenKeys, Is.Empty);
        }

        [Test]
        public void StoredMapOnDefaultDashHasAFreeFallbackAndNoDuplicateActions()
        {
            _store.Values[DashControlBindings.MinimapPreferenceKey] = (int)KeyCode.K;
            Assert.That(_bindings.MinimapToggle, Is.EqualTo(KeyCode.K));
            Assert.That(_bindings.P1Dash, Is.EqualTo(KeyCode.LeftShift));
            Assert.That(_bindings.P2Dash, Is.EqualTo(KeyCode.Keypad2));
            Assert.That(_store.WrittenKeys, Is.Empty);
        }
    }

    internal sealed class MemoryDashBindingStore : IDashBindingStore
    {
        public readonly Dictionary<string, int> Values = new Dictionary<string, int>();
        public readonly List<string> ReadKeys = new List<string>();
        public readonly List<string> WrittenKeys = new List<string>();
        public int SaveCount;
        public bool HasKey(string key) { ReadKeys.Add(key); return Values.ContainsKey(key); }
        public int GetInt(string key, int fallback) { ReadKeys.Add(key); return Values.TryGetValue(key, out int value) ? value : fallback; }
        public void SetInt(string key, int value) { WrittenKeys.Add(key); Values[key] = value; }
        public void Save() { SaveCount++; }
    }
}
