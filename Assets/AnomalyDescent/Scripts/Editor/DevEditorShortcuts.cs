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

        // Compila las escenas finales Menu y Casa de Build Settings.
        [MenuItem("ANOMALY DESCENT/Dev/Compilar build Windows (release)", priority = 210)]
        static void BuildRelease() { Build(false); }
        [MenuItem("ANOMALY DESCENT/Dev/Ejecutar build release", priority = 212)]
        static void RunRelease() { RunBuild(false); }
        [MenuItem("ANOMALY DESCENT/Dev/Ejecutar build development (Profiler)", priority = 213)]
        static void RunDev() { RunBuild(true); }

        [MenuItem("ANOMALY DESCENT/Dev/Ejecutar build release en ventana", priority = 215)]
        static void RunReleaseWindow() { RunBuild(false, true); }
        [MenuItem("ANOMALY DESCENT/Dev/Ejecutar build development en ventana", priority = 216)]
        static void RunDevWindow() { RunBuild(true, true); }

        static void RunBuild(bool dev, bool windowed = false)
        {
            string exe = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Builds/" + (dev ? "Windows_Dev" : "Windows_Release") + "/AnomalyDescent.exe"));
            if (!System.IO.File.Exists(exe)) { Debug.LogWarning("[Build] No existe " + exe + ". Compílala antes."); return; }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(exe), Arguments = windowed ? "-screen-fullscreen 0 -screen-width 1600 -screen-height 900 -force-d3d11" : "" });
            Debug.Log("[Build] Ejecutando " + exe);
        }

        [MenuItem("ANOMALY DESCENT/Dev/Compilar build Windows (development + Profiler)", priority = 211)]
        static void BuildDev() { Build(true); }

        [MenuItem("ANOMALY DESCENT/Dev/Compilar ambas builds (release y development)", priority = 214)]
        static void BuildBoth() { Build(false); Build(true); }

        static void Build(bool dev)
        {
            var scenes = System.Array.FindAll(EditorBuildSettings.scenes, x => x.enabled);
            var paths = System.Array.ConvertAll(scenes, x => x.path);
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Builds/" + (dev ? "Windows_Dev" : "Windows_Release")));
            var opts = new BuildPlayerOptions
            {
                scenes = paths,
                locationPathName = System.IO.Path.Combine(dir, "AnomalyDescent.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = dev ? (BuildOptions.Development | BuildOptions.ConnectWithProfiler) : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var sum = report.summary;
            System.IO.Directory.CreateDirectory("Capturas");
            System.IO.File.WriteAllText("Capturas/build_" + (dev ? "development" : "release") + ".txt",
                System.DateTime.Now + "\nResultado: " + sum.result + "\nRuta: " + opts.locationPathName +
                "\nErrores: " + sum.totalErrors + "\nAvisos: " + sum.totalWarnings + "\nTiempo: " + sum.totalTime +
                "\nEscenas: " + string.Join(", ", paths) + "\n");
            Debug.Log("[Build] " + sum.result + " · " + opts.locationPathName + " · " + (sum.totalSize / (1024f * 1024f)).ToString("0.0") + " MB · " +
                      sum.totalTime.TotalSeconds.ToString("0") + " s · errores " + sum.totalErrors + " · avisos " + sum.totalWarnings +
                      " · escenas: " + string.Join(", ", paths));
            if (sum.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new UnityEditor.Build.BuildFailedException("No se completó la compilación: " + sum.result);
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
