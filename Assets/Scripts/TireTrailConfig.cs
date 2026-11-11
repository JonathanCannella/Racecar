using UnityEngine;

/// <summary>
/// Central configuration for tire trail emission. Create an asset and assign it to cars.
/// Later, TrailEmitter will consume this to override its local settings.
/// </summary>
[CreateAssetMenu(fileName = "TireTrailConfig", menuName = "RaceTrack/Tire Trail Config", order = 0)]
public class TireTrailConfig : ScriptableObject
{
    [Header("Rendering")]
    [Tooltip("Trail line width in meters.")]
    public float width = 0.18f;

    [Tooltip("Seconds for a trail to fully fade out after emission stops.")]
    public float decayTime = 3.0f;

    [Tooltip("Material used by the trail renderer.")]
    public Material material;

    [Tooltip("Random roughness factor for slight width jitter (0 disables). Integer 0–10.")]
    [Range(0, 10)]
    public int roughness = 0;

    [Tooltip("Enable soft ending for trail source when stopping emission (on/off toggle).")]
    public bool softSourceEnd = true;

    [Header("Behavior")]
    [Tooltip("If true, use combined slip (max of |sideways| and |forward|) for trails. If false, use axis-specific thresholds.")]
    public bool useCombinedSlipForTrails = true;

    [Header("Trail thresholds — slip ratio (PhysX)")]
    [Tooltip("Start laying rubber when max(|sidewaysSlip|, |forwardSlip|) >= this.")]
    [Range(0f, 2f)] public float trailCombinedStart = 0.20f;

    [Tooltip("Stop laying rubber when max(|sidewaysSlip|, |forwardSlip|) <= this.")]
    [Range(0f, 2f)] public float trailCombinedStop  = 0.15f;

    [Header("Trail thresholds — slip ratio (axis-specific, optional)")]
    [Tooltip("Start when |sidewaysSlip| >= this." )]
    [Range(0f, 2f)] public float trailSideStart = 0.4f;

    [Tooltip("Stop when |sidewaysSlip| <= this." )]
    [Range(0f, 2f)] public float trailSideStop  = 0.2f;

    [Tooltip("Start when |forwardSlip| >= this." )]
    [Range(0f, 2f)] public float trailFwdStart  = 0.98f;

    [Tooltip("Stop when |forwardSlip| <= this." )]
    [Range(0f, 2f)] public float trailFwdStop   = 0.98f;

    [Header("Advanced")]
    [SerializeField, HideInInspector] private int _configVersion = 2;

#if UNITY_EDITOR
    private void OnValidate()
    {
        width = Mathf.Max(0.001f, width);
        decayTime = Mathf.Max(0f, decayTime);
        roughness = Mathf.Clamp(roughness, 0, 10);
        trailCombinedStart = Mathf.Clamp(trailCombinedStart, 0f, 2f);
        trailCombinedStop  = Mathf.Clamp(trailCombinedStop,  0f, trailCombinedStart);
        trailSideStart = Mathf.Clamp(trailSideStart, 0f, 2f);
        trailSideStop  = Mathf.Clamp(trailSideStop,  0f, trailSideStart);
        trailFwdStart  = Mathf.Clamp(trailFwdStart,  0f, 2f);
        trailFwdStop   = Mathf.Clamp(trailFwdStop,   0f, trailFwdStart);
    }
#endif
}