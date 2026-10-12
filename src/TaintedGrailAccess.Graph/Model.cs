using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TaintedGrailAccess.Graph
{
    public enum Direction { Up, Right, Down, Left }

    /// <summary>Map identity is structural only. Reference identity is an explicit recovery tier.</summary>
    public sealed class ControlId : IEquatable<ControlId>
    {
        public object Reference { get; private set; }
        public object StructuralKey { get; private set; }

        private ControlId(object reference, object structuralKey)
        {
            if (structuralKey == null) throw new ArgumentNullException(nameof(structuralKey));
            Reference = reference;
            StructuralKey = structuralKey;
        }

        public static ControlId Structural(object key) { return new ControlId(null, key); }
        public static ControlId Referenced(object reference, object key)
        {
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            return new ControlId(reference, key);
        }
        public static ControlId ForObject(object reference) { return Referenced(reference, reference); }

        public bool Equals(ControlId other)
        {
            return other != null && Equals(StructuralKey, other.StructuralKey);
        }
        public override bool Equals(object obj) { return Equals(obj as ControlId); }
        public override int GetHashCode() { return StructuralKey.GetHashCode(); }
        public override string ToString() { return StructuralKey.ToString(); }

        /// <summary>Logical sameness for the host differ, not a dictionary equality relation.</summary>
        public static bool SameIdentity(ControlId left, ControlId right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;
            return (left.Reference != null && ReferenceEquals(left.Reference, right.Reference)) || left.Equals(right);
        }
    }

    public sealed class NodeAnnouncement
    {
        public Func<string> Text { get; private set; }
        public string Kind { get; private set; }
        public bool Live { get; private set; }

        public NodeAnnouncement(Func<string> text, string kind = null, bool live = false)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Kind = kind;
            Live = live;
        }
    }

    public sealed class ControlType
    {
        public string Key { get; private set; }
        public IReadOnlyList<string> Order { get; private set; }
        public IReadOnlyList<NodeAnnouncement> Common { get; private set; }

        public ControlType(string key, IEnumerable<string> order = null, IEnumerable<NodeAnnouncement> common = null)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A stable type key is required.", nameof(key));
            Key = key;
            Order = new List<string>(order ?? new string[0]).AsReadOnly();
            Common = new List<NodeAnnouncement>(common ?? new NodeAnnouncement[0]).AsReadOnly();
        }
    }

    /// <summary>Delegates read live state or call original game handlers; no view state belongs here.</summary>
    public sealed class NodeVtable
    {
        public IReadOnlyList<NodeAnnouncement> Announcements { get; private set; }
        public ControlType ControlType { get; set; }
        public Action OnActivate { get; set; }
        public Action OnSecondary { get; set; }
        public Action OnActivateShift { get; set; }
        public Action OnActivateCtrl { get; set; }
        public Action OnTooltip { get; set; }
        public Action<int, bool> OnAdjust { get; set; }
        public Func<string> StateText { get; set; }
        public Func<string> SearchText { get; set; }
        public bool ExcludeFromSearch { get; set; }
        public object HoverSound { get; set; }
        public object ClickSound { get; set; }
        public object ActivateSound { get; set; }
        public Action OnExpand { get; set; }
        public Action OnCollapse { get; set; }
        public bool SpeaksOwnExpansion { get; set; }
        public bool SpeaksOwnPosition { get; set; }

        public NodeVtable(params NodeAnnouncement[] announcements)
        {
            Announcements = new List<NodeAnnouncement>(announcements ?? new NodeAnnouncement[0]).AsReadOnly();
        }
    }

    public sealed class GraphEdge
    {
        public ControlId Destination { get; private set; }
        public string Label { get; private set; }
        public GraphEdge(ControlId destination, string label = null)
        {
            Destination = destination ?? throw new ArgumentNullException(nameof(destination));
            Label = label;
        }
    }

    public sealed class GraphNode
    {
        private readonly Dictionary<Direction, GraphEdge> _transitions = new Dictionary<Direction, GraphEdge>();
        public ControlId Id { get; private set; }
        public NodeVtable Vtable { get; private set; }
        public IReadOnlyDictionary<Direction, GraphEdge> Transitions { get; private set; }
        public GraphNode Parent { get; internal set; }
        public bool Focusable { get; internal set; }
        public bool Expandable { get; internal set; }
        public bool Expanded { get; internal set; }
        public bool SuppressChildPositions { get; internal set; }
        public object StopKey { get; internal set; }
        public object RegionKey { get; internal set; }
        public int PositionIndex { get; internal set; }
        public int PositionCount { get; internal set; }

        internal GraphNode(ControlId id, NodeVtable vtable)
        {
            Id = id;
            Vtable = vtable;
            Focusable = true;
            Transitions = new ReadOnlyDictionary<Direction, GraphEdge>(_transitions);
        }

        internal void SetEdge(Direction direction, GraphEdge edge) { _transitions[direction] = edge; }
        internal bool HasEdge(Direction direction) { return _transitions.ContainsKey(direction); }
    }

    public sealed class GraphRender
    {
        public IReadOnlyDictionary<ControlId, GraphNode> Nodes { get; private set; }
        /// <summary>Declaration order, including structural nodes; navigation filters Focusable.</summary>
        public IReadOnlyList<ControlId> Order { get; private set; }
        public ControlId StartKey { get; private set; }

        internal GraphRender(Dictionary<ControlId, GraphNode> nodes, List<ControlId> order, ControlId startKey)
        {
            Nodes = new ReadOnlyDictionary<ControlId, GraphNode>(nodes);
            Order = order.AsReadOnly();
            StartKey = startKey;
        }
    }

    /// <summary>The only persistent kernel state. A host owns one instance per live screen.</summary>
    public sealed class GraphState
    {
        public ControlId CurKey { get; set; }
        public IReadOnlyList<ControlId> KeyOrder { get; set; }
        public ControlId NextSuggestedMove { get; set; }
        public IDictionary<object, ControlId> StopMemory { get; private set; }
        public ISet<ControlId> Expanded { get; private set; }

        public GraphState()
        {
            KeyOrder = new List<ControlId>().AsReadOnly();
            StopMemory = new Dictionary<object, ControlId>();
            Expanded = new HashSet<ControlId>();
        }
    }

    public sealed class MoveResult
    {
        public bool Moved { get; private set; }
        public GraphNode From { get; private set; }
        public GraphNode To { get; private set; }
        public string TransitionLabel { get; private set; }

        internal MoveResult(GraphNode from, GraphNode to, string transitionLabel = null)
        {
            From = from;
            To = to;
            Moved = from == null ? to != null : to != null && !from.Id.Equals(to.Id);
            TransitionLabel = Moved ? transitionLabel : null;
        }
    }

    public enum TreeResultKind { NoFocus, NotTree, Expanded, Collapsed, Descended, Ascended, Leaf, EmptyGroup }

    public sealed class TreeResult
    {
        public TreeResultKind Kind { get; private set; }
        public MoveResult Move { get; private set; }
        public bool Consumed { get { return Kind != TreeResultKind.NoFocus && Kind != TreeResultKind.NotTree; } }
        internal TreeResult(TreeResultKind kind, MoveResult move) { Kind = kind; Move = move; }
    }
}
