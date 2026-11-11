using UnityEngine;

namespace GameAI
{
    /// <summary>
    /// Samples every vehicle's wheel telemetry at the start of the physics step, before the road generator (-60)
    /// can replace the road collider. Read after a replacement, each wheel's last contact refers to the old
    /// collider and reports "not grounded" (zero slip and load) for that step, although the physics is unaffected.
    /// Added by VehicleStepper.
    /// </summary>
    [DefaultExecutionOrder(-70)]
    public sealed class WheelTelemetrySampler : MonoBehaviour
    {
        void FixedUpdate() => VehicleStepper.SampleWheelTelemetry();
    }
}
