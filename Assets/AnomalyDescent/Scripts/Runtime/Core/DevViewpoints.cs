using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>
    /// Herramienta de desarrollo (solo Editor o build de desarrollo): F1–F5 colocan al jugador en encuadres fijos
    /// para comparar capturas antes/después con la misma posición. En una build final no hace nada.
    /// Los puntos se editan en el Inspector (posición de los pies, giro y cabeceo).
    /// </summary>
    public class DevViewpoints : MonoBehaviour
    {
        [System.Serializable]
        public struct Viewpoint
        {
            public string name;
            public Vector3 feet;
            public float yaw;
            public float pitch;
        }

        public Viewpoint[] points =
        {
            new Viewpoint { name = "Entrada del dormitorio", feet = new Vector3(5.4f, 0f, 5f), yaw = 270f, pitch = 8f },
            new Viewpoint { name = "Dormitorio: cama y silla", feet = new Vector3(3.9f, 0f, 2.1f), yaw = 315f, pitch = 12f },
            new Viewpoint { name = "Pasillo", feet = new Vector3(5.2f, 0f, 5f), yaw = 90f, pitch = 3f },
            new Viewpoint { name = "Refugio y escalera", feet = new Vector3(13.5f, 0f, 5.6f), yaw = 180f, pitch = 12f },
            new Viewpoint { name = "Dormitorio desde la cama", feet = new Vector3(1.4f, 0f, 3.1f), yaw = 110f, pitch = 5f },
        };

        // Teclado numérico 1–9 / 0 (o Alt+1…0; segundo banco Alt+Y/U/I/O): encuadres de prueba apuntando a objetos interactivos.
        static readonly Viewpoint[] testPoints =
        {
            new Viewpoint { name = "Puerta dormitorio (desde dentro)", feet = new Vector3(3.7f, 0f, 5.0f), yaw = 90f, pitch = 5f },
            new Viewpoint { name = "Armario / escondite", feet = new Vector3(1.4f, 0f, 2.3f), yaw = 270f, pitch = 5f },
            new Viewpoint { name = "Silla", feet = new Vector3(3.5f, 0f, 3.5f), yaw = 270f, pitch = 28f },
            new Viewpoint { name = "Cuadro del dormitorio", feet = new Vector3(2.6f, 0f, 3.0f), yaw = 180f, pitch = -8f },
            new Viewpoint { name = "Puerta del refugio (desde el pasillo)", feet = new Vector3(11.2f, 0f, 5.0f), yaw = 90f, pitch = 5f },
            new Viewpoint { name = "Reja de la escalera", feet = new Vector3(13.5f, 0f, 3.6f), yaw = 180f, pitch = 5f },
            new Viewpoint { name = "Bajada de la escalera", feet = new Vector3(13.5f, 0f, 2.1f), yaw = 180f, pitch = 25f },
            new Viewpoint { name = "Lámpara de la mesilla", feet = new Vector3(2.6f, 0f, 4.4f), yaw = 334f, pitch = 23f },
            new Viewpoint { name = "Jarrón del pasillo", feet = new Vector3(6.25f, 0f, 4.5f), yaw = 0f, pitch = 18f },
            new Viewpoint { name = "Cuadro del pasillo", feet = new Vector3(9.5f, 0f, 4.5f), yaw = 0f, pitch = -2f },
            // segundo banco: Alt+Y, Alt+U, Alt+I, Alt+O
            new Viewpoint { name = "Silla desplazada (anomalía tutorial)", feet = new Vector3(4.1f, 0f, 4.3f), yaw = 255f, pitch = 47f },
            new Viewpoint { name = "Pasillo: centro mirando al refugio", feet = new Vector3(8.0f, 0f, 5.0f), yaw = 90f, pitch = 3f },
            new Viewpoint { name = "Refugio: dentro, lejos de la puerta", feet = new Vector3(14.2f, 0f, 4.2f), yaw = 270f, pitch = 5f },
            new Viewpoint { name = "Dentro del armario (frente)", feet = new Vector3(1.0f, 0f, 2.3f), yaw = 270f, pitch = 0f },
        };
        // Teclado numérico 1–0, o con Alt pulsado las teclas 1–0 de la fila superior (portátiles sin teclado numérico).
        static readonly KeyCode[] TestKeys = { KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4, KeyCode.Keypad5,
                                               KeyCode.Keypad6, KeyCode.Keypad7, KeyCode.Keypad8, KeyCode.Keypad9, KeyCode.Keypad0 };
        // Alt+Shift cambia la distribución de teclado en Windows: el segundo banco usa Alt+Y/U/I/O/P.
        static readonly KeyCode[] Bank2 = { KeyCode.Y, KeyCode.U, KeyCode.I, KeyCode.O, KeyCode.P };
        static readonly KeyCode[] TestKeysAlt = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
                                                  KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0 };

        [Tooltip("Al saltar a un punto se guarda una captura del Game View en <proyecto>/Capturas/<etiqueta>_vistaN.png")]
        public bool autoCapture = true;
        public string captureTag = "antes";

        static readonly KeyCode[] Keys = { KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F5 };

        System.Collections.IEnumerator Capture(string file)
        {
            yield return new WaitForSeconds(0.6f);
            string dir = System.IO.Path.Combine(Application.dataPath, "../Capturas");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, file);
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[Captura] " + path);
        }

        bool sequenceRunning;

        System.Collections.IEnumerator CaptureAll(GameDirector d)
        {
            sequenceRunning = true;
            var fl = d.Player.Flashlight;
            for (int i = 0; i < points.Length; i++)
            {
                d.Player.DevTeleport(points[i].feet, points[i].yaw, points[i].pitch);
                if (fl != null && fl.Spot != null) { fl.Spot.enabled = false; }
                yield return Capture(captureTag + "_vista" + (i + 1) + "_off.png");
                yield return new WaitForSeconds(0.3f);
                if (fl != null && fl.Spot != null) { fl.Spot.enabled = true; }
                yield return Capture(captureTag + "_vista" + (i + 1) + "_on.png");
                yield return new WaitForSeconds(0.3f);
            }
            if (fl != null && fl.Spot != null) fl.Spot.enabled = fl.On;
            Debug.Log("[Captura] Secuencia completa: " + points.Length + " puntos × linterna apagada/encendida.");
            sequenceRunning = false;
        }

        void Update()
        {
            if (!(Application.isEditor || Debug.isDebugBuild)) return;
            var d = GameDirector.Instance;
            if (d == null || d.Player == null) return;
            for (int i = 0; i < Keys.Length && i < points.Length; i++)
                if (Input.GetKeyDown(Keys[i]))
                {
                    d.Player.DevTeleport(points[i].feet, points[i].yaw, points[i].pitch);
                    Debug.Log("[Vista " + (i + 1) + "] " + points[i].name);
                    if (autoCapture) StartCoroutine(Capture(captureTag + "_vista" + (i + 1) + ".png"));
                }
            for (int i = 0; i < TestKeys.Length; i++)
            {
                bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                int idx = -1;
                if (Input.GetKeyDown(TestKeys[i]) || (alt && !shift && Input.GetKeyDown(TestKeysAlt[i]))) idx = i;
                else if (alt && i < Bank2.Length && Input.GetKeyDown(Bank2[i])) idx = 10 + i;
                if (idx >= 0 && idx < testPoints.Length)
                {
                    d.Player.DevTeleport(testPoints[idx].feet, testPoints[idx].yaw, testPoints[idx].pitch);
                    Debug.Log("[Prueba " + (idx + 1) + "] " + testPoints[idx].name);
                }
            }
            // F6: captura del encuadre actual (p. ej. con la linterna encendida)
            if (Input.GetKeyDown(KeyCode.F6)) StartCoroutine(Capture(captureTag + "_libre_" + System.DateTime.Now.ToString("HHmmss") + ".png"));
            // F7: recorre todos los puntos y captura cada uno con la linterna apagada y encendida (<etiqueta>_vistaN_off/_on)
            if (Input.GetKeyDown(KeyCode.F7) && !sequenceRunning) StartCoroutine(CaptureAll(d));
        }
    }
}
