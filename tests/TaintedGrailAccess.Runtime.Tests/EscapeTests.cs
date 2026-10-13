using System;
using System.Collections.Generic;
using TaintedGrailAccess.Graph;
using TaintedGrailAccess.Runtime;

namespace TaintedGrailAccess.Runtime.Tests
{
    internal static class EscapeTests
    {
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private sealed class Speech : ISpeechOutput
        { public void Speak(string text, bool interrupt) { } public void Stop() { } }
        private sealed class Screen : AccessibleScreen
        {
            public string Name;
            public int Priority;
            public bool Unfocused, Raw;
            public Func<bool> Active, Back;
            public override string Id => Name;
            public override string ScreenName => Name;
            public override int Layer => Priority;
            public override bool StartUnfocused => Unfocused;
            public override bool CapturesRawInput => Raw;
            public override bool IsActive() => Active();
            public override bool OnBack() => Back != null && Back();
            public override void Build(GraphBuilder builder)
            {
                builder.Add(ControlId.Structural("continue"), new NodeVtable(new NodeAnnouncement(() => "Продолжить")));
            }
        }
        private sealed class Rig : IDisposable
        {
            public bool MenuOpen, ThrowAfterClose;
            public int Closes, NativeOpens;
            public readonly PhysicalInputGate Gate = new PhysicalInputGate();
            public readonly Screen Menu;
            public readonly AccessibilityRuntime Runtime;
            public Rig(bool initiallyOpen = false)
            {
                MenuOpen = initiallyOpen;
                var faults = new FaultBoundary(new NullLog());
                var screens = new ScreenManager(faults);
                screens.Register(new Screen { Name = "game", Active = () => true, Unfocused = true });
                Menu = new Screen { Name = "menu", Priority = 1, Active = () => MenuOpen, Back = () =>
                {
                    Closes++; MenuOpen = false;
                    if (ThrowAfterClose) throw new InvalidOperationException("after original close");
                    return true;
                } };
                screens.Register(Menu);
                Runtime = new AccessibilityRuntime(screens, new Navigator(new Speech(), new Localizer(new Dictionary<string, string>
                { ["search.cleared"] = "Поиск сброшен", ["search.none"] = "Нет совпадений: {0}" }), faults), new InputArbitration());
                Poll(0, false);
            }
            public void Poll(int frame, bool held)
            {
                Gate.Poll(frame, frame * 0.01, true, held ? new[] { new HeldKey(27, new KeyChord(UiKey.Escape)) } : Array.Empty<HeldKey>(), Runtime, () => false);
            }
            public void NativeOpenIfUnclaimed()
            {
                if (!Gate.Suppress(new KeyChord(UiKey.Escape))) { NativeOpens++; MenuOpen = true; }
            }
            public void Dispose() { Gate.Release(); Runtime.Dispose(); }
        }

        public static void NativeMenuOpenerDoesNotCloseOnHold()
        {
            using (var rig = new Rig())
            {
                rig.Poll(1, true);
                Check(!rig.Gate.Suppress(new KeyChord(UiKey.Escape)), "Unfocused game receives the first menu-opening Escape.");
                rig.NativeOpenIfUnclaimed(); rig.Poll(2, true); rig.Poll(3, true);
                Check(rig.MenuOpen && rig.Closes == 0, "A native menu opened after the first poll must not close on the next held frame.");
                Check(rig.Gate.Suppress(new KeyChord(UiKey.Escape)), "Held opener is acquired without another back action.");
                Check(rig.Gate.SuppressMappedAction(new[] { new KeyChord(UiKey.Escape) }), "Native Menu/Cancel action cannot replay the held opener.");
                rig.Poll(4, false); Check(rig.Gate.Suppress(new KeyChord(UiKey.Escape)), "Acquired release stays suppressed for this frame.");
                rig.Poll(5, true); rig.Poll(6, true); rig.NativeOpenIfUnclaimed();
                Check(!rig.MenuOpen && rig.Closes == 1 && rig.NativeOpens == 1, "A separate Escape closes once; holding cannot reopen the game menu.");
                rig.Poll(7, false); rig.Poll(8, false);
                Check(!rig.Gate.Suppress(new KeyChord(UiKey.Escape)), "Release clears the acquired chord.");
                rig.Poll(9, true); rig.NativeOpenIfUnclaimed(); rig.Poll(10, true);
                Check(rig.MenuOpen && rig.NativeOpens == 2 && rig.Closes == 1, "The next new physical press can open the menu again.");
            }
        }

        public static void SearchCancelNeedsAnotherPressForBack()
        {
            using (var rig = new Rig(true))
            {
                rig.Runtime.Update(1, new[] { new KeyChord(UiKey.None, text: "п") });
                Check(rig.Runtime.Navigator.SearchQuery.Length > 0, "Setup starts a search.");
                rig.Runtime.Update(2, new[] { new KeyChord(UiKey.Escape) });
                rig.Runtime.Update(3, new[] { new KeyChord(UiKey.Escape, isRepeat: true) });
                Check(rig.Runtime.Navigator.SearchQuery == "" && rig.MenuOpen && rig.Closes == 0, "Clearing search does not dismiss the menu while Escape is held.");
                rig.Runtime.Update(4, new[] { new KeyChord(UiKey.Escape) });
                Check(!rig.MenuOpen && rig.Closes == 1, "Another separate press performs back.");
            }
        }

        public static void RepeatRespectsCaptureAndModifiers()
        {
            using (var rig = new Rig(true))
            {
                long frame = 0;
                foreach (var modifier in new[] { Modifiers.Control, Modifiers.Shift, Modifiers.Alt, Modifiers.Reserved })
                {
                    var chord = new KeyChord(UiKey.Escape, modifier, isRepeat: true);
                    rig.Runtime.Update(++frame, new[] { chord });
                    Check(!rig.Runtime.Input.IsClaimed(chord), "Modified Escape stays with game/OS: " + modifier);
                }
                var repeat = new KeyChord(UiKey.Escape, isRepeat: true);
                rig.Runtime.Update(++frame, new[] { repeat }, realTextFieldActive: true);
                Check(!rig.Runtime.Input.IsClaimed(repeat), "Native text retains Escape, including repeat.");
                rig.Menu.Raw = true; rig.Runtime.Update(++frame, new[] { repeat });
                Check(!rig.Runtime.Input.IsClaimed(repeat), "Raw capture retains Escape, including repeat.");
                Check(rig.MenuOpen && rig.Closes == 0, "Capture and modifier checks never run an original back handler.");
            }
        }

        public static void ThrowAfterBackDoesNotReplay()
        {
            using (var rig = new Rig(true))
            {
                rig.ThrowAfterClose = true;
                rig.Poll(1, true); rig.Poll(2, true); rig.NativeOpenIfUnclaimed();
                Check(rig.Closes == 1 && !rig.MenuOpen && rig.NativeOpens == 0, "An exception after closing still acquires the press; holding must not reopen or close again.");
            }
        }
    }
}
