using UnityEditor;
using UnityEngine;

namespace SomethingIsWrong.EditorTools
{
    /// <summary>
    /// Ajustes de calidad para un PC de gama media / gráficos integrados con el Built-in Render Pipeline.
    /// No cambia de render pipeline ni instala paquetes.
    /// </summary>
    public static class GraphicsSetupTool
    {
        [MenuItem("ANOMALY DESCENT/Escena/2. Configurar calidad gráfica (alta · sombras suaves)", priority = 2)]
        public static void ApplyMidRange()
        {
            // Interior oscuro: la linterna y 3 lámparas cercanas proyectan sombras suaves de alta resolución.
            QualitySettings.pixelLightCount = 8;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.shadowDistance = 25f;
            QualitySettings.shadowProjection = ShadowProjection.CloseFit;
            QualitySettings.antiAliasing = 4;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
            QualitySettings.realtimeReflectionProbes = true;
            QualitySettings.softParticles = false;
            QualitySettings.vSyncCount = 1;
            QualitySettings.globalTextureMipmapLimit = 0;
            var hb = Object.FindAnyObjectByType<ShadowBudget>();
            if (hb != null)
            {
                Undo.RecordObject(hb, "Presupuesto de sombras alto");
                hb.maxShadowedLights = 3;
                hb.shadowType = LightShadows.Soft;
                EditorUtility.SetDirty(hb);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[ANOMALY DESCENT] Calidad alta aplicada al nivel '" + QualitySettings.names[QualitySettings.GetQualityLevel()] +
                      "' (sombras suaves High, 25 m, MSAA 4x). Color: " + PlayerSettings.colorSpace + ". Guarda la escena.");

            if (PlayerSettings.colorSpace != ColorSpace.Linear &&
                EditorUtility.DisplayDialog("Espacio de color",
                    "El proyecto está en espacio de color " + PlayerSettings.colorSpace + ".\n\n" +
                    "Los materiales PBR (texturas de ambientCG / Poly Haven, modelos de Meshy) están pensados para 'Linear': " +
                    "la luz se suma y se atenúa de forma físicamente correcta.\n\n" +
                    "Al cambiarlo, los colores procedurales se verán algo más claros y habrá que revisar la intensidad de las lámparas. " +
                    "Unity reimportará las texturas (puede tardar).",
                    "Cambiar a Linear", "Mantener"))
            {
                PlayerSettings.colorSpace = ColorSpace.Linear;
                Debug.Log("[ANOMALY DESCENT] Espacio de color cambiado a Linear.");
            }
        }

        [MenuItem("ANOMALY DESCENT/Escena/2b. Calidad mínima (gráficos integrados muy justos)", priority = 3)]
        public static void ApplyLow()
        {
            QualitySettings.pixelLightCount = 4;
            QualitySettings.shadows = ShadowQuality.HardOnly;
            QualitySettings.shadowResolution = ShadowResolution.Low;
            QualitySettings.shadowDistance = 12f;
            QualitySettings.antiAliasing = 0;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
            QualitySettings.realtimeReflectionProbes = true;
            QualitySettings.globalTextureMipmapLimit = 1;
            var budget = Object.FindAnyObjectByType<ShadowBudget>();
            if (budget != null)
            {
                Undo.RecordObject(budget, "Presupuesto de sombras bajo");
                budget.maxShadowedLights = 1;
                budget.shadowType = LightShadows.Hard;
                EditorUtility.SetDirty(budget);
            }
            Debug.Log("[ANOMALY DESCENT] Calidad mínima aplicada (sombras duras, 1 lámpara con sombra, texturas a mitad de resolución). Guarda la escena.");
        }
    }
}
