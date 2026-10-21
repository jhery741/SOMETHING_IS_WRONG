using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>Linterna con batería limitada. [F] para encender/apagar.</summary>
    public class Flashlight : MonoBehaviour
    {
        public Light Spot { get; private set; }
        public float battery = 100f;
        public float drainPerSecond = 0.2f;   // ~8 min encendida; la radio del refugio da pilas nuevas en cada vuelta
        public float maxIntensity = 2.2f;
        [Tooltip("Retraso del haz respecto a la mirada (más alto = sigue antes a la cámara).")]
        public float followSharpness = 14f;
        [Tooltip("Balanceo del haz al caminar (grados).")]
        public float swayDegrees = 0.9f;
        public bool On { get; private set; }
        PlayerController player;
        Transform cam;
        Quaternion lagged;
        float bobT, flickerUntil, flickerSeed;

        public void Setup(Camera camera)
        {
            player = GetComponent<PlayerController>();
            cam = camera.transform;
            var go = new GameObject("Linterna");
            go.transform.SetParent(cam, false);
            // Ligeramente detrás y abajo a la derecha (como sostenida en la mano). Detrás del plano cercano para que
            // la luz cubra toda la pantalla.
            go.transform.localPosition = new Vector3(0.12f, -0.1f, -0.2f);
            Spot = go.AddComponent<Light>();
            Spot.type = LightType.Spot;
            Spot.range = 18f;
            Spot.spotAngle = 58f;
            Spot.intensity = maxIntensity;
            Spot.color = new Color(1f, 0.94f, 0.84f);   // halógeno cálido
            Spot.renderMode = LightRenderMode.ForcePixel;
            // Sombras: la linterna es la que dibuja la escena (como en CAPTURED). Suaves, de alta resolución y sin
            // «peter-panning» (bias bajo): cada mueble proyecta una sombra larga que se mueve con la mano.
            Spot.shadows = LightShadows.Soft;
            Spot.shadowResolution = UnityEngine.Rendering.LightShadowResolution.VeryHigh;
            Spot.shadowStrength = 1f;
            Spot.shadowBias = 0.02f;
            Spot.shadowNormalBias = 0.25f;
            Spot.shadowNearPlane = 0.1f;
            // Haz con reflector: punto caliente, cuerpo y aro tenue (textura importada como Cookie en Resources).
            var cookie = Resources.Load<Texture>("Linterna_Cookie");
            if (cookie != null) Spot.cookie = cookie;
            Spot.enabled = false;
            lagged = cam.rotation;
            flickerSeed = Random.value * 100f;
        }

        void LateUpdate()
        {
            if (Spot == null || cam == null) return;
            float dt = Time.deltaTime;
            // El haz sigue a la mirada con un ligero retraso y se balancea al caminar
            lagged = Quaternion.Slerp(lagged, cam.rotation, 1f - Mathf.Exp(-followSharpness * dt));
            float speed = player != null ? player.CurrentSpeed : 0f;
            bobT += dt * Mathf.Lerp(1.2f, 9f, Mathf.Clamp01(speed / 4f));
            float amp = swayDegrees * Mathf.Clamp01(speed / 2.5f) + 0.15f;
            var sway = Quaternion.Euler(Mathf.Sin(bobT * 2f) * amp * 0.6f, Mathf.Sin(bobT) * amp, 0f);
            Spot.transform.rotation = lagged * sway;
        }

        void Update()
        {
            var d = GameDirector.Instance;
            if (d == null || !d.IsPlaying || player == null || player.IsDead || Spot == null) return;
            if (GameInput.Down(GKey.F)) Toggle();
            if (!On) return;
            battery -= drainPerSecond * Time.deltaTime;
            if (battery <= 0f)
            {
                battery = 0f;
                SetOn(false);
                HUD.Show("La linterna se ha quedado sin batería.", 3f);
                return;
            }
            float k = 1f;
            if (battery < 15f) k = Mathf.PerlinNoise(Time.time * 9f, 0.3f) > 0.35f ? 1f : 0.15f;
            // Interferencia cuando el monstruo está cerca y cazando: parpadeos irregulares
            var m = d.Monster;
            if (m != null && m.IsActive && m.IsHunting)
            {
                float dist = Vector3.Distance(m.transform.position, transform.position);
                if (dist < 7f && Time.time > flickerUntil && Random.value < Time.deltaTime * Mathf.Lerp(3f, 0.4f, dist / 7f))
                    flickerUntil = Time.time + Random.Range(0.05f, 0.25f);
            }
            if (Time.time < flickerUntil) k *= Mathf.PerlinNoise(Time.time * 40f, flickerSeed) > 0.5f ? 0.2f : 0.7f;
            Spot.intensity = maxIntensity * k * Mathf.Lerp(0.55f, 1f, battery / 100f);
        }

        public void Toggle()
        {
            if (!On && battery <= 0f)
            {
                SoundBank.Play2D("click", 0.4f);
                HUD.Show("Sin batería. Busca pilas.", 2.5f);
                return;
            }
            SetOn(!On);
        }

        public void SetOn(bool v)
        {
            On = v;
            if (Spot != null) Spot.enabled = v;
            SoundBank.Play2D("click", 0.5f);
            NoiseSystem.Emit(transform.position, 1f, true);
        }

        public void AddCharge(float v) { battery = Mathf.Min(100f, battery + v); }
    }
}
