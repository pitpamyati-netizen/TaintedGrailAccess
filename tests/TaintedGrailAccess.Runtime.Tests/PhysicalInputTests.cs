using System;
using System.Collections.Generic;
using TaintedGrailAccess.Graph;
using TaintedGrailAccess.Runtime;

namespace TaintedGrailAccess.Runtime.Tests
{
    internal static class PhysicalInputTests
    {
        private sealed class Speech : ISpeechOutput
        { public void Speak(string text, bool interrupt) { } public void Stop() { } }
        private sealed class Screen : AccessibleScreen
        {
            public override string Id => "test";
            public override string ScreenName => "test";
            public bool Modal, Unfocused;
            public Action Action;
            public override bool Exclusive => Modal;
            public override bool StartUnfocused => Unfocused;
            public override bool IsActive() => true;
            public override void Build(GraphBuilder builder)
            {
                builder.Add(ControlId.Structural("one"), new NodeVtable(new NodeAnnouncement(() => "one")) { OnActivate = Action });
                builder.Add(ControlId.Structural("two"), new NodeVtable(new NodeAnnouncement(() => "two")));
            }
        }
        private sealed class Rig : IDisposable
        {
            public readonly Screen Screen = new Screen();
            public readonly PhysicalInputGate Gate = new PhysicalInputGate();
            public readonly AccessibilityRuntime Runtime;
            public bool Capture;
            public int Calls;
            public Rig()
            {
                var faults = new FaultBoundary(new NullLog());
                var screens = new ScreenManager(faults);
                Screen.Action = () => Calls++;
                screens.Register(Screen);
                Runtime = new AccessibilityRuntime(screens, new Navigator(new Speech(),
                    new Localizer(new Dictionary<string, string>()), faults), new InputArbitration());
            }
            public void Poll(int frame, params HeldKey[] keys) => Gate.Poll(frame, frame * 0.01, true, keys, Runtime, () => Capture);
            public void Dispose() { Gate.Release(); Runtime.Dispose(); }
        }
        private static HeldKey Held(UiKey key, Modifiers modifiers = Modifiers.None) => new HeldKey((int)key, new KeyChord(key, modifiers));
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

        public static void NavigationGap()
        {
            using (var rig = new Rig())
            {
                rig.Poll(0); rig.Poll(1, Held(UiKey.Down)); rig.Poll(2, Held(UiKey.Down));
                Check(rig.Gate.Suppress(new KeyChord(UiKey.Down)), "The game cannot navigate between mod repeats.");
                rig.Poll(3);
                Check(rig.Gate.Suppress(new KeyChord(UiKey.Down)), "The owned release must be gated.");
                rig.Poll(4);
                Check(!rig.Gate.Suppress(new KeyChord(UiKey.Down)), "Claims expire after release.");
            }
        }
        public static void ActionAndToolkit()
        {
            using (var rig = new Rig())
            {
                rig.Screen.Action = () =>
                {
                    Check(rig.Runtime.Input.OwnAction, "Original handler needs ownership scope.");
                    Check(!rig.Gate.Suppress(new KeyChord(UiKey.Enter), rig.Runtime.Input.OwnAction), "Own handler must run.");
                    rig.Calls++;
                };
                rig.Poll(0); rig.Poll(1, Held(UiKey.Enter)); rig.Poll(1, Held(UiKey.Enter));
                rig.Poll(2, Held(UiKey.Enter));
                Check(rig.Calls == 1, "One physical Enter has one action across all GUI passes.");
                Check(rig.Gate.Suppress(new KeyChord(UiKey.Enter)), "A later native Submit is suppressed.");
                rig.Poll(3); rig.Poll(4, Held(UiKey.Enter));
                Check(rig.Calls == 2, "A released and pressed key acts again.");
            }
        }
        public static void RawTransition()
        {
            using (var rig = new Rig())
            {
                rig.Screen.Action = () => { rig.Calls++; rig.Capture = true; };
                rig.Poll(0); rig.Poll(1, Held(UiKey.Enter), Held(UiKey.Down));
                Check(rig.Gate.Suppress(new KeyChord(UiKey.Enter)), "Opening Enter cannot bind itself.");
                Check(!rig.Gate.Suppress(new KeyChord(UiKey.Down)), "Another key belongs to new raw capture immediately.");
                rig.Poll(2, Held(UiKey.Enter), Held(UiKey.Down));
                Check(rig.Gate.Suppress(new KeyChord(UiKey.Enter)), "Held opener remains gated in raw capture.");
                Check(!rig.Gate.Suppress(new KeyChord(UiKey.Escape)), "Raw Escape remains with the game.");
                rig.Poll(3); rig.Capture = false; rig.Poll(4, Held(UiKey.Enter));
                Check(rig.Calls == 1, "Capture-closing key waits for release.");
            }
        }
        public static void UnknownAndMappedContributors()
        {
            using (var rig = new Rig())
            {
                rig.Poll(0); rig.Poll(1, Held(UiKey.Down));
                Check(!rig.Gate.Suppress(new KeyChord(UiKey.Down, Modifiers.Alt)), "Unknown Alt chord remains with game/OS.");
                Check(rig.Gate.SuppressMappedAction(new[] { new KeyChord(UiKey.Down) }), "Owned mapped action is suppressed.");
                Check(!rig.Gate.SuppressMappedAction(new[] { new KeyChord(UiKey.Down), new KeyChord(UiKey.None, text: "w") }), "Independent unowned contributor must remain usable.");
                Check(!rig.Gate.SuppressMappedAction(Array.Empty<KeyChord>()), "An unknown source cannot be guessed.");
            }
        }
        public static void BlurAndDisable()
        {
            using (var rig = new Rig())
            {
                rig.Poll(0); rig.Poll(1, Held(UiKey.Enter)); rig.Gate.Blur();
                Check(!rig.Gate.Suppress(new KeyChord(UiKey.Enter)), "Blur returns input outside game.");
                rig.Poll(2, Held(UiKey.Enter));
                Check(rig.Calls == 1 && rig.Gate.Suppress(new KeyChord(UiKey.Enter)), "Owned key cannot replay on window return.");
                rig.Poll(3); rig.Poll(4, Held(UiKey.Enter));
                Check(rig.Calls == 2, "Release after return permits next action.");
                rig.Gate.Release();
                Check(!rig.Gate.Suppress(new KeyChord(UiKey.Enter)), "Disable returns ordinary game input.");
            }
        }
        public static void ModalAndUnfocused()
        {
            using (var rig = new Rig())
            {
                rig.Screen.Unfocused = true; rig.Poll(0); rig.Poll(1, Held(UiKey.Enter));
                Check(rig.Calls == 0 && !rig.Gate.Suppress(new KeyChord(UiKey.Enter)), "No cursor leaves Enter with game.");
                rig.Poll(2); rig.Poll(3, Held(UiKey.Tab)); rig.Screen.Modal = true; rig.Poll(4);
                Check(rig.Gate.Suppress(new KeyChord(UiKey.None, text: "x")), "Mod-owned modal gates unknown application keys.");
                rig.Capture = true; rig.Poll(5);
                Check(!rig.Gate.Suppress(new KeyChord(UiKey.None, text: "x")), "Real text field takes precedence over exclusivity.");
            }
        }
        public static void ExceptionAfterAction()
        {
            using (var rig = new Rig())
            {
                rig.Screen.Action = () => { rig.Calls++; throw new InvalidOperationException(); };
                rig.Poll(0); rig.Poll(1, Held(UiKey.Enter));
                Check(rig.Calls == 1 && rig.Gate.Suppress(new KeyChord(UiKey.Enter)), "Exception after action must not leak Submit.");
                Check(!rig.Runtime.Input.OwnAction, "Ownership scope must unwind on exception.");
            }
        }
        private sealed class MenuView { public bool Visible; }
        public static void NonMainView()
        {
            var hidden = new MenuView();
            var menu = new MenuView { Visible = true };
            var views = new object[] { new object(), hidden, menu };
            Check(ReferenceEquals(menu, LiveViewSelection.Find(views, typeof(MenuView), view => ((MenuView)view).Visible)),
                "A non-main menu must be found after overlay and hidden views.");
            menu.Visible = false;
            Check(LiveViewSelection.Find(views, typeof(MenuView), view => ((MenuView)view).Visible) == null, "Hidden menu is unavailable.");
            var replacement = new MenuView { Visible = true };
            Check(ReferenceEquals(replacement, LiveViewSelection.Find(new[] { replacement }, typeof(MenuView), view => ((MenuView)view).Visible)),
                "The next read must use the recreated view.");
        }
    }
}
