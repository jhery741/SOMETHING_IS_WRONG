using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>Opciones del jugador (accesibilidad y comodidad), guardadas en PlayerPrefs.</summary>
    public static class GameSettings
    {
        const string KSens = "AD_sens", KCaps = "AD_captions", KFx = "AD_fx";
        const string KMaster = "AD_vol_master", KSfx = "AD_vol_sfx", KAmb = "AD_vol_amb", KInvert = "AD_invert_y";

        public static float Sensitivity = 0.1f;
        public static bool Captions = true;
        public static bool Effects = true;
        public static float MasterVolume = 1f;
        public static float SfxVolume = 1f;
        public static float AmbienceVolume = 1f;
        public static bool InvertY = false;
        public static float VoiceVolume = 1f;
        public static float FieldOfView = 70f;
        public static bool CameraBob = true;
        public static bool Grain = true;

        public static void Load()
        {
            Sensitivity = PlayerPrefs.GetFloat(KSens, 0.1f);
            Captions = PlayerPrefs.GetInt(KCaps, 1) == 1;
            Effects = PlayerPrefs.GetInt(KFx, 1) == 1;
            MasterVolume = PlayerPrefs.GetFloat(KMaster, 1f);
            SfxVolume = PlayerPrefs.GetFloat(KSfx, 1f);
            AmbienceVolume = PlayerPrefs.GetFloat(KAmb, 1f);
            InvertY = PlayerPrefs.GetInt(KInvert, 0) == 1;
            VoiceVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("AD_vol_voice", 1f));
            FieldOfView = Mathf.Clamp(PlayerPrefs.GetFloat("AD_fov", 70f), 55f, 100f);
            CameraBob = PlayerPrefs.GetInt("AD_bob", 1) == 1;
            Grain = PlayerPrefs.GetInt("AD_grain", 1) == 1;
            MaxRenderHeight = PlayerPrefs.GetInt("AD_max_h", 1080);
            if (System.Array.IndexOf(RenderHeights, MaxRenderHeight) < 0) MaxRenderHeight = 1080;
            if (!resolutionApplied && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-native") >= 0) MaxRenderHeight = 0;
        }

        /// <summary>
        /// Altura máxima de render en pantalla completa (0 = nativa). Medido en build de desarrollo (Capturas/rendimiento_dev_*):
        /// a 3840x2160 nativos una gráfica integrada (Radeon 5600G) daba 16 FPS limitada por GPU. Por defecto se renderiza
        /// a 1080 líneas y la pantalla escala la imagen. Se puede cambiar con la clave AD_max_h o "-native" al arrancar.
        /// </summary>
        public static int MaxRenderHeight = 1080;
        static bool resolutionApplied;
        static int appliedHeight = -1, nativeWidth, nativeHeight;
        public static readonly string[] ResolutionLabels = { "Nativa", "1440", "1080", "720" };
        static readonly int[] RenderHeights = { 0, 1440, 1080, 720 };
        public static int ResolutionChoice { get { return Mathf.Max(0, System.Array.IndexOf(RenderHeights, MaxRenderHeight)); } }
        public static void SetResolutionChoice(int choice)
        {
            MaxRenderHeight = RenderHeights[Mathf.Clamp(choice, 0, RenderHeights.Length - 1)];
            Apply();
        }

        /// <summary>Aplica lo que no depende de cada sonido (volumen general) y, una vez, el tope de resolución.</summary>
        public static void Apply()
        {
            AudioListener.volume = Mathf.Clamp01(MasterVolume);
            if (Application.isEditor) return;
            if (!resolutionApplied)
            {
                var native = Screen.currentResolution;
                nativeWidth = Screen.fullScreen ? native.width : Screen.width;
                nativeHeight = Screen.fullScreen ? native.height : Screen.height;
                resolutionApplied = true;
            }
            if (appliedHeight == MaxRenderHeight) return;
            appliedHeight = MaxRenderHeight;
            int h = MaxRenderHeight <= 0 ? nativeHeight : Mathf.Min(MaxRenderHeight, nativeHeight);
            int w = Mathf.RoundToInt(nativeWidth * (float)h / Mathf.Max(1, nativeHeight));
            if (Screen.width == w && Screen.height == h) return;
            Screen.SetResolution(w, h, Screen.fullScreenMode);
            Debug.Log("[Ajustes] Render " + w + "x" + h + "; límite " + MaxRenderHeight + ".");
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat(KSens, Sensitivity);
            PlayerPrefs.SetInt(KCaps, Captions ? 1 : 0);
            PlayerPrefs.SetInt(KFx, Effects ? 1 : 0);
            PlayerPrefs.SetFloat(KMaster, MasterVolume);
            PlayerPrefs.SetFloat(KSfx, SfxVolume);
            PlayerPrefs.SetFloat(KAmb, AmbienceVolume);
            PlayerPrefs.SetInt(KInvert, InvertY ? 1 : 0);
            PlayerPrefs.SetFloat("AD_vol_voice", VoiceVolume);
            PlayerPrefs.SetFloat("AD_fov", FieldOfView);
            PlayerPrefs.SetInt("AD_bob", CameraBob ? 1 : 0);
            PlayerPrefs.SetInt("AD_grain", Grain ? 1 : 0);
            PlayerPrefs.SetInt("AD_max_h", MaxRenderHeight);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Progreso en Casa: la vuelta desde la que se puede continuar (se guarda al cerrar cada vuelta y al morir)
    /// y si se ha terminado el juego alguna vez.
    /// </summary>
    public static class SaveData
    {
        const string KLap = "AD_Casa_Vuelta";
        const string KDone = "AD_Casa_Terminado";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { PendingStartLap = 0; }

        /// <summary>Vuelta guardada (1 = desde el principio).</summary>
        public static int CheckpointLap
        {
            get { return Mathf.Max(1, PlayerPrefs.GetInt(KLap, 1)); }
            set { PlayerPrefs.SetInt(KLap, Mathf.Max(1, value)); PlayerPrefs.Save(); }
        }

        public static bool Completed
        {
            get { return PlayerPrefs.GetInt(KDone, 0) == 1; }
            set { PlayerPrefs.SetInt(KDone, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Al recargar la escena tras "Reintentar": vuelta en la que empezar directamente (0 = mostrar la intro).</summary>
        public static int PendingStartLap;

        public static void ResetProgress()
        {
            PlayerPrefs.DeleteKey(KLap);
            PlayerPrefs.DeleteKey(KDone);
            PlayerPrefs.Save();
        }
    }

    public static class Layers
    {
        /// <summary>Capa invisible para la cámara del jugador; solo aparece en las fotos de evidencia.</summary>
        public const int PhotoOnly = 31;

        public static void SetRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetRecursive(c.gameObject, layer);
        }
    }
}
