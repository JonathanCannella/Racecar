using UnityEngine;

namespace GameAI
{
    /// <summary>
    /// Global audio switch. Audio is muted when running headless (batchmode) or in simulated time
    /// (FPS_60_1X_SimTime), where sound is unwanted and only costs CPU. Set by the GameManager in Awake();
    /// sound components check Muted in Start() and disable themselves.
    /// </summary>
    public static class SimulationAudio
    {
        public static bool Muted { get; private set; }

        public static void SetMuted(bool muted)
        {
            if (muted && !Muted)
                Debug.Log("[SimulationAudio] Audio muted (headless or simulated time).");

            Muted = muted;
            AudioListener.pause = muted;
            AudioListener.volume = muted ? 0f : 1f;
        }

        public static bool ShouldMute(bool simulatedTime)
        {
            return Application.isBatchMode || simulatedTime;
        }

        /// <summary>
        /// For sound components: if muted, stop the source and disable the component so it does no per-frame work.
        /// Returns true if the component was disabled.
        /// </summary>
        public static bool DisableIfMuted(Behaviour component, AudioSource source)
        {
            if (!Muted)
                return false;

            if (source != null)
                source.Stop();

            component.enabled = false;
            return true;
        }
    }
}
