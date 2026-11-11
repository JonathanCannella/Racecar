using System.Collections.Generic;
using UnityEngine;


namespace GameAI
{

    /// <summary>
    /// Centralized tire-screech controller. One instance per vehicle.
    /// TrailEmitter reports edges and intensity per wheel; this class mixes and drives an AudioSource.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class TireScreecher : MonoBehaviour
    {
        [Header("Audio")]
        [SerializeField] private AudioSource source; // looped screech clip
        [SerializeField, Range(0f, 1f)] private float minVolume = 0f;
        [SerializeField, Range(0f, 1f)] private float maxVolume = 0.85f;
        [SerializeField] private float attackTime = 0.05f;   // seconds to rise
        [SerializeField] private float releaseTime = 0.15f;  // seconds to fall

        [Header("Vehicle Gating")]
        [SerializeField] private Rigidbody vehicleRB;                 // optional; auto-resolved if null
        [SerializeField, Tooltip("Below this chassis speed, suppress squeal unless wheels are clearly spinning up.")]
        private float minVehicleSpeedForAudio = 0.5f;                 // m/s
        [SerializeField, Tooltip("At low chassis speed, allow squeal only if any wheel exceeds this rpm.")]
        private float minWheelRpmForLowSpeedSqueal = 200f;            // rpm threshold for peel-out

        [Header("Slip Hysteresis (ratio)")]
        [SerializeField] private float slipStart = 0.20f;   // start when >= this
        [SerializeField] private float slipStop  = 0.15f;   // consider stopping when <= this
        [SerializeField] private float stopHoldTime   = 0.08f;  // must remain below stop for this many seconds
        [SerializeField] private float slipFull  = 1.00f;  // maps to intensity 1.0 before shaping

        [Header("Perceptual Mapping")]
        [SerializeField] private bool   useVolumeCurve = true;
        [SerializeField] private AnimationCurve volumeCurve = new AnimationCurve(
            new Keyframe(0f,   0f,   0f,   0.5f),
            new Keyframe(0.2f, 0.01f, 0f,  1f),
            new Keyframe(0.5f, 0.08f, 0.5f, 1.5f),
            new Keyframe(0.8f, 0.4f,  1f,  1f),
            new Keyframe(1f,   1f,    1f,  0f)
        );
        [SerializeField, Range(0.5f, 4f)] private float responseGamma = 1.8f; // used when useVolumeCurve=false

        [Header("Optional explicit wheels (faster)")]
        [SerializeField] private WheelCollider wheelFL;
        [SerializeField] private WheelCollider wheelFR;
        [SerializeField] private WheelCollider wheelRL;
        [SerializeField] private WheelCollider wheelRR;

        [Header("Debug")]
        [SerializeField] private float DB_smoothedVolume;
        [SerializeField] private float DB_maxIntensity;
        [SerializeField] private float DB_maxSlip;

        private bool prevAnyActive;
        private bool warnedNoClip;

        private struct WheelState
        {
            public bool sliding;
            public bool grounded;
            public float slipCombined;        // latest combined slip ratio (dimensionless)
            public float lastBelowStopTime; // realtimeSinceStartup when slip last seen <= slipStop
        }

        private readonly Dictionary<WheelCollider, int> wheelIndex = new Dictionary<WheelCollider, int>(4);
        private WheelState[] states;

        private float smoothedVolume; // current volume after attack/release smoothing

        void Awake()
        {
            if (!source) source = GetComponent<AudioSource>();
            if (source)
            {
                source.loop = true;
                source.playOnAwake = false;
                source.volume = 0f;
            }

            if (!vehicleRB) vehicleRB = GetComponentInParent<Rigidbody>();

            // Build state array from explicit wheels if provided, else grow on demand up to 8
            var list = new List<WheelCollider>(4);
            if (wheelFL) list.Add(wheelFL);
            if (wheelFR) list.Add(wheelFR);
            if (wheelRL) list.Add(wheelRL);
            if (wheelRR) list.Add(wheelRR);
            states = new WheelState[Mathf.Max(4, list.Count == 0 ? 4 : list.Count)];
            for (int i = 0; i < list.Count; i++) wheelIndex[list[i]] = i;
        }

        void Start()
        {
            SimulationAudio.DisableIfMuted(this, source);
        }

        void Update()
        {
            if (!source) return;
            if (!source.clip)
            {
                if (!warnedNoClip)
                {
                    warnedNoClip = true;
                    Debug.LogWarning("TireScreecher: AudioSource has no clip assigned.");
                }
                return;
            }

            float now = Time.realtimeSinceStartup;
            bool anyActive = false;
            float maxSlip = 0f;

            int count = states.Length;
            for (int i = 0; i < count; i++)
            {
                var ws = states[i];

                if (!ws.grounded)
                {
                    ws.sliding = false;
                    ws.lastBelowStopTime = 0f;
                }
                else
                {
                    if (!ws.sliding)
                    {
                        if (ws.slipCombined >= slipStart)
                        {
                            ws.sliding = true;
                            ws.lastBelowStopTime = 0f;
                        }
                    }
                    else
                    {
                        if (ws.slipCombined <= slipStop)
                        {
                            if (ws.lastBelowStopTime <= 0f) ws.lastBelowStopTime = now;
                        }
                        else
                        {
                            ws.lastBelowStopTime = 0f;
                        }

                        if (ws.lastBelowStopTime > 0f && (now - ws.lastBelowStopTime) >= stopHoldTime)
                        {
                            ws.sliding = false;
                        }
                    }

                    if (ws.sliding)
                    {
                        anyActive = true;
                        if (ws.slipCombined > maxSlip) maxSlip = ws.slipCombined;
                    }
                }

                states[i] = ws;
            }

            DB_maxSlip = maxSlip;

            // Low-speed suppression: if chassis is nearly stationary, only allow squeal when wheels are actually spinning up
            if (anyActive)
            {
                float chassisSpeed = vehicleRB ? vehicleRB.linearVelocity.magnitude : 0f;
                if (chassisSpeed < minVehicleSpeedForAudio)
                {
                    float maxRpm = GetMaxObservedWheelRpm();
                    if (maxRpm < minWheelRpmForLowSpeedSqueal)
                    {
                        anyActive = false; // suppress idle-contact scrubbing noise
                        maxSlip = 0f;
                    }
                }
            }

            float maxActiveIntensity = 0f;
            if (anyActive)
            {
                float norm = Mathf.InverseLerp(slipStop, Mathf.Max(slipFull, slipStop + 1e-3f), maxSlip);
                maxActiveIntensity = useVolumeCurve
                    ? Mathf.Clamp01(volumeCurve.Evaluate(norm))
                    : Mathf.Pow(Mathf.Clamp01(norm), responseGamma);
            }

            // Edge-based transport
            if (anyActive && !prevAnyActive)
            {
                source.Play();
            }
            prevAnyActive = anyActive;

            DB_maxIntensity = maxActiveIntensity;

            float targetVol = anyActive ? Mathf.Lerp(minVolume, maxVolume, maxActiveIntensity) : 0f;

            // Smooth towards target with separate attack/release times
            float dt = Time.deltaTime;
            float tc = targetVol > smoothedVolume ? Mathf.Max(1e-4f, attackTime) : Mathf.Max(1e-4f, releaseTime);
            float alpha = 1f - Mathf.Exp(-dt / tc);
            smoothedVolume = Mathf.Lerp(smoothedVolume, targetVol, alpha);

            source.volume = smoothedVolume;
            DB_smoothedVolume = smoothedVolume;

            const float eps = 0.001f;
            if (smoothedVolume > eps)
            {
                if (!source.isPlaying) source.Play();
            }
            else
            {
                if (source.isPlaying) source.Pause();
            }
        }

        public void ReportWheelSlide(WheelCollider wheel, float slipCombined, float slipLateral, float slipLongitudinal, bool grounded)
        {
            int idx = IndexFor(wheel);
            var ws = states[idx];
            ws.grounded = grounded;
            ws.slipCombined = Mathf.Max(0f, slipCombined);
            states[idx] = ws;
        }

        public void ReportTireSlide(bool isSliding, WheelCollider wheel, float intensity = 0f)
        {
            float approxSlip = Mathf.Lerp(slipStop, slipFull, Mathf.Clamp01(intensity));
            ReportWheelSlide(wheel, isSliding ? approxSlip : 0f, 0f, 0f, isSliding);
        }

        private int IndexFor(WheelCollider wheel)
        {
            if (wheel == null) return 0;
            if (wheelIndex.TryGetValue(wheel, out int i)) return i;
            // Grow mapping if dynamic wheels arrive and explicit slots were not set
            int next = wheelIndex.Count;
            if (next >= states.Length)
            {
                System.Array.Resize(ref states, states.Length + 4);
            }
            wheelIndex[wheel] = next;
            return next;
        }

        private float GetMaxObservedWheelRpm()
        {
            float maxRpm = 0f;
            foreach (var kv in wheelIndex)
            {
                var wc = kv.Key;
                if (!wc) continue;
                float rpm = Mathf.Abs(wc.rpm);
                if (rpm > maxRpm) maxRpm = rpm;
            }
            return maxRpm;
        }
    }

}
