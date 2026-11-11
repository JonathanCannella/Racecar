//#define PATH_TRACKER_DIAG

using UnityEngine;
using PathCreation;


namespace PathCreation.Examples
{
    // Moves along a path at constant speed.
    // Depending on the end of path instruction, will either loop, reverse, or stop at the end of the path.
    public class PathTracker : MonoBehaviour
    {
        public PathCreator pathCreator;
        RoadMeshCreator roadMeshCreator;
        float lastDistanceTravelled = 0f;
        public float distanceTravelled = 0f;
        public float totalDistanceTravelled = 0f;
        public Vector3 closestPointOnPath;
        public Vector3 closestPointDirectionOnPath;
        public int currentBezierSegmentIndex;
        public int currentClosestPathPointIndex;
        //public int currentSegment;

        public Vector3 previousPosition;
        // --- Debug ---
        public float DB_PhysicalDistanceTravelled = 0f;

        //float oldestBezierSegmentLen =  -1f;

        public Vector3 eulerOffsetRot = Vector3.zero;

        // --- Coherence search (unified for rolls and per-frame movement) ---
        [Header("Coherence Search Settings")]
        [Tooltip("Max forward distance to scan when searching for the first local minimum (meters). Used in Update and OnPathChanged.")]
        public float maxForwardSearchMeters = 60f;
        [Tooltip("Project positions to horizontal plane (XZ) for distance tests.")]
        public bool projectToHorizontal = true;

        [Tooltip("Multiplier applied to |velocityHint| * deltaTime to size the forward search window in Update().")]
        public float forwardSearchMultiplier = 1.2f;
        [Tooltip("Minimum forward search distance in Update(), even at very low speeds (meters).")]
        public float minForwardSearchMeters = 5f;
        [Tooltip("Do not update tracking if car falls below this Y (meters). Use a negative value to gate only when falling below ground.")]
        public float fallYStopThreshold = -0.5f;

        [Tooltip("Minimum number of segments to scan forward each frame (segment-based coherence).")]
        public int minForwardScanSegments = 2;
        [Tooltip("Hard cap on segments to scan forward each frame (segment-based coherence).")]
        public int maxForwardScanSegmentsCap = 128;
        [Tooltip("Segments to scan forward on OnPathChanged() when starting from index 0.")]
        public int onPathChangedScanSegments = 256;

        [Tooltip("Segments to look behind current index when re-finding position after a roll.")]
        public int onPathChangedLookBehindSegments = 2;
        [Tooltip("Tolerance (m) for treating a rebase delta as the expected first-span deletion; logs are suppressed within this tolerance.")]
        public float coherenceRebaseToleranceMeters = 0.25f;

        [Tooltip("Meters of change in along-path distance in one frame that will be treated as a suspected coherence jump and logged.")]
        public float coherenceJumpThresholdMeters = 5f;

        // Debug info from the last coherence solve (not used in logic)
        private int _dbgLastChosenSeg = -1;
        private float _dbgLastChosenT = 0f;

        // Cache of the previous frame's first-span length (cum[anchor[1]] - cum[anchor[0]])
        private float _cachedFirstSpanLen = 0f;

        // Remember last expected rebase length and when it happened, so logs can suppress expected deltas
        private float _lastRebaseExpectedLen = 0f;
        private bool _rebasedSinceLastStep = false;

        private bool _initialized;
        private bool _warnedMissingPathCreator;

        public float HalfRoadWidth
        {
            get => roadMeshCreator != null ? roadMeshCreator.roadWidth : 0f;
        }

        public bool PathInitialized { get; private set; }

        // Incremented whenever the path changes (or the tracker is bound to a new path),
        // so cached path data can be invalidated even within a single frame.
        public int PathVersion { get; private set; }

        private void Awake()
        {
        }

        void Start()
        {
            TryInit();
            previousPosition = transform.position;
        }

        private void OnEnable()
        {
            TryInit();
            previousPosition = transform.position;
        }

        private void OnDisable()
        {
            if (pathCreator != null)
                pathCreator.pathUpdated -= OnPathChanged;
            _initialized = false;
            _warnedMissingPathCreator = false;
        }

        private void TryInit()
        {
            if (_initialized)
                return;

            if (pathCreator == null)
            {
                if (!_warnedMissingPathCreator)
                {
                    Debug.Log("No pathCreator assigned!");
                    _warnedMissingPathCreator = true;
                }
                return;
            }
            _warnedMissingPathCreator = false;

            roadMeshCreator = pathCreator.GetComponent<RoadMeshCreator>();
            if (roadMeshCreator == null)
            {
                Debug.Log("No roadMeshCreator found!");
            }

            PathInitialized = pathCreator.IsPathInitialized;
            if (PathInitialized)
            {
                _cachedFirstSpanLen = ComputeFirstSpanLen(pathCreator.path);
            }
            pathCreator.pathUpdated += OnPathChanged;
            ++PathVersion;

            _initialized = true;
        }

        public void SetPathCreator(PathCreator pc)
        {
            if (pathCreator == pc) return;
            if (pathCreator != null)
                pathCreator.pathUpdated -= OnPathChanged;
            pathCreator = pc;
            _initialized = false;
            TryInit();
            previousPosition = transform.position;
            if (PathInitialized)
            {
                _cachedFirstSpanLen = ComputeFirstSpanLen(pathCreator.path);
            }
        }

        public float MaxPathDistance
        {
            get => pathCreator.path.cumulativeLengthAtEachVertex[pathCreator.path.cumulativeLengthAtEachVertex.Length - 1];
        }



        public void ResetToDistance(float dist)
        {
            if (dist < 0f)
                Debug.LogError("Don't pass negative distances!");

            if (dist > MaxPathDistance)
                Debug.LogError("Don't pass distance further than the end!");


            SetDistance(dist);
        }

        public void ResetTotalDistance()
        {
            this.totalDistanceTravelled = 0f;
            previousPosition = transform.position;
        }

        void SetDistance(float dist)
        {
            //Debug.Log($"SetDistance({dist})");
            distanceTravelled = dist;

            // this could be subtracting and that is ok
            totalDistanceTravelled += (distanceTravelled - lastDistanceTravelled);

            lastDistanceTravelled = distanceTravelled;

            var tup = pathCreator.path.GetPointAndDirAtDistance(distanceTravelled);

            //closestPointOnPath = pathCreator.path.GetPointAtDistance(distanceTravelled);
            //closestPointDirectionOnPath = pathCreator.path.GetDirectionAtDistance(distanceTravelled);

            closestPointOnPath = tup.Item1;
            closestPointDirectionOnPath = tup.Item2;


            currentBezierSegmentIndex = pathCreator.path.GetBezierSegmentIndexAtDistance(distanceTravelled);

            currentClosestPathPointIndex = pathCreator.path.GetPreviousSegmentIndexAtDistance(distanceTravelled);
        }

        // called if the path is adjusted by deleting the oldest bezier segment
        void AdjustDistance(float dist, float? rebasePred = null, float? pred = null, float? deltaPred = null, float? expectedSpan = null, float? cachedFirstSpan = null, float? currentFirstSpan = null, int? chosenSeg = null, float? chosenT = null)
        {
#if PATH_TRACKER_DIAG
            float adjDelta = dist - distanceTravelled;
            bool looksLikeExpectedRebase = _lastRebaseExpectedLen > 0f && Mathf.Abs(Mathf.Abs(adjDelta) - _lastRebaseExpectedLen) <= coherenceRebaseToleranceMeters;
            // Only print the unified log if the caller indicates a rebase/jump (rebasePred!=null), or if this is a large unexpected adjustment
            bool shouldLog = false;
            if (rebasePred != null && pred != null && deltaPred != null && expectedSpan != null && cachedFirstSpan != null && currentFirstSpan != null && chosenSeg != null && chosenT != null)
            {
                // OnPathChanged is passing all info
                bool matchCached = cachedFirstSpan > 0f && Mathf.Abs(Mathf.Abs(dist - distanceTravelled) - cachedFirstSpan.Value) <= coherenceRebaseToleranceMeters;
                bool matchNow   = currentFirstSpan > 0f && Mathf.Abs(Mathf.Abs(dist - distanceTravelled) - currentFirstSpan.Value) <= coherenceRebaseToleranceMeters;
                if ((!matchCached && !matchNow && Mathf.Abs(deltaPred.Value) >= coherenceJumpThresholdMeters))
                    shouldLog = true;
            }
            else if (!looksLikeExpectedRebase && Mathf.Abs(adjDelta) >= coherenceJumpThresholdMeters)
            {
                shouldLog = true;
            }
            if (shouldLog)
            {
                string log = $"[PathTracker] Rebase/jump: frame={Time.frameCount}";
                log += $" from={distanceTravelled:F3} to={dist:F3}";
                if (pred != null) log += $" pred={pred.Value:F3}";
                log += $" Δ={(dist - distanceTravelled):F3}";
                if (deltaPred != null) log += $" Δ(pred)={deltaPred.Value:F3}";
                if (expectedSpan != null) log += $" expectedSpan={expectedSpan.Value:F3}";
                if (cachedFirstSpan != null) log += $" cachedFirstSpan={cachedFirstSpan.Value:F3}";
                if (currentFirstSpan != null) log += $" currentFirstSpan={currentFirstSpan.Value:F3}";
                if (chosenSeg != null) log += $" chosenSeg={chosenSeg.Value}";
                if (chosenT != null) log += $" t={chosenT.Value:F3}";
                Debug.LogWarning(log);
            }
#endif
            var diff = distanceTravelled - dist;
            distanceTravelled = dist;

            //lastDistanceTravelled = distanceTravelled;
            lastDistanceTravelled -= diff;

            var tup = pathCreator.path.GetPointAndDirAtDistance(distanceTravelled);
            closestPointOnPath = tup.Item1;
            closestPointDirectionOnPath = tup.Item2;
            currentBezierSegmentIndex = pathCreator.path.GetBezierSegmentIndexAtDistance(distanceTravelled);
            currentClosestPathPointIndex = pathCreator.path.GetPreviousSegmentIndexAtDistance(distanceTravelled);
        }


        // Tracks at the physics rate so vehicle controls (FixedUpdate) see path data for the current
        // step. Script execution order (-50) runs this before the vehicles' FixedUpdate.
        void FixedUpdate()
        {
            //if (oldestBezierSegmentLen < 0f)
            //{

            //    oldestBezierSegmentLen = pathCreator.path.cumulativeLengthAtEachVertex[];
            //}

            if (!_initialized)
                TryInit();

            if (pathCreator != null)
            {
                // If the car is falling off the track (below threshold), skip updating tracking this frame
                if (transform.position.y < fallYStopThreshold)
                {
                    previousPosition = transform.position;
                    return;
                }

                // Track physical movement in XZ
                Vector3 curr = transform.position;
                Vector3 prev = previousPosition;
                Vector3 currXZ = curr; currXZ.y = 0f;
                Vector3 prevXZ = prev; prevXZ.y = 0f;
                float physicalDeltaXZ = (currXZ - prevXZ).magnitude;
                DB_PhysicalDistanceTravelled += physicalDeltaXZ;

                var vp = pathCreator.path;

                // Determine how many segments to scan based on speed
                float velMag = velocityHint.magnitude;
                float dynForwardMeters = Mathf.Clamp(velMag * Time.deltaTime * forwardSearchMultiplier,
                                                     minForwardSearchMeters,
                                                     maxForwardSearchMeters);
                // Convert meters to an estimated segment count by walking cum lengths forward from current index
                int startIdx = Mathf.Clamp(currentClosestPathPointIndex, 0, vp.NumPoints - 2);
                int maxSegs = Mathf.Max(minForwardScanSegments, EstimateSegmentsForDistance(vp, startIdx, dynForwardMeters));
                maxSegs = Mathf.Min(maxSegs, maxForwardScanSegmentsCap);

                float d = FindLocalMinDistanceBySegments(vp, transform.position, startIdx, maxSegs);

                float prevDist = distanceTravelled;
                float delta = d - prevDist;
                bool justRebased = _rebasedSinceLastStep;
                _rebasedSinceLastStep = false;
#if PATH_TRACKER_DIAG
                if (!justRebased && Mathf.Abs(delta) >= coherenceJumpThresholdMeters)
                {
                    // Gather a small window of segment lengths around the chosen segment for context
                    var cumLen = vp.cumulativeLengthAtEachVertex;
                    int lastSeg = vp.NumPoints - 2;
                    int s0 = Mathf.Clamp(_dbgLastChosenSeg - 2, 0, lastSeg);
                    int s1 = Mathf.Clamp(_dbgLastChosenSeg + 2, 0, lastSeg);
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    sb.Append("[");
                    for (int si = s0; si <= s1; si++)
                    {
                        float len = Mathf.Max(1e-6f, cumLen[si + 1] - cumLen[si]);
                        sb.Append(len.ToString("F2"));
                        if (si < s1) sb.Append(",");
                    }
                    sb.Append("]");

                    Debug.LogWarning($"[PathTracker] Coherence jump? frame={Time.frameCount} prevDist={prevDist:F3} newDist={d:F3} Δ={delta:F3} chosenSeg={_dbgLastChosenSeg} t={_dbgLastChosenT:F3} startIdx={startIdx} maxSegs={maxSegs} |vel|={velMag:F2} dynForward={dynForwardMeters:F2} segLensAroundChosen={sb}");
                }
#endif

                SetDistance(d);
                previousPosition = transform.position;
            }
        }

        // If the path changes during the game, update the distance travelled so that the follower's position on the new path
        // is as close as possible to its position on the old path
        void OnPathChanged()
        {
            ++PathVersion;

            if (!PathInitialized)
                PathInitialized = pathCreator.IsPathInitialized;

            var vp = pathCreator.path;
            if (vp == null || vp.cumulativeLengthAtEachVertex == null || vp.cumulativeLengthAtEachVertex.Length < 2)
            {
                AdjustDistance(lastDistanceTravelled);
                return;
            }

            // Predict where we should land by subtracting the cached first-span length
            float firstSpanCached = _cachedFirstSpanLen > 0f ? _cachedFirstSpanLen : ComputeFirstSpanLen(vp);
            float pred = Mathf.Clamp(lastDistanceTravelled - firstSpanCached, 0f, MaxPathDistance);

            // Convert prediction to a starting segment index with a small look-behind
            int predStartIdx = Mathf.Clamp(vp.GetPreviousSegmentIndexAtDistance(pred) - Mathf.Max(0, onPathChangedLookBehindSegments), 0, vp.NumPoints - 2);

            int maxSegs = Mathf.Max(onPathChangedScanSegments, minForwardScanSegments);
            float newD = FindLocalMinDistanceBySegments(vp, previousPosition, predStartIdx, maxSegs);

            float prevD = distanceTravelled;
            float deltaPred = newD - pred;

            // Compare against the cached estimate and the newly computed current first-span
            float firstSpanNow = ComputeFirstSpanLen(vp);
            bool matchCached = firstSpanCached > 0f && Mathf.Abs(Mathf.Abs(newD - prevD) - firstSpanCached) <= coherenceRebaseToleranceMeters;
            bool matchNow   = firstSpanNow   > 0f && Mathf.Abs(Mathf.Abs(newD - prevD) - firstSpanNow)   <= coherenceRebaseToleranceMeters;

            // Record expected rebase magnitude for log suppression in AdjustDistance/Update
            _lastRebaseExpectedLen = matchCached ? firstSpanCached : (matchNow ? firstSpanNow : 0f);
            _rebasedSinceLastStep = true;

            // Unified log: only print once per rebase event, with all details
            AdjustDistance(
                newD,
                rebasePred: 1f, // dummy non-null to trigger log
                pred: pred,
                deltaPred: deltaPred,
                expectedSpan: matchCached ? firstSpanCached : (matchNow ? firstSpanNow : 0f),
                cachedFirstSpan: firstSpanCached,
                currentFirstSpan: firstSpanNow,
                chosenSeg: _dbgLastChosenSeg,
                chosenT: _dbgLastChosenT
            );

            // Refresh cache for next roll
            _cachedFirstSpanLen = firstSpanNow;
        }

        static Vector3 ToHorizontal(Vector3 v, bool proj)
        {
            if (proj) { v.y = 0f; }
            return v;
        }

        // Returns along-path distance for the first local-minimum segment starting at prevIndex.
        float FindLocalMinDistanceBySegments(VertexPath vp, Vector3 worldPos, int prevIndex, int maxForwardSegments)
        {
            if (vp == null || vp.NumPoints < 2) return distanceTravelled;

            int lastSeg = vp.NumPoints - 2;
            int i0 = Mathf.Clamp(prevIndex, 0, lastSeg);
            int iEnd = Mathf.Min(lastSeg, i0 + Mathf.Max(1, maxForwardSegments));

            Vector3 p = worldPos; if (projectToHorizontal) p.y = 0f;

            float bestDSq = float.PositiveInfinity;
            int bestI = i0;
            float bestT = 0f;

            float prevDSq = float.PositiveInfinity;
            bool seenDecrease = false;

            for (int i = i0; i <= iEnd; i++)
            {
                Vector3 a = vp.GetPoint(i);
                Vector3 b = vp.GetPoint(i + 1);
                if (projectToHorizontal) { a.y = 0f; b.y = 0f; }

                float t;
                float dSq = PointToSegmentDistanceSqXZ(p, a, b, out t);

                if (dSq < bestDSq)
                {
                    bestDSq = dSq; bestI = i; bestT = Mathf.Clamp01(t);
                }

                if (dSq < prevDSq - 1e-6f) { seenDecrease = true; }
                else if (seenDecrease && dSq > prevDSq + 1e-6f) { break; }

                prevDSq = dSq;
            }

            // Convert (bestI, bestT) to along-path distance
            var cum = vp.cumulativeLengthAtEachVertex;
            float aLen = cum[bestI];
            float segLen = Mathf.Max(1e-6f, cum[bestI + 1] - cum[bestI]);

            // Store debug selection
            _dbgLastChosenSeg = bestI;
            _dbgLastChosenT = bestT;

            return aLen + bestT * segLen;
        }

        static float PointToSegmentDistanceSqXZ(Vector3 p, Vector3 a, Vector3 b, out float t)
        {
            Vector3 ab = b - a;
            float ab2 = ab.x * ab.x + ab.z * ab.z;
            if (ab2 < 1e-9f) { t = 0f; float dx = p.x - a.x, dz = p.z - a.z; return dx * dx + dz * dz; }
            float apx = p.x - a.x, apz = p.z - a.z;
            t = (apx * ab.x + apz * ab.z) / ab2;
            if (t <= 0f) { float dx = apx, dz = apz; return dx * dx + dz * dz; }
            if (t >= 1f) { float dx = p.x - b.x, dz = p.z - b.z; return dx * dx + dz * dz; }
            float projx = a.x + t * ab.x, projz = a.z + t * ab.z;
            float ddx = p.x - projx, ddz = p.z - projz;
            return ddx * ddx + ddz * ddz;
        }

        int EstimateSegmentsForDistance(VertexPath vp, int startIndex, float forwardMeters)
        {
            if (vp == null || vp.NumPoints < 2) return minForwardScanSegments;
            var cum = vp.cumulativeLengthAtEachVertex;
            int lastSeg = vp.NumPoints - 2;
            int i0 = Mathf.Clamp(startIndex, 0, lastSeg);
            float startLen = cum[i0];
            float target = startLen + Mathf.Max(0f, forwardMeters);
            int count = 0;
            for (int i = i0; i <= lastSeg; i++)
            {
                count++;
                if (cum[i + 1] >= target) break;
            }
            return count;
        }

        private Vector3 velocityHint;
        public void SetVelocityHint(Vector3 vel)
        {
            velocityHint = vel;
        }


        static float ComputeFirstSpanLen(VertexPath vp)
        {
            // Robust fallback that does not rely on VertexPath.localAnchorVertexIndex.
            // We scan vertices from the start until the underlying Bezier segment index changes,
            // and return the arc length between those two boundaries.
            if (vp == null) return 0f;
            var cum = vp.cumulativeLengthAtEachVertex;
            if (cum == null || cum.Length < 2) return 0f;

            // Sample a tiny epsilon into the first segment to avoid sitting exactly on a boundary
            const float eps = 1e-3f;
            float d0 = Mathf.Min(cum[0] + eps, cum[cum.Length - 1]);
            int seg0 = vp.GetBezierSegmentIndexAtDistance(d0);
            int lastSeg = cum.Length - 2; // last valid segment index over vertices

            // Walk forward until the Bezier segment index changes
            for (int i = 0; i <= lastSeg; i++)
            {
                float dA = Mathf.Max(cum[i], 0f);
                float dB = cum[i + 1];
                // Look slightly inside [dA,dB] to get the segment id
                float probe = Mathf.Min(dB - 1e-5f, dA + 0.5f * (dB - dA));
                if (probe < dA) probe = dA; // safeguard tiny segments
                int segI = vp.GetBezierSegmentIndexAtDistance(probe);
                if (segI != seg0)
                {
                    // The boundary between first and second Bezier spans is at vertex i
                    return Mathf.Max(0f, cum[i] - cum[0]);
                }
            }

            // If we never observed a change, the whole path is one span; return its total length
            return Mathf.Max(0f, cum[cum.Length - 1] - cum[0]);
        }
    }
}
