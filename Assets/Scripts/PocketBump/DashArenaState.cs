using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PocketBump
{
    public struct DashPlayerState
    {
        public Vector2 Position;
        public Vector2 ControlVelocity;
        public Vector2 ExternalVelocity;
        public Vector2 Facing;
        public Vector2 DashDirection;
        public bool Alive;
        public int DashTicks;
        public int DashCooldownTicks;
        public int StunTicks;
        public int RespawnTicks;
        public int GreenTicks;
        public int PurpleTicks;
        public Vector2 Velocity => ControlVelocity + ExternalVelocity;
    }

    public struct DashBallState
    {
        public bool Active;
        public Vector2 Position;
        public Vector2 Velocity;
        public int DangerousImpactCount;
    }

    public struct DashWallState
    {
        public bool Broken;
        public bool RepairPending;
        public int RepairTicks;
    }

    public enum DashPickupKind { Green, Blue, Purple }

    public struct DashPickupState
    {
        public bool Active;
        public int Id;
        public DashPickupKind Kind;
        public Vector2 Position;
    }

    // Independent of the released v6 state. Simulation owns mutations; snapshots own all
    // mutable arrays. The deeply immutable profile alone is deliberately shared.
    public sealed class DashArenaState
    {
        public int Tick;
        public int Epoch;
        public MatchState Phase;
        public int PhaseTicks;
        public bool GateClosed;
        public int LastScorer;
        public uint InitialSeed;
        public uint RandomState;
        public int NextPickupId;
        public int SpawnTicks;

        public ArenaRulesProfile Rules { get; private set; }
        public int[] Scores => _scores;
        public DashPlayerState[] Players => _players;
        public DashBallState[] Balls => _balls;
        public DashWallState[] Walls => _walls;
        public DashPickupState[] Pickups => _pickups;

        // Row-major: player * objectCount + objectIndex. PlayerContacts[0] is pair 0/1.
        public bool[] PlayerContacts => _playerContacts;
        public bool[] PlayerBallContacts => _playerBallContacts;
        public bool[] CylinderContacts => _cylinderContacts;
        public bool[] WallContacts => _wallContacts;
        // Row-major: ball * wallCount + wallIndex. Balls only use base physics at pillars.
        public bool[] BallWallContacts => _ballWallContacts;

        int[] _scores;
        DashPlayerState[] _players;
        DashBallState[] _balls;
        DashWallState[] _walls;
        DashPickupState[] _pickups;
        bool[] _playerContacts;
        bool[] _playerBallContacts;
        bool[] _cylinderContacts;
        bool[] _wallContacts;
        bool[] _ballWallContacts;

        public DashArenaState(ArenaRulesProfile rules, uint seed, int epoch = 0)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (seed == 0) throw new ArgumentOutOfRangeException(nameof(seed), "Gameplay seed must be nonzero.");
            if (epoch < 0) throw new ArgumentOutOfRangeException(nameof(epoch));
            Rules = rules;
            Epoch = epoch;
            InitialSeed = RandomState = seed;
            Phase = MatchState.Pregame;
            GateClosed = true;
            LastScorer = -1;
            NextPickupId = 1;
            _scores = new int[2];
            _players = new DashPlayerState[2];
            _balls = new DashBallState[rules.BallCount];
            _walls = new DashWallState[rules.Walls.Count];
            _pickups = new DashPickupState[rules.PickupLimit];
            _playerContacts = new bool[1];
            _playerBallContacts = new bool[2 * rules.BallCount];
            _cylinderContacts = new bool[2 * rules.Pillars.Count];
            _wallContacts = new bool[2 * rules.Walls.Count];
            _ballWallContacts = new bool[rules.BallCount * rules.Walls.Count];
        }

        public DashArenaState Clone()
        {
            var copy = new DashArenaState(Rules, InitialSeed, Epoch);
            copy.CopyFrom(this);
            return copy;
        }

        public void CopyFrom(DashArenaState source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (ReferenceEquals(this, source)) return;
            Rules = source.Rules;
            Tick = source.Tick;
            Epoch = source.Epoch;
            Phase = source.Phase;
            PhaseTicks = source.PhaseTicks;
            GateClosed = source.GateClosed;
            LastScorer = source.LastScorer;
            InitialSeed = source.InitialSeed;
            RandomState = source.RandomState;
            NextPickupId = source.NextPickupId;
            SpawnTicks = source.SpawnTicks;
            CopyArray(source._scores, ref _scores);
            CopyArray(source._players, ref _players);
            CopyArray(source._balls, ref _balls);
            CopyArray(source._walls, ref _walls);
            CopyArray(source._pickups, ref _pickups);
            CopyArray(source._playerContacts, ref _playerContacts);
            CopyArray(source._playerBallContacts, ref _playerBallContacts);
            CopyArray(source._cylinderContacts, ref _cylinderContacts);
            CopyArray(source._wallContacts, ref _wallContacts);
            CopyArray(source._ballWallContacts, ref _ballWallContacts);
        }

        static void CopyArray<T>(T[] source, ref T[] destination)
        {
            if (destination == null || destination.Length != source.Length)
                destination = new T[source.Length];
            Array.Copy(source, destination, source.Length);
        }

        // Canonical schema 1: declared order, explicit little-endian integer bytes,
        // raw IEEE float bits, length-prefixed UTF-16 profile digest, then sized arrays.
        // No Unity approximate equality, float rounding, or randomized string hashes.
        public int Checksum()
        {
            var hash = DashStateHash.Begin();
            hash.Add(1);
            hash.Add(Rules.ContentHash);
            hash.Add(Tick);
            hash.Add(Epoch);
            hash.Add((int)Phase);
            hash.Add(PhaseTicks);
            hash.Add(GateClosed);
            hash.Add(LastScorer);
            hash.Add(InitialSeed);
            hash.Add(RandomState);
            hash.Add(NextPickupId);
            hash.Add(SpawnTicks);
            hash.Add(_scores.Length);
            foreach (int score in _scores) hash.Add(score);
            hash.Add(_players.Length);
            foreach (DashPlayerState player in _players)
            {
                hash.Add(player.Position);
                hash.Add(player.ControlVelocity);
                hash.Add(player.ExternalVelocity);
                hash.Add(player.Facing);
                hash.Add(player.DashDirection);
                hash.Add(player.Alive);
                hash.Add(player.DashTicks);
                hash.Add(player.DashCooldownTicks);
                hash.Add(player.StunTicks);
                hash.Add(player.RespawnTicks);
                hash.Add(player.GreenTicks);
                hash.Add(player.PurpleTicks);
            }
            hash.Add(_balls.Length);
            foreach (DashBallState ball in _balls)
            {
                hash.Add(ball.Active);
                hash.Add(ball.Position);
                hash.Add(ball.Velocity);
                hash.Add(ball.DangerousImpactCount);
            }
            hash.Add(_walls.Length);
            foreach (DashWallState wall in _walls)
            {
                hash.Add(wall.Broken);
                hash.Add(wall.RepairPending);
                hash.Add(wall.RepairTicks);
            }
            hash.Add(_pickups.Length);
            foreach (DashPickupState pickup in _pickups)
            {
                hash.Add(pickup.Active);
                hash.Add(pickup.Id);
                hash.Add((int)pickup.Kind);
                hash.Add(pickup.Position);
            }
            hash.Add(_playerContacts);
            hash.Add(_playerBallContacts);
            hash.Add(_cylinderContacts);
            hash.Add(_wallContacts);
            hash.Add(_ballWallContacts);
            return hash.Value;
        }
    }

    public enum DashArenaEventKind
    {
        DashStarted, Impact, Goal, Fell, Respawned, Stunned, WallHit, WallBroken,
        WallRepairPending, WallRepaired, Pickup, Teleport, TeleportFailed, GateOpened, MatchOver, PillarHit
    }

    // Position is payload, not identity: correcting a predicted event can move its effect.
    // The caller must scope its ledger to a connection and assign stable per-tick ordinals.
    public readonly struct DashArenaEventIdentity : IEquatable<DashArenaEventIdentity>
    {
        public readonly int Tick;
        public readonly int Epoch;
        public readonly int Seat;
        public readonly int EntityId;
        public readonly DashArenaEventKind Kind;
        public readonly int Ordinal;

        public DashArenaEventIdentity(int tick, int epoch, int seat, int entityId,
            DashArenaEventKind kind, int ordinal = 0)
        {
            Tick = tick;
            Epoch = epoch;
            Seat = seat;
            EntityId = entityId;
            Kind = kind;
            Ordinal = ordinal;
        }

        public bool Equals(DashArenaEventIdentity other)
        {
            return Tick == other.Tick && Epoch == other.Epoch && Seat == other.Seat &&
                   EntityId == other.EntityId && Kind == other.Kind && Ordinal == other.Ordinal;
        }

        public override bool Equals(object obj) => obj is DashArenaEventIdentity other && Equals(other);
        public override int GetHashCode()
        {
            var hash = DashStateHash.Begin();
            hash.Add(Tick);
            hash.Add(Epoch);
            hash.Add(Seat);
            hash.Add(EntityId);
            hash.Add((int)Kind);
            hash.Add(Ordinal);
            return hash.Value;
        }
    }

    // Transient output belongs in the caller's event list, never in a future-state snapshot.
    public readonly struct DashArenaEvent : IEquatable<DashArenaEvent>
    {
        public readonly int Tick;
        public readonly int Epoch;
        public readonly int Seat;
        public readonly int EntityId;
        public readonly DashArenaEventKind Kind;
        public readonly Vector2 Position;
        public readonly int Ordinal;
        public readonly DashPickupKind? PickupKind;
        public DashArenaEventIdentity Identity => new DashArenaEventIdentity(Tick, Epoch, Seat, EntityId, Kind, Ordinal);

        public DashArenaEvent(int tick, int epoch, int seat, int entityId, DashArenaEventKind kind,
            Vector2 position, int ordinal = 0, DashPickupKind? pickupKind = null)
        {
            Tick = tick;
            Epoch = epoch;
            Seat = seat;
            EntityId = entityId;
            Kind = kind;
            Position = position;
            Ordinal = ordinal;
            PickupKind = pickupKind;
        }

        public bool Equals(DashArenaEvent other)
        {
            return Identity.Equals(other.Identity) && PickupKind == other.PickupKind &&
                   DashStateHash.FloatBits(Position.x) == DashStateHash.FloatBits(other.Position.x) &&
                   DashStateHash.FloatBits(Position.y) == DashStateHash.FloatBits(other.Position.y);
        }

        public override bool Equals(object obj) => obj is DashArenaEvent other && Equals(other);
        public override int GetHashCode()
        {
            var hash = DashStateHash.Begin();
            hash.Add(Identity.GetHashCode());
            hash.Add(Position);
            hash.Add(PickupKind.HasValue);
            if (PickupKind.HasValue) hash.Add((int)PickupKind.Value);
            return hash.Value;
        }
    }

    internal struct DashStateHash
    {
        uint _value;
        public int Value => unchecked((int)_value);
        public static DashStateHash Begin() => new DashStateHash { _value = 2166136261u };
        public void Add(int value) => Add(unchecked((uint)value));
        public void Add(bool value) => Add(value ? 1 : 0);
        public void Add(Vector2 value) { Add(FloatBits(value.x)); Add(FloatBits(value.y)); }
        public void Add(uint value)
        {
            for (int shift = 0; shift < 32; shift += 8)
                AddByte((byte)(value >> shift));
        }
        public void Add(string value)
        {
            if (value == null) { Add(-1); return; }
            Add(value.Length);
            foreach (char character in value)
            {
                AddByte((byte)character);
                AddByte((byte)(character >> 8));
            }
        }
        public void Add(bool[] values)
        {
            Add(values.Length);
            foreach (bool value in values) Add(value);
        }
        void AddByte(byte value) { unchecked { _value = (_value ^ value) * 16777619u; } }
        public static uint FloatBits(float value) => new FloatWord { Float = value }.Word;

        [StructLayout(LayoutKind.Explicit)]
        struct FloatWord
        {
            [FieldOffset(0)] public float Float;
            [FieldOffset(0)] public uint Word;
        }
    }
}
