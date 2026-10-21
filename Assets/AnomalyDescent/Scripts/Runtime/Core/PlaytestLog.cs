using System.IO;
using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>
    /// SOLO DESARROLLO (Editor / build de desarrollo): copia a Capturas/partida_&lt;fecha&gt;.txt los mensajes de juego
    /// ([Vuelta], [Anomalía], [Cámara], [Reporte], [Presencia], [Voz], [Amenaza], [Casa]...) con el tiempo de partida.
    /// Sirve como registro verificable de una partida de prueba. No cambia nada del juego.
    /// </summary>
    public static class PlaytestLog
    {
        static string file;
        static float start;

        public static void Begin()
        {
            if (!(Application.isEditor || Debug.isDebugBuild) || file != null) return;
            try
            {
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Capturas"));
                Directory.CreateDirectory(dir);
                file = Path.Combine(dir, "partida_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                File.WriteAllText(file, "PARTIDA DE PRUEBA — " + System.DateTime.Now + "\n");
                start = Time.realtimeSinceStartup;
                Application.logMessageReceived += OnLog;
            }
            catch (System.Exception e) { Debug.LogWarning("[PlaytestLog] " + e.Message); file = null; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Application.logMessageReceived -= OnLog; file = null; }

        static void OnLog(string msg, string stack, LogType type)
        {
            if (file == null || string.IsNullOrEmpty(msg)) return;
            bool game = msg.StartsWith("[") && !msg.StartsWith("[Build]") && !msg.StartsWith("[Poly");
            if (!game && type != LogType.Error && type != LogType.Exception) return;
            float t = Time.realtimeSinceStartup - start;
            string line = ((int)(t / 60)).ToString("00") + ":" + (t % 60).ToString("00.0") + "  " + (type == LogType.Log ? "" : type.ToString().ToUpper() + " ") + msg;
            if (type == LogType.Exception) line += "\n" + stack;
            try { File.AppendAllText(file, line + "\n"); } catch { }
        }
    }
}
