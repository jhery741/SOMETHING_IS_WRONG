using System.Collections.Generic;
using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>
    /// LA PRESENCIA DE EL CIEGO: cómo "juega" con el jugador antes de perseguirlo.
    ///
    /// ATENCIÓN (0–5) — cuánto se ha fijado en ti. Sube y baja SOLO por estas causas:
    ///   +1    reporte equivocado (objeto normal, habitación o categoría incorrecta)
    ///   +1    anomalía sin reportar demasiado tiempo (+2 si era crítica)
    ///   +1    mentirle (se comprueba con el estado real: dónde estás, si hay anomalías activas que ya fotografiaste)
    ///   +0.5  respuesta evasiva o negativa · +0.25 callar · +0.25 correr mientras habla (máx. una vez cada 30 s)
    ///   −0.5  responder la verdad (pero hablar hace ruido: 4 m en tu posición)
    ///   +0.3  por minuto mientras quedan anomalías por resolver (presión lenta)
    ///   +0.75 por reporte correcto en la vuelta final (cada acierto lo acerca)
    ///   −0.25 reporte correcto (vueltas 1–3) · −1 al cerrar una vuelta
    ///   Cada vuelta tiene un mínimo (vuelta 2: 1 · vuelta 3: 2 · vuelta 4: 3) y un tope de etapa (vuelta 1 nunca pasa de
    ///   "verbal"; vuelta 2 nunca pasa de "acercamiento"). Así, aunque no falles, la presencia escala de vuelta en vuelta.
    ///
    /// ETAPAS (según la atención)
    ///   0 DISTANTE        (&lt;1)  golpes y crujidos en habitaciones lejanas; alguna palabra suelta.
    ///   1 VERBAL          (&lt;2)  habla desde la habitación de al lado, reacciona a lo que haces, hace preguntas.
    ///   2 ALERTA          (&lt;3)  más frecuente, sabe en qué habitación estás, parpadeos y portazos.
    ///   3 ACERCAMIENTO    (&lt;4)  APARECE físicamente en una puerta cercana fuera de tu vista, se queda mirando y se va.
    ///                             Si te acercas a menos de 3 m o haces ruido, deja de jugar y te busca.
    ///   4 FÍSICO          (≥4)  despierta y caza con las reglas de El Ciego (oído + proximidad):
    ///                             BÚSQUEDA → PERSECUCIÓN → ATAQUE; si te pierde: PÉRDIDA; en las vueltas 2–3 se RETIRA
    ///                             tras 30 s sin volver a perseguirte (la atención baja a 2.5). En la vuelta 4 no se retira.
    ///
    /// PAUSAS: tras cada línea hay un silencio mínimo de 8–14 s; las preguntas esperan tu respuesta 9 s ([1] [2] [3]).
    /// Todas las líneas son grabaciones (VoiceLineSet); si falta un audio suena un susurro provisional con su subtítulo.
    /// </summary>
    public class MonsterPresence : MonoBehaviour
    {
        public static readonly string[] StageNames = { "Distante", "Interacción verbal", "Alerta", "Acercamiento", "Físico" };

        public VoiceLineSet voices;
        public const float CreepPerMinute = 0.3f;
        public float Attention { get; private set; }
        public int Stage { get; private set; }
        public int StageCap { get; private set; }
        public float AttentionFloor { get; private set; }
        public bool Asking { get { return question != null; } }
        public string QuestionText { get { return question != null ? question.text : ""; } }
        public readonly List<string> OptionLabels = new List<string>();
        public float QuestionTimeLeft { get { return Mathf.Max(0f, questionDeadline - Time.time); } }
        public readonly HashSet<string> MissingClips = new HashSet<string>();
        public string LastLine { get; private set; }

        GameDirector director;
        LevelRoot level;
        PlayerController player;
        MonsterBase monster;
        AudioSource voice;
        AudioLowPassFilter lowpass;
        VoiceLine question;
        float questionDeadline, nextBeat, silenceUntil, nextQuestion, voiceStopAt;
        readonly Dictionary<VoiceContext, float> cooldown = new Dictionary<VoiceContext, float>();
        readonly Dictionary<string, int> lastUsed = new Dictionary<string, int>();
        int lineCounter;
        Room lastPlayerRoom;
        bool wasHidden, wasInRefuge, physical, huntRetreat;
        float runTime, idleTime, calmPhysical, lastRunPenalty = -99f;
        bool greeted;

        public string StateLabel
        {
            get
            {
                if (monster != null && monster.IsActive)
                {
                    switch (monster.StateName)
                    {
                        case "Watch": return "Acercamiento";
                        case "Emerging": return "Despertar";
                        case "Chase": return "Persecución";
                        case "Search": return "Pérdida";
                        case "Attack": return "Ataque";
                        case "Retreat": return "Retirada";
                        default: return "Búsqueda";
                    }
                }
                return StageNames[Mathf.Clamp(Stage, 0, 4)];
            }
        }

        public void Setup(GameDirector d, LevelRoot l, PlayerController p, MonsterBase m, VoiceLineSet set)
        {
            director = d; level = l; player = p; monster = m;
            voices = set != null ? set : VoiceLineSet.CreateDefault();
            var go = new GameObject("VozElCiego");
            go.transform.SetParent(transform, false);
            voice = go.AddComponent<AudioSource>();
            voice.spatialBlend = 1f;
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.minDistance = 1.5f;
            voice.maxDistance = 26f;
            voice.dopplerLevel = 0f;
            lowpass = go.AddComponent<AudioLowPassFilter>();
            lowpass.cutoffFrequency = 1400f;
            lowpass.enabled = false;
            if (monster != null) monster.StateEvent += OnMonsterEvent;
            int missing = 0;
            foreach (var line in voices.lines) if (ClipFor(line) == null) { missing++; MissingClips.Add(line.id); }
            if (missing > 0 && (Application.isEditor || Debug.isDebugBuild))
                Debug.Log("[Voz] Faltan " + missing + "/" + voices.lines.Count + " grabaciones (Resources/Voz/<id>): se usa susurro provisional + subtítulo. " +
                          string.Join(", ", new List<string>(MissingClips).ToArray()));
        }

        static AudioClip ClipFor(VoiceLine l)
        {
            if (l.clip != null) return l.clip;
            return Resources.Load<AudioClip>("Voz/" + l.id);
        }

        // ------------------------------------------------------------------ vueltas
        public void BeginLap(int lap, int stageCap, float floor)
        {
            StageCap = stageCap;
            AttentionFloor = floor;
            Attention = Mathf.Max(Attention, floor);
            RecomputeStage(false);
            nextBeat = Time.time + (lap == 1 ? 30f : 18f);
            nextQuestion = Time.time + 40f;
        }

        public void OnLapClosed()
        {
            CancelQuestion();
            Attention = Mathf.Max(0f, Attention - 1f);
            if (monster != null && monster.IsActive) monster.Hide();
            physical = false;
            Say(VoiceContext.CierreVuelta, true, NearPlayerVoicePoint(), true);
        }

        public void OnEscapeStart()
        {
            CancelQuestion();
            StageCap = 4;
            Attention = 5f;
            Say(VoiceContext.Final, true, NearPlayerVoicePoint(), true);
            RecomputeStage(true);
        }

        public void AddAttention(float amount, string reason)
        {
            float before = Attention;
            Attention = Mathf.Clamp(Attention + amount, AttentionFloor, 5f);
            if (Application.isEditor || Debug.isDebugBuild)
                Debug.Log("[Presencia] Atención " + before.ToString("0.00") + " → " + Attention.ToString("0.00") + " (" + reason + ")");
            RecomputeStage(true);
        }

        void RecomputeStage(bool announce)
        {
            int s = Attention < 1f ? 0 : Attention < 2f ? 1 : Attention < 3f ? 2 : Attention < 4f ? 3 : 4;
            s = Mathf.Min(s, StageCap);
            if (s == Stage && !(s == 4 && !physical)) return;
            int old = Stage;
            Stage = s;
            if (Application.isEditor || Debug.isDebugBuild) Debug.Log("[Presencia] Etapa " + StageNames[old] + " → " + StageNames[s]);
            if (s == 4 && !physical && director.IsInvestigating) { physical = true; calmPhysical = 0f; director.BeginHunt(); }
            if (announce && s > old && s >= 2 && s < 4) director.FlickerAll(1.2f);
        }

        // ------------------------------------------------------------------ reacciones a acciones concretas
        public void OnPlayerPhoto() { if (Random.value < 0.35f) React(VoiceContext.Foto); }
        public void OnPlayerDoor(Door d, bool open) { if (Random.value < 0.25f) React(VoiceContext.Puerta); }

        public void OnReport(bool correct)
        {
            React(correct ? VoiceContext.ReporteBien : VoiceContext.ReporteMal);
            if (correct && director.Lap < 4) AddAttention(-0.25f, "reporte correcto");
            if (correct && director.Lap >= 4) AddAttention(0.75f, "vuelta final: cada reporte lo acerca");
        }

        void OnMonsterEvent(string e)
        {
            switch (e)
            {
                case "persecucion": Say(VoiceContext.Persecucion, true, monster.transform.position + Vector3.up * 1.8f, false); calmPhysical = 0f; break;
                case "perdida": Say(VoiceContext.Perdida, true, monster.transform.position + Vector3.up * 1.8f, false); break;
                case "retirada": Say(VoiceContext.Retirada, true, monster.transform.position + Vector3.up * 1.8f, false); break;
                case "oculto":
                    // Solo una retirada de CAZA devuelve la presencia a "alerta"; acabar una aparición no cancela nada.
                    if (physical && huntRetreat && director.IsInvestigating && director.Lap < 4)
                    {
                        huntRetreat = false;
                        physical = false;
                        Attention = Mathf.Max(AttentionFloor, 2.5f);
                        Stage = 2;
                        silenceUntil = Time.time + 12f;
                        nextBeat = Time.time + 20f;
                    }
                    break;
            }
        }

        bool React(VoiceContext c)
        {
            if (Stage < 1 || question != null) return false;
            if (Time.time < silenceUntil - 6f) return false;   // las reacciones pueden recortar la pausa, pero no pisar otra línea
            if (voice.isPlaying) return false;
            return Say(c, false, NearPlayerVoicePoint(), true);
        }

        // ------------------------------------------------------------------ bucle
        void Update()
        {
            if (director == null || player == null || !director.IsInvestigating || player.IsDead) { StopIfDone(); return; }
            StopIfDone();
            if (director.Paused) return;

            if (question != null)
            {
                for (int i = 0; i < OptionLabels.Count; i++)
                    if (!director.CarreteOpen && (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))) { Answer(i); break; }
                if (question != null && Time.time > questionDeadline) Answer(OptionLabels.Count - 1);   // sin respuesta = callar
            }

            WatchPlayer();
            // Presión lenta: mientras hay anomalías por resolver, cada minuto sube 0.3 (la casa no espera eternamente).
            if (director.Phase == GamePhase.Investigation && !physical && Attention < 5f)
            {
                Attention = Mathf.Min(5f, Attention + CreepPerMinute / 60f * Time.deltaTime);
                RecomputeStage(true);
            }
            if (monster != null && monster.IsActive) { PhysicalTick(); return; }
            // En caza física el monstruo no puede quedarse oculto (p. ej. si una aparición terminó justo al empezar la caza).
            if (physical && !huntRetreat && monster != null && !monster.IsActive && director.IsInvestigating) monster.Hunt(director.FarSpawnPoint(), player.transform.position);
            if (Time.time >= nextBeat && Time.time >= silenceUntil && question == null) Beat();
        }

        void StopIfDone()
        {
            if (voice != null && voice.isPlaying && voice.loop && Time.time > voiceStopAt) voice.Stop();
        }

        void WatchPlayer()
        {
            var room = director.PlayerRoom;
            // correr cuando ya habla contigo
            runTime = player.IsRunning ? runTime + Time.deltaTime : 0f;
            if (runTime > 1.5f && Stage >= 1 && Time.time - lastRunPenalty > 30f && !(monster != null && monster.IsActive))
            {
                lastRunPenalty = Time.time;
                React(VoiceContext.Correr);
                AddAttention(0.25f, "correr");
            }
            idleTime = player.CurrentSpeed < 0.1f && !player.IsHidden ? idleTime + Time.deltaTime : 0f;
            if (idleTime > 40f) { idleTime = 0f; React(VoiceContext.Quieto); }
            if (player.IsHidden && !wasHidden && Random.value < 0.5f) React(VoiceContext.Esconderse);
            wasHidden = player.IsHidden;
            bool inRefuge = room != null && room.isSafe;
            if (inRefuge && !wasInRefuge && Stage >= 2) React(VoiceContext.Refugio);
            wasInRefuge = inRefuge;
            if (room != lastPlayerRoom && room != null && !room.isSafe && Stage >= 2 && Random.value < 0.15f) SayRoom(room);
            lastPlayerRoom = room;
        }

        void PhysicalTick()
        {
            // Retirada (vueltas 2–3): 30 s cazando sin volver a perseguirte.
            if (monster.IsChasing) calmPhysical = 0f;
            else if (!monster.IsAppearing) calmPhysical += Time.deltaTime;
            if (physical && director.Lap < 4 && calmPhysical > 30f && monster.IsHunting && !monster.IsAppearing)
            {
                calmPhysical = -999f;
                huntRetreat = true;
                if (Application.isEditor || Debug.isDebugBuild) Debug.Log("[Presencia] 30 s cazando sin perseguirte: se retira.");
                monster.Retreat();
            }
        }

        void Beat()
        {
            float r = Random.value;
            switch (Stage)
            {
                case 0:
                    nextBeat = Time.time + Random.Range(35f, 55f);
                    if (!greeted && director.Lap == 1 && director.Anomalies.ReportedCount > 0) { greeted = true; Say(VoiceContext.Saludo, false, FarVoicePoint(), true); }
                    else if (r < 0.25f) Say(VoiceContext.Ambiente, false, FarVoicePoint(), true);
                    else Knock(FarVoicePoint(), r < 0.6f ? "knock" : "creak");
                    break;
                case 1:
                    nextBeat = Time.time + Random.Range(25f, 40f);
                    if (!greeted) { greeted = true; Say(VoiceContext.Saludo, false, NearPlayerVoicePoint(), true); }
                    else if (r < 0.3f && Time.time >= nextQuestion) Ask();
                    else if (r < 0.85f) Say(VoiceContext.Ambiente, false, NearPlayerVoicePoint(), true);
                    else Knock(NearPlayerVoicePoint(), "knock");
                    break;
                case 2:
                    nextBeat = Time.time + Random.Range(15f, 25f);
                    if (r < 0.25f && Time.time >= nextQuestion) Ask();
                    else if (r < 0.45f && director.PlayerRoom != null && !director.PlayerRoom.isSafe) SayRoom(director.PlayerRoom);
                    else { Say(VoiceContext.Ambiente, false, NearPlayerVoicePoint(), true); director.WarningSignal(); }
                    break;
                case 3:
                    nextBeat = Time.time + Random.Range(14f, 22f);
                    if (r < 0.45f && TryAppear()) break;
                    if (r < 0.65f && Time.time >= nextQuestion) Ask();
                    else { Say(VoiceContext.Ambiente, false, NearPlayerVoicePoint(), true); director.WarningSignal(); }
                    break;
                default:
                    nextBeat = Time.time + 10f;
                    break;
            }
        }

        bool TryAppear()
        {
            if (monster == null || director.PlayerRoom == null || director.PlayerRoom.isSafe) return false;
            // Punto de aproximación de una puerta de una habitación vecina, a 4–9 m y fuera de la vista del jugador.
            var options = new List<Vector3>();
            foreach (var d in level.doors)
            {
                if (d == null || !d.Passable) continue;
                foreach (var room in new[] { d.roomA, d.roomB })
                {
                    if (room == null || room.isSafe || room == director.PlayerRoom) continue;
                    Vector3 p = d.ApproachFor(room);
                    float dist = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(player.transform.position.x, 0f, player.transform.position.z));
                    if (dist < 4f || dist > 9f) continue;
                    if (Visibility.IsVisible(player.Cam, p + Vector3.up * 1.2f, 0.4f)) continue;
                    options.Add(p);
                }
            }
            if (options.Count == 0) return false;
            Vector3 at = options[Random.Range(0, options.Count)];
            monster.Appear(at, player.transform.position, Random.Range(5f, 8f));
            Say(VoiceContext.Aparicion, true, at + Vector3.up * 1.8f, false);
            if (Application.isEditor || Debug.isDebugBuild) Debug.Log("[Presencia] Aparición en " + at);
            return true;
        }

        void Knock(Vector3 p, string id)
        {
            SoundBank.PlayAt(id, p, 0.7f, 22f, Random.Range(0.85f, 1.05f), id == "knock" ? "[golpes tras la pared]" : "[crujido]");
            silenceUntil = Time.time + 6f;
        }

        // ------------------------------------------------------------------ preguntas
        void Ask()
        {
            var q = Pick(VoiceContext.Pregunta, null);
            if (q == null || q.options.Count == 0) { Say(VoiceContext.Ambiente, false, NearPlayerVoicePoint(), true); return; }
            float len = Speak(q, NearPlayerVoicePoint(), true);
            question = q;
            questionDeadline = Time.time + len + 9f;
            nextQuestion = Time.time + 75f;
            OptionLabels.Clear();
            string here = director.PlayerRoom != null ? director.PlayerRoom.displayName.ToLower() : "la casa";
            foreach (var o in q.options) OptionLabels.Add(o.label.Replace("{habitacion}", ArticleFor(director.PlayerRoom) + here));
        }

        static string ArticleFor(Room r)
        {
            if (r == null) return "";
            switch (r.id) { case "sala": case "cocina": return "la "; default: return "el "; }
        }

        void CancelQuestion() { question = null; OptionLabels.Clear(); }

        void Answer(int i)
        {
            var q = question;
            string said = i >= 0 && i < OptionLabels.Count ? OptionLabels[i] : "";
            CancelQuestion();
            if (q == null || i < 0 || i >= q.options.Count) return;
            var o = q.options[i];
            bool spoken = o.kind != OptionKind.Silencio;
            if (spoken) NoiseSystem.Emit(player.transform.position, 4f, true);   // hablar en voz alta hace ruido
            HUD.Show("Tú: " + said, 2.5f, new Color(0.8f, 0.85f, 1f));
            bool? truth = null;
            switch (q.check)
            {
                case QuestionCheck.DondeEstas: if (o.kind == OptionKind.Verdad) truth = true; break;
                case QuestionCheck.HasVistoAlgo:
                    bool seen = director.PlayerHasPhotographedActiveAnomaly();
                    if (o.kind == OptionKind.Si) truth = seen; else if (o.kind == OptionKind.No) truth = !seen;
                    break;
            }
            float delta = truth == false ? o.attentionIfLie : o.attention;
            AddAttention(delta, "respuesta " + o.kind + (truth == false ? " (mentira)" : ""));
            silenceUntil = 0f;
            Vector3 vp = NearPlayerVoicePoint();
            if (!spoken) Say(VoiceContext.RespuestaSilencio, true, vp, true);
            else if (truth == false) Say(VoiceContext.RespuestaMentira, true, vp, true);
            else if (q.check == QuestionCheck.Jugar && o.kind == OptionKind.Si) { Say(VoiceContext.Juego, true, vp, true); StartCoroutine(ThreeKnocks()); }
            else if (truth == true || o.kind == OptionKind.Si) Say(VoiceContext.RespuestaVerdad, true, vp, true);
            // Decir dónde estás lo atrae: si está despierto investiga ese punto.
            if (q.check == QuestionCheck.DondeEstas && o.kind == OptionKind.Verdad && monster != null && monster.IsActive) monster.Pulse(player.transform.position);
        }

        System.Collections.IEnumerator ThreeKnocks()
        {
            yield return new WaitForSeconds(3f);
            Vector3 p = FarVoicePoint();
            for (int k = 0; k < 3; k++) { SoundBank.PlayAt("knock", p, 0.8f, 26f, 1f, "[tres golpes]"); yield return new WaitForSeconds(0.7f); }
        }

        // ------------------------------------------------------------------ voz
        void SayRoom(Room r)
        {
            foreach (var l in voices.lines)
                if (l.context == VoiceContext.Habitacion && l.roomId == r.id && Stage >= l.minStage) { Speak(l, NearPlayerVoicePoint(), true); return; }
        }

        bool Say(VoiceContext c, bool force, Vector3 at, bool throughWall)
        {
            float cd;
            if (!force && cooldown.TryGetValue(c, out cd) && Time.time < cd) return false;
            var l = Pick(c, null);
            if (l == null) return false;
            cooldown[c] = Time.time + (c == VoiceContext.Ambiente ? 0f : 40f);
            Speak(l, at, throughWall);
            return true;
        }

        VoiceLine Pick(VoiceContext c, string roomId)
        {
            VoiceLine best = null;
            int bestUse = int.MaxValue;
            var candidates = new List<VoiceLine>();
            foreach (var l in voices.lines)
            {
                if (l.context != c) continue;
                if (Stage < l.minStage || Stage > l.maxStage) continue;
                if (roomId != null && l.roomId != roomId) continue;
                candidates.Add(l);
            }
            if (candidates.Count == 0) foreach (var l in voices.lines) if (l.context == c) candidates.Add(l);
            // La menos usada recientemente (con algo de azar): no repite la misma frase seguida.
            foreach (var l in candidates)
            {
                int u; lastUsed.TryGetValue(l.id, out u);
                int score = u * 10 + Random.Range(0, 5);
                if (score < bestUse) { bestUse = score; best = l; }
            }
            return best;
        }

        float Speak(VoiceLine l, Vector3 at, bool throughWall)
        {
            lastUsed[l.id] = ++lineCounter;
            var clip = ClipFor(l);
            bool provisional = clip == null;
            if (provisional) clip = SoundBank.Get("whisper");
            float dur = Mathf.Max(provisional ? 0f : clip.length, 1.3f + l.text.Length * 0.055f);
            voice.transform.position = at;
            voice.clip = clip;
            voice.loop = provisional;
            voice.pitch = provisional ? 0.8f : 1f;
            voice.volume = (provisional ? 0.55f : 1f) * GameSettings.VoiceVolume;
            Room vr = level.RoomAt(at), pr = director.PlayerRoom;
            lowpass.enabled = throughWall && vr != pr;
            voice.Play();
            voiceStopAt = Time.time + dur;
            silenceUntil = Time.time + dur + Random.Range(8f, 14f);
            LastLine = l.id;
            HUD.Subtitle(voices.speakerName + ": «" + l.text + "»", dur + 1.2f);
            HUD.Caption(vr != null && vr != pr ? "[voz desde " + ArticleFor(vr) + vr.displayName.ToLower() + "]" : "[voz]", at, 30f);
            if (Application.isEditor || Debug.isDebugBuild)
                Debug.Log("[Voz] " + l.id + (provisional ? " (PROVISIONAL)" : "") + " · etapa " + StageNames[Stage] + " · desde " + (vr != null ? vr.displayName : "?") + ": " + l.text);
            return dur;
        }

        /// <summary>Punto de voz en una habitación vecina, junto a la puerta que la une con la tuya (se oye "tras la pared").</summary>
        Vector3 NearPlayerVoicePoint()
        {
            var pr = director.PlayerRoom;
            var list = new List<Vector3>();
            if (pr != null)
                foreach (var d in level.doors)
                {
                    if (d == null || !d.Connects(pr)) continue;
                    var other = d.Other(pr);
                    if (other == null || other.isSafe) continue;
                    Vector3 a = d.ApproachFor(other);
                    Vector3 away = (a - d.transform.position); away.y = 0f;
                    list.Add(a + away.normalized * 0.8f + Vector3.up * 1.6f);
                }
            if (list.Count == 0) return FarVoicePoint();
            return list[Random.Range(0, list.Count)];
        }

        Vector3 FarVoicePoint()
        {
            Room best = null; int bd = -1;
            foreach (var r in level.rooms)
            {
                if (r == null || r.isSafe || r == director.PlayerRoom) continue;
                int d = level.GraphDistance(director.PlayerRoom, r) * 10 + Random.Range(0, 8);
                if (d > bd) { bd = d; best = r; }
            }
            return (best != null ? best.Center : player.transform.position + Vector3.forward * 6f) + Vector3.up * 1.6f;
        }

        void OnDestroy() { if (monster != null) monster.StateEvent -= OnMonsterEvent; }
    }
}
