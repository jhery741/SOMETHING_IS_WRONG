using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomethingIsWrong.EditorTools
{
    /// <summary>
    /// Valores fotométricos del Piso 1 (equivale a ajustarlos a mano en el Inspector y en la ventana Lighting).
    /// Las luces las coloca el autor en la escena; aquí solo se fijan color, intensidad, alcance y sombras de las
    /// que existen, por nombre. Cada luz tiene una fuente visible o plausible:
    ///  · Luz_Mesilla   — quinqué de la mesilla (cálida, naranja; la principal del dormitorio)
    ///  · Luz_Dormitorio — globo de techo del dormitorio (cálida, tenue)
    ///  · Luz_Pasillo   — bombilla desnuda del pasillo (amarillenta)
    ///  · Luz_Refugio   — aplique industrial del refugio (fría, verdosa: el único sitio "seguro")
    ///  · Luz_Escalera  — rebote frío desde abajo (sin sombras, muy tenue)
    /// Ambiente: color plano, oscuro y frío (las sombras caen a azul-gris, no a negro); sin skybox ni niebla.
    /// </summary>
    public static class InteriorLightingPreset
    {
        struct L { public string name; public Color c; public float i, r; public LightShadows s; }

        static readonly L[] LightsP1 =
        {
            new L { name = "Luz_Mesilla",    c = new Color(1.00f, 0.68f, 0.40f), i = 1.10f, r = 4.0f, s = LightShadows.Soft },
            new L { name = "Luz_Dormitorio", c = new Color(1.00f, 0.87f, 0.72f), i = 0.50f, r = 5.5f, s = LightShadows.Soft },
            new L { name = "Luz_Pasillo",    c = new Color(1.00f, 0.88f, 0.72f), i = 0.60f, r = 5.5f, s = LightShadows.Soft },
            new L { name = "Luz_Refugio",    c = new Color(0.86f, 0.92f, 0.92f), i = 0.90f, r = 5.5f, s = LightShadows.Soft },
            new L { name = "Luz_Escalera",   c = new Color(0.60f, 0.68f, 0.80f), i = 0.30f, r = 4.0f, s = LightShadows.None },
        };

        // Piso 2 · El Torcido — sótano de servicio: fluorescentes verdosos enfermos, refugio con luz roja de emergencia
        static readonly L[] LightsP2 =
        {
            new L { name = "Luz_Mesilla",    c = new Color(0.95f, 0.78f, 0.48f), i = 0.70f, r = 3.5f, s = LightShadows.Soft },
            new L { name = "Luz_Dormitorio", c = new Color(0.80f, 0.95f, 0.70f), i = 0.55f, r = 5.5f, s = LightShadows.Soft },
            new L { name = "Luz_Pasillo",    c = new Color(0.85f, 1.00f, 0.65f), i = 0.50f, r = 5.5f, s = LightShadows.Soft },
            new L { name = "Luz_Refugio",    c = new Color(1.00f, 0.42f, 0.30f), i = 0.85f, r = 5.0f, s = LightShadows.Soft },
            new L { name = "Luz_Escalera",   c = new Color(0.60f, 0.80f, 0.50f), i = 0.30f, r = 4.0f, s = LightShadows.None },
        };

        // Piso 3 · La Presencia — casi a oscuras: solo luz fría, azulada, como de luna; la linterna es lo único cálido
        static readonly L[] LightsP3 =
        {
            new L { name = "Luz_Mesilla",    c = new Color(0.70f, 0.80f, 1.00f), i = 0.40f, r = 3.5f, s = LightShadows.Soft },
            new L { name = "Luz_Dormitorio", c = new Color(0.55f, 0.65f, 0.95f), i = 0.38f, r = 5.0f, s = LightShadows.Soft },
            new L { name = "Luz_Pasillo",    c = new Color(0.60f, 0.70f, 1.00f), i = 0.32f, r = 5.0f, s = LightShadows.Soft },
            new L { name = "Luz_Refugio",    c = new Color(0.90f, 0.92f, 1.00f), i = 0.70f, r = 5.0f, s = LightShadows.Soft },
            new L { name = "Luz_Escalera",   c = new Color(0.40f, 0.50f, 0.80f), i = 0.25f, r = 4.0f, s = LightShadows.None },
        };

        [MenuItem("ANOMALY DESCENT/Escena/3. Aplicar iluminación interior (según el piso de la escena)", priority = 4)]
        public static void Apply()
        {
            var bs = Object.FindAnyObjectByType<GameBootstrap>();
            int floor = bs != null ? Mathf.Clamp(bs.floorNumber, 1, 3) : 1;
            var Lights = floor == 3 ? LightsP3 : floor == 2 ? LightsP2 : LightsP1;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.028f, 0.030f, 0.038f);   // 3.ª pasada (CAPTURED): la linterna manda; las lámparas son islas de luz
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;
            RenderSettings.reflectionIntensity = 0.18f;
            // Niebla muy oscura: los fondos de pasillo se pierden en negro y la linterna "abre" el espacio
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = floor == 3 ? 0.09f : floor == 2 ? 0.075f : 0.06f;
            RenderSettings.fogColor = floor == 3 ? new Color(0.010f, 0.012f, 0.020f) : floor == 2 ? new Color(0.012f, 0.015f, 0.010f) : new Color(0.012f, 0.012f, 0.016f);
            if (floor == 2) RenderSettings.ambientLight = new Color(0.026f, 0.030f, 0.022f);
            if (floor == 3) RenderSettings.ambientLight = new Color(0.020f, 0.024f, 0.036f);
            RenderSettings.subtractiveShadowColor = new Color(0.12f, 0.13f, 0.16f);

            int n = 0;
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (light.type == LightType.Directional)
                {
                    // escena cerrada: el sol de la plantilla solo aplanaba el ambiente
                    Undo.RecordObject(light, "Iluminación interior");
                    light.enabled = false;
                    continue;
                }
                foreach (var l in Lights)
                    if (light.gameObject.name == l.name)
                    {
                        Undo.RecordObject(light, "Iluminación interior");
                        light.color = l.c; light.intensity = l.i; light.range = l.r; light.shadows = l.s;
                        light.shadowStrength = 0.9f;
                        light.shadowBias = 0.03f; light.shadowNormalBias = 0.3f;
                        light.renderMode = LightRenderMode.ForcePixel;
                        var rl = light.GetComponent<RoomLight>();
                        if (rl != null) { Undo.RecordObject(rl, "Iluminación interior"); rl.baseIntensity = l.i; }
                        n++;
                    }
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[Iluminación] Piso " + floor + ": ambiente interior aplicado; " + n + " luces ajustadas.");
        }
    }
}
