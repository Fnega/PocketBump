using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    // Exercise moving, live matches rather than only the opening handshake/countdown.
    public sealed class PocketBumpConfirmedHistoryTests
    {
        const int MatchId = 18421;
        const int RunTicks = 1600;
        const int FlushTicks = 32;

        sealed class Delivery
        {
            public int Due;
            public int Receiver;
            public byte[] Bytes;
        }

        static MatchSimState LiveInitial()
        {
            var state = MatchSim.Create(new MatchSettings { pointsToWin = 21, marbleCount = 3 });
            MatchSim.StartMatch(state);
            for (int i = 0; i < MatchSim.KickoffTicks; i++)
                MatchSim.Step(state, PlayerInput.Neutral, PlayerInput.Neutral);
            Assert.That(state.Phase, Is.EqualTo(MatchState.Live));
            state.Tick = 0;
            return state;
        }

        static PlayerInput Tape(int seat, int sampleTick)
        {
            if (sampleTick < 0 || sampleTick >= RunTicks - FlushTicks)
                return PlayerInput.Neutral;

            // All nine keyboard directions, held boost and isolated shoot edges survive
            // precisely the same quantization as real local/remote network input.
            byte[] directions = { 1, 5, 4, 6, 2, 10, 8, 9, 0 };
            byte bits = directions[(sampleTick / 7 + seat * 4) % directions.Length];
            if ((sampleTick / 19 + seat) % 3 == 0) bits |= 16;
            if ((sampleTick + seat * 11) % 37 == 0) bits |= 32;
            PlayerInput input = PlayerInput.Unpack(bits);
            return PlayerInput.Unpack(input.Pack());
        }

        static byte[] EncodeFrame(RollbackSession session, int sampleTick)
        {
            int newest = sampleTick + RollbackSession.DelayTicks;
            var bits = new byte[NetplayProtocol.InputHistoryLength];
            int count = session.CopyLocalHistory(newest, bits);
            Assert.That(count, Is.GreaterThan(0));
            session.TryGetConfirmedChecksum(session.State.Tick - 2, out int checksumTick, out int checksum);
            return LanUdpPeer.EncodeInput(new NetInputPacket
            {
                MatchId = MatchId,
                Seat = (byte)session.LocalSeat,
                NewestTick = newest,
                InputBits = bits,
                HistoryCount = (byte)count,
                AckTick = session.ConfirmedTick,
                ChecksumTick = checksumTick,
                Checksum = checksum
            });
        }

        [Test]
        public void TwoSeatsConvergeDuringLiveMovementWithLossDelayReorderingAndDuplicates()
        {
            MatchSimState initial = LiveInitial();
            MatchSimState baseline = initial.Clone();
            var sessions = new[]
            {
                new RollbackSession(initial.Clone(), 0),
                new RollbackSession(initial.Clone(), 1)
            };
            var pending = new List<Delivery>();
            var baselineHashes = new int[RunTicks];
            var highestReceived = new[] { -1, -1 };
            var rollbackCounts = new int[2];
            int liveTicks = 0, movingTicks = 0, dropped = 0, duplicates = 0, reordered = 0;
            int comparedHistories = 0, deliveredChecksums = 0;

            for (int frame = 0; frame < RunTicks; frame++)
            {
                // Deliver in reverse insertion order, with independently varying delays
                // in both directions. No sleep, wall clock or random seed can hide a failure.
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    Delivery delivery = pending[i];
                    if (delivery.Due > frame) continue;
                    pending.RemoveAt(i);
                    Assert.That(LanUdpPeer.TryDecodeInput(delivery.Bytes, out NetInputPacket packet), Is.True);
                    Assert.That(packet.MatchId, Is.EqualTo(MatchId));
                    Assert.That(packet.Seat, Is.EqualTo(1 - delivery.Receiver));
                    if (packet.NewestTick < highestReceived[delivery.Receiver]) reordered++;
                    highestReceived[delivery.Receiver] = Mathf.Max(highestReceived[delivery.Receiver], packet.NewestTick);
                    RollbackSession receiver = sessions[delivery.Receiver];
                    int accepted = receiver.ReceiveRemoteHistory(packet.NewestTick, packet.InputBits, packet.HistoryCount);
                    if (accepted > 0 && packet.ChecksumTick >= 0)
                    {
                        Assert.That(packet.ChecksumTick, Is.LessThanOrEqualTo(packet.AckTick));
                        receiver.QueueRemoteChecksum(packet.ChecksumTick, packet.Checksum);
                        deliveredChecksums++;
                    }
                }

                if (baseline.Phase == MatchState.Live) liveTicks++;
                Vector2 previous0 = baseline.Players[0].Position;
                Vector2 previous1 = baseline.Players[1].Position;
                MatchSim.Step(baseline,
                    Tape(0, frame - RollbackSession.DelayTicks),
                    Tape(1, frame - RollbackSession.DelayTicks));
                baselineHashes[frame] = baseline.Checksum();
                if (baseline.Players[0].Position != previous0 || baseline.Players[1].Position != previous1)
                    movingTicks++;
                Assert.That(baseline.Tick, Is.EqualTo(frame + 1), "Baseline ended before the full input tape was exercised.");

                for (int seat = 0; seat < sessions.Length; seat++)
                {
                    RollbackSession session = sessions[seat];
                    session.Advance(Tape(seat, frame));
                    Assert.That(session.Status, Is.EqualTo(RollbackStatus.Ok), "seat=" + seat + ", frame=" + frame);
                    if (session.LastRollbackTick >= 0) rollbackCounts[seat]++;
                    Assert.That(session.TryGetConfirmedChecksum(frame, out int confirmed, out int checksum), Is.True);
                    Assert.That(checksum, Is.EqualTo(baselineHashes[confirmed]),
                        "Confirmed history differs: seat=" + seat + ", frame=" + frame + ", confirmed=" + confirmed);
                    comparedHistories++;

                    bool flushing = frame >= RunTicks - FlushTicks;
                    if (!flushing && (frame + seat * 3) % 17 < 2)
                    {
                        dropped++;
                        continue;
                    }

                    byte[] bytes = EncodeFrame(session, frame);
                    int delay = flushing ? 1 : 1 + (frame * 7 + seat * 3) % 6;
                    pending.Add(new Delivery { Due = frame + delay, Receiver = 1 - seat, Bytes = bytes });
                    if (!flushing && (frame + seat) % 13 == 0)
                    {
                        pending.Add(new Delivery { Due = frame + delay + 1, Receiver = 1 - seat, Bytes = bytes });
                        duplicates++;
                    }
                }
            }

            Assert.That(liveTicks, Is.GreaterThan(500));
            Assert.That(movingTicks, Is.GreaterThan(500));
            Assert.That(rollbackCounts[0], Is.GreaterThan(20));
            Assert.That(rollbackCounts[1], Is.GreaterThan(20));
            Assert.That(dropped, Is.GreaterThan(100));
            Assert.That(duplicates, Is.GreaterThan(100));
            Assert.That(reordered, Is.GreaterThan(100));
            Assert.That(deliveredChecksums, Is.GreaterThan(1000));
            Assert.That(comparedHistories, Is.EqualTo(RunTicks * 2));
            foreach (RollbackSession session in sessions)
            {
                Assert.That(session.ConfirmedTick, Is.GreaterThanOrEqualTo(RunTicks - 1));
                Assert.That(session.ValidatedChecksumCount, Is.GreaterThan(0), "The stress test must actually validate peer checksums.");
                AssertStatesEqual(baseline, session.State);
            }
            TestContext.WriteLine("Live ticks: " + liveTicks + ", moving ticks: " + movingTicks +
                ", host/join rollbacks: " + rollbackCounts[0] + "/" + rollbackCounts[1] +
                ", dropped/duplicated/reordered: " + dropped + "/" + duplicates + "/" + reordered);
        }

        [Test]
        public void SuccessiveRollbacksRestoreThePreviouslyCorrectedSnapshot()
        {
            MatchSimState initial = LiveInitial();
            var baseline = new RollbackSession(initial.Clone(), 0);
            var delayed = new RollbackSession(initial.Clone(), 0);
            for (int tick = 2; tick <= 12; tick++)
            {
                PlayerInput input = tick == 2 ? PlayerInput.Unpack(17) :
                    tick == 5 ? PlayerInput.Unpack(36) : PlayerInput.Neutral;
                baseline.ReceiveRemote(tick, input);
                if (tick != 2 && tick != 5) delayed.ReceiveRemote(tick, input);
            }
            for (int i = 0; i < 7; i++)
            {
                baseline.Advance(PlayerInput.Neutral);
                delayed.Advance(PlayerInput.Neutral);
            }

            delayed.ReceiveRemote(2, PlayerInput.Unpack(17));
            baseline.Advance(PlayerInput.Neutral);
            delayed.Advance(PlayerInput.Neutral);
            Assert.That(delayed.LastRollbackTick, Is.EqualTo(2));
            Assert.That(delayed.ChecksumAt(4), Is.EqualTo(baseline.ChecksumAt(4)));

            delayed.ReceiveRemote(5, PlayerInput.Unpack(36));
            baseline.Advance(PlayerInput.Neutral);
            delayed.Advance(PlayerInput.Neutral);
            Assert.That(delayed.LastRollbackTick, Is.EqualTo(5));
            Assert.That(delayed.Status, Is.EqualTo(RollbackStatus.Ok));
            AssertStatesEqual(baseline.State, delayed.State);
        }

        [Test]
        public void InputArrivingAtTheExactRollbackWindowBoundaryIsReplayed()
        {
            MatchSimState initial = LiveInitial();
            var baseline = new RollbackSession(initial.Clone(), 0);
            var delayed = new RollbackSession(initial.Clone(), 0);
            const int lateTick = 100;
            int nextTick = lateTick + RollbackSession.BufferTicks;
            for (int frame = 0; frame < nextTick; frame++)
            {
                if (frame >= RollbackSession.DelayTicks)
                {
                    PlayerInput input = frame == lateTick ? PlayerInput.Unpack(17) : PlayerInput.Neutral;
                    baseline.ReceiveRemote(frame, input);
                    if (frame != lateTick) delayed.ReceiveRemote(frame, input);
                }
                baseline.Advance(PlayerInput.Neutral);
                delayed.Advance(PlayerInput.Neutral);
            }

            Assert.That(delayed.State.Checksum(), Is.Not.EqualTo(baseline.State.Checksum()));
            Assert.That(delayed.TryReceiveRemote(lateTick, PlayerInput.Unpack(17)), Is.True);
            Assert.That(delayed.QueueRemoteChecksum(lateTick, baseline.ChecksumAt(lateTick)), Is.True);
            baseline.Advance(PlayerInput.Neutral);
            delayed.Advance(PlayerInput.Neutral);
            Assert.That(delayed.LastRollbackTick, Is.EqualTo(lateTick));
            Assert.That(delayed.Status, Is.EqualTo(RollbackStatus.Ok));
            AssertStatesEqual(baseline.State, delayed.State);
        }

        [Test]
        public void OldestAcceptedChecksumIsValidatedBeforeHistoryEviction()
        {
            var session = new RollbackSession(LiveInitial(), 0);
            int nextTick = 2 + RollbackSession.BufferTicks;
            for (int tick = 2; tick <= nextTick; tick++) session.ReceiveRemote(tick, PlayerInput.Neutral);
            for (int frame = 0; frame < nextTick; frame++) session.Advance(PlayerInput.Neutral);
            Assert.That(session.QueueRemoteChecksum(2, session.ChecksumAt(2) ^ 1), Is.True);
            session.Advance(PlayerInput.Neutral);
            Assert.That(session.Status, Is.EqualTo(RollbackStatus.Desync));
        }

        [Test]
        public void FutureChecksumCannotOverwritePendingEarlierMismatch()
        {
            var session = new RollbackSession(LiveInitial(), 0);
            session.ReceiveRemote(2, PlayerInput.Neutral);
            for (int i = 0; i < 3; i++) session.Advance(PlayerInput.Neutral);
            Assert.That(session.QueueRemoteChecksum(2, session.ChecksumAt(2) ^ 1), Is.True);
            Assert.That(session.QueueRemoteChecksum(2 + RollbackSession.BufferTicks, 123456), Is.True);
            session.Advance(PlayerInput.Neutral);
            Assert.That(session.Status, Is.EqualTo(RollbackStatus.Desync));
        }

        [Test]
        public void ReceivingLateInputDoesNotAdvertiseUnreplayedPredictedChecksum()
        {
            var session = new RollbackSession(LiveInitial(), 0);
            for (int i = 0; i < 3; i++) session.Advance(PlayerInput.Neutral);
            Assert.That(session.TryReceiveRemote(2, PlayerInput.Unpack(17)), Is.True);
            Assert.That(session.TryGetConfirmedChecksum(2, out int beforeTick, out _), Is.True);
            Assert.That(beforeTick, Is.LessThan(2), "Receiving input alone must not confirm stale simulated history.");
            session.Advance(PlayerInput.Neutral);
            Assert.That(session.TryGetConfirmedChecksum(2, out int afterTick, out _), Is.True);
            Assert.That(afterTick, Is.EqualTo(2));
        }

        static void AssertStatesEqual(MatchSimState expected, MatchSimState actual)
        {
            Assert.That(actual.Checksum(), Is.EqualTo(expected.Checksum()));
            // The wire hash intentionally covers selected fields; compare the complete
            // simulation snapshot as well so un-hashed cooldowns/locks cannot escape.
            Assert.That(JsonUtility.ToJson(actual), Is.EqualTo(JsonUtility.ToJson(expected)));
        }
    }
}
