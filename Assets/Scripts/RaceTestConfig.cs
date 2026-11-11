using UnityEngine;

[CreateAssetMenu(menuName = "RaceTrack/Race Test Config", fileName = "RaceTestConfig")]
public sealed class RaceTestConfig : ScriptableObject
{
    [Header("Prefab & Duration")]
    public string trackPrefabName;
    public float duration_s;

    [Header("Speed Targets")]
    public float minAllowedSpeed;
    public float targetSpeed;
    public float extraCreditSpeed;

    [Header("Wipeouts")]
    public int maxAllowedWipeouts;
    public int maxPartialPenaltyWipeouts;

    [Header("Weights")]
    public float speedScoreWeight;
    public float wipeoutScoreWeight;
    public float extraCreditWeight;
    public float gradeWeight;
}