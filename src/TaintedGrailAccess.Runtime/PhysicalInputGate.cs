using System;
using System.Collections.Generic;
using System.Linq;

namespace TaintedGrailAccess.Runtime
{
    /// <summary>
    /// Keeps a consumed physical press owned until release, including frames without a
    /// navigation repeat and a press that opens raw capture. Stores only key data.
    /// </summary>
    public sealed class PhysicalInputGate
    {
        private readonly KeyboardSnapshotTracker _tracker = new KeyboardSnapshotTracker();
        private readonly KeyboardSnapshotTracker _textTracker = new KeyboardSnapshotTracker();
        private readonly Dictionary<int, KeyChord> _owned = new Dictionary<int, KeyChord>();
        private readonly HashSet<KeyChord> _claims = new HashSet<KeyChord>();
        private long _frame = long.MinValue;
        private bool _focused;
        private bool _exclusive;
        private bool _raw;

        /// <summary>Polls once before any game input path; liveCapture is re-read after actions.</summary>
        public void Poll(long frame, double time, bool focused, IEnumerable<HeldKey> snapshot,
            AccessibilityRuntime runtime, Func<bool> liveCapture, Func<KeyChord, bool> nativeTextInput = null)
        {
            if (frame == _frame) return;
            var held = snapshot.ToArray();
            bool capture = liveCapture();
            var chords = _tracker.Sample(frame, time, focused, capture, held);
            var textChords = _textTracker.Sample(frame, time, focused, !capture || nativeTextInput == null, held)
                .Where(chord => chord.Modifiers == Modifiers.Control && (chord.Key == UiKey.F1 || chord.Key == UiKey.Enter));
            _frame = frame;
            _focused = focused;
            _claims.Clear();
            if (!focused)
            {
                runtime.Input.Release();
                _exclusive = false;
                return;
            }
            // Release events belong to the same press. Suppress that press's release for
            // this frame, without changing the game's own held-key dictionary.
            foreach (var pair in _owned.ToArray())
            {
                var present = held.FirstOrDefault(key => key.PhysicalKey == pair.Key);
                if (!held.Any(key => key.PhysicalKey == pair.Key))
                { _claims.Add(pair.Value); _owned.Remove(pair.Key); }
                else if (present.Chord.Equals(pair.Value)) _claims.Add(pair.Value);
            }
            runtime.UpdateLive(frame, chords.Concat(textChords), liveCapture, nativeTextInput);
            foreach (var key in held)
                if (runtime.Input.IsClaimed(key.Chord))
                { _owned[key.PhysicalKey] = key.Chord; _claims.Add(key.Chord); }
            _exclusive = runtime.Input.Exclusive;
            _raw = runtime.Input.RawCapture;
        }

        /// <summary>Suppresses only this frame's claims, or all application keys in a mod modal.</summary>
        public bool Suppress(KeyChord chord, bool ownAction = false)
        { return _focused && !ownAction && (_claims.Contains(chord) || (_exclusive && !_raw)); }

        /// <summary>A mapped action is suppressed only if every active contributor is owned.</summary>
        public bool SuppressMappedAction(IEnumerable<KeyChord> activeContributors, bool ownAction = false)
        {
            if (!_focused || ownAction) return false;
            if (_exclusive && !_raw) return true;
            var contributors = activeContributors.ToArray();
            return contributors.Length > 0 && contributors.All(chord => Suppress(chord));
        }

        /// <summary>Blur clears frame claims; a previously consumed held press still waits for release.</summary>
        public void Blur()
        { _focused = false; _claims.Clear(); _exclusive = false; _tracker.Reset(); _textTracker.Reset(); _frame = long.MinValue; }

        /// <summary>Disabling or leaving the supported scene returns all keys to the game.</summary>
        public void Release()
        { Blur(); _owned.Clear(); _raw = false; }
    }
}
