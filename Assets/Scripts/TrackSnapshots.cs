using System;
using System.Text;
using UnityEngine;

using PathCreation.Examples;

namespace GameAI
{
    /// <summary>
    /// A moment in a race that can be returned to: the road (seed and number of generated segments; the road
    /// doesn't depend on how the car drove) and the car's pose and motion relative to the road there.
    /// Wheel spin and suspension state aren't stored (Unity's WheelCollider can't be set), so restore at
    /// "safe" moments (see TrackSnapshots.IsSafe): driving straight without sliding.
    /// </summary>
    [Serializable]
    public struct TrackSnapshot
    {
        public string seed;
        public int segmentsRolled;
        public Vector3 lastAnchor;         // road check: must match after replaying the generator

        public float distanceOnPath;       // along the current (partial) road
        public float totalDistance;        // since race start

        // Car relative to the road frame at its closest point (x right, y up, z along the road)
        public Vector3 offset;
        public Quaternion rotation;
        // Car frame
        public Vector3 localVelocity;
        public Vector3 localAngularVelocity;

        public float SpeedKph => localVelocity.magnitude * 3.6f;
    }

    /// <summary>
    /// Capture and restore TrackSnapshots, for training and recording tools (not for use by graded vehicles).
    /// </summary>
    public static class TrackSnapshots
    {
        // Thresholds for a moment that can be restored without the car reacting to lost wheel state. The pose
        // relative to the road is restored exactly, so position and heading only need to be sane (a racing line
        // uses the road's width); the dynamic limits are what matter.
        public const float MaxSidewaysSlip = 0.1f;
        public const float MaxLateralSpeed = 0.5f;          // m/s
        public const float MaxYawRate = 0.1f;               // rad/s
        public const float MaxHeadingErrorDeg = 20f;
        public const float MaxOffsetFractionOfHalfWidth = 0.8f;

        static GenerateDynamicPathExample Generator(AIVehicle vehicle)
        {
            var tracker = vehicle.INTERNAL_PathTracker;
            return tracker != null && tracker.pathCreator != null ? tracker.pathCreator.GetComponent<GenerateDynamicPathExample>() : null;
        }

        static Quaternion RoadFrame(Vector3 direction) => Quaternion.LookRotation(Vector3.ProjectOnPlane(direction, Vector3.up), Vector3.up);

        /// <summary>True if the car is driving straight and settled (all wheels grounded, no sliding, centered).</summary>
        public static bool IsSafe(AIVehicle vehicle, out string reason)
        {
            var sb = new StringBuilder();
            var tracker = vehicle.INTERNAL_PathTracker;
            var rb = vehicle.INTERNAL_Rigidbody;
            if (tracker == null || rb == null || Generator(vehicle) == null)
            {
                reason = "no road";
                return false;
            }

            for (int i = 0; i < 4; i++)
            {
                if (!vehicle.TryGetWheelTelemetry(i, out var w) || !w.grounded) sb.Append($"wheel {i} not grounded; ");
                else if (Mathf.Abs(w.sidewaysSlip) > MaxSidewaysSlip) sb.Append($"wheel {i} sliding ({w.sidewaysSlip:0.00}); ");
            }

            var car = vehicle.transform;
            var localVel = car.InverseTransformDirection(rb.linearVelocity);
            if (Mathf.Abs(localVel.x) > MaxLateralSpeed) sb.Append($"lateral speed {localVel.x:0.00}; ");
            if (Mathf.Abs(rb.angularVelocity.y) > MaxYawRate) sb.Append($"yaw rate {rb.angularVelocity.y:0.00}; ");

            var heading = Vector3.SignedAngle(tracker.closestPointDirectionOnPath, car.forward, Vector3.up);
            if (Mathf.Abs(heading) > MaxHeadingErrorDeg) sb.Append($"heading {heading:0.0}; ");

            var offset = Quaternion.Inverse(RoadFrame(tracker.closestPointDirectionOnPath)) * (car.position - tracker.closestPointOnPath);
            if (Mathf.Abs(offset.x) > MaxOffsetFractionOfHalfWidth * tracker.HalfRoadWidth) sb.Append($"offset {offset.x:0.00}; ");

            reason = sb.ToString();
            return reason.Length == 0;
        }

        public static TrackSnapshot Capture(AIVehicle vehicle)
        {
            var tracker = vehicle.INTERNAL_PathTracker;
            var rb = vehicle.INTERNAL_Rigidbody;
            var generator = Generator(vehicle);
            var frame = RoadFrame(tracker.closestPointDirectionOnPath);
            var inverse = Quaternion.Inverse(frame);
            var car = vehicle.transform;

            return new TrackSnapshot
            {
                seed = generator.Seed,
                segmentsRolled = generator.SegmentsRolled,
                lastAnchor = generator.LastAnchor,
                distanceOnPath = tracker.distanceTravelled,
                totalDistance = tracker.totalDistanceTravelled,
                offset = inverse * (car.position - tracker.closestPointOnPath),
                rotation = inverse * car.rotation,
                localVelocity = car.InverseTransformDirection(rb.linearVelocity),
                localAngularVelocity = car.InverseTransformDirection(rb.angularVelocity),
            };
        }

        /// <summary>
        /// Put the road and the car back to the snapshot. Call between physics steps (e.g. from FixedUpdate or
        /// an agent's OnEpisodeBegin). The road's seed must match. Returns false (and changes nothing) otherwise.
        /// </summary>
        public static bool Restore(AIVehicle vehicle, in TrackSnapshot snapshot)
        {
            var tracker = vehicle.INTERNAL_PathTracker;
            var rb = vehicle.INTERNAL_Rigidbody;
            var generator = Generator(vehicle);
            if (generator == null || generator.Seed != snapshot.seed)
            {
                Debug.LogError($"[TrackSnapshots] Can't restore: road seed {(generator != null ? generator.Seed : "none")} != snapshot seed {snapshot.seed}");
                return false;
            }

            if (generator.SegmentsRolled != snapshot.segmentsRolled)
                generator.RestoreSegmentsRolled(snapshot.segmentsRolled);
            if ((generator.LastAnchor - snapshot.lastAnchor).sqrMagnitude > 1e-6f)
                Debug.LogWarning($"[TrackSnapshots] Road differs after restore: last anchor {generator.LastAnchor} vs {snapshot.lastAnchor}");

            tracker.ResetToDistance(Mathf.Clamp(snapshot.distanceOnPath, 0f, tracker.MaxPathDistance));
            tracker.totalDistanceTravelled = snapshot.totalDistance;

            var frame = RoadFrame(tracker.closestPointDirectionOnPath);
            var position = tracker.closestPointOnPath + frame * snapshot.offset;
            var rotation = frame * snapshot.rotation;

            vehicle.transform.SetPositionAndRotation(position, rotation);
            rb.position = position;
            rb.rotation = rotation;
            rb.linearVelocity = rotation * snapshot.localVelocity;
            rb.angularVelocity = rotation * snapshot.localAngularVelocity;

            tracker.previousPosition = position;
            tracker.SetVelocityHint(rb.linearVelocity);
            vehicle.INTERNAL_OnTeleported();
            return true;
        }

        /// <summary>
        /// Test hook: apply full brake regardless of the controller's throttle (steering is unaffected), e.g. to
        /// check whether a situation is survivable with maximum braking.
        /// </summary>
        public static void ForceFullBrake(AIVehicle vehicle, bool on)
        {
            vehicle.INTERNAL_ControlNoise.y = on ? -2f : 0f;   // applied throttle is clamped to [-1, 1]
        }
    }
}
