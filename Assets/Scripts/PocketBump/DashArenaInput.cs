using System;
using UnityEngine;

namespace PocketBump
{
    // New rules vocabulary. Deliberately not interpreted by the released v6 codec.
    public struct DashArenaInput : IEquatable<DashArenaInput>
    {
        public Vector2 Move;
        public bool DashPressed;
        public static DashArenaInput Neutral => default;
        public DashArenaInput Predict() => new DashArenaInput { Move = Move };

        // Vector2.Equals compares its exact components, unlike Unity's approximate ==.
        public bool EqualsInput(DashArenaInput other) => Move.Equals(other.Move) && DashPressed == other.DashPressed;
        public bool Equals(DashArenaInput other) => EqualsInput(other);
        public override bool Equals(object other) => other is DashArenaInput input && EqualsInput(input);
        public override int GetHashCode()
        {
            unchecked { return (Move.GetHashCode() * 397) ^ (DashPressed ? 1 : 0); }
        }

        public byte Pack()
        {
            byte bits = 0;
            if (Move.x > .5f) bits |= 1;
            else if (Move.x < -.5f) bits |= 2;
            if (Move.y > .5f) bits |= 4;
            else if (Move.y < -.5f) bits |= 8;
            if (DashPressed) bits |= 16;
            return bits;
        }

        public static bool TryUnpack(byte bits, out DashArenaInput input)
        {
            input = default;
            if ((bits & 0xe0) != 0 || (bits & 3) == 3 || (bits & 12) == 12) return false;
            input.Move = new Vector2(((bits & 1) != 0 ? 1 : 0) - ((bits & 2) != 0 ? 1 : 0),
                ((bits & 4) != 0 ? 1 : 0) - ((bits & 8) != 0 ? 1 : 0));
            if (input.Move.sqrMagnitude > 1f) input.Move.Normalize();
            input.DashPressed = (bits & 16) != 0;
            return true;
        }

        public static DashArenaInput Unpack(byte bits)
        {
            if (!TryUnpack(bits, out DashArenaInput input))
                throw new ArgumentOutOfRangeException(nameof(bits), "Invalid movement/Dash input bits.");
            return input;
        }
    }
}
