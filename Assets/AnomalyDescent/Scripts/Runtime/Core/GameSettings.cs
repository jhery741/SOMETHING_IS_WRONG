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

        public static void Load()
        {
            Sensitivity = PlayerPrefs.GetFloat(KSens, 0.1f);
            Captions = PlayerPrefs.GetInt(KCaps, 1) == 1;
            Effects = PlayerPrefs.GetInt(KFx, 1) == 1;
            MasterVolume = PlayerPrefs.GetFloat(KMaster, 1f);
            SfxVolume = PlayerPrefs.GetFloat(KSfx, 1f);
            AmbienceVolume = PlayerPrefs.GetFloat(KAmb, 1f);
            InvertY = PlayerPrefs.GetInt(KInvert, 0) == 1;
        }

        /// <summary>Aplica lo que no depende de cada sonido (volumen general).</summary>
        public static void Apply()
        {
            AudioListener.volume = Mathf.Clamp01(MasterVolume);
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
            PlayerPrefs.Save();
        }
    }

    /// <summary>Progreso entre pisos (solo lo que el diseño necesita: el piso más profundo alcanzado).</summary>
    public static class SaveData
    {
        const string KFloor = "AD_PisoDesbloqueado";

        /// <summary>Piso más profundo desbloqueado (1 = solo el primero).</summary>
        public static int UnlockedFloor { get { return Mathf.Max(1, PlayerPrefs.GetInt(KFloor, 1)); } }

        public static void UnlockFloor(int floor)
        {
            if (floor <= UnlockedFloor) return;
            PlayerPrefs.SetInt(KFloor, floor);
            PlayerPrefs.Save();
        }

        public static void ResetProgress()
        {
            PlayerPrefs.DeleteKey(KFloor);
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
