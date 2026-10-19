#if SIW_POSTFX
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace SomethingIsWrong.EditorTools
{
    /// <summary>
    /// Crea (o actualiza) el perfil de postprocesado del Piso 1 y el volumen global de la escena.
    /// Dirección: horror cinematográfico realista — ACES, oclusión ambiental marcada en rincones,
    /// bloom suave solo en fuentes de luz, sombras frías y luces cálidas, poca saturación. Sin grano.
    /// Equivale a crear el perfil a mano (Create > Post-processing Profile) y ajustar sus valores en el Inspector.
    /// </summary>
    public static class PostFxSetup
    {
        const string Dir = "Assets/AnomalyDescent/Art/PostFX";
        const string ResourcesPath = "Packages/com.unity.postprocessing/PostProcessing/PostProcessResources.asset";

        internal static int CurrentFloor()
        {
            var b = Object.FindAnyObjectByType<GameBootstrap>();
            return b != null ? Mathf.Clamp(b.floorNumber, 1, 3) : 1;
        }

        [MenuItem("ANOMALY DESCENT/Escena/4. Postprocesado cinematográfico (perfil + volumen)", priority = 5)]
        public static void Apply()
        {
            if (!AssetDatabase.IsValidFolder("Assets/AnomalyDescent/Art")) AssetDatabase.CreateFolder("Assets/AnomalyDescent", "Art");
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/AnomalyDescent/Art", "PostFX");

            int floor = CurrentFloor();
            string profilePath = Dir + "/PF_Piso" + floor + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<PostProcessProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<PostProcessProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
            }

            var ao = Get<AmbientOcclusion>(profile);
            ao.mode.Override(AmbientOcclusionMode.MultiScaleVolumetricObscurance);
            ao.intensity.Override(0.8f);
            ao.thicknessModifier.Override(1.6f);
            ao.color.Override(new Color(0.02f, 0.02f, 0.035f));

            var bloom = Get<Bloom>(profile);
            bloom.intensity.Override(0.9f);
            bloom.threshold.Override(1.05f);
            bloom.softKnee.Override(0.6f);
            bloom.diffusion.Override(7f);
            bloom.color.Override(new Color(1f, 0.86f, 0.70f));

            var cg = Get<ColorGrading>(profile);
            cg.gradingMode.Override(GradingMode.HighDefinitionRange);
            cg.tonemapper.Override(Tonemapper.ACES);
            cg.postExposure.Override(0.1f);
            cg.temperature.Override(floor == 3 ? -22f : -8f);
            cg.tint.Override(floor == 2 ? -10f : 0f);                         // Piso 2: verdoso enfermizo
            cg.saturation.Override(floor == 3 ? -38f : floor == 2 ? -28f : -20f);
            cg.contrast.Override(10f);
            cg.lift.Override(new Vector4(0.96f, 0.99f, 1.06f, 0.0f));   // sombras azul-gris
            cg.gamma.Override(new Vector4(1f, 1f, 1f, 0f));
            cg.gain.Override(new Vector4(1.04f, 1.0f, 0.94f, 0f));          // altas luces cálidas

            var ae = Get<AutoExposure>(profile);
            ae.filtering.Override(new Vector2(50f, 95f));
            ae.minLuminance.Override(-0.5f);   // no "ilumina" de más la oscuridad
            ae.maxLuminance.Override(1.2f);
            ae.keyValue.Override(1f);
            ae.eyeAdaptation.Override(EyeAdaptation.Progressive);
            ae.speedUp.Override(1.5f);
            ae.speedDown.Override(0.8f);

            var vig = Get<Vignette>(profile);
            vig.intensity.Override(0.30f);
            vig.smoothness.Override(0.5f);
            vig.roundness.Override(0.9f);
            vig.color.Override(new Color(0.01f, 0.01f, 0.02f));

            var ca = Get<ChromaticAberration>(profile);
            ca.intensity.Override(0.05f);

            // Grano de película: rompe el banding en la penumbra y da textura de cámara
            var grain = Get<Grain>(profile);
            grain.colored.Override(false);
            grain.intensity.Override(0.22f);
            grain.size.Override(0.9f);
            grain.lumContrib.Override(0.8f);

            if (floor == 2) { cg.gain.Override(new Vector4(1.0f, 1.03f, 0.92f, 0f)); cg.lift.Override(new Vector4(0.98f, 1.02f, 0.97f, 0f)); vig.intensity.Override(0.34f); }
            if (floor == 3) { cg.gain.Override(new Vector4(0.95f, 0.99f, 1.06f, 0f)); cg.lift.Override(new Vector4(0.95f, 0.98f, 1.08f, 0f)); vig.intensity.Override(0.38f); ca.intensity.Override(0.12f); grain.intensity.Override(0.3f); }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = GameObject.Find("PostFX");
            if (go == null)
            {
                go = new GameObject("PostFX");
                Undo.RegisterCreatedObjectUndo(go, "PostFX");
                var lighting = GameObject.Find("Lighting");
                if (lighting != null) go.transform.SetParent(lighting.transform, false);
            }
            var vol = go.GetComponent<PostProcessVolume>();
            if (vol == null) vol = Undo.AddComponent<PostProcessVolume>(go);
            vol.isGlobal = true;
            vol.priority = 0;
            vol.weight = 1f;
            vol.sharedProfile = profile;
            var rig = go.GetComponent<PostFxRig>();
            if (rig == null) rig = Undo.AddComponent<PostFxRig>(go);
            rig.resources = AssetDatabase.LoadAssetAtPath<PostProcessResources>(ResourcesPath);
            if (rig.resources == null) Debug.LogError("[PostFX] No se encontró " + ResourcesPath);
            EditorUtility.SetDirty(go);

            // Vista de escena con el mismo look: capa en la cámara de la Scene View no aplica; la previsualización es en Play.
            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log("[PostFX] Perfil " + profilePath + " aplicado. Volumen global en '" + go.name + "'. Recursos: " + (rig.resources != null));
        }

        static T Get<T>(PostProcessProfile p) where T : PostProcessEffectSettings
        {
            T s;
            if (p.TryGetSettings(out s)) { s.enabled.Override(true); return s; }
            s = p.AddSettings<T>();
            s.name = typeof(T).Name;
            s.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(s, p);
            s.enabled.Override(true);
            return s;
        }
    }
}
#endif
