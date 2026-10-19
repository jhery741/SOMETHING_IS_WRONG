using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>Sonido ambiental normal de la casa (p. ej. un grifo que gotea). Es un falso positivo plausible.</summary>
    public class AmbientLoop : MonoBehaviour
    {
        public string soundId = "drip_loop";
        public float volume = 0.5f;
        public float range = 6f;
        public string caption;

        void Start()
        {
            var src = SoundBank.AddLoop(gameObject, soundId, volume, range, true);
            if (src != null && src.clip != null)
            {
                src.time = Random.Range(0f, src.clip.length * 0.9f);
                src.Play();
            }
        }

        void Update()
        {
            if (!string.IsNullOrEmpty(caption)) HUD.Caption(caption, transform.position, range);
        }
    }
}
