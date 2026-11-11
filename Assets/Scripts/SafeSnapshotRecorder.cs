using System.Collections.Generic;
using UnityEngine;

namespace GameAI
{
    /// <summary>
    /// Keeps recent TrackSnapshots taken at safe moments (TrackSnapshots.IsSafe), at most one per interval, for
    /// returning to a point before trouble (e.g. a few seconds before a wipeout). Put it on the car.
    /// For training and recording tools (not for use by graded vehicles).
    /// </summary>
    [DefaultExecutionOrder(0)] // after VehicleStepper (-40): sees this step's state
    public class SafeSnapshotRecorder : MonoBehaviour
    {
        public float intervalSeconds = 0.5f;
        public float keepSeconds = 30f;
        public float minSpeedKph = 20f;

        public struct Entry
        {
            public float time;
            public TrackSnapshot snapshot;
        }

        readonly List<Entry> entries = new List<Entry>();
        AIVehicle vehicle;
        float lastTime = float.NegativeInfinity;

        public IReadOnlyList<Entry> Entries => entries;

        void Start() => vehicle = GetComponentInParent<AIVehicle>();

        void FixedUpdate()
        {
            if (vehicle == null || !vehicle.IsControlStep || vehicle.Racetrack == null) return;

            var now = Time.time;
            while (entries.Count > 0 && now - entries[0].time > keepSeconds)
                entries.RemoveAt(0);

            if (now - lastTime < intervalSeconds) return;
            if (vehicle.Velocity.magnitude * 3.6f < minSpeedKph) return;
            if (!TrackSnapshots.IsSafe(vehicle, out _)) return;

            entries.Add(new Entry { time = now, snapshot = TrackSnapshots.Capture(vehicle) });
            lastTime = now;
        }

        /// <summary>The latest safe snapshot taken at least `secondsBefore` before now.</summary>
        public bool TryGetBefore(float secondsBefore, out Entry entry)
        {
            var limit = Time.time - secondsBefore;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].time <= limit)
                {
                    entry = entries[i];
                    return true;
                }
            }
            entry = default;
            return false;
        }

        /// <summary>The latest safe snapshot at or before race distance `totalDistance` (m).</summary>
        public bool TryGetAtOrBeforeDistance(float totalDistance, out Entry entry)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].snapshot.totalDistance <= totalDistance)
                {
                    entry = entries[i];
                    return true;
                }
            }
            entry = default;
            return false;
        }

        /// <summary>Forget snapshots taken after `time` (after restoring the one taken then).</summary>
        public void DiscardAfter(float time)
        {
            entries.RemoveAll(e => e.time > time);
            lastTime = time;
        }

        /// <summary>Forget all snapshots (e.g. after restoring one, since later ones are now in the future).</summary>
        public void Clear()
        {
            entries.Clear();
            lastTime = float.NegativeInfinity;
        }
    }
}
