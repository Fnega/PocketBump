using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class PocketBumpDashTickInputSamplerTests
    {
        DashControlBindings _bindings;
        FakeKeyboard _keyboard;
        DashTickInputSampler _sampler;

        [SetUp]
        public void Setup()
        {
            _bindings = new DashControlBindings(new MemoryDashBindingStore());
            _keyboard = new FakeKeyboard();
            _sampler = new DashTickInputSampler(_bindings, _keyboard);
        }

        [Test]
        public void ShortPressBetweenTicksSurvivesReleaseAndIsConsumedOnce()
        {
            _keyboard.NextFrame(KeyCode.K);
            _sampler.LatchUpdate(false, 0);
            _keyboard.NextFrame();
            _sampler.LatchUpdate(false, 0);
            Assert.That(_sampler.SampleLocal(0).DashPressed, Is.True);
            Assert.That(_sampler.SampleLocal(0).DashPressed, Is.False);
            Assert.That(_sampler.SampleLocal(1).DashPressed, Is.False);
        }

        [Test]
        public void RepeatedCaptureInOneFrameCannotRecreateAnAlreadyConsumedEdge()
        {
            _keyboard.NextFrame(KeyCode.K);
            _sampler.LatchUpdate(false, 0);
            Assert.That(_sampler.SampleLocal(0).DashPressed, Is.True);
            _sampler.LatchUpdate(false, 0);
            Assert.That(_sampler.SampleLocal(0).DashPressed, Is.False);
        }

        [Test]
        public void HeldDashDoesNotRepeatAcrossDashAndCooldownLengths()
        {
            int edges = 0;
            for (int i = 0; i < 500; i++)
            {
                _keyboard.NextFrame(KeyCode.K);
                _sampler.LatchUpdate(false, 0);
                if (_sampler.SampleLocal(0).DashPressed) edges++;
            }
            Assert.That(edges, Is.EqualTo(1));
            _keyboard.NextFrame();
            _sampler.LatchUpdate(false, 0);
            _keyboard.NextFrame(KeyCode.K);
            _sampler.LatchUpdate(false, 0);
            Assert.That(_sampler.SampleLocal(0).DashPressed, Is.True);
        }

        [Test]
        public void MultipleUnsampledPressesAreOneEdgeRatherThanAFutureQueue()
        {
            _keyboard.NextFrame(KeyCode.K);
            _sampler.LatchUpdate(false, 0);
            _keyboard.NextFrame();
            _sampler.LatchUpdate(false, 0);
            _keyboard.NextFrame(KeyCode.K);
            _sampler.LatchUpdate(false, 0);
            Assert.That(_sampler.SampleLocal(0).DashPressed, Is.True);
            Assert.That(_sampler.SampleLocal(0).DashPressed, Is.False);
        }

        [Test]
        public void LocalDashActionsAndMovementStayIndependent()
        {
            _keyboard.NextFrame(KeyCode.W, KeyCode.D, KeyCode.DownArrow, KeyCode.Keypad2);
            _sampler.LatchUpdate(false, 0);
            DashArenaInput one = _sampler.SampleLocal(0), two = _sampler.SampleLocal(1);
            Assert.That(one.Move, Is.EqualTo(new Vector2(1f, 1f).normalized));
            Assert.That(one.DashPressed, Is.False);
            Assert.That(two.Move, Is.EqualTo(Vector2.down));
            Assert.That(two.DashPressed, Is.True);
            Assert.That(_sampler.SampleLocal(1).DashPressed, Is.False);
        }

        [Test]
        public void DefaultP2DashUsesKeypadTwoNotNumberRowAndConsumesItsEdgeOnce()
        {
            _keyboard.NextFrame(KeyCode.Alpha2);
            _sampler.LatchUpdate(false, 0);
            Assert.That(_sampler.SampleLocal(1).DashPressed, Is.False);
            _keyboard.NextFrame(KeyCode.Keypad2);
            _sampler.LatchUpdate(false, 0);
            Assert.That(_sampler.SampleLocal(0).DashPressed, Is.False);
            Assert.That(_sampler.SampleLocal(1).DashPressed, Is.True);
            Assert.That(_sampler.SampleLocal(1).DashPressed, Is.False);
            _keyboard.NextFrame(KeyCode.Keypad2);
            _sampler.LatchUpdate(false, 0);
            Assert.That(_sampler.SampleLocal(1).DashPressed, Is.False, "Holding Num2 must not trigger another Dash.");
        }

        [Test]
        public void ExplicitP2NumberRowBindingIsSampledInsteadOfKeypadDefault()
        {
            Assert.That(_bindings.TrySet(1, KeyCode.Alpha2), Is.True);
            _keyboard.NextFrame(KeyCode.Keypad2);
            _sampler.LatchUpdate(false, 0);
            Assert.That(_sampler.SampleLocal(1).DashPressed, Is.False);
            _keyboard.NextFrame(KeyCode.Alpha2);
            _sampler.LatchUpdate(false, 0);
            Assert.That(_sampler.SampleLocal(1).DashPressed, Is.True);
            Assert.That(_sampler.SampleLocal(1).DashPressed, Is.False);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void LanUsesOnlyTheLocalP1BindingsForEitherAssignedSeat(int seat)
        {
            Assert.That(_bindings.TrySet(0, KeyCode.Q), Is.True);
            _keyboard.NextFrame(KeyCode.W, KeyCode.Q, KeyCode.DownArrow, KeyCode.Keypad2);
            _sampler.LatchUpdate(true, seat);
            DashArenaInput input = _sampler.SampleNetplaySeat(seat);
            Assert.That(input.Move, Is.EqualTo(Vector2.up));
            Assert.That(input.DashPressed, Is.True);
            Assert.That(_sampler.SampleNetplaySeat(seat).DashPressed, Is.False);
            Assert.Throws<InvalidOperationException>(() => _sampler.SampleNetplaySeat(1 - seat));
            Assert.Throws<InvalidOperationException>(() => _sampler.SampleLocal(1 - seat));
        }

        [TestCase(0)]
        [TestCase(1)]
        public void LanIgnoresLocalP2ActionsAndRetiredShootKeys(int seat)
        {
            _keyboard.NextFrame(KeyCode.DownArrow, KeyCode.Keypad2, KeyCode.Alpha2, KeyCode.J, KeyCode.Alpha1);
            _sampler.LatchUpdate(true, seat);
            DashArenaInput input = _sampler.SampleNetplaySeat(seat);
            Assert.That(input.Move, Is.EqualTo(Vector2.zero));
            Assert.That(input.DashPressed, Is.False);
        }

        [Test]
        public void ClearDropsBothEdgesAndSuppressesSameFrameRelatchingForRematch()
        {
            _keyboard.NextFrame(KeyCode.K, KeyCode.Keypad2);
            _sampler.LatchUpdate(false, 0);
            _sampler.Clear();
            _sampler.LatchUpdate(false, 0);
            Assert.That(_sampler.SampleLocal(0).DashPressed, Is.False);
            Assert.That(_sampler.SampleLocal(1).DashPressed, Is.False);
            _keyboard.NextFrame(KeyCode.K, KeyCode.Keypad2);
            _sampler.LatchUpdate(false, 0);
            Assert.That(_sampler.SampleLocal(0).DashPressed, Is.False);
            Assert.That(_sampler.SampleLocal(1).DashPressed, Is.False);
            _keyboard.NextFrame();
            _sampler.LatchUpdate(false, 0);
            _keyboard.NextFrame(KeyCode.K);
            _sampler.LatchUpdate(false, 0);
            Assert.That(_sampler.SampleLocal(0).DashPressed, Is.True);
        }

        [Test]
        public void ChangingModeOrAssignedSeatCannotCarryAnOldEdge()
        {
            _keyboard.NextFrame(KeyCode.K, KeyCode.Keypad2);
            _sampler.LatchUpdate(false, 0);
            _keyboard.NextFrame();
            _sampler.LatchUpdate(true, 1);
            Assert.That(_sampler.SampleNetplaySeat(1).DashPressed, Is.False);
            _keyboard.NextFrame(KeyCode.K);
            _sampler.LatchUpdate(true, 1);
            _keyboard.NextFrame();
            _sampler.LatchUpdate(true, 0);
            Assert.That(_sampler.SampleNetplaySeat(0).DashPressed, Is.False);
        }

        [Test]
        public void OppositeMovementKeysCancelAndReleaseHasNoSyntheticDash()
        {
            _keyboard.NextFrame(KeyCode.W, KeyCode.S, KeyCode.D, KeyCode.A);
            _sampler.LatchUpdate(false, 0);
            Assert.That(_sampler.SampleLocal(0).Move, Is.EqualTo(Vector2.zero));
            _keyboard.NextFrame();
            _sampler.LatchUpdate(false, 0);
            DashArenaInput input = _sampler.SampleLocal(0);
            Assert.That(input.Move, Is.EqualTo(Vector2.zero));
            Assert.That(input.DashPressed, Is.False);
        }

        [Test]
        public void SamplingConsumesAnUnavailablePressRatherThanSavingItForRecovery()
        {
            var state = DashArenaSimulation.Create(ArenaRulesProfile.CreateGreybox());
            DashArenaSimulation.StartMatch(state);
            for (int i = 0; i < state.Rules.KickoffTicks; i++) DashArenaSimulation.Step(state, default, default);
            state.Players[0].DashCooldownTicks = 2;
            _keyboard.NextFrame(KeyCode.K);
            _sampler.LatchUpdate(false, 0);
            DashArenaSimulation.Step(state, _sampler.SampleLocal(0), default);
            Assert.That(state.Players[0].DashTicks, Is.Zero);
            for (int tick = 0; tick < 4; tick++)
            {
                _keyboard.NextFrame(KeyCode.K);
                _sampler.LatchUpdate(false, 0);
                DashArenaSimulation.Step(state, _sampler.SampleLocal(0), default);
            }
            Assert.That(state.Players[0].DashCooldownTicks, Is.Zero);
            Assert.That(state.Players[0].DashTicks, Is.Zero);
        }

        [Test]
        public void InvalidSeatFailsBeforeAccessingAnInputArray()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _sampler.LatchUpdate(true, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => _sampler.SampleLocal(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _sampler.SampleNetplaySeat(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => _sampler.ReadMove(-1));
        }

        sealed class FakeKeyboard : IDashKeyboardInput
        {
            HashSet<KeyCode> _held = new HashSet<KeyCode>();
            readonly HashSet<KeyCode> _down = new HashSet<KeyCode>();
            public int FrameCount { get; private set; }
            public bool IsHeld(KeyCode key) => _held.Contains(key);
            public bool WasPressedThisFrame(KeyCode key) => _down.Contains(key);
            public void NextFrame(params KeyCode[] held)
            {
                FrameCount++;
                var next = new HashSet<KeyCode>(held);
                _down.Clear();
                foreach (KeyCode key in next) if (!_held.Contains(key)) _down.Add(key);
                _held = next;
            }
        }
    }
}
