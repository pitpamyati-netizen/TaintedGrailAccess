using System;
using System.Collections.Generic;

namespace TaintedGrailAccess.Runtime
{
    public sealed class AccessibilityRuntime : IDisposable
    {
        public ScreenManager Screens { get; }
        public Navigator Navigator { get; }
        public InputArbitration Input { get; }
        public AccessibilityRuntime(ScreenManager screens, Navigator navigator, InputArbitration input)
        { Screens = screens; Navigator = navigator; Input = input; }

        // The game host MUST call this early enough that its global keymap has
        // not consumed this frame. The fingerprinted title host guards each input entry.
        public void Update(long frame, IEnumerable<KeyChord> chords, bool realTextFieldActive = false)
        { UpdateLive(frame, chords, () => realTextFieldActive); }

        /// <summary>Re-reads raw/text ownership after each action, including synchronous dialogs.</summary>
        public void UpdateLive(long frame, IEnumerable<KeyChord> chords, Func<bool> liveCapture)
        {
            Screens.Settle();
            // OnUpdate can open a dialog. Observe it before the first key action.
            Screens.Settle(false);
            Navigator.Attach(Screens.Current);
            Navigator.RealTextFieldActive = liveCapture();
            Navigator.Tick();
            var current = Screens.Current;
            Input.BeginFrame(frame, current != null && current.Screen.Exclusive && Navigator.IsSeated,
                Navigator.RealTextFieldActive || (current != null && current.Screen.CapturesRawInput));
            foreach (var chord in chords)
            {
                bool consumed;
                using (Input.EnterOwnAction()) consumed = Navigator.Handle(chord);
                if (consumed) Input.Claim(chord);
                // An action may synchronously replace the screen or open a modal.
                Screens.Settle(false);
                Navigator.RealTextFieldActive = liveCapture();
                Navigator.Attach(Screens.Current);
                Navigator.Tick();
                current = Screens.Current;
                Input.UpdateOwnership(current != null && current.Screen.Exclusive && Navigator.IsSeated,
                    Navigator.RealTextFieldActive || (current != null && current.Screen.CapturesRawInput));
            }
            Navigator.Tick();
        }

        public void Dispose()
        {
            Input.Release();
            Screens.Clear();
            Navigator.Attach(null);
        }
    }
}
