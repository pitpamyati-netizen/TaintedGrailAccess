using System;
using System.Collections.Generic;
using System.Linq;
using TaintedGrailAccess.Graph;

namespace TaintedGrailAccess.Runtime
{
    /// <summary>Fresh combat controls. All providers are evaluated by the current render only.</summary>
    public static class CombatPresentation
    {
        /// <summary>After a card disappears, suggest a survivor in the hand before another section.</summary>
        public static void KeepHandFocus(GraphState state, IReadOnlyList<ControlId> cards, ControlId emptyTarget)
        {
            ControlId remembered;
            if (state.NextSuggestedMove != null || state.CurKey == null ||
                !state.StopMemory.TryGetValue("combat.hand", out remembered) ||
                !ControlId.SameIdentity(remembered, state.CurKey)) return;
            if (ResolveCard(cards, state.CurKey) != null) return;
            int index = state.KeyOrder.ToList().FindIndex(id => ControlId.SameIdentity(id, state.CurKey));
            for (int i = index - 1; i >= 0; i--)
            {
                var survivor = ResolveCard(cards, state.KeyOrder[i]);
                if (survivor != null) { state.NextSuggestedMove = survivor; return; }
            }
            state.NextSuggestedMove = cards.FirstOrDefault() ?? emptyTarget;
        }

        private static ControlId ResolveCard(IReadOnlyList<ControlId> cards, ControlId previous)
        {
            // Reference identity precedes the structural key, just as in KeyGraph.
            return (previous.Reference == null ? null : cards.FirstOrDefault(id => ReferenceEquals(id.Reference, previous.Reference)))
                ?? cards.FirstOrDefault(id => id.Equals(previous));
        }

        /// <summary>Adds a distinct instance with passive inspection and a separately guarded action.</summary>
        public static void Add(GraphBuilder builder, object instance, string instanceKey, Func<string> label,
            Func<string> value, Func<string> unavailable, Func<string> selected, Action activate, Action tooltip,
            Action secondary = null, Func<string> stateText = null)
        {
            builder.Add(ControlId.Referenced(instance, instanceKey), new NodeVtable(
                new NodeAnnouncement(label, "label", true), new NodeAnnouncement(value ?? (() => null), "value", true),
                new NodeAnnouncement(unavailable ?? (() => null), "reason", true), new NodeAnnouncement(selected ?? (() => null), "selected", true))
            { OnActivate = activate, OnTooltip = tooltip, OnSecondary = secondary, StateText = stateText });
        }

        /// <summary>Rechecks the current selection before calling the game's confirmation button.</summary>
        public static bool CanConfirmSelection(int selected, int? minimum, int? maximum, bool displayOnly, bool buttonEnabled)
        {
            return !displayOnly && buttonEnabled && selected >= 0 && (!minimum.HasValue || selected >= minimum.Value) &&
                (!maximum.HasValue || selected <= maximum.Value);
        }

        /// <summary>Rejects a target that disappeared, died, became hidden or ceased to be legal.</summary>
        public static bool CanTarget(bool screenOwnsInput, bool shown, bool alive, bool stillListed, bool legal)
        { return screenOwnsInput && shown && alive && stillListed && legal; }

        /// <summary>Reads a shown card's detail without querying a concealed description.
        /// CardSpecPage deliberately suppresses its tooltip; its visible description remains readable.</summary>
        public static string CardDetail(bool knownDescription, Func<string> tooltip, Func<string> shownDescription)
        {
            if (knownDescription && tooltip != null)
            {
                var detail = tooltip();
                if (!string.IsNullOrWhiteSpace(detail)) return detail;
            }
            return shownDescription == null ? null : shownDescription();
        }

        /// <summary>Keys repeated action buttons by their current model instance and handler role.
        /// Equal labels and overlapping reward animations must not create duplicate controls.</summary>
        public static string ActionKey(string modelId, string action)
        { return "combat.action." + modelId + "." + action; }
    }

    /// <summary>Announces append-only visible log entries once, retaining only strings and a session key.</summary>
    public sealed class CombatLogCursor
    {
        private string _session;
        private int _count;
        private string _last;

        /// <summary>Initial observation is a silent baseline; a replaced/truncated log also starts a new baseline.</summary>
        public IReadOnlyList<string> Observe(string session, IReadOnlyList<string> visibleMessages)
        {
            if (session == null || visibleMessages == null) { Reset(); return Array.Empty<string>(); }
            bool replaced = session != _session || visibleMessages.Count < _count ||
                (_count > 0 && !string.Equals(visibleMessages[_count - 1], _last, StringComparison.Ordinal));
            var added = replaced ? Array.Empty<string>() : visibleMessages.Skip(_count).Where(text => !string.IsNullOrWhiteSpace(text)).ToArray();
            _session = session; _count = visibleMessages.Count;
            _last = _count == 0 ? null : visibleMessages[_count - 1];
            return added;
        }

        /// <summary>Clears progress when a combat scene is closed or input ownership is released.</summary>
        public void Reset() { _session = null; _count = 0; _last = null; }
    }
}
