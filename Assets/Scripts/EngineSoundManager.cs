/*
 * This code is part of Arcade Car Physics for Unity by Saarg (2018)
 * 
 * This is distributed under the MIT Licence (see LICENSE.md for details)
 */
using GameAI;
using UnityEngine;

namespace GameAI {
    [RequireComponent(typeof(AIVehicle))]
    [RequireComponent(typeof(AudioSource))]

    public class EngineSoundManager : MonoBehaviour {

        [Header("AudioClips")]
        public AudioClip starting;
        public AudioClip rolling;
        public AudioClip stopping;

        [Header("pitch parameter")]

        [Range(0.0f, 3.0f)]
        public float minPitch = 0.7f;

        public float maxPitch = 2f;

        [Range(0.0f, 0.1f)]
        public float pitchSpeed = 0.05f;

        [Range(0.0f, 1.0f)]
        public float minVolume = 0.3f;

        [Range(0.0f, 1.0f)]
        public float maxVolume = 1.0f;



        [Header("speed normalization")]
        public float topSpeedKPH = 220f;

        [Header("smoothing")]
        public float pitchSmoothTime = 0.08f;         // seconds, SmoothDamp
        public float volumeSmoothTime = 0.12f;        // seconds, SmoothDamp

        [Header("volume shaping")]
        [Range(0.1f, 4.0f)] public float volumeResponseExponent = 1.4f;
        [Range(0f, 1f)] public float volumeMinAtIdle = 0.25f; // floor when idling

        [Header("debug toggles")]
        public bool DB_enableLogs = false;
        public bool DB_onScreenHUD = false;

        [Header("debug vars")]
        [SerializeField] float DB_speedKPH;
        [SerializeField] float DB_normSpeed;
        [SerializeField] float DB_targetPitch_dbg;
        [SerializeField] float DB_targetVol_dbg;

        [SerializeField]
        private AudioSource source;
        private AIVehicle _vehicle;

        private float _pitchVel;
        private float _volumeVel;

        [SerializeField]
        AudioClip DB_currentClip;

        [SerializeField]
        float DB_pitch;

        [SerializeField]
        float DB_normThrottle;

        void Start()
        {
            if (SimulationAudio.DisableIfMuted(this, source))
                return;

            //_source = GetComponent<AudioSource>();
            _vehicle = GetComponent<AIVehicle>();

            source.clip = rolling;
            source.Play();
            DB_currentClip = rolling;
            source.pitch = minPitch;
            source.volume = minVolume;
        }

        void Update()
        {
            var throttle = Mathf.Clamp01(_vehicle.Throttle);
            DB_speedKPH = Mathf.Max(0f, _vehicle.Speed_kph);

            float normSpeed = Mathf.InverseLerp(0f, Mathf.Max(1f, topSpeedKPH), DB_speedKPH);
            DB_normSpeed = normSpeed;

            float targetPitch = Mathf.Lerp(minPitch, maxPitch, normSpeed);
            float smoothedPitch = Mathf.SmoothDamp(source.pitch, targetPitch, ref _pitchVel, pitchSmoothTime);
            source.pitch = Mathf.Clamp(smoothedPitch, minPitch, maxPitch);

            float perceptual = Mathf.Pow(Mathf.Max(throttle, normSpeed), volumeResponseExponent);
            float targetVol = Mathf.Max(Mathf.Lerp(minVolume, maxVolume, perceptual), Mathf.Lerp(minVolume, maxVolume, volumeMinAtIdle));
            float smoothedVol = Mathf.SmoothDamp(source.volume, targetVol, ref _volumeVel, volumeSmoothTime);
            source.volume = Mathf.Clamp(smoothedVol, minVolume, maxVolume);

            DB_currentClip = source.clip;
            DB_pitch = source.pitch;
            DB_targetPitch_dbg = targetPitch;
            DB_targetVol_dbg = targetVol;
        }

        void OnGUI()
        {
            if (!DB_onScreenHUD) return;
            var r = new Rect(10, 10, 420, 140);
            GUI.Box(r, "EngineSound Debug");
            GUILayout.BeginArea(new Rect(20, 35, 400, 120));
            GUILayout.Label($"speed: {DB_speedKPH:F1} kph  norm: {DB_normSpeed:F2}");
            GUILayout.Label($"pitch: {DB_pitch:F2} -> tgt {DB_targetPitch_dbg:F2}");
            GUILayout.Label($"vol: {source.volume:F2} -> tgt {DB_targetVol_dbg:F2}");
            GUILayout.EndArea();
        }
    }
}
