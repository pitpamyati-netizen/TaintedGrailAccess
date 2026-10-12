using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace TaintedGrailAccess.Graph
{
    /// <summary>
    /// Pure, silent navigation. Each operation first rebuilds. The host isolates Build exceptions,
    /// supplies a null render during transitions, and owns input, search sessions and speech.
    /// </summary>
    public sealed class KeyGraph
    {
        private readonly Func<GraphRender> _render;
        private static readonly Regex RichText = new Regex(@"</?[A-Za-z][^>]*>|<#[0-9A-Fa-f]{6,8}>", RegexOptions.CultureInvariant);
        public GraphState State { get; private set; }
        public GraphRender Render { get; private set; }
        public GraphNode Current
        {
            get
            {
                GraphNode node;
                return Render != null && State.CurKey != null && Render.Nodes.TryGetValue(State.CurKey, out node) && node.Focusable ? node : null;
            }
        }

        public KeyGraph(GraphState state, Func<GraphRender> render)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            _render = render ?? throw new ArgumentNullException(nameof(render));
        }

        /// <summary>reconcile:false permits a host-owned unfocused overlay without seating a cursor.</summary>
        public GraphRender Rerender(bool reconcile = true)
        {
            Render = _render();
            if (Render != null && reconcile) Reconcile();
            return Render;
        }

        public MoveResult Move(Direction direction)
        {
            Rerender();
            GraphNode from = Current;
            GraphNode to = Follow(from, direction);
            GraphEdge edge;
            string label = from != null && from.Transitions.TryGetValue(direction, out edge) ? edge.Label : null;
            return Land(from, to ?? from, label);
        }

        /// <summary>Visits each node at most once even in cycles; carries the last nonempty crossed label.</summary>
        public MoveResult MoveToEdge(Direction direction)
        {
            Rerender();
            GraphNode from = Current, to = from;
            string label = null;
            var seen = new HashSet<ControlId>();
            if (from != null) seen.Add(from.Id);
            while (to != null)
            {
                GraphNode next = Follow(to, direction);
                if (next == null || !seen.Add(next.Id)) break;
                GraphEdge edge = to.Transitions[direction];
                if (!string.IsNullOrEmpty(edge.Label)) label = edge.Label;
                to = next;
            }
            return Land(from, to, label);
        }

        public MoveResult MoveStop(int sign, bool wrap = true)
        {
            Rerender();
            GraphNode from = Current;
            if (from == null || sign == 0) return Stay(from);
            List<object> stops = DistinctKeys(false, from.StopKey);
            int index = stops.FindIndex(key => Equals(key, from.StopKey));
            int next = index + Math.Sign(sign);
            if (next < 0 || next >= stops.Count)
            {
                if (!wrap) return Stay(from);
                next = (next + stops.Count) % stops.Count;
            }
            object destination = stops[next];
            ControlId remembered;
            GraphNode to = State.StopMemory.TryGetValue(destination, out remembered) ? Resolve(remembered, destination, true) : null;
            to = to ?? Selected(destination) ?? First(destination, null, false);
            return Land(from, to ?? from);
        }

        /// <summary>Regions wrap inside the current stop. An untagged node does not claim a region jump.</summary>
        public MoveResult MoveRegion(int sign)
        {
            Rerender();
            GraphNode from = Current;
            if (from == null || from.RegionKey == null || sign == 0) return Stay(from);
            List<object> regions = DistinctKeys(true, from.StopKey);
            if (regions.Count < 2) return Stay(from);
            int index = regions.FindIndex(key => Equals(key, from.RegionKey));
            int next = (index + Math.Sign(sign) + regions.Count) % regions.Count;
            return Land(from, First(from.StopKey, regions[next], true) ?? from);
        }

        public MoveResult Focus(ControlId id)
        {
            Rerender();
            GraphNode from = Current;
            return Land(from, Resolve(id) ?? from);
        }

        public MoveResult FocusByReference(object reference)
        {
            Rerender();
            GraphNode from = Current, to = null;
            if (Render != null && reference != null)
                foreach (ControlId key in Render.Order)
                {
                    GraphNode node = Render.Nodes[key];
                    if (node.Focusable && ReferenceEquals(node.Id.Reference, reference)) { to = node; break; }
                }
            return Land(from, to ?? from);
        }

        public MoveResult MoveToSiblingEdge(bool first)
        {
            Rerender();
            GraphNode from = Current, to = null;
            if (from == null) return Stay(null);
            foreach (ControlId key in Render.Order)
            {
                GraphNode node = Render.Nodes[key];
                if (!node.Focusable || !Equals(node.StopKey, from.StopKey) || !SameParent(node, from)) continue;
                to = node;
                if (first) break;
            }
            return Land(from, to ?? from);
        }

        public TreeResult TreeRight()
        {
            Rerender();
            GraphNode from = Current;
            if (from == null) return Tree(TreeResultKind.NoFocus, from);
            if (!InTree(from)) return Tree(TreeResultKind.NotTree, from);
            if (!from.Expandable) return Tree(TreeResultKind.Leaf, from);
            if (from.Expanded)
            {
                GraphNode child = FirstDescendant(from);
                return child == null ? Tree(TreeResultKind.Leaf, from) : new TreeResult(TreeResultKind.Descended, Land(from, child));
            }

            Expand(from);
            Rerender();
            // A transient empty render is not evidence that the group has no children.
            if (Render == null) return Tree(TreeResultKind.Expanded, from);
            GraphNode header = Resolve(from.Id);
            if (header != null && FirstDescendant(header, false) == null)
            {
                Collapse(header);
                Rerender();
                GraphNode restored = Resolve(header.Id) ?? Current;
                return new TreeResult(TreeResultKind.EmptyGroup, Land(from, restored ?? from));
            }
            return new TreeResult(TreeResultKind.Expanded, Land(from, header ?? Current ?? from));
        }

        public TreeResult TreeLeft()
        {
            Rerender();
            GraphNode from = Current;
            if (from == null) return Tree(TreeResultKind.NoFocus, from);
            if (!InTree(from)) return Tree(TreeResultKind.NotTree, from);
            if (from.Expandable && from.Expanded)
            {
                Collapse(from);
                Rerender();
                return new TreeResult(TreeResultKind.Collapsed, Land(from, Resolve(from.Id) ?? Current ?? from));
            }
            GraphNode parent = from.Parent;
            while (parent != null && (!parent.Focusable || !Equals(parent.StopKey, from.StopKey))) parent = parent.Parent;
            return parent == null ? Tree(TreeResultKind.Leaf, from) : new TreeResult(TreeResultKind.Ascended, Land(from, parent));
        }

        public bool Activate() { return Invoke(node => node.OnActivate); }
        public bool Secondary() { return Invoke(node => node.OnSecondary); }
        public bool ActivateShift() { return Invoke(node => node.OnActivateShift); }
        public bool ActivateCtrl() { return Invoke(node => node.OnActivateCtrl); }
        public bool Tooltip() { return Invoke(node => node.OnTooltip); }

        public bool TryAdjust(int sign, bool large)
        {
            Rerender();
            GraphNode node = Current;
            if (node == null || node.Vtable.OnAdjust == null) return false;
            node.Vtable.OnAdjust(Math.Sign(sign), large);
            return true;
        }

        /// <summary>Case-insensitive substring search, scoped to one stop and in declaration order.</summary>
        public IReadOnlyList<ControlId> Search(string query)
        {
            Rerender();
            var result = new List<ControlId>();
            GraphNode current = Current;
            if (current == null) return result.AsReadOnly();
            string needle = StripMarkup(query ?? string.Empty);
            foreach (ControlId key in Render.Order)
            {
                GraphNode node = Render.Nodes[key];
                if (!node.Focusable || node.Vtable.ExcludeFromSearch || !Equals(node.StopKey, current.StopKey)) continue;
                Func<string> searchText = node.Vtable.SearchText ?? node.Vtable.Announcements[0].Text;
                string text = StripMarkup(searchText() ?? string.Empty);
                if (text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) result.Add(node.Id);
            }
            return result.AsReadOnly();
        }

        public static string StripMarkup(string text)
        {
            return string.IsNullOrEmpty(text) ? string.Empty : WebUtility.HtmlDecode(RichText.Replace(text, string.Empty));
        }

        public static bool InTree(GraphNode node)
        {
            for (GraphNode part = node; part != null; part = part.Parent) if (part.Expandable) return true;
            return false;
        }

        /// <summary>Down-right traversal followed by every unvisited focusable declaration.</summary>
        public static IReadOnlyList<ControlId> ComputeOrder(GraphRender render)
        {
            var result = new List<ControlId>();
            if (render == null) return result.AsReadOnly();
            var seen = new HashSet<ControlId>();
            var fringe = new List<ControlId> { render.StartKey };
            for (int i = 0; i < fringe.Count; i++)
            {
                ControlId key = fringe[i];
                GraphNode node;
                while (key != null && render.Nodes.TryGetValue(key, out node) && node.Focusable && seen.Add(node.Id))
                {
                    result.Add(node.Id);
                    GraphEdge edge;
                    if (node.Transitions.TryGetValue(Direction.Down, out edge)) fringe.Add(edge.Destination);
                    key = node.Transitions.TryGetValue(Direction.Right, out edge) ? edge.Destination : null;
                }
            }
            foreach (ControlId key in render.Order)
                if (render.Nodes[key].Focusable && seen.Add(key)) result.Add(key);
            return result.AsReadOnly();
        }

        private void Reconcile()
        {
            GraphNode resolved = null;
            if (State.NextSuggestedMove != null)
            {
                resolved = Resolve(State.NextSuggestedMove);
                State.NextSuggestedMove = null;
            }
            if (resolved == null && State.CurKey != null)
            {
                resolved = Resolve(State.CurKey);
                if (resolved == null)
                {
                    int index = -1;
                    for (int i = 0; i < State.KeyOrder.Count; i++)
                        if (State.KeyOrder[i].Equals(State.CurKey)) { index = i; break; }
                    // Use PREVIOUS order, walk backward only, and permit a surviving object to have moved.
                    for (int i = index - 1; i >= 0 && resolved == null; i--) resolved = Resolve(State.KeyOrder[i]);
                }
            }
            if (resolved == null)
            {
                GraphNode start;
                if (Render.Nodes.TryGetValue(Render.StartKey, out start)) resolved = Selected(start.StopKey) ?? start;
            }
            if (resolved != null) Remember(resolved);
            State.KeyOrder = ComputeOrder(Render);
        }

        private GraphNode Resolve(ControlId id, object stop = null, bool restrictStop = false)
        {
            if (id == null || Render == null) return null;
            if (id.Reference != null)
                foreach (ControlId key in Render.Order)
                {
                    GraphNode node = Render.Nodes[key];
                    if (node.Focusable && (!restrictStop || Equals(node.StopKey, stop)) && ReferenceEquals(node.Id.Reference, id.Reference)) return node;
                }
            GraphNode structural;
            return Render.Nodes.TryGetValue(id, out structural) && structural.Focusable && (!restrictStop || Equals(structural.StopKey, stop)) ? structural : null;
        }

        private GraphNode Follow(GraphNode from, Direction direction)
        {
            if (from == null || Render == null) return null;
            GraphEdge edge;
            GraphNode node;
            return from.Transitions.TryGetValue(direction, out edge) && Render.Nodes.TryGetValue(edge.Destination, out node)
                && node.Focusable && Equals(from.StopKey, node.StopKey) ? node : null;
        }

        private GraphNode Selected(object stop)
        {
            foreach (ControlId key in Render.Order)
            {
                GraphNode node = Render.Nodes[key];
                if (!node.Focusable || !Equals(node.StopKey, stop)) continue;
                bool overridesCommonSelection = false;
                foreach (NodeAnnouncement part in node.Vtable.Announcements)
                    if (part.Kind == "selected")
                    {
                        overridesCommonSelection = true;
                        if (!string.IsNullOrEmpty(part.Text())) return node;
                    }
                if (!overridesCommonSelection && node.Vtable.ControlType != null)
                    foreach (NodeAnnouncement part in node.Vtable.ControlType.Common)
                        if (part.Kind == "selected" && !string.IsNullOrEmpty(part.Text())) return node;
            }
            return null;
        }

        private GraphNode First(object stop, object region, bool restrictRegion)
        {
            foreach (ControlId key in Render.Order)
            {
                GraphNode node = Render.Nodes[key];
                if (node.Focusable && Equals(node.StopKey, stop) && (!restrictRegion || Equals(node.RegionKey, region))) return node;
            }
            return null;
        }

        private List<object> DistinctKeys(bool regions, object stop)
        {
            var keys = new List<object>();
            var seen = new HashSet<object>();
            foreach (ControlId id in Render.Order)
            {
                GraphNode node = Render.Nodes[id];
                if (!node.Focusable || (regions && !Equals(node.StopKey, stop))) continue;
                object key = regions ? node.RegionKey : node.StopKey;
                if (key != null && seen.Add(key)) keys.Add(key);
            }
            return keys;
        }

        private GraphNode FirstDescendant(GraphNode parent, bool sameStop = true)
        {
            foreach (ControlId id in Render.Order)
            {
                GraphNode candidate = Render.Nodes[id];
                if (!candidate.Focusable || (sameStop && !Equals(candidate.StopKey, parent.StopKey))) continue;
                for (GraphNode ancestor = candidate.Parent; ancestor != null; ancestor = ancestor.Parent)
                    if (ancestor.Id.Equals(parent.Id)) return candidate;
            }
            return null;
        }

        private static bool SameParent(GraphNode left, GraphNode right)
        {
            return left.Parent == null ? right.Parent == null : right.Parent != null && left.Parent.Id.Equals(right.Parent.Id);
        }

        private void Expand(GraphNode node)
        {
            if (node.Vtable.OnExpand != null) node.Vtable.OnExpand();
            else State.Expanded.Add(node.Id);
        }
        private void Collapse(GraphNode node)
        {
            if (node.Vtable.OnCollapse != null) node.Vtable.OnCollapse();
            else State.Expanded.Remove(node.Id);
        }
        private bool Invoke(Func<NodeVtable, Action> slot)
        {
            Rerender();
            GraphNode node = Current;
            Action action = node == null ? null : slot(node.Vtable);
            if (action == null) return false;
            action();
            return true;
        }

        private MoveResult Land(GraphNode from, GraphNode to, string label = null)
        {
            if (to != null && Render != null)
            {
                GraphNode current;
                if (Render.Nodes.TryGetValue(to.Id, out current) && current.Focusable) Remember(current);
            }
            return new MoveResult(from, to, label);
        }
        private void Remember(GraphNode node)
        {
            State.CurKey = node.Id;
            State.StopMemory[node.StopKey] = node.Id;
        }
        private static MoveResult Stay(GraphNode from) { return new MoveResult(from, from); }
        private static TreeResult Tree(TreeResultKind kind, GraphNode from) { return new TreeResult(kind, Stay(from)); }
    }
}
