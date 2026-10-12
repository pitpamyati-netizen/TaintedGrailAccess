using System;
using System.Collections.Generic;
using System.Globalization;

namespace TaintedGrailAccess.Graph
{
    /// <summary>The host supplies localized wording; defaults make a standalone kernel exercisable.</summary>
    public sealed class AnnouncementSettings
    {
        public bool SpeakPositions { get; set; } = true;
        public Func<string, string, bool> IncludePart { get; set; } = (type, kind) => true;
        public Func<bool, string> ExpansionText { get; set; } = expanded => expanded ? "expanded" : "collapsed";
        public Func<int, int, string> PositionText { get; set; } = (index, count) => index.ToString(CultureInfo.InvariantCulture) + " of " + count.ToString(CultureInfo.InvariantCulture);
    }

    public sealed class ResolvedLivePart
    {
        /// <summary>Kind plus occurrence, or declaration-derived index for kindless parts; never a delegate.</summary>
        public string Key { get; private set; }
        public string Kind { get; private set; }
        public int Index { get; private set; }
        public string Text { get; private set; }
        internal ResolvedLivePart(string key, string kind, int index, string text)
        { Key = key; Kind = kind; Index = index; Text = text; }
    }

    /// <summary>Composes live strings only. It never sends speech and never retains graph or speech state.</summary>
    public sealed class GraphAnnouncer
    {
        public AnnouncementSettings Settings { get; private set; }

        public GraphAnnouncer(AnnouncementSettings settings = null)
        {
            Settings = settings ?? new AnnouncementSettings();
        }

        public IReadOnlyList<NodeAnnouncement> EffectiveParts(GraphNode node)
        {
            var parts = new List<NodeAnnouncement>();
            if (node == null) return parts.AsReadOnly();
            ControlType type = node.Vtable.ControlType;
            var overrides = new HashSet<string>(StringComparer.Ordinal);
            foreach (NodeAnnouncement part in node.Vtable.Announcements)
                if (part.Kind != null) overrides.Add(part.Kind);
            if (type != null)
                foreach (NodeAnnouncement common in type.Common)
                    if (common.Kind == null || !overrides.Contains(common.Kind)) parts.Add(common);
            parts.AddRange(node.Vtable.Announcements);

            var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
            if (type != null)
                for (int i = 0; i < type.Order.Count; i++)
                {
                    string kind = type.Order[i];
                    if (kind != null && !ranks.ContainsKey(kind)) ranks.Add(kind, i);
                }
            var sortable = new List<OrderedPart>();
            for (int i = 0; i < parts.Count; i++)
            {
                int rank;
                if (parts[i].Kind == null || !ranks.TryGetValue(parts[i].Kind, out rank)) rank = int.MaxValue;
                sortable.Add(new OrderedPart(parts[i], rank, i));
            }
            // Explicit declaration-index tie breaker makes stability independent of List.Sort's algorithm.
            sortable.Sort((left, right) => left.Rank == right.Rank ? left.Index.CompareTo(right.Index) : left.Rank.CompareTo(right.Rank));
            parts.Clear();
            string typeKey = type == null ? string.Empty : type.Key;
            foreach (OrderedPart item in sortable)
                if (Settings.IncludePart == null || Settings.IncludePart(typeKey, item.Part.Kind)) parts.Add(item.Part);
            return parts.AsReadOnly();
        }

        public IReadOnlyList<ResolvedLivePart> ResolveLiveParts(GraphNode node)
        {
            IReadOnlyList<NodeAnnouncement> parts = EffectiveParts(node);
            var result = new List<ResolvedLivePart>();
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < parts.Count; i++)
            {
                NodeAnnouncement part = parts[i];
                string kind = part.Kind ?? string.Empty;
                int occurrence;
                occurrences.TryGetValue(kind, out occurrence);
                occurrences[kind] = occurrence + 1;
                if (!part.Live) continue;
                string key = part.Kind == null ? "@" + i.ToString(CultureInfo.InvariantCulture)
                    : part.Kind + "#" + occurrence.ToString(CultureInfo.InvariantCulture);
                result.Add(new ResolvedLivePart(key, part.Kind, i, part.Text() ?? string.Empty));
            }
            return result.AsReadOnly();
        }

        public string ComposeNode(GraphNode node)
        {
            if (node == null) return string.Empty;
            var text = new List<string>();
            IReadOnlyList<NodeAnnouncement> parts = EffectiveParts(node);
            foreach (NodeAnnouncement part in parts) Append(text, part.Text());
            string typeKey = node.Vtable.ControlType == null ? string.Empty : node.Vtable.ControlType.Key;
            if (node.Expandable && !node.Vtable.SpeaksOwnExpansion && Includes(typeKey, "expanded") && Settings.ExpansionText != null)
                Append(text, Settings.ExpansionText(node.Expanded));
            bool ownsPosition = node.Vtable.SpeaksOwnPosition;
            // An authored position suppresses automatic wording even if its current value is silent.
            foreach (NodeAnnouncement part in node.Vtable.Announcements) if (part.Kind == "position") ownsPosition = true;
            if (node.Vtable.ControlType != null)
                foreach (NodeAnnouncement part in node.Vtable.ControlType.Common) if (part.Kind == "position") ownsPosition = true;
            if (Settings.SpeakPositions && !ownsPosition && node.PositionCount > 1 && Includes(typeKey, "position") && Settings.PositionText != null)
                Append(text, Settings.PositionText(node.PositionIndex, node.PositionCount));
            return string.Join(", ", text);
        }

        public string Compose(GraphNode from, GraphNode to, string transitionLabel = null)
        {
            if (to == null) return string.Empty;
            List<GraphNode> fromPath = Path(from), toPath = Path(to);
            int shared = 0;
            while (shared < fromPath.Count && shared < toPath.Count && fromPath[shared].Id.Equals(toPath[shared].Id)) shared++;
            var text = new List<string>();
            Append(text, transitionLabel);
            if (shared >= toPath.Count)
            {
                Append(text, ComposeNode(to));
                return string.Join(", ", text);
            }
            var readouts = new List<string>();
            var labels = new List<string>();
            for (int i = shared; i < toPath.Count; i++)
            {
                readouts.Add(ComposeNode(toPath[i]));
                labels.Add(toPath[i].Vtable.Announcements[0].Text() ?? string.Empty);
            }
            for (int i = 0; i < readouts.Count; i++)
            {
                if (i + 1 < readouts.Count && labels[i].Length > 0
                    && (labels[i] == labels[i + 1] || readouts[i + 1].StartsWith(labels[i] + ",", StringComparison.Ordinal))) continue;
                Append(text, readouts[i]);
            }
            return string.Join(", ", text);
        }

        private bool Includes(string type, string kind) { return Settings.IncludePart == null || Settings.IncludePart(type, kind); }
        private static void Append(List<string> text, string part) { if (!string.IsNullOrEmpty(part)) text.Add(part); }
        private static List<GraphNode> Path(GraphNode node)
        {
            var path = new List<GraphNode>();
            var seen = new HashSet<ControlId>();
            for (GraphNode cursor = node; cursor != null && seen.Add(cursor.Id); cursor = cursor.Parent) path.Add(cursor);
            path.Reverse();
            return path;
        }
        private sealed class OrderedPart
        {
            public readonly NodeAnnouncement Part;
            public readonly int Rank, Index;
            public OrderedPart(NodeAnnouncement part, int rank, int index) { Part = part; Rank = rank; Index = index; }
        }
    }
}
