using System;
using System.Collections.Generic;
using System.Linq;
using TaintedGrailAccess.Graph;
using TaintedGrailAccess.Runtime;

namespace TaintedGrailAccess.Runtime.Tests
{
    internal static class CombatReadoutTests
    {
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private sealed class Speech : ISpeechOutput
        {
            public readonly List<(string Text, bool Interrupt)> Lines = new List<(string, bool)>();
            public void Speak(string text, bool interrupt) => Lines.Add((text, interrupt));
            public void Stop() { }
        }
        private sealed class Screen : AccessibleScreen
        {
            public bool Raw, Info = true;
            public int Reads, Actions;
            public string Value = "100/100";
            public Speech Speech;
            public override string Id => "combat";
            public override string ScreenName => "combat";
            public override bool CapturesRawInput => Raw;
            public override bool IsActive() => true;
            public override void Build(GraphBuilder builder)
            {
                builder.Add(ControlId.Structural("card"), new NodeVtable(new NodeAnnouncement(() => "Карта")) { OnActivate = () => Actions++ });
            }
            public override bool OnContextualInput(KeyChord chord) => Info && CombatReadoutKeys.Handle(chord,
                kind => { Reads++; return Value; }, Speech, "Не показано");
        }
        private sealed class Rig : IDisposable
        {
            public readonly Speech Speech = new Speech();
            public readonly Screen Screen = new Screen();
            public readonly PhysicalInputGate Gate = new PhysicalInputGate();
            public readonly AccessibilityRuntime Runtime;
            public bool TextCapture;
            public Rig()
            {
                Screen.Speech = Speech;
                var faults = new FaultBoundary(new NullLog());
                var screens = new ScreenManager(faults); screens.Register(Screen);
                Runtime = new AccessibilityRuntime(screens, new Navigator(Speech, new Localizer(new Dictionary<string, string>()), faults), new InputArbitration());
                Poll(0); Speech.Lines.Clear();
            }
            public void Poll(int frame, params HeldKey[] held) => Gate.Poll(frame, frame * 0.01, true, held, Runtime, () => TextCapture);
            public void Dispose() { Gate.Release(); Runtime.Dispose(); }
        }
        private static HeldKey Held(UiKey key, Modifiers modifiers) => new HeldKey((int)key, new KeyChord(key, modifiers));

        public static void ExactChordMatrix()
        {
            var bindings = new Dictionary<KeyChord, CombatReadout>
            {
                [new KeyChord(UiKey.H, Modifiers.Control)] = CombatReadout.Health,
                [new KeyChord(UiKey.Y, Modifiers.Control)] = CombatReadout.Energy,
                [new KeyChord(UiKey.B, Modifiers.Control)] = CombatReadout.Barrier,
                [new KeyChord(UiKey.R, Modifiers.Control)] = CombatReadout.ClassResource,
                [new KeyChord(UiKey.P, Modifiers.Control)] = CombatReadout.Effects,
                [new KeyChord(UiKey.I, Modifiers.Control)] = CombatReadout.Intents,
                [new KeyChord(UiKey.I, Modifiers.Alt)] = CombatReadout.ShortIntents,
                [new KeyChord(UiKey.H, Modifiers.Alt)] = CombatReadout.Hand
            };
            foreach (UiKey key in Enum.GetValues(typeof(UiKey)))
                for (int mask = 0; mask < 16; mask++)
                {
                    var chord = new KeyChord(key, (Modifiers)mask);
                    CombatReadout actual, expected;
                    var found = CombatReadoutKeys.TryGet(chord, out actual);
                    Check(found == bindings.TryGetValue(chord, out expected) && (!found || actual == expected), "Only an exact assigned info chord is claimed: " + key + "/" + mask);
                }
        }
        public static void PassiveFreshAndHeld()
        {
            using (var rig = new Rig())
            {
                var key = Held(UiKey.H, Modifiers.Control);
                var focus = rig.Runtime.Navigator.Current.Id;
                rig.Poll(1, key); rig.Poll(2, key);
                Check(rig.Screen.Reads == 1 && rig.Speech.Lines.Count == 1 && rig.Speech.Lines.Single().Interrupt, "One held info press reads once and interrupts speech.");
                Check(rig.Speech.Lines.Single().Text == "100/100", "Read current displayed value.");
                Check(rig.Gate.Suppress(key.Chord) && rig.Gate.SuppressMappedAction(new[] { key.Chord }), "Own chord is suppressed in native paths.");
                Check(!rig.Gate.SuppressMappedAction(new[] { key.Chord, new KeyChord(UiKey.Y) }), "An independent unclaimed contributor remains available.");
                rig.Poll(3); rig.Screen.Value = "87/100"; rig.Poll(4, key);
                Check(rig.Screen.Reads == 2 && rig.Speech.Lines.Last().Text == "87/100", "Next press reads fresh health.");
                Check(rig.Screen.Actions == 0 && ControlId.SameIdentity(focus, rig.Runtime.Navigator.Current.Id), "Information never plays a card or moves focus.");
                rig.Poll(5); rig.Screen.Value = null; rig.Poll(6, Held(UiKey.Y, Modifiers.Control));
                Check(rig.Speech.Lines.Last().Text == "Не показано", "Absent visible value has a clear fallback.");
            }
        }
        public static void AltAndCaptureBoundaries()
        {
            using (var rig = new Rig())
            {
                rig.Poll(1, Held(UiKey.I, Modifiers.Alt));
                Check(rig.Screen.Reads == 1 && rig.Gate.Suppress(new KeyChord(UiKey.I, Modifiers.Alt)), "Explicit Alt+I reaches contextual information.");
                rig.Poll(2); rig.Poll(3, Held(UiKey.Down, Modifiers.Alt));
                Check(!rig.Gate.Suppress(new KeyChord(UiKey.Down, Modifiers.Alt)) && rig.Screen.Reads == 1, "Unknown Alt command stays with game/OS.");
                rig.Poll(4); rig.TextCapture = true; rig.Poll(5, Held(UiKey.H, Modifiers.Control));
                Check(rig.Screen.Reads == 1 && !rig.Gate.Suppress(new KeyChord(UiKey.H, Modifiers.Control)), "Real text input owns its keys.");
                rig.Poll(6); rig.TextCapture = false; rig.Poll(7); rig.Screen.Raw = true;
                rig.Poll(8, Held(UiKey.H, Modifiers.Alt));
                Check(rig.Screen.Reads == 1 && !rig.Gate.Suppress(new KeyChord(UiKey.H, Modifiers.Alt)), "Raw modal never invokes info readout.");
                rig.Poll(9); rig.Screen.Raw = false; rig.Screen.Info = false; rig.Poll(10, Held(UiKey.I, Modifiers.Alt));
                Check(!rig.Gate.Suppress(new KeyChord(UiKey.I, Modifiers.Alt)) && rig.Screen.Reads == 1, "A screen without combat info does not acquire these keys.");
                rig.Poll(11); rig.Screen.Info = true; rig.Poll(12, Held(UiKey.H, Modifiers.Control | Modifiers.Reserved));
                Check(!rig.Gate.Suppress(new KeyChord(UiKey.H, Modifiers.Control | Modifiers.Reserved)) && rig.Screen.Reads == 1, "Windows and NVDA modifier chords remain unclaimed.");
            }
        }
    }
}
