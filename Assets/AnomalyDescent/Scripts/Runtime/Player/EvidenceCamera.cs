using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SomethingIsWrong
{
    public enum PhotoQuality { Nitida, Lejos, Tapada, Desencuadrada, SinObjetivo }
    public enum CarreteState { Cerrado, Fotos, Habitacion, Categoria }

    /// <summary>
    /// Cámara de evidencia del investigador.
    ///
    /// CONTROLES
    ///   Clic derecho (mantener) o [Q] (alternar) · levantar la cámara (visor).
    ///   Rueda del ratón (o Z/X) · zoom (60° → 18°).        Clic izquierdo o [Espacio] · disparar (flash: hace ruido).
    ///   [Tab] · carrete: [A]/[D] cambiar de foto · [Enter] reportar la foto · [1-9] habitación · [1-5] categoría · [Esc] atrás.
    ///
    /// VALIDACIÓN (sin visión artificial: geometría de la escena)
    ///   De todos los objetos reportables visibles se elige el sujeto principal (más centrado, más grande, menos tapado) y se
    ///   comprueba, por este orden:
    ///   · ENCUADRE: su centro está dentro del 60 % central del visor; si está en el borde → "Desencuadrada".
    ///   · DISTANCIA: ocupa al menos el 8 % de la altura del visor y está a menos de 15 m; si no → "Lejos" (usa el zoom).
    ///   · OCLUSIÓN: de 5 rayos a su volumen, al menos 3 llegan sin chocar con otra cosa; si no → "Tapada".
    ///   Solo una foto "Nítida" se puede reportar. La foto guarda qué anomalías tenía el objeto en el momento del disparo:
    ///   reportar una foto de un objeto normal es un falso positivo.
    /// </summary>
    public class EvidenceCamera : MonoBehaviour
    {
        public class Photo
        {
            public Texture2D tex;
            public float time;
            public int lap;
            public Room roomTaken;
            public Reportable subject;
            public string subjectName;
            public Room subjectRoom;
            public PhotoQuality quality;
            public readonly List<Anomaly> anomaliesAtCapture = new List<Anomaly>();
            public bool reported;
        }

        public struct Analysis
        {
            public Reportable subject;
            public PhotoQuality quality;
            public float distance;
        }

        public static bool HideHudForCapture;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { HideHudForCapture = false; }

        public const float BaseFov = 60f, MinFov = 18f;
        const float CenterMargin = 0.2f;       // centro del sujeto dentro de [0.2, 0.8] del visor
        const float MinScreenFraction = 0.08f; // altura angular mínima respecto al visor
        const float MaxDistance = 15f;
        const int Capacity = 24;

        readonly List<Photo> photos = new List<Photo>();
        PlayerController player;
        Light flash;
        float cooldown, normalFov, targetFov = BaseFov, nextAnalysis;
        bool toggledUp;
        int lastCloseFrame = -10;

        public bool Raised { get; private set; }
        public float Fov { get { return targetFov; } }
        public float Zoom { get { return BaseFov / targetFov; } }
        public Analysis Live { get; private set; }
        public CarreteState State { get; private set; }
        public int Index { get; private set; }
        public Room ChosenRoom { get; private set; }
        public bool Transmitting { get; private set; }
        public IList<Photo> Photos { get { return photos; } }
        public Photo Current { get { return Index >= 0 && Index < photos.Count ? photos[Index] : null; } }
        public bool UIOpen { get { return State != CarreteState.Cerrado; } }
        public bool ClosedThisFrame { get { return lastCloseFrame == Time.frameCount; } }
        public readonly List<Room> ReportRooms = new List<Room>();

        public void Setup(PlayerController p)
        {
            player = p;
            normalFov = p.Cam.fieldOfView;
            var go = new GameObject("Flash");
            go.transform.SetParent(p.Cam.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.05f, -0.15f);
            flash = go.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.range = 10f;
            flash.intensity = 1.3f;
            flash.color = new Color(0.9f, 0.95f, 1f);
            flash.enabled = false;
        }

        void Update()
        {
            var d = GameDirector.Instance;
            bool playing = d != null && d.IsPlaying && player != null && !player.IsDead;
            if (!playing)
            {
                if (UIOpen && (d == null || !d.IsRunning)) CloseCarrete();
                ApplyFov(false);
                return;
            }
            if (cooldown > 0f) cooldown -= Time.deltaTime;

            if (ReportRooms.Count == 0 && d.Level != null)
                foreach (var r in d.Level.rooms) if (r != null && !r.isSafe) ReportRooms.Add(r);

            if (UIOpen) { HandleCarrete(d); ApplyFov(false); return; }

            bool busy = player.IsHidden;
            if (GameInput.Down(GKey.Tab) && !busy) { OpenCarrete(); return; }

            if (GameInput.Down(GKey.Q) && !busy) toggledUp = !toggledUp;
            bool want = !busy && (toggledUp || Input.GetMouseButton(1));
            if (busy) toggledUp = false;
            if (want != Raised)
            {
                Raised = want;
                SoundBank.Play2D("click", 0.15f, want ? 1.3f : 0.9f);
                if (!want) targetFov = BaseFov;
            }
            if (Raised)
            {
                float wheel = Input.mouseScrollDelta.y + (Input.GetKey(KeyCode.X) ? 6f : 0f) * Time.deltaTime - (Input.GetKey(KeyCode.Z) ? 6f : 0f) * Time.deltaTime;
                if (Mathf.Abs(wheel) > 0.01f) targetFov = Mathf.Clamp(targetFov - wheel * 4f, MinFov, BaseFov);
                if (Time.unscaledTime >= nextAnalysis) { nextAnalysis = Time.unscaledTime + 0.15f; Live = Analyze(); }
                if (GameInput.MouseDown(0) || GameInput.Down(GKey.Space))
                {
                    if (cooldown > 0f) HUD.Show("La cámara se está recargando...", 1f);
                    else StartCoroutine(Capture(d));
                }
            }
            ApplyFov(Raised);
        }

        void ApplyFov(bool raised)
        {
            if (player == null || player.Cam == null) return;
            float goal = raised ? targetFov : GameSettings.FieldOfView;
            player.Cam.fieldOfView = Mathf.MoveTowards(player.Cam.fieldOfView, goal, 160f * Time.unscaledDeltaTime);
            player.lookScale = raised ? Mathf.Clamp(targetFov / BaseFov, 0.3f, 1f) : 1f;
            player.moveScale = raised ? 0.6f : 1f;
        }

        // ------------------------------------------------------------------ validación
        static readonly RaycastHit[] hits = new RaycastHit[16];

        public Analysis Analyze()
        {
            var res = new Analysis { quality = PhotoQuality.SinObjetivo };
            var cam = player.Cam;
            float best = 0f;
            var list = Object.FindObjectsByType<Reportable>();
            foreach (var r in list)
            {
                Bounds b;
                if (!BoundsOf(r, out b)) continue;
                Vector3 c = b.center;
                Vector3 vp = cam.WorldToViewportPoint(c);
                if (vp.z <= 0.1f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) continue;
                float dist = vp.z;
                float radius = b.extents.magnitude * 0.75f;
                float frac = (2f * Mathf.Atan(radius / Mathf.Max(dist, 0.05f)) * Mathf.Rad2Deg) / cam.fieldOfView;
                float vis = VisibleFraction(cam, r, b, dist);
                if (vis < 0.2f) continue;   // no se ve: ni siquiera cuenta como sujeto
                float off = Mathf.Max(Mathf.Abs(vp.x - 0.5f), Mathf.Abs(vp.y - 0.5f));
                // Prima lo que está en el centro del visor (lo que el jugador apunta) por encima del tamaño.
                float center = Mathf.Clamp01(1f - off * 2.2f);
                float score = vis * center * center * Mathf.Sqrt(Mathf.Clamp(frac, 0.02f, 1f)) / (1f + dist * 0.05f);
                if (score <= best) continue;
                best = score;
                res.subject = r;
                res.distance = dist;
                if (off > 0.5f - CenterMargin) res.quality = PhotoQuality.Desencuadrada;
                else if (frac < MinScreenFraction || dist > MaxDistance) res.quality = PhotoQuality.Lejos;
                else if (vis < 0.6f) res.quality = PhotoQuality.Tapada;
                else res.quality = PhotoQuality.Nitida;
            }
            return res;
        }

        static bool BoundsOf(Reportable r, out Bounds b)
        {
            b = new Bounds();
            bool any = false;
            foreach (var c in r.GetComponentsInChildren<Collider>())
            {
                if (!c.enabled || !c.gameObject.activeInHierarchy) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            if (any) return true;
            foreach (var rd in r.GetComponentsInChildren<Renderer>())
            {
                if (!rd.enabled) continue;
                if (!any) { b = rd.bounds; any = true; } else b.Encapsulate(rd.bounds);
            }
            return any;
        }

        float VisibleFraction(Camera cam, Reportable r, Bounds b, float dist)
        {
            Vector3 from = cam.transform.position;
            Vector3 right = cam.transform.right, up = cam.transform.up;
            float ex = Mathf.Min(b.extents.x + b.extents.z, b.extents.magnitude) * 0.35f, ey = b.extents.y * 0.35f;
            int ok = 0;
            for (int i = 0; i < 5; i++)
            {
                Vector3 p = b.center;
                if (i == 1) p += right * ex; else if (i == 2) p -= right * ex; else if (i == 3) p += up * ey; else if (i == 4) p -= up * ey;
                Vector3 dir = p - from;
                float d = dir.magnitude;
                if (d < 0.05f) { ok++; continue; }
                int n = Physics.RaycastNonAlloc(from, dir / d, hits, d + 0.05f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
                float nearest = float.MaxValue; Transform nt = null;
                for (int k = 0; k < n; k++)
                {
                    var h = hits[k];
                    if (h.collider == null || h.collider.transform.IsChildOf(player.transform)) continue;
                    if (h.collider.isTrigger && h.collider.GetComponentInParent<Reportable>() != r) continue;   // otros triggers no tapan
                    if (h.distance < nearest) { nearest = h.distance; nt = h.collider.transform; }
                }
                if (nt == null || nt.IsChildOf(r.transform) || nearest >= d - 0.08f) ok++;
            }
            return ok / 5f;
        }

        public static string QualityText(PhotoQuality q)
        {
            switch (q)
            {
                case PhotoQuality.Nitida: return "NÍTIDA";
                case PhotoQuality.Lejos: return "LEJOS — acércate o usa el zoom";
                case PhotoQuality.Tapada: return "TAPADA — algo se interpone";
                case PhotoQuality.Desencuadrada: return "DESENCUADRADA — céntralo";
            }
            return "SIN OBJETIVO";
        }

        // ------------------------------------------------------------------ disparo
        IEnumerator Capture(GameDirector d)
        {
            cooldown = 1.2f;
            var a = Analyze();
            var photo = new Photo
            {
                time = Time.time,
                lap = d.Lap,
                roomTaken = d.PlayerRoom,
                subject = a.subject,
                subjectName = a.subject != null ? a.subject.displayName.Trim() : "",
                subjectRoom = a.subject != null && d.Level != null ? (d.Level.RoomAt(a.subject.transform.position) ?? d.Level.NearestRoom(a.subject.transform.position)) : null,
                quality = a.quality
            };
            if (a.subject != null && d.Anomalies != null) d.Anomalies.ActiveAnomaliesOn(a.subject, photo.anomaliesAtCapture);

            HideHudForCapture = true;
            int oldMask = player.Cam.cullingMask;
            player.Cam.cullingMask = oldMask | (1 << Layers.PhotoOnly);   // el dispositivo revela lo que el ojo no ve
            flash.enabled = true;
            SoundBank.Play2D("shutter", 0.7f);
            NoiseSystem.Emit(transform.position, 5f, true);
            yield return new WaitForEndOfFrame();
            Texture2D shot = null;
            try { shot = ScreenCapture.CaptureScreenshotAsTexture(); }
            catch (System.Exception e) { Debug.LogWarning("[Cámara] " + e.Message); }
            HideHudForCapture = false;
            player.Cam.cullingMask = oldMask;
            yield return new WaitForSeconds(0.06f);
            flash.enabled = false;
            if (shot == null) yield break;
            photo.tex = Downscale(shot, 640);
            Destroy(shot);
            if (photos.Count >= Capacity) { if (photos[0].tex != null) Destroy(photos[0].tex); photos.RemoveAt(0); }
            photos.Add(photo);
            string what = photo.quality == PhotoQuality.SinObjetivo ? "foto de " + (photo.roomTaken != null ? photo.roomTaken.displayName : "la casa")
                                                                     : photo.subjectName;
            HUD.Show("Foto " + photos.Count + ": " + what + " · " + QualityText(photo.quality) + "   ([Tab] carrete)", 3f,
                     photo.quality == PhotoQuality.Nitida ? new Color(0.75f, 1f, 0.8f) : new Color(1f, 0.85f, 0.6f));
            if (Application.isEditor || Debug.isDebugBuild)
                Debug.Log("[Cámara] Foto " + photos.Count + " · " + photo.quality + " · sujeto " + (photo.subjectName == "" ? "—" : photo.subjectName) +
                          " · dist " + a.distance.ToString("0.0") + " m · zoom x" + Zoom.ToString("0.0") + " · anomalías en el sujeto: " + photo.anomaliesAtCapture.Count);
            if (d.Presence != null) d.Presence.OnPlayerPhoto();
        }

        static Texture2D Downscale(Texture2D src, int width)
        {
            int w = Mathf.Min(width, src.width), h = Mathf.Max(1, Mathf.RoundToInt(src.height * (w / (float)src.width)));
            var rt = RenderTexture.GetTemporary(w, h);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return t;
        }

        // ------------------------------------------------------------------ carrete y reporte
        void OpenCarrete()
        {
            Raised = false;
            toggledUp = false;
            State = CarreteState.Fotos;
            Index = photos.Count - 1;   // se abre en la foto más reciente
            player.inputLocked = true;
            SoundBank.Play2D("click", 0.2f, 1.1f);
        }

        void CloseCarrete()
        {
            State = CarreteState.Cerrado;
            if (player != null && !player.IsDead) player.inputLocked = false;
            lastCloseFrame = Time.frameCount;
        }

        void HandleCarrete(GameDirector d)
        {
            bool back = GameInput.Down(GKey.Escape) || Input.GetKeyDown(KeyCode.Backspace);
            switch (State)
            {
                case CarreteState.Fotos:
                    if (back || GameInput.Down(GKey.Tab)) { CloseCarrete(); return; }
                    if (photos.Count == 0) return;
                    if (GameInput.Down(GKey.A) || GameInput.Down(GKey.Left)) Index = (Index - 1 + photos.Count) % photos.Count;
                    if (GameInput.Down(GKey.D) || GameInput.Down(GKey.Right)) Index = (Index + 1) % photos.Count;
                    if (GameInput.Down(GKey.Enter) || GameInput.Down(GKey.E))
                    {
                        var p = Current;
                        if (p == null) return;
                        if (Transmitting) HUD.Show("Espera: la radio sigue transmitiendo.", 2f);
                        else if (d.Phase == GamePhase.Observation) HUD.Show("Aún estás observando: la casa todavía no ha cambiado.", 2.5f);
                        else if (p.reported) HUD.Show("Esa foto ya está reportada.", 2f);
                        else if (p.lap != d.Lap) HUD.Show("Es una foto de una vuelta anterior: sirve para comparar, no para reportar.", 3f);
                        else if (p.quality != PhotoQuality.Nitida) HUD.Show("Solo se pueden reportar fotos NÍTIDAS. Esta está: " + QualityText(p.quality), 3f);
                        else { State = CarreteState.Habitacion; ChosenRoom = null; }
                    }
                    break;
                case CarreteState.Habitacion:
                    if (back) { State = CarreteState.Fotos; return; }
                    for (int i = 0; i < ReportRooms.Count && i < 9; i++)
                        if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) { ChosenRoom = ReportRooms[i]; State = CarreteState.Categoria; return; }
                    break;
                case CarreteState.Categoria:
                    if (back) { State = CarreteState.Habitacion; return; }
                    for (int i = 0; i < 5; i++)
                        if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                        {
                            var p = Current;
                            var room = ChosenRoom;
                            CloseCarrete();
                            StartCoroutine(Transmit(p, room, (AnomalyCategory)i));
                            return;
                        }
                    break;
            }
        }

        IEnumerator Transmit(Photo p, Room room, AnomalyCategory cat)
        {
            Transmitting = true;
            HUD.Show("Transmitiendo por radio: " + p.subjectName + " · " + room.displayName + " · " + AnomalyText.Name(cat) + "...", 2.2f);
            SoundBank.PlayAt("static", player.transform.position + Vector3.up * 1.3f, 0.5f, 12f);
            NoiseSystem.Emit(player.transform.position, 9f, true);
            yield return new WaitForSeconds(2f);
            Transmitting = false;
            var d = GameDirector.Instance;
            if (d == null || !d.IsRunning || player.IsDead) yield break;
            if (d.SubmitPhotoReport(p, room, cat)) p.reported = true;
        }

        void OnDestroy()
        {
            foreach (var p in photos) if (p.tex != null) Destroy(p.tex);
            HideHudForCapture = false;
        }
    }
}
