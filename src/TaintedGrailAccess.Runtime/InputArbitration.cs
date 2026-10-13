using System;
using System.Collections.Generic;
using System.Linq;

namespace TaintedGrailAccess.Runtime
{
    // Per-frame ownership model. Physical Rewired/EventSystem/game-handler hooks
    // live in the fingerprinted game host; isolated model tests alone do not prove them.
    public sealed class InputArbitration
    {
        private readonly HashSet<KeyChord> _claims = new HashSet<KeyChord>();
        private int _ownActionDepth;
        public long Frame { get; private set; }
        public bool Exclusive { get; private set; }
        public bool RawCapture { get; private set; }
        public bool OwnAction { get { return _ownActionDepth > 0; } }
        public void BeginFrame(long frame, bool exclusive, bool rawCapture)
        {
            Frame = frame;
            Exclusive = exclusive && !rawCapture;
            RawCapture = rawCapture;
            _claims.Clear();
        }
        public void UpdateOwnership(bool exclusive, bool rawCapture)
        {
            Exclusive = exclusive && !rawCapture;
            RawCapture = rawCapture;
            // Keep chords already consumed before a key-binding dialog opened.
            // BeginFrame clears them on the following frame.
        }
        /// <summary>Native text commands may claim their explicitly handled chord during capture.</summary>
        public void Claim(KeyChord chord, bool nativeTextCommand = false) { if (!RawCapture || nativeTextCommand) _claims.Add(chord); }
        /// <summary>Reports an explicit consumed chord, independently of modal policy.</summary>
        public bool IsClaimed(KeyChord chord) { return _claims.Contains(chord); }
        public bool ShouldSuppressGameChord(KeyChord chord)
        { return !OwnAction && (_claims.Contains(chord) || (!RawCapture && Exclusive)); }
        public bool ShouldSuppressToolkitSubmit(bool accessibilityHasFocus)
        {
            return !OwnAction && (_claims.Any(chord => chord.Key == UiKey.Enter || chord.Key == UiKey.Space || chord.Key == UiKey.Backspace)
                || (!RawCapture && (Exclusive || accessibilityHasFocus)));
        }
        public bool ShouldSuppressToolkitSubmit(KeyChord chord, bool accessibilityHasFocus)
        { return ShouldSuppressGameChord(chord) || (!OwnAction && !RawCapture && accessibilityHasFocus); }
        public IDisposable EnterOwnAction()
        {
            _ownActionDepth++;
            return new Scope(this);
        }
        public void Release()
        {
            _claims.Clear();
            Exclusive = false;
            RawCapture = false;
        }
        private sealed class Scope : IDisposable
        {
            private InputArbitration _owner;
            public Scope(InputArbitration owner) { _owner = owner; }
            public void Dispose()
            {
                if (_owner == null) return;
                _owner._ownActionDepth--;
                _owner = null;
            }
        }
    }
}
