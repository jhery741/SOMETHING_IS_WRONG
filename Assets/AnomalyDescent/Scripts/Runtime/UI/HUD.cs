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
        GUIStyle sTitle, sSub, sText, sSmall, sCenter, sPrompt, sMsg, sButton, sToggle;
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

            if (!PostFxRig.Active) DrawFilmEffects();
            DrawDanger(d);
            if (d.Player.IsHidden) DrawHidden(d);
            if (d.Phase != GamePhase.Defeat && d.Phase != GamePhase.Victory)
            {
                if (d.Player.Evidence != null && d.Player.Evidence.Raised) DrawViewfinder(d);
                else DrawCrosshair(d);
                DrawStatus(d);
                DrawBars(d);
                DrawPhase(d);
                DrawCaptions(d);
                DrawSubtitle();
                DrawQuestion(d);
                DrawCarrete(d);
                DrawLapTitle(d);
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
            if (GameSettings.Effects && vignette != null) GUI.DrawTexture(new Rect(0f, 0f, W, H), vignette);
            GUI.color = new Color(1f, 1f, 1f, 0.08f);
            if (GameSettings.Grain && grain != null) GUI.DrawTextureWithTexCoords(new Rect(0f, 0f, W, H), grain,
                new Rect((Time.frameCount % 13) / 13f, (Time.frameCount % 7) / 7f, W / 256f, H / 256f));
            GUI.color = old;
        }

        void DrawIntro(GameDirector d)
        {
            Fill(new Rect(0f, 0f, W, H), new Color(0.01f, 0.008f, 0.006f, 0.97f));
            float cw = Mathf.Min(1240f, W - 80f);
            float x = (W - cw) * 0.5f;
            Label(new Rect(x, 50f, cw, 90f), "ANOMALY: DESCENT", sTitle, Ink);
            Label(new Rect(x, 140f, cw, 40f), "UNA CASA · " + d.LapCount + " VUELTAS · " + MonsterName(d).ToUpper(), sSub, Danger);

            string body =
                "Has venido a documentar esta casa. No cambia mientras la miras: cambia cuando dejas de hacerlo.\n" +
                "Cada vuelta: <b>observa</b> la casa normal, <b>fotografía</b> lo que cambie y <b>repórtalo</b> con su habitación y categoría. " +
                "Cuando lo tengas todo, <b>cierra la vuelta en la radio del refugio</b>. Tras la última, sal por la puerta principal.\n" +
                "Algo en la casa te escucha. Primero hablará contigo. Cada error le acerca un poco más.";
            Label(new Rect(x, 196f, cw, 200f), body, sText, Ink);

            float colW = (cw - 40f) * 0.5f;
            string c1 =
                "<b>CONTROLES</b>\n" +
                "WASD · moverse            Ratón · mirar\n" +
                "Shift · correr (mucho ruido)   C / Ctrl · agacharse\n" +
                "E · interactuar (puertas, escondites, radio)\n" +
                "Clic derecho (o Q) · levantar la cámara\n" +
                "Rueda o Z/X · zoom    Clic izq. o Espacio · foto\n" +
                "Tab · carrete: A/D fotos · Enter reportar\n" +
                "      → 1-6 habitación → 1-5 categoría\n" +
                "1 / 2 / 3 · responder cuando te pregunte\n" +
                "F · linterna   G · lanzar botella   Esc · pausa\n" +
                "J/L e I/K · girar la vista con teclado";
            string c2 =
                "<b>CÓMO SOBREVIVIR</b>\n" +
                "• Fotografía cada habitación al principio: el carrete sirve para comparar.\n" +
                "• Una foto vale si el objeto está centrado, cerca y sin nada delante.\n" +
                "• Hay cosas raras que son normales: no reportes por reflejo.\n" +
                "• Contestarle la verdad le calma, pero hablar hace ruido.\n" +
                "• Cuando aparezca, no te acerques. No ve: te OYE.\n" +
                "• Agachado y quieto eres casi silencioso. El refugio le detiene.";
            Label(new Rect(x, 410f, colW, 360f), c1, sText, Ink);
            Label(new Rect(x + colW + 40f, 410f, colW, 360f), c2, sText, Ink);
            if (SaveData.CheckpointLap > 1)
                Label(new Rect(x, H - 190f, cw, 40f), "[C] Continuar desde la vuelta " + SaveData.CheckpointLap, sSub, Warm);

            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3f);
            Label(new Rect(x, H - 140f, cw, 40f), "Haz clic o pulsa ENTER para empezar desde la vuelta 1", sSub, new Color(Ink.r, Ink.g, Ink.b, pulse));
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
            if (!d.Player.IsHidden)
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
            if (!string.IsNullOrEmpty(it.Prompt)) Label(new Rect(cx - 640f, cy + 30f, 1280f, 34f), it.Prompt, sPrompt, Ink);
        }

        void DrawStatus(GameDirector d)
        {
            string room = d.PlayerRoom != null ? d.PlayerRoom.displayName.ToUpper() : "—";
            int rep = d.Anomalies != null ? d.Anomalies.ReportedCount : 0;
            int tot = d.Anomalies != null ? d.Anomalies.Total : 0;
            var pr = d.Presence;
            float h = 100f + (d.showDebug ? 70f : 0f);
            Fill(new Rect(24f, 24f, 380f, h), new Color(0f, 0f, 0f, 0.28f));
            Label(new Rect(36f, 28f, 360f, 26f), "VUELTA " + d.Lap + "/" + d.LapCount + " · " + room, sSmall, Ink);
            string an = d.Phase == GamePhase.Observation ? "Anomalías: aún ninguna (observa)" : "Anomalías de la vuelta " + rep + "/" + tot;
            Label(new Rect(36f, 50f, 360f, 24f), an, sSmall, rep >= tot && tot > 0 ? Good : Dim);
            if (pr != null)
            {
                // Atención de la presencia (regla visible: los errores la suben; la verdad y cerrar vueltas la bajan)
                Label(new Rect(36f, 74f, 120f, 24f), "Te escucha", sSmall, Dim);
                for (int i = 0; i < 5; i++)
                {
                    float fill = Mathf.Clamp01(pr.Attention - i);
                    var r0 = new Rect(140f + i * 22f, 80f, 16f, 12f);
                    Fill(r0, new Color(0f, 0f, 0f, 0.5f));
                    Fill(new Rect(r0.x, r0.y, r0.width * fill, r0.height), Color.Lerp(new Color(0.75f, 0.7f, 0.55f), Danger, i / 4f));
                }
                Label(new Rect(260f, 74f, 150f, 24f), pr.StateLabel, sSmall, pr.Stage >= 3 || d.MonsterAwake ? Danger : Dim);
            }
            float y = 100f;
            if (d.showDebug)
            {
                // Solo desarrollo (F9 en el Editor / build de desarrollo)
                Label(new Rect(36f, y, 360f, 24f), "[dev] atención " + (pr != null ? pr.Attention.ToString("0.00") : "-") + " · etapa " + (pr != null ? pr.Stage.ToString() : "-"), sSmall, Dim);
                int active = d.Anomalies != null ? d.Anomalies.ActiveCount : 0;
                Label(new Rect(36f, y + 22f, 360f, 24f), "[dev] activas " + active + (d.Monster != null ? " · " + d.Monster.StateName : ""), sSmall, Dim);
                var pp = d.Player.transform.position;
                Label(new Rect(36f, y + 44f, 360f, 24f), "[dev] pos " + pp.x.ToString("0.0") + ", " + pp.z.ToString("0.0") + " · rumbo " + Mathf.Repeat(d.Player.Yaw, 360f).ToString("0"), sSmall, Dim);
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
            if (p.Evidence != null) extra += "   ·   Fotos " + p.Evidence.Photos.Count + " [Tab]";
            Label(new Rect(x + w + 14f, y + 6f, 140f, 80f), extra.Replace("   ·   ", "\n"), sSmall, Dim);
        }

        void DrawPhase(GameDirector d)
        {
            string t = null;
            Color c = Ink;
            switch (d.Phase)
            {
                case GamePhase.Observation: t = "OBSERVA LA CASA  " + GameDirector.FormatTime(d.PhaseTimer) + "   ·   (la radio del refugio empieza antes)"; break;
                case GamePhase.Investigation:
                    t = d.Anomalies != null && !d.Anomalies.AllApplied ? "INVESTIGA — algo va a cambiar cuando no mires" : "INVESTIGA — fotografía y reporta lo que ha cambiado";
                    c = Dim;
                    break;
                case GamePhase.LapComplete: t = "VUELTA COMPLETA — CIÉRRALA EN LA RADIO DEL REFUGIO"; c = Good; break;
                case GamePhase.Escape: t = "SAL POR LA PUERTA PRINCIPAL DEL RECIBIDOR"; c = Good; break;
            }
            if (d.MonsterAwake && d.Monster.IsChasing) { t = "TE PERSIGUE"; c = Danger; }
            else if (d.MonsterAwake && d.Monster.IsAppearing) { t = "ESTÁ AHÍ. NO TE ACERQUES."; c = Danger; }
            if (t == null) return;
            Label(new Rect(0f, 28f, W, 40f), "<b>" + t + "</b>", sPrompt, c);
        }

        void DrawLapTitle(GameDirector d)
        {
            if (Time.unscaledTime > d.TitleUntil) return;
            float a = Mathf.Clamp01((d.TitleUntil - Time.unscaledTime) / 0.8f);
            string t = d.Phase == GamePhase.Escape ? "LA PUERTA ESTÁ ABIERTA" : (d.CurrentLap != null ? d.CurrentLap.title : "");
            Label(new Rect(0f, H * 0.28f, W, 90f), t, sTitle, new Color(Ink.r, Ink.g, Ink.b, a));
        }

        void DrawQuestion(GameDirector d)
        {
            var pr = d.Presence;
            if (pr == null || !pr.Asking || pr.OptionLabels.Count == 0) return;
            float w = 640f, h = 70f + pr.OptionLabels.Count * 38f;
            var r = new Rect((W - w) * 0.5f, H * 0.5f - h - 30f, w, h);
            Fill(r, new Color(0.02f, 0.015f, 0.01f, 0.82f));
            Fill(new Rect(r.x, r.y, w * Mathf.Clamp01(pr.QuestionTimeLeft / 9f), 3f), Warm);
            Label(new Rect(r.x + 20f, r.y + 10f, w - 40f, 34f), "<i>«" + pr.QuestionText + "»</i>  — responde o calla", sCenter, Warm);
            for (int i = 0; i < pr.OptionLabels.Count; i++)
                Label(new Rect(r.x + 60f, r.y + 50f + i * 38f, w - 100f, 34f), "<b>[" + (i + 1) + "]</b>  " + pr.OptionLabels[i], sText, Ink);
        }

        void DrawViewfinder(GameDirector d)
        {
            var ev = d.Player.Evidence;
            float m = 90f, L = 60f, t = 3f;
            Color c = new Color(1f, 1f, 1f, 0.75f);
            Fill(new Rect(m, m, L, t), c); Fill(new Rect(m, m, t, L), c);
            Fill(new Rect(W - m - L, m, L, t), c); Fill(new Rect(W - m - t, m, t, L), c);
            Fill(new Rect(m, H - m - t, L, t), c); Fill(new Rect(m, H - m - L, t, L), c);
            Fill(new Rect(W - m - L, H - m - t, L, t), c); Fill(new Rect(W - m - t, H - m - L, t, L), c);
            // zona de encuadre válida (60 % central)
            float zx = W * 0.2f, zy = H * 0.2f, zw = W * 0.6f, zh = H * 0.6f;
            Color z = new Color(1f, 1f, 1f, 0.2f);
            Fill(new Rect(zx, zy, 24f, 1f), z); Fill(new Rect(zx, zy, 1f, 24f), z);
            Fill(new Rect(zx + zw - 24f, zy, 24f, 1f), z); Fill(new Rect(zx + zw, zy, 1f, 24f), z);
            Fill(new Rect(zx, zy + zh, 24f, 1f), z); Fill(new Rect(zx, zy + zh - 24f, 1f, 24f), z);
            Fill(new Rect(zx + zw - 24f, zy + zh, 24f, 1f), z); Fill(new Rect(zx + zw, zy + zh - 24f, 1f, 24f), z);
            float cx = W * 0.5f, cy = H * 0.5f;
            Fill(new Rect(cx - 14f, cy, 28f, 1.5f), c); Fill(new Rect(cx, cy - 14f, 1.5f, 28f), c);
            Fill(new Rect(m + 6f, m + 14f, 10f, 10f), new Color(1f, 0.2f, 0.15f, 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 4f)));
            Label(new Rect(m + 24f, m + 6f, 400f, 28f), "ZOOM x" + ev.Zoom.ToString("0.0") + "   ·   " + ev.Photos.Count + "/24", sSmall, c);
            var a = ev.Live;
            string q = EvidenceCamera.QualityText(a.quality);
            string subj = a.subject != null ? a.subject.displayName.Trim() + " · " + q : q;
            Color qc = a.quality == PhotoQuality.Nitida ? Good : (a.quality == PhotoQuality.SinObjetivo ? Dim : Warm);
            Label(new Rect(0f, H - m - 46f, W, 30f), subj, sCenter, qc);
            Label(new Rect(0f, H - m - 18f, W, 28f), "Clic izquierdo / Espacio: foto  ·  Rueda o Z/X: zoom  ·  Suelta clic derecho / Q: bajar", sCenter, Dim);
        }

        void DrawCaptions(GameDirector d)
        {
            float now = Time.unscaledTime;
            caps.RemoveAll(c => c.until < now);
            if (caps.Count == 0) return;
            var cam = d.Player.Cam;
            float y = H * 0.5f + 25f;
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
            var r = new Rect((W - w) * 0.5f, H - 214f, w, 56f);
            Fill(r, new Color(0f, 0f, 0f, 0.65f));
            Label(new Rect(r.x + 20f, r.y + 6f, r.width - 40f, r.height), "<i>" + subtitle + "</i>", sMsg, Warm);
        }

        void DrawMessages()
        {
            float now = Time.unscaledTime;
            msgs.RemoveAll(m => m.until < now);
            float bottom = H - 230f;
            if (msgs.Count > 0)
            {
                var m = msgs[msgs.Count - 1];
                float a = Mathf.Clamp01((m.until - now) / 0.6f);
                float w = Mathf.Min(1150f, W - 80f);
                float h = Mathf.Max(40f, sMsg.CalcHeight(new GUIContent(m.text), w - 24f) + 12f);
                // Un aviso completo ocupa una banda propia, entre sonidos y diálogo.
                var r = new Rect((W - w) * 0.5f, bottom - h, w, h);
                Fill(r, new Color(0f, 0f, 0f, 0.45f * a));
                Label(r, m.text, sMsg, new Color(m.color.r, m.color.g, m.color.b, a));
            }
        }

        void DrawCarrete(GameDirector d)
        {
            var ev = d.Player.Evidence;
            if (ev == null || !ev.UIOpen) return;
            Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, 0.72f));
            Label(new Rect(0f, 40f, W, 40f), "<b>CARRETE</b>  ·  vuelta " + d.Lap, sSub, Ink);
            var p = ev.Current;
            if (p == null)
            {
                Label(new Rect(0f, H * 0.45f, W, 40f), "No hay fotos. Levanta la cámara con clic derecho (o Q) y dispara con clic izquierdo.", sCenter, Ink);
                Label(new Rect(0f, H - 90f, W, 30f), "[Tab] / [Esc] cerrar", sCenter, Dim);
                return;
            }
            float maxW = W * 0.56f, maxH = H * 0.56f;
            float aspect = p.tex != null ? (float)p.tex.width / Mathf.Max(1, p.tex.height) : 16f / 9f;
            float pw = maxW, ph = pw / aspect;
            if (ph > maxH) { ph = maxH; pw = ph * aspect; }
            var r = new Rect((W - pw) * 0.5f, 110f, pw, ph);
            Fill(new Rect(r.x - 10f, r.y - 10f, r.width + 20f, r.height + 70f), new Color(0.9f, 0.86f, 0.76f, 0.95f));
            if (p.tex != null) GUI.DrawTexture(r, p.tex, ScaleMode.ScaleToFit);
            var dark = new Color(0.1f, 0.1f, 0.1f);
            string where = p.roomTaken != null ? p.roomTaken.displayName : "?";
            string head = "Foto " + (ev.Index + 1) + "/" + ev.Photos.Count + "  ·  tomada en " + where + "  ·  hace " + GameDirector.FormatTime(Time.time - p.time) +
                          (p.lap != d.Lap ? "  ·  vuelta " + p.lap : "");
            Label(new Rect(r.x, r.y + r.height + 6f, r.width, 28f), head, sCenter, dark);
            string sub = p.quality == PhotoQuality.SinObjetivo ? "Sin objetivo (foto de referencia)" : "«" + p.subjectName + "» — " + EvidenceCamera.QualityText(p.quality);
            if (p.reported) sub += "  ·  REPORTADA";
            Label(new Rect(r.x, r.y + r.height + 32f, r.width, 28f), sub, sCenter, p.quality == PhotoQuality.Nitida ? new Color(0.1f, 0.4f, 0.15f) : new Color(0.5f, 0.3f, 0.05f));

            float y = r.y + r.height + 80f;
            switch (ev.State)
            {
                case CarreteState.Fotos:
                    Label(new Rect(0f, y, W, 30f), "[A] / [D] cambiar de foto  ·  [Enter] reportar esta foto  ·  [Tab] / [Esc] cerrar", sCenter, Ink);
                    break;
                case CarreteState.Habitacion:
                {
                    Label(new Rect(0f, y, W, 30f), "<b>¿En qué habitación está?</b>   ([Esc] atrás)", sCenter, Warm);
                    float colW = 230f, x0 = (W - colW * Mathf.Min(3, ev.ReportRooms.Count)) * 0.5f;
                    for (int i = 0; i < ev.ReportRooms.Count && i < 9; i++)
                        Label(new Rect(x0 + (i % 3) * colW, y + 36f + (i / 3) * 32f, colW, 30f), "<b>[" + (i + 1) + "]</b> " + ev.ReportRooms[i].displayName, sText, Ink);
                    break;
                }
                case CarreteState.Categoria:
                    Label(new Rect(0f, y, W, 30f), "<b>¿Qué ha cambiado?</b>  ·  " + (ev.ChosenRoom != null ? ev.ChosenRoom.displayName : "") + "   ([Esc] atrás)", sCenter, Warm);
                    for (int i = 0; i < 5; i++)
                    {
                        var c = (AnomalyCategory)i;
                        Label(new Rect(W * 0.5f - 330f, y + 36f + i * 30f, 660f, 30f),
                              "<b>[" + (i + 1) + "] " + AnomalyText.Name(c) + "</b>  <color=#9a917f>— " + AnomalyText.Hint(c) + "</color>", sText, Ink);
                    }
                    break;
            }
        }

        void DrawPause(GameDirector d)
        {
            Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, 0.8f));
            Label(new Rect(0f, 65f, W, 80f), "PAUSA", sTitle, Ink);
            float bw = 420f, x = (W - bw) * 0.5f;
            float y = 150f;

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
            GameSettings.VoiceVolume = VolumeSlider(x, ref y, bw, "Voces", GameSettings.VoiceVolume);
            Label(new Rect(x, y, bw, 28f), "Campo de visión: " + GameSettings.FieldOfView.ToString("0") + "°", sSmall, Ink);
            GameSettings.FieldOfView = GUI.HorizontalSlider(new Rect(x, y + 28f, bw, 20f), GameSettings.FieldOfView, 55f, 100f);
            if (d.Player.Evidence == null || !d.Player.Evidence.Raised) d.Player.Cam.fieldOfView = GameSettings.FieldOfView;
            y += 54f;
            Label(new Rect(x, y, bw, 28f), "Resolución de render (altura máxima)", sSmall, Ink);
            GameSettings.SetResolutionChoice(GUI.Toolbar(new Rect(x, y + 28f, bw, 34f), GameSettings.ResolutionChoice, GameSettings.ResolutionLabels, sButton));
            y += 70f;
            GameSettings.Apply();
            GameSettings.InvertY = GUI.Toggle(new Rect(x, y, bw, 34f), GameSettings.InvertY, "  Invertir eje vertical", sToggle);
            y += 40f;
            GameSettings.Captions = GUI.Toggle(new Rect(x, y, bw, 34f), GameSettings.Captions, "  Subtítulos de sonidos", sToggle);
            y += 40f;
            GameSettings.Effects = GUI.Toggle(new Rect(x, y, bw, 34f), GameSettings.Effects, "  Viñeta suave", sToggle);
            y += 40f;
            GameSettings.CameraBob = GUI.Toggle(new Rect(x, y, bw, 34f), GameSettings.CameraBob, "  Balanceo de cámara", sToggle);
            y += 40f;
            GameSettings.Grain = GUI.Toggle(new Rect(x, y, bw, 34f), GameSettings.Grain, "  Grano de película", sToggle);
            y += 40f;
            if (Application.isEditor || Debug.isDebugBuild)
                d.showDebug = GUI.Toggle(new Rect(x, y, bw, 34f), d.showDebug, "  [dev] Contadores de prueba", sToggle);
            y += 54f;

            if (GUI.Button(new Rect(x, y, bw, 56f), "Continuar", sButton)) d.Resume();
            if (GUI.Button(new Rect(x, y + 70f, bw, 56f), "Reiniciar vuelta " + d.Lap, sButton)) d.Restart();
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
            Label(new Rect(0f, 170f, W, 90f), "HAS SALIDO", sTitle, Good);
            Label(new Rect(0f, 270f, W, 40f), "Fuera es de noche y llueve. A tu espalda, alguien cierra la puerta con mucho cuidado.", sSub, Ink);
            Label(new Rect(0f, 310f, W, 40f), "«Vuelve otra noche», dice la casa. No suena como una pregunta.", sSub, Dim);
            string stats = "Tiempo: " + GameDirector.FormatTime(d.PlayTime) + "    ·    Vueltas: " + d.LapCount +
                           "    ·    Reportes correctos: " + d.CorrectReports + "    ·    Errores: " + d.WrongReports;
            Label(new Rect(0f, 380f, W, 34f), stats, sCenter, Dim);
            float bw = 320f;
            Label(new Rect(0f, 430f, W, 34f), "FIN", sCenter, Ink);
            if (GUI.Button(new Rect((W - bw) * 0.5f, 500f, bw, 56f), "Jugar de nuevo  [Enter]", sButton)) d.RestartFromBeginning();
        }

        void DrawDefeat(GameDirector d)
        {
            float t = Time.unscaledTime - d.DefeatTime;
            Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, Mathf.Clamp01(t / 1.8f)));
            if (t < 1.8f) return;
            float a = Mathf.Clamp01((t - 1.8f) / 0.8f);
            Label(new Rect(0f, 240f, W, 90f), "DESAPARICIÓN", sTitle, new Color(Danger.r, Danger.g, Danger.b, a));
            Label(new Rect(0f, 340f, W, 40f), MonsterName(d) + " te encontró en la vuelta " + d.Lap + ". Tu informe queda incompleto.", sSub, new Color(Ink.r, Ink.g, Ink.b, a));
            Label(new Rect(0f, 410f, W, 34f), "Anomalías de la vuelta: " + d.Anomalies.ReportedCount + "/" + d.Anomalies.Total +
                  "    ·    Tiempo: " + GameDirector.FormatTime(d.PlayTime), sCenter, new Color(Dim.r, Dim.g, Dim.b, a));
            float bw = 360f;
            if (a > 0.5f && GUI.Button(new Rect((W - bw) * 0.5f, 500f, bw, 56f), "Reintentar la vuelta " + d.Lap + "  [R]", sButton)) d.Restart();
            if (a > 0.5f && GUI.Button(new Rect((W - bw) * 0.5f, 570f, bw, 56f), "Desde el principio", sButton)) d.RestartFromBeginning();
        }
    }
}

