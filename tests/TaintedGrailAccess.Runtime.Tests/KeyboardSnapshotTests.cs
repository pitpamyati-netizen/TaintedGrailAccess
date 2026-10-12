using System;
using System.Collections.Generic;
using System.Linq;
using TaintedGrailAccess.Runtime;

namespace TaintedGrailAccess.Runtime.Tests
{
    internal static class KeyboardSnapshotTests
    {
        public static void PressHoldRelease()
        {
            var tracker = Ready();
            var press = Sample(tracker, 1, 0.1, UiKey.Enter).Single();
            Check(!press.IsRepeat, "First press must be an action.");
            Check(Sample(tracker, 2, 0.11, UiKey.Enter).Single().IsRepeat, "Held action must only be offered as repeat.");
            Sample(tracker, 3, 0.12);
            Check(!Sample(tracker, 4, 0.13, UiKey.Enter).Single().IsRepeat, "Release must permit the next action.");
        }

        public static void StartupHeldKey()
        {
            var tracker = new KeyboardSnapshotTracker();
            Check(Sample(tracker, 0, 0, UiKey.Enter).Count == 0, "Startup cannot activate a held key.");
            Check(Sample(tracker, 1, 1, UiKey.Enter).Count == 0, "Waiting does not replace release.");
            Sample(tracker, 2, 1.1);
            Check(!Sample(tracker, 3, 1.2, UiKey.Enter).Single().IsRepeat, "Released startup key may act normally.");
        }

        public static void NavigationRepeat()
        {
            var tracker = Ready();
            Check(Sample(tracker, 1, 0.1, UiKey.Down).Count == 1, "Arrow acts immediately.");
            Check(Sample(tracker, 2, 0.49, UiKey.Down).Count == 0, "Arrow waits for repeat delay.");
            Check(Sample(tracker, 3, 0.5, UiKey.Down).Single().IsRepeat, "Arrow repeats at delay.");
            Check(Sample(tracker, 4, 0.51, UiKey.Down).Count == 0, "Arrow waits for repeat interval.");
            Check(Sample(tracker, 5, 2, UiKey.Down).Count == 1, "Slow frame must not produce a burst of catch-up actions.");
        }

        public static void ModifierIsNotNewPress()
        {
            var tracker = Ready();
            tracker.Sample(1, 0.1, true, false, new[] { Held(13, UiKey.Enter) });
            var result = tracker.Sample(2, 0.2, true, false, new[] { Held(13, UiKey.Enter, Modifiers.Shift) }).Single();
            Check(result.IsRepeat && result.Shift, "Shift added to held Enter cannot create Shift+Enter activation.");
        }

        public static void WindowReturnRequiresRelease()
        {
            var tracker = Ready();
            tracker.Sample(1, 0.1, true, false, new[] { Held(13, UiKey.Enter) });
            Check(tracker.Sample(2, 0.2, false, false, Array.Empty<HeldKey>()).Count == 0, "Blur produces no keys.");
            Check(tracker.Sample(3, 0.3, true, false, new[] { Held(13, UiKey.Enter) }).Count == 0, "A key pressed outside the window must not activate on return.");
            Check(Sample(tracker, 4, 0.4, UiKey.Enter).Count == 0, "Held returning key remains blocked.");
            Sample(tracker, 5, 0.5);
            Check(Sample(tracker, 6, 0.6, UiKey.Enter).Count == 1, "Release restores input.");
        }

        public static void RawAndTextCapture()
        {
            var tracker = Ready();
            tracker.Sample(1, 0.1, true, false, new[] { Held(13, UiKey.Enter) });
            Check(tracker.Sample(2, 0.2, true, true, new[] { Held(13, UiKey.Enter) }).Count == 0, "Capture receives input without tracker actions.");
            Check(tracker.Sample(3, 0.3, true, false, new[] { Held(13, UiKey.Enter) }).Count == 0, "Capture exit cannot replay its closing Enter.");
            Sample(tracker, 4, 0.4);
            Check(Sample(tracker, 5, 0.5, UiKey.Enter).Count == 1, "Release after capture enables a new press.");
        }

        public static void MultipleGuiPasses()
        {
            var tracker = Ready();
            Check(Sample(tracker, 1, 0.1, UiKey.Enter).Count == 1, "First GUI pass sees the press.");
            Check(Sample(tracker, 1, 0.1, UiKey.Enter).Count == 0, "Another GUI pass cannot repeat it in one frame.");
        }

        public static void PhysicalIdentity()
        {
            var tracker = Ready();
            tracker.Sample(1, 0.1, true, false, new[] { Held(13, UiKey.Enter) });
            var result = tracker.Sample(2, 0.2, true, false, new[] { Held(13, UiKey.Enter), Held(271, UiKey.Enter) });
            Check(result.Count == 2 && result[0].IsRepeat && !result[1].IsRepeat, "Return and keypad Enter have separate physical lifetimes.");
        }

        public static void InvalidSnapshotDoesNotRelease()
        {
            var tracker = Ready();
            Sample(tracker, 1, 0.1, UiKey.Enter);
            Throws(() => tracker.Sample(2, 0.2, true, false, new[] { Held(13, UiKey.Enter), Held(13, UiKey.Enter) }));
            Check(Sample(tracker, 2, 0.2, UiKey.Enter).Single().IsRepeat, "Rejected snapshot must not fabricate release.");
            Throws(() => tracker.Sample(3, 0.1, true, false, Array.Empty<HeldKey>()));
            Throws(() => tracker.Sample(3, double.NaN, true, false, Array.Empty<HeldKey>()));
            Throws(() => tracker.Sample(3, 0.3, true, false, new[] { new HeldKey(65, new KeyChord(UiKey.None, text: "a")) }));
            Check(Sample(tracker, 3, 0.3, UiKey.Enter).Single().IsRepeat, "Invalid time or text cannot reset held action.");
        }

        public static void ResetRequiresNeutral()
        {
            var tracker = Ready();
            Sample(tracker, 1, 0.1, UiKey.Enter);
            tracker.Reset();
            Check(Sample(tracker, 0, 0, UiKey.Enter).Count == 0, "Reset cannot turn a held key into a new action.");
        }

        private static KeyboardSnapshotTracker Ready()
        {
            var tracker = new KeyboardSnapshotTracker();
            Sample(tracker, 0, 0);
            return tracker;
        }
        private static HeldKey Held(int physical, UiKey key, Modifiers modifiers = Modifiers.None)
        { return new HeldKey(physical, new KeyChord(key, modifiers)); }
        private static IReadOnlyList<KeyChord> Sample(KeyboardSnapshotTracker tracker, long frame, double time, params UiKey[] keys)
        { return tracker.Sample(frame, time, true, false, keys.Select(key => Held(key == UiKey.Enter ? 13 : (int)key + 100, key))); }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Throws(Action action)
        {
            try { action(); } catch (ArgumentException) { return; }
            throw new Exception("Invalid snapshot must be rejected.");
        }
    }
}
