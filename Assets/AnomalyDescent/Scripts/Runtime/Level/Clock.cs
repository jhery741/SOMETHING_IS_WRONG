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

        void Start()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 0.5f;
            src.maxDistance = 7f;
            src.volume = 0.35f;
            src.playOnAwake = false;
            src.dopplerLevel = 0f;
        }

        static float Mod(float a, float m) { float r = a % m; return r < 0f ? r + m : r; }

        void Update()
        {
            clockSeconds += Time.deltaTime * (reversed ? -1f : 1f);
            int s = Mathf.FloorToInt(clockSeconds);
            if (s != lastTick)
            {
                lastTick = s;
                var clip = SoundBank.Get("tick");
                if (src != null && clip != null && !(reversed && Mod(s, 4) == 0)) src.PlayOneShot(clip);
            }
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
