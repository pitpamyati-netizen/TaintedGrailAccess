using System;
using System.Collections.Generic;
using System.Linq;
using TaintedGrailAccess.Graph;

internal static class Program
{
    private static int _passed, _failed;
    private static bool _verbose;

    private static int Main(string[] args)
    {
        _verbose = args.Contains("--verbose");
        IdentityTests();
        BuilderTests();
        ReconciliationTests();
        OperationTests();
        TreeTests();
        AnnouncementTests();
        Console.WriteLine("Graph kernel: " + _passed + " passed; " + _failed + " failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static void IdentityTests()
    {
        T("identity equality and hash depend only on structural key", () =>
        {
            ControlId a = ControlId.Referenced(new object(), "x"), b = ControlId.Referenced(new object(), "x");
            Check(a.Equals(b)); Equal(a.GetHashCode(), b.GetHashCode());
            var map = new Dictionary<ControlId, string> { [a] = "value" };
            Equal("value", map[b]);
        });
        T("reference sameness is separate from map equality", () =>
        {
            object item = new object();
            ControlId a = ControlId.Referenced(item, "old"), b = ControlId.Referenced(item, "new");
            Check(!a.Equals(b)); Check(ControlId.SameIdentity(a, b));
        });
        T("domain value equality is not reference identity", () =>
        {
            var a = new ValueObject(1); var b = new ValueObject(1);
            Check(a.Equals(b)); Check(!ControlId.SameIdentity(ControlId.Referenced(a, "a"), ControlId.Referenced(b, "b")));
        });
        T("two absent references do not equate distinct controls", () =>
        {
            Check(!ControlId.SameIdentity(Id("a"), Id("b"))); Check(ControlId.SameIdentity(null, null));
            Check(!ControlId.SameIdentity(Id("a"), null));
        });
        T("ForObject retains object as both tiers", () =>
        {
            object value = new object(); ControlId id = ControlId.ForObject(value);
            Check(ReferenceEquals(value, id.Reference)); Check(ReferenceEquals(value, id.StructuralKey));
        });
        T("composite structural keys use value equality", () =>
        {
            Check(ControlId.Structural(Tuple.Create("hand", 4)).Equals(ControlId.Structural(Tuple.Create("hand", 4))));
        });
        T("null identities fail early", () =>
        {
            Throws<ArgumentNullException>(() => ControlId.Structural(null));
            Throws<ArgumentNullException>(() => ControlId.Referenced(null, "a"));
        });
    }

    private static void BuilderTests()
    {
        T("implicit menu rows form a vertical list", () =>
        {
            GraphRender r = Build(new GraphState(), b => { Add(b, "a"); Add(b, "b"); Add(b, "c"); });
            Edge(r, "a", Direction.Down, "b"); Edge(r, "b", Direction.Up, "a"); Edge(r, "b", Direction.Down, "c");
            Missing(r, "a", Direction.Up); Missing(r, "c", Direction.Down);
        });
        T("horizontal menu rows wire both directions", () =>
        {
            GraphRender r = Build(new GraphState(), b => { b.BeginRow(); Add(b, "a"); Add(b, "b"); Add(b, "c"); b.EndRow(); });
            Edge(r, "a", Direction.Right, "b"); Edge(r, "c", Direction.Left, "b");
            Missing(r, "a", Direction.Left); Missing(r, "c", Direction.Right);
        });
        T("matching row keys preserve columns", () =>
        {
            GraphRender r = Build(new GraphState(), b =>
            {
                Row(b, "columns", "a", "b"); Row(b, "columns", "c", "d");
            });
            Edge(r, "b", Direction.Down, "d"); Edge(r, "d", Direction.Up, "b");
        });
        T("missing target column falls back to first item", () =>
        {
            GraphRender r = Build(new GraphState(), b => { Row(b, "grid", "a", "b", "c"); Row(b, "grid", "d", "e"); });
            Edge(r, "c", Direction.Down, "d"); Edge(r, "b", Direction.Down, "e");
        });
        T("absent or different row keys land in first column", () =>
        {
            GraphRender r = Build(new GraphState(), b => { Row(b, null, "a", "b"); Row(b, null, "c", "d"); Row(b, "other", "e", "f"); });
            Edge(r, "b", Direction.Down, "c"); Edge(r, "d", Direction.Down, "e"); Edge(r, "f", Direction.Up, "c");
        });
        T("arrows never cross a stop even through explicit Connect", () =>
        {
            GraphRender r = Build(new GraphState(), b =>
            {
                b.BeginStop("a"); Add(b, "a1"); b.BeginStop("b"); Add(b, "b1");
                b.Connect(Id("a1"), Direction.Down, Id("b1"));
            });
            Missing(r, "a1", Direction.Down); Missing(r, "b1", Direction.Up);
        });
        T("same stop rows chain across other stop declarations", () =>
        {
            GraphRender r = Build(new GraphState(), b =>
            {
                b.BeginStop("a"); Add(b, "a1"); b.BeginStop("b"); Add(b, "b1"); b.BeginStop("a"); Add(b, "a2");
            });
            Edge(r, "a1", Direction.Down, "a2"); Missing(r, "b1", Direction.Down);
        });
        T("dangling raw edges are dropped", () =>
        {
            GraphRender r = Build(new GraphState(), b =>
            {
                Raw(b, "a"); b.Connect(Id("a"), Direction.Right, Id("missing")); b.Connect(Id("missing"), Direction.Down, Id("a"));
            });
            Missing(r, "a", Direction.Right);
        });
        T("raw edge labels and explicit menu overrides survive", () =>
        {
            GraphRender r = Build(new GraphState(), b =>
            {
                Add(b, "a"); Add(b, "b"); Add(b, "c"); b.Connect(Id("a"), Direction.Down, Id("c"), "Next lane");
            });
            Edge(r, "a", Direction.Down, "c"); Equal("Next lane", r.Nodes[Id("a")].Transitions[Direction.Down].Label);
        });
        T("auto stop keys are value-stable between builders", () =>
        {
            Action<GraphBuilder> recipe = b => { b.BeginStop(); Add(b, "a"); b.BeginStop(); Add(b, "b"); };
            GraphRender one = Build(new GraphState(), recipe), two = Build(new GraphState(), recipe);
            Equal(one.Nodes[Id("a")].StopKey, two.Nodes[Id("a")].StopKey);
            Check(!Equals(one.Nodes[Id("a")].StopKey, one.Nodes[Id("b")].StopKey));
        });
        T("region tags reset at stop boundary", () =>
        {
            GraphRender r = Build(new GraphState(), b => { b.SetRegion("top"); Add(b, "a"); b.BeginStop("second"); Add(b, "b"); });
            Equal("top", r.Nodes[Id("a")].RegionKey); Check(r.Nodes[Id("b")].RegionKey == null);
        });
        T("menu raw menu seams are stitched bidirectionally", () =>
        {
            GraphRender r = Build(new GraphState(), b =>
            {
                Row(b, null, "h1", "h2"); Raw(b, "r1"); Raw(b, "r2");
                b.Connect(Id("r1"), Direction.Down, Id("r2")); b.Connect(Id("r2"), Direction.Up, Id("r1"));
                Row(b, null, "f1", "f2");
            });
            Edge(r, "h1", Direction.Down, "r1"); Edge(r, "h2", Direction.Down, "r1"); Edge(r, "r1", Direction.Up, "h1");
            Edge(r, "r2", Direction.Down, "f1"); Edge(r, "f1", Direction.Up, "r2"); Edge(r, "f2", Direction.Up, "r2");
        });
        T("stitch searches missing slots and preserves raw wiring", () =>
        {
            GraphRender r = Build(new GraphState(), b =>
            {
                Add(b, "header"); Raw(b, "r1"); Raw(b, "r2"); Add(b, "footer");
                b.Connect(Id("r1"), Direction.Up, Id("r2")); b.Connect(Id("r2"), Direction.Down, Id("r1"));
            });
            Edge(r, "r1", Direction.Up, "r2"); Edge(r, "header", Direction.Down, "r2");
            Edge(r, "r2", Direction.Down, "r1"); Edge(r, "footer", Direction.Up, "r1");
        });
        T("raw content interrupts menu vertical chaining", () =>
        {
            GraphRender r = Build(new GraphState(), b => { Add(b, "before"); Raw(b, "raw"); Add(b, "after"); });
            Edge(r, "before", Direction.Down, "raw"); Edge(r, "after", Direction.Up, "raw");
        });
        T("mode seams do not cross stops", () =>
        {
            GraphRender r = Build(new GraphState(), b => { b.BeginStop("menu"); Add(b, "a"); b.BeginStop("raw"); Raw(b, "b"); });
            Missing(r, "a", Direction.Down); Missing(r, "b", Direction.Up);
        });
        T("rows stamp positions while raw nodes have none", () =>
        {
            GraphRender r = Build(new GraphState(), b => { Row(b, null, "a", "b"); Raw(b, "raw"); });
            Equal(2, r.Nodes[Id("b")].PositionIndex); Equal(2, r.Nodes[Id("b")].PositionCount); Equal(0, r.Nodes[Id("raw")].PositionCount);
        });
        T("vertical positions are scoped by parent and stop", () =>
        {
            GraphRender r = Build(new GraphState(), b =>
            {
                b.PushContext("Panel"); Add(b, "a"); Add(b, "b"); b.PopContext(); Add(b, "root");
                b.BeginStop("second"); Add(b, "other");
            });
            Equal(2, r.Nodes[Id("b")].PositionCount); Equal(2, r.Nodes[Id("b")].PositionIndex);
            Equal(1, r.Nodes[Id("root")].PositionCount); Equal(1, r.Nodes[Id("other")].PositionCount);
        });
        T("a parent may suppress all direct child positions", () =>
        {
            GraphRender r = Build(new GraphState(), b => { b.PushContext("Log", suppressChildPositions: true); Add(b, "a"); Add(b, "b"); b.PopContext(); });
            Equal(0, r.Nodes[Id("a")].PositionCount); Equal(0, r.Nodes[Id("b")].PositionIndex);
        });
        T("context identity is stable and contexts are not focusable", () =>
        {
            Action<GraphBuilder> recipe = b => { b.PushContext("Settings", "list"); Add(b, "child"); b.PopContext(); };
            GraphRender a = Build(new GraphState(), recipe), b2 = Build(new GraphState(), recipe);
            Check(a.Nodes[Id("child")].Parent.Id.Equals(b2.Nodes[Id("child")].Parent.Id));
            Check(!a.Nodes[Id("child")].Parent.Focusable); Equal("child", a.StartKey.StructuralKey);
        });
        T("pure contexts are never valid raw edge destinations", () =>
        {
            GraphRender r = Build(new GraphState(), b =>
            {
                GraphNode parent = b.PushContext("Context"); Raw(b, "a"); b.Connect(Id("a"), Direction.Right, parent.Id); b.PopContext();
            });
            Missing(r, "a", Direction.Right);
        });
        T("collapsed ancestors suppress recursively but preserve declaration balance", () =>
        {
            GraphRender r = Build(new GraphState(), b =>
            {
                b.BeginGroup(Id("outer"), V("Outer"));
                b.PushContext("Hidden"); b.BeginGroup(Id("inner"), V("Inner"), true); Add(b, "hidden"); b.EndGroup(); b.PopContext(); b.EndGroup();
                Add(b, "visible");
            });
            Seq(new[] { "outer", "visible" }, r.Order.Select(k => k.ToString())); Check(!r.Nodes[Id("outer")].Expanded);
        });
        T("raw connections inside a collapsed group do not leak into visible navigation", () =>
        {
            GraphRender r = Build(new GraphState(), b =>
            {
                b.BeginGroup(Id("g"), V("Group")); b.Connect(Id("g"), Direction.Right, Id("tail")); b.EndGroup(); Add(b, "tail");
            });
            Missing(r, "g", Direction.Right);
        });
        T("expanded set and explicit expansion values are read on build", () =>
        {
            var state = new GraphState(); state.Expanded.Add(Id("a")); state.Expanded.Add(Id("b"));
            GraphRender r = Build(state, b =>
            {
                b.BeginGroup(Id("a"), V("A")); Add(b, "a-child"); b.EndGroup();
                b.BeginGroup(Id("b"), V("B"), false); Add(b, "b-child"); b.EndGroup();
            });
            Check(r.Nodes.ContainsKey(Id("a-child"))); Check(!r.Nodes.ContainsKey(Id("b-child")));
        });
        T("an empty declaration pass returns null", () => Check(Build(new GraphState(), b => { }) == null));
        T("structure with no focusable content returns null", () => Check(Build(new GraphState(), b => { b.PushContext("Empty"); b.PopContext(); }) == null));
        T("duplicate structural ids fail even with different references", () =>
        {
            Throws<InvalidOperationException>(() => Build(new GraphState(), b =>
            { b.Add(ControlId.Referenced(new object(), "same"), V("One")); b.Add(ControlId.Referenced(new object(), "same"), V("Two")); }));
        });
        T("empty announcement lists fail validation", () => Throws<InvalidOperationException>(() => Build(new GraphState(), b => b.Add(Id("a"), new NodeVtable()))));
        T("null announcement parts fail validation", () => Throws<InvalidOperationException>(() => Build(new GraphState(), b => b.Add(Id("a"), new NodeVtable(new NodeAnnouncement[] { null })))));
        T("unclosed rows fail validation", () => Throws<InvalidOperationException>(() => Build(new GraphState(), b => { b.BeginRow(); Add(b, "a"); })));
        T("unclosed hierarchy fails validation", () => Throws<InvalidOperationException>(() => Build(new GraphState(), b => { b.PushContext("Open"); Add(b, "a"); })));
        T("scope kinds must remain balanced", () => Throws<InvalidOperationException>(() => Build(new GraphState(), b => { b.BeginGroup(Id("g"), V("Group")); b.PopContext(); })));
        T("builders cannot mutate an already published snapshot", () =>
        {
            var b = new GraphBuilder(new GraphState()); Add(b, "a"); b.Build(); Throws<InvalidOperationException>(() => Add(b, "b"));
        });
    }

    private static void ReconciliationTests()
    {
        T("tier one follows the moved object before an occupied structural slot", () =>
        {
            object moving = new object(), replacement = new object(); bool shifted = false; var state = new GraphState();
            var graph = Engine(state, b =>
            {
                if (shifted) b.Add(ControlId.Referenced(replacement, "old"), V("Replacement"));
                b.Add(ControlId.Referenced(moving, shifted ? "new" : "old"), V("Moving"));
            });
            graph.Rerender(); shifted = true; graph.Rerender();
            Equal("new", state.CurKey.StructuralKey); Check(ReferenceEquals(moving, state.CurKey.Reference));
        });
        T("tier two follows a rebuilt object and refreshes reference metadata", () =>
        {
            object current = new object(); var state = new GraphState();
            var graph = Engine(state, b => { Add(b, "first"); b.Add(ControlId.Referenced(current, "item"), V("Item")); });
            graph.Focus(Id("item")); current = new object(); graph.Rerender();
            Equal("item", state.CurKey.StructuralKey); Check(ReferenceEquals(current, state.CurKey.Reference));
        });
        T("nearest survivor walks backward in the previous order after reorder", () =>
        {
            string[] items = { "a", "b", "c", "d" }; var state = new GraphState();
            var graph = Engine(state, b => { foreach (string item in items) Add(b, item); });
            graph.Focus(Id("c")); items = new[] { "d", "a", "b" }; graph.Rerender(); Equal("b", state.CurKey.StructuralKey);
        });
        T("nearest survivor can itself have moved structurally", () =>
        {
            object previous = new object(); bool changed = false; var state = new GraphState();
            var graph = Engine(state, b =>
            {
                b.Add(ControlId.Referenced(previous, changed ? "new-prev" : "prev"), V("Previous"));
                if (!changed) Add(b, "removed");
            });
            graph.Focus(Id("removed")); changed = true; graph.Rerender(); Equal("new-prev", state.CurKey.StructuralKey);
        });
        T("selected member seats initial focus in the start stop", () =>
        {
            var state = new GraphState(); var graph = Engine(state, b =>
            {
                b.BeginStop("first"); Add(b, "a"); b.Add(Id("selected"), SelectedV("Selected", true));
                b.BeginStop("second"); b.Add(Id("other-selected"), SelectedV("Other", true));
            });
            graph.Rerender(); Equal("selected", state.CurKey.StructuralKey);
        });
        T("selected state is evaluated live", () =>
        {
            bool selected = false; var state = new GraphState();
            var graph = Engine(state, b => { Add(b, "first"); b.Add(Id("second"), new NodeVtable(Label("Second"), new NodeAnnouncement(() => selected ? "selected" : null, "selected"))); });
            selected = true; graph.Rerender(); Equal("second", state.CurKey.StructuralKey);
        });
        T("initial selection includes common parts and respects a silent node override", () =>
        {
            var type = new ControlType("radio", common: new[] { new NodeAnnouncement(() => "selected", "selected") });
            var graph = Engine(new GraphState(), b =>
            {
                b.Add(Id("first"), new NodeVtable(Label("First"), new NodeAnnouncement(() => null, "selected")) { ControlType = type });
                b.Add(Id("second"), new NodeVtable(Label("Second")) { ControlType = type });
            });
            graph.Rerender(); Equal("second", graph.State.CurKey.StructuralKey);
        });
        T("all old nodes gone falls back to selection or requested start", () =>
        {
            bool changed = false; var state = new GraphState(); var graph = Engine(state, b =>
            {
                if (!changed) Add(b, "old"); else { Add(b, "a"); Add(b, "start"); b.SetStart(Id("start")); }
            });
            graph.Rerender(); changed = true; graph.Rerender(); Equal("start", state.CurKey.StructuralKey);
        });
        T("present suggested focus wins once", () =>
        {
            var state = new GraphState(); var graph = Engine(state, b => { Add(b, "a"); Add(b, "b"); });
            graph.Rerender(); state.NextSuggestedMove = Id("b"); graph.Rerender();
            Equal("b", state.CurKey.StructuralKey); Check(state.NextSuggestedMove == null);
        });
        T("absent suggestion is consumed and does not jump later", () =>
        {
            bool present = false; var state = new GraphState(); var graph = Engine(state, b => { Add(b, "a"); if (present) Add(b, "later"); });
            graph.Rerender(); state.NextSuggestedMove = Id("later"); graph.Rerender(); Check(state.NextSuggestedMove == null);
            present = true; graph.Rerender(); Equal("a", state.CurKey.StructuralKey);
        });
        T("empty render preserves all persistent cursor fields", () =>
        {
            bool empty = false; var state = new GraphState(); var graph = new KeyGraph(state, () => empty ? null : Build(state, b => { Add(b, "a"); Add(b, "b"); }));
            graph.Focus(Id("b")); IReadOnlyList<ControlId> previousOrder = state.KeyOrder; state.NextSuggestedMove = Id("a");
            state.Expanded.Add(Id("g")); int memories = state.StopMemory.Count; empty = true;
            MoveResult result = graph.Move(Direction.Up); Check(!result.Moved && result.To == result.From);
            Equal("b", state.CurKey.StructuralKey); Check(ReferenceEquals(previousOrder, state.KeyOrder));
            Equal("a", state.NextSuggestedMove.StructuralKey); Equal(memories, state.StopMemory.Count); Check(state.Expanded.Contains(Id("g")));
            empty = false; graph.Rerender(); Equal("a", state.CurKey.StructuralKey);
        });
        T("an unfocused host can rebuild without seating a cursor", () =>
        {
            var state = new GraphState(); var graph = Engine(state, b => Add(b, "a"));
            graph.Rerender(false); Check(graph.Render != null); Check(state.CurKey == null); Check(graph.Current == null);
            graph.Rerender(); Equal("a", state.CurKey.StructuralKey);
        });
        T("down-right traversal is total and cycle-safe", () =>
        {
            GraphRender r = Build(new GraphState(), b =>
            {
                b.PushContext("Context"); Raw(b, "a"); Raw(b, "d"); Raw(b, "c"); Raw(b, "b"); Raw(b, "orphan"); b.PopContext();
                b.Connect(Id("a"), Direction.Right, Id("b")); b.Connect(Id("a"), Direction.Down, Id("c"));
                b.Connect(Id("b"), Direction.Down, Id("d")); b.Connect(Id("b"), Direction.Right, Id("a"));
                b.Connect(Id("c"), Direction.Right, Id("d")); b.Connect(Id("c"), Direction.Down, Id("a"));
                b.BeginStop("other"); Add(b, "other");
            });
            Seq(new[] { "a", "b", "c", "d", "orphan", "other" }, KeyGraph.ComputeOrder(r).Select(k => k.ToString()));
        });
        T("reordering does not merge control metadata or move unrelated references", () =>
        {
            object a = new object(), bObj = new object(); bool reversed = false; var state = new GraphState();
            var graph = Engine(state, builder =>
            {
                if (reversed) builder.Add(ControlId.Referenced(bObj, "b"), V("B fresh"));
                builder.Add(ControlId.Referenced(a, "a"), V(reversed ? "A fresh" : "A old"));
                if (!reversed) builder.Add(ControlId.Referenced(bObj, "b"), V("B old"));
            });
            graph.Rerender(); GraphNode old = graph.Current; reversed = true; graph.Rerender();
            Equal("a", state.CurKey.StructuralKey); Check(!ReferenceEquals(old, graph.Current));
            Equal("A fresh", graph.Current.Vtable.Announcements[0].Text()); Equal("B fresh", graph.Render.Nodes[Id("b")].Vtable.Announcements[0].Text());
        });
    }

    private static void OperationTests()
    {
        T("each public operation reads a fresh snapshot", () =>
        {
            int builds = 0; var state = new GraphState(); var graph = new KeyGraph(state, () =>
            {
                builds++; return Build(state, b => { Add(b, "a"); Add(b, "b"); });
            });
            Action[] operations =
            {
                () => graph.Move(Direction.Down), () => graph.MoveToEdge(Direction.Up), () => graph.MoveStop(1), () => graph.MoveRegion(1),
                () => graph.Focus(Id("a")), () => graph.FocusByReference(new object()), () => graph.MoveToSiblingEdge(false),
                () => graph.TreeRight(), () => graph.TreeLeft(), () => graph.Activate(), () => graph.Secondary(),
                () => graph.ActivateShift(), () => graph.ActivateCtrl(), () => graph.Tooltip(), () => graph.TryAdjust(1, false), () => graph.Search("a")
            };
            foreach (Action operation in operations) { int before = builds; operation(); Check(builds > before); }
        });
        T("hard edges return To identical to From", () =>
        {
            var graph = Engine(new GraphState(), b => Add(b, "a")); MoveResult result = graph.Move(Direction.Up);
            Check(!result.Moved); Check(ReferenceEquals(result.From, result.To));
        });
        T("move returns crossed edge label", () =>
        {
            var graph = Engine(new GraphState(), b => { Raw(b, "a"); Raw(b, "b"); b.Connect(Id("a"), Direction.Right, Id("b"), "Inventory"); });
            MoveResult result = graph.Move(Direction.Right); Check(result.Moved); Equal("Inventory", result.TransitionLabel);
        });
        T("edge moves terminate on directional cycles", () =>
        {
            var graph = Engine(new GraphState(), b =>
            {
                Raw(b, "a"); Raw(b, "b"); Raw(b, "c"); b.Connect(Id("a"), Direction.Right, Id("b"));
                b.Connect(Id("b"), Direction.Right, Id("c")); b.Connect(Id("c"), Direction.Right, Id("a"));
            });
            graph.MoveToEdge(Direction.Right); Equal("c", graph.State.CurKey.StructuralKey);
        });
        T("focus by reference synchronizes to the requested object", () =>
        {
            object a = new object(), bObj = new object(); var graph = Engine(new GraphState(), b =>
            { b.Add(ControlId.Referenced(a, "a"), V("A")); b.Add(ControlId.Referenced(bObj, "b"), V("B")); });
            graph.FocusByReference(bObj); Equal("b", graph.State.CurKey.StructuralKey);
            Check(!graph.Focus(Id("missing")).Moved); Check(!graph.FocusByReference(null).Moved);
        });
        T("stop cycling honors remembered position before selection", () =>
        {
            var graph = Engine(new GraphState(), b =>
            {
                b.BeginStop("a"); Add(b, "a1"); Add(b, "a2");
                b.BeginStop("b"); Add(b, "b1"); b.Add(Id("b2"), SelectedV("B2", true));
            });
            graph.Focus(Id("a2")); graph.MoveStop(1); Equal("b2", graph.State.CurKey.StructuralKey);
            graph.Focus(Id("b1")); graph.MoveStop(1); Equal("a2", graph.State.CurKey.StructuralKey);
            graph.MoveStop(1); Equal("b1", graph.State.CurKey.StructuralKey);
        });
        T("stop boundaries support no-wrap and negative wrap", () =>
        {
            var graph = Engine(new GraphState(), b => { b.BeginStop("a"); Add(b, "a"); b.BeginStop("b"); Add(b, "b"); });
            Check(!graph.MoveStop(-1, false).Moved); graph.MoveStop(-1, true); Equal("b", graph.State.CurKey.StructuralKey);
            Check(!graph.MoveStop(1, false).Moved);
        });
        T("stop memory survives stop reordering and reference movement", () =>
        {
            bool changed = false; object remembered = new object(); var graph = Engine(new GraphState(), b =>
            {
                if (changed) { b.BeginStop("b"); Add(b, "b"); }
                b.BeginStop("a"); Add(b, "a1"); b.Add(ControlId.Referenced(remembered, changed ? "moved" : "a2"), V("Remembered"));
                if (!changed) { b.BeginStop("b"); Add(b, "b"); }
            });
            graph.Focus(Id("a2")); graph.MoveStop(1); changed = true; graph.MoveStop(1); Equal("moved", graph.State.CurKey.StructuralKey);
        });
        T("stale stop memory cannot land in a different stop", () =>
        {
            bool moved = false; object item = new object(); var graph = Engine(new GraphState(), b =>
            {
                b.BeginStop("a"); Add(b, "a-first"); if (!moved) b.Add(ControlId.Referenced(item, "item"), V("Item"));
                b.BeginStop("b"); Add(b, "b-first"); if (moved) b.Add(ControlId.Referenced(item, "item-new"), V("Item"));
            });
            graph.Focus(Id("item")); graph.Focus(Id("b-first")); moved = true; graph.MoveStop(1); Equal("a-first", graph.State.CurKey.StructuralKey);
        });
        T("region jumps wrap inside current stop", () =>
        {
            var graph = Engine(new GraphState(), b =>
            {
                b.BeginStop("a"); b.SetRegion("top"); Add(b, "a1"); Add(b, "a2"); b.SetRegion("bottom"); Add(b, "a3");
                b.BeginStop("b"); b.SetRegion("other"); Add(b, "b1");
            });
            graph.MoveRegion(-1); Equal("a3", graph.State.CurKey.StructuralKey); graph.MoveRegion(1); Equal("a1", graph.State.CurKey.StructuralKey);
        });
        T("untagged controls do not perform region jumps", () =>
        {
            var graph = Engine(new GraphState(), b => { Add(b, "plain"); b.SetRegion("r"); Add(b, "region"); }); Check(!graph.MoveRegion(1).Moved);
        });
        T("actions invoke latest live handlers and optional slots report absence", () =>
        {
            int generation = 0, invoked = 0, adjusted = 0; bool large = false;
            var graph = Engine(new GraphState(), b =>
            {
                int version = ++generation;
                b.Add(Id("a"), new NodeVtable(Label("A")) { OnActivate = () => invoked = version, OnAdjust = (sign, big) => { adjusted = sign; large = big; } });
            });
            graph.Rerender(); Check(graph.Activate()); Equal(generation, invoked); Check(graph.TryAdjust(-8, true)); Equal(-1, adjusted); Check(large);
            Check(!graph.Tooltip() && !graph.Secondary() && !graph.ActivateShift() && !graph.ActivateCtrl());
        });
        T("search strips markup uses overrides and excludes other stops", () =>
        {
            var graph = Engine(new GraphState(), b =>
            {
                b.BeginStop("hand"); b.Add(Id("a"), V("<color=red>Огненный</color> удар"));
                b.Add(Id("b"), new NodeVtable(Label("Ice")) { SearchText = () => "ОГНЕННЫЙ резерв" });
                b.Add(Id("hidden"), new NodeVtable(Label("Огненный скрытый")) { ExcludeFromSearch = true });
                b.BeginStop("other"); b.Add(Id("other"), V("Огненный"));
            });
            Seq(new[] { "a", "b" }, graph.Search("огненный").Select(k => k.ToString())); Equal("A & B", KeyGraph.StripMarkup("<b>A &amp; B</b>"));
        });
    }

    private static void TreeTests()
    {
        T("tree right expands then descends, and left ascends then collapses", () =>
        {
            var state = new GraphState(); var graph = Engine(state, b =>
            { b.BeginGroup(Id("g"), V("Group")); Add(b, "a"); Add(b, "b"); b.EndGroup(); });
            Equal(TreeResultKind.Expanded, graph.TreeRight().Kind); Equal("g", state.CurKey.StructuralKey); Check(state.Expanded.Contains(Id("g")));
            Equal(TreeResultKind.Descended, graph.TreeRight().Kind); Equal("a", state.CurKey.StructuralKey);
            Equal(TreeResultKind.Leaf, graph.TreeRight().Kind); Equal(TreeResultKind.Ascended, graph.TreeLeft().Kind);
            Equal("g", state.CurKey.StructuralKey); Equal(TreeResultKind.Collapsed, graph.TreeLeft().Kind); Check(!state.Expanded.Contains(Id("g")));
        });
        T("empty tree groups automatically recollapse", () =>
        {
            var state = new GraphState(); var graph = Engine(state, b => { b.BeginGroup(Id("empty"), V("Empty")); b.EndGroup(); });
            TreeResult result = graph.TreeRight(); Equal(TreeResultKind.EmptyGroup, result.Kind); Check(result.Consumed);
            Check(!state.Expanded.Contains(Id("empty"))); Check(!graph.Current.Expanded);
        });
        T("explicit expansion delegates own native state", () =>
        {
            bool expanded = false; int opens = 0, closes = 0; var graph = Engine(new GraphState(), b =>
            {
                b.BeginGroup(Id("g"), new NodeVtable(Label("Group"))
                { OnExpand = () => { expanded = true; opens++; }, OnCollapse = () => { expanded = false; closes++; } }, expanded);
                Add(b, "child"); b.EndGroup();
            });
            graph.TreeRight(); Check(expanded); Equal(1, opens); graph.TreeLeft(); Check(!expanded); Equal(1, closes);
        });
        T("empty explicit groups invoke native recollapse handler", () =>
        {
            bool expanded = false; int closes = 0; var graph = Engine(new GraphState(), b =>
            {
                b.BeginGroup(Id("g"), new NodeVtable(Label("Empty")) { OnExpand = () => expanded = true, OnCollapse = () => { expanded = false; closes++; } }, expanded); b.EndGroup();
            });
            Equal(TreeResultKind.EmptyGroup, graph.TreeRight().Kind); Check(!expanded); Equal(1, closes);
        });
        T("tree operations bypass pure structural ancestors", () =>
        {
            var state = new GraphState(); state.Expanded.Add(Id("g")); var graph = Engine(state, b =>
            { b.BeginGroup(Id("g"), V("Group")); b.PushContext("Inner"); Add(b, "child"); b.PopContext(); b.EndGroup(); });
            graph.TreeRight(); Equal("child", state.CurKey.StructuralKey); graph.TreeLeft(); Equal("g", state.CurKey.StructuralKey);
        });
        T("tree expansion keeps children in another stop reachable by Tab", () =>
        {
            var graph = Engine(new GraphState(), b =>
            {
                b.BeginStop("header"); b.BeginGroup(Id("g"), V("Group")); b.BeginStop("children"); Add(b, "child"); b.EndGroup();
            });
            Equal(TreeResultKind.Expanded, graph.TreeRight().Kind); Check(graph.Current.Expanded);
            Equal(TreeResultKind.Leaf, graph.TreeRight().Kind); Equal("g", graph.State.CurKey.StructuralKey);
            graph.MoveStop(1); Equal("child", graph.State.CurKey.StructuralKey);
        });
        T("tree left cannot cross a stop through an ancestor", () =>
        {
            var graph = Engine(new GraphState(), b =>
            {
                b.BeginStop("header"); b.BeginGroup(Id("g"), V("Group"), true); b.BeginStop("children"); Add(b, "child"); b.EndGroup();
            });
            graph.Focus(Id("child")); TreeResult result = graph.TreeLeft(); Equal(TreeResultKind.Leaf, result.Kind);
            Check(!result.Move.Moved); Equal("child", graph.State.CurKey.StructuralKey);
        });
        T("home end remain at current parent depth and stop", () =>
        {
            var state = new GraphState(); state.Expanded.Add(Id("g")); state.Expanded.Add(Id("nested")); var graph = Engine(state, b =>
            {
                b.BeginGroup(Id("g"), V("Group")); Add(b, "a"); b.BeginGroup(Id("nested"), V("Nested")); Add(b, "deep"); b.EndGroup(); Add(b, "b"); b.EndGroup();
                b.BeginStop("other"); Add(b, "other");
            });
            graph.Focus(Id("a")); graph.MoveToSiblingEdge(false); Equal("b", state.CurKey.StructuralKey);
            graph.MoveToSiblingEdge(true); Equal("a", state.CurKey.StructuralKey);
            graph.Focus(Id("deep")); Check(!graph.MoveToSiblingEdge(false).Moved);
        });
        T("non-tree controls do not claim tree semantics", () =>
        {
            var graph = Engine(new GraphState(), b => Add(b, "plain")); Check(!graph.TreeLeft().Consumed); Equal(TreeResultKind.NotTree, graph.TreeRight().Kind);
        });
        T("transient empty render while expanding does not erase cursor or expansion", () =>
        {
            var state = new GraphState(); bool transition = false; var graph = new KeyGraph(state, () =>
            {
                if (transition) return null;
                return Build(state, b => { b.BeginGroup(Id("g"), new NodeVtable(Label("Group")) { OnExpand = () => { state.Expanded.Add(Id("g")); transition = true; } }); Add(b, "child"); b.EndGroup(); });
            });
            Equal(TreeResultKind.Expanded, graph.TreeRight().Kind); Equal("g", state.CurKey.StructuralKey); Check(state.Expanded.Contains(Id("g")));
            transition = false; graph.Rerender(); Check(graph.Current.Expanded);
        });
    }

    private static void AnnouncementTests()
    {
        T("type parts merge with own overrides and stable kind order", () =>
        {
            var type = new ControlType("test", new[] { "label", "role", "value" }, new[] { new NodeAnnouncement(() => "common role", "role"), new NodeAnnouncement(() => "common unknown", "x") });
            GraphNode node = Single(new NodeVtable(Label("Name"), new NodeAnnouncement(() => "own role", "role"), new NodeAnnouncement(() => "9", "value"), new NodeAnnouncement(() => "own unknown", "x2"), new NodeAnnouncement(() => "kindless")) { ControlType = type });
            Equal("Name, own role, 9, common unknown, own unknown, kindless", Announcer().ComposeNode(node));
        });
        T("node parts override common kind even when silent", () =>
        {
            var type = new ControlType("test", new[] { "label", "role" }, new[] { new NodeAnnouncement(() => "button", "role") });
            GraphNode node = Single(new NodeVtable(Label("Name"), new NodeAnnouncement(() => null, "role")) { ControlType = type });
            Equal("Name", Announcer().ComposeNode(node));
        });
        T("per type per kind filter feeds composed readout and live watch", () =>
        {
            GraphNode node = Single(new NodeVtable(Label("A"), new NodeAnnouncement(() => "selected", "selected", true), new NodeAnnouncement(() => "3", "value", true)) { ControlType = new ControlType("card") });
            var announcer = new GraphAnnouncer(new AnnouncementSettings { SpeakPositions = false, IncludePart = (type, kind) => type != "card" || kind != "selected" });
            Equal("A, 3", announcer.ComposeNode(node)); Equal(1, announcer.ResolveLiveParts(node).Count); Equal("value", announcer.ResolveLiveParts(node)[0].Kind);
        });
        T("live closures resolve each composition without cached text", () =>
        {
            string value = "old"; GraphNode node = Single(new NodeVtable(Label("Name"), new NodeAnnouncement(() => value, "value", true)));
            var announcer = Announcer(); Equal("Name, old", announcer.ComposeNode(node)); value = "new";
            Equal("Name, new", announcer.ComposeNode(node)); Equal("new", announcer.ResolveLiveParts(node)[0].Text);
            value = null; Equal("Name", announcer.ComposeNode(node)); Equal("", announcer.ResolveLiveParts(node)[0].Text);
        });
        T("live part keys survive newly allocated closures and duplicated kinds", () =>
        {
            Func<GraphNode> factory = () => Single(new NodeVtable(Label("Name"), new NodeAnnouncement(() => "one", "value", true), new NodeAnnouncement(() => "two", "value", true), new NodeAnnouncement(() => "anonymous", null, true)));
            var announcer = Announcer(); var first = announcer.ResolveLiveParts(factory()); var second = announcer.ResolveLiveParts(factory());
            Seq(first.Select(p => p.Key), second.Select(p => p.Key)); Equal(3, first.Select(p => p.Key).Distinct().Count());
        });
        T("context entry speaks hierarchy outermost first", () =>
        {
            GraphRender r = Build(new GraphState(), b => { b.PushContext("Difficulty", "list"); b.Add(Id("normal"), new NodeVtable(Label("Normal"), new NodeAnnouncement(() => "radio button", "role"), new NodeAnnouncement(() => "selected", "selected"))); b.PopContext(); });
            Equal("Difficulty, list, Normal, radio button, selected", Announcer().Compose(null, r.Nodes[Id("normal")]));
        });
        T("sibling transitions speak only the new control", () =>
        {
            GraphRender r = Build(new GraphState(), b => { b.PushContext("Panel"); Add(b, "a"); Add(b, "b"); b.PopContext(); });
            Equal("b", Announcer().Compose(r.Nodes[Id("a")], r.Nodes[Id("b")]));
        });
        T("descending does not repeat the group header", () =>
        {
            GraphRender r = Build(new GraphState(), b => { b.BeginGroup(Id("g"), V("Group"), true); Add(b, "child"); b.EndGroup(); });
            Equal("child", Announcer().Compose(r.Nodes[Id("g")], r.Nodes[Id("child")]));
        });
        T("ascending and same-node composition speak own readout", () =>
        {
            GraphRender r = Build(new GraphState(), b => { b.BeginGroup(Id("g"), V("Group"), true); Add(b, "child"); b.EndGroup(); });
            Equal("Group, expanded", Announcer().Compose(r.Nodes[Id("child")], r.Nodes[Id("g")]));
            Equal("child", Announcer().Compose(r.Nodes[Id("child")], r.Nodes[Id("child")]));
        });
        T("reparented same-key leaf reads its new hierarchy", () =>
        {
            GraphRender before = Build(new GraphState(), b => { b.PushContext("Old"); Add(b, "leaf"); b.PopContext(); });
            GraphRender after = Build(new GraphState(), b => { b.PushContext("New"); Add(b, "leaf"); b.PopContext(); });
            Equal("New, leaf", Announcer().Compose(before.Nodes[Id("leaf")], after.Nodes[Id("leaf")]));
        });
        T("equal labels with different identity do not merge paths", () =>
        {
            GraphRender r = Build(new GraphState(), b =>
            {
                b.BeginStop("one"); b.PushContext("Same", "list"); Add(b, "a"); b.PopContext();
                b.BeginStop("two"); b.PushContext("Same", "list"); Add(b, "b"); b.PopContext();
            });
            Equal("Same, list, b", Announcer().Compose(r.Nodes[Id("a")], r.Nodes[Id("b")]));
        });
        T("duplicate wrapper label is removed", () =>
        {
            GraphRender r = Build(new GraphState(), b => { b.PushContext("Game difficulty", "panel"); b.Add(Id("g"), new NodeVtable(Label("Game difficulty"), new NodeAnnouncement(() => "slider", "role"))); b.PopContext(); });
            Equal("Game difficulty, slider", Announcer().Compose(null, r.Nodes[Id("g")]));
        });
        T("wrapper deduplication also recognizes a readout prefix", () =>
        {
            GraphRender r = Build(new GraphState(), b => { b.PushContext("Difficulty"); b.Add(Id("g"), V("Difficulty, Normal")); b.PopContext(); });
            Equal("Difficulty, Normal", Announcer().Compose(null, r.Nodes[Id("g")]));
        });
        T("transition label precedes all changed hierarchy", () =>
        {
            GraphNode node = Single(V("Sword")); Equal("Equipment, Sword", Announcer().Compose(null, node, "Equipment"));
        });
        T("position wording is localized and suppressible", () =>
        {
            GraphRender r = Build(new GraphState(), b => { Add(b, "a"); Add(b, "b"); });
            var announcer = new GraphAnnouncer(new AnnouncementSettings { PositionText = (i, n) => i + " из " + n });
            Equal("b, 2 из 2", announcer.ComposeNode(r.Nodes[Id("b")])); announcer.Settings.SpeakPositions = false;
            Equal("b", announcer.ComposeNode(r.Nodes[Id("b")]));
        });
        T("authored silent position still prevents duplicate automatic position", () =>
        {
            GraphRender r = Build(new GraphState(), b => { Add(b, "a"); b.Add(Id("b"), new NodeVtable(Label("B"), new NodeAnnouncement(() => null, "position"))); });
            Equal("B", new GraphAnnouncer().ComposeNode(r.Nodes[Id("b")]));
        });
        T("own expansion wording is never duplicated", () =>
        {
            GraphRender r = Build(new GraphState(), b =>
            { b.BeginGroup(Id("g"), new NodeVtable(Label("Group"), new NodeAnnouncement(() => "folded", "expanded")) { SpeaksOwnExpansion = true }); b.EndGroup(); });
            Equal("Group, folded", Announcer().ComposeNode(r.Nodes[Id("g")]));
        });
        T("localized expansion and singleton positions are correct", () =>
        {
            GraphRender r = Build(new GraphState(), b => { b.BeginGroup(Id("g"), V("Группа")); b.EndGroup(); });
            var announcer = new GraphAnnouncer(new AnnouncementSettings { ExpansionText = expanded => expanded ? "развёрнуто" : "свёрнуто" });
            Equal("Группа, свёрнуто", announcer.ComposeNode(r.Nodes[Id("g")]));
        });
    }

    private static ControlId Id(string key) { return ControlId.Structural(key); }
    private static NodeAnnouncement Label(string text) { return new NodeAnnouncement(() => text, "label"); }
    private static NodeVtable V(string label) { return new NodeVtable(Label(label)); }
    private static NodeVtable SelectedV(string label, bool selected) { return new NodeVtable(Label(label), new NodeAnnouncement(() => selected ? "selected" : null, "selected")); }
    private static GraphNode Add(GraphBuilder b, string key) { return b.Add(Id(key), V(key)); }
    private static GraphNode Raw(GraphBuilder b, string key) { return b.AddNode(Id(key), V(key)); }
    private static void Row(GraphBuilder b, object rowKey, params string[] keys) { b.BeginRow(rowKey); foreach (string key in keys) Add(b, key); b.EndRow(); }
    private static GraphRender Build(GraphState state, Action<GraphBuilder> recipe) { var b = new GraphBuilder(state); recipe(b); return b.Build(); }
    private static KeyGraph Engine(GraphState state, Action<GraphBuilder> recipe) { return new KeyGraph(state, () => Build(state, recipe)); }
    private static GraphNode Single(NodeVtable vtable) { return Build(new GraphState(), b => b.Add(Id("single"), vtable)).Nodes[Id("single")]; }
    private static GraphAnnouncer Announcer() { return new GraphAnnouncer(new AnnouncementSettings { SpeakPositions = false }); }
    private static void Edge(GraphRender render, string from, Direction direction, string to)
    {
        GraphEdge edge; Check(render.Nodes[Id(from)].Transitions.TryGetValue(direction, out edge), "Missing " + from + " " + direction);
        Equal(to, edge.Destination.StructuralKey);
    }
    private static void Missing(GraphRender render, string from, Direction direction) { Check(!render.Nodes[Id(from)].Transitions.ContainsKey(direction), "Unexpected edge " + from + " " + direction); }
    private static void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception("Expected [" + expected + "], got [" + actual + "]"); }
    private static void Seq<T>(IEnumerable<T> expected, IEnumerable<T> actual) { Check(expected.SequenceEqual(actual), "Different sequence: " + string.Join(",", actual)); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name);
    }
    private static void T(string name, Action test)
    {
        try { test(); _passed++; if (_verbose) Console.WriteLine("PASS " + name); }
        catch (Exception ex) { _failed++; Console.Error.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private sealed class ValueObject
    {
        private readonly int _value;
        public ValueObject(int value) { _value = value; }
        public override bool Equals(object obj) { return obj is ValueObject && ((ValueObject)obj)._value == _value; }
        public override int GetHashCode() { return _value; }
    }
}
