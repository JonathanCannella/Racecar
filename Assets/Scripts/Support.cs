using UnityEngine;
using System.ComponentModel;
using System;

namespace GameAI
{
    [Obsolete("obsolete")]
    public sealed class PathTrackerWrapper
    {
        private readonly IRacetrackData _rt;
        public PathTrackerWrapper(IRacetrackData rt) { _rt = rt; }

        [Obsolete("obsolete")]
        public float HalfRoadWidth => _rt?.HalfRoadWidth ?? 0f;
        [Obsolete("obsolete")]
        public float distanceTravelled => _rt?.DistanceTravelled ?? 0f;
        [Obsolete("obsolete")]
        public float totalDistanceTravelled => _rt?.TotalDistanceTravelled ?? 0f;
        [Obsolete("obsolete")]
        public float MaxPathDistance => _rt?.MaxPathDistance ?? 0f;

        [Obsolete("obsolete")]
        public Vector3 closestPointOnPath => _rt?.ClosestPointOnPath ?? Vector3.zero;
        [Obsolete("obsolete")]
        public Vector3 closestPointDirectionOnPath => _rt?.ClosestPointDirectionOnPath ?? Vector3.forward;

        [Obsolete("obsolete")]
        public int currentBezierSegmentIndex => _rt?.CurrentBezierSegmentIndex ?? -1;
        [Obsolete("obsolete")]
        public int currentClosestPathPointIndex => _rt?.CurrentClosestPathPointIndex ?? -1;

        [Obsolete("obsolete")]
        public PathCreatorWrapper pathCreator => new PathCreatorWrapper(_rt);

        [Obsolete("obsolete")]
        public Vector3 GetClosestPointOnPath(Vector3 p) => _rt?.GetClosestPointOnPath(p) ?? Vector3.zero;
        [Obsolete("obsolete")]
        public Vector3 GetClosestPointDirectionOnPath(Vector3 p) => _rt?.GetClosestPointDirectionOnPath(p) ?? Vector3.forward;
        [Obsolete("obsolete")]
        public (Vector3, Vector3) GetClosestPointAndDirectionOnPath(Vector3 p)
        {
            if (_rt == null) return (Vector3.zero, Vector3.forward);
            _rt.GetClosestPointAndDirectionOnPath(p, out var q, out var t);
            return (q, t);
        }

        [Obsolete("obsolete")]
        public Vector3 GetPointAtDistance(float s) => _rt?.GetPointAtDistance(s) ?? Vector3.zero;
        [Obsolete("obsolete")]
        public Vector3 GetDirectionAtDistance(float s) => _rt?.GetDirectionAtDistance(s) ?? Vector3.forward;
        [Obsolete("obsolete")]
        public (Vector3, Vector3) GetPointAndDirAtDistance(float s)
        {
            if (_rt == null) return (Vector3.zero, Vector3.forward);
            _rt.GetPointAndDirectionAtDistance(s, out var p, out var d);
            return (p, d);
        }
    }

    [Obsolete("obsolete")]
    public sealed class PathCreatorWrapper
    {
        private readonly IRacetrackData _rt;
        public PathCreatorWrapper(IRacetrackData rt) { _rt = rt; }
        [Obsolete("obsolete")]
        public bool IsPathInitialized => _rt?.IsInitialized ?? false;
        [Obsolete("obsolete")]
        public BezierPathWrapper bezierPath => new BezierPathWrapper(_rt);
        [Obsolete("obsolete")]
        public VertexPathWrapper path => new VertexPathWrapper(_rt);
    }

    [Obsolete("obsolete")]
    public sealed class VertexPathWrapper
    {
        private readonly IRacetrackData _rt;
        public VertexPathWrapper(IRacetrackData rt) { _rt = rt; }

        [Obsolete("obsolete")]
        public Vector3 GetPointAtDistance(float s) => _rt?.GetPointAtDistance(s) ?? Vector3.zero;
        [Obsolete("obsolete")]
        public Vector3 GetDirectionAtDistance(float s) => _rt?.GetDirectionAtDistance(s) ?? Vector3.forward;
        [Obsolete("obsolete")]
        public (Vector3, Vector3) GetPointAndDirAtDistance(float s)
        {
            if (_rt == null) return (Vector3.zero, Vector3.forward);
            _rt.GetPointAndDirectionAtDistance(s, out var p, out var d);
            return (p, d);
        }

        [Obsolete("obsolete")]
        public Vector3 GetClosestPointOnPath(Vector3 p) => _rt?.GetClosestPointOnPath(p) ?? Vector3.zero;
        [Obsolete("obsolete")]
        public Vector3 GetClosestPointDirectionOnPath(Vector3 p) => _rt?.GetClosestPointDirectionOnPath(p) ?? Vector3.forward;
        [Obsolete("obsolete")]
        public (Vector3, Vector3) GetClosestPointAndDirectionOnPath(Vector3 p)
        {
            if (_rt == null) return (Vector3.zero, Vector3.forward);
            _rt.GetClosestPointAndDirectionOnPath(p, out var q, out var t);
            return (q, t);
        }
    }

    [Obsolete("obsolete")]
    public sealed class BezierPathWrapper
    {
        private readonly IRacetrackData _rt;
        public BezierPathWrapper(IRacetrackData rt) { _rt = rt; }

        [Obsolete("obsolete")]
        public int NumSegments => _rt?.BezierSegmentCount ?? 0;

        [Obsolete("obsolete")]
        public int NumControlPoints
        {
            get
            {
                int segs = _rt?.BezierSegmentCount ?? 0;
                return segs > 0 ? segs * 4 : 0;
            }
        }

        [Obsolete("obsolete")]
        public Vector3 GetPoint(int i)
        {
            if (_rt == null) return Vector3.zero;
            int segs = _rt.BezierSegmentCount;
            if (segs <= 0) return Vector3.zero;

            int total = segs * 4;
            if (i < 0 || i >= total) return Vector3.zero;

            int seg = i / 4;
            int which = i % 4;

            var p0 = _rt.BezierP0;
            var p1 = _rt.BezierP1;
            var p2 = _rt.BezierP2;
            var p3 = _rt.BezierP3;

            switch (which)
            {
                case 0: return p0[seg];
                case 1: return p1[seg];
                case 2: return p2[seg];
                default: return p3[seg];
            }
        }

        [Obsolete("obsolete")]
        public bool TryGetSegment(int i, out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3)
        {
            p0 = p1 = p2 = p3 = Vector3.zero;
            if (_rt == null) return false;

            int segs = _rt.BezierSegmentCount;
            if (i < 0 || i >= segs) return false;

            var P0 = _rt.BezierP0;
            var P1 = _rt.BezierP1;
            var P2 = _rt.BezierP2;
            var P3 = _rt.BezierP3;

            p0 = P0[i];
            p1 = P1[i];
            p2 = P2[i];
            p3 = P3[i];
            return true;
        }

        [Obsolete("obsolete")]
        public bool TryGetClosestSegment(out int segIndex, out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3)
        {
            if (_rt == null)
            {
                segIndex = -1; p0 = p1 = p2 = p3 = Vector3.zero;
                return false;
            }
            return _rt.TryGetClosestBezierSegmentSummary(out segIndex, out p0, out p1, out p2, out p3);
        }
    }

    public partial class AIVehicle
    {
        private PathTrackerWrapper _pathTrackerWrapper;
        [Obsolete("obsolete")]
        public PathTrackerWrapper pathTracker
        {
            get
            {
                if (_pathTrackerWrapper == null)
                    _pathTrackerWrapper = new PathTrackerWrapper(this.Racetrack);
                return _pathTrackerWrapper;
            }
        }
    }

}
