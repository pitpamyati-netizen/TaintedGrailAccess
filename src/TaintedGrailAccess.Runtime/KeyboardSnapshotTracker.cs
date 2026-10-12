using System;
using System.Collections.Generic;

namespace TaintedGrailAccess.Runtime
{
    /// <summary>A held physical key and its current chord, supplied by the host without UI references.</summary>
    public struct HeldKey
    {
        public int PhysicalKey { get; }
        public KeyChord Chord { get; }

        /// <summary>PhysicalKey distinguishes keys such as Return and keypad Enter.</summary>
        public HeldKey(int physicalKey, KeyChord chord)
        { PhysicalKey = physicalKey; Chord = chord; }
    }

    /// <summary>
    /// Converts live held-key snapshots into presses and repeats. It does not read a keyboard,
    /// claim input, or prove the order of the game's input paths. Hosts must supply monotonic time.
    /// </summary>
    public sealed class KeyboardSnapshotTracker
    {
        private sealed class HeldState
        {
            public bool Blocked;
            public double NextRepeat;
        }

        private readonly Dictionary<int, HeldState> _held = new Dictionary<int, HeldState>();
        private readonly double _repeatDelay;
        private readonly double _repeatInterval;
        private bool _wasAvailable;
        private long _lastFrame = long.MinValue;
        private double _lastTime = double.NegativeInfinity;

        /// <summary>Navigation repeat timing in seconds; action repeats are emitted only as IsRepeat.</summary>
        public KeyboardSnapshotTracker(double repeatDelay = 0.4, double repeatInterval = 0.08)
        {
            if (!Finite(repeatDelay) || repeatDelay < 0) throw new ArgumentOutOfRangeException(nameof(repeatDelay));
            if (!Finite(repeatInterval) || repeatInterval <= 0) throw new ArgumentOutOfRangeException(nameof(repeatInterval));
            _repeatDelay = repeatDelay;
            _repeatInterval = repeatInterval;
        }

        /// <summary>
        /// Samples at most once per frame. On startup, window return, or raw/text capture exit,
        /// already held keys wait for release. Modifier changes never create a new physical press.
        /// </summary>
        public IReadOnlyList<KeyChord> Sample(long frame, double time, bool windowFocused, bool standDown,
            IEnumerable<HeldKey> snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (!Finite(time) || time < _lastTime) throw new ArgumentOutOfRangeException(nameof(time));
            if (frame < _lastFrame) throw new ArgumentOutOfRangeException(nameof(frame));
            if (frame == _lastFrame) return Array.Empty<KeyChord>();

            // Validate before changing state: a torn or invalid snapshot cannot become a release.
            var keys = new List<HeldKey>();
            var present = new HashSet<int>();
            foreach (var key in snapshot)
            {
                if (!present.Add(key.PhysicalKey)) throw new ArgumentException("Duplicate physical key.", nameof(snapshot));
                if (key.Chord.Key == UiKey.None || key.Chord.Text != null)
                    throw new ArgumentException("Only named navigation keys are supported; text belongs to the host.", nameof(snapshot));
                keys.Add(key);
            }

            _lastFrame = frame;
            _lastTime = time;
            bool available = windowFocused && !standDown;
            var released = new List<int>();
            foreach (var key in _held.Keys) if (!present.Contains(key)) released.Add(key);
            foreach (var key in released) _held.Remove(key);

            var result = new List<KeyChord>();
            foreach (var key in keys)
            {
                HeldState state;
                bool newlyHeld = !_held.TryGetValue(key.PhysicalKey, out state);
                if (newlyHeld)
                {
                    state = new HeldState { NextRepeat = time + _repeatDelay };
                    _held.Add(key.PhysicalKey, state);
                }
                if (!available || !_wasAvailable) state.Blocked = true;
                if (state.Blocked) continue;

                bool repeat = !newlyHeld;
                // Actions must be offered as repeats on every held frame so the ownership layer
                // can prevent another input path from acting, without invoking the action again.
                if (repeat && IsNavigation(key.Chord.Key))
                {
                    if (time < state.NextRepeat) continue;
                    state.NextRepeat = time + _repeatInterval;
                }
                result.Add(new KeyChord(key.Chord.Key, key.Chord.Modifiers, isRepeat: repeat));
            }
            _wasAvailable = available;
            return result;
        }

        /// <summary>Forgetting the host also requires a new neutral snapshot before accepting held keys.</summary>
        public void Reset()
        {
            _held.Clear();
            _wasAvailable = false;
            _lastFrame = long.MinValue;
            _lastTime = double.NegativeInfinity;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool IsNavigation(UiKey key)
        {
            return key == UiKey.Up || key == UiKey.Right || key == UiKey.Down || key == UiKey.Left ||
                key == UiKey.Tab || key == UiKey.Home || key == UiKey.End;
        }
    }
}
