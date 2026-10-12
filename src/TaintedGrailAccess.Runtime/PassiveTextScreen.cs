using System;
using TaintedGrailAccess.Graph;

namespace TaintedGrailAccess.Runtime
{
    /// <summary>A fresh read of one visible text block. Null means that it cannot be read now.</summary>
    public sealed class VisibleText
    {
        public object Identity { get; }
        public string Text { get; }

        /// <summary>The identity is a domain object, never a retained game view.</summary>
        public VisibleText(object identity, string text)
        {
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            Text = text;
        }
    }

    /// <summary>
    /// A text-only screen for a host that supplies no keyboard events. All reads are live;
    /// navigation and announcements still use the common screen manager and navigator.
    /// This class provides no game action and does not implement input arbitration.
    /// </summary>
    public sealed class PassiveTextScreen : AccessibleScreen
    {
        private readonly Func<VisibleText> _read;
        private readonly string _id;
        private readonly string _name;

        /// <summary>Creates a screen whose source must return null for hidden or unavailable text.</summary>
        public PassiveTextScreen(string id, string name, Func<VisibleText> read)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A screen id is required.", nameof(id));
            _id = id;
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _read = read ?? throw new ArgumentNullException(nameof(read));
        }

        public override string Id => _id;
        public override string ScreenName => _name;

        public override bool IsActive()
        {
            var current = _read();
            return current != null && !string.IsNullOrWhiteSpace(current.Text);
        }

        public override void Build(GraphBuilder builder)
        {
            var current = _read();
            if (current == null || string.IsNullOrWhiteSpace(current.Text)) return;
            builder.Add(ControlId.Referenced(current.Identity, _id + ".text"),
                new NodeVtable(new NodeAnnouncement(() => _read()?.Text, "label", true)));
        }
    }
}
