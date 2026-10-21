using UnityEngine;

namespace SomethingIsWrong
{
    public class Clock : MonoBehaviour
    {
        public Transform hourHand, minuteHand, secondHand;
        public bool reversed;
        float clockSeconds = 3 * 3600 + 33 * 60 + 10;
        int lastTick = int.MinValue;
        AudioSource src;
        bool looped;   // grabación larga de tictac (Freesound 675680): se reproduce en bucle, no un disparo por segundo
        const float BaseVolume = 0.35f;

        void Start()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 0.5f;
            src.maxDistance = 7f;
            src.volume = BaseVolume;
            src.playOnAwake = false;
            src.dopplerLevel = 0f;
            var clip = SoundBank.Get("tick");
            if (clip != null && clip.length > 1.2f)
            {
                looped = true;
                src.clip = clip;
                src.loop = true;
                src.time = Random.Range(0f, clip.length * 0.9f);
                src.Play();
            }
        }

        static float Mod(float a, float m) { float r = a % m; return r < 0f ? r + m : r; }

        void Update()
        {
            clockSeconds += Time.deltaTime * (reversed ? -1f : 1f);
            int s = Mathf.FloorToInt(clockSeconds);
            if (looped)
            {
                // Anomalía: al ir hacia atrás el tictac suena más grave y se salta un golpe cada 4 s.
                src.pitch = reversed ? 0.86f : 1f;
                bool skip = reversed && Mod(s, 4) == 0;
                src.volume = (skip ? 0f : BaseVolume) * GameSettings.AmbienceVolume;
            }
            else if (s != lastTick)
            {
                lastTick = s;
                var clip = SoundBank.Get("tick");
                src.volume = BaseVolume * GameSettings.AmbienceVolume;
                if (src != null && clip != null && !(reversed && Mod(s, 4) == 0)) src.PlayOneShot(clip);
            }
            lastTick = s;
            float sec = Mod(s, 60f);
            float min = Mod(clockSeconds / 60f, 60f);
            float hr = Mod(clockSeconds / 3600f, 12f);
            // Ángulo negativo = sentido horario visto de frente
            if (secondHand != null) secondHand.localRotation = Quaternion.Euler(0f, 0f, -sec * 6f);
            if (minuteHand != null) minuteHand.localRotation = Quaternion.Euler(0f, 0f, -min * 6f);
            if (hourHand != null) hourHand.localRotation = Quaternion.Euler(0f, 0f, -hr * 30f);
        }
    }
}
