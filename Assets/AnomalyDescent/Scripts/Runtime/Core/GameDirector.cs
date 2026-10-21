using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SomethingIsWrong
{
    /// <summary>Fases de una partida en Casa.</summary>
    public enum GamePhase { Intro, Observation, Investigation, LapComplete, Escape, Victory, Defeat }

    /// <summary>Una vuelta de la noche (editable en GameBootstrap).</summary>
    [System.Serializable]
    public class LapConfig
    {
        public string title = "VUELTA";
        [Tooltip("Segundos de observación con la casa normal (se puede acortar desde la radio del refugio).")]
        public float observeSeconds = 20f;
        [Tooltip("Anomalías que aparecen en esta vuelta.")]
        public int anomalies = 2;
        [Tooltip("Segundos entre anomalías (mín, máx).")]
        public Vector2 interval = new Vector2(20f, 35f);
        [Tooltip("Etapa máxima de la presencia: 0 distante · 1 verbal · 2 alerta · 3 acercamiento · 4 físico.")]
        [Range(0, 4)] public int maxPresenceStage = 4;
        [Tooltip("Atención mínima de la presencia durante esta vuelta.")]
        public float attentionFloor = 0f;
        [Tooltip("La primera anomalía es la de tutorial.")]
        public bool tutorial;
    }

    /// <summary>
    /// Bucle de ANOMALY: DESCENT en una sola casa, por VUELTAS:
    ///   OBSERVAR (casa normal; fotografía lo que quieras) → INVESTIGAR (aparecen las anomalías de la vuelta cuando no miras;
    ///   fotografíalas y repórtalas desde el carrete con habitación + categoría) → VUELTA COMPLETA (todas reportadas) →
    ///   CERRAR LA VUELTA en la radio del refugio (la casa vuelve a la normalidad, se guarda el progreso) → siguiente vuelta.
    ///   Tras la última vuelta se abre la PUERTA PRINCIPAL y El Ciego despierta del todo: salir por ella = victoria.
    ///   Si El Ciego te atrapa: derrota → reintentar desde el principio de esa vuelta (o desde el principio).
    /// La presión del monstruo la lleva MonsterPresence (atención y etapas).
    /// </summary>
    public class GameDirector : MonoBehaviour
    {
        public static GameDirector Instance { get; private set; }

        public GamePhase Phase { get; private set; }
        public bool Paused { get; private set; }
        public float PhaseTimer { get; private set; }
        public float PlayTime { get; private set; }
        public float DefeatTime { get; private set; }
        public Room PlayerRoom { get; private set; }
        public int CorrectReports { get; private set; }
        public int WrongReports { get; private set; }
        public int Lap { get; private set; }
        public int LapCount { get { return laps != null ? laps.Count : 0; } }
        public LapConfig CurrentLap { get { return Lap >= 1 && Lap <= LapCount ? laps[Lap - 1] : null; } }
        public float TitleUntil { get; private set; }

        public bool showDebug = false;
        public int startLap = 1;

        public LevelRoot Level { get; private set; }
        public PlayerController Player { get; private set; }
        public MonsterBase Monster { get; private set; }
        public AnomalyManager Anomalies { get; private set; }
        public MonsterPresence Presence { get; private set; }

        List<LapConfig> laps;
        ExitDoor exitDoor;
        float nextNatural;
        bool tipChase;

        public bool IsPlaying
        {
            get
            {
                if (Paused) return false;
                return Phase == GamePhase.Observation || Phase == GamePhase.Investigation || Phase == GamePhase.LapComplete || Phase == GamePhase.Escape;
            }
        }
        /// <summary>Partida en curso (aunque esté en pausa).</summary>
        public bool IsRunning { get { return Phase != GamePhase.Intro && Phase != GamePhase.Victory && Phase != GamePhase.Defeat; } }
        /// <summary>La casa "está viva": hay anomalías o amenaza en juego.</summary>
        public bool IsInvestigating { get { return Phase == GamePhase.Investigation || Phase == GamePhase.LapComplete || Phase == GamePhase.Escape; } }
        public bool MonsterAwake { get { return Monster != null && Monster.IsActive; } }
        public bool CarreteOpen { get { return Player != null && Player.Evidence != null && Player.Evidence.UIOpen; } }
        public bool IsFinalLap { get { return Lap >= LapCount; } }

        /// <summary>El refugio detiene a El Ciego en la puerta y no puede atrapar dentro.</summary>
        public bool RefugeProtects { get { return true; } }

        public string ThreatLabel { get { return Presence != null ? Presence.StateLabel : "—"; } }

        void Awake() { Instance = this; }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Time.timeScale = 1f;
        }

        public void Configure(LevelRoot level, PlayerController player, MonsterBase monster, AnomalyManager anomalies, MonsterPresence presence, List<LapConfig> lapList)
        {
            Level = level;
            Player = player;
            Monster = monster;
            Anomalies = anomalies;
            Presence = presence;
            laps = lapList;
            exitDoor = level.GetComponentInChildren<ExitDoor>(true);
            if (exitDoor == null) Debug.LogError("[Casa] No hay ExitDoor (puerta principal): la partida no se podrá terminar.");
            else { var leaf = exitDoor.GetComponent<Door>(); leaf.locked = true; leaf.isOpen = false; }
            if (level.GetComponentInChildren<LapTerminal>(true) == null) Debug.LogError("[Casa] No hay LapTerminal (radio del refugio): las vueltas no se podrán cerrar.");
        }

        public void EnterIntro()
        {
            Phase = GamePhase.Intro;
            Time.timeScale = 0f;
            SetCursor(false);
        }

        public void StartGame(int fromLap)
        {
            Time.timeScale = 1f;
            SetCursor(true);
            nextNatural = Time.time + 20f;
            PlayTime = 0f;
            StartLap(Mathf.Clamp(fromLap, 1, LapCount));
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
                    if (SaveData.CheckpointLap > 1 && GameInput.Down(GKey.C)) StartGame(SaveData.CheckpointLap);
                    else if (GameInput.Down(GKey.Enter) || GameInput.Down(GKey.Space) || GameInput.MouseDown(0)) StartGame(startLap);
                    return;
                case GamePhase.Victory:
                    if (GameInput.Down(GKey.Enter) || GameInput.Down(GKey.R)) RestartFromBeginning();
                    return;
                case GamePhase.Defeat:
                    if (GameInput.Down(GKey.R) || GameInput.Down(GKey.Enter)) Restart();
                    return;
            }

            HandlePause();
            if (Paused) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Input.GetKeyDown(KeyCode.F9)) showDebug = !showDebug;
#endif
            if (Cursor.lockState != CursorLockMode.Locked && GameInput.MouseDown(0) && !CarreteOpen) SetCursor(true);

            float dt = Time.deltaTime;
            PlayTime += dt;
            PhaseTimer -= dt;

            switch (Phase)
            {
                case GamePhase.Observation:
                    NaturalEvents();
                    if (PhaseTimer <= 0f) EndObservation();
                    break;
                case GamePhase.Investigation:
                    Anomalies.Tick(Player, Level);
                    NaturalEvents();
                    break;
                case GamePhase.LapComplete:
                case GamePhase.Escape:
                    NaturalEvents();
                    break;
            }
            if (!tipChase && Monster != null && Monster.IsChasing)
            {
                tipChase = true;
                HUD.Show("Consejo: " + Monster.ChaseHint + " Agachado y quieto no te oye.", 6f);
            }
            UpdateMonsterPresence();
        }

        void HandlePause()
        {
            if (!GameInput.Down(GKey.Escape)) return;
            if (CarreteOpen || (Player.Evidence != null && Player.Evidence.ClosedThisFrame)) return;
            Paused = !Paused;
            Time.timeScale = Paused ? 0f : 1f;
            AudioListener.pause = Paused;
            SetCursor(!Paused);
            if (!Paused) GameSettings.Save();
        }

        public void Resume()
        {
            if (!Paused) return;
            Paused = false;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            SetCursor(true);
            GameSettings.Save();
        }

        // ------------------------------------------------------------ vueltas
        void StartLap(int n)
        {
            Lap = n;
            var cfg = CurrentLap;
            Phase = GamePhase.Observation;
            PhaseTimer = cfg.observeSeconds;
            TitleUntil = Time.unscaledTime + 4f;
            Anomalies.interval = cfg.interval;
            Anomalies.BeginLap(n, cfg.anomalies, cfg.tutorial);
            if (Presence != null) Presence.BeginLap(n, cfg.maxPresenceStage, cfg.attentionFloor);
            SetLightsAlert(false);
            if (Application.isEditor || Debug.isDebugBuild) Debug.Log("[Vuelta " + n + "/" + LapCount + "] " + cfg.title + " · observación " + cfg.observeSeconds + " s · " + cfg.anomalies + " anomalías");
            if (n == 1)
                HUD.Show("OBSERVA LA CASA. Fotografía cada habitación (clic derecho levanta la cámara, clic izquierdo dispara). Luego algo cambiará.", 8f);
            else
                HUD.Show("Nueva vuelta. La casa ha vuelto a la normalidad... por ahora. Obsérvala.", 6f);
        }

        /// <summary>Fin de la observación (por tiempo o desde la radio del refugio).</summary>
        public void EndObservation()
        {
            if (Phase != GamePhase.Observation) return;
            Phase = GamePhase.Investigation;
            PhaseTimer = 0f;
            HUD.Show(Anomalies.Total == 1 ? "Algo va a cambiar cuando no mires. Encuéntralo, fotografíalo y repórtalo desde el carrete [Tab]."
                                          : Anomalies.Total + " cosas van a cambiar cuando no mires. Fotografíalas y repórtalas desde el carrete [Tab].", 6f);
            if (Anomalies.Total == 0)
            {
                Debug.LogError("[Casa] No hay anomalías válidas para esta vuelta: se da por completa.");
                Phase = GamePhase.LapComplete;
            }
        }

        /// <summary>Cierre de vuelta desde la radio del refugio.</summary>
        public void CloseLap()
        {
            if (Phase != GamePhase.LapComplete) return;
            SoundBank.Play2D("static", 0.4f, 0.9f);
            Anomalies.ResetLap();
            if (Presence != null) Presence.OnLapClosed();
            SetLightsAlert(false);
            foreach (var d in Level.doors) if (d != null && d.leaf != null && d.leaf.GetComponent<ExitDoor>() == null) d.leaf.locked = false;
            int next = Lap + 1;
            if (Application.isEditor || Debug.isDebugBuild) Debug.Log("[Vuelta " + Lap + "] cerrada en la radio. Siguiente: " + (next > LapCount ? "huida final" : next.ToString()));
            if (next > LapCount) { BeginEscape(); return; }
            SaveData.CheckpointLap = next;
            if (Player.Flashlight != null) Player.Flashlight.AddCharge(100f);
            HUD.Show("Vuelta " + Lap + " cerrada. Progreso guardado. Pilas nuevas para la linterna.", 4f, new Color(0.6f, 1f, 0.7f));
            StartLap(next);
        }

        void BeginEscape()
        {
            Phase = GamePhase.Escape;
            TitleUntil = Time.unscaledTime + 4f;
            if (exitDoor != null) { var leaf = exitDoor.GetComponent<Door>(); leaf.locked = false; }
            if (Player.Flashlight != null) Player.Flashlight.AddCharge(100f);
            SoundBank.PlayAt("gate", exitDoor != null ? exitDoor.transform.position : Player.transform.position, 1f, 40f, 0.9f, "[cerrojo de la puerta principal]");
            HUD.Show("Informe completo. La PUERTA PRINCIPAL del recibidor se ha abierto. Sal de la casa.", 8f, new Color(0.6f, 1f, 0.7f));
            if (Presence != null) Presence.OnEscapeStart();
        }

        public void OnExitReached()
        {
            if (Phase != GamePhase.Escape) return;
            Phase = GamePhase.Victory;
            Player.inputLocked = true;
            if (Monster != null) Monster.Hide();
            SetLightsAlert(false);
            SetCursor(false);
            SaveData.CheckpointLap = 1;
            SaveData.Completed = true;
            if (Application.isEditor || Debug.isDebugBuild) Debug.Log("[Casa] Victoria: el jugador sale por la puerta principal. Tiempo " + FormatTime(PlayTime));
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

        /// <summary>Señal de alerta: parpadeo en tu habitación y, a veces, un portazo cerca.</summary>
        public void WarningSignal()
        {
            if (PlayerRoom != null && PlayerRoom.roomLight != null) PlayerRoom.roomLight.Flicker(1.2f);
            var open = new List<Door>();
            foreach (var d in Level.doors) if (d != null && d.leaf != null && d.leaf.isOpen && !d.vanished && !d.Connects(PlayerRoom)) open.Add(d.leaf);
            if (open.Count > 0 && Random.value < 0.5f)
            {
                var door = open[Random.Range(0, open.Count)];
                door.Slam();
                HUD.Caption("[portazo]", door.transform.position, 30f);
            }
        }

        // ------------------------------------------------------------ amenaza
        public void AddError(string reason, int amount = 1)
        {
            if (!IsRunning) return;
            SoundBank.Play2D("beep_bad", 0.6f);
            HUD.Show(reason, 4f, new Color(1f, 0.6f, 0.45f));
            if (MonsterAwake) Monster.Pulse(Player.transform.position + RandomFlat(2.5f));
            if (Presence != null) Presence.AddAttention(amount, reason);
        }

        static Vector3 RandomFlat(float r)
        {
            var c = Random.insideUnitCircle * r;
            return new Vector3(c.x, 0f, c.y);
        }

        /// <summary>La presencia ha llegado a la etapa física: El Ciego despierta lejos del jugador.</summary>
        public void BeginHunt()
        {
            if (Monster == null) return;
            if (Monster.IsActive)
            {
                // Estaba jugando (aparición o retirada): deja de jugar y va hacia ti.
                if (Application.isEditor || Debug.isDebugBuild) Debug.Log("[Amenaza] " + Monster.monsterName + " deja de jugar y empieza a cazar (vuelta " + Lap + ").");
                Monster.Hunt(FarSpawnPoint(), Player.transform.position + RandomFlat(2f));
                HUD.Show(Monster.AwakeningHint, 7f, new Color(1f, 0.45f, 0.4f));
                return;
            }
            if (Application.isEditor || Debug.isDebugBuild) Debug.Log("[Amenaza] " + Monster.monsterName + " despierta (vuelta " + Lap + ").");
            SetLightsAlert(true);
            SoundBank.Play2D("bang", 0.9f);
            Monster.Hunt(FarSpawnPoint(), Player.transform.position);
            HUD.Show("ALGO HA DESPERTADO", 4f, new Color(1f, 0.3f, 0.25f));
            HUD.Show(Monster.AwakeningHint, 7f, new Color(1f, 0.45f, 0.4f));
            StartCoroutine(AlertOff(3.5f));
        }

        IEnumerator AlertOff(float s)
        {
            yield return new WaitForSeconds(s);
            SetLightsAlert(false);
            FlickerAll(1.5f);
        }

        /// <summary>Habitación lejos del jugador (distancia real y por puertas), nunca la suya ni una contigua.</summary>
        public Vector3 FarSpawnPoint()
        {
            Room best = null;
            float bd = -1f;
            Vector3 pp = Player.transform.position;
            foreach (var r in Level.rooms)
            {
                if (r.isSafe || r == PlayerRoom) continue;
                int g = Level.GraphDistance(PlayerRoom, r);
                float flat = Vector2.Distance(new Vector2(r.Center.x, r.Center.z), new Vector2(pp.x, pp.z));
                float score = flat + (g >= 2 ? 4f : 0f) + Random.Range(0f, 1.5f);
                if (score > bd) { bd = score; best = r; }
            }
            return best != null ? best.Center : Level.rooms[0].Center;
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
        float nextHeartbeat;

        void SetLightsAlert(bool on)
        {
            foreach (var r in Level.rooms) if (r.roomLight != null) r.roomLight.SetAlert(on);
        }

        public void FlickerAll(float seconds)
        {
            foreach (var r in Level.rooms) if (r.roomLight != null) r.roomLight.Flicker(seconds);
        }

        // ------------------------------------------------------------ anomalías, pistas y reportes
        public void OnTutorialAnomaly(Anomaly a)
        {
            HUD.Show("Algo ha cambiado en la casa. Compara con tus fotos [Tab], fotografía el cambio y repórtalo desde el carrete.", 8f, new Color(1f, 0.9f, 0.6f));
        }

        public void OnCriticalAnomaly(Anomaly a)
        {
            if (a.room != null && a.room.roomLight != null) a.room.roomLight.Flicker(2.5f);
            SoundBank.PlayAt("bang", a.AnchorPosition, 0.8f, 30f, 0.75f, "[estruendo]");
        }

        public bool PlayerHasPhotographedActiveAnomaly()
        {
            if (Player == null || Player.Evidence == null) return false;
            foreach (var p in Player.Evidence.Photos)
            {
                if (p.lap != Lap) continue;
                foreach (var a in p.anomaliesAtCapture) if (a != null && a.active && !a.reported) return true;
            }
            return false;
        }

        /// <summary>Reporte desde el carrete. Devuelve true si es correcto.</summary>
        public bool SubmitPhotoReport(EvidenceCamera.Photo photo, Room room, AnomalyCategory c)
        {
            Anomaly a;
            var res = Anomalies.ReportPhoto(photo, room, c, out a);
            if (Application.isEditor || Debug.isDebugBuild)
                Debug.Log("[Reporte] foto «" + photo.subjectName + "» · " + (room != null ? room.displayName : "?") + " · " + c + " → " + res + (a != null ? " (" + a.anomalyName.Trim() + " en " + a.room.displayName + ")" : ""));
            switch (res)
            {
                case ReportResult.Correct:
                    CorrectReports++;
                    SoundBank.Play2D("beep_ok", 0.6f);
                    HUD.Show("REPORTE CORRECTO: " + a.anomalyName.Trim() + " · " + room.displayName + " · " + AnomalyText.Name(c) + ".  " +
                             Anomalies.ReportedCount + "/" + Anomalies.Total + " en esta vuelta.", 5f, new Color(0.6f, 1f, 0.7f));
                    if (Presence != null) Presence.OnReport(true);
                    if (Anomalies.AllReported && Phase == GamePhase.Investigation)
                    {
                        Phase = GamePhase.LapComplete;
                        HUD.Show("Vuelta completa. Vuelve al REFUGIO y ciérrala en la radio [E].", 8f, new Color(0.6f, 1f, 0.7f));
                    }
                    return true;
                case ReportResult.WrongRoom:
                    WrongReports++;
                    AddError("Reporte rechazado: ese cambio no está en " + room.displayName.ToLower() + ".");
                    if (Presence != null) Presence.OnReport(false);
                    return false;
                case ReportResult.WrongCategory:
                    WrongReports++;
                    AddError("Reporte rechazado: la categoría no coincide.");
                    if (Presence != null) Presence.OnReport(false);
                    return false;
                case ReportResult.OldPhoto:
                    HUD.Show("Esa foto es de otra vuelta.", 3f);
                    return false;
                case ReportResult.AlreadyReported:
                    HUD.Show("Ese cambio ya está reportado.", 3f);
                    return false;
                default:
                    WrongReports++;
                    AddError("Falso positivo: «" + photo.subjectName + "» estaba normal cuando lo fotografiaste.");
                    if (Presence != null) Presence.OnReport(false);
                    return false;
            }
        }

        public void OnPlayerCaught(MonsterBase m)
        {
            if (Application.isEditor || Debug.isDebugBuild) Debug.Log("[Amenaza] Jugador atrapado por " + (m != null ? m.monsterName : "?") + " en la vuelta " + Lap);
            if (Phase == GamePhase.Defeat || Phase == GamePhase.Victory) return;
            Phase = GamePhase.Defeat;
            DefeatTime = Time.unscaledTime;
            Player.Kill(m.transform.position);
            SoundBank.Play2D("scream", 1f, 1.1f);
            SetCursor(false);
            SaveData.CheckpointLap = Mathf.Min(Lap, LapCount);
        }

        /// <summary>Reintentar desde el principio de la vuelta actual (o la guardada).</summary>
        public void Restart()
        {
            SaveData.PendingStartLap = Mathf.Clamp(Lap, 1, Mathf.Max(1, LapCount));
            Reload();
        }

        public void RestartFromBeginning()
        {
            SaveData.CheckpointLap = 1;
            SaveData.PendingStartLap = 1;
            Reload();
        }

        void Reload()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
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

