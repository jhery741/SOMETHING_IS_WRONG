using System.Collections.Generic;
using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>
    /// Interfaz IMGUI (sin dependencias): inicio, estado, barras, mensajes, subtítulos de sonidos (con dirección),
    /// subtítulos de grabaciones, lectura de notas, panel de reporte, visor de fotos, pausa con opciones,
    /// victoria y derrota. Incluye un post-procesado ligero (viñeta + grano) que funciona en cualquier pipeline.
    /// </summary>
    public class HUD : MonoBehaviour
    {
        public static HUD Instance { get; private set; }

        class Msg { public string text; public float until; public Color color; }
        class Cap { public string text; public Vector3 pos; public float until; }

        static readonly List<Msg> pendingBeforeInstance = new List<Msg>();
        static readonly Dictionary<string, float> captionCooldown = new Dictionary<string, float>();
        static float noiseLevel, noiseTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            pendingBeforeInstance.Clear();
            captionCooldown.Clear();
            noiseLevel = 0f;
            noiseTime = 0f;
        }

        readonly List<Msg> msgs = new List<Msg>();
        readonly List<Cap> caps = new List<Cap>();
        string subtitle;
        float subtitleUntil;
        Texture2D white, vignette, grain;
        GUIStyle sTitle, sSub, sText, sSmall, sCenter, sPrompt, sMsg, sButton, sToggle, sNote;
        bool stylesReady;
        float W, H;

        static readonly Color Ink = new Color(0.92f, 0.89f, 0.82f);
        static readonly Color Dim = new Color(0.66f, 0.62f, 0.56f);
        static readonly Color Danger = new Color(1f, 0.35f, 0.3f);
        static readonly Color Good = new Color(0.6f, 1f, 0.7f);
        static readonly Color Warm = new Color(1f, 0.85f, 0.55f);

        void Awake()
        {
            Instance = this;
            white = Texture2D.whiteTexture;
            vignette = MakeVignette();
            grain = ProceduralTextures.Grain();
            foreach (var m in pendingBeforeInstance) msgs.Add(m);
            pendingBeforeInstance.Clear();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (vignette != null) Destroy(vignette);
            if (grain != null) Destroy(grain);
        }

        // ------------------------------------------------------------ API estática
        public static void Show(string text, float seconds = 3.5f) { Show(text, seconds, Ink); }

        public static void Show(string text, float seconds, Color color)
        {
            var m = new Msg { text = text, until = Time.unscaledTime + seconds, color = color };
            if (Instance == null) { pendingBeforeInstance.Add(m); return; }
            Instance.msgs.Add(m);
            if (Instance.msgs.Count > 4) Instance.msgs.RemoveAt(0);
        }

        /// <summary>Subtítulo de sonido (accesibilidad). Solo si está activado y el sonido está a su alcance.</summary>
        public static void Caption(string text, Vector3 worldPos, float range)
        {
            if (!GameSettings.Captions || Instance == null || string.IsNullOrEmpty(text)) return;
            var d = GameDirector.Instance;
            if (d == null || d.Player == null || d.Player.Cam == null) return;
            if ((d.Player.Cam.transform.position - worldPos).sqrMagnitude > range * range) return;
            float now = Time.unscaledTime;
            float last;
            if (captionCooldown.TryGetValue(text, out last) && now - last < 2.5f)
            {
                foreach (var c in Instance.caps) if (c.text == text) { c.pos = worldPos; c.until = now + 2.2f; }
                return;
            }
            captionCooldown[text] = now;
            Instance.caps.RemoveAll(c => c.text == text);
            Instance.caps.Add(new Cap { text = text, pos = worldPos, until = now + 2.2f });
            if (Instance.caps.Count > 4) Instance.caps.RemoveAt(0);
        }

        /// <summary>Subtítulo de diálogo (grabaciones). Se muestra siempre.</summary>
        public static void Subtitle(string text, float seconds)
        {
            if (Instance == null) return;
            Instance.subtitle = text;
            Instance.subtitleUntil = Time.unscaledTime + seconds;
        }

        public static void NotifyNoise(float radius, bool fromPlayer)
        {
            if (!fromPlayer) return;
            if (radius >= CurrentNoise()) { noiseLevel = radius; noiseTime = Time.unscaledTime; }
        }

        static float CurrentNoise()
        {
            return noiseLevel * Mathf.Clamp01(1f - (Time.unscaledTime - noiseTime) / 1.2f);
        }

        // ------------------------------------------------------------ estilos
        static Texture2D MakeVignette()
        {
            const int S = 128;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f;
                    px[y * S + x] = new Color(1f, 1f, 1f, Mathf.SmoothStep(0.35f, 1f, d));
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        GUIStyle Make(int size, TextAnchor align, FontStyle fs = FontStyle.Normal)
        {
            var s = new GUIStyle(GUI.skin.label);
            s.fontSize = size;
            s.alignment = align;
            s.fontStyle = fs;
            s.wordWrap = true;
            s.richText = true;
            s.normal.textColor = Ink;
            return s;
        }

        void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;
            sTitle = Make(64, TextAnchor.MiddleCenter, FontStyle.Bold);
            sSub = Make(28, TextAnchor.MiddleCenter, FontStyle.Bold);
            sText = Make(22, TextAnchor.UpperLeft);
            sSmall = Make(18, TextAnchor.UpperLeft);
            sCenter = Make(22, TextAnchor.UpperCenter);
            sPrompt = Make(24, TextAnchor.UpperCenter, FontStyle.Bold);
            sMsg = Make(22, TextAnchor.MiddleCenter);
            sNote = Make(23, TextAnchor.UpperLeft);
            sNote.normal.textColor = new Color(0.12f, 0.1f, 0.08f);
            sButton = new GUIStyle(GUI.skin.button);
            sButton.fontSize = 24;
            sToggle = new GUIStyle(GUI.skin.toggle);
            sToggle.fontSize = 22;
            sToggle.normal.textColor = Ink;
            sToggle.onNormal.textColor = Ink;
        }

        // ------------------------------------------------------------ helpers
        void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        void Label(Rect r, string text, GUIStyle style, Color color)
        {
            var old = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.85f);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.normal.textColor = old;
        }

        void Bar(float x, float y, float w, string label, float value01, Color c)
        {
            Label(new Rect(x, y - 2f, w + 120f, 22f), label, sSmall, Dim);
            Fill(new Rect(x, y + 20f, w, 5f), new Color(0f, 0f, 0f, 0.5f));
            Fill(new Rect(x, y + 20f, w * Mathf.Clamp01(value01), 5f), c);
        }

        // ------------------------------------------------------------ dibujo
        void OnGUI()
        {
            if (EvidenceCamera.HideHudForCapture) return;
            EnsureStyles();
            float scale = Screen.height / 1080f;
            if (scale <= 0f) return;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            W = Screen.width / scale;
            H = 1080f;

            var d = GameDirector.Instance;
            if (d == null || d.Player == null) return;

            if (d.Phase == GamePhase.Intro) { DrawIntro(d); return; }

            if (GameSettings.Effects && !PostFxRig.Active) DrawFilmEffects();
            DrawDanger(d);
            if (d.Player.IsHidden) DrawHidden(d);
            if (d.Phase != GamePhase.Defeat && d.Phase != GamePhase.Victory)
            {
                DrawCrosshair(d);
                DrawStatus(d);
                DrawBars(d);
                DrawPhase(d);
                DrawCaptions(d);
                DrawSubtitle();
                DrawNote(d);
                DrawPhoto(d);
                DrawReportPanel(d);
            }
            DrawMessages();

            if (d.Phase == GamePhase.Victory) DrawVictory(d);
            else if (d.Phase == GamePhase.Defeat) DrawDefeat(d);
            else if (d.Paused) DrawPause(d);
        }

        void DrawFilmEffects()
        {
            // Viñeta muy suave para enfocar la mirada; sin grano (la imagen la dan materiales y luz, no un filtro).
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.22f);
            if (vignette != null) GUI.DrawTexture(new Rect(0f, 0f, W, H), vignette);
            GUI.color = old;
        }

        void DrawIntro(GameDirector d)
        {
            Fill(new Rect(0f, 0f, W, H), new Color(0.01f, 0.008f, 0.006f, 0.97f));
            float cw = Mathf.Min(1240f, W - 80f);
            float x = (W - cw) * 0.5f;
            Label(new Rect(x, 50f, cw, 90f), "ANOMALY: DESCENT", sTitle, Ink);
            Label(new Rect(x, 140f, cw, 40f), d.floorTitle, sSub, Danger);

            int total = d.Anomalies != null ? d.Anomalies.Total : 7;
            string body =
                "Has venido a investigar las desapariciones de esta casa. No cambia mientras la miras: cambia cuando dejas de hacerlo.\n" +
                "Cada reporte equivocado o anomalía ignorada eleva la amenaza. Al llegar al límite despierta <b>" + MonsterName(d) + "</b>, " +
                "y ya no se irá de este piso.\n\n" +
                "<b>Objetivo:</b> reporta las " + total + " anomalías para abrir la reja de la <b>escalera</b> y desciende.";
            Label(new Rect(x, 200f, cw, 190f), body, sText, Ink);

            float colW = (cw - 40f) * 0.5f;
            string c1 =
                "<b>CONTROLES</b>\n" +
                "WASD · moverse            Ratón · mirar\n" +
                "Shift · correr (mucho ruido)\n" +
                "C / Ctrl · agacharse (casi silencioso)\n" +
                "E · interactuar, leer, esconderse\n" +
                "F · linterna        Q · foto de evidencia\n" +
                "Tab (mantener) · comparar con la foto\n" +
                "R · reportar lo que miras  →  1-5 categoría\n" +
                "G / clic derecho · lanzar botella\n" +
                "Esc · pausa y opciones";
            string c2 =
                "<b>CÓMO SOBREVIVIR</b>\n" +
                "• Fotografía cada habitación al principio.\n" +
                "• Hay cosas raras que son normales: no reportes por reflejo.\n" +
                "• Las fotos pueden mostrar lo que el ojo no ve.\n" +
                "• Hay una señal clara antes de cada ataque: apréndela.\n" +
                "• Lanza botellas lejos para desviar a la amenaza.\n" +
                "• No repitas escondite.\n" +
                "• El refugio de la escalera la detiene en la puerta.";
            Label(new Rect(x, 400f, colW, 360f), c1, sText, Ink);
            Label(new Rect(x + colW + 40f, 400f, colW, 360f), c2, sText, Ink);

            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3f);
            Label(new Rect(x, H - 140f, cw, 40f), "Haz clic o pulsa ENTER para entrar", sSub, new Color(Ink.r, Ink.g, Ink.b, pulse));
        }

        void DrawDanger(GameDirector d)
        {
            if (d.Monster == null || !d.Monster.IsActive || vignette == null) return;
            Vector3 a = d.Monster.transform.position, b = d.Player.transform.position;
            float dist = Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));
            float k = Mathf.Clamp01(1f - dist / 10f);
            if (k <= 0f) return;
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * (4f + 6f * k));
            var old = GUI.color;
            GUI.color = new Color(0.7f, 0f, 0f, k * 0.75f * pulse);
            GUI.DrawTexture(new Rect(0f, 0f, W, H), vignette);
            GUI.color = old;
        }

        void DrawHidden(GameDirector d)
        {
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            if (vignette != null) GUI.DrawTexture(new Rect(0f, 0f, W, H), vignette);
            GUI.color = old;
            string name = d.Player.CurrentHide != null ? d.Player.CurrentHide.displayName : "";
            Label(new Rect(0f, 70f, W, 40f), "ESCONDIDO · " + name + " — no hagas ruido · [E] salir", sCenter, Dim);
        }

        void DrawCrosshair(GameDirector d)
        {
            var it = d.Player.Interaction;
            float cx = W * 0.5f, cy = H * 0.5f;
            if (!d.Player.IsHidden && (it == null || it.ReadingNote == null))
            {
                if (it != null && it.LookingAtInteractable)
                {
                    Fill(new Rect(cx - 9f, cy - 1f, 6f, 2f), Ink);
                    Fill(new Rect(cx + 3f, cy - 1f, 6f, 2f), Ink);
                    Fill(new Rect(cx - 1f, cy - 9f, 2f, 6f), Ink);
                    Fill(new Rect(cx - 1f, cy + 3f, 2f, 6f), Ink);
                }
                else Fill(new Rect(cx - 2f, cy - 2f, 4f, 4f), new Color(1f, 1f, 1f, 0.7f));
            }
            if (it == null) return;
            if (!string.IsNullOrEmpty(it.Prompt)) Label(new Rect(cx - 400f, cy + 30f, 800f, 34f), it.Prompt, sPrompt, Ink);
            if (!string.IsNullOrEmpty(it.ReportHint)) Label(new Rect(cx - 400f, cy + 64f, 800f, 30f), it.ReportHint, sCenter, Dim);
        }

        void DrawStatus(GameDirector d)
        {
            string room = d.PlayerRoom != null ? d.PlayerRoom.displayName.ToUpper() : "—";
            int rep = d.Anomalies != null ? d.Anomalies.ReportedCount : 0;
            int tot = d.Anomalies != null ? d.Anomalies.Total : 0;
            bool clues = d.CluesTotal > 0;
            float h = 52f + (clues ? 22f : 0f) + (d.showDebug ? 48f : 0f);
            Fill(new Rect(24f, 24f, 330f, h), new Color(0f, 0f, 0f, 0.28f));
            Label(new Rect(36f, 28f, 320f, 26f), "PISO " + d.floorNumber + " · " + room, sSmall, Ink);
            Label(new Rect(36f, 50f, 320f, 24f), "Anomalías " + rep + "/" + tot, sSmall, rep >= tot && tot > 0 ? Good : Dim);
            float y = 72f;
            if (clues) { Label(new Rect(36f, y, 320f, 24f), "Pistas " + d.CluesFound + "/" + d.CluesTotal, sSmall, Dim); y += 22f; }
            if (d.showDebug)
            {
                // Solo desarrollo (F9 en el Editor / build de desarrollo)
                Color ec = d.Errors >= d.errorThreshold - 1 ? Danger : Dim;
                Label(new Rect(36f, y, 320f, 24f), "[dev] errores " + d.Errors + "/" + d.errorThreshold + " · " + d.ThreatLabel, sSmall, ec);
                int active = d.Anomalies != null ? d.Anomalies.ActiveCount : 0;
                Label(new Rect(36f, y + 22f, 320f, 24f), "[dev] activas " + active + (d.Monster != null ? " · " + d.Monster.StateName : ""), sSmall, Dim);
            }
        }

        void DrawBars(GameDirector d)
        {
            // Indicadores compactos abajo a la izquierda, sin panel grande.
            float x = 32f, w = 170f, y = H - 128f;
            Fill(new Rect(22f, y - 8f, w + 150f, 120f), new Color(0f, 0f, 0f, 0.22f));
            var p = d.Player;
            Bar(x, y, w, p.IsExhausted ? "ALIENTO (agotado)" : "ALIENTO", p.stamina / p.maxStamina, p.IsExhausted ? Danger : new Color(0.75f, 0.78f, 0.8f));
            var fl = p.Flashlight;
            if (fl != null) Bar(x, y + 36f, w, "LINTERNA " + fl.battery.ToString("0") + "%", fl.battery / 100f, fl.On ? Warm : new Color(Warm.r, Warm.g, Warm.b, 0.45f));
            float noise = CurrentNoise() / 12f;
            Bar(x, y + 72f, w, "RUIDO", noise, Color.Lerp(new Color(0.5f, 0.75f, 0.6f), Danger, Mathf.Clamp01(noise)));
            string extra = "Botellas " + p.throwables + "/" + PlayerController.MaxThrowables;
            if (p.Evidence != null) extra += p.Evidence.HasPhoto(d.PlayerRoom) ? "   ·   foto ✓ [Tab]" : "   ·   sin foto [Q]";
            Label(new Rect(x + w + 14f, y + 6f, 140f, 80f), extra.Replace("   ·   ", "\n"), sSmall, Dim);
        }

        void DrawPhase(GameDirector d)
        {
            string t = null;
            Color c = Ink;
            switch (d.Phase)
            {
                case GamePhase.Observation: t = "OBSERVACIÓN — memoriza el piso  " + GameDirector.FormatTime(d.PhaseTimer); break;
                case GamePhase.Awakening: t = "ALGO HA DESPERTADO"; c = Danger; break;
                case GamePhase.Active:
                    switch (d.ThreatLabel)
                    {
                        case "Cacería": t = "CACERÍA"; c = Danger; break;
                        case "Búsqueda": t = "TE BUSCA"; c = new Color(1f, 0.6f, 0.45f); break;
                        default: t = (MonsterName(d) + " acecha").ToUpper(); c = Dim; break;
                    }
                    break;
                case GamePhase.Investigation:
                    if (d.Errors >= d.errorThreshold - 1) { t = "ADVERTENCIA"; c = new Color(1f, 0.6f, 0.45f); }
                    break;
            }
            if (d.Anomalies != null && d.Anomalies.AllReported && d.Phase != GamePhase.Victory)
            {
                t = "LA ESCALERA ESTÁ ABIERTA — DESCIENDE";
                c = Good;
            }
            if (t == null) return;
            Label(new Rect(0f, 28f, W, 40f), "<b>" + t + "</b>", sPrompt, c);
        }

        void DrawCaptions(GameDirector d)
        {
            float now = Time.unscaledTime;
            caps.RemoveAll(c => c.until < now);
            if (caps.Count == 0) return;
            var cam = d.Player.Cam;
            float y = H * 0.5f + 120f;
            foreach (var c in caps)
            {
                Vector3 local = cam.transform.InverseTransformDirection(c.pos - cam.transform.position);
                float ang = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                string arrow = Mathf.Abs(ang) < 35f ? "↑ " : (Mathf.Abs(ang) > 145f ? "↓ " : (ang > 0f ? "→ " : "← "));
                float a = Mathf.Clamp01((c.until - now) / 0.5f);
                string txt = arrow + c.text;
                var r = new Rect(W * 0.5f - 260f, y, 520f, 30f);
                Fill(r, new Color(0f, 0f, 0f, 0.5f * a));
                Label(r, txt, sCenter, new Color(1f, 0.95f, 0.75f, a));
                y += 34f;
            }
        }

        void DrawSubtitle()
        {
            if (string.IsNullOrEmpty(subtitle) || Time.unscaledTime > subtitleUntil) return;
            float w = Mathf.Min(1200f, W - 80f);
            var r = new Rect((W - w) * 0.5f, H - 330f, w, 64f);
            Fill(r, new Color(0f, 0f, 0f, 0.65f));
            Label(new Rect(r.x + 20f, r.y + 6f, r.width - 40f, r.height), "<i>" + subtitle + "</i>", sMsg, Warm);
        }

        void DrawNote(GameDirector d)
        {
            var it = d.Player.Interaction;
            if (it == null || it.ReadingNote == null) return;
            var n = it.ReadingNote;
            float w = 560f, h = 640f;
            var r = new Rect(W - w - 60f, (H - h) * 0.5f, w, h);
            Fill(new Rect(r.x - 6f, r.y - 6f, w + 12f, h + 12f), new Color(0f, 0f, 0f, 0.6f));
            Fill(r, new Color(0.86f, 0.81f, 0.68f, 0.97f));
            var old = sNote.fontStyle;
            sNote.fontStyle = FontStyle.Bold;
            GUI.Label(new Rect(r.x + 34f, r.y + 28f, w - 68f, 40f), n.title, sNote);
            sNote.fontStyle = old;
            GUI.Label(new Rect(r.x + 34f, r.y + 80f, w - 68f, h - 140f), n.body, sNote);
            Label(new Rect(r.x, r.y + h + 14f, w, 30f), "[E] cerrar  ·  puedes moverte mientras lees", sCenter, Dim);
        }

        void DrawMessages()
        {
            float now = Time.unscaledTime;
            msgs.RemoveAll(m => m.until < now);
            float y = H - 270f;
            for (int i = msgs.Count - 1; i >= 0; i--)
            {
                var m = msgs[i];
                float a = Mathf.Clamp01((m.until - now) / 0.6f);
                float w = Mathf.Min(1150f, W - 80f);
                var r = new Rect((W - w) * 0.5f, y, w, 40f);
                Fill(r, new Color(0f, 0f, 0f, 0.45f * a));
                Label(r, m.text, sMsg, new Color(m.color.r, m.color.g, m.color.b, a));
                y -= 46f;
            }
        }

        void DrawReportPanel(GameDirector d)
        {
            var it = d.Player.Interaction;
            if (it == null || !it.ReportOpen || it.ReportTarget == null) return;
            float w = 680f, h = 390f;
            var r = new Rect((W - w) * 0.5f, (H - h) * 0.5f - 40f, w, h);
            Fill(r, new Color(0.03f, 0.02f, 0.015f, 0.92f));
            Fill(new Rect(r.x, r.y, w, 3f), Danger);
            Label(new Rect(r.x + 30f, r.y + 20f, w - 60f, 36f), "<b>DISPOSITIVO DE EVIDENCIA · REPORTE</b>", sSub, Ink);
            var room = d.Level != null ? d.Level.RoomById(it.ReportTarget.roomId) : null;
            string where = room != null ? room.displayName : "?";
            Label(new Rect(r.x + 30f, r.y + 66f, w - 60f, 30f), "Objetivo: <b>" + it.ReportTarget.displayName + "</b> — " + where, sCenter, Ink);
            for (int i = 0; i < 5; i++)
            {
                var c = (AnomalyCategory)i;
                Label(new Rect(r.x + 60f, r.y + 112f + i * 40f, w - 100f, 34f),
                    "<b>[" + (i + 1) + "]  " + AnomalyText.Name(c) + "</b>  <color=#9a917f>— " + AnomalyText.Hint(c) + "</color>", sText, Ink);
            }
            Label(new Rect(r.x + 30f, r.y + h - 50f, w - 60f, 30f), "[R] / [Esc] cancelar  ·  La radio tarda 2 s y hace ruido", sCenter, Dim);
        }

        void DrawPhoto(GameDirector d)
        {
            var ev = d.Player.Evidence;
            if (ev == null || !ev.Viewing) return;
            float age;
            var tex = ev.PhotoOf(d.PlayerRoom, out age);
            string roomName = d.PlayerRoom != null ? d.PlayerRoom.displayName : "?";
            if (tex == null)
            {
                var r0 = new Rect(W * 0.5f - 320f, H * 0.5f - 50f, 640f, 100f);
                Fill(r0, new Color(0f, 0f, 0f, 0.8f));
                Label(new Rect(r0.x, r0.y + 30f, r0.width, 40f), "No hay foto de " + roomName + ". Pulsa [Q] para tomar una.", sCenter, Ink);
                return;
            }
            float maxW = W * 0.62f, maxH = H * 0.62f;
            float aspect = (float)tex.width / Mathf.Max(1, tex.height);
            float pw = maxW, ph = pw / aspect;
            if (ph > maxH) { ph = maxH; pw = ph * aspect; }
            var r = new Rect((W - pw) * 0.5f, (H - ph) * 0.5f, pw, ph);
            Fill(new Rect(r.x - 10f, r.y - 46f, r.width + 20f, r.height + 56f), new Color(0.9f, 0.86f, 0.76f, 0.95f));
            GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
            Label(new Rect(r.x, r.y - 42f, r.width, 34f), "<b>FOTO · " + roomName.ToUpper() + "</b>  —  hace " + GameDirector.FormatTime(age), sCenter, new Color(0.1f, 0.1f, 0.1f));
        }

        void DrawPause(GameDirector d)
        {
            Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, 0.8f));
            Label(new Rect(0f, 150f, W, 80f), "PAUSA", sTitle, Ink);
            float bw = 420f, x = (W - bw) * 0.5f;
            float y = 240f;

            Label(new Rect(x, y, bw, 30f), "Sensibilidad del ratón: " + GameSettings.Sensitivity.ToString("0.00"), sText, Ink);
            float sens = GUI.HorizontalSlider(new Rect(x, y + 36f, bw, 24f), GameSettings.Sensitivity, 0.02f, 0.35f);
            if (!Mathf.Approximately(sens, GameSettings.Sensitivity))
            {
                GameSettings.Sensitivity = sens;
                d.Player.sensitivity = sens;
            }
            y += 66f;
            GameSettings.MasterVolume = VolumeSlider(x, ref y, bw, "Volumen general", GameSettings.MasterVolume);
            GameSettings.SfxVolume = VolumeSlider(x, ref y, bw, "Efectos", GameSettings.SfxVolume);
            GameSettings.AmbienceVolume = VolumeSlider(x, ref y, bw, "Ambiente", GameSettings.AmbienceVolume);
            GameSettings.Apply();
            GameSettings.InvertY = GUI.Toggle(new Rect(x, y, bw, 34f), GameSettings.InvertY, "  Invertir eje vertical", sToggle);
            y += 40f;
            GameSettings.Captions = GUI.Toggle(new Rect(x, y, bw, 34f), GameSettings.Captions, "  Subtítulos de sonidos", sToggle);
            y += 40f;
            GameSettings.Effects = GUI.Toggle(new Rect(x, y, bw, 34f), GameSettings.Effects, "  Viñeta suave", sToggle);
            y += 40f;
            if (Application.isEditor || Debug.isDebugBuild)
                d.showDebug = GUI.Toggle(new Rect(x, y, bw, 34f), d.showDebug, "  [dev] Contadores de prueba", sToggle);
            y += 54f;

            if (GUI.Button(new Rect(x, y, bw, 56f), "Continuar", sButton)) d.Resume();
            if (GUI.Button(new Rect(x, y + 70f, bw, 56f), "Reiniciar piso", sButton)) d.Restart();
            if (GUI.Button(new Rect(x, y + 140f, bw, 56f), "Salir", sButton))
            {
                GameSettings.Save();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
            Label(new Rect(0f, y + 220f, W, 30f), "[Esc] continuar", sCenter, Dim);
        }

        float VolumeSlider(float x, ref float y, float bw, string label, float v)
        {
            Label(new Rect(x, y, bw, 28f), label + ": " + Mathf.RoundToInt(v * 100f) + "%", sSmall, Ink);
            float nv = GUI.HorizontalSlider(new Rect(x, y + 28f, bw, 20f), v, 0f, 1f);
            y += 54f;
            return nv;
        }

        static string MonsterName(GameDirector d)
        {
            return d != null && d.Monster != null ? d.Monster.monsterName : "la amenaza";
        }

        void DrawVictory(GameDirector d)
        {
            Fill(new Rect(0f, 0f, W, H), new Color(0.01f, 0.012f, 0.025f, 0.94f));
            if (d.IsFinalFloor)
            {
                Label(new Rect(0f, 170f, W, 90f), "INFORME COMPLETO", sTitle, Good);
                Label(new Rect(0f, 270f, W, 40f), "El último tramo de escalera acaba en una puerta de servicio. Fuera es de noche y llueve.", sSub, Ink);
                Label(new Rect(0f, 310f, W, 40f), "El Ciego, El Torcido, La Presencia: tres nombres más en el expediente de Ferrer. Esta vez, con firma.", sSub, Dim);
            }
            else
            {
                Label(new Rect(0f, 200f, W, 90f), "PISO " + d.floorNumber + " COMPLETADO", sTitle, Good);
                Label(new Rect(0f, 300f, W, 40f), "Desciendes. El aire se vuelve frío y huele a limpio.", sSub, Ink);
            }
            string stats = "Tiempo: " + GameDirector.FormatTime(d.PlayTime) +
                           "    ·    Reportes correctos: " + d.CorrectReports + "    ·    Errores: " + d.WrongReports +
                           "    ·    Pistas: " + d.CluesFound + "/" + d.CluesTotal;
            Label(new Rect(0f, 380f, W, 34f), stats, sCenter, Dim);
            float bw = 320f;
            if (d.NextSceneAvailable)
            {
                Label(new Rect(0f, 430f, W, 34f), "Progreso guardado.", sCenter, Dim);
                if (GUI.Button(new Rect((W - bw) * 0.5f, 500f, bw, 56f), "Descender  [Enter]", sButton)) d.LoadNextFloor();
                if (GUI.Button(new Rect((W - bw) * 0.5f, 570f, bw, 56f), "Repetir piso  [R]", sButton)) d.Restart();
            }
            else
            {
                if (d.IsFinalFloor)
                {
                    Label(new Rect(0f, 430f, W, 34f), "FIN", sCenter, Ink);
                    if (GUI.Button(new Rect((W - bw) * 0.5f, 500f, bw, 56f), "Volver al Piso 1  [Enter]", sButton)) d.BackToFirstFloor();
                    if (GUI.Button(new Rect((W - bw) * 0.5f, 570f, bw, 56f), "Repetir Piso 3  [R]", sButton)) d.Restart();
                }
                else
                {
                    Label(new Rect(0f, 430f, W, 34f), d.nextFloorTeaser + " Progreso guardado.", sCenter, Dim);
                    if (GUI.Button(new Rect((W - bw) * 0.5f, 520f, bw, 56f), "Jugar de nuevo  [R]", sButton)) d.Restart();
                }
            }
        }

        void DrawDefeat(GameDirector d)
        {
            float t = Time.unscaledTime - d.DefeatTime;
            Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, Mathf.Clamp01(t / 1.8f)));
            if (t < 1.8f) return;
            float a = Mathf.Clamp01((t - 1.8f) / 0.8f);
            Label(new Rect(0f, 240f, W, 90f), "DESAPARICIÓN", sTitle, new Color(Danger.r, Danger.g, Danger.b, a));
            Label(new Rect(0f, 340f, W, 40f), MonsterName(d) + " te encontró. Tu informe queda incompleto, como el de Ferrer.", sSub, new Color(Ink.r, Ink.g, Ink.b, a));
            Label(new Rect(0f, 410f, W, 34f), "Anomalías reportadas: " + d.Anomalies.ReportedCount + "/" + d.Anomalies.Total +
                  "    ·    Tiempo: " + GameDirector.FormatTime(d.PlayTime), sCenter, new Color(Dim.r, Dim.g, Dim.b, a));
            float bw = 320f;
            if (a > 0.5f && GUI.Button(new Rect((W - bw) * 0.5f, 500f, bw, 56f), "Reintentar  [R]", sButton)) d.Restart();
        }
    }
}
