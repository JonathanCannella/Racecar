using UnityEngine;
using System.Collections.Generic;

public class EmoteSoundManager : MonoBehaviour
{
    public enum EmoteSoundType
    {
        Scream,
        Mph88,
        YeeHaw,
    }

    [SerializeField] private AudioSource audioSource;
    [SerializeField] private List<AudioClip> screamClips;
    [SerializeField] private AudioClip Mph88Clip;
    [SerializeField] private AudioClip YeeHawClip;

    public void Play(EmoteSoundType effect)
    {
        if (GameAI.SimulationAudio.Muted)
            return;

        switch (effect)
        {
            case EmoteSoundType.Scream:
                if (screamClips != null && screamClips.Count > 0 && audioSource != null && !audioSource.isPlaying)
                {
                    AudioClip clip = screamClips[Random.Range(0, screamClips.Count)];
                    audioSource.PlayOneShot(clip);
                }
                break;
            case EmoteSoundType.Mph88:
                if (Mph88Clip != null && audioSource != null && !audioSource.isPlaying)
                {
                    audioSource.PlayOneShot(Mph88Clip);
                }
                break;

            case EmoteSoundType.YeeHaw:
                if (YeeHawClip != null && audioSource != null && !audioSource.isPlaying)
                {
                    audioSource.PlayOneShot(YeeHawClip);
                }
                break;
        }
    }
}
