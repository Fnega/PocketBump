using System;
using UnityEngine;

namespace PocketBump
{
    public interface IDashKeyboardInput
    {
        int FrameCount { get; }
        bool IsHeld(KeyCode key);
        bool WasPressedThisFrame(KeyCode key);
    }

    // Latch once per render frame, consume once for each new local simulation tick.
    // Callers retain the returned input for retransmission/replay instead of resampling.
    public sealed class DashTickInputSampler
    {
        readonly DashControlBindings _bindings;
        readonly IDashKeyboardInput _keyboard;
        readonly bool[] _dashLatched = new bool[2];
        int _lastLatchFrame = int.MinValue;
        bool _routingConfigured;
        bool _netplayLocalSeatOnly;
        int _localSeat;

        public DashTickInputSampler() : this(new DashControlBindings(), new UnityKeyboard()) { }
        public DashTickInputSampler(DashControlBindings bindings) : this(bindings, new UnityKeyboard()) { }
        public DashTickInputSampler(DashControlBindings bindings, IDashKeyboardInput keyboard)
        {
            _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        }

        // Use at match/epoch transitions and when abandoning pending gameplay input.
        // Do not re-latch the old GetKeyDown if restarted again within this same frame.
        public void Clear()
        {
            _dashLatched[0] = _dashLatched[1] = false;
            _lastLatchFrame = _keyboard.FrameCount;
        }

        public void LatchUpdate(bool netplayLocalSeatOnly, int localSeat)
        {
            RequireSeat(localSeat);
            if (_routingConfigured && (_netplayLocalSeatOnly != netplayLocalSeatOnly || _localSeat != localSeat))
                _dashLatched[0] = _dashLatched[1] = false;
            _routingConfigured = true;
            _netplayLocalSeatOnly = netplayLocalSeatOnly;
            _localSeat = localSeat;
            if (_lastLatchFrame == _keyboard.FrameCount) return;
            _lastLatchFrame = _keyboard.FrameCount;

            if (netplayLocalSeatOnly)
            {
                _dashLatched[localSeat] |= _keyboard.WasPressedThisFrame(_bindings.Dash(0));
                return;
            }
            for (int seat = 0; seat < 2; seat++)
                _dashLatched[seat] |= _keyboard.WasPressedThisFrame(_bindings.Dash(seat));
        }

        public DashArenaInput SampleLocal(int seat)
        {
            RequireSeat(seat);
            if (_routingConfigured && _netplayLocalSeatOnly)
                throw new InvalidOperationException("LAN input must use SampleNetplaySeat for the assigned seat only.");
            return SampleSeat(seat, seat);
        }

        public DashArenaInput SampleNetplaySeat(int assignedSeat)
        {
            RequireSeat(assignedSeat);
            if (_routingConfigured && (!_netplayLocalSeatOnly || assignedSeat != _localSeat))
                throw new InvalidOperationException("Input routing does not match the assigned LAN seat.");
            // Both computers use their own P1 keys, even when the joiner owns seat one.
            return SampleSeat(0, assignedSeat);
        }

        DashArenaInput SampleSeat(int bindingIndex, int logicalSeat)
        {
            bool dash = _dashLatched[logicalSeat];
            _dashLatched[logicalSeat] = false;
            return new DashArenaInput { Move = ReadMove(bindingIndex), DashPressed = dash };
        }

        public Vector2 ReadMove(int bindingIndex)
        {
            RequireSeat(bindingIndex);
            Vector2 move = bindingIndex == 0
                ? new Vector2((_keyboard.IsHeld(KeyCode.D) ? 1f : 0f) - (_keyboard.IsHeld(KeyCode.A) ? 1f : 0f),
                    (_keyboard.IsHeld(KeyCode.W) ? 1f : 0f) - (_keyboard.IsHeld(KeyCode.S) ? 1f : 0f))
                : new Vector2((_keyboard.IsHeld(KeyCode.RightArrow) ? 1f : 0f) - (_keyboard.IsHeld(KeyCode.LeftArrow) ? 1f : 0f),
                    (_keyboard.IsHeld(KeyCode.UpArrow) ? 1f : 0f) - (_keyboard.IsHeld(KeyCode.DownArrow) ? 1f : 0f));
            return move.sqrMagnitude > 0f ? move.normalized : Vector2.zero;
        }

        static void RequireSeat(int seat)
        {
            if (seat < 0 || seat > 1) throw new ArgumentOutOfRangeException(nameof(seat));
        }

        sealed class UnityKeyboard : IDashKeyboardInput
        {
            public int FrameCount => Time.frameCount;
            public bool IsHeld(KeyCode key) => Input.GetKey(key);
            public bool WasPressedThisFrame(KeyCode key) => Input.GetKeyDown(key);
        }
    }
}
