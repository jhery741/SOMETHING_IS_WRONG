using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SomethingIsWrong.EditorTools
{
    /// <summary>
    /// Utilidades de proyecto. YA NO crea escenas ni escenario automáticamente al abrir Unity.
    /// · Mantiene las escenas de los pisos en Build Settings (en orden de descenso).
    /// · Crea los materiales base que MaterialLibrary usa para objetos TEMPORALES de partida (botellas lanzadas, figuras provisionales).
    /// </summary>
    [InitializeOnLoad]
    public static class SiwProjectSetup
    {
        public const string Root = "Assets/AnomalyDescent";
        public const string ScenesDir = Root + "/Scenes";
        /// <summary>Orden de juego: se empieza en el Piso 1 y se desciende.</summary>
        public static readonly string[] FloorScenes = { ScenesDir + "/Piso1.unity", ScenesDir + "/Piso2.unity", ScenesDir + "/Piso3.unity" };
        public const string ScenePath = ScenesDir + "/Piso1.unity";

        static SiwProjectSetup() { EditorApplication.delayCall += AutoSetup; }

        static void AutoSetup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += AutoSetup; return; }
            EnsureBaseMaterials(false);
            EnsureInBuildSettings();
        }

        [MenuItem("ANOMALY DESCENT/Abrir escena Piso 1", priority = 0)]
        public static void OpenScene()
        {
            if (!File.Exists(ScenePath)) { EditorUtility.DisplayDialog("Piso 1", "No existe " + ScenePath + ". Constrúyela siguiendo GUIA_CONSTRUCCION_ESCENA.md.", "OK"); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("ANOMALY DESCENT/Reparar materiales base (si cambias de render pipeline)", priority = 50)]
        public static void RepairMaterials()
        {
            EnsureBaseMaterials(true);
            Debug.Log("[ANOMALY DESCENT] Materiales base actualizados para: " + MaterialLibrary.DesiredShaderName());
        }

        [MenuItem("ANOMALY DESCENT/Actualizar Build Settings (pisos en orden)", priority = 51)]
        public static void EnsureInBuildSettings()
        {
            // Escenas guardadas fuera de Scenes/ (p. ej. Assets/AnomalyDescent/Piso1.unity) se mueven a su sitio, conservando GUID.
            foreach (var path in FloorScenes)
            {
                string loose = Root + "/" + Path.GetFileName(path);
                if (File.Exists(loose) && !File.Exists(path))
                {
                    EnsureFolder(ScenesDir);
                    string err = AssetDatabase.MoveAsset(loose, path);
                    if (!string.IsNullOrEmpty(err)) Debug.LogWarning("[ANOMALY DESCENT] No se pudo mover " + loose + ": " + err);
                }
            }
            var scenes = EditorBuildSettings.scenes.ToList();
            bool changed = false;
            // Quita entradas antiguas de escenas que ya no existen (p. ej. el antiguo Piso3 generado)
            int removed = scenes.RemoveAll(s => s.path.StartsWith(ScenesDir) && !File.Exists(s.path));
            if (removed > 0) changed = true;
            int insertAt = 0;
            foreach (var path in FloorScenes)
            {
                if (!File.Exists(path)) continue;
                int idx = scenes.FindIndex(s => s.path == path);
                if (idx < 0) { scenes.Insert(insertAt, new EditorBuildSettingsScene(path, true)); changed = true; }
                else if (idx != insertAt) { var e = scenes[idx]; scenes.RemoveAt(idx); scenes.Insert(insertAt, e); changed = true; }
                insertAt++;
            }
            if (changed) { EditorBuildSettings.scenes = scenes.ToArray(); AssetDatabase.SaveAssets(); }
            Debug.Log("[Build] Escenas en Build Settings: " + string.Join(" → ", EditorBuildSettings.scenes.Select(x => System.IO.Path.GetFileNameWithoutExtension(x.path) + (x.enabled ? "" : " (desactivada)"))));
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string name = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        public static void EnsureBaseMaterials(bool force)
        {
            string folder = Root + "/Resources";
            EnsureFolder(folder);
            Shader s = Shader.Find(MaterialLibrary.DesiredShaderName());
            if (s == null) s = Shader.Find("Standard");
            if (s == null) return;
            MakeMat(folder + "/SIW_BaseLit.mat", s, false, force);
            MakeMat(folder + "/SIW_BaseEmissive.mat", s, true, force);
            AssetDatabase.SaveAssets();
        }

        static void MakeMat(string path, Shader s, bool emissive, bool force)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null && m.shader == s && !force) return;
            if (m == null)
            {
                m = new Material(s);
                AssetDatabase.CreateAsset(m, path);
            }
            else m.shader = s;
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.white);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else m.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(m);
        }
    }
}
