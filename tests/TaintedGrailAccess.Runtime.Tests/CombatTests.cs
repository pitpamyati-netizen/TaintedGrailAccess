using System;
using System.Collections.Generic;
using System.Linq;
using TaintedGrailAccess.Graph;
using TaintedGrailAccess.Runtime;

namespace TaintedGrailAccess.Runtime.Tests
{
    internal static class CombatTests
    {
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private sealed class Card
        {
            public readonly string Id;
            public string Label = "Удар";
            public string Cost = "2";
            public bool Enabled = true;
            public Card(string id) { Id = id; }
        }
        private sealed class Rig
        {
            public readonly GraphState State = new GraphState();
            public readonly List<Card> Cards = new List<Card>();
            public readonly KeyGraph Graph;
            public int Activations, Tooltips, RenderVersion, UsedVersion;
            public Rig()
            {
                Graph = new KeyGraph(State, () =>
                {
                    var builder = new GraphBuilder(State);
                    int version = ++RenderVersion;
                    builder.BeginStop("hand");
                    foreach (var card in Cards)
                    {
                        var instance = card;
                        CombatPresentation.Add(builder, instance, instance.Id, () => instance.Label, () => instance.Cost,
                            () => instance.Enabled ? null : "Недоступно", null,
                            () => { if (Cards.Contains(instance) && instance.Enabled) { Activations++; UsedVersion = version; } }, () => Tooltips++);
                    }
                    builder.BeginStop("actions");
                    builder.Add(ControlId.Structural("end"), new NodeVtable(new NodeAnnouncement(() => "Конец хода")));
                    return builder.Build();
                });
            }
        }
        public static void InspectionAndFreshAction()
        {
            var rig = new Rig(); var card = new Card("instance-1"); rig.Cards.Add(card);
            rig.Graph.Rerender(); rig.Graph.Tooltip();
            Check(rig.Activations == 0 && rig.Tooltips == 1, "Tooltip and render must not play a card.");
            card.Cost = "0"; card.Label = "Обратная сторона";
            rig.Graph.Rerender();
            Check(rig.Graph.Current.Vtable.Announcements[0].Text() == card.Label && rig.Graph.Current.Vtable.Announcements[1].Text() == "0", "Use live displayed side/cost.");
            int previous = rig.RenderVersion; rig.Graph.Activate();
            Check(rig.Activations == 1 && rig.UsedVersion > previous, "The action must come from a fresh render.");
            card.Enabled = false; rig.Graph.Activate();
            Check(rig.Activations == 1, "A newly unavailable card cannot be played.");
        }
        public static void DuplicateIdentityAndRemoval()
        {
            var rig = new Rig(); var one = new Card("one"); var two = new Card("two"); var three = new Card("three");
            rig.Cards.AddRange(new[] { one, two, three }); rig.Graph.FocusByReference(two);
            rig.Cards.Reverse(); rig.Graph.Rerender();
            Check(ReferenceEquals(rig.Graph.Current.Id.Reference, two), "Identical names must not lose the selected instance after reorder.");
            rig.Cards.Remove(two); rig.Graph.Rerender();
            Check(ReferenceEquals(rig.Graph.Current.Id.Reference, three), "The nearest preceding surviving card keeps the focus.");
            rig.Graph.Activate(); Check(rig.Activations == 1, "An operation after deletion invokes the survivor, not a stale card delegate.");
            rig.Cards.Clear(); rig.Graph.Rerender();
            Check(rig.Graph.Current.Id.StructuralKey.Equals("end"), "An empty hand still allows a turn action.");
        }
        public static void SelectionRules()
        {
            Check(!CombatPresentation.CanConfirmSelection(0, 2, 2, false, true), "Require all N selections.");
            Check(!CombatPresentation.CanConfirmSelection(1, 2, 2, false, true), "Partial selection cannot confirm.");
            Check(CombatPresentation.CanConfirmSelection(2, 2, 2, false, true), "Exactly N can confirm.");
            Check(!CombatPresentation.CanConfirmSelection(3, 2, 2, false, true), "Over-selection cannot confirm even if a stale button stayed enabled.");
            Check(!CombatPresentation.CanConfirmSelection(2, 2, 2, false, false), "Original button still owns validation.");
            Check(!CombatPresentation.CanConfirmSelection(2, 2, 2, true, true), "A display-only pile cannot activate cards.");
            Check(CombatPresentation.CanConfirmSelection(0, null, null, false, true), "Do not invent a minimum for optional selection.");
            Check(!CombatPresentation.CanConfirmSelection(-1, null, null, false, true), "Invalid counts fail closed.");
        }
        public static void HandFocusAcrossSectionsAndTargeting()
        {
            var cards = new List<Card> { new Card("first"), new Card("second"), new Card("third") };
            bool targeting = false;
            Card pending = null;
            var faults = new FaultBoundary(new NullLog());
            var screens = new ScreenManager(faults);
            var combat = new TestScreen("combat", 0, () => true, builder =>
            {
                builder.BeginStop("status"); builder.Add(ControlId.Structural("hero"), new NodeVtable(new NodeAnnouncement(() => "Герой")));
                builder.BeginStop("combat.hand");
                CombatPresentation.KeepHandFocus(builder.State, cards.Select(c => ControlId.Referenced(c, c.Id)).ToArray(), ControlId.Structural("end"));
                foreach (var card in cards)
                {
                    var current = card;
                    CombatPresentation.Add(builder, current, current.Id, () => current.Label, null, null, null,
                        () => { pending = current; targeting = true; }, null);
                }
                builder.BeginStop("actions"); builder.Add(ControlId.Structural("end"), new NodeVtable(new NodeAnnouncement(() => "Конец хода")));
            });
            var target = new TestScreen("target", 1, () => targeting, builder => builder.Add(ControlId.Structural("target"),
                new NodeVtable(new NodeAnnouncement(() => "Враг")) { OnActivate = () => { cards.Remove(pending); targeting = false; } }));
            screens.Register(combat); screens.Register(target);
            var speech = new TestSpeech();
            using (var runtime = new AccessibilityRuntime(screens, new Navigator(speech, new Localizer(new Dictionary<string, string>()), faults), new InputArbitration()))
            {
                long frame = 0;
                Action<KeyChord[]> step = chords => runtime.Update(++frame, chords);
                step(Array.Empty<KeyChord>()); step(new[] { new KeyChord(UiKey.Tab) });
                Check(runtime.Navigator.Current.Id.Reference == cards[0], "Setup focuses first card after hero section.");
                step(new[] { new KeyChord(UiKey.Enter) });
                Check(screens.Current.Screen.Id == "target", "Card opens target selection.");
                targeting = false; step(Array.Empty<KeyChord>());
                Check(runtime.Navigator.Current.Id.Reference == cards[0], "Cancellation returns to the same unplayed instance.");
                step(new[] { new KeyChord(UiKey.Enter) }); step(new[] { new KeyChord(UiKey.Enter) });
                Check(runtime.Navigator.Current.Id.Reference == cards[0] && cards[0].Id == "second", "Deleting first card keeps focus in the hand, not on hero.");
                cards.Reverse(); step(Array.Empty<KeyChord>());
                Check(runtime.Navigator.Current.Id.Reference == cards[1], "Reordering retains the same surviving instance.");
                step(new[] { new KeyChord(UiKey.Enter) }); step(new[] { new KeyChord(UiKey.Enter) });
                Check(runtime.Navigator.Current.Id.Reference == cards[0], "Deleting last card chooses a preceding hand survivor.");
                step(new[] { new KeyChord(UiKey.Enter) }); step(new[] { new KeyChord(UiKey.Enter) });
                Check(runtime.Navigator.Current.Id.Equals(ControlId.Structural("end")), "Empty hand returns to end turn.");
                cards.Add(new Card("drawn")); step(Array.Empty<KeyChord>());
                Check(runtime.Navigator.Current.Id.Equals(ControlId.Structural("end")), "Background draw must not steal focus from actions.");
                step(new[] { new KeyChord(UiKey.Tab) });
                Check(runtime.Navigator.Current.Id.Equals(ControlId.Structural("hero")), "Hero inspection remains deliberate.");
                cards.Clear(); step(Array.Empty<KeyChord>());
                Check(runtime.Navigator.Current.Id.Equals(ControlId.Structural("hero")), "Hand changes must not steal deliberate hero focus.");
            }
            var oldInstance = new object(); var newInstance = new object();
            var removed = ControlId.Referenced(new object(), "gone");
            var state = new GraphState { CurKey = removed, KeyOrder = new[] { ControlId.Referenced(oldInstance, "old-key"), removed } };
            state.StopMemory["combat.hand"] = removed;
            var currentCards = new[] { ControlId.Referenced(newInstance, "old-key"), ControlId.Referenced(oldInstance, "new-key") };
            CombatPresentation.KeepHandFocus(state, currentCards, ControlId.Structural("end"));
            Check(state.NextSuggestedMove.Reference == oldInstance, "Reference survivor takes priority over a reused structural key.");
            state.NextSuggestedMove = null;
            CombatPresentation.KeepHandFocus(state, new[] { currentCards[0] }, ControlId.Structural("end"));
            Check(state.NextSuggestedMove.Reference == newInstance, "Structural survivor is used if the old reference is absent.");
            state.NextSuggestedMove = ControlId.Structural("hero");
            CombatPresentation.KeepHandFocus(state, currentCards, ControlId.Structural("end"));
            Check(state.NextSuggestedMove.Equals(ControlId.Structural("hero")), "An existing explicit move takes priority over hand recovery.");
        }

        private sealed class TestScreen : AccessibleScreen
        {
            private readonly string _id;
            private readonly int _layer;
            private readonly Func<bool> _active;
            private readonly Action<GraphBuilder> _build;
            public TestScreen(string id, int layer, Func<bool> active, Action<GraphBuilder> build)
            { _id = id; _layer = layer; _active = active; _build = build; }
            public override string Id => _id;
            public override string ScreenName => _id;
            public override int Layer => _layer;
            public override bool IsActive() => _active();
            public override void Build(GraphBuilder builder) => _build(builder);
        }
        private sealed class TestSpeech : ISpeechOutput
        { public void Speak(string text, bool interrupt) { } public void Stop() { } }
        public static void TargetRules()
        {
            for (int mask = 0; mask < 32; mask++)
            {
                bool can = CombatPresentation.CanTarget((mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0, (mask & 16) != 0);
                Check(can == (mask == 31), "Target requires ownership, visible/live identity, current list and legality: " + mask);
            }
        }
        public static void LogNewLinesOnce()
        {
            var cursor = new CombatLogCursor();
            Check(cursor.Observe("fight-1", new[] { "История" }).Count == 0, "Initial history is a silent baseline.");
            var lines = cursor.Observe("fight-1", new[] { "История", "Урон 3", "Урон 3" });
            Check(lines.SequenceEqual(new[] { "Урон 3", "Урон 3" }), "Equal messages from distinct events must both be narrated.");
            Check(cursor.Observe("fight-1", new[] { "История", "Урон 3", "Урон 3" }).Count == 0, "Frame polling must not repeat events.");
            Check(cursor.Observe("fight-2", new[] { "Победа" }).Count == 0, "Another combat gets its own baseline.");
            Check(cursor.Observe("fight-2", new[] { "Победа", "Награда" }).Single() == "Награда", "Next event after replacement works.");
            Check(cursor.Observe("fight-2", new[] { "Новый журнал" }).Count == 0, "Truncated/replaced history does not replay.");
            cursor.Reset(); Check(cursor.Observe("fight-2", new[] { "Новый журнал", "Поздняя запись" }).Count == 0, "Scene release clears previous progress.");
        }
        public static void ReadOnlyControls()
        {
            var state = new GraphState();
            var graph = new KeyGraph(state, () =>
            {
                var builder = new GraphBuilder(state);
                CombatPresentation.Add(builder, new object(), "status", () => "Здоровье", () => "10/20", null, null, null, null);
                return builder.Build();
            });
            Check(!graph.Activate() && !graph.Tooltip(), "Absent actions remain absent for read-only status.");
        }
    }
}
