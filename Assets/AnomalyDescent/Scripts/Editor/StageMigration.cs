using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SomethingIsWrong.EditorTools
{
    /// <summary>
    /// Replanteo "una casa, un monstruo" — etapa 1. Operaciones de archivo equivalentes a hacerlas a mano en el
    /// Project window (copiar, mover, duplicar una escena); no crea geometría ni objetos de escena.
    ///  1. Copia de seguridad de Assets, Packages y ProjectSettings junto al proyecto (Backup_E1).
    ///  2. Mueve a Assets/AnomalyDescent/_Archivo lo que sale de la versión inicial (con AssetDatabase.MoveAsset:
    ///     conserva los GUID, nada se borra y todo se puede devolver).
    ///  3. Duplica Scenes/Piso1 como Scenes/Casa y deja Build Settings = Casa (el menú se añade en la etapa 6).
    /// Escribe el detalle en Capturas/etapa1.txt.
    /// </summary>
    public static class StageMigration
    {
        const string Root = "Assets/AnomalyDescent";
        const string Archive = Root + "/_Archivo";

        static readonly string[] ToArchive =
        {
            Root + "/Scenes/Piso2.unity",
            Root + "/Scenes/Piso2",
            Root + "/Scenes/Piso3.unity",
            Root + "/Scenes/Piso3",
            Root + "/Art/Monsters/ElTorcido",
            Root + "/Art/Monsters/LaPresencia",
            Root + "/Art/PostFX/PF_Piso2.asset",
            Root + "/Art/PostFX/PF_Piso3.asset",
        };

        [MenuItem("ANOMALY DESCENT/Etapas/1. Copia de seguridad + archivo + escena Casa", priority = 300)]
        public static void Run()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Etapa1] Sal de Play primero."); return; }
            EditorSceneManager.SaveOpenScenes();
            var log = new StringBuilder("ETAPA 1 — " + System.DateTime.Now + "\n");

            // 1 · copia de seguridad
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            // Nombre corto: las rutas de los FBX de Meshy ya rozan el límite de 260 caracteres de Windows
            string backup = Path.GetFullPath(Path.Combine(project, "..", "Backup_E1"));
            if (Directory.Exists(backup)) backup += "_" + System.DateTime.Now.ToString("HHmm");
            try
            {
                foreach (var dir in new[] { "Assets", "Packages", "ProjectSettings" })
                    CopyDir(Path.Combine(project, dir), Path.Combine(backup, dir), log);
                log.AppendLine("Copia de seguridad: " + backup);
            }
            catch (System.Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError("[Etapa1] La copia de seguridad falló; no se ha movido nada. " + e.Message);
                return;
            }
            EditorUtility.ClearProgressBar();

            // 2 · archivo (si Piso2/3 está abierta, se abre Piso1 antes de mover)
            EditorSceneManager.OpenScene(Root + "/Scenes/Piso1.unity");
            if (!AssetDatabase.IsValidFolder(Archive)) AssetDatabase.CreateFolder(Root, "_Archivo");
            foreach (var path in ToArchive)
            {
                if (AssetDatabase.LoadAssetAtPath<Object>(path) == null && !AssetDatabase.IsValidFolder(path)) { log.AppendLine("No existe (se omite): " + path); continue; }
                string rel = path.Substring(Root.Length + 1);                 // p. ej. Art/Monsters/ElTorcido
                string dest = Archive + "/" + rel;
                EnsureFolder(Path.GetDirectoryName(dest).Replace('\\', '/'));
                string err = AssetDatabase.MoveAsset(path, dest);
                log.AppendLine(string.IsNullOrEmpty(err) ? "Archivado: " + path + " → " + dest : "ERROR al mover " + path + ": " + err);
            }

            // 3 · escena Casa
            string casa = Root + "/Scenes/Casa.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(casa) == null)
                log.AppendLine(AssetDatabase.CopyAsset(Root + "/Scenes/Piso1.unity", casa) ? "Escena creada: " + casa + " (copia de Piso1)" : "ERROR al copiar Piso1 → Casa");
            else log.AppendLine("La escena Casa ya existía: no se toca.");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(casa, true) };
            log.AppendLine("Build Settings: " + string.Join(", ", EditorBuildSettings.scenes.Select(s => s.path)));
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(casa);

            string outDir = Path.Combine(project, "Capturas");
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "etapa1.txt"), log.ToString());
            Debug.Log("[Etapa1] Terminada. Detalle en Capturas/etapa1.txt");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void CopyDir(string src, string dst, StringBuilder log)
        {
            var files = Directory.GetFiles(src, "*", SearchOption.AllDirectories);
            long bytes = 0;
            for (int i = 0; i < files.Length; i++)
            {
                string f = files[i];
                string rel = f.Substring(src.Length).TrimStart('\\', '/');
                if (rel.StartsWith("_DUPLICADO_ACCIDENTAL")) continue;
                string target = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(f, target, true);
                bytes += new FileInfo(f).Length;
                if (i % 50 == 0) EditorUtility.DisplayProgressBar("Copia de seguridad", rel, (float)i / files.Length);
            }
            log.AppendLine(string.Format("  {0}: {1} archivos, {2:0} MB", Path.GetFileName(src), files.Length, bytes / 1048576.0));
        }
    }
}
