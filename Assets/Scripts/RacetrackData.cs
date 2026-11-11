using UnityEngine;
using PathCreation.Examples;
using System.Collections.Generic;
using UnityEngine.Animations;

namespace GameAI
{
    /// <summary>
    /// Read-only information about the racetrack.
    /// </summary>
    public interface IRacetrackData
    {
        // Scalars
        float HalfRoadWidth { get; }
        float DistanceTravelled { get; }          // distance along current partial track (m)
        float TotalDistanceTravelled { get; }     // total since race start (m)
        float MaxPathDistance { get; }            // total length of current partial track (m)

        // Indices into current discretizations
        int CurrentBezierSegmentIndex { get; }
        int CurrentClosestPathPointIndex { get; }

        // Closest point info at the vehicle
        Vector3 ClosestPointOnPath { get; }
        Vector3 ClosestPointDirectionOnPath { get; }

        // Queries relative to an arbitrary world position
        Vector3 GetClosestPointOnPath(Vector3 worldPos);
        Vector3 GetClosestPointDirectionOnPath(Vector3 worldPos);
        void GetClosestPointAndDirectionOnPath(Vector3 worldPos, out Vector3 point, out Vector3 direction);

        // Arc-length queries along the path (absolute s)
        Vector3 GetPointAtDistance(float s);
        Vector3 GetDirectionAtDistance(float s);
        void GetPointAndDirectionAtDistance(float s, out Vector3 point, out Vector3 direction);

        // Arc-length queries relative to current vehicle position (distance offsets)
        Vector3 GetPointAhead(float offsetMeters);
        Vector3 GetDirectionAhead(float offsetMeters);
        void GetPointAndDirectionAhead(float offsetMeters, out Vector3 point, out Vector3 direction);

        // Utility
        float ClampDistance(float s);
        float RemainingDistance { get; }
        bool IsInitialized { get; }

        // Read-only cached discretization (lazy, frame-aware)
        IReadOnlyList<Vector3> Vertices { get; }
        IReadOnlyList<Vector3> Tangents { get; }
        IReadOnlyList<float>   CumulativeLength { get; }

        // Mapping helpers (use cached cumulative length)
        bool TryMapDistanceToVertexIndex(float s, out int index);
        bool TryMapWindowToVertexRange(float s0, float s1, out int i0, out int i1);

        // Enumerate a window [s0,s1] with an optional stride over vertex indices
        IEnumerable<(float s, Vector3 point, Vector3 tangent)> EnumerateWindow(float s0, float s1, int stride = 1);

        // Single cached-vertex sample by index (returns false if index out of range)
        bool TryGetVertexSample(int index, out float s, out Vector3 point, out Vector3 tangent);

        // Enumerate a window with original vertex indices (useful when stride > 1)
        IEnumerable<(int index, float s, Vector3 point, Vector3 tangent)> EnumerateWindowIndexed(float s0, float s1, int stride = 1);

        // Bézier control points aligned by segment index (0..BezierSegmentCount-1)
        int BezierSegmentCount { get; }
        IReadOnlyList<Vector3> BezierP0 { get; }
        IReadOnlyList<Vector3> BezierP1 { get; }
        IReadOnlyList<Vector3> BezierP2 { get; }
        IReadOnlyList<Vector3> BezierP3 { get; }

        // Convenience: copy out the four control points of the current closest segment
        bool TryGetClosestBezierSegmentSummary(out int segIndex, out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3);
    }

    /// <summary>
    /// Concrete implementation backed by a PathTracker. All access is read-only from the caller's perspective.
    /// </summary>
    public sealed class RacetrackData : IRacetrackData
    {
        private readonly PathTracker _tracker; // never exposed

        internal const float DefaultHalfRoadWidth = 5.0f;
        private float _halfRoadWidthCached = DefaultHalfRoadWidth;

        public RacetrackData(PathTracker tracker)
        {
            _tracker = tracker;
            if (_tracker != null && _tracker.HalfRoadWidth > 0.001f)
                _halfRoadWidthCached = _tracker.HalfRoadWidth;
        }

        // Lazy, frame-aware cache of vertex path data
        private int _cacheFrame = -1;
        private int _cachedPathVersion = -1;
        private int _cachedNumPoints = -1;
        private float _cachedPathLength = -1f;
        private Vector3[] _vertices;
        private Vector3[] _tangents;
        private float[] _cumulative;

        // Cached Bézier segment control points (world space)
        private int _cachedNumSegments = -1;
        private Vector3[] _bezP0;
        private Vector3[] _bezP1;
        private Vector3[] _bezP2;
        private Vector3[] _bezP3;

        // Read-only views that wrap our private arrays (never expose the arrays themselves)
        private ReadOnlyVec3List _verticesView;
        private ReadOnlyVec3List _tangentsView;
        private ReadOnlyFloatList _cumulativeView;

        // Read-only views over Bézier control point arrays
        private ReadOnlyVec3List _bezP0View;
        private ReadOnlyVec3List _bezP1View;
        private ReadOnlyVec3List _bezP2View;
        private ReadOnlyVec3List _bezP3View;

        // Scalars
        public float HalfRoadWidth
        {
            get
            {
                // If tracker is available, opportunistically refresh cache when it reports a valid width
                if (_tracker != null)
                {
                    float hw = _tracker.HalfRoadWidth;
                    if (hw > 0.001f && !Mathf.Approximately(hw, _halfRoadWidthCached))
                        _halfRoadWidthCached = hw;
                }
                return _halfRoadWidthCached; 
            }
        }
        public float DistanceTravelled => _tracker != null ? _tracker.distanceTravelled : 0f;
        public float TotalDistanceTravelled => _tracker != null ? _tracker.totalDistanceTravelled : 0f;
        public float MaxPathDistance => _tracker != null ? _tracker.MaxPathDistance : 0f;

        // Indices
        public int CurrentBezierSegmentIndex => _tracker != null ? _tracker.currentBezierSegmentIndex : -1;
        public int CurrentClosestPathPointIndex => _tracker != null ? _tracker.currentClosestPathPointIndex : -1;

        // Closest point info
        public Vector3 ClosestPointOnPath => _tracker != null ? _tracker.closestPointOnPath : Vector3.zero;
        public Vector3 ClosestPointDirectionOnPath => _tracker != null ? _tracker.closestPointDirectionOnPath : Vector3.forward;

        public bool IsInitialized => _tracker != null && _tracker.PathInitialized;
        public float RemainingDistance => Mathf.Max(0f, MaxPathDistance - DistanceTravelled);

        // Queries relative to any world position
        public Vector3 GetClosestPointOnPath(Vector3 worldPos)
        {
            if (_tracker == null || _tracker.pathCreator == null || _tracker.pathCreator.path == null)
                return Vector3.zero;
            return _tracker.pathCreator.path.GetClosestPointOnPath(worldPos);
        }

        public Vector3 GetClosestPointDirectionOnPath(Vector3 worldPos)
        {
            if (_tracker == null || _tracker.pathCreator == null || _tracker.pathCreator.path == null)
                return Vector3.forward;
            return _tracker.pathCreator.path.GetClosestPointDirectionOnPath(worldPos);
        }

        public void GetClosestPointAndDirectionOnPath(Vector3 worldPos, out Vector3 point, out Vector3 direction)
        {
            if (_tracker == null || _tracker.pathCreator == null || _tracker.pathCreator.path == null)
            {
                point = Vector3.zero; direction = Vector3.forward; return;
            }
            var res = _tracker.pathCreator.path.GetClosestPointAndDirectionOnPath(worldPos);
            point = res.Item1;
            direction = res.Item2;
        }

        // Arc-length queries (absolute s)
        public Vector3 GetPointAtDistance(float s)
        {
            if (_tracker == null || _tracker.pathCreator == null || _tracker.pathCreator.path == null)
                return Vector3.zero;
            return _tracker.pathCreator.path.GetPointAtDistance(ClampDistance(s));
        }

        public Vector3 GetDirectionAtDistance(float s)
        {
            if (_tracker == null || _tracker.pathCreator == null || _tracker.pathCreator.path == null)
                return Vector3.forward;
            return _tracker.pathCreator.path.GetDirectionAtDistance(ClampDistance(s));
        }

        public void GetPointAndDirectionAtDistance(float s, out Vector3 point, out Vector3 direction)
        {
            if (_tracker == null || _tracker.pathCreator == null || _tracker.pathCreator.path == null)
            {
                point = Vector3.zero; direction = Vector3.forward; return;
            }
            var res = _tracker.pathCreator.path.GetPointAndDirAtDistance(ClampDistance(s));
            point = res.Item1;
            direction = res.Item2;
        }

        // Offset queries relative to current vehicle position
        public Vector3 GetPointAhead(float offsetMeters)
        {
            return GetPointAtDistance(DistanceTravelled + offsetMeters);
        }

        public Vector3 GetDirectionAhead(float offsetMeters)
        {
            return GetDirectionAtDistance(DistanceTravelled + offsetMeters);
        }

        public void GetPointAndDirectionAhead(float offsetMeters, out Vector3 point, out Vector3 direction)
        {
            GetPointAndDirectionAtDistance(DistanceTravelled + offsetMeters, out point, out direction);
        }

        // Utility
        public float ClampDistance(float s)
        {
            if (s <= 0f) return 0f;
            float max = MaxPathDistance;
            return s >= max ? max : s;
        }

        private void EnsureCacheFresh()
        {
            if (_tracker == null || _tracker.pathCreator == null || _tracker.pathCreator.path == null)
                return;

            var vpath = _tracker.pathCreator.path;
            int frame = Time.frameCount;
            int version = _tracker.PathVersion;
            int n = vpath.NumPoints;
            float len = vpath.length;

            // PathVersion catches path changes between physics steps within the same frame
            bool needRebuild = (_cacheFrame != frame) || (version != _cachedPathVersion) || (n != _cachedNumPoints) || !Mathf.Approximately(len, _cachedPathLength);
            if (!needRebuild)
                return;

            _cacheFrame = frame;
            _cachedPathVersion = version;
            _cachedNumPoints = n;
            _cachedPathLength = len;

            // Allocate or resize buffers
            if (_vertices == null || _vertices.Length != n)
            {
                _vertices = new Vector3[n];
                _tangents = new Vector3[n];
                _cumulative = new float[n];
                _verticesView = new ReadOnlyVec3List(_vertices);
                _tangentsView = new ReadOnlyVec3List(_tangents);
                _cumulativeView = new ReadOnlyFloatList(_cumulative);
            }

            // Populate from VertexPath API; do not read any arrays by reference
            for (int i = 0; i < n; i++)
            {
                _vertices[i] = vpath.GetPoint(i);
                _tangents[i] = vpath.GetTangent(i);
            }

            // cumulativeLengthAtEachVertex is typically provided by VertexPath; if not, compute from diffs
            var cum = vpath.cumulativeLengthAtEachVertex; // may be public in PathCreator package
            if (cum != null && cum.Length == n)
            {
                for (int i = 0; i < n; i++) _cumulative[i] = cum[i];
            }
            else
            {
                float acc = 0f;
                _cumulative[0] = 0f;
                for (int i = 1; i < n; i++)
                {
                    acc += Vector3.Distance(_vertices[i - 1], _vertices[i]);
                    _cumulative[i] = acc;
                }
            }

            // Populate Bézier segment control points (transform to world space)
            var creator = _tracker.pathCreator;
            var bpath = (creator != null) ? creator.bezierPath : null;
            int segCount = (bpath != null) ? bpath.NumSegments : 0;

            bool needSegRebuild = (_cachedNumSegments != segCount);
            if (needSegRebuild && segCount > 0)
            {
                _bezP0 = new Vector3[segCount];
                _bezP1 = new Vector3[segCount];
                _bezP2 = new Vector3[segCount];
                _bezP3 = new Vector3[segCount];
                _bezP0View = new ReadOnlyVec3List(_bezP0);
                _bezP1View = new ReadOnlyVec3List(_bezP1);
                _bezP2View = new ReadOnlyVec3List(_bezP2);
                _bezP3View = new ReadOnlyVec3List(_bezP3);
                _cachedNumSegments = segCount;
            }

            if (segCount > 0)
            {
                var xf = creator.transform;
                for (int si = 0; si < segCount; si++)
                {
                    // GetPointsInSegment returns local-space control points
                    var pts = bpath.GetPointsInSegment(si);
                    _bezP0[si] = xf.TransformPoint(pts[0]);
                    _bezP1[si] = xf.TransformPoint(pts[1]);
                    _bezP2[si] = xf.TransformPoint(pts[2]);
                    _bezP3[si] = xf.TransformPoint(pts[3]);
                }
            }
            else
            {
                _cachedNumSegments = 0;
                _bezP0 = _bezP1 = _bezP2 = _bezP3 = null;
                _bezP0View = _bezP1View = _bezP2View = _bezP3View = null;
            }
        }

        public IReadOnlyList<Vector3> Vertices { get { EnsureCacheFresh(); return _verticesView ?? ReadOnlyVec3List.Empty; } }
        public IReadOnlyList<Vector3> Tangents { get { EnsureCacheFresh(); return _tangentsView ?? ReadOnlyVec3List.Empty; } }
        public IReadOnlyList<float>   CumulativeLength { get { EnsureCacheFresh(); return _cumulativeView ?? ReadOnlyFloatList.Empty; } }

        // Bézier control points aligned by segment index (0..BezierSegmentCount-1)
        public int BezierSegmentCount { get { EnsureCacheFresh(); return _bezP0 != null ? _bezP0.Length : 0; } }
        public IReadOnlyList<Vector3> BezierP0 { get { EnsureCacheFresh(); return _bezP0View ?? ReadOnlyVec3List.Empty; } }
        public IReadOnlyList<Vector3> BezierP1 { get { EnsureCacheFresh(); return _bezP1View ?? ReadOnlyVec3List.Empty; } }
        public IReadOnlyList<Vector3> BezierP2 { get { EnsureCacheFresh(); return _bezP2View ?? ReadOnlyVec3List.Empty; } }
        public IReadOnlyList<Vector3> BezierP3 { get { EnsureCacheFresh(); return _bezP3View ?? ReadOnlyVec3List.Empty; } }

        // Convenience: copy out the four control points of the current closest segment
        public bool TryGetClosestBezierSegmentSummary(out int segIndex, out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3)
        {
            EnsureCacheFresh();
            segIndex = CurrentBezierSegmentIndex;
            if (segIndex < 0 || _bezP0 == null || segIndex >= _bezP0.Length)
            {
                p0 = p1 = p2 = p3 = Vector3.zero; return false;
            }
            p0 = _bezP0[segIndex];
            p1 = _bezP1[segIndex];
            p2 = _bezP2[segIndex];
            p3 = _bezP3[segIndex];
            return true;
        }

        public bool TryMapDistanceToVertexIndex(float s, out int index)
        {
            EnsureCacheFresh();
            index = -1;
            if (_cumulative == null || _cumulative.Length == 0)
                return false;

            float clamped = ClampDistance(s);
            int lo = 0, hi = _cumulative.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_cumulative[mid] < clamped) lo = mid + 1; else hi = mid;
            }
            index = lo;
            return true;
        }

        public bool TryMapWindowToVertexRange(float s0, float s1, out int i0, out int i1)
        {
            EnsureCacheFresh();
            i0 = i1 = -1;
            if (_cumulative == null || _cumulative.Length == 0)
                return false;
            float a = ClampDistance(Mathf.Min(s0, s1));
            float b = ClampDistance(Mathf.Max(s0, s1));
            // lower bound for a
            int lo = 0, hi = _cumulative.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_cumulative[mid] < a) lo = mid + 1; else hi = mid;
            }
            i0 = lo;
            // upper bound for b
            lo = 0; hi = _cumulative.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (_cumulative[mid] <= b) lo = mid; else hi = mid - 1;
            }
            i1 = lo;
            if (i1 < i0) i1 = i0;
            return true;
        }

        public IEnumerable<(float s, Vector3 point, Vector3 tangent)> EnumerateWindow(float s0, float s1, int stride = 1)
        {
            EnsureCacheFresh();
            if (_cumulative == null || _cumulative.Length == 0)
                yield break;
            if (stride <= 0) stride = 1;

            if (!TryMapWindowToVertexRange(s0, s1, out int i0, out int i1))
                yield break;

            if (i0 > i1) yield break;

            for (int i = i0; i <= i1; i += stride)
            {
                yield return (_cumulative[i], _vertices[i], _tangents[i]);
            }
        }

        public bool TryGetVertexSample(int index, out float s, out Vector3 point, out Vector3 tangent)
        {
            EnsureCacheFresh();
            s = 0f; point = Vector3.zero; tangent = Vector3.forward;
            if (_cumulative == null || index < 0 || index >= _cumulative.Length)
                return false;
            s = _cumulative[index];
            point = _vertices[index];
            tangent = _tangents[index];
            return true;
        }

        public IEnumerable<(int index, float s, Vector3 point, Vector3 tangent)> EnumerateWindowIndexed(float s0, float s1, int stride = 1)
        {
            EnsureCacheFresh();
            if (_cumulative == null || _cumulative.Length == 0)
                yield break;
            if (stride <= 0) stride = 1;

            if (!TryMapWindowToVertexRange(s0, s1, out int i0, out int i1))
                yield break;

            if (i0 > i1) yield break;

            for (int i = i0; i <= i1; i += stride)
            {
                yield return (i, _cumulative[i], _vertices[i], _tangents[i]);
            }
        }
    }

    // Lightweight read-only views over our private arrays
    internal sealed class ReadOnlyVec3List : IReadOnlyList<Vector3>
    {
        public static readonly ReadOnlyVec3List Empty = new ReadOnlyVec3List(System.Array.Empty<Vector3>());
        private readonly Vector3[] _src;
        public ReadOnlyVec3List(Vector3[] src) { _src = src; }
        public int Count => _src.Length;
        public Vector3 this[int index] => _src[index]; // value copy
        public IEnumerator<Vector3> GetEnumerator() { for (int i = 0; i < _src.Length; i++) yield return _src[i]; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal sealed class ReadOnlyFloatList : IReadOnlyList<float>
    {
        public static readonly ReadOnlyFloatList Empty = new ReadOnlyFloatList(System.Array.Empty<float>());
        private readonly float[] _src;
        public ReadOnlyFloatList(float[] src) { _src = src; }
        public int Count => _src.Length;
        public float this[int index] => _src[index];
        public IEnumerator<float> GetEnumerator() { for (int i = 0; i < _src.Length; i++) yield return _src[i]; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}