using System;
using System.Collections.Generic;
using System.Linq;
using TaintedGrailAccess.Graph;

namespace TaintedGrailAccess.Runtime
{
    /// <summary>Map geometry only. Coordinates are game map units, never screen pixels.</summary>
    public struct MapPoint
    {
        public double X { get; }
        public double Z { get; }
        public MapPoint(double x, double z) { X = x; Z = z; }
        public bool IsFinite => !double.IsNaN(X) && !double.IsInfinity(X) && !double.IsNaN(Z) && !double.IsInfinity(Z);
        public double DistanceTo(MapPoint other) => Math.Sqrt((X - other.X) * (X - other.X) + (Z - other.Z) * (Z - other.Z));
    }

    /// <summary>Evidence level returned by an explicit route query.</summary>
    public enum RouteAvailability { Unknown, Missing, Partial, Complete }

    /// <summary>Passive route estimate, never authorization or a veto for the game's movement handler.</summary>
    public sealed class RouteInspection
    {
        public RouteAvailability Availability { get; }
        public double Length { get; }
        public int PointCount { get; }
        public double StartGap { get; }
        public double EndGap { get; }
        private RouteInspection(RouteAvailability availability, double length, int pointCount = 0, double startGap = 0, double endGap = 0)
        { Availability = availability; Length = length; PointCount = pointCount; StartGap = startGap; EndGap = endGap; }
        public static RouteInspection Inspect(IEnumerable<MapPoint> path, MapPoint origin, MapPoint target, double tolerance)
        {
            if (!origin.IsFinite || !target.IsFinite || double.IsNaN(tolerance) || tolerance < 0 || double.IsInfinity(tolerance))
                return new RouteInspection(RouteAvailability.Unknown, 0);
            var points = path == null ? Array.Empty<MapPoint>() : path.ToArray();
            if (points.Length == 0) return new RouteInspection(RouteAvailability.Missing, 0);
            if (points.Any(point => !point.IsFinite)) return new RouteInspection(RouteAvailability.Unknown, 0);
            double length = origin.DistanceTo(points[0]);
            for (int i = 1; i < points.Length; i++) length += points[i - 1].DistanceTo(points[i]);
            var startGap = origin.DistanceTo(points[0]); var endGap = points[points.Length - 1].DistanceTo(target);
            return new RouteInspection(startGap <= tolerance && endGap <= tolerance
                ? RouteAvailability.Complete : RouteAvailability.Partial, length, points.Length, startGap, endGap);
        }
    }

    /// <summary>Passive list presentation. Actions recheck disclosure and readiness at invocation.</summary>
    public static class ExplorationPresentation
    {
        /// <summary>Names already reduced to spoken, visible UI text. Never query hidden name providers.</summary>
        public static string PlaceLabel(bool disclosed, Func<IEnumerable<string>> visibleNames, Func<string> unnamed, Func<string> anonymous)
        {
            if (!disclosed) return anonymous();
            return visibleNames().FirstOrDefault(name => name != null && name.Any(char.IsLetterOrDigit)) ?? unnamed();
        }
        /// <summary>Passive hover information is not an exclusive interaction panel.</summary>
        public static bool BlocksExploration(bool visible, bool hoverOnly) => visible && !hoverOnly;

        /// <summary>Eight directions relative to the current game camera's horizontal forward vector.</summary>
        public static string Direction(MapPoint origin, MapPoint target, double cameraForwardX, double cameraForwardZ)
        {
            if (!origin.IsFinite || !target.IsFinite || double.IsNaN(cameraForwardX) || double.IsNaN(cameraForwardZ) ||
                double.IsInfinity(cameraForwardX) || double.IsInfinity(cameraForwardZ) || cameraForwardX * cameraForwardX + cameraForwardZ * cameraForwardZ < 0.000001)
                return "unknown";
            if (origin.DistanceTo(target) < 0.1) return "here";
            double dx = target.X - origin.X, dz = target.Z - origin.Z;
            double forward = dx * cameraForwardX + dz * cameraForwardZ;
            double right = dx * cameraForwardZ - dz * cameraForwardX;
            int sector = ((int)Math.Floor(Math.Atan2(right, forward) * 4 / Math.PI + 0.5) + 8) % 8;
            return new[] { "ahead", "aheadRight", "right", "behindRight", "behind", "behindLeft", "left", "aheadLeft" }[sector];
        }

        /// <summary>The name/detail providers are never queried for a partially disclosed marker.</summary>
        public static void Add(GraphBuilder builder, object instance, object key, Func<bool> disclosed, Func<bool> present,
            Func<string> name, Func<string> anonymousName, Func<string> geometry, Func<string> reason,
            Action activate, Action inspect)
        {
            builder.Add(ControlId.Referenced(instance, key), new NodeVtable(
                new NodeAnnouncement(() => !present() ? null : disclosed() ? name() : anonymousName(), "label"),
                new NodeAnnouncement(() => present() ? geometry() : null, "value"),
                new NodeAnnouncement(() => present() ? reason() : null, "reason"))
            {
                OnActivate = () => { if (present()) activate(); },
                OnTooltip = () => { if (present()) inspect(); }
            });
        }
    }

    /// <summary>Observes an owned move using strings and coordinates only; never advances or teleports the hero.</summary>
    public sealed class ExplorationTravelWatch
    {
        private string _move;
        private MapPoint _position;
        private double _lastProgressTime;
        private int _distanceBucket;
        private bool _reportedStall;
        public bool IsTracking => _move != null;
        /// <summary>True only for the original order, so a native replacement cannot be cancelled by this watch.</summary>
        public bool Owns(string move) => _move != null && _move == move;
        /// <summary>Rebaseline on OS focus events, including when the game receives no ticks while minimized.</summary>
        public void Pause(double now) { _lastProgressTime = now; }
        public void Begin(string move, MapPoint position, double now, double remaining)
        {
            if (string.IsNullOrEmpty(move)) throw new ArgumentException("A live move identity is required.", nameof(move));
            _move = move; _position = position; _lastProgressTime = now;
            _distanceBucket = (int)Math.Ceiling(remaining / 10); _reportedStall = false;
        }
        /// <summary>Returns an event key only on a meaningful change. Pauses rebaseline the stall clock.</summary>
        public string Observe(string move, MapPoint position, double now, double remaining, bool canRun, bool activeWindow, bool arrived)
        {
            if (_move == null) return null;
            if (move != _move) { Reset(); return arrived ? "arrived" : "stopped"; }
            if (!activeWindow || !canRun) { _lastProgressTime = now; _position = position; return null; }
            if (position.DistanceTo(_position) >= 0.25)
            { _position = position; _lastProgressTime = now; _reportedStall = false; }
            if (!_reportedStall && now - _lastProgressTime >= 8)
            { _reportedStall = true; return "stalled"; }
            int bucket = (int)Math.Ceiling(remaining / 10);
            if (bucket < _distanceBucket) { _distanceBucket = bucket; return "progress"; }
            return null;
        }
        public void Reset() { _move = null; _reportedStall = false; }
    }
}
