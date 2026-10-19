using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SomethingIsWrong.EditorTools
{
    /// <summary>
    /// ANIMACIÓN PROVISIONAL de los monstruos (diagnóstico 18-10-2026).
    ///
    /// Causa del "monstruo congelado": en los tres FBX de Meshy (exportación *Merged_Animations*) las tomas Walking,
    /// Listening_Gesture y Crawl_and_Look_Back contienen SOLO curvas constantes (2 claves iguales por curva). Unity las
    /// importa bien, pero no hay movimiento que reproducir. Running es la única toma con datos reales.
    ///
    /// Esta herramienta NO crea modelos ni inventa geometría: genera clips Humanoid (curvas de músculo) a partir de los
    /// datos reales de Running y de poses diseñadas a mano, todos con el prefijo PROV_ para que nadie los confunda con
    /// animación final:
    ///   PROV_Reposo     — postura encorvada con respiración (sustituye al Walk ×0.15 / clip vacío)
    ///   PROV_Caminar    — paso lento derivado de Running (amplitud reducida, ciclo más largo)
    ///   PROV_Buscar     — El Ciego: escucha girando e inclinando la cabeza · El Torcido: olfatea · La Presencia: inmóvil y torcida al ser observada
    ///   PROV_Despertar  — se incorpora desde agachado (El Torcido: se encoge y tensa antes de embestir)
    ///   PROV_Atacar     — levanta los brazos y se abalanza
    /// Rehace el Animator de cada monstruo EN EL MISMO ASSET (mismo GUID: escenas y prefabs siguen enlazados) con umbrales
    /// iguales a las velocidades reales de la IA. Cuando haya FBX buenos de Meshy, el asistente de integración los sustituye.
    /// </summary>
    public static class ProvisionalMonsterAnimation
    {
        const string Root = "Assets/AnomalyDescent/Art/Monsters";

        class Plan
        {
            public string folder, label;
            public float walkSpeed, runSpeed, lungeSpeed;   // m/s reales de la IA
            public float walkCycle;                         // s por ciclo de PROV_Caminar
            public float walkAmp, armAmp;                   // amplitud respecto a Running
            public float awakenLen; public bool awakenIsWindup;
            public string searchKind;                       // "escuchar" | "olfatear" | "observada"
        }

        static readonly Plan[] Plans =
        {
            new Plan { folder = "ElCiego", label = "El Ciego", walkSpeed = 1.4f, runSpeed = 3.6f, walkCycle = 1.25f, walkAmp = 0.5f, armAmp = 0.35f,
                       awakenLen = 3.0f, searchKind = "escuchar" },
            new Plan { folder = "ElTorcido", label = "El Torcido", walkSpeed = 1.8f, runSpeed = 3.6f, lungeSpeed = 7.5f, walkCycle = 0.95f, walkAmp = 0.6f, armAmp = 0.5f,
                       awakenLen = 0.9f, awakenIsWindup = true, searchKind = "olfatear" },
            new Plan { folder = "LaPresencia", label = "La Presencia", walkSpeed = 1.6f, runSpeed = 3.6f, walkCycle = 1.5f, walkAmp = 0.3f, armAmp = 0.08f,
                       awakenLen = 3.0f, searchKind = "observada" },
        };

        [MenuItem("ANOMALY DESCENT/Monstruos/Reparar animaciones (clips PROV_ + Animator)", priority = 122)]
        public static void RepairAll()
        {
            var sb = new StringBuilder("ANIMACIÓN PROVISIONAL — " + System.DateTime.Now + "\n");
            foreach (var p in Plans) Repair(p, sb);
            AssetDatabase.SaveAssets();
            Write("anim_reparacion.txt", sb.ToString());
            Debug.Log("[Anim] Reparación terminada. Detalle en Capturas/anim_reparacion.txt");
        }

        // ------------------------------------------------------------------------------------------------
        static void Repair(Plan p, StringBuilder sb)
        {
            string dir = Root + "/" + p.folder;
            sb.AppendLine("\n== " + p.label + " ==");
            string modelPath = AssetDatabase.FindAssets("t:Model", new[] { dir + "/Meshy_Export" }).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
            var ctrl = AssetDatabase.FindAssets("t:AnimatorController", new[] { dir + "/Animator" })
                .Select(g => AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
            if (modelPath == null || ctrl == null) { sb.AppendLine("Falta FBX o Animator: se omite."); return; }

            // El Torcido estaba en Generic porque su clip de gatear no admitía retarget; ese clip está vacío, así que se pasa a
            // Humanoid (su esqueleto mixamorig tiene todos los huesos obligatorios) para poder animarlo con músculos.
            var imp = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            if (imp.animationType != ModelImporterAnimationType.Human)
            {
                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.SaveAndReimport();
                sb.AppendLine("Rig cambiado a Humanoid.");
            }
            var avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isHuman || !avatar.isValid)
            {
                sb.AppendLine("ERROR: el Avatar no es Humanoid válido; no se pueden generar clips de músculo.");
                return;
            }
            var run = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__") && c.name.Contains("Running"));
            if (run == null || !run.humanMotion) { sb.AppendLine("ERROR: no hay clip Running humanoide."); return; }

            var src = new Dictionary<string, AnimationCurve>();
            foreach (var b in AnimationUtility.GetCurveBindings(run))
                if (b.type == typeof(Animator) && b.path == "") src[b.propertyName] = AnimationUtility.GetEditorCurve(run, b);
            var mean = new Dictionary<string, float>();
            foreach (var kv in src)
            {
                float s = 0f; const int N = 34;
                for (int i = 0; i < N; i++) s += kv.Value.Evaluate(run.length * i / N);
                mean[kv.Key] = s / N;
            }
            sb.AppendLine("Running: " + run.length.ToString("0.00") + " s, " + src.Count + " curvas. Medias: Spine F-B " + M(mean, "Spine Front-Back") +
                          ", L UpperLeg F-B " + M(mean, "Left Upper Leg Front-Back") + ", L Arm D-U " + M(mean, "Left Arm Down-Up") +
                          ", L Forearm " + M(mean, "Left Forearm Stretch") + ", RootT.y " + M(mean, "RootT.y"));

            var ctx = new Ctx { p = p, src = src, mean = mean, runLen = run.length };
            var reposo = Save(dir, "PROV_Reposo", Build(ctx, 4f, 30, true, Reposo), true);
            var caminar = Save(dir, "PROV_Caminar", Build(ctx, p.walkCycle, 30, true, Caminar), true);
            var buscar = Save(dir, "PROV_Buscar", Build(ctx, p.searchKind == "observada" ? 2f : 6f, 30, true, Buscar), true);
            var despertar = Save(dir, "PROV_Despertar", Build(ctx, p.awakenLen, 30, false, Despertar), false);
            var atacar = Save(dir, "PROV_Atacar", Build(ctx, 0.9f, 30, false, Atacar), false);

            RebuildController(ctrl, p, reposo, caminar, run, buscar, despertar, atacar, sb);
        }

        static string M(Dictionary<string, float> d, string k) { float v; return d.TryGetValue(k, out v) ? v.ToString("0.00") : "—"; }

        // ------------------------------------------------------------------------------------------------ poses
        class Ctx
        {
            public Plan p; public Dictionary<string, AnimationCurve> src; public Dictionary<string, float> mean; public float runLen;
            public float Mean(string k) { float v; return mean.TryGetValue(k, out v) ? v : 0f; }
            public float Run(string k, float t) { AnimationCurve c; return src.TryGetValue(k, out c) ? c.Evaluate(t) : 0f; }
        }

        delegate void PoseFn(Ctx c, float t, float len, Dictionary<string, float> o);

        const float TAU = Mathf.PI * 2f;
        static float S(float t, float period, float phase = 0f) { return Mathf.Sin(TAU * t / period + phase); }

        static void Add(Dictionary<string, float> o, string k, float v) { float cur; o.TryGetValue(k, out cur); o[k] = cur + v; }
        static void Both(Dictionary<string, float> o, string k, float v) { Add(o, "Left " + k, v); Add(o, "Right " + k, v); }
        /// <summary>Lleva el músculo a un valor absoluto (compensa la media de Running, que es la base de todos los clips).</summary>
        static void SetBoth(Ctx c, Dictionary<string, float> o, string k, float target)
        {
            foreach (var side in new[] { "Left ", "Right " })
            {
                float cur; o.TryGetValue(side + k, out cur);
                o[side + k] = target - c.Mean(side + k);
            }
        }

        /// <summary>Postura base de cada monstruo (desviación sobre la media de Running). Valores en unidades de músculo.</summary>
        static void Stance(Ctx c, Dictionary<string, float> o)
        {
            // La media de Running ya es una postura erguida algo inclinada; se corrige hacia una posición de pie inquietante.
            Add(o, "Spine Front-Back", 0.15f);
            Add(o, "Chest Front-Back", 0.1f);
            Add(o, "Head Nod Down-Up", -0.15f);
            // Piernas y brazos a valores absolutos (no relativos a la carrera): de pie, rodillas casi rectas, brazos colgando
            SetBoth(c, o, "Upper Leg Front-Back", 0.05f);
            SetBoth(c, o, "Lower Leg Stretch", 0.8f);
            SetBoth(c, o, "Foot Up-Down", 0f);
            SetBoth(c, o, "Arm Down-Up", -0.55f);
            SetBoth(c, o, "Arm Front-Back", 0.1f);
            SetBoth(c, o, "Forearm Stretch", 0.55f);
            Add(o, "RootT.y", 0.03f);
            switch (c.p.folder)
            {
                case "ElCiego":
                    Add(o, "Head Nod Down-Up", 0.25f);          // cara al frente, "escuchando"
                    break;
                case "ElTorcido":
                    Add(o, "Spine Front-Back", 0.3f);           // muy encorvado
                    Add(o, "Chest Front-Back", 0.25f);
                    Add(o, "Head Nod Down-Up", 0.45f);          // mira hacia delante pese a la joroba
                    Both(o, "Lower Leg Stretch", -0.3f);        // rodillas dobladas, listo para saltar
                    Both(o, "Upper Leg Front-Back", 0.2f);
                    Add(o, "RootT.y", -0.08f);
                    Add(o, "Spine Left-Right", 0.15f);          // asimetría: "torcido"
                    break;
                case "LaPresencia":
                    Add(o, "Head Tilt Left-Right", 0.55f);      // cabeza ladeada de forma antinatural
                    Add(o, "Neck Tilt Left-Right", 0.3f);
                    Add(o, "Spine Front-Back", -0.1f);          // erguida, rígida
                    Both(o, "Arm Down-Up", -0.15f);
                    break;
            }
        }

        static void Reposo(Ctx c, float t, float len, Dictionary<string, float> o)
        {
            Stance(c, o);
            float br = S(t, len);                         // respiración lenta (un ciclo por clip)
            Add(o, "Chest Front-Back", 0.1f * br);
            Add(o, "Spine Front-Back", 0.05f * br);
            Add(o, "Head Nod Down-Up", 0.06f * br);
            Both(o, "Arm Down-Up", -0.05f * br);
            Add(o, "Head Tilt Left-Right", 0.05f * S(t, len, 1f));
            if (c.p.folder == "ElTorcido") Add(o, "Head Turn Left-Right", 0.12f * S(t * 2f, len));   // tic nervioso
        }

        static void Caminar(Ctx c, float t, float len, Dictionary<string, float> o)
        {
            Stance(c, o);
            float rt = (t / len) * c.runLen;                // un ciclo de Running estirado a "walkCycle"
            foreach (var k in c.src.Keys)
            {
                if (k.StartsWith("RootQ") || k == "RootT.x" || k == "RootT.z") continue;
                float amp = k.Contains("Arm") || k.Contains("Forearm") || k.Contains("Shoulder") || k.Contains("Hand") ? c.p.armAmp : c.p.walkAmp;
                if (k == "RootT.y") amp *= 0.5f;
                Add(o, k, amp * (c.Run(k, rt) - c.Mean(k)));
            }
        }

        static void Buscar(Ctx c, float t, float len, Dictionary<string, float> o)
        {
            Stance(c, o);
            switch (c.p.searchKind)
            {
                case "escuchar":   // gira la cabeza a un lado y otro, inclinando la oreja; se detiene en cada lado
                    float sweep = Mathf.Clamp(S(t, len) * 1.6f, -1f, 1f);
                    Add(o, "Head Turn Left-Right", 0.8f * sweep);
                    Add(o, "Neck Turn Left-Right", 0.5f * sweep);
                    Add(o, "Head Tilt Left-Right", -0.45f * sweep);
                    Add(o, "Spine Twist Left-Right", 0.3f * sweep);
                    Add(o, "Chest Twist Left-Right", 0.25f * sweep);
                    Both(o, "Arm Front-Back", 0.15f * Mathf.Abs(sweep));
                    Add(o, "Head Nod Down-Up", 0.1f * S(t, len * 0.5f));
                    break;
                case "olfatear":   // cabeza baja que se mueve a tirones
                    Add(o, "Head Nod Down-Up", -0.25f + 0.12f * Mathf.Sign(S(t, 0.75f)));
                    Add(o, "Head Turn Left-Right", 0.7f * S(t, len));
                    Add(o, "Spine Twist Left-Right", 0.25f * S(t, len));
                    Add(o, "Spine Front-Back", 0.15f);
                    Add(o, "Chest Front-Back", 0.06f * S(t, 0.6f));
                    break;
                case "observada":  // inmóvil a propósito (canon: no se mueve mientras la miras): brazos algo adelantados
                    Both(o, "Arm Front-Back", 0.35f);
                    Both(o, "Arm Down-Up", 0.1f);
                    Add(o, "Head Nod Down-Up", -0.1f);
                    break;
            }
        }

        static float Smooth(float a, float b, float x) { return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x)); }

        static void Crouch(Dictionary<string, float> o, float w)
        {
            Add(o, "Spine Front-Back", 0.7f * w);
            Add(o, "Chest Front-Back", 0.4f * w);
            Add(o, "Head Nod Down-Up", -0.5f * w);
            Both(o, "Upper Leg Front-Back", 0.75f * w);
            Both(o, "Lower Leg Stretch", -0.9f * w);
            Both(o, "Foot Up-Down", 0.3f * w);
            Both(o, "Arm Down-Up", -0.3f * w);
            Both(o, "Forearm Stretch", -0.4f * w);
            Add(o, "RootT.y", -0.32f * w);
        }

        static void Despertar(Ctx c, float t, float len, Dictionary<string, float> o)
        {
            Stance(c, o);
            float x = t / len;
            if (c.p.awakenIsWindup)
            {
                // El Torcido: se encoge y echa los brazos atrás (anticipación legible de la embestida)
                float w = Smooth(0f, 0.7f, x);
                Crouch(o, 0.6f * w);
                Both(o, "Arm Front-Back", -0.5f * w);
                Add(o, "Head Nod Down-Up", 0.6f * w);   // la cabeza sigue mirando al jugador
                Add(o, "Jaw Close", -0.8f * w);
                return;
            }
            Crouch(o, 1f - Smooth(0.1f, 0.8f, x));      // se incorpora
            float jerk = Mathf.Exp(-Mathf.Pow((x - 0.88f) / 0.04f, 2f));   // sacudida de cabeza al final
            Add(o, "Head Nod Down-Up", 0.35f * jerk);
            Add(o, "Head Tilt Left-Right", (c.p.folder == "LaPresencia" ? 0.5f : -0.3f) * jerk);
            Add(o, "Jaw Close", -0.6f * Smooth(0.75f, 0.9f, x));
        }

        static void Atacar(Ctx c, float t, float len, Dictionary<string, float> o)
        {
            Stance(c, o);
            float x = t / len;
            float up = Smooth(0f, 0.35f, x), grab = Smooth(0.45f, 0.75f, x);
            Both(o, "Arm Down-Up", 1.4f * up - 0.6f * grab);
            Both(o, "Arm Front-Back", 1.0f * up);
            Both(o, "Forearm Stretch", 0.3f * up - 0.6f * grab);
            Add(o, "Spine Front-Back", 0.35f * up);
            Add(o, "Head Nod Down-Up", 0.3f * up);
            Add(o, "Jaw Close", -1f * up);
            Both(o, "Upper Leg Front-Back", 0.2f * up);
            Both(o, "Lower Leg Stretch", -0.25f * up);
        }

        // ------------------------------------------------------------------------------------------------ clips
        static AnimationClip Build(Ctx c, float len, int fps, bool loop, PoseFn fn)
        {
            var clip = new AnimationClip { frameRate = fps };
            int frames = Mathf.Max(2, Mathf.RoundToInt(len * fps));
            var keys = new Dictionary<string, List<Keyframe>>();
            var o = new Dictionary<string, float>();
            for (int f = 0; f <= frames; f++)
            {
                float t = len * f / frames;
                o.Clear();
                fn(c, t, len, o);
                // Todas las curvas de Running (incluidos dedos y raíz) + las de diseño que no estuvieran
                foreach (var k in c.src.Keys.Union(o.Keys))
                {
                    if (k.StartsWith("RootQ")) continue;
                    float d; o.TryGetValue(k, out d);
                    float v = c.Mean(k) + d;
                    if (!k.StartsWith("RootT")) v = Mathf.Clamp(v, -1.2f, 1.2f);
                    List<Keyframe> l;
                    if (!keys.TryGetValue(k, out l)) keys[k] = l = new List<Keyframe>();
                    l.Add(new Keyframe(t, v));
                }
            }
            foreach (var kv in keys)
            {
                var curve = new AnimationCurve(kv.Value.ToArray());
                for (int i = 0; i < curve.length; i++) AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                for (int i = 0; i < curve.length; i++) AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), kv.Key), curve);
            }
            // Orientación de la raíz: la media de Running (erguido, mirando al frente)
            foreach (var q in new[] { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" })
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), q),
                    AnimationCurve.Constant(0f, len, MeanQ(c, q)));
            var st = AnimationUtility.GetAnimationClipSettings(clip);
            st.loopTime = loop;
            st.loopBlend = loop;
            st.loopBlendOrientation = st.loopBlendPositionY = st.loopBlendPositionXZ = true;   // raíz horneada en la pose: sin root motion
            st.keepOriginalOrientation = st.keepOriginalPositionY = st.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, st);
            return clip;
        }

        static float MeanQ(Ctx c, string comp)
        {
            var q = new Quaternion(c.Mean("RootQ.x"), c.Mean("RootQ.y"), c.Mean("RootQ.z"), c.Mean("RootQ.w"));
            float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (mag < 1e-4f) q = Quaternion.identity; else q = new Quaternion(q.x / mag, q.y / mag, q.z / mag, q.w / mag);
            switch (comp) { case "RootQ.x": return q.x; case "RootQ.y": return q.y; case "RootQ.z": return q.z; default: return q.w; }
        }

        /// <summary>Guarda el clip; si ya existe se copia encima para conservar el GUID.</summary>
        static AnimationClip Save(string dir, string name, AnimationClip clip, bool loop)
        {
            string path = dir + "/Animator/" + name + ".anim";
            clip.name = name;
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null) { EditorUtility.CopySerialized(clip, existing); EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        // ------------------------------------------------------------------------------------------------ Animator
        static void RebuildController(AnimatorController ctrl, Plan p, AnimationClip reposo, AnimationClip caminar, AnimationClip run,
                                      AnimationClip buscar, AnimationClip despertar, AnimationClip atacar, StringBuilder sb)
        {
            var sm = ctrl.layers[0].stateMachine;
            foreach (var t in sm.anyStateTransitions.ToArray()) sm.RemoveAnyStateTransition(t);
            foreach (var cs in sm.states.ToArray())
            {
                if (cs.state.motion is BlendTree old && AssetDatabase.GetAssetPath(old) == AssetDatabase.GetAssetPath(ctrl))
                    AssetDatabase.RemoveObjectFromAsset(old);
                sm.RemoveState(cs.state);
            }
            foreach (var n in new[] { "Speed", "Chasing", "Searching", "Attack", "Awaken" })
                if (!ctrl.parameters.Any(x => x.name == n))
                    ctrl.AddParameter(n, n == "Speed" ? AnimatorControllerParameterType.Float :
                                         n == "Attack" || n == "Awaken" ? AnimatorControllerParameterType.Trigger : AnimatorControllerParameterType.Bool);

            BlendTree tree;
            var loco = ctrl.CreateBlendTreeInController("Locomocion", out tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(reposo, 0f);
            tree.AddChild(caminar, p.walkSpeed);
            tree.AddChild(run, p.runSpeed);
            if (p.lungeSpeed > p.runSpeed) tree.AddChild(run, p.lungeSpeed);
            var ch = tree.children;
            for (int i = 0; i < ch.Length; i++)
            {
                ch[i].timeScale = 1f;
                if (i == 3) ch[i].timeScale = p.lungeSpeed / p.runSpeed * 0.75f;   // embestida: zancada más larga y algo más rápida
            }
            tree.children = ch;
            sm.defaultState = loco;
            loco.writeDefaultValues = true;

            var sBuscar = sm.AddState("Buscar");
            sBuscar.motion = buscar;
            var t1 = loco.AddTransition(sBuscar); t1.AddCondition(AnimatorConditionMode.If, 0f, "Searching"); t1.hasExitTime = false; t1.duration = 0.25f;
            var t2 = sBuscar.AddTransition(loco); t2.AddCondition(AnimatorConditionMode.IfNot, 0f, "Searching"); t2.hasExitTime = false; t2.duration = 0.25f;
            // Si empieza a moverse mientras "busca", vuelve a la locomoción (evita deslizarse con la pose de búsqueda)
            var t3 = sBuscar.AddTransition(loco); t3.AddCondition(AnimatorConditionMode.Greater, 0.4f, "Speed"); t3.hasExitTime = false; t3.duration = 0.25f;
            t1.AddCondition(AnimatorConditionMode.Less, 0.4f, "Speed");

            var sDesp = sm.AddState("Despertar");
            sDesp.motion = despertar;
            var a1 = sm.AddAnyStateTransition(sDesp); a1.AddCondition(AnimatorConditionMode.If, 0f, "Awaken"); a1.hasExitTime = false; a1.duration = 0.1f; a1.canTransitionToSelf = false;
            var a2 = sDesp.AddTransition(loco); a2.hasExitTime = true; a2.exitTime = 0.98f; a2.duration = 0.15f;

            var sAtk = sm.AddState("Atacar");
            sAtk.motion = atacar;
            var b1 = sm.AddAnyStateTransition(sAtk); b1.AddCondition(AnimatorConditionMode.If, 0f, "Attack"); b1.hasExitTime = false; b1.duration = 0.08f; b1.canTransitionToSelf = false;
            // Tras atacar la partida termina (pantalla de derrota): se queda en la última pose.

            EditorUtility.SetDirty(ctrl);
            sb.AppendLine("Animator " + ctrl.name + ": Locomocion = PROV_Reposo 0 · PROV_Caminar " + p.walkSpeed + " · Running " + p.runSpeed +
                          (p.lungeSpeed > p.runSpeed ? " · Running ×" + (p.lungeSpeed / p.runSpeed * 0.75f).ToString("0.00") + " a " + p.lungeSpeed : "") +
                          " m/s; Buscar (Searching, Speed<0.4) = PROV_Buscar (" + p.searchKind + "); AnyState→Despertar [Awaken]; AnyState→Atacar [Attack].");
        }

        static void Write(string file, string text)
        {
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Capturas"));
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, file), text);
        }

        // ------------------------------------------------------------------------------------------------ comprobación visual
        /// <summary>
        /// Renderiza, fuera de la vista, 8 fotogramas de cada clip del Animator de cada monstruo sobre su prefab real y
        /// los guarda como tira PNG en Capturas/anim_tiras/. Equivale a mirar la vista previa del clip en el Inspector.
        /// </summary>
        [MenuItem("ANOMALY DESCENT/Monstruos/Renderizar tiras de animación (comprobación)", priority = 123)]
        public static void RenderStrips()
        {
            string outDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Capturas/anim_tiras"));
            System.IO.Directory.CreateDirectory(outDir);
            var offset = new Vector3(0f, -500f, 0f);
            var lightGo = new GameObject("DiagLight") { hideFlags = HideFlags.HideAndDontSave };
            var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.4f;
            lightGo.transform.rotation = Quaternion.Euler(35f, -40f, 0f);
            var camGo = new GameObject("DiagCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.32f, 0.33f, 0.36f);
            cam.fieldOfView = 30f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 30f; cam.enabled = false;
            var rt = new RenderTexture(256, 384, 24);
            cam.targetTexture = rt;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ground.hideFlags = HideFlags.HideAndDontSave;
            ground.transform.position = offset; ground.transform.rotation = Quaternion.Euler(90f, 0f, 0f); ground.transform.localScale = Vector3.one * 6f;
            var oldAmbient = RenderSettings.ambientLight; var oldFog = RenderSettings.fog;
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.55f); RenderSettings.fog = false;
            int count = 0;
            try
            {
                foreach (var dir in AssetDatabase.GetSubFolders(Root))
                {
                    var prefab = AssetDatabase.FindAssets("t:Prefab", new[] { dir + "/Prefabs" }).Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
                    var ctrl = AssetDatabase.FindAssets("t:AnimatorController", new[] { dir + "/Animator" }).Select(g => AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
                    if (prefab == null || ctrl == null) continue;
                    var go = Object.Instantiate(prefab, offset, Quaternion.identity);
                    go.hideFlags = HideFlags.HideAndDontSave;
                    try
                    {
                        var anim = go.GetComponentInChildren<Animator>();
                        var rends = go.GetComponentsInChildren<Renderer>();
                        foreach (var r in rends) if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                        var clips = new List<AnimationClip>();
                        foreach (var cs in ctrl.layers[0].stateMachine.states)
                        {
                            if (cs.state.motion is AnimationClip ac) clips.Add(ac);
                            else if (cs.state.motion is BlendTree bt) foreach (var c in bt.children) if (c.motion is AnimationClip bc) clips.Add(bc);
                        }
                        // Encuadre fijo por monstruo (pose de reposo de la escena), igual para todos sus clips
                        if (clips.Count > 0) clips[0].SampleAnimation(anim.gameObject, 0f);
                        Bounds fb = rends[0].bounds; foreach (var r in rends) fb.Encapsulate(r.bounds);
                        float fh = Mathf.Max(1f, fb.size.y);
                        Vector3 center = new Vector3(fb.center.x, offset.y + fh * 0.5f, fb.center.z);
                        foreach (var clip in clips.Distinct())
                        {
                            const int F = 8;
                            var strip = new Texture2D(256 * F, 384, TextureFormat.RGB24, false);
                            for (int f = 0; f < F; f++)
                            {
                                clip.SampleAnimation(anim.gameObject, clip.length * f / F);
                                go.transform.position = offset;   // la raíz no se mueve
                                camGo.transform.position = center + new Vector3(0.75f, 0.12f, 1f).normalized * fh * 2.3f;
                                camGo.transform.LookAt(center);
                                cam.Render();
                                RenderTexture.active = rt;
                                strip.ReadPixels(new Rect(0, 0, 256, 384), 256 * f, 0);
                                RenderTexture.active = null;
                            }
                            strip.Apply();
                            string safe = clip.name.Replace("|", "_").Replace("target_character_", "");
                            System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, System.IO.Path.GetFileName(dir) + "__" + safe + ".png"), strip.EncodeToPNG());
                            Object.DestroyImmediate(strip);
                            count++;
                        }
                    }
                    finally { Object.DestroyImmediate(go); }
                }
            }
            finally
            {
                RenderSettings.ambientLight = oldAmbient; RenderSettings.fog = oldFog;
                cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(ground); Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo);
            }
            Debug.Log("[Anim] " + count + " tiras renderizadas en " + outDir);
        }
    }
}
