using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace PocketBump
{
    public readonly struct ArenaPillarDefinition
    {
        public int Id { get; }
        public Vector2 Position { get; }
        public float Radius { get; }
        public ArenaPillarDefinition(int id, Vector2 position, float radius)
        { Id = id; Position = position; Radius = radius; }
    }

    public readonly struct ArenaWallDefinition
    {
        public int Id { get; }
        public Vector2 Position { get; }
        public Vector2 HalfSize { get; }
        public ArenaWallDefinition(int id, Vector2 position, Vector2 halfSize)
        { Id = id; Position = position; HalfSize = halfSize; }
    }

    // Frozen once before a match. Production V1 preserves the user-approved greybox snapshot;
    // experiments remain explicitly provisional. Neither peers nor views derive rules from scenes.
    public sealed class ArenaRulesProfile
    {
        public const int MaximumBalls = 8;
        public const int MaximumObstacles = 64;
        public const int RulesRevision = 1;
        readonly Builder data;
        readonly ReadOnlyCollection<ArenaPillarDefinition> pillars;
        readonly ReadOnlyCollection<ArenaWallDefinition> walls;
        readonly ReadOnlyCollection<Vector2>[] respawns;
        readonly ReadOnlyCollection<Vector2> pickups, teleports;

        public int TickRate => 60;
        public bool IsProvisional => data.IsProvisional;
        public int PointsToWin => data.PointsToWin;
        public int BallCount => data.BallCount;
        public float Width => data.Width;
        public float Height => data.Height;
        public float HalfWidth => Width * .5f;
        public float HalfHeight => Height * .5f;
        public float PlayerRadius => data.PlayerRadius;
        public float BallRadius => data.BallRadius;
        public float GoalWidth => data.GoalWidth;
        public float DeathOpening => data.DeathOpening;
        public float WallThickness => data.WallThickness;
        // Reserve the complete eight-ball cluster while players approach the kickoff gate.
        // A thin decorative divider alone lets player footprints overlap newly spawned balls.
        public float KickoffHalfDepth
        {
            get
            {
                float extent = WallThickness * .5f;
                foreach (Vector2 point in data.BallSpawns)
                    extent = Mathf.Max(extent, Mathf.Abs(point.y) + BallRadius + RepairClearance);
                return extent;
            }
        }
        public int DashTicks => data.DashTicks;
        public int DashCooldownTicks => data.DashCooldownTicks;
        public int StunTicks => data.StunTicks;
        public int RespawnTicks => data.RespawnTicks;
        public int WallRepairTicks => data.WallRepairTicks;
        public int GreenTicks => data.GreenTicks;
        public int PurpleTicks => data.PurpleTicks;
        public int BuffSpawnTicks => data.BuffSpawnTicks;
        public int PickupLimit => data.PickupLimit;
        public int FreezeTicks => data.FreezeTicks;
        public int KickoffTicks => data.KickoffTicks;
        public float GreenImpactMultiplier => data.GreenImpactMultiplier;
        public float NormalSpeed => data.NormalSpeed;
        public float Acceleration => data.Acceleration;
        public float Braking => data.Braking;
        public float ExternalDecay => data.ExternalDecay;
        public float MaxExternalSpeed => data.MaxExternalSpeed;
        public float DashSpeed => data.DashSpeed;
        public float BallMaxSpeed => data.BallMaxSpeed;
        public float BallDrag => data.BallDrag;
        public float NormalShotImpulse => data.NormalShotImpulse;
        public float DashShotImpulse => data.DashShotImpulse;
        public float NormalPlayerImpulse => data.NormalPlayerImpulse;
        public float DashPlayerImpulse => data.DashPlayerImpulse;
        public float WallBreakSpeed => data.WallBreakSpeed;
        public float DangerousBallSpeed => data.DangerousBallSpeed;
        public float DangerousImpulseScale => data.DangerousImpulseScale;
        public float DangerousEnergyRetention => data.DangerousEnergyRetention;
        public float ContactReleasePadding => data.ContactReleasePadding;
        public float RepairClearance => data.RepairClearance;
        public float PickupRadius => data.PickupRadius;
        public float PlayerMass => data.PlayerMass;
        public float BallMass => data.BallMass;
        public float Bounciness => data.Bounciness;
        public float PlayerBounciness => data.PlayerBounciness;
        public float SpawnSafetyRadius => data.SpawnSafetyRadius;
        public IReadOnlyList<ArenaPillarDefinition> Pillars => pillars;
        public IReadOnlyList<ArenaWallDefinition> Walls => walls;
        public IReadOnlyList<Vector2> PickupCandidates => pickups;
        public IReadOnlyList<Vector2> TeleportCandidates => teleports;
        public string ContentHash { get; }
        public string LayoutHash { get; }

        ArenaRulesProfile(Builder source)
        {
            data = source.CopyOwned();
            Array.Sort(data.Pillars, (a, b) => a.Id.CompareTo(b.Id));
            Array.Sort(data.Walls, (a, b) => a.Id.CompareTo(b.Id));
            pillars = Array.AsReadOnly(data.Pillars);
            walls = Array.AsReadOnly(data.Walls);
            respawns = new[] { Array.AsReadOnly(data.Respawn0), Array.AsReadOnly(data.Respawn1) };
            pickups = Array.AsReadOnly(data.PickupCandidates);
            teleports = Array.AsReadOnly(data.TeleportCandidates);
            Validate();
            LayoutHash = ComputeHash(false);
            ContentHash = ComputeHash(true);
        }

        public static ArenaRulesProfile CreateGreybox(int pointsToWin = 8, int ballCount = 1) =>
            Builder.CreateGreybox(pointsToWin, ballCount).Freeze();

        // V1 is a content snapshot, not an alias for the current experimental preset. Its
        // only supported match-specific choices remain target score and scoring-ball count.
        public static ArenaRulesProfile CreateProductionV1(int pointsToWin = 8, int ballCount = 1) =>
            Builder.CreateProductionV1(pointsToWin, ballCount).Freeze();

        public Builder ToBuilder() => data.CopyOwned();
        public Vector2 SpawnForSeat(int seat)
        {
            RequireSeat(seat);
            return data.Spawns[seat];
        }
        public IReadOnlyList<Vector2> RespawnCandidates(int seat)
        {
            RequireSeat(seat);
            return respawns[seat];
        }
        public Vector2 BallSpawn(int index)
        {
            if (index < 0 || index >= BallCount) throw new ArgumentOutOfRangeException(nameof(index));
            return data.BallSpawns[index];
        }
        static void RequireSeat(int seat)
        { if (seat < 0 || seat > 1) throw new ArgumentOutOfRangeException(nameof(seat)); }

        void Validate()
        {
            Require(PointsToWin >= MatchSettings.MinPointsToWin && PointsToWin <= MatchSettings.MaxPointsToWin,
                "PointsToWin must preserve the supported 1..21 match settings.");
            Require(BallCount >= 1 && BallCount <= MaximumBalls, "BallCount must be within 1..8.");
            Positive(Width, nameof(Width)); Positive(Height, nameof(Height));
            Require(Width <= 256f && Height <= 256f, "Layout exceeds bounded route-check dimensions.");
            Positive(PlayerRadius, nameof(PlayerRadius)); Positive(BallRadius, nameof(BallRadius));
            Positive(GoalWidth, nameof(GoalWidth)); Positive(DeathOpening, nameof(DeathOpening));
            Positive(WallThickness, nameof(WallThickness)); Positive(SpawnSafetyRadius, nameof(SpawnSafetyRadius));
            Require(GoalWidth > BallRadius * 2f && GoalWidth < Width, "Goal must admit a whole ball.");
            Require(DeathOpening > PlayerRadius * 2f && DeathOpening < Height, "Death opening must admit a whole player.");
            Require(SpawnSafetyRadius >= PlayerRadius, "Spawn safe radius must cover a whole player.");
            int[] durations = { DashTicks, DashCooldownTicks, StunTicks, RespawnTicks, WallRepairTicks,
                GreenTicks, PurpleTicks, BuffSpawnTicks, FreezeTicks, KickoffTicks };
            foreach (int duration in durations) Require(duration > 0 && duration <= 360000, "Tick duration outside 1..360000.");
            Require(PickupLimit > 0 && PickupLimit <= 16, "PickupLimit must be within 1..16.");
            float[] tuning = { GreenImpactMultiplier, NormalSpeed, Acceleration, Braking, ExternalDecay,
                MaxExternalSpeed, DashSpeed, BallMaxSpeed, BallDrag, NormalShotImpulse, DashShotImpulse,
                NormalPlayerImpulse, DashPlayerImpulse, WallBreakSpeed, DangerousBallSpeed,
                DangerousImpulseScale, DangerousEnergyRetention, ContactReleasePadding, RepairClearance,
                PickupRadius, PlayerMass, BallMass, Bounciness, PlayerBounciness };
            foreach (float value in tuning) Positive(value, "Handfeel setting");
            Require(DangerousEnergyRetention <= 1f && Bounciness <= 1f && PlayerBounciness <= 1f,
                "Retention and restitution cannot exceed one.");
            Require(GreenImpactMultiplier >= 1f, "Green impact multiplier cannot weaken outgoing impacts.");
            Require(DashSpeed > NormalSpeed && WallBreakSpeed <= DashSpeed && BallMaxSpeed >= DashSpeed,
                "Dash, wall threshold and ball cap must support their intended interactions.");
            Require(pillars.Count + walls.Count <= MaximumObstacles, "Too many obstacles.");
            var ids = new HashSet<int>();
            foreach (ArenaPillarDefinition pillar in pillars)
            {
                Require(pillar.Id >= 0 && ids.Add(pillar.Id), "Pillar ID " + pillar.Id + " must be unique and nonnegative.");
                Positive(pillar.Radius, "Pillar " + pillar.Id + " radius");
                Require(ArenaGeometry.ContainsFootprint(pillar.Position, pillar.Radius, HalfWidth, HalfHeight),
                    "Pillar " + pillar.Id + " footprint is outside the arena.");
            }
            foreach (ArenaWallDefinition wall in walls)
            {
                Require(wall.Id >= 0 && ids.Add(wall.Id), "Wall ID " + wall.Id + " must be unique and nonnegative.");
                Require(ArenaGeometry.IsFinite(wall.Position) && ArenaGeometry.IsFinite(wall.HalfSize) &&
                    wall.HalfSize.x > 0f && wall.HalfSize.y > 0f &&
                    Mathf.Abs(wall.Position.x) + wall.HalfSize.x <= HalfWidth &&
                    Mathf.Abs(wall.Position.y) + wall.HalfSize.y <= HalfHeight, "Invalid wall " + wall.Id + " footprint.");
            }
            for (int i = 0; i < pillars.Count; i++)
            {
                for (int j = i + 1; j < pillars.Count; j++)
                    Require(!ArenaGeometry.CirclesOverlap(pillars[i].Position, pillars[i].Radius,
                        pillars[j].Position, pillars[j].Radius), "Pillars " + pillars[i].Id + " and " + pillars[j].Id + " overlap.");
                foreach (ArenaWallDefinition wall in walls)
                    Require(!ArenaGeometry.CircleAabbOverlap(pillars[i].Position, pillars[i].Radius,
                        wall.Position, wall.HalfSize), "Pillar " + pillars[i].Id + " and wall " + wall.Id + " overlap.");
            }
            for (int i = 0; i < walls.Count; i++)
                for (int j = i + 1; j < walls.Count; j++)
                    Require(!ArenaGeometry.AabbsOverlap(walls[i].Position, walls[i].HalfSize,
                        walls[j].Position, walls[j].HalfSize), "Walls " + walls[i].Id + " and " + walls[j].Id + " overlap.");
            // A clear approach point alone is insufficient: an obstacle behind that point
            // could still seal the actual boundary opening traversed by a ball or player.
            ValidatePortal(new Vector2(0f, HalfHeight - PlayerRadius * .5f), new Vector2(GoalWidth * .5f, PlayerRadius * .5f));
            ValidatePortal(new Vector2(0f, -HalfHeight + PlayerRadius * .5f), new Vector2(GoalWidth * .5f, PlayerRadius * .5f));
            ValidatePortal(new Vector2(HalfWidth - PlayerRadius * .5f, 0f), new Vector2(PlayerRadius * .5f, DeathOpening * .5f));
            ValidatePortal(new Vector2(-HalfWidth + PlayerRadius * .5f, 0f), new Vector2(PlayerRadius * .5f, DeathOpening * .5f));
            Require(data.Spawns.Length == 2 && data.Respawn0.Length > 0 && data.Respawn1.Length > 0,
                "Each seat needs a spawn and an ordered respawn set.");
            Require(data.PickupCandidates.Length > 0 && data.TeleportCandidates.Length > 0,
                "A playable layout needs Buff and teleport candidates; dynamic occupancy may temporarily block them.");
            Require(data.BallSpawns.Length == MaximumBalls, "Layout must reserve all eight possible ball footprints.");
            var routes = new List<Vector2>();
            ValidatePositions(data.Spawns, SpawnSafetyRadius, routes);
            Require(data.Spawns[0].y > KickoffHalfDepth + PlayerRadius && data.Spawns[1].y < -KickoffHalfDepth - PlayerRadius,
                "Seats must spawn in their respective north/south halves, clear of the reserved kickoff cluster.");
            ValidatePositions(data.Respawn0, PlayerRadius + RepairClearance, routes);
            ValidatePositions(data.Respawn1, PlayerRadius + RepairClearance, routes);
            foreach (Vector2 point in data.Respawn0) Require(point.y > PlayerRadius, "Seat zero respawn crosses half.");
            foreach (Vector2 point in data.Respawn1) Require(point.y < -PlayerRadius, "Seat one respawn crosses half.");
            ValidatePositions(data.BallSpawns, BallRadius, null);
            for (int i = 0; i < data.BallSpawns.Length; i++)
            {
                Require(data.BallSpawns[i].magnitude <= 3f, "Ball cluster must remain central.");
                foreach (Vector2 spawn in data.Spawns)
                    Require(!ArenaGeometry.CirclesOverlap(data.BallSpawns[i], BallRadius, spawn, SpawnSafetyRadius),
                        "Ball cluster intersects a player birth region.");
                for (int j = i + 1; j < data.BallSpawns.Length; j++)
                    Require(!ArenaGeometry.CirclesOverlap(data.BallSpawns[i], BallRadius, data.BallSpawns[j], BallRadius),
                        "Initial balls overlap.");
            }
            ValidatePositions(data.PickupCandidates, Mathf.Max(PlayerRadius, PickupRadius + RepairClearance), routes);
            ValidatePositions(data.TeleportCandidates, PlayerRadius + RepairClearance, routes);
            foreach (Vector2 point in data.PickupCandidates)
                foreach (Vector2 spawn in data.Spawns)
                    Require(!ArenaGeometry.CirclesOverlap(point, PickupRadius, spawn, SpawnSafetyRadius),
                        "Pickup candidate intersects a birth safety region.");
            routes.Add(Vector2.zero);
            routes.Add(new Vector2(0f, HalfHeight - PlayerRadius - .05f));
            routes.Add(new Vector2(0f, -HalfHeight + PlayerRadius + .05f));
            ValidateRoutes(routes);
        }

        void ValidatePortal(Vector2 position, Vector2 halfSize)
        {
            foreach (ArenaPillarDefinition pillar in pillars)
                Require(!ArenaGeometry.CircleAabbOverlap(pillar.Position, pillar.Radius, position, halfSize),
                    "Pillar " + pillar.Id + " blocks a boundary goal/death opening.");
            foreach (ArenaWallDefinition wall in walls)
                Require(!ArenaGeometry.AabbsOverlap(wall.Position, wall.HalfSize, position, halfSize),
                    "Wall " + wall.Id + " blocks a boundary goal/death opening.");
        }

        void ValidatePositions(Vector2[] points, float radius, List<Vector2> routes)
        {
            Require(points.Length <= 128, "Candidate list exceeds bounded capacity.");
            for (int i = 0; i < points.Length; i++)
            {
                Require(ArenaGeometry.ContainsFootprint(points[i], radius, HalfWidth, HalfHeight),
                    "Spawn/candidate footprint is out of bounds.");
                foreach (ArenaPillarDefinition pillar in pillars)
                    Require(!ArenaGeometry.CirclesOverlap(points[i], radius, pillar.Position, pillar.Radius),
                        "Pillar " + pillar.Id + " blocks a mandatory spawn/candidate footprint.");
                foreach (ArenaWallDefinition wall in walls)
                    Require(!ArenaGeometry.CircleAabbOverlap(points[i], radius, wall.Position, wall.HalfSize),
                        "Wall " + wall.Id + " blocks a mandatory spawn/candidate footprint.");
                for (int j = 0; j < i; j++) Require(points[i] != points[j], "Candidate positions must be unique within a list.");
                routes?.Add(points[i]);
            }
        }

        // One-unit deterministic flood-fill, using swept full footprints on each edge. This
        // rejects enclosed spawn/goal/candidate regions instead of only checking endpoints.
        void ValidateRoutes(List<Vector2> required)
        {
            int extentX = Mathf.CeilToInt(HalfWidth), extentY = Mathf.CeilToInt(HalfHeight);
            int columns = extentX * 2 + 1, rows = extentY * 2 + 1;
            var clear = new bool[columns * rows];
            var visited = new bool[clear.Length];
            var queue = new int[clear.Length];
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                    clear[y * columns + x] = ArenaGeometry.IsFootprintClear(this, new Vector2(x - extentX, y - extentY), PlayerRadius);
            Vector2 first = data.Spawns[0];
            int startX = Mathf.RoundToInt(first.x) + extentX, startY = Mathf.RoundToInt(first.y) + extentY;
            int start = startY * columns + startX;
            Require(start >= 0 && start < clear.Length && clear[start] &&
                ArenaGeometry.IsRouteSegmentClear(this, first, new Vector2(startX - extentX, startY - extentY), PlayerRadius),
                "Spawn lacks a safe route-check anchor.");
            int head = 0, tail = 0;
            queue[tail++] = start; visited[start] = true;
            int[] dx = { -1, 1, 0, 0 }, dy = { 0, 0, -1, 1 };
            while (head < tail)
            {
                int current = queue[head++], x = current % columns, y = current / columns;
                for (int direction = 0; direction < 4; direction++)
                {
                    int nx = x + dx[direction], ny = y + dy[direction];
                    if (nx < 0 || nx >= columns || ny < 0 || ny >= rows) continue;
                    int next = ny * columns + nx;
                    if (!clear[next] || visited[next] || !ArenaGeometry.IsRouteSegmentClear(this,
                        new Vector2(x - extentX, y - extentY), new Vector2(nx - extentX, ny - extentY), PlayerRadius)) continue;
                    visited[next] = true; queue[tail++] = next;
                }
            }
            foreach (Vector2 point in required)
            {
                bool reachable = false;
                int x = Mathf.RoundToInt(point.x) + extentX, y = Mathf.RoundToInt(point.y) + extentY;
                for (int ny = Mathf.Max(0, y - 1); ny <= Mathf.Min(rows - 1, y + 1) && !reachable; ny++)
                    for (int nx = Mathf.Max(0, x - 1); nx <= Mathf.Min(columns - 1, x + 1); nx++)
                        if (visited[ny * columns + nx] && ArenaGeometry.IsRouteSegmentClear(this,
                            point, new Vector2(nx - extentX, ny - extentY), PlayerRadius)) { reachable = true; break; }
                if (!reachable)
                {
                    var blockingIds = new List<string>();
                    foreach (ArenaPillarDefinition pillar in pillars) blockingIds.Add("pillar:" + pillar.Id);
                    foreach (ArenaWallDefinition wall in walls) blockingIds.Add("wall:" + wall.Id);
                    throw new ArgumentException("Spawn, center, goal approach or candidate is disconnected by solid layout. " +
                        "Review stable obstacle IDs [" + string.Join(",", blockingIds) + "].");
                }
            }
        }

        static void Require(bool condition, string message)
        { if (!condition) throw new ArgumentException(message); }
        static void Positive(float value, string name)
        { Require(ArenaGeometry.IsFinite(value) && value > 0f && value <= 10000f, name + " must be positive, finite and within 10000."); }

        string ComputeHash(bool includeRules)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(includeRules ? "PocketBump.rules" : "PocketBump.layout");
                writer.Write(RulesRevision);
                WriteFloat(writer, Width); WriteFloat(writer, Height); WriteFloat(writer, PlayerRadius);
                WriteFloat(writer, BallRadius); WriteFloat(writer, GoalWidth); WriteFloat(writer, DeathOpening);
                WriteFloat(writer, WallThickness); WriteFloat(writer, SpawnSafetyRadius);
                WriteFloat(writer, KickoffHalfDepth);
                writer.Write(pillars.Count);
                foreach (ArenaPillarDefinition pillar in pillars)
                { writer.Write(pillar.Id); WriteVector(writer, pillar.Position); WriteFloat(writer, pillar.Radius); }
                writer.Write(walls.Count);
                foreach (ArenaWallDefinition wall in walls)
                { writer.Write(wall.Id); WriteVector(writer, wall.Position); WriteVector(writer, wall.HalfSize); }
                WritePoints(writer, data.Spawns); WritePoints(writer, data.Respawn0); WritePoints(writer, data.Respawn1);
                WritePoints(writer, data.BallSpawns); WritePoints(writer, data.PickupCandidates); WritePoints(writer, data.TeleportCandidates);
                if (includeRules)
                {
                    writer.Write(TickRate); writer.Write(IsProvisional); writer.Write(PointsToWin); writer.Write(BallCount);
                    writer.Write(MaximumBalls); writer.Write(MaximumObstacles);
                    writer.Write(DashTicks); writer.Write(DashCooldownTicks); writer.Write(StunTicks); writer.Write(RespawnTicks);
                    writer.Write(WallRepairTicks); writer.Write(GreenTicks); writer.Write(PurpleTicks); writer.Write(BuffSpawnTicks);
                    writer.Write(PickupLimit); writer.Write(FreezeTicks); writer.Write(KickoffTicks);
                    float[] tuning = { GreenImpactMultiplier, NormalSpeed, Acceleration, Braking, ExternalDecay,
                        MaxExternalSpeed, DashSpeed, BallMaxSpeed, BallDrag, NormalShotImpulse, DashShotImpulse,
                        NormalPlayerImpulse, DashPlayerImpulse, WallBreakSpeed, DangerousBallSpeed,
                        DangerousImpulseScale, DangerousEnergyRetention, ContactReleasePadding, RepairClearance,
                        PickupRadius, PlayerMass, BallMass, Bounciness, PlayerBounciness };
                    foreach (float value in tuning) WriteFloat(writer, value);
                    writer.Write("xorshift32:13,17,5;spawn:position,type:equal3;blue:one");
                    writer.Write(ArenaRandom.DefaultSeed);
                }
                writer.Flush();
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] digest = sha.ComputeHash(stream.ToArray());
                    var result = new StringBuilder(64);
                    foreach (byte value in digest) result.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                    return result.ToString();
                }
            }
        }
        static void WriteFloat(BinaryWriter writer, float value) => writer.Write(BitConverter.ToInt32(BitConverter.GetBytes(value), 0));
        static void WriteVector(BinaryWriter writer, Vector2 value)
        { WriteFloat(writer, value.x); WriteFloat(writer, value.y); }
        static void WritePoints(BinaryWriter writer, Vector2[] points)
        { writer.Write(points.Length); foreach (Vector2 point in points) WriteVector(writer, point); }

        // Mutable authoring/test object only; Freeze always copies every array before validation.
        public sealed class Builder
        {
            public bool IsProvisional = true;
            public int PointsToWin = 8, BallCount = 1;
            public float Width = 48f, Height = 72f, PlayerRadius = .78f, BallRadius = .3f;
            public float GoalWidth = 3.4f, DeathOpening = 9.675f, WallThickness = .45f, SpawnSafetyRadius = 3f;
            public int DashTicks = 18, DashCooldownTicks = 180, StunTicks = 36, RespawnTicks = 180;
            public int WallRepairTicks = 600, GreenTicks = 300, PurpleTicks = 300, BuffSpawnTicks = 600;
            public int PickupLimit = 3, FreezeTicks = 60, KickoffTicks = 180;
            public float GreenImpactMultiplier = 1.5f, NormalSpeed = 12f, Acceleration = 36f, Braking = 21f;
            public float ExternalDecay = 8f, MaxExternalSpeed = 72f, DashSpeed = 24f, BallMaxSpeed = 48f, BallDrag = 2f;
            public float NormalShotImpulse = 4f, DashShotImpulse = 12f, NormalPlayerImpulse = 6f, DashPlayerImpulse = 14f;
            public float WallBreakSpeed = 18f, DangerousBallSpeed = 10f, DangerousImpulseScale = 1.15f;
            public float DangerousEnergyRetention = .75f, ContactReleasePadding = .08f, RepairClearance = .05f, PickupRadius = .6f;
            public float PlayerMass = 2.8f, BallMass = .38f, Bounciness = .58f, PlayerBounciness = .90f;
            public ArenaPillarDefinition[] Pillars = Array.Empty<ArenaPillarDefinition>();
            public ArenaWallDefinition[] Walls = Array.Empty<ArenaWallDefinition>();
            public Vector2[] Spawns = Array.Empty<Vector2>(), Respawn0 = Array.Empty<Vector2>(), Respawn1 = Array.Empty<Vector2>();
            public Vector2[] BallSpawns = Array.Empty<Vector2>(), PickupCandidates = Array.Empty<Vector2>(), TeleportCandidates = Array.Empty<Vector2>();

            public static Builder CreateGreybox(int pointsToWin = 8, int ballCount = 1)
            {
                // Future experiments belong here (or in a copied builder), never in the
                // approved V1 snapshot below. Each invocation owns independent authoring arrays.
                Builder result = CreateProductionV1(pointsToWin, ballCount);
                result.IsProvisional = true;
                return result;
            }

            internal static Builder CreateProductionV1(int pointsToWin, int ballCount)
            {
                // Approved 2026-10-02: explicitly pin every scalar as well as the ordered
                // geometry. Do not inherit experimental Builder field defaults in V1.
                return new Builder
                {
                    IsProvisional = false, PointsToWin = pointsToWin, BallCount = ballCount,
                    Width = 48f, Height = 72f, PlayerRadius = .78f, BallRadius = .3f,
                    GoalWidth = 3.4f, DeathOpening = 9.675f, WallThickness = .45f, SpawnSafetyRadius = 3f,
                    DashTicks = 18, DashCooldownTicks = 180, StunTicks = 36, RespawnTicks = 180,
                    WallRepairTicks = 600, GreenTicks = 300, PurpleTicks = 300, BuffSpawnTicks = 600,
                    PickupLimit = 3, FreezeTicks = 60, KickoffTicks = 180,
                    GreenImpactMultiplier = 1.5f, NormalSpeed = 12f, Acceleration = 36f, Braking = 21f,
                    ExternalDecay = 8f, MaxExternalSpeed = 72f, DashSpeed = 24f, BallMaxSpeed = 48f, BallDrag = 2f,
                    NormalShotImpulse = 4f, DashShotImpulse = 12f, NormalPlayerImpulse = 6f, DashPlayerImpulse = 14f,
                    WallBreakSpeed = 18f, DangerousBallSpeed = 10f, DangerousImpulseScale = 1.15f,
                    DangerousEnergyRetention = .75f, ContactReleasePadding = .08f, RepairClearance = .05f, PickupRadius = .6f,
                    PlayerMass = 2.8f, BallMass = .38f, Bounciness = .58f, PlayerBounciness = .90f,
                    Pillars = new[] {
                        new ArenaPillarDefinition(100, new Vector2(-6f, -8f), 1.3f),
                        new ArenaPillarDefinition(101, new Vector2(6f, -8f), 1.3f),
                        new ArenaPillarDefinition(102, new Vector2(-6f, 8f), 1.3f),
                        new ArenaPillarDefinition(103, new Vector2(6f, 8f), 1.3f) },
                    Walls = new[] {
                        new ArenaWallDefinition(200, new Vector2(-12f, -4f), new Vector2(3f, .5f)),
                        new ArenaWallDefinition(201, new Vector2(12f, -4f), new Vector2(3f, .5f)),
                        new ArenaWallDefinition(202, new Vector2(-12f, 4f), new Vector2(3f, .5f)),
                        new ArenaWallDefinition(203, new Vector2(12f, 4f), new Vector2(3f, .5f)) },
                    Spawns = new[] { new Vector2(0f, 18f), new Vector2(0f, -18f) },
                    Respawn0 = new[] { new Vector2(0f, 18f), new Vector2(-3f, 18f), new Vector2(3f, 18f), new Vector2(0f, 21f) },
                    Respawn1 = new[] { new Vector2(0f, -18f), new Vector2(-3f, -18f), new Vector2(3f, -18f), new Vector2(0f, -21f) },
                    // Center first, then a regular .75-unit ring. All eight starts are reserved
                    // even for a one-ball match, avoiding setting-dependent obstacle collisions.
                    BallSpawns = new[] { Vector2.zero, new Vector2(.75f, 0f), new Vector2(.375f, .6495191f),
                        new Vector2(-.375f, .6495191f), new Vector2(-.75f, 0f), new Vector2(-.375f, -.6495191f),
                        new Vector2(.375f, -.6495191f), new Vector2(0f, 1.5f) },
                    PickupCandidates = new[] { new Vector2(0f, 8f), new Vector2(0f, -8f),
                        new Vector2(-12f, -12f), new Vector2(12f, -12f), new Vector2(-12f, 12f), new Vector2(12f, 12f),
                        new Vector2(-16f, 0f), new Vector2(16f, 0f), new Vector2(0f, -26f), new Vector2(0f, 26f) },
                    TeleportCandidates = new[] { new Vector2(-10f, -22f), new Vector2(10f, -22f),
                        new Vector2(-10f, 22f), new Vector2(10f, 22f), new Vector2(-16f, -12f),
                        new Vector2(16f, -12f), new Vector2(-16f, 12f), new Vector2(16f, 12f),
                        new Vector2(0f, -8f), new Vector2(0f, 8f) }
                };
            }

            public ArenaRulesProfile Freeze() => new ArenaRulesProfile(this);
            internal Builder CopyOwned()
            {
                var result = (Builder)MemberwiseClone();
                result.Pillars = Copy(Pillars); result.Walls = Copy(Walls);
                result.Spawns = Copy(Spawns); result.Respawn0 = Copy(Respawn0); result.Respawn1 = Copy(Respawn1);
                result.BallSpawns = Copy(BallSpawns); result.PickupCandidates = Copy(PickupCandidates);
                result.TeleportCandidates = Copy(TeleportCandidates);
                return result;
            }
            static T[] Copy<T>(T[] values)
            {
                if (values == null) throw new ArgumentException("Profile arrays cannot be null.");
                return (T[])values.Clone();
            }
        }
    }
}
