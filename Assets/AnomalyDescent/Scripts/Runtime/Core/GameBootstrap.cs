using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>
    /// ANOMALY: DESCENT — punto de entrada de la escena de un piso.
    /// NO construye el escenario: el piso (LevelRoot con habitaciones, puertas, muebles, anomalías...) debe estar
    /// colocado a mano en la escena. Este componente solo crea lo que es lógica de partida:
    /// el jugador (cámara, linterna, cámara de evidencia), el HUD, el director y el gestor de anomalías,
    /// y prepara al monstruo del piso que ya está en la escena.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Header("Piso (canon: 1 = El Ciego · 2 = El Torcido · 3 = La Presencia)")]
        [Range(1, 3)] public int floorNumber = 1;
        public string floorTitle = "PISO 1 · APRENDER";
        [Tooltip("Escena del siguiente piso al descender (vacío = fin de la demo). Debe estar en Build Settings.")]
        public string nextSceneName = "Piso2";
        [Tooltip("Texto que se muestra al completar el piso si la escena siguiente aún no existe.")]
        public string nextFloorTeaser = "Siguiente: Piso 2 · El sótano — en construcción.";

        [Header("Monstruo del piso (objeto de la escena, dentro del grupo Monster)")]
        [Tooltip("ElCiegoController (Piso 1), ElTorcidoController (Piso 2) o LaPresenciaController (Piso 3).")]
        public MonsterBase monster;

        [Header("Reglas del piso")]
        [Tooltip("Errores antes de despertar al monstruo (prueba entre 3 y 5).")]
        public int errorThreshold = 4;
        public float observationTime = 45f;
        public Vector2 anomalyInterval = new Vector2(28f, 45f);
        public float ignoredAnomalyPenaltyAfter = 150f;
        public int startingBottles = 1;
        [Tooltip("0 = aleatorio en cada partida")]
        public int randomSeed = 0;

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
            else
            {
                if (monster.floor != floorNumber)
                    Debug.LogWarning("[ANOMALY DESCENT] El monstruo '" + monster.monsterName + "' pertenece al Piso " + monster.floor + " y esta escena es el Piso " + floorNumber + ".");
                monster.Setup(player, level);
            }

            var hud = new GameObject("HUD").AddComponent<HUD>();
            if (systemsGroup != null) hud.transform.SetParent(systemsGroup, false);

            var director = gameObject.AddComponent<GameDirector>();
            director.errorThreshold = errorThreshold;
            director.observationTime = observationTime;
            director.showDebug = showDebugCounters && (Application.isEditor || Debug.isDebugBuild);
            director.floorNumber = floorNumber;
            director.floorTitle = floorTitle;
            director.nextSceneName = nextSceneName;
            director.nextFloorTeaser = nextFloorTeaser;

            var anomalies = gameObject.AddComponent<AnomalyManager>();
            anomalies.interval = anomalyInterval;
            anomalies.ignoreAfter = ignoredAnomalyPenaltyAfter;
            anomalies.Init(director, level, randomSeed);

            director.Configure(level, player, monster, anomalies);
            director.EnterIntro();
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
            cam.fieldOfView = fieldOfView;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = ~(1 << Layers.PhotoOnly);   // lo que solo sale en las fotos
            camGo.AddComponent<AudioListener>();
            PostFxRig.AttachTo(cam);

            var ambience = SoundBank.AddLoop(camGo, "drone", 0.22f * GameSettings.AmbienceVolume, 10f, false);
            if (ambience != null && ambience.clip != null) ambience.Play();

            var pc = go.AddComponent<PlayerController>();
            pc.Setup(cam, GameSettings.Sensitivity);
            pc.throwables = startingBottles;
            return pc;
        }
    }
}
