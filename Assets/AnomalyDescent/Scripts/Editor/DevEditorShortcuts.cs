using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SomethingIsWrong.EditorTools
{
    /// <summary>
    /// Atajos de desarrollo del Editor (no tocan la escena): Game View maximizado al dar Play, y su contrario.
    /// Usa reflexión porque Unity no expone públicamente la opción "Play Maximized" de la Game View.
    /// </summary>
    public static class DevEditorShortcuts
    {
        [MenuItem("ANOMALY DESCENT/Dev/Game View: maximizar al dar Play", priority = 200)]
        static void Max() { SetPlayMaximized(true); }
        [MenuItem("ANOMALY DESCENT/Dev/Game View: no maximizar al dar Play", priority = 201)]
        static void NoMax() { SetPlayMaximized(false); }

        // Equivale a File > Build Profiles > Windows > Build con las escenas de Build Settings (Piso1 → Piso2 → Piso3).
        [MenuItem("ANOMALY DESCENT/Dev/Compilar build Windows (release)", priority = 210)]
        static void BuildRelease() { Build(false); }
        [MenuItem("ANOMALY DESCENT/Dev/Ejecutar build release", priority = 212)]
        static void RunRelease() { RunBuild(false); }
        [MenuItem("ANOMALY DESCENT/Dev/Ejecutar build development (Profiler)", priority = 213)]
        static void RunDev() { RunBuild(true); }

        static void RunBuild(bool dev)
        {
            string exe = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../" + (dev ? "Build_Windows_Dev" : "Build_Windows") + "/AnomalyDescent.exe"));
            if (!System.IO.File.Exists(exe)) { Debug.LogWarning("[Build] No existe " + exe + ". Compílala antes."); return; }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(exe) });
            Debug.Log("[Build] Ejecutando " + exe);
        }

        [MenuItem("ANOMALY DESCENT/Dev/Compilar build Windows (development + Profiler)", priority = 211)]
        static void BuildDev() { Build(true); }

        static void Build(bool dev)
        {
            var scenes = System.Array.FindAll(EditorBuildSettings.scenes, x => x.enabled);
            var paths = System.Array.ConvertAll(scenes, x => x.path);
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../" + (dev ? "Build_Windows_Dev" : "Build_Windows")));
            var opts = new BuildPlayerOptions
            {
                scenes = paths,
                locationPathName = System.IO.Path.Combine(dir, "AnomalyDescent.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = dev ? (BuildOptions.Development | BuildOptions.ConnectWithProfiler) : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var sum = report.summary;
            Debug.Log("[Build] " + sum.result + " · " + opts.locationPathName + " · " + (sum.totalSize / (1024f * 1024f)).ToString("0.0") + " MB · " +
                      sum.totalTime.TotalSeconds.ToString("0") + " s · errores " + sum.totalErrors + " · avisos " + sum.totalWarnings +
                      " · escenas: " + string.Join(", ", paths));
        }

        static void SetPlayMaximized(bool on)
        {
            var t = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (t == null) { Debug.LogWarning("[Dev] No se encontró GameView."); return; }
            int n = 0;
            foreach (var w in Resources.FindObjectsOfTypeAll(t))
            {
                bool done = false;
                var pBehavior = t.GetProperty("enterPlayModeBehavior", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pBehavior != null && pBehavior.PropertyType.IsEnum)
                {
                    var names = System.Enum.GetNames(pBehavior.PropertyType);
                    string want = null;
                    foreach (var s in names) if (on ? s.Contains("Maximized") : s.Contains("Focused")) { want = s; break; }
                    if (want != null) { pBehavior.SetValue(w, System.Enum.Parse(pBehavior.PropertyType, want)); done = true; }
                }
                if (!done)
                {
                    var p = t.GetProperty("maximizeOnPlay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null) { p.SetValue(w, on); done = true; }
                }
                if (done) { n++; ((EditorWindow)w).Repaint(); }
            }
            Debug.Log("[Dev] Game View " + (on ? "maximizada" : "no maximizada") + " al dar Play (" + n + " vistas).");
        }
    }
}
