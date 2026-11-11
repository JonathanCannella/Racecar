using UnityEngine;

namespace PathCreation.Examples
{
    // Moves along a path at constant speed.
    // Depending on the end of path instruction, will either loop, reverse, or stop at the end of the path.
    public class BogusPathTracker : MonoBehaviour
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

        [Header("Coherence settings")]
        [Tooltip("Use a local, coherence-aware search instead of the global closest-point search.")]
        [SerializeField] private bool enableCoherentSearch = true;

        [Tooltip("How many tessellated polyline segments to search backward and forward around the last known index.")]
        [SerializeField, Min(1)] private int searchRadiusVertices = 24;

        [Tooltip("Half-width of the distance window (in meters) searched around the predicted along-track distance.")]
        [SerializeField, Min(0.1f)] private float searchHalfWindowMeters = 25f;

        [Tooltip("Hard cap on how far the tracked distance may change per frame, in meters. Set to 0 to disable.")]
        [SerializeField, Min(0f)] private float hardMaxDeltaMetersPerFrame = 0f;

        [Tooltip("Multiplier for speed*dt used to center the search window; also limits per-frame distance change. Requires a velocity hint. Set to 0 to disable.")]
        [SerializeField, Min(0f)] private float speedDeltaMultiplier = 1.5f;

        [Tooltip("Penalty weight for deviation from the expected per-frame distance window.")]
        [SerializeField, Min(0f)] private float coherencePenaltyWeight = 4f;

        [Tooltip("Preference weight for candidates whose direction aligns with the velocity hint.")]
        [SerializeField, Min(0f)] private float velocityAlignmentWeight = 1f;

        [Tooltip("Reject candidates that fall outside the road tube (HalfRoadWidth + margin). Set false to disable.")]
        [SerializeField] private bool enforceTrackWidthGate = true;

        [Tooltip("Additional margin beyond HalfRoadWidth when gating candidates by track width.")]
        [SerializeField, Min(0f)] private float lateralMarginMeters = 0.5f;

        [Tooltip("Penalty weight for large frame-to-frame changes in signed lateral offset (meters).")]
        [SerializeField, Min(0f)] private float lateralPenaltyWeight = 2f;

        [Tooltip("Meters of allowable change in signed lateral offset per frame before penalization.")]
        [SerializeField, Min(0f)] private float lateralJumpMetersAllowed = 1.0f;

        [Tooltip("Log a warning when |chosenDist - predictedDist| exceeds this many meters. Set 0 to disable logging.")]
        [SerializeField, Min(0f)] private float coherenceJumpLogThresholdMeters = 10f;

        [Tooltip("Optional velocity hint supplied by the vehicle in world space. If zero, alignment is ignored.")]
        public Vector3 velocityHint = Vector3.zero;

        public Vector3 previousPosition;

        private float previousLateralOffset = 0f;

        //float oldestBezierSegmentLen =  -1f;

        public Vector3 eulerOffsetRot = Vector3.zero;

        private bool _initialized;
        private bool _warnedMissingPathCreator;

        // --- Coherence cache (for roll events) ---
        // Previous frame's polyline segment index (i) and interpolation percent (t in [0,1]) along [i,i+1]
        private int _cachedPrevVertexIndex = 0;
        private float _cachedPrevPercent = 0f;
        // Previous frame's first Bézier segment vertex count (how many polyline vertices were removed on roll)
        private int _cachedFirstSegVertexCount = 0;

        public float HalfRoadWidth
        {
            get => roadMeshCreator != null ? roadMeshCreator.roadWidth : 0f;
        }

        public bool PathInitialized { get; private set; }

        private void Awake()
        {
        }

        void Start()
        {
            TryInit();
        }

        private void OnEnable()
        {
            TryInit();
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
            pathCreator.pathUpdated += OnPathChanged;

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
        }

        public void SetVelocityHint(Vector3 v)
        {
            velocityHint = v;
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
            //this.lastDistanceTravelled = 0f;
            //this.distanceTravelled = 0f;
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

            // Update signed lateral offset using a right vector in XZ plane (assumes up = Vector3.up)
            Vector3 right = Vector3.Cross(Vector3.up, closestPointDirectionOnPath).normalized;
            previousLateralOffset = Vector3.Dot(transform.position - closestPointOnPath, right);

            currentBezierSegmentIndex = pathCreator.path.GetBezierSegmentIndexAtDistance(distanceTravelled);

            currentClosestPathPointIndex = pathCreator.path.GetPreviousSegmentIndexAtDistance(distanceTravelled);

            // Keep coherence cache updated each frame
            UpdateCoherenceCacheWithIndex(currentClosestPathPointIndex);
        }

        // called if the path is adjusted by deleting the oldest bezier segment
        void AdjustDistance(float dist)
        {
            distanceTravelled = dist;

            // this could be subtracting and that is ok
            //totalDistanceTravelled += (distanceTravelled - lastDistanceTravelled);

            lastDistanceTravelled = distanceTravelled;

            //closestPointOnPath = pathCreator.path.GetPointAtDistance(distanceTravelled);

            //closestPointDirectionOnPath = pathCreator.path.GetDirectionAtDistance(distanceTravelled);

            var tup = pathCreator.path.GetPointAndDirAtDistance(distanceTravelled);

            closestPointOnPath = tup.Item1;
            closestPointDirectionOnPath = tup.Item2;

            Vector3 right = Vector3.Cross(Vector3.up, closestPointDirectionOnPath).normalized;
            previousLateralOffset = Vector3.Dot(transform.position - closestPointOnPath, right);

            currentBezierSegmentIndex = pathCreator.path.GetBezierSegmentIndexAtDistance(distanceTravelled);

            currentClosestPathPointIndex = pathCreator.path.GetPreviousSegmentIndexAtDistance(distanceTravelled);

            // Keep coherence cache updated after adjustments
            UpdateCoherenceCacheWithIndex(currentClosestPathPointIndex);

            //if (currentBezierSegmentIndex > 3)
            //    Debug.Log($"WHY IS THIS GREATER THAN 3 RIGHT NOW?: {currentBezierSegmentIndex}");
        }


        void Update()
        {
            //if (oldestBezierSegmentLen < 0f)
            //{

            //    oldestBezierSegmentLen = pathCreator.path.cumulativeLengthAtEachVertex[];
            //}

            if (!_initialized)
                TryInit();

            if (pathCreator != null)
            {
                float dist = enableCoherentSearch
                    ? GetCoherentDistanceAlongPath(transform.position, Time.deltaTime)
                    : pathCreator.path.GetClosestDistanceAlongPath(transform.position);
                SetDistance(dist);
                previousPosition = transform.position;
            }
        }

        // If the path changes during the game, update the distance travelled so that the follower's position on the new path
        // is as close as possible to its position on the old path
        void OnPathChanged()
        {
            // this is a bit fragile. assumes that only the last bezier segment was deleted on the change

            if(!PathInitialized)
                PathInitialized = pathCreator.IsPathInitialized;

            var vp = pathCreator.path;
            if (vp == null || vp.NumPoints < 2 || vp.cumulativeLengthAtEachVertex == null || vp.cumulativeLengthAtEachVertex.Length < 2)
            {
                // Fallback: if path is degenerate, do a safe global projection
                AdjustDistance(pathCreator.path.GetClosestDistanceAlongPath(previousPosition));
                return;
            }

            // Shift cached polyline index by the number of vertices that the first Bézier segment contributed
            int removedVerts = Mathf.Max(0, _cachedFirstSegVertexCount);
            int shiftedIndex = ShiftedIndexAfterRoll(_cachedPrevVertexIndex, removedVerts, vp.NumPoints);

            // Reconstruct distance from cached fraction along the new [i, i+1]
            float newDist = DistanceFromIndexPercent(vp, shiftedIndex, _cachedPrevPercent);
            AdjustDistance(newDist);
        }

        // Compute a coherence-aware closest distance along the path by searching a local window of segments
        // around the previously stored vertex index. Penalize large temporal jumps and reward alignment with velocity.
        float GetCoherentDistanceAlongPath(Vector3 worldPos, float dt)
        {
            var vp = pathCreator.path;

            // Fallback if we lack context
            if (vp == null || vp.NumPoints < 2)
                return pathCreator.path.GetClosestDistanceAlongPath(worldPos);

            float maxLen = vp.cumulativeLengthAtEachVertex[vp.cumulativeLengthAtEachVertex.Length - 1];

            // Predict along-track distance advance from last frame
            float predictedAdvance = 0f;
            if (speedDeltaMultiplier > 0f && dt > 0f && velocityHint.sqrMagnitude > 1e-6f)
                predictedAdvance = speedDeltaMultiplier * velocityHint.magnitude * dt;

            // Hard cap if enabled
            if (hardMaxDeltaMetersPerFrame > 0f)
                predictedAdvance = Mathf.Min(predictedAdvance <= 0f ? hardMaxDeltaMetersPerFrame : predictedAdvance, hardMaxDeltaMetersPerFrame);

            // Base the prediction on the *current* corrected along-path distance
            float predictedDist = Mathf.Clamp(distanceTravelled + predictedAdvance, 0f, maxLen);
            float prevDist = distanceTravelled;

            // Build a distance window around the prediction
            float dMin = Mathf.Max(0f, predictedDist - searchHalfWindowMeters);
            float dMax = Mathf.Min(maxLen, predictedDist + searchHalfWindowMeters);

            // Convert window to vertex indices
            int iStart = VertexIndexAtOrBeforeDistance(vp, dMin);
            int iEnd = VertexIndexAtOrBeforeDistance(vp, dMax);
            if (iEnd <= iStart)
            {
                // Ensure at least one segment
                iEnd = Mathf.Min(vp.NumPoints - 1, iStart + 1);
            }

            // Iterate candidate segments within the window
            float bestScore = float.PositiveInfinity;
            float bestDist = distanceTravelled; // default to previous if nothing better is found

            Vector3 velDir = velocityHint.sqrMagnitude > 1e-6f ? velocityHint.normalized : Vector3.zero;

            for (int i = iStart; i < iEnd; i++)
            {
                Vector3 a = vp.GetPoint(i);
                Vector3 b = vp.GetPoint(i + 1);
                Vector3 ab = b - a;
                float segLen = ab.magnitude;
                if (segLen < 1e-6f)
                    continue;
                Vector3 abDir = ab / segLen;

                Vector3 right = Vector3.Cross(Vector3.up, abDir).normalized;

                // Closest point on this segment to worldPos
                float t = Vector3.Dot(worldPos - a, abDir) / segLen;
                t = Mathf.Clamp01(t);
                Vector3 p = a + abDir * (t * segLen);

                float candLat = Vector3.Dot(worldPos - p, right);

                if (enforceTrackWidthGate)
                {
                    float halfWidthWithMargin = HalfRoadWidth + lateralMarginMeters;
                    if (Mathf.Abs(candLat) > halfWidthWithMargin)
                        continue;
                }

                // Along-path distance of this candidate
                float distAtA = vp.cumulativeLengthAtEachVertex[i];
                float candDist = distAtA + t * segLen;

                // Base spatial fit (squared distance)
                float baseErr = (worldPos - p).sqrMagnitude;

                // Penalize deviation from predictedDist to enforce temporal coherence
                float cohPenalty = 0f;
                float jump = Mathf.Abs(candDist - predictedDist);
                if (jump > 0f)
                    cohPenalty = coherencePenaltyWeight * jump * jump;

                // Velocity alignment preference
                float alignPenalty = 0f;
                if (velocityAlignmentWeight > 0f && velDir != Vector3.zero)
                {
                    float align = Mathf.Max(0f, Vector3.Dot(velDir, abDir)); // [0,1]
                    alignPenalty = velocityAlignmentWeight * (1f - align);
                }

                float latPenalty = 0f;
                if (lateralPenaltyWeight > 0f)
                {
                    float latJump = Mathf.Abs(candLat - previousLateralOffset) - lateralJumpMetersAllowed;
                    if (latJump > 0f) latPenalty = lateralPenaltyWeight * latJump * latJump;
                }

                float score = baseErr + cohPenalty + alignPenalty + latPenalty;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestDist = candDist;
                }
            }

            if (coherenceJumpLogThresholdMeters > 0f)
            {
                float jumpFromPred = Mathf.Abs(bestDist - predictedDist);
                if (jumpFromPred > coherenceJumpLogThresholdMeters)
                {
#if UNITY_EDITOR
                    Debug.LogWarning(
                        $"[PathTracker] Coherence jump? prevDist={prevDist:F2}m, predictedAdvance={predictedAdvance:F2}m, predictedDist={predictedDist:F2}m, chosenDist={bestDist:F2}m, |Δ|={jumpFromPred:F2}m\n" +
                        $"window=[{dMin:F2},{dMax:F2}]m, searchHalfWindowMeters={searchHalfWindowMeters:F2}, speedDeltaMultiplier={speedDeltaMultiplier:F2}, hardMaxDelta={hardMaxDeltaMetersPerFrame:F2}, " +
                        $"cohW={coherencePenaltyWeight:F2}, latW={lateralPenaltyWeight:F2}, alignW={velocityAlignmentWeight:F2}, enforceTrackWidthGate={enforceTrackWidthGate}");
#endif
                }
            }
            return bestDist;
        }

        // Find the vertex index whose cumulative length is at or just before the given distance
        int VertexIndexAtOrBeforeDistance(VertexPath vp, float dist)
        {
            var arr = vp.cumulativeLengthAtEachVertex;
            int lo = 0, hi = arr.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (arr[mid] <= dist) lo = mid + 1; else hi = mid - 1;
            }
            return Mathf.Clamp(hi, 0, arr.Length - 1);
        }

        // --- Helpers for coherent roll handling ---
        // Update cache using the provided previous polyline segment index (i) and current distanceTravelled
        void UpdateCoherenceCacheWithIndex(int prevIndex)
        {
            var vp = pathCreator != null ? pathCreator.path : null;
            if (vp == null || vp.cumulativeLengthAtEachVertex == null || vp.cumulativeLengthAtEachVertex.Length < 2)
            {
                _cachedPrevVertexIndex = 0;
                _cachedPrevPercent = 0f;
                _cachedFirstSegVertexCount = 0;
                return;
            }

            _cachedPrevVertexIndex = Mathf.Clamp(prevIndex, 0, vp.NumPoints - 2);

            var cum = vp.cumulativeLengthAtEachVertex;
            int i0 = Mathf.Clamp(_cachedPrevVertexIndex, 0, cum.Length - 2);
            float aLen = cum[i0];
            float bLen = cum[i0 + 1];
            float denom = Mathf.Max(1e-6f, bLen - aLen);
            _cachedPrevPercent = Mathf.Clamp01((distanceTravelled - aLen) / denom);

            var anchorIdx = vp.localAnchorVertexIndex;
            _cachedFirstSegVertexCount = (anchorIdx != null && anchorIdx.Length > 1)
                ? Mathf.Max(0, anchorIdx[1] - anchorIdx[0])
                : 0;
        }

        // Reconstruct an along-path distance from polyline segment index and interpolation fraction
        float DistanceFromIndexPercent(VertexPath vp, int prevIndex, float t)
        {
            var cum = vp.cumulativeLengthAtEachVertex;
            int i0 = Mathf.Clamp(prevIndex, 0, cum.Length - 2);
            float aLen = cum[i0];
            float bLen = cum[i0 + 1];
            float denom = Mathf.Max(1e-6f, bLen - aLen);
            return aLen + Mathf.Clamp01(t) * denom;
        }

        // Compute shifted index after removing a given number of vertices from the front
        int ShiftedIndexAfterRoll(int prevIndex, int removedVertexCount, int numPoints)
        {
            int idx = Mathf.Max(0, prevIndex - Mathf.Max(0, removedVertexCount));
            return Mathf.Min(idx, Mathf.Max(0, numPoints - 2));
        }
    }
}