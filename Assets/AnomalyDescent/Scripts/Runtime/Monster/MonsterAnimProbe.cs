using System.Text;
using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>
    /// SOLO DESARROLLO (Editor / development build): mide si el monstruo se desplaza y si su esqueleto anima.
    /// · Raíz: velocidad real del objeto (m/s) frente a CurrentSpeed que envía la IA.
    /// · Animator: estado activo, tiempo normalizado, Speed/Searching/Chasing reales, speed del Animator.
    /// · Esqueleto: grados por segundo que giran los huesos de la malla (≈0 = malla congelada).
    /// [F9] muestra el panel en pantalla. Cada 0,5 s se añade una línea a Capturas/anim_play_&lt;monstruo&gt;.txt.
    /// </summary>
    public class MonsterAnimProbe : MonoBehaviour
    {
        public static readonly string[] KnownStates = { "Locomocion", "Reposo", "Buscar", "Despertar", "Atacar" };
        static bool overlay;
        static GUIStyle style;

        MonsterBase m;
        Animator anim;
        Transform[] bones;
        Quaternion[] lastRot;
        Vector3 lastPos;
        float sampleT, accAngle, accDist, accTime;
        readonly StringBuilder log = new StringBuilder();
        string lastLine = "";
        float lastFlush;

        public void Init(MonsterBase monster, Animator a)
        {
            m = monster;
            anim = a;
            var smr = GetComponentInChildren<SkinnedMeshRenderer>(true);
            bones = smr != null ? smr.bones : new Transform[0];
            lastRot = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++) if (bones[i] != null) lastRot[i] = bones[i].localRotation;
            lastPos = transform.position;
            log.AppendLine("t\testadoIA\tvisible\tCurrentSpeed\tvelRaiz\tAnimEstado\tnormTime\tSpeedParam\tSearching\tChasing\tanimSpeed\thuesos_deg_s");
        }

        public static string StateName(Animator a, out float normalized)
        {
            normalized = 0f;
            if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null) return "(sin Animator)";
            var st = a.GetCurrentAnimatorStateInfo(0);
            normalized = st.normalizedTime;
            string n = "?";
            foreach (var k in KnownStates) if (st.IsName(k)) { n = k; break; }
            if (a.IsInTransition(0))
            {
                var nx = a.GetNextAnimatorStateInfo(0);
                foreach (var k in KnownStates) if (nx.IsName(k)) { n += "→" + k; break; }
            }
            return n;
        }

        static string P(Animator a, string name, AnimatorControllerParameterType t)
        {
            if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null) return "-";
            foreach (var p in a.parameters)
                if (p.name == name && p.type == t)
                    return t == AnimatorControllerParameterType.Float ? a.GetFloat(name).ToString("0.00") : (a.GetBool(name) ? "1" : "0");
            return "n/a";
        }

        // ---- cámara espectadora (solo desarrollo, F12): encuadra al monstruo aunque esté en otra sala, con luz propia
        static bool spectate;
        static Camera specCam;
        static MonsterAnimProbe active;

        // ---- grabación de evidencia: con la cámara espectadora y el panel activos, guarda un JPG cada 0,25 s
        float nextShot; int shotIndex;
        System.Collections.IEnumerator Shot(string file)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            int w = tex.width / 2, h = tex.height / 2;
            var rt = RenderTexture.GetTemporary(w, h);
            Graphics.Blit(tex, rt);
            var small = new Texture2D(w, h, TextureFormat.RGB24, false);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            small.ReadPixels(new Rect(0, 0, w, h), 0, 0); small.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            try { System.IO.File.WriteAllBytes(file, small.EncodeToJPG(72)); } catch { }
            Destroy(tex); Destroy(small);
        }

        void LateUpdate()
        {
            if (spectate && overlay && m != null && m.IsActive && active == this && Time.unscaledTime >= nextShot && shotIndex < 50)
            {
                nextShot = Time.unscaledTime + 0.6f;
                string shotDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Capturas/anim_play_frames"));
                System.IO.Directory.CreateDirectory(shotDir);
                StartCoroutine(Shot(System.IO.Path.Combine(shotDir, m.monsterName.Replace(" ", "") + "_" + (shotIndex++).ToString("000") + ".jpg")));
            }
            if (!spectate || m == null || !m.IsActive || active != this) return;
            if (specCam == null)
            {
                var go = new GameObject("DEV_CamaraEspectadora");
                specCam = go.AddComponent<Camera>();
                specCam.depth = 50f; specCam.nearClipPlane = 0.05f; specCam.fieldOfView = 55f;
                var l = go.AddComponent<Light>(); l.type = LightType.Spot; l.range = 10f; l.spotAngle = 70f; l.intensity = 1.6f; l.shadows = LightShadows.Soft;
            }
            specCam.enabled = true;
            Vector3 chest = m.transform.position + Vector3.up * 1.3f;
            Vector3 dir = (Quaternion.Euler(0f, 35f, 0f) * m.transform.forward + Vector3.up * 0.15f).normalized;
            float d = 3.2f;
            RaycastHit hit;
            if (Physics.SphereCast(chest, 0.2f, dir, out hit, d, ~0, QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(m.transform))
                d = Mathf.Max(0.8f, hit.distance - 0.1f);
            specCam.transform.position = chest + dir * d;
            specCam.transform.LookAt(m.transform.position + Vector3.up * 1.0f);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9)) overlay = !overlay;
            if (Input.GetKeyDown(KeyCode.F12) && m != null && m.IsActive)
            {
                spectate = !spectate; active = this;
                if (!spectate && specCam != null) specCam.enabled = false;
            }
            if (spectate && specCam != null && (m == null || !m.IsActive) && active == this) specCam.enabled = false;
            if (m == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            accTime += dt;
            Vector3 p = transform.position; p.y = 0f;
            Vector3 lp = lastPos; lp.y = 0f;
            accDist += Vector3.Distance(p, lp);
            lastPos = transform.position;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                accAngle += Quaternion.Angle(lastRot[i], bones[i].localRotation);
                lastRot[i] = bones[i].localRotation;
            }
            sampleT += dt;
            if (sampleT < 0.5f) return;
            float boneRate = bones.Length > 0 ? accAngle / accTime / bones.Length : 0f;   // grados/s por hueso
            float rootSpeed = accDist / accTime;
            float nt;
            string st = StateName(anim, out nt);
            bool vis = anim != null && anim.gameObject.activeInHierarchy;
            lastLine = string.Format("{0:0.0}\t{1}\t{2}\t{3:0.00}\t{4:0.00}\t{5}\t{6:0.00}\t{7}\t{8}\t{9}\t{10:0.00}\t{11:0.0}",
                Time.time, m.StateName, vis ? "sí" : "no", m.CurrentSpeed, rootSpeed, st, nt,
                P(anim, "Speed", AnimatorControllerParameterType.Float), P(anim, "Searching", AnimatorControllerParameterType.Bool),
                P(anim, "Chasing", AnimatorControllerParameterType.Bool), anim != null ? anim.speed : 0f, boneRate);
            if (m.IsActive) log.AppendLine(lastLine);
            sampleT = accAngle = accDist = accTime = 0f;
            if (Time.unscaledTime - lastFlush > 5f) Flush();
        }

        void Flush()
        {
            lastFlush = Time.unscaledTime;
            try
            {
                string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Capturas"));
                System.IO.Directory.CreateDirectory(dir);
                string name = m != null ? m.monsterName.Replace(" ", "") : "Monstruo";
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "anim_play_" + name + ".txt"), log.ToString());
            }
            catch (System.Exception e) { Debug.LogWarning("[AnimProbe] No se pudo escribir el registro: " + e.Message); }
        }

        void OnDisable() { if (m != null) Flush(); }
        void OnApplicationQuit() { if (m != null) Flush(); }

        void OnGUI()
        {
            if (!overlay || m == null || !m.IsActive) return;
            var cols = lastLine.Split('\t');
            if (cols.Length < 12) return;
            string txt = m.monsterName + "  IA: " + cols[1] + "  visible: " + cols[2] +
                         "\nCurrentSpeed " + cols[3] + " m/s   raíz real " + cols[4] + " m/s" +
                         "\nAnimator: " + cols[5] + "  t=" + cols[6] + "  animSpeed " + cols[10] +
                         "\nSpeed=" + cols[7] + "  Searching=" + cols[8] + "  Chasing=" + cols[9] +
                         "\nEsqueleto: " + cols[11] + " °/s por hueso" + (float.Parse(cols[11]) < 1f ? "  ← CONGELADO" : "");
            if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(14, Screen.height / 50), richText = false };
            float w = style.fontSize * 34f, h = style.fontSize * 7.6f;
            var r = new Rect(Screen.width - w - 40f, Screen.height * 0.12f, w, h);
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(0.75f, 1f, 0.75f);
            GUI.Label(new Rect(r.x + 10f, r.y + 6f, r.width - 20f, r.height - 12f), txt, style);
            GUI.color = Color.white;
        }
    }
}
