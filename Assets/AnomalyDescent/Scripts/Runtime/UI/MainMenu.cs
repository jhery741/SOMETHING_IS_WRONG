using UnityEngine;
using UnityEngine.SceneManagement;

namespace SomethingIsWrong
{
    // Lógica de interfaz. El objeto que contiene este componente se coloca a mano en Menu.unity.
    public class MainMenu : MonoBehaviour
    {
        public TextAsset licenses;
        string page = "Inicio";
        Vector2 creditsScroll;
        GUIStyle title, text, button, toggle;
        bool loading;
        string creditsBody;

        void Awake()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            GameSettings.Load();
            GameSettings.Apply();
            if (licenses == null) licenses = Resources.Load<TextAsset>("CreditosLicencias");
            creditsBody = FormatCredits(licenses != null ? licenses.text : "Registro completo: Art/LICENCIAS.md.");
        }

        void Begin(bool resume)
        {
            if (loading) return;
            loading = true;
            GameSettings.Save();
            // Nueva partida conserva la constancia de haber terminado el juego.
            if (!resume) SaveData.CheckpointLap = 1;
            SaveData.PendingStartLap = resume ? SaveData.CheckpointLap : 0;
            SceneManager.LoadScene("Casa");
        }

        void OnGUI()
        {
            float scale = Screen.height / 1080f;
            if (scale <= 0f) return;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 58, alignment = TextAnchor.MiddleCenter };
                text = new GUIStyle(GUI.skin.label) { fontSize = 24, wordWrap = true };
                button = new GUIStyle(GUI.skin.button) { fontSize = 27 };
                toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 24 };
            }
            GUI.color = new Color(0.018f, 0.022f, 0.028f);
            GUI.DrawTexture(new Rect(0, 0, width, 1080), Texture2D.whiteTexture);
            GUI.color = new Color(0.92f, 0.89f, 0.82f);
            GUI.Label(new Rect(0, 65, width, 90), "ANOMALY: DESCENT", title);
            float x = (width - 580) / 2f;
            if (page == "Inicio")
            {
                GUI.Label(new Rect(x, 180, 580, 100), "Una casa. Cuatro vueltas. Alguien escucha.", text);
                if (GUI.Button(new Rect(x, 330, 580, 65), "Nueva partida", button)) Begin(false);
                GUI.enabled = SaveData.CheckpointLap > 1;
                if (GUI.Button(new Rect(x, 415, 580, 65), "Continuar · vuelta " + SaveData.CheckpointLap, button)) Begin(true);
                GUI.enabled = true;
                if (GUI.Button(new Rect(x, 500, 580, 65), "Opciones", button)) page = "Opciones";
                if (GUI.Button(new Rect(x, 585, 580, 65), "Créditos", button)) page = "Créditos";
                if (GUI.Button(new Rect(x, 670, 580, 65), "Salir", button)) Quit();
                if (SaveData.Completed) GUI.Label(new Rect(x, 780, 580, 50), "Has completado el informe de la casa.", text);
            }
            else if (page == "Opciones")
            {
                float y = 190;
                GameSettings.MasterVolume = Slider(x, ref y, "Volumen general", GameSettings.MasterVolume, 0, 1, true);
                GameSettings.SfxVolume = Slider(x, ref y, "Efectos", GameSettings.SfxVolume, 0, 1, true);
                GameSettings.AmbienceVolume = Slider(x, ref y, "Ambiente", GameSettings.AmbienceVolume, 0, 1, true);
                GameSettings.VoiceVolume = Slider(x, ref y, "Voces", GameSettings.VoiceVolume, 0, 1, true);
                GameSettings.Sensitivity = Slider(x, ref y, "Sensibilidad", GameSettings.Sensitivity, 0.02f, 0.35f, false);
                GameSettings.FieldOfView = Slider(x, ref y, "Campo de visión", GameSettings.FieldOfView, 55, 100, false);
                GameSettings.InvertY = Toggle(x, ref y, "Invertir eje vertical", GameSettings.InvertY);
                GameSettings.CameraBob = Toggle(x, ref y, "Balanceo de cámara", GameSettings.CameraBob);
                GameSettings.Grain = Toggle(x, ref y, "Grano de película", GameSettings.Grain);
                GameSettings.Effects = Toggle(x, ref y, "Viñeta", GameSettings.Effects);
                GameSettings.Captions = Toggle(x, ref y, "Subtítulos de sonidos", GameSettings.Captions);
                GUI.Label(new Rect(x, y, 580, 32), "Resolución de render (altura máxima)", text);
                GameSettings.SetResolutionChoice(GUI.Toolbar(new Rect(x, y + 34, 580, 40), GameSettings.ResolutionChoice, GameSettings.ResolutionLabels, button));
                GameSettings.Apply();
            }
            else
            {
                var area = new Rect(Mathf.Max(35, (width - 1100) / 2), 190, Mathf.Min(1100, width - 70), 650);
                string body = "ANOMALY: DESCENT\nProyecto de Franz.\n\nEl Ciego: modelo existente del proyecto, con animaciones de Meshy aportadas por el usuario. Voz: susurro sintetizado con subtítulos.\n\n" +
                    creditsBody;
                float height = text.CalcHeight(new GUIContent(body), area.width - 35);
                creditsScroll = GUI.BeginScrollView(area, creditsScroll, new Rect(0, 0, area.width - 35, height + 20));
                GUI.Label(new Rect(0, 0, area.width - 35, height), body, text);
                GUI.EndScrollView();
            }
            if (page != "Inicio" && GUI.Button(new Rect(x, 935, 580, 60), "Guardar y volver", button))
            { GameSettings.Save(); page = "Inicio"; }
        }

        float Slider(float x, ref float y, string label, float value, float min, float max, bool percent)
        {
            GUI.Label(new Rect(x, y, 580, 32), label + ": " + (percent ? Mathf.RoundToInt(value * 100) + "%" : value.ToString("0.00")), text);
            value = GUI.HorizontalSlider(new Rect(x, y + 36, 580, 22), value, min, max);
            y += 67;
            return value;
        }
        static string FormatCredits(string source)
        {
            var result = new System.Text.StringBuilder();
            foreach (string raw in source.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("|---") || line.StartsWith("| Recurso |")) continue;
                if (line.StartsWith("|"))
                {
                    string[] cells = line.Trim('|').Split('|');
                    if (cells.Length < 6) continue;
                    result.AppendLine(cells[0].Trim() + " — " + cells[3].Trim());
                    result.AppendLine(cells[2].Trim() + " · " + cells[5].Trim());
                    result.AppendLine(cells[4].Trim());
                    result.AppendLine();
                }
                else result.AppendLine(line.TrimStart('#', ' '));
            }
            return result.ToString();
        }
        bool Toggle(float x, ref float y, string label, bool value)
        { value = GUI.Toggle(new Rect(x, y, 580, 36), value, "  " + label, toggle); y += 43; return value; }
        void OnApplicationQuit() { GameSettings.Save(); }
        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
