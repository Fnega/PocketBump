using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class DashSoundFeedbackTests
    {
        [TestCase(DashSoundCue.Dash)]
        [TestCase(DashSoundCue.Impact)]
        [TestCase(DashSoundCue.WallBreak)]
        [TestCase(DashSoundCue.GreenPickup)]
        [TestCase(DashSoundCue.BluePickup)]
        [TestCase(DashSoundCue.PurplePickup)]
        [TestCase(DashSoundCue.Goal)]
        [TestCase(DashSoundCue.Respawn)]
        public void OriginalSamplesAreRepeatableFiniteBoundedAndDoNotUseUnityRandom(DashSoundCue cue)
        {
            UnityEngine.Random.State savedRandom = UnityEngine.Random.state;
            float[] first = DashSoundSynthesis.Generate(cue);
            float[] second = DashSoundSynthesis.Generate(cue);
            Assert.That(UnityEngine.Random.state, Is.EqualTo(savedRandom));
            CollectionAssert.AreEqual(first, second);
            Assert.That(first.Length, Is.GreaterThan(DashSoundSynthesis.SampleRate / 10));
            Assert.That(first.Length, Is.LessThan(DashSoundSynthesis.SampleRate));
            double energy = 0;
            foreach (float sample in first)
            {
                Assert.That(float.IsNaN(sample) || float.IsInfinity(sample), Is.False);
                Assert.That(Math.Abs(sample), Is.LessThanOrEqualTo(.70001f));
                energy += sample * sample;
            }
            Assert.That(energy / first.Length, Is.GreaterThan(.0001), "The clip must contain audible energy, not just silence.");
            Assert.That(first[0], Is.Zero);
            Assert.That(first[first.Length - 1], Is.Zero);
        }

        [Test]
        public void CorrectedEventPayloadAndOlderTicksCannotReplayASound()
        {
            var gate = new DashSoundEventGate(); gate.ResetForEpoch(4);
            Assert.That(gate.TryAccept(Event(8, 4, DashArenaEventKind.Goal), out var cue), Is.True);
            Assert.That(cue, Is.EqualTo(DashSoundCue.Goal));
            var movedPayload = new DashArenaEvent(8, 4, 0, 0, DashArenaEventKind.Goal, new Vector2(3, 5));
            Assert.That(gate.TryAccept(movedPayload, out _), Is.False);
            Assert.That(gate.TryAccept(Event(9, 4, DashArenaEventKind.DashStarted), out _), Is.True);
            Assert.That(gate.TryAccept(Event(8, 4, DashArenaEventKind.Goal), out _), Is.False);
            Assert.That(gate.TryAccept(Event(10, 3, DashArenaEventKind.Goal), out _), Is.False);
            Assert.That(gate.TryAccept(Event(10, 5, DashArenaEventKind.Goal), out _), Is.False);
            gate.ResetForEpoch(5);
            Assert.That(gate.TryAccept(Event(0, 5, DashArenaEventKind.Goal), out _), Is.True);
        }

        [Test]
        public void SameTickDistinctEventsRemainDistinctAndRepeatedContactsAreLimited()
        {
            var gate = new DashSoundEventGate(); gate.ResetForEpoch(0);
            Assert.That(gate.TryAccept(Event(0, 0, DashArenaEventKind.Impact), out _), Is.True);
            Assert.That(gate.TryAccept(Event(0, 0, DashArenaEventKind.PillarHit), out _), Is.False);
            Assert.That(gate.TryAccept(Event(0, 0, DashArenaEventKind.WallBroken), out _), Is.True);
            Assert.That(gate.TryAccept(Event(0, 0, DashArenaEventKind.Goal), out _), Is.True);
            Assert.That(gate.TryAccept(Event(1, 0, DashArenaEventKind.Impact), out _), Is.False);
            Assert.That(gate.TryAccept(Event(2, 0, DashArenaEventKind.Impact), out _), Is.False);
            Assert.That(gate.TryAccept(Event(3, 0, DashArenaEventKind.Impact), out _), Is.True);
        }

        [TestCase(DashPickupKind.Green, DashSoundCue.GreenPickup)]
        [TestCase(DashPickupKind.Blue, DashSoundCue.BluePickup)]
        [TestCase(DashPickupKind.Purple, DashSoundCue.PurplePickup)]
        public void PickupSoundsCarryTheConfirmedKind(DashPickupKind pickup, DashSoundCue expected)
        {
            var gate = new DashSoundEventGate(); gate.ResetForEpoch(0);
            var item = new DashArenaEvent(0, 0, 1, 23, DashArenaEventKind.Pickup, Vector2.zero, 0, pickup);
            Assert.That(gate.TryAccept(item, out var cue), Is.True);
            Assert.That(cue, Is.EqualTo(expected));
            Assert.That(gate.TryAccept(item, out _), Is.False);
        }

        [Test]
        public void DelayedRollbackEventsBecomeAudibleOnlyAfterCorrectedConfirmation()
        {
            var state = DashArenaSimulation.Create(ArenaRulesProfile.CreateProductionV1(), 19);
            DashArenaSimulation.StartMatch(state); state.Phase = MatchState.Live; state.PhaseTicks = 0;
            state.GateClosed = false; state.Players[1].Position = new Vector2(0, -2); state.Players[1].Facing = Vector2.up;
            state.Pickups[0] = new DashPickupState { Active = true, Id = 1, Kind = DashPickupKind.Green, Position = Vector2.zero };
            state.NextPickupId = 2;
            var session = new DashRollbackSession(state, 0);
            var ledger = new DashConfirmedEventLedger();
            var gate = new DashSoundEventGate(); gate.ResetForEpoch(0);
            var cues = new List<DashSoundCue>();
            for (int tick = 0; tick < 8; tick++) session.Advance(DashArenaInput.Neutral);
            Assert.That(ledger.TryCommit(session, out var events), Is.True);
            Accept(gate, events, cues);
            CollectionAssert.DoesNotContain(cues, DashSoundCue.GreenPickup);
            for (int tick = 2; tick < 8; tick++)
                session.TryReceiveRemote(tick, new DashArenaInput { Move = Vector2.up, DashPressed = tick == 2 });
            Assert.That(ledger.TryCommit(session, out events), Is.True);
            Accept(gate, events, cues);
            CollectionAssert.DoesNotContain(cues, DashSoundCue.GreenPickup);
            session.Reconcile();
            int checksum = session.State.Checksum();
            Assert.That(ledger.TryCommit(session, out events), Is.True);
            Accept(gate, events, cues); Accept(gate, events, cues);
            Assert.That(cues.FindAll(item => item == DashSoundCue.GreenPickup).Count, Is.EqualTo(1));
            Assert.That(cues.FindAll(item => item == DashSoundCue.Dash).Count, Is.EqualTo(1));
            Assert.That(session.State.Checksum(), Is.EqualTo(checksum));
        }

        static DashArenaEvent Event(int tick, int epoch, DashArenaEventKind kind) =>
            new DashArenaEvent(tick, epoch, 0, 0, kind, Vector2.zero);

        static void Accept(DashSoundEventGate gate, IReadOnlyList<DashArenaEvent> events, List<DashSoundCue> output)
        {
            foreach (var item in events) if (gate.TryAccept(item, out var cue)) output.Add(cue);
        }
    }
}
