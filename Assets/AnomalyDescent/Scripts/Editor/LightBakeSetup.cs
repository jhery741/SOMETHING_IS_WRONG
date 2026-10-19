using System.Collections.Generic;
using UnityEditor;
using EditorMeshUtility = UnityEditor.ProBuilder.EditorMeshUtility;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.Rendering;

namespace SomethingIsWrong.EditorTools
{
    /// <summary>
    /// Iluminación global horneada para el Piso 1 (Built-in, modo "Baked Indirect"):
    ///  · Arquitectura (bajo "Architecture", salvo puertas, rejas y objetos de anomalías) → Contribute GI + UV2.
    ///  · Luces de habitación → Mixed: luz directa y sombras siguen en tiempo real (parpadeos, alerta roja, anomalías
    ///    de luz), y el rebote indirecto se hornea en lightmaps. La linterna sigue siendo Realtime.
    ///  · Muebles y objetos de anomalía quedan dinámicos y reciben el rebote mediante Light Probes.
    ///  · Una Reflection Probe horneada con proyección de caja por habitación (reflejos correctos en suelo y madera).
    /// Equivale a marcar Static, colocar sondas y pulsar "Generate Lighting" en la ventana Lighting.
    /// </summary>
    public static class LightBakeSetup
    {
        const string SettingsPath = "Assets/AnomalyDescent/Art/Lighting/LS_Piso1.lighting";

        [MenuItem("ANOMALY DESCENT/Escena/5. Preparar iluminación horneada (static, sondas)", priority = 6)]
        public static void Prepare()
        {
            var dynamicRoots = CollectDynamicRoots();
            var arch = GameObject.Find("Architecture");
            if (arch == null) { Debug.LogError("[Bake] No hay objeto 'Architecture' en la escena."); return; }

            int nStatic = 0, nUv = 0;
            foreach (var mr in arch.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (IsUnder(mr.transform, dynamicRoots)) continue;
                var go = mr.gameObject;
                var flags = GameObjectUtility.GetStaticEditorFlags(go);
                flags |= StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;
                flags &= ~StaticEditorFlags.BatchingStatic;   // el techo puede bajar (anomalía)
                GameObjectUtility.SetStaticEditorFlags(go, flags);
                mr.receiveGI = ReceiveGI.Lightmaps;
                mr.scaleInLightmap = 1f;
                nStatic++;
                var pb = go.GetComponent<ProBuilderMesh>();
                if (pb != null) { EditorMeshUtility.Optimize(pb, true); nUv++; }
            }
            // Todo lo demás con renderer: dinámico, iluminado por sondas
            int nProbe = 0;
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
            {
                if (mr.transform.IsChildOf(arch.transform) && !IsUnder(mr.transform, dynamicRoots)) continue;
                var f = GameObjectUtility.GetStaticEditorFlags(mr.gameObject);
                if ((f & StaticEditorFlags.ContributeGI) != 0) GameObjectUtility.SetStaticEditorFlags(mr.gameObject, f & ~StaticEditorFlags.ContributeGI & ~StaticEditorFlags.BatchingStatic);
                mr.lightProbeUsage = LightProbeUsage.BlendProbes;
                mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                nProbe++;
            }

            // Luces
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (!l.name.StartsWith("Luz_")) continue;
                l.lightmapBakeType = LightmapBakeType.Mixed;
                l.bounceIntensity = 1.3f;
                UnityEditor.EditorUtility.SetDirty(l);
            }

            var rooms = Object.FindObjectsByType<Room>(FindObjectsInactive.Include);
            BuildProbes(rooms);
            BuildReflectionProbes(rooms);
            ApplyLightingSettings();
            TextureQualityFixes();

            EditorSceneManager.MarkSceneDirty(arch.scene);
            Debug.Log("[Bake] Preparado: " + nStatic + " piezas de arquitectura static (" + nUv + " con UV2 de ProBuilder), " + nProbe + " renderers dinámicos con sondas, " + rooms.Length + " habitaciones con sondas. Ahora: 6. Hornear.");
        }

        [MenuItem("ANOMALY DESCENT/Escena/6. Hornear iluminación (Generate Lighting)", priority = 7)]
        public static void Bake()
        {
            if (Lightmapping.lightingSettings == null) ApplyLightingSettings();
            Lightmapping.bakeCompleted -= OnBaked;
            Lightmapping.bakeCompleted += OnBaked;
            bool ok = Lightmapping.BakeAsync();
            Debug.Log("[Bake] Horneado iniciado: " + ok + " (lightmapper " + Lightmapping.lightingSettings.lightmapper + ")");
        }

        static void OnBaked()
        {
            Lightmapping.bakeCompleted -= OnBaked;
            var maps = LightmapSettings.lightmaps;
            Debug.Log("[Bake] Horneado completado: " + (maps != null ? maps.Length : 0) + " lightmaps, " +
                      (LightmapSettings.lightProbes != null ? LightmapSettings.lightProbes.count : 0) + " sondas de luz.");
            EditorSceneManager.SaveOpenScenes();
        }

        static HashSet<Transform> CollectDynamicRoots()
        {
            var set = new HashSet<Transform>();
            foreach (var a in Object.FindObjectsByType<Anomaly>(FindObjectsInactive.Include))
            {
                a.IsValid(out _);
                if (a.target != null) set.Add(a.target);
                if (a.showObject != null) set.Add(a.showObject.transform);
                if (a.swapRenderer != null) set.Add(a.swapRenderer.transform);
                if (a.door != null) set.Add(a.door.transform);
            }
            foreach (var d in Object.FindObjectsByType<Door>(FindObjectsInactive.Include)) set.Add(d.transform);
            foreach (var g in Object.FindObjectsByType<StairGate>(FindObjectsInactive.Include)) set.Add(g.transform);
            foreach (var r in Object.FindObjectsByType<Reportable>(FindObjectsInactive.Include)) set.Add(r.transform);
            foreach (var h in Object.FindObjectsByType<HideSpot>(FindObjectsInactive.Include)) set.Add(h.transform);
            return set;
        }

        static bool IsUnder(Transform t, HashSet<Transform> roots)
        {
            for (var p = t; p != null; p = p.parent) if (roots.Contains(p)) return true;
            return false;
        }

        static Transform Group(string name)
        {
            var lighting = GameObject.Find("Lighting");
            var t = lighting != null ? lighting.transform.Find(name) : null;
            if (t == null) { var g = GameObject.Find(name); if (g != null) t = g.transform; }
            if (t == null)
            {
                var go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, name);
                if (lighting != null) go.transform.SetParent(lighting.transform, false);
                t = go.transform;
            }
            return t;
        }

        // Rejilla de sondas dentro de cada habitación (a 0.3 m de las paredes), a ras de suelo, a media altura y bajo el techo.
        static void BuildProbes(Room[] rooms)
        {
            var g = Group("LightProbes");
            var lpg = g.GetComponent<LightProbeGroup>();
            if (lpg == null) lpg = Undo.AddComponent<LightProbeGroup>(g.gameObject);
            g.position = Vector3.zero; g.rotation = Quaternion.identity; g.localScale = Vector3.one;
            var pts = new List<Vector3>();
            foreach (var r in rooms)
            {
                r.SyncFromVolume();
                Rect a = r.area;
                float y0 = r.transform.position.y;
                if (r.volume != null) y0 = r.volume.bounds.min.y;
                float h = Mathf.Max(1.2f, r.height);
                float[] ys = { 0.25f, h * 0.5f, h - 0.3f };
                int nx = Mathf.Max(2, Mathf.CeilToInt((a.width - 0.6f) / 1.25f) + 1);
                int nz = Mathf.Max(2, Mathf.CeilToInt((a.height - 0.6f) / 1.25f) + 1);
                for (int ix = 0; ix < nx; ix++)
                    for (int iz = 0; iz < nz; iz++)
                        foreach (float y in ys)
                        {
                            float x = Mathf.Lerp(a.xMin + 0.3f, a.xMax - 0.3f, nx == 1 ? 0.5f : ix / (float)(nx - 1));
                            float z = Mathf.Lerp(a.yMin + 0.3f, a.yMax - 0.3f, nz == 1 ? 0.5f : iz / (float)(nz - 1));
                            pts.Add(new Vector3(x, y0 + y, z));
                        }
            }
            Undo.RecordObject(lpg, "Sondas");
            lpg.probePositions = pts.ToArray();
            Debug.Log("[Bake] " + pts.Count + " sondas de luz.");
        }

        static void BuildReflectionProbes(Room[] rooms)
        {
            var g = Group("ReflectionProbes");
            foreach (var r in rooms)
            {
                string n = "RP_" + r.name;
                var t = g.Find(n);
                if (t == null)
                {
                    var go = new GameObject(n);
                    Undo.RegisterCreatedObjectUndo(go, n);
                    go.transform.SetParent(g, false);
                    t = go.transform;
                }
                var rp = t.GetComponent<ReflectionProbe>();
                if (rp == null) rp = Undo.AddComponent<ReflectionProbe>(t.gameObject);
                float y0 = r.volume != null ? r.volume.bounds.min.y : r.transform.position.y;
                float h = Mathf.Max(1.2f, r.height);
                t.position = new Vector3(r.area.center.x, y0 + Mathf.Min(1.6f, h * 0.55f), r.area.center.y);
                rp.mode = ReflectionProbeMode.Baked;
                rp.boxProjection = true;
                rp.size = new Vector3(r.area.width, h, r.area.height);
                rp.center = new Vector3(0f, y0 + h * 0.5f - t.position.y, 0f);
                rp.resolution = 128;
                rp.hdr = true;
                rp.intensity = 1f;
                rp.blendDistance = 0.5f;
                rp.clearFlags = ReflectionProbeClearFlags.SolidColor;
                rp.backgroundColor = Color.black;
                UnityEditor.EditorUtility.SetDirty(rp);
            }
        }

        static void ApplyLightingSettings()
        {
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(SettingsPath);
            if (ls == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/AnomalyDescent/Art/Lighting")) AssetDatabase.CreateFolder("Assets/AnomalyDescent/Art", "Lighting");
                ls = new LightingSettings { name = "LS_Piso1" };
                AssetDatabase.CreateAsset(ls, SettingsPath);
            }
            ls.bakedGI = true;
            ls.realtimeGI = false;
            ls.mixedBakeMode = MixedLightingMode.IndirectOnly;
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            ls.lightmapResolution = 24f;
            ls.lightmapPadding = 4;
            ls.lightmapMaxSize = 1024;
            ls.directSampleCount = 32;
            ls.indirectSampleCount = 512;
            ls.environmentSampleCount = 128;
            ls.maxBounces = 3;
            ls.ao = false;   // la AO la pone el postprocesado
            ls.lightmapCompression = LightmapCompression.HighQuality;
            ls.directionalityMode = LightmapsMode.CombinedDirectional;
            ls.indirectScale = 1.3f;
            ls.albedoBoost = 1.0f;
            UnityEditor.EditorUtility.SetDirty(ls);
            AssetDatabase.SaveAssets();
            Lightmapping.lightingSettings = ls;
        }

        // Filtrado anisótropo forzado (suelos y paredes a ángulo rasante sin emborronarse) y techo menos rugoso.
        static void TextureQualityFixes()
        {
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            foreach (var guid in AssetDatabase.FindAssets("M_Techo t:Material"))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || m.name != "M_Techo") continue;
                if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", 0.2f);
                UnityEditor.EditorUtility.SetDirty(m);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
