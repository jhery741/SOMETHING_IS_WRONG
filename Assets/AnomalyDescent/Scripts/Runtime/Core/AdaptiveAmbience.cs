using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>Escala el ambiente existente con la vuelta y la presencia. No genera música nueva.</summary>
    public class AdaptiveAmbience : MonoBehaviour
    {
        AudioSource source;
        public void Init(AudioSource ambience) { source = ambience; }
        void Update()
        {
            if (source == null) return;
            var director = GameDirector.Instance;
            float tension = director != null ? Mathf.Clamp01(((director.Lap - 1) +
                (director.Presence != null ? director.Presence.Stage / 4f : 0f)) / 4f) : 0f;
            float target = Mathf.Lerp(0.12f, 0.28f, tension) * GameSettings.AmbienceVolume;
            source.volume = Mathf.MoveTowards(source.volume, target, Time.unscaledDeltaTime * 0.08f);
            source.pitch = Mathf.Lerp(0.85f, 1.05f, tension);
        }
    }
}
