using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SomethingIsWrong.EditorTools
{
    /// <summary>
    /// Informe de animación de los tres monstruos (no modifica nada):
    /// importación de cada FBX (rig, Avatar, clips, rango de frames, bucle), contenido real de cada clip
    /// (curvas, duración) y una prueba de muestreo: se instancia el prefab visual fuera de escena y se mide
    /// cuánto cambia la pose del esqueleto a lo largo del clip. Escribe Capturas/diag_animacion_editor.txt.
    /// </summary>
    public static class AnimationDiagnostics
    {
        const string Root = "Assets/AnomalyDescent/Art/Monsters";

        [MenuItem("ANOMALY DESCENT/Monstruos/Diagnóstico de animación (informe)", priority = 120)]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("DIAGNÓSTICO DE ANIMACIÓN — " + System.DateTime.Now);
            foreach (var dir in AssetDatabase.GetSubFolders(Root))
            {
                string name = System.IO.Path.GetFileName(dir);
                sb.AppendLine("\n==================== " + name + " ====================");
                var ctrl = AssetDatabase.FindAssets("t:AnimatorController", new[] { dir + "/Animator" })
                    .Select(g => AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
                var prefab = AssetDatabase.FindAssets("t:Prefab", new[] { dir + "/Prefabs" })
                    .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
                foreach (var g in AssetDatabase.FindAssets("t:Model", new[] { dir + "/Meshy_Export" }))
                    DescribeImporter(AssetDatabase.GUIDToAssetPath(g), sb);
                if (ctrl == null) { sb.AppendLine("SIN Animator Controller."); continue; }
                DescribeController(ctrl, sb);
                if (prefab != null) SampleTest(prefab, ctrl, sb);
            }
            string outDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Capturas"));
            System.IO.Directory.CreateDirectory(outDir);
            string file = System.IO.Path.Combine(outDir, "diag_animacion_editor.txt");
            System.IO.File.WriteAllText(file, sb.ToString());
            Debug.Log("[Diag] Informe de animación escrito en " + file);
        }

        [MenuItem("ANOMALY DESCENT/Monstruos/Copiar FBX de monstruos a Capturas (análisis externo)", priority = 121)]
        public static void CopyFbx()
        {
            string outDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Capturas/fbx"));
            System.IO.Directory.CreateDirectory(outDir);
            foreach (var dir in AssetDatabase.GetSubFolders(Root))
                foreach (var g in AssetDatabase.FindAssets("t:Model", new[] { dir + "/Meshy_Export" }))
                {
                    string src = System.IO.Path.GetFullPath(AssetDatabase.GUIDToAssetPath(g));
                    System.IO.File.Copy(src, System.IO.Path.Combine(outDir, System.IO.Path.GetFileName(dir) + ".fbx"), true);
                }
            Debug.Log("[Diag] FBX copiados (solo lectura, copia) en " + outDir);
        }

        static void DescribeImporter(string path, StringBuilder sb)
        {
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) return;
            sb.AppendLine("FBX: " + System.IO.Path.GetFileName(path));
            sb.AppendLine("  rig=" + imp.animationType + " avatarSetup=" + imp.avatarSetup +
                          (imp.sourceAvatar != null ? " sourceAvatar=" + imp.sourceAvatar.name : "") +
                          " importAnimation=" + imp.importAnimation + " resampleCurves=" + imp.resampleCurves +
                          " animCompression=" + imp.animationCompression);
            var av = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            sb.AppendLine("  Avatar: " + (av == null ? "NINGUNO" : av.name + " válido=" + av.isValid + " humano=" + av.isHuman));
            foreach (var ti in imp.importedTakeInfos)
                sb.AppendLine(string.Format("  take FBX «{0}»: start {1:0.000} s stop {2:0.000} s (bake {3:0.000}-{4:0.000}) fps {5}",
                    ti.name, ti.startTime, ti.stopTime, ti.bakeStartTime, ti.bakeStopTime, ti.sampleRate));
            var clips = imp.clipAnimations;
            bool custom = clips != null && clips.Length > 0;
            if (!custom) clips = imp.defaultClipAnimations;
            sb.AppendLine("  clips (" + (custom ? "configurados" : "por defecto") + "):");
            foreach (var c in clips)
                sb.AppendLine(string.Format("    · {0}  frames {1:0}-{2:0}  loopTime={3} loopPose={4} lockRoot(rot/Y/XZ)={5}/{6}/{7}",
                    c.name, c.firstFrame, c.lastFrame, c.loopTime, c.loopPose, c.lockRootRotation, c.lockRootHeightY, c.lockRootPositionXZ));
            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(x => !x.name.StartsWith("__preview__")))
            {
                var st = AnimationUtility.GetAnimationClipSettings(clip);
                var binds = AnimationUtility.GetCurveBindings(clip);
                int curves = binds.Length, moving = 0, keys = 0;
                float kmin = float.MaxValue, kmax = float.MinValue;
                foreach (var bd in binds)
                {
                    var cv = AnimationUtility.GetEditorCurve(clip, bd);
                    if (cv == null || cv.length == 0) continue;
                    keys += cv.length;
                    kmin = Mathf.Min(kmin, cv.keys[0].time); kmax = Mathf.Max(kmax, cv.keys[cv.length - 1].time);
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (var k in cv.keys) { lo = Mathf.Min(lo, k.value); hi = Mathf.Max(hi, k.value); }
                    if (hi - lo > 1e-3f) moving++;
                }
                sb.AppendLine(string.Format("      curvas con movimiento {0}/{1}, claves {2}, tiempo de claves {3:0.00}-{4:0.00} s", moving, curves, keys, kmin, kmax));
                sb.AppendLine(string.Format("    clip importado «{0}»: {1:0.00} s, {2} fps, curvas={3}, humano={4}, isLooping={5}, loopTime={6}, start {7:0.00} stop {8:0.00}",
                    clip.name, clip.length, clip.frameRate, curves, clip.humanMotion, clip.isLooping, st.loopTime, st.startTime, st.stopTime));
            }
        }

        static IEnumerable<AnimationClip> ClipsOf(Motion m)
        {
            if (m is AnimationClip c) yield return c;
            else if (m is BlendTree bt) foreach (var ch in bt.children) foreach (var x in ClipsOf(ch.motion)) yield return x;
        }

        static void DescribeController(AnimatorController ctrl, StringBuilder sb)
        {
            sb.AppendLine("Animator: " + ctrl.name + "  parámetros: " + string.Join(", ", ctrl.parameters.Select(p => p.name + ":" + p.type)));
            var sm = ctrl.layers[0].stateMachine;
            sb.AppendLine("  estado por defecto: " + (sm.defaultState != null ? sm.defaultState.name : "NINGUNO"));
            foreach (var cs in sm.states)
            {
                var s = cs.state;
                sb.AppendLine("  Estado «" + s.name + "» speed=" + s.speed + " motion=" + (s.motion != null ? s.motion.name : "NINGUNO") +
                              " writeDefaults=" + s.writeDefaultValues);
                if (s.motion is BlendTree bt)
                {
                    sb.AppendLine("    BlendTree " + bt.blendType + " param=" + bt.blendParameter + " auto=" + bt.useAutomaticThresholds);
                    foreach (var ch in bt.children)
                        sb.AppendLine(string.Format("      umbral {0:0.00}  clip {1}  timeScale {2:0.00}", ch.threshold, ch.motion != null ? ch.motion.name : "-", ch.timeScale));
                }
                foreach (var t in s.transitions)
                    sb.AppendLine("    → " + (t.destinationState != null ? t.destinationState.name : "?") + " [" +
                                  string.Join(" & ", t.conditions.Select(c => c.parameter + " " + c.mode)) + "] exitTime=" + (t.hasExitTime ? t.exitTime.ToString("0.00") : "no"));
            }
            foreach (var t in sm.anyStateTransitions)
                sb.AppendLine("  AnyState → " + (t.destinationState != null ? t.destinationState.name : "?") + " [" +
                              string.Join(" & ", t.conditions.Select(c => c.parameter + " " + c.mode)) + "]");
            foreach (var p in new[] { "Attack", "Awaken" })
            {
                bool used = sm.anyStateTransitions.Any(t => t.conditions.Any(c => c.parameter == p)) ||
                            sm.states.Any(cs => cs.state.transitions.Any(t => t.conditions.Any(c => c.parameter == p)));
                sb.AppendLine("  Trigger " + p + ": " + (used ? "tiene transición" : "SIN EFECTO (ninguna transición lo usa)"));
            }
        }

        /// <summary>Instancia el prefab fuera de escena y muestrea cada clip: ¿cambia la pose? ¿cierra el ciclo?</summary>
        static void SampleTest(GameObject prefab, AnimatorController ctrl, StringBuilder sb)
        {
            var go = Object.Instantiate(prefab);
            go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var anim = go.GetComponentInChildren<Animator>();
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                sb.AppendLine("Prefab " + prefab.name + ": Animator=" + (anim != null) +
                              (anim != null ? " avatar=" + (anim.avatar != null ? anim.avatar.name + (anim.avatar.isHuman ? "(humano)" : "(genérico)") : "NINGUNO") +
                                              " controller=" + (anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "NINGUNO") +
                                              " rootMotion=" + anim.applyRootMotion + " culling=" + anim.cullingMode + " speed=" + anim.speed : "") +
                              " huesos=" + (smr != null ? smr.bones.Length : 0));
                if (anim == null || smr == null) return;
                var bones = smr.bones.Where(b => b != null).ToArray();
                var seen = new HashSet<AnimationClip>();
                foreach (var cs in ctrl.layers[0].stateMachine.states)
                    foreach (var clip in ClipsOf(cs.state.motion))
                    {
                        if (clip == null || !seen.Add(clip)) continue;
                        const int N = 12;
                        var poses = new Quaternion[N + 1][];
                        for (int k = 0; k <= N; k++)
                        {
                            clip.SampleAnimation(anim.gameObject, clip.length * k / N);
                            poses[k] = bones.Select(b => b.localRotation).ToArray();
                        }
                        float travel = 0f, maxFromStart = 0f;
                        for (int k = 1; k <= N; k++)
                        {
                            travel += Diff(poses[k - 1], poses[k]);
                            maxFromStart = Mathf.Max(maxFromStart, Diff(poses[0], poses[k]));
                        }
                        float seam = Diff(poses[0], poses[N]);
                        sb.AppendLine(string.Format("  Muestreo «{0}» ({1:0.00} s): recorrido {2:0.0}°/hueso, máx. desde inicio {3:0.0}°, fin→inicio {4:0.0}° → {5}",
                            clip.name, clip.length, travel, maxFromStart, seam,
                            travel < 1f ? "POSE FIJA (el clip no anima el esqueleto)" : (seam < Mathf.Max(2f, travel * 0.08f) ? "anima y cierra el ciclo" : "anima; el ciclo no cierra")));
                    }
            }
            finally { Object.DestroyImmediate(go); }
        }

        static float Diff(Quaternion[] a, Quaternion[] b)
        {
            float s = 0f;
            for (int i = 0; i < a.Length; i++) s += Quaternion.Angle(a[i], b[i]);
            return a.Length > 0 ? s / a.Length : 0f;
        }
    }
}
