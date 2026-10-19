using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SomethingIsWrong
{
    public enum GamePhase { Intro, Observation, Investigation, Awakening, Active, Victory, Defeat }

    /// <summary>
    /// Bucle principal (documento de concepto, secciones 3 y 5):
    /// Explorar/Observar → Inspeccionar → Reportar → Gestionar amenaza (Latente → Advertencia)
    /// → Despertar → El Ciego permanece activo en el piso (Acecho / Búsqueda / Cacería) → Descender.
    /// </summary>
    public class GameDirector : MonoBehaviour
    {
        public static GameDirector Instance { get; private set; }

        public GamePhase Phase { get; private set; }
        public bool Paused { get; private set; }
        public int Errors { get; private set; }
        public float PhaseTimer { get; private set; }
        public float PlayTime { get; private set; }
        public float DefeatTime { get; private set; }
        public Room PlayerRoom { get; private set; }
        public int CluesFound { get; private set; }
        public int CluesTotal { get { return Level != null ? Level.notes.Count : 0; } }
        public int CorrectReports { get; private set; }
        public int WrongReports { get; private set; }

        [Header("Reglas")]
        public int errorThreshold = 4;
        public float observationTime = 45f;
        public bool showDebug = false;

        [Header("Piso")]
        public int floorNumber = 1;
        public string floorTitle = "PISO 1";
        public string nextSceneName = "";
        public string nextFloorTeaser = "";

        /// <summary>¿La escena del siguiente piso existe en Build Settings?</summary>
        public bool NextSceneAvailable
        {
            get { return !string.IsNullOrEmpty(nextSceneName) && Application.CanStreamedLevelBeLoaded(nextSceneName); }
        }

        public LevelRoot Level { get; private set; }
        public PlayerController Player { get; private set; }
        public MonsterBase Monster { get; private set; }
        public AnomalyManager Anomalies { get; private set; }

        float nextAmbient, nextNatural, nextHeartbeat;
        int correctStreak;
        float moveAcc, runAcc;
        bool runPenalized, tipThrowShown, tipFirstCorrect;

        public bool IsPlaying
        {
            get
            {
                if (Paused) return false;
                return Phase == GamePhase.Observation || Phase == GamePhase.Investigation ||
                       Phase == GamePhase.Awakening || Phase == GamePhase.Active;
            }
        }

        public bool MonsterAwake { get { return Phase == GamePhase.Awakening || Phase == GamePhase.Active; } }

        /// <summary>El refugio (rellano de la escalera) detiene a los monstruos en la puerta y no pueden atrapar dentro.</summary>
        public bool RefugeProtects { get { return true; } }

        public string ThreatLabel
        {
            get
            {
                switch (Phase)
                {
                    case GamePhase.Awakening: return "Despertar";
                    case GamePhase.Active:
                        return Monster == null ? "Activo" : Monster.ThreatLabel;
                }
                return Errors >= errorThreshold - 1 ? "Advertencia" : "Latente";
            }
        }

        void Awake() { Instance = this; }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Time.timeScale = 1f;
        }

        public void Configure(LevelRoot level, PlayerController player, MonsterBase monster, AnomalyManager anomalies)
        {
            Level = level;
            Player = player;
            Monster = monster;
            Anomalies = anomalies;
        }

        public void EnterIntro()
        {
            Phase = GamePhase.Intro;
            Time.timeScale = 0f;
            SetCursor(false);
        }

        public void StartGame()
        {
            Time.timeScale = 1f;
            Phase = GamePhase.Observation;
            PhaseTimer = observationTime;
            SetCursor(true);
            nextAmbient = Time.time + 25f;
            nextNatural = Time.time + 20f;
            HUD.Show("EXPLORA Y MEMORIZA. Fotografía cada habitación con [Q]: luego podrás comparar con [Tab].", 7f);
        }

        static void SetCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void Update()
        {
            if (Level == null || Player == null) return;
            var r = Level.RoomAt(Player.transform.position);
            if (r != null) PlayerRoom = r;

            switch (Phase)
            {
                case GamePhase.Intro:
                    if (GameInput.Down(GKey.Enter) || GameInput.Down(GKey.Space) || GameInput.MouseDown(0)) StartGame();
                    return;
                case GamePhase.Victory:
                    if (GameInput.Down(GKey.Enter) && NextSceneAvailable) LoadNextFloor();
                    else if (GameInput.Down(GKey.Enter) && IsFinalFloor) BackToFirstFloor();
                    else if (GameInput.Down(GKey.R) || GameInput.Down(GKey.Enter)) Restart();
                    return;
                case GamePhase.Defeat:
                    if (GameInput.Down(GKey.R) || GameInput.Down(GKey.Enter)) Restart();
                    return;
            }

            HandlePause();
            if (Paused) return;
            if ((Application.isEditor || Debug.isDebugBuild) && Input.GetKeyDown(KeyCode.F9)) showDebug = !showDebug;   // solo desarrollo
            // solo desarrollo: F10 suma un error (para probar el despertar del monstruo sin esperar al ciclo completo)
            if ((Application.isEditor || Debug.isDebugBuild) && Input.GetKeyDown(KeyCode.F10) && Phase != GamePhase.Observation) AddError("[dev] Error forzado.");
            // solo desarrollo: F8 da el piso por superado (probar transiciones Piso 1 → 2 → 3 → final)
            if ((Application.isEditor || Debug.isDebugBuild) && Input.GetKeyDown(KeyCode.F8) && Phase != GamePhase.Observation) OnStairReached();
            // solo desarrollo: F12 = cámara espectadora del monstruo (MonsterAnimProbe)
            // solo desarrollo: F11 adelanta la próxima anomalía (se aplica por el ciclo normal si se cumplen sus condiciones)
            if ((Application.isEditor || Debug.isDebugBuild) && Input.GetKeyDown(KeyCode.F11) && Anomalies != null) Anomalies.DevSkipWait();
            if (Cursor.lockState != CursorLockMode.Locked && GameInput.MouseDown(0)) SetCursor(true);

            float dt = Time.deltaTime;
            PlayTime += dt;
            PhaseTimer -= dt;

            switch (Phase)
            {
                case GamePhase.Observation:
                    NaturalEvents();
                    if (PhaseTimer <= 0f)
                    {
                        Phase = GamePhase.Investigation;
                        HUD.Show("Fin de la observación. La casa cambiará cuando no mires. Apunta y reporta con [R].", 6f);
                        if (Anomalies.Total == 0)
                        {
                            // Escena sin anomalías válidas: no se bloquea la salida (el validador del editor lo avisa).
                            Debug.LogError("[ANOMALY DESCENT] No hay anomalías válidas en este piso; la reja se abre directamente.");
                            if (Level.stairGate != null) Level.stairGate.Open();
                        }
                    }
                    break;
                case GamePhase.Investigation:
                    Anomalies.Tick(Player, Level);
                    WarningSignals();
                    NaturalEvents();
                    Adaptive(dt);
                    break;
                case GamePhase.Awakening:
                    if (PhaseTimer <= 0f)
                    {
                        Phase = GamePhase.Active;
                        SetLightsAlert(false);
                        FlickerAll(1.5f);
                        if (Monster != null) HUD.Show(Monster.AwakeningHint, 7f, new Color(1f, 0.45f, 0.4f));
                    }
                    break;
                case GamePhase.Active:
                    Anomalies.Tick(Player, Level);
                    NaturalEvents();
                    Adaptive(dt);
                    if (!tipThrowShown && Monster != null && Monster.IsChasing)
                    {
                        tipThrowShown = true;
                        HUD.Show("Consejo: " + Monster.ChaseHint, 5f);
                    }
                    break;
            }

            UpdateMonsterPresence();
        }

        void HandlePause()
        {
            if (!GameInput.Down(GKey.Escape)) return;
            var it = Player.Interaction;
            if (it != null && (it.ReportOpen || it.ReadingNote != null || it.LastClosedFrame == Time.frameCount)) return;
            Paused = !Paused;
            Time.timeScale = Paused ? 0f : 1f;
            SetCursor(!Paused);
            if (!Paused) GameSettings.Save();
        }

        public void Resume()
        {
            if (!Paused) return;
            Paused = false;
            Time.timeScale = 1f;
            SetCursor(true);
            GameSettings.Save();
        }

        // ------------------------------------------------------------ ambiente
        /// <summary>Sonidos normales de una casa vieja: falsos positivos plausibles.</summary>
        void NaturalEvents()
        {
            if (Time.time < nextNatural) return;
            nextNatural = Time.time + Random.Range(35f, 60f);
            var room = Level.rooms[Random.Range(0, Level.rooms.Count)];
            Vector3 p = room.Center + Vector3.up * 2.4f;
            if (Random.value < 0.5f) SoundBank.PlayAt("creak", p, 0.45f, 16f, 0.5f, "[crujido de la casa]");
            else SoundBank.PlayAt("knock", p, 0.3f, 14f, 1.7f, "[tuberías]");
        }

        /// <summary>Estado de advertencia: avisos claros antes de que despierte.</summary>
        void WarningSignals()
        {
            if (Time.time < nextAmbient) return;
            bool warning = Errors >= errorThreshold - 1;
            nextAmbient = Time.time + Random.Range(22f, 40f) * (warning ? 0.6f : 1f);
            if (Errors >= 1)
            {
                var others = new List<Room>();
                foreach (var room in Level.rooms) if (room != PlayerRoom) others.Add(room);
                if (others.Count > 0)
                {
                    var room = others[Random.Range(0, others.Count)];
                    SoundBank.PlayAt("breath", room.Center + Vector3.up * 1.2f, 0.55f, 18f, 0.85f, "[respiración tras la pared]");
                }
            }
            if (warning)
            {
                if (PlayerRoom != null && PlayerRoom.roomLight != null) PlayerRoom.roomLight.Flicker(1.2f);
                var open = new List<Door>();
                foreach (var d in Level.doors) if (d.leaf != null && d.leaf.isOpen && !d.vanished) open.Add(d.leaf);
                if (open.Count > 0 && Random.value < 0.5f)
                {
                    var door = open[Random.Range(0, open.Count)];
                    door.Slam();
                    HUD.Caption("[portazo]", door.transform.position, 30f);
                }
            }
        }

        /// <summary>Casa adaptativa (sección 12): si el jugador corre demasiado, El Ciego oye mejor.</summary>
        void Adaptive(float dt)
        {
            if (Player.IsHidden || Monster == null) return;
            float k = Mathf.Exp(-dt / 60f);
            moveAcc = moveAcc * k + (Player.CurrentSpeed > 0.3f ? dt : 0f);
            runAcc = runAcc * k + (Player.IsRunning ? dt : 0f);
            float ratio = runAcc / Mathf.Max(moveAcc, 0.01f);
            if (!runPenalized && moveAcc > 10f && ratio > 0.4f)
            {
                runPenalized = true;
                Monster.hearingMultiplier = Mathf.Min(1.8f, Monster.hearingMultiplier + 0.15f);
                HUD.Show("La casa ha notado que corres mucho. " + Monster.monsterName + " te percibirá mejor.", 4.5f, new Color(1f, 0.7f, 0.5f));
            }
            else if (runPenalized && ratio < 0.15f) runPenalized = false;
        }

        // ------------------------------------------------------------ amenaza
        public void AddError(string reason, int amount = 1)
        {
            if (Phase == GamePhase.Victory || Phase == GamePhase.Defeat) return;
            SoundBank.Play2D("beep_bad", 0.6f);
            correctStreak = 0;
            Errors += amount;
            if (MonsterAwake && Monster != null)
            {
                Monster.hearingMultiplier = Mathf.Min(1.8f, Monster.hearingMultiplier + 0.08f * amount);
                Monster.Pulse(Player.transform.position + RandomFlat(2.5f));
                HUD.Show(reason + " La casa delata tu posición.", 4f, new Color(1f, 0.5f, 0.45f));
                return;
            }
            if (Errors >= errorThreshold)
            {
                HUD.Show(reason, 3f, new Color(1f, 0.5f, 0.45f));
                BeginAwakening();
            }
            else if (Errors >= errorThreshold - 1)
            {
                HUD.Show(reason + " ADVERTENCIA: algo se agita en la casa...", 5f, new Color(1f, 0.6f, 0.45f));
                FlickerAll(2f);
                nextAmbient = Mathf.Min(nextAmbient, Time.time + 4f);
            }
            else HUD.Show(reason, 4f, new Color(1f, 0.7f, 0.5f));
        }

        static Vector3 RandomFlat(float r)
        {
            var c = Random.insideUnitCircle * r;
            return new Vector3(c.x, 0f, c.y);
        }

        void BeginAwakening()
        {
            if (Application.isEditor || Debug.isDebugBuild) Debug.Log("[Amenaza] " + (Monster != null ? Monster.monsterName : "sin monstruo") + " despierta (errores " + Errors + "/" + errorThreshold + ").");
            Phase = GamePhase.Awakening;
            PhaseTimer = 3.5f;
            correctStreak = 0;
            if (Player.Interaction != null && Player.Interaction.ReportOpen) Player.inputLocked = false;
            SetLightsAlert(true);
            SoundBank.Play2D("bang", 0.9f);
            Room spawn = PickSpawnRoom();
            Vector3 pos = spawn != null ? spawn.Center : Level.rooms[0].Center;
            if (Monster != null) Monster.Awaken(pos);
            HUD.Show("ALGO HA DESPERTADO", 4f, new Color(1f, 0.3f, 0.25f));
        }

        Room PickSpawnRoom()
        {
            Room best = null;
            int bd = -1;
            foreach (var r in Level.rooms)
            {
                if (r.isSafe || r == PlayerRoom) continue;
                int d = Level.GraphDistance(PlayerRoom, r) * 100 + Random.Range(0, 50);
                if (d > bd) { bd = d; best = r; }
            }
            return best;
        }

        void UpdateMonsterPresence()
        {
            if (Monster == null || !Monster.IsActive) return;
            var mr = Level.RoomAt(Monster.transform.position);
            if (mr != null && mr.roomLight != null) mr.roomLight.Flicker(0.3f);
            float dx = Monster.transform.position.x - Player.transform.position.x;
            float dz = Monster.transform.position.z - Player.transform.position.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            if (dist < 10f && Time.time >= nextHeartbeat && !Player.IsDead)
            {
                SoundBank.Play2D("heartbeat", Mathf.Lerp(0.9f, 0.2f, dist / 10f));
                nextHeartbeat = Time.time + Mathf.Lerp(0.55f, 1.3f, dist / 10f);
            }
        }

        void SetLightsAlert(bool on)
        {
            foreach (var r in Level.rooms) if (r.roomLight != null) r.roomLight.SetAlert(on);
        }

        void FlickerAll(float seconds)
        {
            foreach (var r in Level.rooms) if (r.roomLight != null) r.roomLight.Flicker(seconds);
        }

        // ------------------------------------------------------------ eventos de anomalías y pistas
        public void OnTutorialAnomaly(Anomaly a)
        {
            string room = a.room != null ? a.room.displayName : "la casa";
            HUD.Show("Algo ha cambiado en la " + room + ". Compáralo con tu foto [Tab] y repórtalo con [R].", 7f, new Color(1f, 0.9f, 0.6f));
        }

        public void OnCriticalAnomaly(Anomaly a)
        {
            if (a.room != null && a.room.roomLight != null) a.room.roomLight.Flicker(2.5f);
            SoundBank.PlayAt("bang", a.AnchorPosition, 0.8f, 30f, 0.75f, "[estruendo]");
            HUD.Show("Has oído un estruendo: algo importante cambió. Si lo ignoras, contará doble.", 5f, new Color(1f, 0.75f, 0.5f));
        }

        public void OnClueFound(NoteItem n)
        {
            CluesFound++;
            HUD.Show("Pista encontrada: " + n.title + " (" + CluesFound + "/" + CluesTotal + ")", 3f, new Color(0.85f, 0.85f, 1f));
        }

        public void SubmitReport(Reportable r, AnomalyCategory c)
        {
            Anomaly a;
            var res = Anomalies.Report(r, c, out a);
            if (Application.isEditor || Debug.isDebugBuild)
                Debug.Log("[Reporte] «" + (r != null ? r.displayName : "?") + "» como " + c + " → " + res + (a != null ? " (" + a.anomalyName + ")" : "") + " · errores antes: " + Errors);
            switch (res)
            {
                case ReportResult.Correct:
                    CorrectReports++;
                    SoundBank.Play2D("beep_ok", 0.6f);
                    correctStreak++;
                    string extra = "";
                    if (correctStreak >= 2 && Errors > 0 && !MonsterAwake)
                    {
                        Errors--;
                        correctStreak = 0;
                        extra = " La amenaza baja un poco.";
                    }
                    HUD.Show("REPORTE CORRECTO: " + a.anomalyName + " (" + AnomalyText.Name(c) + "). " +
                             Anomalies.ReportedCount + "/" + Anomalies.Total + "." + extra, 5f, new Color(0.6f, 1f, 0.7f));
                    if (!tipFirstCorrect)
                    {
                        tipFirstCorrect = true;
                        HUD.Show("Cada acierto te acerca a abrir la reja de la escalera.", 4f);
                    }
                    if (Anomalies.AllReported)
                    {
                        if (Level.stairGate != null) Level.stairGate.Open();
                        HUD.Show("Informe completo. La reja de la ESCALERA se ha abierto: desciende.", 8f, new Color(0.6f, 1f, 0.7f));
                    }
                    break;
                case ReportResult.WrongCategory:
                    WrongReports++;
                    AddError("Reporte rechazado: la categoría no coincide.");
                    break;
                default:
                    WrongReports++;
                    AddError("Falso positivo: «" + r.displayName + "» es normal en esta casa.");
                    break;
            }
        }

        public void OnStairReached()
        {
            if (Phase == GamePhase.Victory || Phase == GamePhase.Defeat) return;
            Phase = GamePhase.Victory;
            Player.inputLocked = true;
            if (Monster != null) Monster.Hide();
            SetLightsAlert(false);
            SetCursor(false);
            SaveData.UnlockFloor(floorNumber + 1);   // guardado entre pisos: solo el piso más profundo alcanzado
            if (Application.isEditor || Debug.isDebugBuild) Debug.Log("[Piso] Escalera alcanzada en el Piso " + floorNumber + " → victoria; siguiente escena: " + nextSceneName);
        }

        public void OnPlayerCaught(MonsterBase m)
        {
            if (Application.isEditor || Debug.isDebugBuild) Debug.Log("[Amenaza] Jugador atrapado por " + (m != null ? m.monsterName : "?"));
            if (Phase == GamePhase.Defeat || Phase == GamePhase.Victory) return;
            Phase = GamePhase.Defeat;
            DefeatTime = Time.unscaledTime;
            Player.Kill(m.transform.position);
            SoundBank.Play2D("scream", 1f, 1.1f);
            SetCursor(false);
        }

        /// <summary>Último piso del juego (el 3, o cualquiera sin escena siguiente configurada a partir del 3).</summary>
        public bool IsFinalFloor { get { return floorNumber >= 3 && !NextSceneAvailable; } }

        public void BackToFirstFloor()
        {
            Time.timeScale = 1f;
            GameSettings.Save();
            if (Application.isEditor || Debug.isDebugBuild) Debug.Log("[Piso] Fin del juego → vuelta al Piso 1.");
            SceneManager.LoadScene(0);
        }

        public void LoadNextFloor()
        {
            Time.timeScale = 1f;
            GameSettings.Save();
            if (NextSceneAvailable) SceneManager.LoadScene(nextSceneName);
            else Restart();
        }

        public void Restart()
        {
            Time.timeScale = 1f;
            GameSettings.Save();
            var s = SceneManager.GetActiveScene();
            if (s.buildIndex >= 0) SceneManager.LoadScene(s.buildIndex);
            else SceneManager.LoadScene(s.name);
        }

        public static string FormatTime(float s)
        {
            if (s < 0f) s = 0f;
            int t = Mathf.FloorToInt(s);
            return (t / 60) + ":" + (t % 60).ToString("00");
        }
    }
}
