//#define PATH_DEBUG

using System.Collections.Generic;

using UnityEngine;

using System.Linq;

namespace PathCreation.Examples {
    // Example of creating a path at runtime from a set of points.

    [RequireComponent(typeof(PathCreator))]
    public class GenerateDynamicPathExample : MonoBehaviour {

        bool closedLoop = false;
        public PathTracker pathFollower;
        PathCreator pathCreator;

        [SerializeField]
        private bool Mode2D = true;

        [SerializeField]
        private float RandAbsAngle = 30f;

        [SerializeField]
        private string seed = "424242";

        // Random seed for the generated road. Set before Start() (e.g. right after instantiating) to take effect.
        public string Seed { get => seed; set => seed = value; }

        [SerializeField]
        private float AngleMaxRandomAccel = 10f;

        [SerializeField]
        private float AngleAbsMaxVel = 15f;

        [SerializeField]
        private float AngleVel = 0f;

        [SerializeField]
        private float TargetAngle = 0f;

        [SerializeField]
        private float AngleAbsMax = 20f;

        [SerializeField]
        private float AngleVelDecay = 0.8f;

        [SerializeField]
        private float AngleDecay = 0.6f;

        [SerializeField]
        private float SegMinLen = 2f;

        [SerializeField]
        private float SegMaxLen = 15f;

        [Header("Segment budget")]
        [SerializeField, Min(1)]
        private int totalSegments = 12; // number of cubic Bézier segments to maintain

        [Header("Segment maintenance")]
        [SerializeField, Min(0)]
        private int trailingSegments = 3; // how many segments to keep behind the follower before we delete from the front

        private float prevFollowerDist = 0f;
        private float[] segLensInternal;

        private void Awake()
        {

            pathCreator = GetComponent<PathCreator>();
            if(pathCreator == null)
            {
                Debug.LogError("pathCreator is null");
            }

            if(pathFollower == null)
            {
                Debug.LogError("path follower is null");
            }
        }


        Random.State currState;

#if PATH_DEBUG
        public Vector3[] DB_initPts;
        public float DB_initLen;
        public int DB_initSegs;
#endif

        void Start() {

            initAngleVel = AngleVel;
            initTargetAngle = TargetAngle;

            Random.InitState(ParsedSeed());

            pathCreator.bezierPath = CreateInitialPath();

#if PATH_DEBUG
            DB_initPts = null; // initial anchors are built in CreateInitialPath()
            DB_initLen = pathCreator.path.length;
            DB_initSegs = pathCreator.bezierPath.NumSegments;
#endif

            currState = Random.state;
        }

        int ParsedSeed()
        {
            int iseed;

            if(!int.TryParse(seed, out iseed))
            {
                iseed = seed.GetHashCode();
            }
            return iseed;
        }

        // The starting road: a straight run of totalSegments segments. Uses no random numbers.
        BezierPath CreateInitialPath()
        {
            //Vector3 v2 = Mode2D ?
            //    Quaternion.AngleAxis(Random.Range(-180f, 180f), Vector3.up) * Vector3.right :
            //    Random.onUnitSphere * Random.Range(5f, 15f);

            // totalSegments + 1 anchors needed for totalSegments segments
            Vector3[] pts = new Vector3[totalSegments + 1];

            var fval = 60f; //was 10 (car was -48.24)
            var size = 10f;
            for (int i = 0; i < pts.Length; ++i)
            {
                pts[i] = new Vector3(0f, 0f, fval - i * size);
            }

            BezierPath bezierPath = new BezierPath(pts, closedLoop, Mode2D ? PathSpace.xz : PathSpace.xyz);
            bezierPath.ControlPointMode = BezierPath.ControlMode.Aligned;
            return bezierPath;
        }

        // Snapshot support. The road depends only on the seed and how many segments have been rolled (not on how
        // or when the car drove), so a road state is (seed, SegmentsRolled).
        float initAngleVel, initTargetAngle;

        /// <summary>Number of segments generated since the race started.</summary>
        public int SegmentsRolled { get; private set; }

        /// <summary>Last anchor of the current road; identifies the road state when checking a restore.</summary>
        public Vector3 LastAnchor
        {
            get
            {
                var bp = pathCreator.bezierPath;
                return bp.GetPoint(bp.NumPoints - 1);
            }
        }

        /// <summary>
        /// Rebuild the road as it was after `segmentsRolled` segments, by replaying the generator from the seed on a
        /// detached path and then assigning it (one path update, so one mesh rebuild and one tracker update).
        /// Callers must re-place any followers (the tracker's distance is meaningless after this).
        /// </summary>
        public void RestoreSegmentsRolled(int segmentsRolled)
        {
            var callerState = Random.state;

            Random.InitState(ParsedSeed());
            AngleVel = initAngleVel;
            TargetAngle = initTargetAngle;

            var bp = CreateInitialPath();
            for (int i = 0; i < segmentsRolled; ++i)
                bp.DeleteSegmentFromBeginningAndAddToEnd(GenerateRandomAnchor(bp));

            currState = Random.state;
            SegmentsRolled = segmentsRolled;
            Random.state = callerState;

            pathCreator.bezierPath = bp;
        }

        // Need for random rotations. Uses plane eqn to find perpendicular vector
        // if v is (approximately) zero, returns zero
        Vector3 AnyPerpUnitV(Vector3 v)
        {
            Vector3 ret = Vector3.zero;

            if (!Mathf.Approximately(0f, v.x))
            {
                ret = new Vector3((-v.y - v.z) / v.x, 1f, 1f);
            }
            else if (!Mathf.Approximately(0f, v.y))
            {
                ret = new Vector3(1f, (-v.x - v.z) / v.y, 1f);
            }
            else if (!Mathf.Approximately(0f, v.z))
            {
                ret = new Vector3(1f, 1f, (-v.x - v.y) / v.z);
            }

            return ret.normalized;
        }




        float RBinom(float rangeMagnitude)
        {
            // Not really a binomial distribution but something similar with central tendency
            // around 0
            return Random.Range(0f, rangeMagnitude) - Random.Range(0f, rangeMagnitude);
        }




        float VelSmoothedRandAngle()
        {
            var aaccel = RBinom(1f) * AngleMaxRandomAccel;
            AngleVel = Mathf.Clamp( AngleVelDecay * AngleVel + aaccel, -AngleAbsMaxVel, AngleAbsMaxVel);
            TargetAngle = Mathf.Clamp(AngleDecay * TargetAngle + AngleVel, -AngleAbsMax, AngleAbsMax);

            return TargetAngle;
        }

        Vector3 GenerateRandomAnchor(BezierPath bp)
        {

            var lasti = bp.NumSegments - 1;
            var lastSeg = bp.GetPointsInSegment(lasti);

            var lastAnchor = lastSeg[3];
            var nextToLastAnchor = lastSeg[0];
            var lastControl = lastSeg[2];

            // we assume that path is currently headed in direction of last tangent
            // But we will rotate the new control point from that direction by some max rand angle

            var contDir = (lastAnchor - lastControl).normalized;

            // any perpendicular v will do
            Vector3 perpDir = Mode2D ? Vector3.up : AnyPerpUnitV(contDir);

            var angleRange = RandAbsAngle;

            //var angRot = Quaternion.AngleAxis(Random.Range(-angleRange, angleRange), perpDir);
            //var angRot = Quaternion.AngleAxis(RBinom(angleRange), perpDir);

            var ang = VelSmoothedRandAngle();

            var angRot = Quaternion.AngleAxis(ang, perpDir);

            var segLenHalfRange = (SegMaxLen - SegMinLen)*0.5f;

            var segLen = SegMinLen + segLenHalfRange + RBinom(segLenHalfRange);

            //Debug.Log($"segLen is: {segLen}");

            var newAnchorDir = angRot * (contDir * segLen);

            Vector3 newAnchor = Vector3.zero;

            if (Mode2D)
            {
                newAnchor = lastAnchor + newAnchorDir;

                newAnchor.y = 0f;
            }
            else
            {
                //angRot = Quaternion.AngleAxis(Random.Range(-180f, 180f), contDir);
                angRot = Quaternion.AngleAxis(RBinom( 180f), contDir);

                newAnchor = lastAnchor + angRot * newAnchorDir;
            }

            return newAnchor;

        }


        void RandomizeLastControl()
        {
            var bp = pathCreator.bezierPath;

            var lasti = bp.NumSegments - 1;
            var lastSeg = bp.GetPointsInSegment(lasti);

            var lastAnchor = lastSeg[3];
            var firstAnchor = lastSeg[0];

            var anchorDist = Vector3.Distance(firstAnchor, lastAnchor);
            var halfAnchorDist = 0.5f * anchorDist;

            var nextToLastAnchor = lastSeg[0];
            var lastControl = lastSeg[2];
            var firstControl = lastSeg[1];

            // We don't want first tangent to be too long and cause extreme path
            var firstTangentVec = firstControl - firstAnchor;
            var firstTangLen = firstTangentVec.magnitude;

            if(firstTangLen > halfAnchorDist)
            {
                firstControl = firstAnchor + (firstTangentVec / firstTangLen) * halfAnchorDist;
            }

            var contRel =  firstControl - lastAnchor;
            var contDist = contRel.magnitude;
            var contDir = contRel / contDist;

            // any perpendicular v will do
            Vector3 perpDir = Mode2D ? Vector3.up : AnyPerpUnitV(contDir);

            //var angleRange = RandAbsAngle;

            //var angRot = Quaternion.AngleAxis(Random.Range(-angleRange, angleRange), perpDir);
            var ang = VelSmoothedRandAngle();


            var angRot = Quaternion.AngleAxis(ang, perpDir);


            var newControlDir = angRot * (contDir * Random.Range(1f, contDist));

            Vector3 newControl = Vector3.zero;

            if (Mode2D)
            {
                newControl = lastAnchor + newControlDir;

                newControl.y = 0f;
            }
            else
            {
                //angRot = Quaternion.AngleAxis(Random.Range(-180f, 180f), contDir);
                angRot = Quaternion.AngleAxis(RBinom( 180f), contDir);

                newControl = lastAnchor + angRot * newControlDir;
            }

            // this is the possibly revised control for first tangent (maybe shortened)
            bp.SetPoint(1, firstControl, true);
            bp.SetPoint(2, newControl);

        }


        void AddRandomSegmentToPath()
        {

            var bp = pathCreator.bezierPath;

            var newAnchor = GenerateRandomAnchor(bp);

            //bp.AddSegmentToEnd(newAnchor);

            bp.AddSegmentToEnd(newAnchor);


            //RandomizeLastControl();

        }


        void RemoveFirstAndAddRandomSegmentToPath()
        {

            var bp = pathCreator.bezierPath;

            var newAnchor = GenerateRandomAnchor(bp);

            //bp.AddSegmentToEnd(newAnchor);

            bp.DeleteSegmentFromBeginningAndAddToEnd(newAnchor);

            ++SegmentsRolled;

        }


        public int DB_updateCount = 0;
        public float DB_followerPos = 0f;      
        public float DB_length = 0f;
        public int DB_currSeg = 0;
        public int DB_numSegs = 0;
        public float[] DB_items;
        public float[] DB_segLens;

        // Runs at the physics rate (script execution order -60, before PathTracker) so the path
        // the vehicles see is updated in the same step they act on it.
        private void FixedUpdate()
        {
            Random.state = currState;

            ++DB_updateCount;

            prevFollowerDist = pathFollower.distanceTravelled;

            DB_followerPos = pathFollower.distanceTravelled;
            

            var bp = pathCreator.bezierPath;

            var path = pathCreator.path;

            DB_length = pathCreator.path.length;

            var currSeg = pathFollower.currentBezierSegmentIndex;

            DB_currSeg = currSeg;

            DB_numSegs = bp.NumSegments;

            // // Ensure the path has exactly totalSegments segments
            // while (bp.NumSegments < totalSegments)
            // {
            //     AddRandomSegmentToPath();
            // }
            // while (bp.NumSegments > totalSegments)
            // {
            //     // delete from front
            //     bp.DeleteSegment(0);
            // }

            var items = path.cumulativeLengthAtEachVertex.Where((item, index) => path.localAnchorVertexIndex.Contains(index));

            DB_items = items.ToArray();

            items = items.Zip(items.Skip(1), (x, y) => y - x);

            var arrItems = items.ToArray();

            segLensInternal = arrItems;
            DB_segLens = arrItems;

            // Single-step maintenance: remove at most ONE segment per frame to preserve PathTracker invariants.
            // If the follower has advanced beyond the trailing window, delete-from-front and add-to-end once.
            if (currSeg > trailingSegments)
            {
                //Debug.Log($"Del-Add seg b/c currSeg is: {currSeg}");
                // del first seg
                //bp.DeleteSegment(0);

                // replace del seg with new random one at the end
                RemoveFirstAndAddRandomSegmentToPath();

                // Re-query follower position after path modification (informational)
                currSeg = pathFollower.currentBezierSegmentIndex;
#if UNITY_EDITOR
                if (currSeg > trailingSegments + 1)
                {
                    var segLensStr = (segLensInternal != null) ? string.Join(",", segLensInternal.Select(x => x.ToString("F2")).ToArray()) : "null";
                    float prevDist = prevFollowerDist;
                    float currDist = pathFollower.distanceTravelled;
                    float removedSegLen = (segLensInternal != null && segLensInternal.Length > 0) ? segLensInternal[segLensInternal.Length - 1] : -1f;
                    Debug.LogWarning($"Follower remains beyond trailing window after single maintenance step. currSeg={currSeg}, trailingSegments={trailingSegments}, prevDist={prevDist:F2}, currDist={currDist:F2}, segLens=[{segLensStr}], removedSegLen={removedSegLen:F2}");
                }
#endif
            }

            currState = Random.state;

        }

    }
}