using System;
using System.Collections.Generic;

namespace TaintedGrailAccess.Graph
{
    /// <summary>One declaration pass, one snapshot. Allocate a fresh builder for every render.</summary>
    public sealed class GraphBuilder
    {
        private readonly GraphState _state;
        private readonly Dictionary<ControlId, GraphNode> _nodes = new Dictionary<ControlId, GraphNode>();
        private readonly List<ControlId> _order = new List<ControlId>();
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<PendingEdge> _pending = new List<PendingEdge>();
        private readonly Stack<Scope> _scopes = new Stack<Scope>();
        private Row _row;
        private object _stop = new AutoStopKey(0);
        private object _region;
        private int _nextAutoStop;
        private bool _implicitStopUsed;
        private bool _built;
        private ControlId _requestedStart;

        public GraphBuilder(GraphState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public void BeginStop(object key = null)
        {
            EnsureMutable();
            if (_row != null) throw new InvalidOperationException("Close the current row before starting a stop.");
            if (_implicitStopUsed && _nextAutoStop == 0) _nextAutoStop = 1;
            _stop = key ?? new AutoStopKey(_nextAutoStop);
            _nextAutoStop++;
            _region = null;
        }

        public void SetRegion(object key) { EnsureMutable(); _region = key; }
        public void SetStart(ControlId id) { EnsureMutable(); _requestedStart = id; }

        public void BeginRow(object key = null)
        {
            EnsureMutable();
            if (_row != null) throw new InvalidOperationException("A menu row is already open.");
            _row = new Row(key);
        }

        public void EndRow()
        {
            EnsureMutable();
            if (_row == null) throw new InvalidOperationException("No menu row is open.");
            _row = null;
        }

        public GraphNode Add(ControlId id, NodeVtable vtable)
        {
            EnsureMutable();
            ValidateArguments(id, vtable);
            if (Suppressed) return null;
            GraphNode node = Declare(id, vtable, true);
            Row row = _row ?? new Row(null);
            if (row.Nodes.Count == 0)
            {
                row.Stop = _stop;
                _entries.Add(new Entry(row));
            }
            row.Nodes.Add(node);
            return node;
        }

        public GraphNode AddNode(ControlId id, NodeVtable vtable)
        {
            EnsureMutable();
            ValidateArguments(id, vtable);
            if (_row != null) throw new InvalidOperationException("Raw nodes must be outside an explicit menu row.");
            if (Suppressed) return null;
            GraphNode node = Declare(id, vtable, true);
            _entries.Add(new Entry(node));
            return node;
        }

        /// <summary>Explicit edges win. Missing, non-focusable and cross-stop endpoints are discarded.</summary>
        public void Connect(ControlId from, Direction direction, ControlId to, string label = null)
        {
            EnsureMutable();
            if (from == null) throw new ArgumentNullException(nameof(from));
            if (to == null) throw new ArgumentNullException(nameof(to));
            if (Suppressed) return;
            _pending.Add(new PendingEdge(from, direction, to, label));
        }

        public GraphNode PushContext(string label, string role = null, bool suppressChildPositions = false)
        {
            EnsureMutable();
            if (label == null) throw new ArgumentNullException(nameof(label));
            bool suppressed = Suppressed;
            GraphNode node = null;
            if (!suppressed)
            {
                var parts = new List<NodeAnnouncement> { new NodeAnnouncement(() => label, "label") };
                if (!string.IsNullOrEmpty(role)) parts.Add(new NodeAnnouncement(() => role, "role"));
                object parentKey = Parent == null ? null : Parent.Id.StructuralKey;
                node = Declare(ControlId.Structural(new ContextKey(_stop, parentKey, label)), new NodeVtable(parts.ToArray()), false);
                node.SuppressChildPositions = suppressChildPositions;
            }
            _scopes.Push(new Scope(node, false, suppressed));
            return node;
        }

        public void PopContext()
        {
            EnsureMutable();
            if (_scopes.Count == 0 || _scopes.Peek().Group) throw new InvalidOperationException("The current scope is not a context.");
            _scopes.Pop();
        }

        /// <summary>Implicit expansion defaults to collapsed and is owned solely by GraphState.Expanded.</summary>
        public GraphNode BeginGroup(ControlId id, NodeVtable vtable, bool? expanded = null, bool suppressChildPositions = false)
        {
            EnsureMutable();
            bool suppressed = Suppressed;
            GraphNode node = Add(id, vtable);
            if (node != null)
            {
                node.Expandable = true;
                node.Expanded = expanded ?? _state.Expanded.Contains(id);
                node.SuppressChildPositions = suppressChildPositions;
            }
            _scopes.Push(new Scope(node, true, suppressed || node == null || !node.Expanded));
            return node;
        }

        public void EndGroup()
        {
            EnsureMutable();
            if (_scopes.Count == 0 || !_scopes.Peek().Group) throw new InvalidOperationException("The current scope is not a group.");
            _scopes.Pop();
        }

        public GraphRender Build()
        {
            EnsureMutable();
            if (_row != null) throw new InvalidOperationException("An unclosed menu row remains.");
            if (_scopes.Count != 0) throw new InvalidOperationException("An unclosed context or group remains.");
            foreach (GraphNode node in _nodes.Values)
            {
                if (node.Vtable.Announcements.Count == 0) throw new InvalidOperationException("Every node requires an announcement: " + node.Id);
                foreach (NodeAnnouncement part in node.Vtable.Announcements)
                    if (part == null) throw new InvalidOperationException("An announcement cannot be null: " + node.Id);
                if (node.Vtable.ControlType != null)
                    foreach (NodeAnnouncement part in node.Vtable.ControlType.Common)
                        if (part == null) throw new InvalidOperationException("A type-common announcement cannot be null.");
            }
            _built = true;
            ControlId first = null;
            foreach (ControlId key in _order)
            {
                if (_nodes[key].Focusable) { first = key; break; }
            }
            if (first == null) return null;

            foreach (PendingEdge edge in _pending)
            {
                GraphNode from, to;
                if (_nodes.TryGetValue(edge.From, out from) && _nodes.TryGetValue(edge.To, out to)
                    && from.Focusable && to.Focusable && Equals(from.StopKey, to.StopKey))
                    from.SetEdge(edge.Direction, new GraphEdge(to.Id, edge.Label));
            }

            var stops = new Dictionary<object, List<Entry>>();
            foreach (Entry entry in _entries)
            {
                List<Entry> entries;
                if (!stops.TryGetValue(entry.Stop, out entries))
                {
                    entries = new List<Entry>();
                    stops.Add(entry.Stop, entries);
                }
                entries.Add(entry);
            }
            foreach (List<Entry> entries in stops.Values) WireStop(entries);
            StampPositions();

            GraphNode start;
            if (_requestedStart != null && _nodes.TryGetValue(_requestedStart, out start) && start.Focusable) first = start.Id;
            return new GraphRender(_nodes, _order, first);
        }

        private GraphNode Declare(ControlId id, NodeVtable vtable, bool focusable)
        {
            if (_nodes.ContainsKey(id)) throw new InvalidOperationException("Duplicate control identity: " + id);
            GraphNode node = new GraphNode(id, vtable)
            {
                Parent = Parent,
                Focusable = focusable,
                StopKey = _stop,
                RegionKey = _region
            };
            _nodes.Add(id, node);
            _order.Add(id);
            if (_nextAutoStop == 0) _implicitStopUsed = true;
            return node;
        }

        private void WireStop(List<Entry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                Row row = entries[i].Row;
                if (row == null) continue;
                for (int column = 0; column < row.Nodes.Count; column++)
                {
                    if (column > 0) Fill(row.Nodes[column], Direction.Left, row.Nodes[column - 1]);
                    if (column + 1 < row.Nodes.Count) Fill(row.Nodes[column], Direction.Right, row.Nodes[column + 1]);
                }
                // A raw block interrupts vertical row chaining; never jump past it.
                if (i + 1 < entries.Count && entries[i + 1].Row != null)
                    WireRows(row, entries[i + 1].Row);
            }

            for (int i = 0; i + 1 < entries.Count; i++)
            {
                if (entries[i].Row != null && entries[i + 1].Raw != null)
                {
                    GraphNode target = null;
                    for (int j = i + 1; j < entries.Count && entries[j].Raw != null; j++)
                        if (!entries[j].Raw.HasEdge(Direction.Up)) { target = entries[j].Raw; break; }
                    Stitch(entries[i].Row, Direction.Down, target, Direction.Up);
                }
                else if (entries[i].Raw != null && entries[i + 1].Row != null)
                {
                    GraphNode target = null;
                    for (int j = i; j >= 0 && entries[j].Raw != null; j--)
                        if (!entries[j].Raw.HasEdge(Direction.Down)) { target = entries[j].Raw; break; }
                    Stitch(entries[i + 1].Row, Direction.Up, target, Direction.Down);
                }
            }
        }

        private static void WireRows(Row upper, Row lower)
        {
            bool columns = upper.Key != null && Equals(upper.Key, lower.Key);
            for (int i = 0; i < upper.Nodes.Count; i++)
                Fill(upper.Nodes[i], Direction.Down, lower.Nodes[columns && i < lower.Nodes.Count ? i : 0]);
            for (int i = 0; i < lower.Nodes.Count; i++)
                Fill(lower.Nodes[i], Direction.Up, upper.Nodes[columns && i < upper.Nodes.Count ? i : 0]);
        }

        private static void Stitch(Row menu, Direction toRaw, GraphNode raw, Direction toMenu)
        {
            if (raw == null) return;
            GraphNode backlink = null;
            foreach (GraphNode cell in menu.Nodes)
            {
                if (!cell.HasEdge(toRaw))
                {
                    Fill(cell, toRaw, raw);
                    if (backlink == null) backlink = cell;
                }
            }
            if (backlink != null) Fill(raw, toMenu, backlink);
        }

        private static void Fill(GraphNode from, Direction direction, GraphNode to)
        {
            if (!from.HasEdge(direction) && Equals(from.StopKey, to.StopKey)) from.SetEdge(direction, new GraphEdge(to.Id));
        }

        private void StampPositions()
        {
            var lists = new Dictionary<SiblingKey, List<GraphNode>>();
            foreach (Entry entry in _entries)
            {
                if (entry.Row == null) continue;
                List<GraphNode> cells = entry.Row.Nodes;
                if (cells.Count > 1)
                {
                    for (int i = 0; i < cells.Count; i++) Stamp(cells[i], i + 1, cells.Count);
                }
                else
                {
                    GraphNode node = cells[0];
                    var key = new SiblingKey(node.Parent == null ? null : node.Parent.Id, node.StopKey);
                    List<GraphNode> siblings;
                    if (!lists.TryGetValue(key, out siblings))
                    {
                        siblings = new List<GraphNode>();
                        lists.Add(key, siblings);
                    }
                    siblings.Add(node);
                }
            }
            foreach (List<GraphNode> siblings in lists.Values)
                for (int i = 0; i < siblings.Count; i++) Stamp(siblings[i], i + 1, siblings.Count);
        }

        private static void Stamp(GraphNode node, int index, int count)
        {
            if (node.Parent != null && node.Parent.SuppressChildPositions) return;
            node.PositionIndex = index;
            node.PositionCount = count;
        }

        private bool Suppressed { get { return _scopes.Count > 0 && _scopes.Peek().Suppressed; } }
        private GraphNode Parent { get { return _scopes.Count == 0 ? null : _scopes.Peek().Node; } }
        private void EnsureMutable() { if (_built) throw new InvalidOperationException("A builder can be built only once."); }
        private static void ValidateArguments(ControlId id, NodeVtable vtable)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            if (vtable == null) throw new ArgumentNullException(nameof(vtable));
        }

        private sealed class Scope
        {
            public readonly GraphNode Node;
            public readonly bool Group;
            public readonly bool Suppressed;
            public Scope(GraphNode node, bool group, bool suppressed) { Node = node; Group = group; Suppressed = suppressed; }
        }
        private sealed class Row
        {
            public readonly object Key;
            public object Stop;
            public readonly List<GraphNode> Nodes = new List<GraphNode>();
            public Row(object key) { Key = key; }
        }
        private sealed class Entry
        {
            public readonly Row Row;
            public readonly GraphNode Raw;
            public object Stop { get { return Row != null ? Row.Stop : Raw.StopKey; } }
            public Entry(Row row) { Row = row; }
            public Entry(GraphNode raw) { Raw = raw; }
        }
        private sealed class PendingEdge
        {
            public readonly ControlId From, To;
            public readonly Direction Direction;
            public readonly string Label;
            public PendingEdge(ControlId from, Direction direction, ControlId to, string label)
            { From = from; Direction = direction; To = to; Label = label; }
        }
        private sealed class AutoStopKey : IEquatable<AutoStopKey>
        {
            private readonly int _index;
            public AutoStopKey(int index) { _index = index; }
            public bool Equals(AutoStopKey other) { return other != null && _index == other._index; }
            public override bool Equals(object obj) { return Equals(obj as AutoStopKey); }
            public override int GetHashCode() { return _index; }
            public override string ToString() { return "auto-stop:" + _index; }
        }
        private sealed class ContextKey : IEquatable<ContextKey>
        {
            private readonly object _stop, _parent;
            private readonly string _label;
            public ContextKey(object stop, object parent, string label) { _stop = stop; _parent = parent; _label = label; }
            public bool Equals(ContextKey other)
            { return other != null && Equals(_stop, other._stop) && Equals(_parent, other._parent) && _label == other._label; }
            public override bool Equals(object obj) { return Equals(obj as ContextKey); }
            public override int GetHashCode()
            { unchecked { return ((_stop.GetHashCode() * 397) ^ (_parent == null ? 0 : _parent.GetHashCode())) * 397 ^ _label.GetHashCode(); } }
            public override string ToString() { return "context:" + _stop + "/" + _parent + "/" + _label; }
        }
        private sealed class SiblingKey : IEquatable<SiblingKey>
        {
            private readonly ControlId _parent;
            private readonly object _stop;
            public SiblingKey(ControlId parent, object stop) { _parent = parent; _stop = stop; }
            public bool Equals(SiblingKey other) { return other != null && Equals(_parent, other._parent) && Equals(_stop, other._stop); }
            public override bool Equals(object obj) { return Equals(obj as SiblingKey); }
            public override int GetHashCode() { return (_parent == null ? 0 : _parent.GetHashCode() * 397) ^ _stop.GetHashCode(); }
        }
    }
}
