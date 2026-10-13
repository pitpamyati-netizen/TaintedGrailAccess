using System;
using System.Collections.Generic;
using System.Linq;
using TaintedGrailAccess.Graph;

namespace TaintedGrailAccess.Runtime
{
    public sealed class Navigator
    {
        private readonly ISpeechOutput _speech;
        private readonly IRuntimeLog _log;
        private readonly ISoundOutput _sound;
        private readonly Localizer _text;
        private readonly FaultBoundary _faults;
        private readonly GraphAnnouncer _announcer;
        private readonly Dictionary<string, string> _live = new Dictionary<string, string>();
        private ScreenSession _session;
        private KeyGraph _graph;
        private GraphNode _lastSpoken;
        private string _query = "";
        private ControlId _searchLanding;
        private bool _handlingClaimed;
        public bool RealTextFieldActive { get; set; }
        public bool HasFocus { get { return _session != null && _session.Seated && _graph != null && _graph.Current != null; } }
        public bool IsSeated { get { return _session != null && _session.Seated; } }
        public GraphNode Current { get { return HasFocus ? _graph.Current : null; } }
        public string SearchQuery { get { return _query; } }

        public Navigator(ISpeechOutput speech, Localizer text, FaultBoundary faults,
            IRuntimeLog log = null, ISoundOutput sound = null, AnnouncementSettings settings = null)
        {
            _speech = speech ?? throw new ArgumentNullException(nameof(speech));
            _text = text ?? throw new ArgumentNullException(nameof(text));
            _faults = faults ?? throw new ArgumentNullException(nameof(faults));
            _log = log ?? new NullLog();
            _sound = sound ?? new NullSound();
            settings = settings ?? new AnnouncementSettings();
            settings.ExpansionText = value => _text.Get(value ? "tree.expanded" : "tree.collapsed");
            settings.PositionText = (index, count) => _text.Get("position", index, count);
            _announcer = new GraphAnnouncer(settings);
        }

        public void Attach(ScreenSession session)
        {
            if (ReferenceEquals(session, _session)) return;
            _session = session;
            _graph = session == null ? null : new KeyGraph(session.State, BuildSafe);
            _lastSpoken = null;
            _live.Clear();
            ClearSearch();
            if (session != null) Say(session.Screen.ScreenName, false);
        }

        private GraphRender BuildSafe()
        {
            return _faults.Read(_session.Screen.Id, () =>
            {
                var builder = new GraphBuilder(_session.State);
                _session.Screen.Build(builder);
                return builder.Build();
            }, (GraphRender)null);
        }

        public void Tick()
        {
            if (_session == null) return;
            _faults.Run(_session.Screen.Id, () =>
            {
                if (_session.Screen.CapturesRawInput) { ClearSearch(); return; }
                if (RealTextFieldActive) { ClearSearch(); return; }
                var suggestion = _session.State.NextSuggestedMove;
                var render = _graph.Rerender(_session.Seated);
                if (_session.Seated && render != null && suggestion != null && _graph.Current != null
                    && Same(suggestion, _graph.Current.Id)) _session.InitialStopPending = false;
                if (!_session.Seated && render != null && suggestion != null)
                {
                    _session.State.NextSuggestedMove = null;
                    var requested = render.Order.Select(id => render.Nodes[id])
                        .FirstOrDefault(node => node.Focusable && Same(suggestion, node.Id));
                    if (requested != null)
                    {
                        _session.Seated = true;
                        _session.InitialStopPending = false;
                        _graph.Focus(requested.Id);
                    }
                }
                if (!_session.Seated || render == null) return;
                ApplyInitialStop();
                var current = _graph.Current;
                if (current == null) return;
                if (!Same(_lastSpoken, current))
                    AnnounceLanding(current, null, false);
                else
                {
                    // Keep the new path snapshot even when an object merely moved.
                    _lastSpoken = current;
                    WatchLive(current);
                }
                if (_searchLanding != null && !Same(_searchLanding, current.Id)) ClearSearch();
            });
        }

        private void ApplyInitialStop()
        {
            if (!_session.InitialStopPending || _graph.Render == null) return;
            _session.InitialStopPending = false;
            var stop = _session.Screen.InitialFocusStop;
            if (stop == null) return;
            var nodes = _graph.Render.Order.Select(id => _graph.Render.Nodes[id])
                .Where(node => node.Focusable && Equals(node.StopKey, stop)).ToList();
            var target = nodes.FirstOrDefault(IsSelectedModel) ?? nodes.FirstOrDefault();
            if (target != null) _graph.Focus(target.Id);
        }

        private static bool IsSelectedModel(GraphNode node)
        {
            // Selection controls navigation even when the user hides its spoken
            // part. Own parts override common type parts before this decision.
            var own = node.Vtable.Announcements.Where(part => part.Kind == "selected").ToList();
            if (own.Count > 0) return own.Any(part => !string.IsNullOrEmpty(part.Text()));
            var type = node.Vtable.ControlType;
            return type != null && type.Common.Any(part => part.Kind == "selected" && !string.IsNullOrEmpty(part.Text()));
        }

        public bool Handle(KeyChord chord)
        {
            if (_session == null || _session.Screen.CapturesRawInput) return false;
            if (RealTextFieldActive) { ClearSearch(); return false; }
            _handlingClaimed = false;
            try { return HandleCore(chord); }
            catch (Exception error)
            {
                _faults.Report(_session.Screen.Id, error);
                // A partially executed action must NEVER leak the same chord to
                // the game. A failed optional sound is not permission to replay.
                return _handlingClaimed;
            }
        }

        /// <summary>Native editor commands retain a consumed press even if their handler throws after acting.</summary>
        public bool HandleNativeTextInput(KeyChord chord, Func<KeyChord, bool> handler)
        {
            if (!RealTextFieldActive || handler == null) return false;
            try { return handler(chord); }
            catch (Exception error)
            {
                _faults.Report(_session == null ? "native-text" : _session.Screen.Id, error);
                return true;
            }
        }

        private bool HandleCore(KeyChord chord)
        {
            try { if (_session.Screen.OnContextualInput(chord)) return true; }
            catch { _handlingClaimed = true; throw; }
            // An explicitly assigned contextual chord may use Alt. Other Alt/OS
            // chords must never be reinterpreted as ordinary graph commands.
            if (chord.Alt || (chord.Modifiers & Modifiers.Reserved) != 0) return false;
            if (chord.Key == UiKey.Tab && !chord.Control)
            {
                _handlingClaimed = true;
                ClearSearch();
                if (!_session.Seated)
                {
                    _session.Seated = true;
                    if (_graph.Rerender() == null) { _session.Seated = false; return false; }
                    ApplyInitialStop();
                    if (chord.Shift) _graph.MoveStop(-1, true);
                    AnnounceLanding(_graph.Current, null, true);
                    return true;
                }
                var move = _graph.MoveStop(chord.Shift ? -1 : 1, !_session.Screen.StartUnfocused);
                if (!move.Moved && _session.Screen.StartUnfocused) Blur();
                else AnnounceMove(move);
                return true;
            }
            if (chord.Key == UiKey.Escape && !chord.Control && !chord.Shift)
            {
                _handlingClaimed = true;
                // A native handler may have opened this screen on the preceding
                // frame of the same physical press. Acquire the hold without
                // invoking Back again or turning search cancellation into Back.
                if (chord.IsRepeat) return true;
                if (_query.Length > 0) { ClearSearch(); Say(_text.Get("search.cleared"), true); return true; }
                return _session.Screen.OnBack();
            }
            if (!_session.Seated) return false;
            _graph.Rerender();
            if (_graph.Current == null) return false;

            if (chord.Key == UiKey.None && !chord.Control && !string.IsNullOrEmpty(chord.Text) && chord.Text.All(c => !char.IsControl(c)))
            {
                _handlingClaimed = true;
                _query = (_query + chord.Text);
                if (_query.Length > 256) _query = _query.Substring(0, 256);
                SearchTo(0, true);
                return true;
            }
            if (_query.Length > 0 && !chord.Control)
            {
                if (chord.Key == UiKey.Up || chord.Key == UiKey.Down)
                { _handlingClaimed = true; SearchTo(chord.Key == UiKey.Up ? -1 : 1, false); return true; }
                if (chord.Key == UiKey.Home || chord.Key == UiKey.End)
                { _handlingClaimed = true; SearchTo(chord.Key == UiKey.Home ? 0 : int.MaxValue, true); return true; }
                if (chord.Key == UiKey.Backspace)
                {
                    _handlingClaimed = true;
                    _query = _query.Substring(0, _query.Length - 1);
                    if (_query.Length == 0) { ClearSearch(); Say(_text.Get("search.cleared"), true); }
                    else SearchTo(0, true);
                    return true;
                }
            }

            Direction direction;
            if (TryDirection(chord.Key, out direction))
            {
                if (chord.Control)
                {
                    if (_graph.Current.RegionKey == null) return false;
                    _handlingClaimed = true;
                    ClearSearch();
                    AnnounceMove(_graph.MoveRegion(direction == Direction.Up || direction == Direction.Left ? -1 : 1));
                    return true;
                }
                if ((direction == Direction.Left || direction == Direction.Right) && _graph.Current.Vtable.OnAdjust != null)
                {
                    _handlingClaimed = true;
                    StateAction(() => _graph.TryAdjust(direction == Direction.Left ? -1 : 1, chord.Shift), "action.unavailable");
                    return true;
                }
                ClearSearch();
                _handlingClaimed = true;
                var move = _graph.Move(direction);
                if (move.Moved) { AnnounceMove(move); return true; }
                if (InTree(_graph.Current))
                {
                    if (direction == Direction.Right || direction == Direction.Left)
                    {
                        var tree = direction == Direction.Right ? _graph.TreeRight() : _graph.TreeLeft();
                        AnnounceTree(tree, chord.IsRepeat);
                    }
                    return true;
                }
                // A live exploration overlay can let movement fall back to the game.
                return !_session.Screen.StartUnfocused;
            }
            if (!chord.Control && (chord.Key == UiKey.Home || chord.Key == UiKey.End))
            {
                _handlingClaimed = true;
                ClearSearch();
                bool first = chord.Key == UiKey.Home;
                if (InTree(_graph.Current)) AnnounceMove(_graph.MoveToSiblingEdge(first));
                else
                {
                    bool horizontal = (_graph.Current.Transitions.ContainsKey(Direction.Left) || _graph.Current.Transitions.ContainsKey(Direction.Right))
                        && !_graph.Current.Transitions.ContainsKey(Direction.Up) && !_graph.Current.Transitions.ContainsKey(Direction.Down);
                    AnnounceMove(_graph.MoveToEdge(horizontal ? (first ? Direction.Left : Direction.Right) : (first ? Direction.Up : Direction.Down)));
                }
                return true;
            }
            if (chord.Key == UiKey.Enter)
            {
                if (chord.Control && chord.Shift) return false;
                _handlingClaimed = true;
                // Held Enter must not spend a second card or buy a second item.
                if (chord.IsRepeat) return true;
                if (chord.Control) StateAction(_graph.ActivateCtrl, "action.unavailable");
                else if (chord.Shift) StateAction(_graph.ActivateShift, "action.unavailable");
                else StateAction(_graph.Activate, "action.unavailable");
                return true;
            }
            if (!chord.Control && !chord.Shift && chord.Key == UiKey.Backspace)
            {
                _handlingClaimed = true;
                if (!chord.IsRepeat) StateAction(_graph.Secondary, "action.noSecondary");
                return true;
            }
            if (!chord.Control && !chord.Shift && (chord.Key == UiKey.Space || chord.Key == UiKey.F1))
            {
                _handlingClaimed = true;
                if (!chord.IsRepeat && !_graph.Tooltip()) Say(_text.Get("action.noTooltip"), true);
                return true;
            }
            return false;
        }

        public void Blur()
        {
            if (_session == null) return;
            _session.Seated = false;
            _session.State.CurKey = null;
            _session.State.NextSuggestedMove = null;
            _lastSpoken = null;
            _live.Clear();
            ClearSearch();
            Say(_session.Screen.UnfocusedAnnouncement, true);
        }

        private void SearchTo(int offset, bool absolute)
        {
            var results = _graph.Search(_query);
            if (results.Count == 0) { Say(_text.Get("search.none", _query), true); return; }
            if (_graph.Current == null) return;
            int current = -1;
            for (int i = 0; i < results.Count; i++) if (Same(results[i], _graph.Current.Id)) { current = i; break; }
            int index = absolute ? offset : Math.Max(0, current) + offset;
            index = Math.Max(0, Math.Min(results.Count - 1, index));
            var move = _graph.Focus(results[index]);
            if (_graph.Current == null) return;
            _searchLanding = _graph.Current.Id;
            if (move.Moved || !Same(_lastSpoken, _graph.Current)) AnnounceMove(move);
            else Say(_text.Get("search.matches", results.Count), true);
        }

        private void StateAction(Func<bool> invoke, string unavailable)
        {
            var beforeNode = _graph.Current;
            var before = beforeNode.Vtable.StateText == null ? null : beforeNode.Vtable.StateText();
            if (!invoke()) { Say(_text.Get(unavailable), true); return; }
            if (beforeNode.Vtable.ClickSound != null)
                _faults.Run(_session.Screen.Id, () => _sound.Play(beforeNode.Vtable.ClickSound));
            if (beforeNode.Vtable.ActivateSound != null)
                _faults.Run(_session.Screen.Id, () => _sound.Play(beforeNode.Vtable.ActivateSound));
            _graph.Rerender();
            var after = _graph.Current;
            if (after == null || !Same(beforeNode, after) || after.Vtable.StateText == null) return;
            var state = after.Vtable.StateText();
            if (!string.IsNullOrEmpty(state) && state != before)
            {
                Say(state, true);
                Baseline(after);
            }
        }

        private void AnnounceTree(TreeResult result, bool repeat)
        {
            if (result.Move != null && result.Move.Moved) { AnnounceMove(result.Move); return; }
            if (result.Kind == TreeResultKind.Expanded || result.Kind == TreeResultKind.Collapsed)
            {
                if (_graph.Current != null && !_graph.Current.Vtable.SpeaksOwnExpansion)
                    Say(_text.Get(result.Kind == TreeResultKind.Expanded ? "tree.expanded" : "tree.collapsed"), true);
                else if (_graph.Current != null)
                {
                    var own = _graph.Current;
                    var changes = _announcer.ResolveLiveParts(own).Where(part =>
                    {
                        string old;
                        return !string.IsNullOrEmpty(part.Text) && (!_live.TryGetValue(part.Key, out old) || old != part.Text);
                    }).Select(part => part.Text).ToArray();
                    if (changes.Length > 0) Say(string.Join(", ", changes), true);
                    else if (own.Vtable.StateText != null) Say(own.Vtable.StateText(), true);
                    else Say(_announcer.ComposeNode(own), true);
                }
                if (_graph.Current != null) Baseline(_graph.Current);
            }
            else if (!repeat && result.Kind == TreeResultKind.EmptyGroup) Say(_text.Get("tree.empty"), true);
            else if (!repeat && result.Kind == TreeResultKind.Leaf) Say(_text.Get("tree.leaf"), true);
        }

        private void AnnounceMove(MoveResult move)
        {
            var current = _graph.Current;
            if (current != null && (move.Moved || !Same(_lastSpoken, current)))
                AnnounceLanding(current, move.TransitionLabel, true);
        }

        // The ONLY focus speech chokepoint, used by both direct input and differ.
        // All direct paths update the same memory so the next tick stays silent.
        private void AnnounceLanding(GraphNode node, string transition, bool userInitiated)
        {
            if (node == null) return;
            var line = _announcer.Compose(_lastSpoken, node, transition);
            // Commit identity before optional speech/sound/live-part callbacks.
            // A faulty backend must not cause the same landing every frame.
            _lastSpoken = node;
            Say(line, userInitiated);
            if (node.Vtable.HoverSound != null)
                _faults.Run(_session.Screen.Id, () => _sound.Play(node.Vtable.HoverSound));
            _faults.Run(_session.Screen.Id, () => Baseline(node));
            _faults.Run(_session.Screen.Id + "/log", () => _log.Write("focus", node.Id.StructuralKey.ToString()));
        }

        private void Baseline(GraphNode node)
        {
            _live.Clear();
            foreach (var part in _announcer.ResolveLiveParts(node)) _live[part.Key] = part.Text;
        }

        private void WatchLive(GraphNode node)
        {
            var next = new Dictionary<string, string>();
            foreach (var part in _announcer.ResolveLiveParts(node))
            {
                string before;
                if (_live.TryGetValue(part.Key, out before) && before != part.Text && !string.IsNullOrEmpty(part.Text))
                    Say(part.Text, false);
                else if (!_live.ContainsKey(part.Key) && !string.IsNullOrEmpty(part.Text)) Say(part.Text, false);
                next[part.Key] = part.Text;
            }
            _live.Clear();
            foreach (var value in next) _live[value.Key] = value.Value;
        }

        private void Say(string line, bool interrupt)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            _faults.Run((_session == null ? "runtime" : _session.Screen.Id) + "/speech", () => _speech.Speak(line, interrupt));
        }
        private void ClearSearch() { _query = ""; _searchLanding = null; }
        private static bool Same(GraphNode a, GraphNode b) { return a != null && b != null && Same(a.Id, b.Id); }
        private static bool Same(ControlId a, ControlId b)
        { return a != null && b != null && ((a.Reference != null && ReferenceEquals(a.Reference, b.Reference)) || a.Equals(b)); }
        private static bool InTree(GraphNode node)
        { for (var part = node; part != null; part = part.Parent) if (part.Expandable) return true; return false; }
        private static bool TryDirection(UiKey key, out Direction direction)
        {
            switch (key)
            {
                case UiKey.Up: direction = Direction.Up; return true;
                case UiKey.Right: direction = Direction.Right; return true;
                case UiKey.Down: direction = Direction.Down; return true;
                case UiKey.Left: direction = Direction.Left; return true;
                default: direction = Direction.Up; return false;
            }
        }
    }
}
