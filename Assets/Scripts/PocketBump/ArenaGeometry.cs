using System;
using UnityEngine;

namespace PocketBump
{
    // Shared, side-effect-free geometry for validating authored layouts and safe destinations.
    // Gameplay sweep resolution remains in the simulator; Unity colliders are presentation only.
    public static class ArenaGeometry
    {
        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool IsFinite(Vector2 value) => IsFinite(value.x) && IsFinite(value.y);

        public static bool CirclesOverlap(Vector2 a, float aRadius, Vector2 b, float bRadius)
        {
            float radius = aRadius + bRadius;
            return (a - b).sqrMagnitude < radius * radius;
        }

        public static bool CircleAabbOverlap(Vector2 position, float radius, Vector2 center, Vector2 halfSize)
        {
            Vector2 closest = new Vector2(Mathf.Clamp(position.x, center.x - halfSize.x, center.x + halfSize.x),
                Mathf.Clamp(position.y, center.y - halfSize.y, center.y + halfSize.y));
            return (position - closest).sqrMagnitude < radius * radius;
        }

        public static bool AabbsOverlap(Vector2 a, Vector2 aHalfSize, Vector2 b, Vector2 bHalfSize) =>
            Mathf.Abs(a.x - b.x) < aHalfSize.x + bHalfSize.x &&
            Mathf.Abs(a.y - b.y) < aHalfSize.y + bHalfSize.y;

        public static bool ContainsFootprint(Vector2 position, float radius, float halfWidth, float halfHeight) =>
            IsFinite(position) && IsFinite(radius) && radius >= 0f &&
            Mathf.Abs(position.x) + radius <= halfWidth && Mathf.Abs(position.y) + radius <= halfHeight;

        public static float PointSegmentDistanceSquared(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 delta = end - start;
            float lengthSquared = delta.sqrMagnitude;
            float along = lengthSquared > 0f ? Mathf.Clamp01(Vector2.Dot(point - start, delta) / lengthSquared) : 0f;
            return (point - (start + along * delta)).sqrMagnitude;
        }

        public static bool IsFootprintClear(ArenaRulesProfile profile, Vector2 position, float radius)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (!ContainsFootprint(position, radius, profile.HalfWidth, profile.HalfHeight)) return false;
            for (int i = 0; i < profile.Pillars.Count; i++)
                if (CirclesOverlap(position, radius, profile.Pillars[i].Position, profile.Pillars[i].Radius)) return false;
            for (int i = 0; i < profile.Walls.Count; i++)
                if (CircleAabbOverlap(position, radius, profile.Walls[i].Position, profile.Walls[i].HalfSize)) return false;
            return true;
        }

        // Conservative capsule test for validation: square-expanded wall corners can reject a
        // very tight route, but cannot approve one through a solid wall.
        public static bool IsRouteSegmentClear(ArenaRulesProfile profile, Vector2 start, Vector2 end, float radius)
        {
            if (!IsFootprintClear(profile, start, radius) || !IsFootprintClear(profile, end, radius)) return false;
            for (int i = 0; i < profile.Pillars.Count; i++)
            {
                ArenaPillarDefinition pillar = profile.Pillars[i];
                float expanded = radius + pillar.Radius;
                if (PointSegmentDistanceSquared(pillar.Position, start, end) < expanded * expanded) return false;
            }
            for (int i = 0; i < profile.Walls.Count; i++)
            {
                ArenaWallDefinition wall = profile.Walls[i];
                if (SegmentIntersectsAabb(start, end, wall.Position, wall.HalfSize + Vector2.one * radius)) return false;
            }
            return true;
        }

        static bool SegmentIntersectsAabb(Vector2 start, Vector2 end, Vector2 center, Vector2 halfSize)
        {
            float first = 0f, last = 1f;
            Vector2 delta = end - start;
            return Clip(start.x, delta.x, center.x - halfSize.x, center.x + halfSize.x, ref first, ref last) &&
                Clip(start.y, delta.y, center.y - halfSize.y, center.y + halfSize.y, ref first, ref last);
        }

        static bool Clip(float origin, float delta, float minimum, float maximum, ref float first, ref float last)
        {
            if (delta == 0f) return origin >= minimum && origin <= maximum;
            float near = (minimum - origin) / delta, far = (maximum - origin) / delta;
            if (near > far) { float temporary = near; near = far; far = temporary; }
            first = Mathf.Max(first, near);
            last = Mathf.Min(last, far);
            return first <= last;
        }
    }

    public static class ArenaRandom
    {
        public const uint DefaultSeed = 0x6d2b79f5u;
        public static uint NormalizeSeed(uint seed) => seed == 0u ? DefaultSeed : seed;

        // xorshift32: exactly 32-bit unsigned state, including after snapshot restoration.
        public static uint Next(ref uint state)
        {
            uint value = NormalizeSeed(state);
            unchecked { value ^= value << 13; value ^= value >> 17; value ^= value << 5; }
            state = value;
            return value;
        }

        public static int NextIndex(ref uint state, int count)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            return (int)(Next(ref state) % (uint)count);
        }

        // Every consumed blue pickup advances once, even when no candidate is safe.
        public static int TeleportStartIndex(ref uint state, int candidateCount)
        {
            if (candidateCount < 0) throw new ArgumentOutOfRangeException(nameof(candidateCount));
            uint value = Next(ref state);
            return candidateCount == 0 ? -1 : (int)(value % (uint)candidateCount);
        }

        // A successful generation spends exactly two values (location, then equally weighted
        // green/blue/purple type). Empty sets spend none and must not allocate a pickup ID.
        public static bool TryChoosePickup(ref uint state, int candidateCount, out int location, out int kind)
        {
            if (candidateCount < 0) throw new ArgumentOutOfRangeException(nameof(candidateCount));
            location = kind = -1;
            if (candidateCount == 0) return false;
            location = NextIndex(ref state, candidateCount);
            kind = NextIndex(ref state, 3);
            return true;
        }
    }
}
