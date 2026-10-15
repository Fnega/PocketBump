using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class PocketBumpDashStateTests
    {
        const uint Seed = 0x12345678u;
        const BindingFlags PublicFields = BindingFlags.Instance | BindingFlags.Public;

        [Test]
        public void ConstructorRequiresProfileNonzeroSeedAndNonnegativeEpoch()
        {
            ArenaRulesProfile rules = ArenaRulesProfile.CreateGreybox();
            Assert.Throws<ArgumentNullException>(() => new DashArenaState(null, Seed));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DashArenaState(rules, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DashArenaState(rules, Seed, -1));
            var state = new DashArenaState(rules, Seed, 2);
            Assert.That(state.Tick, Is.Zero);
            Assert.That(state.Epoch, Is.EqualTo(2));
            Assert.That(state.Phase, Is.EqualTo(MatchState.Pregame));
            Assert.That(state.InitialSeed, Is.EqualTo(Seed));
            Assert.That(state.RandomState, Is.EqualTo(Seed));
            Assert.That(state.LastScorer, Is.EqualTo(-1));
            Assert.That(state.NextPickupId, Is.EqualTo(1));
            Assert.That(state.GateClosed, Is.True);
        }

        [TestCase(1)]
        [TestCase(3)]
        [TestCase(8)]
        public void ArrayCapacitiesAndFlatContactTablesFollowProfile(int ballCount)
        {
            ArenaRulesProfile rules = ArenaRulesProfile.CreateGreybox(ballCount: ballCount);
            var state = new DashArenaState(rules, Seed);
            Assert.That(state.Scores.Length, Is.EqualTo(2));
            Assert.That(state.Players.Length, Is.EqualTo(2));
            Assert.That(state.Balls.Length, Is.EqualTo(ballCount));
            Assert.That(state.Walls.Length, Is.EqualTo(rules.Walls.Count));
            Assert.That(state.Pickups.Length, Is.EqualTo(rules.PickupLimit));
            Assert.That(state.PlayerContacts.Length, Is.EqualTo(1));
            Assert.That(state.PlayerBallContacts.Length, Is.EqualTo(2 * ballCount));
            Assert.That(state.CylinderContacts.Length, Is.EqualTo(2 * rules.Pillars.Count));
            Assert.That(state.WallContacts.Length, Is.EqualTo(2 * rules.Walls.Count));
            Assert.That(state.BallWallContacts.Length, Is.EqualTo(ballCount * rules.Walls.Count));
        }

        [Test]
        public void VelocityCombinesControlAndExternalChannelsWithoutMutatingEither()
        {
            var player = new DashPlayerState
            {
                ControlVelocity = new Vector2(2, -3), ExternalVelocity = new Vector2(-7, 11)
            };
            Assert.That(player.Velocity, Is.EqualTo(new Vector2(-5, 8)));
            Assert.That(player.ControlVelocity, Is.EqualTo(new Vector2(2, -3)));
            Assert.That(player.ExternalVelocity, Is.EqualTo(new Vector2(-7, 11)));
        }

        [Test]
        public void ClonePreservesEveryFieldWithoutSharingAnyMutableArray()
        {
            DashArenaState source = PopulatedState(3);
            DashArenaState copy = source.Clone();
            AssertCompleteState(source, copy);
            Assert.That(copy.Rules, Is.SameAs(source.Rules), "Only the immutable profile may be shared.");
            foreach (PropertyInfo property in ArrayProperties())
                Assert.That(property.GetValue(copy), Is.Not.SameAs(property.GetValue(source)), property.Name);
        }

        [Test]
        public void EveryAuthoritativeScalarAndArrayElementAffectsChecksumAndIsDeepCopied()
        {
            // A deliberately populated schema fixture, not a naturally reached game state.
            // Enumerating fields catches newly added value fields forgotten by copy/hash code.
            DashArenaState baseline = PopulatedState(3);
            int expectedHash = baseline.Checksum();
            int count = 0;
            foreach (Mutation mutation in Mutations(baseline))
            {
                DashArenaState altered = baseline.Clone();
                mutation.Apply(altered);
                Assert.That(altered.Checksum(), Is.Not.EqualTo(expectedHash), mutation.Name + " absent from checksum");
                Assert.That(baseline.Checksum(), Is.EqualTo(expectedHash), mutation.Name + " aliased source");
                AssertCompleteState(altered, altered.Clone());
                var restored = new DashArenaState(baseline.Rules, 1);
                restored.CopyFrom(altered);
                AssertCompleteState(altered, restored);
                count++;
            }
            Assert.That(count, Is.GreaterThan(100), "The fixture must exercise all dynamic arrays and both seats.");
        }

        [Test]
        public void CopyFromResizesBothDirectionsAndPreservesSourceProfile()
        {
            DashArenaState source = PopulatedState(8);
            var target = new DashArenaState(ArenaRulesProfile.CreateGreybox(pointsToWin: 12, ballCount: 1), 7, 1);
            target.CopyFrom(source);
            AssertCompleteState(source, target);
            foreach (PropertyInfo property in ArrayProperties())
                Assert.That(property.GetValue(target), Is.Not.SameAs(property.GetValue(source)), property.Name);
            DashArenaState small = PopulatedState(1);
            target.CopyFrom(small);
            AssertCompleteState(small, target);
            Assert.That(target.Balls.Length, Is.EqualTo(1));
            Assert.That(target.PlayerBallContacts.Length, Is.EqualTo(2));
            Assert.That(target.BallWallContacts.Length, Is.EqualTo(small.Rules.Walls.Count));
        }

        [Test]
        public void CopyFromResizesObstaclePickupAndContactArraysAcrossLayouts()
        {
            DashArenaState full = PopulatedState(8);
            ArenaRulesProfile.Builder builder = ArenaRulesProfile.Builder.CreateGreybox(ballCount: 1);
            builder.Pillars = Array.Empty<ArenaPillarDefinition>();
            builder.Walls = Array.Empty<ArenaWallDefinition>();
            builder.PickupLimit = 1;
            var sparse = new DashArenaState(builder.Freeze(), 9);
            var destination = full.Clone();
            destination.CopyFrom(sparse);
            AssertCompleteState(sparse, destination);
            Assert.That(destination.CylinderContacts, Is.Empty);
            Assert.That(destination.WallContacts, Is.Empty);
            Assert.That(destination.BallWallContacts, Is.Empty);
            Assert.That(destination.Pickups.Length, Is.EqualTo(1));
            destination.CopyFrom(full);
            AssertCompleteState(full, destination);
            foreach (PropertyInfo property in ArrayProperties())
                Assert.That(property.GetValue(destination), Is.Not.SameAs(property.GetValue(full)), property.Name);
        }

        [Test]
        public void CopyFromSelfIsHarmlessAndNullIsRejected()
        {
            DashArenaState state = PopulatedState();
            DashArenaState before = state.Clone();
            state.CopyFrom(state);
            AssertCompleteState(before, state);
            Assert.Throws<ArgumentNullException>(() => state.CopyFrom(null));
        }

        [Test]
        public void CanonicalProfileIdentityAffectsHashButInstanceIdentityDoesNot()
        {
            var first = new DashArenaState(ArenaRulesProfile.CreateGreybox(), Seed);
            var sameContent = new DashArenaState(ArenaRulesProfile.CreateGreybox(), Seed);
            var otherRules = new DashArenaState(ArenaRulesProfile.CreateGreybox(pointsToWin: 12), Seed);
            Assert.That(first.Rules.ContentHash, Is.EqualTo(sameContent.Rules.ContentHash));
            Assert.That(first.Checksum(), Is.EqualTo(sameContent.Checksum()));
            Assert.That(first.Rules.ContentHash, Is.Not.EqualTo(otherRules.Rules.ContentHash));
            Assert.That(first.Checksum(), Is.Not.EqualTo(otherRules.Checksum()));
            first.CopyFrom(otherRules);
            AssertCompleteState(first, otherRules);
        }

        [Test]
        public void ChecksumUsesRawFloatBitsIncludingSignedZeroAndSingleBitChanges()
        {
            DashArenaState state = PopulatedState();
            state.Players[0].Position.x = 1f;
            int original = state.Checksum();
            state.Players[0].Position.x = FloatFromBits(FloatBits(1f) + 1);
            Assert.That(state.Checksum(), Is.Not.EqualTo(original), "No decimal rounding is permitted.");
            state.Players[0].Position.x = 0f;
            int positiveZero = state.Checksum();
            state.Players[0].Position.x = FloatFromBits(int.MinValue);
            Assert.That(state.Checksum(), Is.Not.EqualTo(positiveZero), "Signed zero is part of raw state.");
            AssertCompleteState(state, state.Clone());
        }

        [Test]
        public void RestoringSnapshotRestoresRandomStreamWithoutChangingInitialSeed()
        {
            DashArenaState state = PopulatedState();
            ArenaRandom.Next(ref state.RandomState);
            DashArenaState snapshot = state.Clone();
            uint[] expected = DrawRandom(state, 12);
            state.CopyFrom(snapshot);
            uint[] replayed = DrawRandom(state, 12);
            CollectionAssert.AreEqual(expected, replayed);
            Assert.That(state.InitialSeed, Is.EqualTo(snapshot.InitialSeed));
            Assert.That(state.RandomState, Is.Not.EqualTo(snapshot.RandomState));
        }

        [Test]
        public void EpochAndSeedProvenanceAreDistinctAuthoritativeState()
        {
            DashArenaState state = PopulatedState();
            DashArenaState nextEpoch = state.Clone();
            nextEpoch.Epoch++;
            Assert.That(nextEpoch.Checksum(), Is.Not.EqualTo(state.Checksum()));
            DashArenaState otherInitialSeed = state.Clone();
            otherInitialSeed.InitialSeed++;
            Assert.That(otherInitialSeed.RandomState, Is.EqualTo(state.RandomState));
            Assert.That(otherInitialSeed.Checksum(), Is.Not.EqualTo(state.Checksum()));
        }

        [Test]
        public void EventIdentityDeduplicatesReplayButSeparatesEpochTickSeatEntityKindAndOrdinal()
        {
            var original = new DashArenaEvent(40, 2, 0, 17, DashArenaEventKind.WallHit, Vector2.one, 1);
            var ledger = new HashSet<DashArenaEventIdentity> { original.Identity };
            Assert.That(ledger.Add(original.Identity), Is.False);
            DashArenaEventIdentity[] distinct =
            {
                new DashArenaEventIdentity(41, 2, 0, 17, DashArenaEventKind.WallHit, 1),
                new DashArenaEventIdentity(40, 3, 0, 17, DashArenaEventKind.WallHit, 1),
                new DashArenaEventIdentity(40, 2, 1, 17, DashArenaEventKind.WallHit, 1),
                new DashArenaEventIdentity(40, 2, 0, 18, DashArenaEventKind.WallHit, 1),
                new DashArenaEventIdentity(40, 2, 0, 17, DashArenaEventKind.WallBroken, 1),
                new DashArenaEventIdentity(40, 2, 0, 17, DashArenaEventKind.WallHit, 2)
            };
            foreach (DashArenaEventIdentity identity in distinct)
                Assert.That(ledger.Add(identity), Is.True);
            Assert.That(ledger.Count, Is.EqualTo(7));
        }

        [Test]
        public void CorrectedEventPayloadKeepsIdentityAndEventsDoNotChangeStateChecksum()
        {
            DashArenaState state = PopulatedState();
            int checksum = state.Checksum();
            var original = new DashArenaEvent(8, 0, 1, 2, DashArenaEventKind.Pickup, Vector2.zero,
                pickupKind: DashPickupKind.Green);
            var corrected = new DashArenaEvent(8, 0, 1, 2, DashArenaEventKind.Pickup, Vector2.one,
                pickupKind: DashPickupKind.Purple);
            Assert.That(corrected.Identity, Is.EqualTo(original.Identity));
            Assert.That(corrected, Is.Not.EqualTo(original));
            var withoutBuff = new DashArenaEvent(8, 0, 1, 2, DashArenaEventKind.Pickup, Vector2.zero);
            Assert.That(withoutBuff, Is.Not.EqualTo(original), "Null and Green (enum zero) are different payloads.");
            Assert.That(withoutBuff.GetHashCode(), Is.Not.EqualTo(original.GetHashCode()));
            var eventList = new List<DashArenaEvent> { original };
            eventList[0] = corrected;
            eventList.Clear();
            Assert.That(state.Checksum(), Is.EqualTo(checksum));
            foreach (FieldInfo field in typeof(DashArenaEvent).GetFields(PublicFields))
                Assert.That(field.IsInitOnly, Is.True, field.Name);
        }

        static DashArenaState PopulatedState(int ballCount = 3)
        {
            var state = new DashArenaState(ArenaRulesProfile.CreateGreybox(ballCount: ballCount), Seed, 3)
            {
                Tick = 137, Phase = MatchState.Live, PhaseTicks = 7, GateClosed = false,
                LastScorer = 1, RandomState = 0xabcdef12u, NextPickupId = 42, SpawnTicks = 337
            };
            state.Scores[0] = 3;
            state.Scores[1] = 4;
            for (int i = 0; i < state.Players.Length; i++)
                state.Players[i] = new DashPlayerState
                {
                    Position = new Vector2(1.25f + i, -2.5f - i),
                    ControlVelocity = new Vector2(3.75f + i, -4.125f),
                    ExternalVelocity = new Vector2(-5.25f, 6.5f + i),
                    Facing = new Vector2(0.6f, 0.8f), DashDirection = new Vector2(-0.8f, 0.6f),
                    Alive = i == 0, DashTicks = 7 + i, DashCooldownTicks = 19 + i,
                    StunTicks = 23 + i, RespawnTicks = 29 + i, GreenTicks = 31 + i, PurpleTicks = 37 + i
                };
            for (int i = 0; i < state.Balls.Length; i++)
                state.Balls[i] = new DashBallState
                {
                    Active = i % 2 == 0, Position = new Vector2(i + 7.25f, -i - 8.5f),
                    Velocity = new Vector2(9.75f, i + 10.5f), DangerousImpactCount = i + 11
                };
            for (int i = 0; i < state.Walls.Length; i++)
                state.Walls[i] = new DashWallState { Broken = true, RepairPending = i % 2 == 0, RepairTicks = 43 + i };
            for (int i = 0; i < state.Pickups.Length; i++)
                state.Pickups[i] = new DashPickupState
                {
                    Active = i % 2 == 0, Id = i + 71, Kind = (DashPickupKind)(i % 3),
                    Position = new Vector2(i + 13.25f, -i - 14.5f)
                };
            foreach (PropertyInfo property in ArrayProperties())
            {
                if (property.PropertyType != typeof(bool[])) continue;
                var array = (bool[])property.GetValue(state);
                for (int i = 0; i < array.Length; i++) array[i] = i % 2 == 0;
            }
            return state;
        }

        sealed class Mutation
        {
            public string Name;
            public Action<DashArenaState> Apply;
        }

        static IEnumerable<Mutation> Mutations(DashArenaState fixture)
        {
            foreach (FieldInfo field in typeof(DashArenaState).GetFields(PublicFields))
                yield return new Mutation
                {
                    Name = field.Name,
                    Apply = state => field.SetValue(state, Changed(field.GetValue(state)))
                };
            foreach (PropertyInfo property in ArrayProperties())
            {
                var array = (Array)property.GetValue(fixture);
                Type element = property.PropertyType.GetElementType();
                for (int i = 0; i < array.Length; i++)
                {
                    int index = i;
                    if (element == typeof(int) || element == typeof(bool))
                    {
                        yield return new Mutation
                        {
                            Name = property.Name + "[" + index + "]",
                            Apply = state =>
                            {
                                var values = (Array)property.GetValue(state);
                                values.SetValue(Changed(values.GetValue(index)), index);
                            }
                        };
                        continue;
                    }
                    foreach (FieldInfo field in element.GetFields(PublicFields))
                    {
                        int components = field.FieldType == typeof(Vector2) ? 2 : 1;
                        for (int componentIndex = 0; componentIndex < components; componentIndex++)
                        {
                            int component = componentIndex;
                            yield return new Mutation
                            {
                                Name = property.Name + "[" + index + "]." + field.Name + "." + component,
                                Apply = state =>
                                {
                                    var values = (Array)property.GetValue(state);
                                    object value = values.GetValue(index);
                                    object oldField = field.GetValue(value);
                                    if (field.FieldType == typeof(Vector2))
                                    {
                                        var vector = (Vector2)oldField;
                                        vector[component] = (float)Changed(vector[component]);
                                        field.SetValue(value, vector);
                                    }
                                    else field.SetValue(value, Changed(oldField));
                                    values.SetValue(value, index);
                                }
                            };
                        }
                    }
                }
            }
        }

        static object Changed(object value)
        {
            if (value is bool flag) return !flag;
            if (value is int integer) return integer + 1;
            if (value is uint unsigned) return unsigned + 1u;
            if (value is float scalar) return FloatFromBits(FloatBits(scalar) ^ 1);
            Type type = value.GetType();
            if (type.IsEnum)
                foreach (object candidate in Enum.GetValues(type))
                    if (!candidate.Equals(value)) return candidate;
            throw new InvalidOperationException("Add mutation coverage for " + type.FullName);
        }

        static IEnumerable<PropertyInfo> ArrayProperties()
        {
            foreach (PropertyInfo property in typeof(DashArenaState).GetProperties(PublicFields))
                if (property.PropertyType.IsArray) yield return property;
        }

        static void AssertCompleteState(DashArenaState expected, DashArenaState actual)
        {
            Assert.That(actual.Rules, Is.SameAs(expected.Rules));
            foreach (FieldInfo field in typeof(DashArenaState).GetFields(PublicFields))
                AssertExact(field.GetValue(expected), field.GetValue(actual), field.Name);
            foreach (PropertyInfo property in ArrayProperties())
            {
                var left = (Array)property.GetValue(expected);
                var right = (Array)property.GetValue(actual);
                Assert.That(right.Length, Is.EqualTo(left.Length), property.Name);
                for (int i = 0; i < left.Length; i++)
                    AssertExact(left.GetValue(i), right.GetValue(i), property.Name + "[" + i + "]");
            }
            Assert.That(actual.Checksum(), Is.EqualTo(expected.Checksum()));
        }

        static void AssertExact(object expected, object actual, string path)
        {
            Type type = expected.GetType();
            if (type == typeof(float))
                Assert.That(FloatBits((float)actual), Is.EqualTo(FloatBits((float)expected)), path);
            else if (type.IsPrimitive || type.IsEnum)
                Assert.That(actual, Is.EqualTo(expected), path);
            else
                foreach (FieldInfo field in type.GetFields(PublicFields))
                    AssertExact(field.GetValue(expected), field.GetValue(actual), path + "." + field.Name);
        }

        static int FloatBits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        static float FloatFromBits(int value) => BitConverter.ToSingle(BitConverter.GetBytes(value), 0);

        static uint[] DrawRandom(DashArenaState state, int count)
        {
            var values = new uint[count];
            for (int i = 0; i < count; i++) values[i] = ArenaRandom.Next(ref state.RandomState);
            return values;
        }
    }
}
