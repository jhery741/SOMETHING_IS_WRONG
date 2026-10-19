using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>
    /// Herramienta de evidencia: [Q] toma una foto de la habitación actual (con flash, hace ruido).
    /// Mantén [Tab] para ver la última foto de la habitación donde estás y comparar.
    /// </summary>
    public class EvidenceCamera : MonoBehaviour
    {
        class Photo { public Texture2D tex; public float time; }

        public static bool HideHudForCapture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { HideHudForCapture = false; }

        readonly Dictionary<Room, Photo> photos = new Dictionary<Room, Photo>();
        PlayerController player;
        Light flash;
        float cooldown;

        public bool Viewing { get; private set; }
        public int PhotoCount { get { return photos.Count; } }

        public void Setup(PlayerController p)
        {
            player = p;
            var go = new GameObject("Flash");
            go.transform.SetParent(p.Cam.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.05f, -0.15f);
            flash = go.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.range = 10f;
            flash.intensity = 1.3f;   // con HDR + bloom, 3.5 quemaba la foto a blanco
            flash.color = new Color(0.9f, 0.95f, 1f);
            flash.enabled = false;
        }

        void Update()
        {
            Viewing = false;
            var d = GameDirector.Instance;
            if (d == null || !d.IsPlaying || player == null || player.IsDead) return;
            if (cooldown > 0f) cooldown -= Time.deltaTime;
            bool reportOpen = player.Interaction != null && player.Interaction.ReportOpen;
            Viewing = GameInput.Held(GKey.Tab) && !reportOpen;
            if (GameInput.Down(GKey.Q) && !player.IsHidden && !reportOpen)
            {
                if (cooldown > 0f) HUD.Show("La cámara se está recargando...", 1.2f);
                else StartCoroutine(Capture());
            }
        }

        public bool HasPhoto(Room r) { return r != null && photos.ContainsKey(r); }

        public Texture2D PhotoOf(Room r, out float age)
        {
            age = 0f;
            Photo p;
            if (r == null || !photos.TryGetValue(r, out p)) return null;
            age = Time.time - p.time;
            return p.tex;
        }

        IEnumerator Capture()
        {
            cooldown = 2.5f;
            var room = GameDirector.Instance.PlayerRoom;
            HideHudForCapture = true;
            int oldMask = player.Cam.cullingMask;
            player.Cam.cullingMask = oldMask | (1 << Layers.PhotoOnly);   // el dispositivo revela lo que el ojo no ve
            flash.enabled = true;
            SoundBank.Play2D("shutter", 0.7f);
            NoiseSystem.Emit(transform.position, 7f, true);
            yield return new WaitForEndOfFrame();
            Texture2D tex = null;
            try { tex = ScreenCapture.CaptureScreenshotAsTexture(); }
            catch (System.Exception e) { Debug.LogWarning("[EvidenceCamera] " + e.Message); }
            HideHudForCapture = false;
            player.Cam.cullingMask = oldMask;
            yield return new WaitForSeconds(0.06f);
            flash.enabled = false;
            if (tex == null || room == null) yield break;
            Photo old;
            if (photos.TryGetValue(room, out old) && old.tex != null) Destroy(old.tex);
            photos[room] = new Photo { tex = tex, time = Time.time };
            HUD.Show("Foto guardada: " + room.displayName + ". Mantén [Tab] aquí para compararla.", 3.5f);
        }

        void OnDestroy()
        {
            foreach (var p in photos.Values) if (p.tex != null) Destroy(p.tex);
            HideHudForCapture = false;
        }
    }
}
