using System.Collections.Generic;
using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>
    /// ANOMALY: DESCENT — punto de entrada de la escena Casa.
    /// NO construye el escenario: la casa (LevelRoot con habitaciones, puertas, muebles, anomalías...) está
    /// colocada a mano en la escena. Este componente solo crea lo que es lógica de partida:
    /// el jugador (cámara, linterna, cámara de evidencia), el HUD, el director de vueltas, el gestor de anomalías
    /// y la presencia del monstruo, y prepara a El Ciego, que ya está en la escena.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Header("Monstruo de la casa (objeto de la escena, dentro del grupo Monster)")]
        public MonsterBase monster;
        [Tooltip("Líneas de voz de El Ciego (asset editable). Vacío = las líneas por defecto definidas en VoiceLineSet.")]
        public VoiceLineSet voices;

        [Header("Vueltas de la noche")]
        public List<LapConfig> laps = DefaultLaps();
        public float ignoredAnomalyPenaltyAfter = 150f;
        public int startingBottles = 1;
        [Tooltip("0 = aleatorio en cada partida")]
        public int randomSeed = 0;

        public static List<LapConfig> DefaultLaps()
        {
            return new List<LapConfig>
            {
                new LapConfig { title = "VUELTA 1 · APRENDER", observeSeconds = 45f, anomalies = 1, interval = new Vector2(5f, 8f), maxPresenceStage = 1, attentionFloor = 0f, tutorial = true },
                new LapConfig { title = "VUELTA 2 · OBSERVAR", observeSeconds = 20f, anomalies = 2, interval = new Vector2(18f, 30f), maxPresenceStage = 3, attentionFloor = 1f },
                new LapConfig { title = "VUELTA 3 · LA VOZ", observeSeconds = 15f, anomalies = 3, interval = new Vector2(16f, 26f), maxPresenceStage = 4, attentionFloor = 2f },
                new LapConfig { title = "VUELTA 4 · LA CAZA", observeSeconds = 12f, anomalies = 3, interval = new Vector2(14f, 22f), maxPresenceStage = 4, attentionFloor = 3f },
            };
        }

        [Header("Jugador")]
        public float fieldOfView = 70f;

        [Header("Escena")]
        [Tooltip("Desactivado (recomendado): se usa la iluminación guardada en Window > Rendering > Lighting.\n" +
                 "Activado: niebla y luz ambiental antiguas forzadas por código.")]
        public bool overrideSceneLighting = false;
        public Transform playerGroup;
        public Transform systemsGroup;

        [Header("Depuración")]
        public bool showDebugCounters = false;

        void Start()
        {
            Time.timeScale = 1f;
            PlaytestLog.Begin();
            GameSettings.Load();
            GameSettings.Apply();
            if (overrideSceneLighting) SetupEnvironment();

            var level = LevelRoot.Instance != null ? LevelRoot.Instance : FindAnyObjectByType<LevelRoot>();
            if (level == null)
            {
                Debug.LogError("[ANOMALY DESCENT] La escena no tiene un LevelRoot. El escenario se construye a mano en la escena " +
                               "(ver GUIA_CONSTRUCCION_ESCENA.md); el juego ya no lo genera por código.");
                enabled = false;
                return;
            }
            level.Refresh();
            if (level.rooms.Count == 0)
            {
                Debug.LogError("[ANOMALY DESCENT] LevelRoot no tiene habitaciones (componentes Room). Revisa la escena con 'ANOMALY DESCENT > Validar escena'.");
                enabled = false;
                return;
            }

            var oldCam = Camera.main;
            if (oldCam != null) oldCam.gameObject.SetActive(false);

            var player = CreatePlayer(level);
            if (playerGroup != null) player.transform.SetParent(playerGroup, true);

            if (monster == null) monster = FindAnyObjectByType<MonsterBase>();
            if (monster == null)
                Debug.LogWarning("[ANOMALY DESCENT] No hay monstruo en la escena: la partida se podrá jugar, pero sin amenaza.");
            else monster.Setup(player, level);

            var hud = new GameObject("HUD").AddComponent<HUD>();
            if (systemsGroup != null) hud.transform.SetParent(systemsGroup, false);

            if (laps == null || laps.Count == 0) laps = DefaultLaps();
            var director = gameObject.AddComponent<GameDirector>();
            director.showDebug = showDebugCounters && (Application.isEditor || Debug.isDebugBuild);

            var anomalies = gameObject.AddComponent<AnomalyManager>();
            anomalies.ignoreAfter = ignoredAnomalyPenaltyAfter;
            anomalies.Init(director, level, randomSeed);

            var presence = gameObject.AddComponent<MonsterPresence>();
            presence.Setup(director, level, player, monster, voices);

            director.Configure(level, player, monster, anomalies, presence, laps);
            int pending = SaveData.PendingStartLap;
            SaveData.PendingStartLap = 0;
            if (pending > 0) director.StartGame(pending);   // "Reintentar vuelta": sin pasar por la intro
            else director.EnterIntro();
        }

        static void SetupEnvironment()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.045f, 0.038f, 0.03f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.05f;
            RenderSettings.fogColor = new Color(0.02f, 0.015f, 0.01f);
            RenderSettings.skybox = null;
        }

        PlayerController CreatePlayer(LevelRoot level)
        {
            var go = new GameObject("Jugador");
            go.tag = "Player";
            Vector3 spawn = level.playerSpawn != null ? level.playerSpawn.position : level.rooms[0].Center + Vector3.up * 0.05f;
            Quaternion rot = level.playerSpawn != null ? Quaternion.Euler(0f, level.playerSpawn.eulerAngles.y, 0f) : Quaternion.identity;
            if (level.playerSpawn == null) Debug.LogWarning("[ANOMALY DESCENT] LevelRoot sin 'Player Spawn': el jugador aparece en el centro de la primera habitación.");
            go.transform.SetPositionAndRotation(spawn, rot);

            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.75f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.875f, 0f);
            cc.stepOffset = 0.3f;
            cc.slopeLimit = 50f;
            cc.skinWidth = 0.03f;

            var camGo = new GameObject("CamaraJugador");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(go.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.62f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 60f;
            cam.fieldOfView = GameSettings.FieldOfView;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = ~(1 << Layers.PhotoOnly);   // lo que solo sale en las fotos
            camGo.AddComponent<AudioListener>();
            PostFxRig.AttachTo(cam);

            var ambience = SoundBank.AddLoop(camGo, "drone", 0.22f * GameSettings.AmbienceVolume, 10f, false);
            if (ambience != null && ambience.clip != null) ambience.Play();
            camGo.AddComponent<AdaptiveAmbience>().Init(ambience);

            var pc = go.AddComponent<PlayerController>();
            pc.Setup(cam, GameSettings.Sensitivity);
            pc.throwables = startingBottles;
            return pc;
        }
    }
}
