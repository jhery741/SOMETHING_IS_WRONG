using UnityEngine;

namespace SomethingIsWrong
{
    public class RoomLight : MonoBehaviour
    {
        public Light source;
        [Tooltip("0 = usa la intensidad de la luz de la escena.")] public float baseIntensity = 0f;
        Color baseColor = Color.white;
        float flickerUntil;
        bool alert;
        float seed;

        void Awake()
        {
            if (source == null) source = GetComponent<Light>();
            if (source != null)
            {
                if (baseIntensity <= 0f) baseIntensity = source.intensity;
                baseColor = source.color;
            }
            seed = Random.value * 100f;
        }

        public void Flicker(float seconds) { flickerUntil = Mathf.Max(flickerUntil, Time.time + seconds); }
        public void SetAlert(bool on) { alert = on; }

        void Update()
        {
            if (source == null) return;
            float k = 1f;
            if (Time.time < flickerUntil)
            {
                float n = Mathf.PerlinNoise(Time.time * 14f, seed);
                k = n > 0.42f ? Mathf.Lerp(0.5f, 1f, n) : 0.06f;
            }
            if (alert) k *= 0.45f;
            source.intensity = baseIntensity * k;
            source.color = alert ? new Color(1f, 0.22f, 0.15f) : baseColor;
        }
    }
}
