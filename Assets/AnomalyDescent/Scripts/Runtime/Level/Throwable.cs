using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>Botella lanzada: al romperse crea un señuelo sonoro de 11 m.</summary>
    public class Throwable : MonoBehaviour
    {
        bool broken;
        float born;

        void Start() { born = Time.time; }

        void Update()
        {
            if (Time.time - born > 8f) Destroy(gameObject);
        }

        void OnCollisionEnter(Collision c)
        {
            if (broken) return;
            if (c.relativeVelocity.magnitude < 1.5f) return;
            broken = true;
            Vector3 p = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            SoundBank.PlayAt("glass", p, 1f, 30f, Random.Range(0.9f, 1.1f), "[cristal roto]");
            NoiseSystem.Emit(p, 11f, false, null, true);
            Destroy(gameObject);
        }
    }
}
