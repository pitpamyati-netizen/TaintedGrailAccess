using System;
using System.Collections.Generic;
using System.Linq;
using TaintedGrailAccess.Graph;

namespace TaintedGrailAccess.Runtime
{
    public sealed class ScreenSession
    {
        public IAccessibleScreen Screen { get; }
        public GraphState State { get; } = new GraphState();
        public bool Seated { get; internal set; }
        public bool InitialStopPending { get; internal set; } = true;
        internal ScreenSession(IAccessibleScreen screen)
        { Screen = screen; Seated = !screen.StartUnfocused; }
    }

    // Screen predicates are polled; transitions are not dependent on fragile
    // subscriptions to views that the game can silently destroy and recreate.
    public sealed class ScreenManager
    {
        private readonly List<IAccessibleScreen> _registered = new List<IAccessibleScreen>();
        private readonly Dictionary<string, IAccessibleScreen> _byId = new Dictionary<string, IAccessibleScreen>();
        private readonly Dictionary<string, ScreenSession> _sessions = new Dictionary<string, ScreenSession>();
        private HashSet<string> _active = new HashSet<string>();
        private readonly FaultBoundary _faults;
        public ScreenSession Current { get; private set; }

        public ScreenManager(FaultBoundary faults) { _faults = faults ?? throw new ArgumentNullException(nameof(faults)); }
        public void Register(IAccessibleScreen screen)
        {
            if (screen == null || string.IsNullOrWhiteSpace(screen.Id)) throw new ArgumentException("Screen needs a stable id.");
            if (_byId.ContainsKey(screen.Id)) throw new ArgumentException("Duplicate screen id: " + screen.Id);
            _byId.Add(screen.Id, screen);
            _registered.Add(screen);
        }

        public void Settle(bool updateScreens = true)
        {
            var requested = new HashSet<string>();
            foreach (var screen in _registered)
                if (_faults.Read(screen.Id, screen.IsActive, false)) requested.Add(screen.Id);

            var now = new HashSet<string>();
            foreach (var screen in _registered)
                if (AncestorsActive(screen, requested)) now.Add(screen.Id);

            var top = TopScreen(now);
            var changed = (Current == null ? null : Current.Screen.Id) != (top == null ? null : top.Id);
            if (changed && Current != null) _faults.Run(Current.Screen.Id, Current.Screen.OnUnfocus);

            foreach (var screen in _registered.Where(s => _active.Contains(s.Id) && !now.Contains(s.Id)).OrderByDescending(Depth))
            {
                _faults.Run(screen.Id, screen.OnPop);
                if (!screen.KeepStateOnPop) _sessions.Remove(screen.Id);
            }
            foreach (var screen in _registered.Where(s => now.Contains(s.Id) && !_active.Contains(s.Id)).OrderBy(Depth))
            {
                if (!_sessions.ContainsKey(screen.Id)) _sessions.Add(screen.Id, new ScreenSession(screen));
                _faults.Run(screen.Id, screen.OnPush);
            }
            _active = now;
            Current = top == null ? null : _sessions[top.Id];
            if (changed && Current != null) _faults.Run(Current.Screen.Id, Current.Screen.OnFocus);

            if (updateScreens)
                foreach (var screen in _registered.Where(s => now.Contains(s.Id))) _faults.Run(screen.Id, screen.OnUpdate);
        }

        public void Clear()
        {
            if (Current != null) _faults.Run(Current.Screen.Id, Current.Screen.OnUnfocus);
            foreach (var screen in _registered.Where(s => _active.Contains(s.Id)).OrderByDescending(Depth))
                _faults.Run(screen.Id, screen.OnPop);
            Current = null;
            _active.Clear();
            _sessions.Clear();
        }

        private bool AncestorsActive(IAccessibleScreen screen, HashSet<string> requested)
        {
            var seen = new HashSet<string>();
            while (screen != null)
            {
                if (!seen.Add(screen.Id))
                {
                    _faults.Report(screen.Id, new InvalidOperationException("Cycle in screen hierarchy."));
                    return false;
                }
                if (!requested.Contains(screen.Id)) return false;
                if (screen.ParentId == null) return true;
                if (!_byId.TryGetValue(screen.ParentId, out screen)) return false;
            }
            return false;
        }

        private int Depth(IAccessibleScreen screen)
        {
            var depth = 0;
            var seen = new HashSet<string>();
            while (screen.ParentId != null && seen.Add(screen.Id) && _byId.TryGetValue(screen.ParentId, out screen)) depth++;
            return depth;
        }

        private IAccessibleScreen TopScreen(HashSet<string> active)
        {
            IAccessibleScreen chosen = null;
            string parent = null;
            while (true)
            {
                IAccessibleScreen next = null;
                // Later registration is a stable tie break, never view object order.
                foreach (var screen in _registered)
                    if (active.Contains(screen.Id) && screen.ParentId == parent && (next == null || screen.Layer >= next.Layer)) next = screen;
                if (next == null) return chosen;
                chosen = next;
                parent = next.Id;
            }
        }
    }
}
