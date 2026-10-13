using System;
using System.Collections.Generic;
using System.Linq;
using TaintedGrailAccess.Graph;
using TaintedGrailAccess.Runtime;

namespace TaintedGrailAccess.Runtime.Tests
{
    internal static class ExplorationTests
    {
        private sealed class RecordingSpeech : ISpeechOutput
        {
            public void Speak(string text, bool interrupt) { }
            public void Stop() { }
        }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        public static void Geometry()
        {
            var origin = new MapPoint(0, 0);
            var targets = new[] { new MapPoint(0, 10), new MapPoint(10, 10), new MapPoint(10, 0), new MapPoint(10, -10), new MapPoint(0, -10), new MapPoint(-10, -10), new MapPoint(-10, 0), new MapPoint(-10, 10) };
            var names = new[] { "ahead", "aheadRight", "right", "behindRight", "behind", "behindLeft", "left", "aheadLeft" };
            for (int i = 0; i < targets.Length; i++) Check(ExplorationPresentation.Direction(origin, targets[i], 0, 1) == names[i], "Eight camera-relative directions.");
            Check(ExplorationPresentation.Direction(origin, targets[2], 1, 0) == "ahead", "Turning the camera changes direction, without changing the hero.");
            Check(ExplorationPresentation.Direction(origin, origin, 0, 1) == "here", "Coincident location.");
            Check(ExplorationPresentation.Direction(origin, targets[0], 0, 0) == "unknown", "No camera cannot invent a direction.");
            Check(ExplorationPresentation.Direction(origin, new MapPoint(double.NaN, 0), 0, 1) == "unknown", "Invalid coordinates cannot produce a bearing.");
            Check(new MapPoint(3, 4).DistanceTo(origin) == 5, "Direct distance in map units.");
        }
        public static void Routes()
        {
            var origin = new MapPoint(0, 0); var target = new MapPoint(10, 0);
            var detour = new[] { origin, new MapPoint(0, 5), new MapPoint(10, 5), target };
            var route = RouteInspection.Inspect(detour, origin, target, 3);
            Check(route.Availability == RouteAvailability.Complete && route.Length == 20 && origin.DistanceTo(target) == 10, "A detour has a distinct summed path length.");
            Check(RouteInspection.Inspect(new[] { origin, new MapPoint(4, 0) }, origin, target, 3).Availability == RouteAvailability.Partial, "Nonempty path can end short of the target.");
            Check(RouteInspection.Inspect(new[] { new MapPoint(6, 0), target }, origin, target, 3).Availability == RouteAvailability.Partial, "A path disconnected from the hero is partial.");
            Check(RouteInspection.Inspect(null, origin, target, 3).Availability == RouteAvailability.Missing, "Missing route.");
            Check(RouteInspection.Inspect(Array.Empty<MapPoint>(), origin, target, 3).Availability == RouteAvailability.Missing, "Empty route.");
            Check(RouteInspection.Inspect(new[] { origin, new MapPoint(double.PositiveInfinity, 0) }, origin, target, 3).Availability == RouteAvailability.Unknown, "Invalid route is unknown.");
            Check(RouteInspection.Inspect(detour, origin, target, -1).Availability == RouteAvailability.Unknown, "Invalid tolerance makes the estimate unknown.");
            Check(RouteInspection.Inspect(new[] { origin, new MapPoint(7, 0) }, origin, target, 3).Availability == RouteAvailability.Complete, "Exact original arrival tolerance.");
            var shiftedStart = RouteInspection.Inspect(new[] { new MapPoint(6, 0), target }, origin, target, 3);
            Check(shiftedStart.StartGap == 6 && shiftedStart.EndGap == 0 && shiftedStart.PointCount == 2,
                "A shifted start must not be described as a path ending short of the target.");
            var shortEnd = RouteInspection.Inspect(new[] { origin, new MapPoint(4, 0) }, origin, target, 3);
            Check(shortEnd.StartGap == 0 && shortEnd.EndGap == 6 && shortEnd.Length == 4,
                "The estimate reports actual endpoint gaps and length separately.");
        }
        public static void RouteEstimateDoesNotVetoOriginalAction()
        {
            var origin = new MapPoint(0, 0); var target = new MapPoint(10, 0);
            foreach (var path in new[] { new[] { origin, target }, new[] { origin, new MapPoint(4, 0) }, Array.Empty<MapPoint>(), new[] { new MapPoint(double.NaN, 0) } })
            {
                bool shown = true, nativeAllowed = true; int inspections = 0, originalCalls = 0, moves = 0;
                var watch = new ExplorationTravelWatch(); var state = new GraphState();
                var graph = new KeyGraph(state, () =>
                {
                    var builder = new GraphBuilder(state);
                    ExplorationPresentation.Add(builder, state, "location", () => true, () => shown,
                        () => "Место", () => "?", () => null, () => null,
                        () => { originalCalls++; if (nativeAllowed) moves++; },
                        () => { inspections++; RouteInspection.Inspect(path, origin, target, 3); });
                    return builder.Build();
                });
                graph.Rerender(); graph.Tooltip();
                Check(inspections == 1 && originalCalls == 0 && moves == 0, "Inspecting any estimate remains passive.");
                graph.Activate();
                Check(originalCalls == 1 && moves == 1 && inspections == 1, "Enter delegates once to the original handler without using or repeating the estimate.");
                nativeAllowed = false; graph.Activate();
                Check(originalCalls == 2 && moves == 1, "Native rejection cannot manufacture movement.");
                shown = false; graph.Activate(); Check(originalCalls == 2, "A vanished location cannot receive the original handler.");
                watch.Begin("move", origin, 0, 10);
                Check(watch.Observe(null, new MapPoint(4, 0), 1, 6, true, true, false) == "stopped",
                    "Ending an actual move short of the target is not reported as arrival.");
            }
        }
        public static void SpokenPlaceLabels()
        {
            int reads = 0;
            Func<IEnumerable<string>> names = () => { reads++; return new[] { "…", "...", "Кузница" }; };
            Check(ExplorationPresentation.PlaceLabel(false, names, () => "Место без названия", () => "Нераскрытое место") == "Нераскрытое место" && reads == 0,
                "A partial marker never reads even a known UI name.");
            Check(ExplorationPresentation.PlaceLabel(true, names, () => "Место без названия", () => "?") == "Кузница" && reads == 1,
                "A meaningful displayed name is preferred over an ellipsis.");
            Check(ExplorationPresentation.PlaceLabel(true, () => new[] { "…", "...", "  ", null, "???" }, () => "Место без названия", () => "?") == "Место без названия",
                "Punctuation-only game names receive an audible generic label, without inventing an identity.");
            Check(ExplorationPresentation.PlaceLabel(true, () => new[] { "Мельница 2", "Кузница" }, () => "?", () => "?") == "Мельница 2",
                "The first actual visible name keeps its original language and numbers.");
        }
        public static void StablePlaceNumbers()
        {
            var names = new ExplorationPlaceNames(); var first = new object(); var second = new object();
            Check(names.Number(first) == 1 && names.Number(second) == 2, "Distinct visible models get distinct own labels.");
            foreach (var instance in new[] { second, first, second, first })
                Check(names.Number(instance) == (ReferenceEquals(instance, first) ? 1 : 2), "Reordering and temporary hiding do not renumber places.");
            Func<string> fallback = () => "Место " + names.Number(first);
            Func<string> anonymous = () => "Нераскрытое место " + names.Number(first);
            int hiddenReads = 0;
            Func<IEnumerable<string>> shown = () => { hiddenReads++; return new[] { "…" }; };
            Check(ExplorationPresentation.PlaceLabel(false, shown, fallback, anonymous) == "Нераскрытое место 1" && hiddenReads == 0,
                "An anonymous numbered marker never reads a hidden game name.");
            Check(ExplorationPresentation.PlaceLabel(true, shown, fallback, anonymous) == "Место 1",
                "Disclosure preserves the same number when the game has no meaningful name.");
            Check(ExplorationPresentation.PlaceLabel(true, () => new[] { "Кузница" }, fallback, anonymous) == "Кузница",
                "A real shown game name has priority over the mod's assigned name.");
            Check(names.Number(new object()) == 3, "A procedural replacement gets a new identity, even at an old list position.");
            names.Reset(); Check(names.Number(second) == 1 && names.Number(first) == 2, "A released world starts a fresh numbering registry.");
        }

        public static void MarkerMeaning()
        {
            var known = new Dictionary<string, string>
            {
                ["icon_map_stone"] = "crossroads", ["Income"] = "crossroads",
                ["icon_map_statue"] = "monument", ["Claimed"] = "monument",
                ["icon_map_boss"] = "boss", ["Explored"] = "boss",
                ["icon_map_treasure"] = "treasure", ["Unclaimed"] = "treasure",
                ["icon_map_enemy"] = "combat", ["Encounter"] = "combat",
                ["icon_map_location"] = "event", ["Lost"] = "event",
                ["icon_map_npc"] = "npc", ["Unexplored"] = "quest"
            };
            foreach (var pair in known)
            {
                int reads = 0; Func<string> sprite = () => { reads++; return pair.Key; };
                Check(ExplorationPresentation.MarkerKind(false, true, sprite) == null && reads == 0, "A concealed image's cached sprite cannot reveal its meaning.");
                Check(ExplorationPresentation.MarkerKind(true, false, sprite) == null && reads == 0, "A disabled full icon cannot leak through a partial marker.");
                Check(ExplorationPresentation.MarkerKind(true, true, sprite) == pair.Value && reads == 1, "A currently drawn authored symbol conveys its verified meaning.");
            }
            foreach (var unknown in new[] { null, "", "icon_map_enemy_clone", "ICON_MAP_ENEMY", "custom_shop", "icon_map_hero" })
                Check(ExplorationPresentation.MarkerKind(true, true, () => unknown) == null, "Never guess a custom symbol from its technical name or a substring.");
            Check(known["icon_map_statue"] == "monument" && known["icon_map_npc"] == "npc" && known["Lost"] == "event",
                "A shared symbol cannot claim menhir versus portal, shop versus other NPC, or a hidden event subtype.");
        }
        public static void PartialMarkerMeaning()
        {
            bool disclosed = false, shown = true; string sprite = "icon_map_boss"; int names = 0, moves = 0;
            var model = new object(); var state = new GraphState(); var numbers = new ExplorationPlaceNames();
            Func<string> visibleMeaning = () => ExplorationPresentation.MarkerKind(shown, true, () => sprite);
            Func<string> anonymous = () => visibleMeaning() == "boss" ? "Босс " + numbers.Number(model) + "; место ещё не раскрыто" : "Нераскрытое место " + numbers.Number(model);
            Func<string> label = () => ExplorationPresentation.PlaceLabel(disclosed,
                () => { names++; return new[] { "Показанное имя" }; }, () => "Место " + numbers.Number(model), anonymous);
            var graph = new KeyGraph(state, () =>
            {
                var b = new GraphBuilder(state);
                ExplorationPresentation.Add(b, model, "marker", () => disclosed, () => shown, label, anonymous, () => "12 шагов", null, () => moves++, null);
                return b.Build();
            });
            graph.Rerender(); Check(graph.Current.Vtable.Announcements[0].Text() == "Босс 1; место ещё не раскрыто" && names == 0 && moves == 0,
                "Reading a displayed partial boss symbol neither reveals its hidden name nor starts a move.");
            sprite = "custom_boss";
            Check(graph.Current.Vtable.Announcements[0].Text() == "Нераскрытое место 1" && names == 0,
                "An unknown changed sprite keeps the same anonymous number, without guessing or reading a hidden name.");
            disclosed = true; graph.Rerender();
            Check(graph.Current.Vtable.Announcements[0].Text() == "Показанное имя" && names == 1,
                "Once disclosed, the actual shown name has priority over category and number.");
            shown = false; graph.Rerender();
            Check(graph.Current.Vtable.Announcements[0].Text() == null && names == 1 && moves == 0, "A vanished marker is silent and passive.");
        }

        public static void HoverInformationIsNotModal()
        {
            Check(!ExplorationPresentation.BlocksExploration(true, true), "Visible hover information does not block the scanner's original action.");
            Check(ExplorationPresentation.BlocksExploration(true, false), "A shown clickable interaction panel remains modal.");
            Check(!ExplorationPresentation.BlocksExploration(false, false) && !ExplorationPresentation.BlocksExploration(false, true),
                "Invisible panels cannot own exploration input.");
        }
        public static void DisclosureAndPassiveInspection()
        {
            bool disclosed = false, shown = true; int names = 0, moves = 0, inspections = 0;
            var state = new GraphState();
            var graph = new KeyGraph(state, () =>
            {
                var builder = new GraphBuilder(state);
                ExplorationPresentation.Add(builder, state, "location", () => disclosed, () => shown,
                    () => { names++; return "Тайный храм"; }, () => "Нераскрытое место", () => "Справа, 20", () => null,
                    () => moves++, () => inspections++);
                return builder.Build();
            });
            graph.Rerender();
            Check(graph.Current.Vtable.Announcements[0].Text() == "Нераскрытое место" && names == 0 && moves == 0, "A partial icon never queries the hidden name or moves the hero.");
            graph.Tooltip(); Check(inspections == 1 && moves == 0 && names == 0, "Route inspection does not move or reveal a name.");
            disclosed = true; graph.Rerender(); Check(graph.Current.Vtable.Announcements[0].Text() == "Тайный храм", "Newly disclosed name is read live.");
            shown = false; graph.Activate(); graph.Tooltip();
            Check(moves == 0 && inspections == 1 && graph.Current.Vtable.Announcements[0].Text() == null, "Disappearing objects neither speak stale data nor accept actions.");
        }
        public static void IdentityAndProceduralReplacement()
        {
            var a = new object(); var b = new object(); var objects = new List<object> { a, b }; var state = new GraphState();
            var graph = new KeyGraph(state, () =>
            {
                var builder = new GraphBuilder(state);
                foreach (var obj in objects) ExplorationPresentation.Add(builder, obj, ReferenceEquals(obj, a) ? "a" : "b", () => true, () => objects.Contains(obj),
                    () => "Одинаковое место", () => "?", () => "Расстояние", () => null, () => { }, () => { });
                return builder.Build();
            });
            graph.Rerender(); graph.FocusByReference(b); objects.Reverse(); graph.Rerender();
            Check(ReferenceEquals(graph.Current.Id.Reference, b), "Distance sorting does not switch between equal names.");
            objects.Remove(b); graph.Rerender(); Check(ReferenceEquals(graph.Current.Id.Reference, a), "Disappeared location falls back to a survivor.");
            objects.Clear(); graph.Rerender(); objects.Add(new object()); graph.Rerender();
            Check(graph.Current != null && !ReferenceEquals(graph.Current.Id.Reference, a), "Procedural replacement uses newly built live nodes.");
        }
        public static void TravelProgressAndStall()
        {
            var watch = new ExplorationTravelWatch(); var origin = new MapPoint(0, 0);
            watch.Begin("move", origin, 0, 50);
            Check(watch.Owns("move") && !watch.Owns("different"), "Cancellation only owns the original order.");
            Check(watch.Observe("move", origin, 7, 50, true, true, false) == null, "No premature stall.");
            Check(watch.Observe("move", origin, 8, 50, true, true, false) == "stalled", "Detect eight seconds without motion.");
            Check(watch.Observe("move", origin, 20, 50, true, true, false) == null, "Stall announces once.");
            Check(watch.Observe("move", new MapPoint(11, 0), 21, 39, true, true, false) == "progress", "Real travel crosses a distance bucket.");
            Check(watch.Observe("move", new MapPoint(11, 0), 29, 39, true, true, false) == "stalled", "Progress re-arms stall detection.");
        }
        public static void TravelPauseReplacementArrival()
        {
            var watch = new ExplorationTravelWatch(); var origin = new MapPoint(0, 0);
            watch.Begin("move", origin, 0, 50);
            watch.Pause(100);
            Check(watch.Observe("move", origin, 101, 50, true, true, false) == null, "OS focus return after suspended ticks rebases the stall clock.");
            watch.Begin("move", origin, 0, 50);
            Check(watch.Observe("move", origin, 20, 50, true, false, false) == null, "Alt+Tab pauses the stall clock.");
            Check(watch.Observe("move", origin, 22, 50, false, true, false) == null, "A game modal pauses the stall clock.");
            Check(watch.Observe("move", origin, 23, 50, true, true, false) == null, "Returning does not immediately report a stall.");
            Check(watch.Observe("different", origin, 24, 50, true, true, false) == "stopped" && !watch.IsTracking, "Replacement is not arrival.");
            watch.Begin("move", origin, 25, 2);
            Check(watch.Observe(null, origin, 26, 0, true, true, true) == "arrived", "Arrival requires actual destination evidence.");
            Check(watch.Observe(null, origin, 27, 0, true, true, true) == null, "Completion announces once.");
            watch.Begin("move", origin, 28, 2); watch.Reset(); Check(!watch.IsTracking, "Cancel/scene unload clears owned travel.");
        }
        private sealed class WorldScreen : AccessibleScreen
        {
            public bool EventOpen, MapReady = true; public int Moves, Cancels;
            public override string Id => "world";
            public override string ScreenName => "Исследование";
            public override bool StartUnfocused => true;
            public override bool IsActive() => true;
            public override void Build(GraphBuilder builder)
            {
                builder.BeginStop("locations");
                builder.Add(ControlId.Structural("place"), new NodeVtable(new NodeAnnouncement(() => "Место"))
                { OnActivate = () => { if (MapReady && !EventOpen) { Moves++; EventOpen = true; } } });
                builder.BeginStop("travel");
                builder.Add(ControlId.Structural("cancel"), new NodeVtable(new NodeAnnouncement(() => "Остановить")) { OnActivate = () => Cancels++ });
            }
        }
        public static void InputAndEventReturn()
        {
            var screen = new WorldScreen(); var faults = new FaultBoundary(new NullLog()); var screens = new ScreenManager(faults);
            screens.Register(screen);
            screens.Register(new EventScreen(screen));
            using (var runtime = new AccessibilityRuntime(screens, new Navigator(new RecordingSpeech(), new Localizer(new Dictionary<string, string>()), faults), new InputArbitration()))
            {
                var gate = new PhysicalInputGate(); long frame = 0;
                Action<UiKey> press = key => gate.Poll(++frame, frame * 0.1, true, key == UiKey.None ? Array.Empty<HeldKey>() : new[] { new HeldKey((int)key, new KeyChord(key)) }, runtime, () => false);
                press(UiKey.None); press(UiKey.Enter); Check(screen.Moves == 0 && !gate.Suppress(new KeyChord(UiKey.Enter)), "Unfocused Enter remains with the game.");
                press(UiKey.None); press(UiKey.Tab); press(UiKey.None); press(UiKey.Down);
                Check(screen.Moves == 0, "List navigation cannot move the hero.");
                press(UiKey.None); press(UiKey.Enter); press(UiKey.Enter);
                Check(screen.Moves == 1 && screens.Current.Screen.Id == "event", "Held Enter opens one original event.");
                press(UiKey.None); press(UiKey.Escape); press(UiKey.Escape);
                Check(!screen.EventOpen && screens.Current.Screen.Id == "world" && runtime.Navigator.HasFocus, "Event closes and restores scanner focus without repeating the opening key.");
                press(UiKey.None); screen.MapReady = false; press(UiKey.Enter); Check(screen.Moves == 1, "Live map interactivity prevents another action.");
            }
        }
        private sealed class EventScreen : AccessibleScreen
        {
            private readonly WorldScreen _world;
            public EventScreen(WorldScreen world) { _world = world; }
            public override string Id => "event";
            public override int Layer => 5;
            public override string ScreenName => "Событие";
            public override bool IsActive() => _world.EventOpen;
            public override void Build(GraphBuilder builder) => builder.Add(ControlId.Structural("text"), new NodeVtable(new NodeAnnouncement(() => "Событие")));
            public override bool OnBack() { _world.EventOpen = false; return true; }
        }
    }
}
