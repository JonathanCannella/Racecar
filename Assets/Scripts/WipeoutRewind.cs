using UnityEngine;

using Unity.MLAgents;

namespace GameAI
{
    /// <summary>
    /// Optional training aid (advanced; off unless rewind_fraction &gt; 0): after some wipeouts, put the car back
    /// where it was a little before the mistake, at the speed it had then, instead of at rest at the crash site.
    /// Hard spots (e.g. a tight turn after a straight) then get practiced many more times.
    ///
    /// Put it on the car (or the NN agent object) next to your NNVehicle. Settings come from the trainer config's
    /// environment_parameters, or the defaults below:
    ///   rewind_fraction       share of wipeouts that rewind (0 = off)
    ///   rewind_min_meters     go back at least this far before where the car left the road
    ///   rewind_max_failures   wiping out again near the same place goes further back each time; after this
    ///                         many, the car is left reset at rest
    /// It returns to the latest safe moment (driving straight, not sliding) recorded by a SafeSnapshotRecorder,
    /// which it adds if there isn't one.
    ///
    /// Use it with continuing episodes (wipeout_ends_episode: 0). Rewinding sets the race distance back, so a
    /// progress reward computed from changes in Racetrack.TotalDistanceTravelled (see the NN guide) automatically
    /// takes back the reward for the distance lost: a rewound wipeout costs about what a reset at rest does.
    /// Training only: it does nothing when no trainer is connected (e.g. in graded races).
    /// </summary>
    [DefaultExecutionOrder(-65)] // FixedUpdate before the road generator (-60): apply a pending rewind
    public class WipeoutRewind : MonoBehaviour
    {
        [Header("Defaults (trainer environment_parameters override)")]
        [Range(0f, 1f)] public float rewindFraction = 0f;
        public float rewindMinMeters = 60f;
        public int rewindMaxFailures = 3;
        [Tooltip("Only rewind while a trainer is connected (off: also in Play mode / tests)")]
        public bool onlyWhenTraining = true;

        [Header("Stats (read only)")]
        public int Wipeouts;
        public int Rewinds;

        AIVehicle vehicle;
        SafeSnapshotRecorder recorder;
        readonly System.Random random = new System.Random();
        bool pending;
        SafeSnapshotRecorder.Entry target;
        float lastRewindDistance = float.NegativeInfinity;
        int failures;

        void Start()
        {
            vehicle = GetComponentInParent<AIVehicle>();
            if (vehicle == null)
            {
                Debug.LogError("WipeoutRewind needs an AIVehicle on this object or a parent.");
                enabled = false;
                return;
            }

            recorder = vehicle.GetComponentInChildren<SafeSnapshotRecorder>();
            if (recorder == null)
            {
                recorder = vehicle.gameObject.AddComponent<SafeSnapshotRecorder>();
                recorder.keepSeconds = 60f;
            }
            vehicle.WipedOut += OnWipedOut;
        }

        void OnDestroy()
        {
            if (vehicle != null)
                vehicle.WipedOut -= OnWipedOut;
        }

        bool Training => Academy.IsInitialized && Academy.Instance.IsCommunicatorOn;

        float Parameter(string key, float fallback) =>
            Academy.IsInitialized ? Academy.Instance.EnvironmentParameters.GetWithDefault(key, fallback) : fallback;

        void OnWipedOut()
        {
            ++Wipeouts;
            if (onlyWhenTraining && !Training) return;

            var fraction = Parameter("rewind_fraction", rewindFraction);
            if (fraction <= 0f || random.NextDouble() >= fraction) return;

            var minMeters = Parameter("rewind_min_meters", rewindMinMeters);
            var maxFailures = Mathf.RoundToInt(Parameter("rewind_max_failures", rewindMaxFailures));
            var leftRoadAt = vehicle.Racetrack.TotalDistanceTravelled;   // tracking stops once the car drops off the road

            // Failing again near the same place: go further back, and eventually give up (stay reset at rest)
            failures = leftRoadAt - lastRewindDistance < 4f * minMeters ? failures + 1 : 0;
            if (failures >= maxFailures)
            {
                failures = 0;
                lastRewindDistance = float.NegativeInfinity;
                return;
            }

            if (!recorder.TryGetAtOrBeforeDistance(leftRoadAt - minMeters * (1 + failures), out target))
                return;

            lastRewindDistance = target.snapshot.totalDistance;
            pending = true;   // applied next physics step (the wipeout reset has a pending rigidbody move)
        }

        void FixedUpdate()
        {
            if (!pending) return;
            pending = false;
            if (!TrackSnapshots.Restore(vehicle, target.snapshot)) return;
            recorder.DiscardAfter(target.time);
            ++Rewinds;
            if (Academy.IsInitialized)
                Academy.Instance.StatsRecorder.Add("Training/Rewinds", 1f, StatAggregationMethod.Sum);   // shown in TensorBoard
        }
    }
}
