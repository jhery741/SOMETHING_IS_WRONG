using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>Lámpara que se balancea: es un "engaño" (cambio natural, no anomalía).</summary>
    public class SwingingLamp : MonoBehaviour
    {
        public float amplitude = 6f, speed = 1.1f;
        float phase;
        Quaternion baseRot;

        void Start()
        {
            baseRot = transform.localRotation;
            phase = Random.value * 6f;
        }

        void Update()
        {
            float t = Time.time * speed + phase;
            transform.localRotation = baseRot * Quaternion.Euler(Mathf.Sin(t) * amplitude, 0f, Mathf.Sin(t * 0.73f) * amplitude * 0.5f);
        }
    }
}
