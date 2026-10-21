#if DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SomethingIsWrong
{
    /// <summary>
    /// SOLO BUILD DE DESARROLLO (no se compila en release ni actúa en el Editor): mide el rendimiento real del ejecutable
    /// mientras se juega en Casa con ProfilerRecorder (hilo principal, GC, memoria, draw calls) y FrameTimingManager (GPU).
    /// Escribe un resumen cada 30 s y al salir en &lt;carpeta del exe&gt;/Capturas/rendimiento_dev_&lt;fecha&gt;.txt.
    /// Complementa al Profiler de Unity (conexión en vivo); no cambia nada del juego.
    /// </summary>
    public class PerfRecorder : MonoBehaviour
    {
        ProfilerRecorder mainThread, gcAlloc, usedMem, sysMem, drawCalls, batches, setPass, tris;
        readonly List<float> frameMs = new List<float>(), mainMs = new List<float>(), gpuMs = new List<float>();
        readonly List<long> gcBytes = new List<long>(), draws = new List<long>(), batchList = new List<long>(), setPassList = new List<long>(), triList = new List<long>();
        long peakUsed, peakSys;
        float nextWrite, recording;
        string file;
        bool skipFocusedFrame;
        readonly FrameTiming[] timing = new FrameTiming[1];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor) return;
            var go = new GameObject("DEV_PerfRecorder");
            DontDestroyOnLoad(go);
            go.AddComponent<PerfRecorder>();
        }

        void OnEnable()
        {
            mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
            gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            usedMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
            sysMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        }

        void OnDisable()
        {
            Write();
            mainThread.Dispose(); gcAlloc.Dispose(); usedMem.Dispose(); sysMem.Dispose();
            drawCalls.Dispose(); batches.Dispose(); setPass.Dispose(); tris.Dispose();
        }

        void OnApplicationQuit() { Write(); }

        void OnApplicationFocus(bool focused) { if (focused) skipFocusedFrame = true; }

        void Update()
        {
            // Solo se mide la partida en Casa, sin pausa (el menú no cuenta).
            if (SceneManager.GetActiveScene().name != "Casa" || GameDirector.Instance == null || !GameDirector.Instance.IsPlaying || !Application.isFocused) return;
            if (skipFocusedFrame) { skipFocusedFrame = false; return; }
            if (file == null)
            {
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Capturas"));
                Directory.CreateDirectory(dir);
                file = Path.Combine(dir, "rendimiento_dev_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                nextWrite = Time.realtimeSinceStartup + 30f;
                return; // descarta el fotograma de carga
            }
            float dt = Time.unscaledDeltaTime;
            recording += dt;
            frameMs.Add(dt * 1000f);
            if (mainThread.Valid && mainThread.LastValue > 0) mainMs.Add(mainThread.LastValue * 1e-6f);
            if (gcAlloc.Valid) gcBytes.Add(gcAlloc.LastValue);
            if (usedMem.Valid) peakUsed = System.Math.Max(peakUsed, usedMem.LastValue);
            if (sysMem.Valid) peakSys = System.Math.Max(peakSys, sysMem.LastValue);
            if (drawCalls.Valid) draws.Add(drawCalls.LastValue);
            if (batches.Valid) batchList.Add(batches.LastValue);
            if (setPass.Valid) setPassList.Add(setPass.LastValue);
            if (tris.Valid) triList.Add(tris.LastValue);
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].gpuFrameTime > 0) gpuMs.Add((float)timing[0].gpuFrameTime);
            if (Time.realtimeSinceStartup >= nextWrite) { nextWrite += 30f; Write(); }
        }

        static float Pct(List<float> v, float p)
        {
            if (v.Count == 0) return 0f;
            var s = v.OrderBy(x => x).ToList();
            return s[Mathf.Clamp(Mathf.CeilToInt(p * s.Count) - 1, 0, s.Count - 1)];
        }

        static string Stats(List<float> v, string unit)
        {
            if (v.Count == 0) return "sin datos";
            return string.Format("media {0:F2} {5} · mediana {1:F2} · p95 {2:F2} · p99 {3:F2} · máx {4:F2}", v.Average(), Pct(v, .5f), Pct(v, .95f), Pct(v, .99f), v.Max(), unit);
        }

        static string Longs(List<long> v, string unit)
        {
            if (v.Count == 0) return "sin datos";
            return string.Format("media {0:F0} · máx {1} {2}", v.Average(), v.Max(), unit);
        }

        void Write()
        {
            if (file == null || frameMs.Count < 10) return;
            var sb = new StringBuilder();
            sb.AppendLine("RENDIMIENTO — build de desarrollo, ejecutable real — " + System.DateTime.Now);
            sb.AppendLine("Equipo: " + SystemInfo.processorType + " · " + SystemInfo.processorCount + " hilos · " + SystemInfo.systemMemorySize + " MB RAM");
            sb.AppendLine("GPU: " + SystemInfo.graphicsDeviceName + " · " + SystemInfo.graphicsDeviceType + " · " + SystemInfo.graphicsMemorySize + " MB");
            sb.AppendLine("Pantalla: " + Screen.width + "x" + Screen.height + " · " + (Screen.fullScreen ? "completa" : "ventana") + " · calidad " + QualitySettings.names[QualitySettings.GetQualityLevel()] + " · vSync " + QualitySettings.vSyncCount + " · objetivo " + Application.targetFrameRate);
            sb.AppendLine("Muestras: " + frameMs.Count + " fotogramas en " + recording.ToString("F1") + " s de partida en Casa (sin menú ni pausa). Build de desarrollo: algo más lenta que release.");
            sb.AppendLine("Fotograma total: " + Stats(frameMs, "ms") + " → " + (frameMs.Count / Mathf.Max(recording, 0.01f)).ToString("F1") + " FPS medios");
            sb.AppendLine("Picos: >33,3 ms (bajo 30 FPS) = " + frameMs.Count(x => x > 33.3f) + " · >50 ms = " + frameMs.Count(x => x > 50f) + " · >100 ms = " + frameMs.Count(x => x > 100f));
            sb.AppendLine("CPU hilo principal: " + Stats(mainMs, "ms"));
            sb.AppendLine("GPU (FrameTimingManager): " + (gpuMs.Count > 0 ? Stats(gpuMs, "ms") : "sin datos (Frame Timing Stats desactivado o no soportado)"));
            sb.AppendLine("GC por fotograma: " + Longs(gcBytes, "bytes") + " · fotogramas con GC>0: " + gcBytes.Count(x => x > 0));
            sb.AppendLine("Memoria: pico usada " + (peakUsed / 1048576) + " MB · pico sistema " + (peakSys / 1048576) + " MB");
            sb.AppendLine("Draw calls: " + Longs(draws, "") + " · batches: " + Longs(batchList, "") + " · SetPass: " + Longs(setPassList, "") + " · triángulos: " + Longs(triList, ""));
            try { File.WriteAllText(file, sb.ToString()); } catch { }
        }
    }
}
#endif
