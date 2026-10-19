using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SomethingIsWrong.EditorTools
{
    /// <summary>
    /// Asistente de integración de CADA MONSTRUO (uno por piso) a partir del modelo real de Meshy (con rig de Meshy o de Mixamo).
    /// No inventa nada: si falta el FBX o los clips, lo indica y se detiene.
    ///
    /// Pasos: carpetas → configurar FBX (Humanoid, extraer texturas/materiales) → validar (Avatar, escala, pies, triángulos)
    /// → configurar clips (Avatar copiado, bucles, raíz bloqueada) → Animator Controller (parámetros de MonsterBase)
    /// → prefab "<Id>_Visual" (escala y pies en Y = 0) → asignar al MonsterBase de la escena.
    ///
    /// Navegación: MonsterBase mueve al monstruo con CharacterController por el grafo de puertas del nivel.
    /// NO se añade NavMeshAgent (competiría por el movimiento) y el Animator va SIN root motion.
    /// </summary>
    public class MonsterSetupWizard : EditorWindow
    {
        // Una carpeta por monstruo (canon): Piso 1 El Ciego · Piso 2 El Torcido · Piso 3 La Presencia
        static readonly string[] MonsterIds = { "Piso1_ElCiego", "Piso2_ElTorcido", "Piso3_LaPresencia" };
        static readonly string[] MonsterLabels = { "Piso 1 · El Ciego", "Piso 2 · El Torcido", "Piso 3 · La Presencia" };
        // Carpeta de arte de cada monstruo: Art/Monsters/<Carpeta>/Meshy_Export (FBX + PNG de Meshy, sin modificar).
        // Lo generado (materiales, Animator, prefab visual) va junto, en Materials/, Animator/ y Prefabs/.
        static readonly string[] MonsterFolders = { "ElCiego", "ElTorcido", "LaPresencia" };
        int monsterIndex;
        string Id { get { return MonsterIds[Mathf.Clamp(monsterIndex, 0, 2)]; } }
        string MonsterRoot { get { return SiwProjectSetup.Root + "/Art/Monsters/" + MonsterFolders[Mathf.Clamp(monsterIndex, 0, 2)]; } }
        string ExportDir { get { return MonsterRoot + "/Meshy_Export"; } }
        string ModelDir { get { return ExportDir; } }
        string TexDir { get { return MonsterRoot + "/Textures"; } }
        string MatDir { get { return MonsterRoot + "/Materials"; } }
        string AnimDir { get { return MonsterRoot + "/Animations"; } }
        string AnimatorDir { get { return MonsterRoot + "/Animator"; } }
        string PrefabDir { get { return MonsterRoot + "/Prefabs"; } }
        string ControllerPath { get { return AnimatorDir + "/" + Id + "_Animator.controller"; } }
        string PrefabPath { get { return PrefabDir + "/" + Id + "_Visual.prefab"; } }

        GameObject model;
        bool humanoid = true;
        bool extractEmbedded = true;   // la integración por menú usa los PNG de Meshy y no extrae nada          // tipo de rig: Humanoid o Generic, según el modelo y sus clips
        float idleFromWalkSpeed = 0.15f;   // si no hay clip Idle: Walk muy lento como reposo PROVISIONAL
        AnimationClip idle, walk, run, search, attack, awaken;
        float targetHeight = 2.15f;
        Vector3 modelRotation = Vector3.zero;
        float walkGameSpeed = 1.4f, runGameSpeed = 3.6f;      // = ElCiegoController.patrolSpeed / chaseSpeed
        float walkClipSpeed = 1.5f, runClipSpeed = 3.5f;      // velocidad a la que los pies del clip no patinan (m/s, ya escalado)
        string log = "";
        Vector2 scroll;

        [MenuItem("ANOMALY DESCENT/Monstruos/Asistente de integración (Meshy)", priority = 60)]
        static void Open()
        {
            var w = GetWindow<MonsterSetupWizard>("Monstruo · Meshy");
            var m = Object.FindAnyObjectByType<MonsterBase>();
            if (m != null) { w.targetHeight = m.visualHeight; w.monsterIndex = Mathf.Clamp(m.floor - 1, 0, 2); }
            w.minSize = new Vector2(420, 560);
        }

        public void CreateFolders()
        {
            foreach (var d in new[] { ExportDir, MatDir, AnimatorDir, PrefabDir }) SiwProjectSetup.EnsureFolder(d);
            AssetDatabase.Refresh();
            Debug.Log("[Monstruo] Carpetas listas en " + MonsterRoot + ": Meshy_Export (FBX + PNG), Materials, Animator, Prefabs.");
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox(
                "Coloca el FBX de Meshy (con rig) en Monster/Model y los FBX de animación en Monster/Animations.\n" +
                "Arrastra aquí el modelo y los clips (despliega cada FBX con la flecha: el clip suele llamarse 'mixamo.com' o el nombre de la acción).\n" +
                "Ejecuta los pasos en orden. Cada paso escribe su resultado abajo.", MessageType.Info);

            monsterIndex = EditorGUILayout.Popup("Monstruo", monsterIndex, MonsterLabels);
            EditorGUILayout.LabelField("Carpeta", MonsterRoot, EditorStyles.miniLabel);
            if (GUILayout.Button("0. Crear carpetas")) CreateFolders();

            EditorGUILayout.Space();
            model = (GameObject)EditorGUILayout.ObjectField("Modelo FBX (con rig)", model, typeof(GameObject), false);
            humanoid = EditorGUILayout.Popup("Tipo de rig", humanoid ? 0 : 1, new[] { "Humanoid", "Generic" }) == 0;
            targetHeight = EditorGUILayout.FloatField("Altura final (m)", targetHeight);
            modelRotation = EditorGUILayout.Vector3Field("Rotación de corrección", modelRotation);
            using (new EditorGUI.DisabledScope(model == null))
            {
                if (GUILayout.Button("1. Configurar FBX (Humanoid + extraer texturas y materiales)")) ConfigureModel();
                if (GUILayout.Button("2. Validar modelo (Avatar, escala, pies, triángulos)")) Validate();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Clips", EditorStyles.boldLabel);
            idle = Clip("Idle (reposo) *", idle);
            walk = Clip("Walk (caminar, In Place)", walk);
            run = Clip("Run (correr, In Place)", run);
            search = Clip("Search (buscar/olfatear)", search);
            awaken = Clip("Awaken (despertar/alarido)", awaken);
            attack = Clip("Attack (atacar)", attack);
            using (new EditorGUI.DisabledScope(model == null || idle == null))
                if (GUILayout.Button("3. Configurar clips (Avatar del modelo, bucles, raíz fija)")) ConfigureClips();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Anti-deslizamiento", EditorStyles.boldLabel);
            walkGameSpeed = EditorGUILayout.FloatField("Velocidad de patrulla (juego)", walkGameSpeed);
            runGameSpeed = EditorGUILayout.FloatField("Velocidad de persecución (juego)", runGameSpeed);
            walkClipSpeed = EditorGUILayout.FloatField("Velocidad natural del clip Walk", walkClipSpeed);
            runClipSpeed = EditorGUILayout.FloatField("Velocidad natural del clip Run", runClipSpeed);
            EditorGUILayout.HelpBox("Velocidad natural = metros por segundo a los que el paso del clip coincide con el suelo (a la altura final). " +
                                    "Mídela en Play: si los pies patinan hacia delante, súbela; si 'reman' hacia atrás, bájala.", MessageType.None);
            using (new EditorGUI.DisabledScope(idle == null && walk == null))
                if (GUILayout.Button("4. Crear Animator Controller")) CreateController();

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(model == null))
                if (GUILayout.Button("5. Crear prefab visual y asignarlo al monstruo de la escena", GUILayout.Height(30))) CreatePrefab();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Resultado", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(log, GUILayout.MinHeight(140));
            EditorGUILayout.EndScrollView();
        }

        static AnimationClip Clip(string label, AnimationClip c)
        {
            return (AnimationClip)EditorGUILayout.ObjectField(label, c, typeof(AnimationClip), false);
        }

        void Log(string s) { log = s; Debug.Log("[Monstruo]\n" + s); Repaint(); }

        string ModelPath { get { return model != null ? AssetDatabase.GetAssetPath(model) : null; } }

        Avatar ModelAvatar()
        {
            if (model == null) return null;
            return AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
        }

        // ------------------------------------------------------------ 1
        void ConfigureModel()
        {
            var sb = new StringBuilder();
            var imp = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (imp == null) { Log("El objeto elegido no es un modelo importado (.fbx)."); return; }
            CreateFolders();
            imp.animationType = humanoid ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importBlendShapes = true;
            imp.importAnimation = true;
            imp.importCameras = false;
            imp.importLights = false;
            imp.SaveAndReimport();
            sb.AppendLine("Rig: " + (humanoid ? "Humanoid" : "Generic") + " (Avatar creado desde este modelo).");

            int extracted = 0;
            if (extractEmbedded)
            {
                SiwProjectSetup.EnsureFolder(TexDir);
                if (imp.ExtractTextures(TexDir)) sb.AppendLine("Texturas incrustadas extraídas a " + TexDir + ".");
                else sb.AppendLine("No había texturas incrustadas (o ya estaban fuera). Si Meshy te dio PNG aparte, ponlos en " + TexDir + ".");
                AssetDatabase.Refresh();

                foreach (var m in AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Material>())
                {
                    string dst = MatDir + "/" + m.name + ".mat";
                    if (AssetDatabase.LoadAssetAtPath<Material>(dst) != null) continue;
                    string err = AssetDatabase.ExtractAsset(m, dst);
                    if (string.IsNullOrEmpty(err)) extracted++;
                    else sb.AppendLine("No se pudo extraer el material " + m.name + ": " + err);
                }
            }
            else sb.AppendLine("Texturas: se usan los PNG de Meshy junto al FBX (no se extraen las incrustadas).");
            AssetDatabase.WriteImportSettingsIfDirty(ModelPath);
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
            sb.AppendLine("Materiales extraídos a " + MatDir + ": " + extracted + " (editables; revisa que tengan Albedo y Normal).");
            Log(sb.ToString() + "\n" + ValidationText());
        }

        // ------------------------------------------------------------ 2
        void Validate() { Log(ValidationText()); }

        string ValidationText()
        {
            var sb = new StringBuilder("VALIDACIÓN de " + model.name + "\n");
            var imp = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (imp != null) sb.AppendLine("· Tipo de rig: " + imp.animationType + (imp.animationType == ModelImporterAnimationType.Human ? "" : "  ← debe ser Humanoid (paso 1)"));
            var av = ModelAvatar();
            if (av == null) sb.AppendLine("· Avatar: NO EXISTE. El FBX no tiene esqueleto utilizable o el paso 1 no se ha hecho.");
            else
            {
                sb.AppendLine("· Avatar: " + (av.isValid ? "válido" : "NO válido") + ", humano: " + (av.isHuman ? "sí" : "NO"));
                if (!av.isHuman || !av.isValid)
                    sb.AppendLine("  → Abre el FBX > Rig > Configure... y revisa el mapeo (Hips, Spine, Head, brazos y piernas). " +
                                  "Si Meshy no exportó esqueleto, ríggealo (Meshy auto-rig o Mixamo) y vuelve a importarlo.");
            }

            // Instancia temporal (no se guarda) dentro de un contenedor: las medidas incluyen la rotación de corrección.
            var wrapper = new GameObject("_ElCiego_Medida") { hideFlags = HideFlags.HideAndDontSave };
            var tmp = (GameObject)Object.Instantiate(model, wrapper.transform);
            tmp.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                tmp.transform.localPosition = Vector3.zero;
                tmp.transform.localRotation = Quaternion.Euler(modelRotation);
                bool any = false;
                var b = new Bounds();
                int tris = 0, skinned = 0, mats = 0, matsNoTex = 0;
                foreach (var mf in tmp.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) { Encapsulate(ref b, ref any, wrapper.transform, mf.transform, mf.sharedMesh.bounds); tris += Tris(mf.sharedMesh); }
                foreach (var sk in tmp.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (sk.sharedMesh != null) { Encapsulate(ref b, ref any, wrapper.transform, sk.transform, sk.sharedMesh.bounds); tris += Tris(sk.sharedMesh); skinned++; }
                foreach (var r in tmp.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) { matsNoTex++; continue; }
                        mats++;
                        if (m.HasProperty("_MainTex") && m.mainTexture == null) matsNoTex++;
                    }
                var worldB = b;          // medido en el contenedor (= mundo, con la rotación de corrección)
                bool anyW = any;
                float h = b.size.y;
                sb.AppendLine("· Mallas con esqueleto: " + skinned + (skinned == 0 ? "  ← no hay SkinnedMeshRenderer: no se podrá animar" : ""));
                sb.AppendLine("· Triángulos: " + tris + (tris > 60000 ? "  ← alto para gama media; decima en Meshy (objetivo 10k–30k)" : ""));
                sb.AppendLine("· Altura importada: " + h.ToString("0.00") + " m" +
                              (h < 0.3f || h > 6f ? "  ← escala sospechosa (¿cm vs m?). Ajusta 'Scale Factor' en el FBX o deja que el prefab la corrija." : "") +
                              " → se escalará ×" + (h > 0.001f ? (targetHeight / h).ToString("0.00") : "?") + " hasta " + targetHeight + " m");
                if (anyW)
                {
                    sb.AppendLine("· Pivote → pies: los pies están a " + worldB.min.y.ToString("0.00") + " m del pivote" +
                                  (Mathf.Abs(worldB.min.y) > 0.05f ? "  (el prefab lo corrige poniendo los pies en Y = 0)" : " (correcto)"));
                    sb.AppendLine("· Centro en planta: (" + worldB.center.x.ToString("0.00") + ", " + worldB.center.z.ToString("0.00") + ")");
                    sb.AppendLine("· Profundidad Z: " + worldB.size.z.ToString("0.00") + " m, anchura X: " + worldB.size.x.ToString("0.00") + " m " +
                                  "(en pose T la anchura X debe ser mucho mayor que Z; si es al revés, el modelo está girado 90°)");
                }
                var anim = tmp.GetComponentInChildren<Animator>();
                if (anim != null && anim.avatar != null && anim.avatar.isHuman)
                {
                    var lf = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                    var head = anim.GetBoneTransform(HumanBodyBones.Head);
                    if (lf != null && head != null)
                        sb.AppendLine("· Mira hacia " + (Vector3.Dot(head.position - lf.position, Vector3.up) > 0 ? "arriba (bien)" : "ABAJO (modelo invertido)") +
                                      ". Comprueba en el prefab que la cara mira a +Z (flecha azul); si no, usa 'Rotación de corrección' (p. ej. 0,180,0).");
                }
                sb.AppendLine("· Materiales: " + mats + (matsNoTex > 0 ? "  (" + matsNoTex + " sin textura de color: asígnalas desde " + TexDir + ")" : ""));
            }
            finally { Object.DestroyImmediate(wrapper); }
            return sb.ToString();
        }

        static int Tris(Mesh m)
        {
            int t = 0;
            for (int i = 0; i < m.subMeshCount; i++) t += (int)m.GetIndexCount(i) / 3;
            return t;
        }

        // ------------------------------------------------------------ 3
        void ConfigureClips()
        {
            var av = ModelAvatar();
            if (av == null || (humanoid && !av.isHuman)) { Log("Primero el paso 1: el modelo necesita un Avatar " + (humanoid ? "Humanoid" : "") + " válido."); return; }
            var sb = new StringBuilder();
            var loops = new HashSet<string> { "idle", "walk", "run", "search" };
            // Se guardan ruta y nombre ANTES de reimportar (al reimportar, las referencias a los clips se invalidan)
            var info = new List<(string role, string path, string name)>();
            void Add(string role, AnimationClip c) { if (c != null) info.Add((role, AssetDatabase.GetAssetPath(c), c.name)); }
            Add("idle", idle); Add("walk", walk); Add("run", run); Add("search", search); Add("awaken", awaken); Add("attack", attack);

            foreach (var group in info.GroupBy(x => x.path))
            {
                string p = group.Key;
                var imp = AssetImporter.GetAtPath(p) as ModelImporter;
                if (imp == null) { sb.AppendLine(string.Join(", ", group.Select(x => x.role)) + ": no es un FBX; se usa tal cual."); continue; }
                if (p != ModelPath)
                {
                    imp.animationType = humanoid ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
                    imp.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                    imp.sourceAvatar = av;
                }
                var clips = imp.clipAnimations;
                if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;
                foreach (var ca in clips)
                {
                    var match = group.FirstOrDefault(x => x.name == ca.name);
                    if (match.role == null && clips.Length == 1) match = group.First();
                    if (match.role == null) continue;
                    bool loop = loops.Contains(match.role);
                    ca.loopTime = loop;
                    ca.loopPose = loop;
                    ca.lockRootRotation = true; ca.keepOriginalOrientation = true;
                    ca.lockRootHeightY = true; ca.keepOriginalPositionY = true;
                    ca.lockRootPositionXZ = true; ca.keepOriginalPositionXZ = true;
                    sb.AppendLine(match.role + " (" + ca.name + "): " + (loop ? "bucle" : "una vez") + ", raíz bloqueada" + (p != ModelPath ? ", Avatar copiado del modelo" : ""));
                }
                imp.clipAnimations = clips;
                imp.SaveAndReimport();
            }
            var paths = info.ToDictionary(x => x.role, x => x.path);
            var names = info.ToDictionary(x => x.role, x => x.name);
            idle = Reload(paths, names, "idle"); walk = Reload(paths, names, "walk"); run = Reload(paths, names, "run");
            search = Reload(paths, names, "search"); awaken = Reload(paths, names, "awaken"); attack = Reload(paths, names, "attack");
            Log(sb.ToString());
        }

        static AnimationClip Reload(Dictionary<string, string> paths, Dictionary<string, string> names, string role)
        {
            string p, n;
            if (!paths.TryGetValue(role, out p) || !names.TryGetValue(role, out n)) return null;
            var all = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
            return all.FirstOrDefault(c => c.name == n) ?? all.FirstOrDefault();
        }

        // ------------------------------------------------------------ 4
        void CreateController()
        {
            CreateFolders();
            if (AssetDatabase.LoadAssetAtPath<Object>(ControllerPath) != null) AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            // Exactamente los parámetros que consulta MonsterBase.CacheParams()
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Chasing", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Searching", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Awaken", AnimatorControllerParameterType.Trigger);

            var sm = ctrl.layers[0].stateMachine;
            BlendTree tree;
            var loco = ctrl.CreateBlendTreeInController("Locomocion", out tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            bool idleFallback = idle == null && walk != null;
            tree.AddChild(idleFallback ? walk : idle, 0f);
            if (walk != null) tree.AddChild(walk, walkGameSpeed);
            if (run != null) tree.AddChild(run, runGameSpeed);
            // Escala de tiempo: a la velocidad de juego, el paso del clip coincide con el desplazamiento real
            var ch = tree.children;
            for (int i = 0; i < ch.Length; i++)
            {
                if (i == 0 && idleFallback) { ch[i].timeScale = idleFromWalkSpeed; continue; }
                if (ch[i].motion == walk && walk != null && walkClipSpeed > 0.01f) ch[i].timeScale = walkGameSpeed / walkClipSpeed;
                if (ch[i].motion == run && run != null && runClipSpeed > 0.01f) ch[i].timeScale = runGameSpeed / runClipSpeed;
            }
            tree.children = ch;
            sm.defaultState = loco;

            if (search != null)
            {
                var s = sm.AddState("Buscar");
                s.motion = search;
                var t = loco.AddTransition(s); t.AddCondition(AnimatorConditionMode.If, 0f, "Searching"); t.hasExitTime = false; t.duration = 0.2f;
                var back = s.AddTransition(loco); back.AddCondition(AnimatorConditionMode.IfNot, 0f, "Searching"); back.hasExitTime = false; back.duration = 0.2f;
            }
            if (awaken != null)
            {
                var w = sm.AddState("Despertar");
                w.motion = awaken;
                var t = sm.AddAnyStateTransition(w); t.AddCondition(AnimatorConditionMode.If, 0f, "Awaken"); t.hasExitTime = false; t.duration = 0.1f; t.canTransitionToSelf = false;
                var back = w.AddTransition(loco); back.hasExitTime = true; back.exitTime = 0.95f; back.duration = 0.2f;
            }
            if (attack != null)
            {
                var a = sm.AddState("Atacar");
                a.motion = attack;
                var t = sm.AddAnyStateTransition(a); t.AddCondition(AnimatorConditionMode.If, 0f, "Attack"); t.hasExitTime = false; t.duration = 0.1f; t.canTransitionToSelf = false;
                // Tras el ataque la partida termina (GameDirector.OnPlayerCaught); se queda en el estado.
            }
            AssetDatabase.SaveAssets();
            Log("Animator creado: " + ControllerPath + "\nParámetros: Speed, Chasing, Searching, Attack, Awaken (los que usa MonsterBase).\n" +
                "Locomoción: Idle 0 m/s" + (walk != null ? ", Walk " + walkGameSpeed + " m/s (×" + (walkGameSpeed / walkClipSpeed).ToString("0.00") + ")" : "") +
                (run != null ? ", Run " + runGameSpeed + " m/s (×" + (runGameSpeed / runClipSpeed).ToString("0.00") + ")" : "") +
                "\nEstados extra: " + (search != null ? "Buscar " : "") + (awaken != null ? "Despertar " : "") + (attack != null ? "Atacar" : "") +
                (search == null || awaken == null || attack == null ? "\nFaltan clips opcionales: el monstruo usará la locomoción en esos momentos." : "") +
                (idleFallback ? "\nSIN clip Idle: reposo PROVISIONAL = Walk a ×" + idleFromWalkSpeed + ". Añade un Idle de Meshy cuando exista." : ""));
        }

        // ------------------------------------------------------------ 5
        void CreatePrefab()
        {
            CreateFolders();
            var av = ModelAvatar();
            var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            var root = new GameObject(Id + "_Visual");
            try
            {
                var inst = PrefabUtility.InstantiatePrefab(model, root.transform) as GameObject;
                if (inst == null) inst = Object.Instantiate(model, root.transform);
                inst.name = model.name;
                var t = inst.transform;
                t.localPosition = Vector3.zero;
                t.localRotation = Quaternion.Euler(modelRotation);
                t.localScale = Vector3.one;

                foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var rb in inst.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);

                Bounds b = Measure(root.transform, inst.transform);
                if (b.size.y > 0.001f) t.localScale = Vector3.one * (targetHeight / b.size.y);
                b = Measure(root.transform, inst.transform);
                t.localPosition += new Vector3(-b.center.x, -b.min.y, -b.center.z);   // pies en Y = 0, centrado en el pivote

                var anim = inst.GetComponent<Animator>();
                if (anim == null) anim = inst.AddComponent<Animator>();
                if (av != null) anim.avatar = av;
                anim.updateMode = AnimatorUpdateMode.Normal;
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;                          // el movimiento lo pone MonsterBase
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var sk in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true)) sk.updateWhenOffscreen = true;

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                var sb = new StringBuilder("Prefab guardado: " + PrefabPath + "\n");
                sb.AppendLine("Altura: " + targetHeight + " m · pies en Y = 0 · root motion desactivado · sin colisionadores (la cápsula de juego es el CharacterController del monstruo).");
                if (ctrl == null) sb.AppendLine("AVISO: aún no hay Animator Controller (paso 4). El prefab se verá en pose de reposo.");
                if (av == null || !av.isHuman) sb.AppendLine("AVISO: el Avatar no es Humanoid válido; las animaciones Humanoid no se reproducirán.");

                MonsterBase target = null;
                foreach (var mb in Object.FindObjectsByType<MonsterBase>(FindObjectsInactive.Include))
                    if (mb.floor == monsterIndex + 1) target = mb;
                if (target != null)
                {
                    Undo.RecordObject(target, "Asignar modelo del monstruo");
                    target.visualPrefab = prefab;
                    if (ctrl != null) target.animatorController = ctrl;
                    target.visualHeight = targetHeight;
                    target.visualRotation = Vector3.zero;   // la corrección ya está dentro del prefab
                    target.monsterName = MonsterLabels[monsterIndex].Split('·')[1].Trim();
                    EditorUtility.SetDirty(target);
                    EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
                    sb.AppendLine("Asignado a '" + target.name + "' (" + target.gameObject.scene.name + "). GUARDA LA ESCENA (Ctrl+S).");
                }
                else sb.AppendLine("No hay un monstruo del Piso " + (monsterIndex + 1) + " en la escena abierta: abre su escena y asigna el prefab y el Animator en su Inspector.");
                sb.AppendLine("\nPrueba: Play → provoca errores hasta despertarlo (o baja 'Error Threshold' a 1) y observa: pies sobre el suelo, giro de cabeza, caminar sin patinar, puertas.");
                Log(sb.ToString());
                Selection.activeObject = prefab;
            }
            finally { Object.DestroyImmediate(root); }
        }


        // ================================================================== integración por menú (sin ventana)
        // Mismos pasos que la ventana, con la configuración de cada modelo de Meshy. Busca el FBX en
        // Art/Monsters/<Monstruo>/Meshy_Export. Si no hay FBX, no hace nada: el monstruo conserva la figura provisional.
        struct Plan
        {
            public bool humanoid; public float height;
            public string idle, walk, run, search, awaken, attack;   // fragmentos del nombre del clip
            public string why;
        }

        static Plan PlanFor(int i)
        {
            switch (i)
            {
                case 0: return new Plan { humanoid = true, height = 2.15f, idle = "Listening", walk = "Walking", run = "Running", search = "Listening",
                    why = "Humanoid: esqueleto mixamorig completo (piernas, brazos, cabeza) y clips de pie; permite reutilizar clips humanoides futuros (Idle/Attack)." };
                case 1: return new Plan { humanoid = false, height = 1.85f, idle = "Crawl", walk = "Walking", run = "Running", search = "Crawl", awaken = "Crawl",
                    why = "Generic: el clip 'Crawl_and_Look_Back' apoya manos en el suelo y el esqueleto no tiene LeftToeBase; el retargeting humanoide deformaría la pose. Los clips salen del mismo rig, así que Generic los reproduce tal cual." };
                default: return new Plan { humanoid = true, height = 2.2f, walk = "Walking", run = "Running",
                    why = "Humanoid: esqueleto mixamorig completo; solo trae Walking/Running. Humanoid permite añadir después un Idle/Attack humanoide sin re-riggear." };
            }
        }

        [MenuItem("ANOMALY DESCENT/Monstruos/Integrar modelo Meshy · Piso 1 El Ciego", priority = 61)]
        static void Batch1() { RunBatch(0); }
        [MenuItem("ANOMALY DESCENT/Monstruos/Integrar modelo Meshy · Piso 2 El Torcido", priority = 62)]
        static void Batch2() { RunBatch(1); }
        [MenuItem("ANOMALY DESCENT/Monstruos/Integrar modelo Meshy · Piso 3 La Presencia", priority = 63)]
        static void Batch3() { RunBatch(2); }
        [MenuItem("ANOMALY DESCENT/Monstruos/Asignar prefabs visuales existentes a la escena abierta", priority = 64)]
        static void AssignExisting()
        {
            int n = 0;
            for (int i = 0; i < 3; i++)
            {
                var w = CreateInstance<MonsterSetupWizard>();
                w.monsterIndex = i;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(w.PrefabPath);
                var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(w.ControllerPath);
                foreach (var mb in Object.FindObjectsByType<MonsterBase>(FindObjectsInactive.Include))
                    if (mb.floor == i + 1 && prefab != null)
                    {
                        Undo.RecordObject(mb, "Asignar visual");
                        mb.visualPrefab = prefab; mb.animatorController = ctrl; mb.visualHeight = PlanFor(i).height; mb.visualRotation = Vector3.zero;
                        EditorUtility.SetDirty(mb); EditorSceneManager.MarkSceneDirty(mb.gameObject.scene); n++;
                    }
                DestroyImmediate(w);
            }
            Debug.Log("[Monstruo] Prefabs visuales asignados en la escena abierta: " + n + ". Guarda la escena.");
        }

        static void RunBatch(int i)
        {
            var w = CreateInstance<MonsterSetupWizard>();
            try
            {
                w.monsterIndex = i;
                var plan = PlanFor(i);
                w.humanoid = plan.humanoid; w.targetHeight = plan.height; w.extractEmbedded = false;
                w.CreateFolders();
                string fbxPath = AssetDatabase.FindAssets("t:Model", new[] { w.ExportDir }).Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(pth => pth.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase));
                if (fbxPath == null)
                {
                    Debug.LogWarning("[Monstruo] " + MonsterLabels[i] + ": no hay FBX en " + w.ExportDir + ". Se mantiene la figura PROVISIONAL.");
                    return;
                }
                w.model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                var sb = new StringBuilder("== " + MonsterLabels[i] + " ==\nFBX: " + fbxPath + "\nRig elegido: " + (plan.humanoid ? "Humanoid" : "Generic") + ". " + plan.why + "\n");

                // texturas de Meshy (PNG junto al FBX) → material PBR remapeado sobre los materiales del FBX
                w.ConfigureModel();
                string dir = System.IO.Path.GetDirectoryName(fbxPath).Replace('\\', '/');
                Texture2D Tex(string suffix) => AssetDatabase.FindAssets("t:Texture2D", new[] { dir }).Select(AssetDatabase.GUIDToAssetPath)
                    .Where(pth => System.IO.Path.GetFileNameWithoutExtension(pth).EndsWith(suffix)).Select(AssetDatabase.LoadAssetAtPath<Texture2D>).FirstOrDefault();
                var albedo = Tex("texture_0"); var normal = Tex("texture_0_normal"); var rough = Tex("texture_0_roughness"); var metal = Tex("texture_0_metallic");
                foreach (var t in new[] { albedo, normal, rough, metal })
                {
                    if (t == null) continue;
                    var ti = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter;
                    if (ti != null && ti.maxTextureSize > 2048) { ti.maxTextureSize = 2048; ti.SaveAndReimport(); }
                }
                string matPath = w.MatDir + "/M_" + MonsterFolders[i] + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, matPath); }
                if (albedo != null)
                {
                    PbrMaterialBuilder.PackedOutputDir = w.MatDir;   // ruta corta (la carpeta de Meshy está muy anidada)
                    try { PbrMaterialBuilder.BuildMaterial(mat, albedo, normal, rough, metal, null, null, Vector2.one, 1f); }
                    finally { PbrMaterialBuilder.PackedOutputDir = null; }
                    var imp = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
                    foreach (var m in AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Material>())
                        imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), mat);
                    imp.SaveAndReimport();
                    sb.AppendLine("Material: " + matPath + " (albedo" + (normal != null ? " + normal" : ", SIN normal") + (rough != null ? " + rugosidad" : "") + (metal != null ? " + metal" : "") + ")");
                }
                else sb.AppendLine("AVISO: no se encontró *_texture_0.png junto al FBX.");
                w.model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);

                // Orientación: el rig de Meshy trae un hueso "headfront" delante de la cara. Si queda en −Z respecto a
                // "Head", el modelo mira hacia atrás en Unity y se gira 180° dentro del prefab visual.
                {
                    var tmp = (GameObject)Object.Instantiate(w.model);
                    tmp.hideFlags = HideFlags.HideAndDontSave;
                    try
                    {
                        Transform head = null, front = null;
                        foreach (var tr in tmp.GetComponentsInChildren<Transform>(true))
                        {
                            string nm = tr.name.ToLowerInvariant();
                            if (nm.EndsWith("headfront")) front = tr;
                            else if (nm.EndsWith(":head") || nm == "head") head = tr;
                        }
                        if (head != null && front != null)
                        {
                            float dz = front.position.z - head.position.z;
                            w.modelRotation = dz < 0f ? new Vector3(0f, 180f, 0f) : Vector3.zero;
                            sb.AppendLine("Orientación: headfront " + (dz < 0f ? "detrás (−Z) → giro 180° en el prefab" : "delante (+Z) → sin giro") + " (Δz " + dz.ToString("0.00") + ")");
                        }
                        else sb.AppendLine("Orientación: no hay hueso headfront; se deja sin giro (comprobar en Play).");
                    }
                    finally { Object.DestroyImmediate(tmp); }
                }

                // clips del FBX combinado de Meshy
                var clips = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
                sb.AppendLine("Clips en el FBX (" + clips.Count + "): " + string.Join(", ", clips.Select(c => c.name)));
                AnimationClip Find(string frag) => string.IsNullOrEmpty(frag) ? null : clips.FirstOrDefault(c => c.name.IndexOf(frag, System.StringComparison.OrdinalIgnoreCase) >= 0);
                w.idle = Find(plan.idle); w.walk = Find(plan.walk); w.run = Find(plan.run);
                w.search = Find(plan.search); w.awaken = Find(plan.awaken); w.attack = Find(plan.attack);
                var av = w.ModelAvatar();
                sb.AppendLine("Avatar: " + (av == null ? "NO" : (av.isValid ? "válido" : "NO válido") + (av.isHuman ? ", humano" : ", genérico")));
                if (w.idle != null || w.walk != null)
                {
                    if (w.idle != null) w.ConfigureClips();
                    else
                    {   // ConfigureClips usa Idle como referencia: sin Idle, se configura con Walk
                        w.idle = w.walk; w.ConfigureClips(); w.idle = null;
                    }
                    sb.AppendLine("Clips → Idle: " + N(w.idle) + " · Walk: " + N(w.walk) + " · Run: " + N(w.run) + " · Search: " + N(w.search) + " · Awaken: " + N(w.awaken) + " · Attack: " + N(w.attack));
                    w.CreateController();
                }
                else sb.AppendLine("AVISO: el FBX no tiene clips reconocibles; el prefab quedará en pose de reposo.");
                w.CreatePrefab();
                sb.AppendLine("Prefab visual: " + w.PrefabPath);
                sb.AppendLine("Faltan (esperados para la versión final): " +
                    string.Join(", ", new[] { ("Idle", w.idle), ("Search", w.search), ("Awaken", w.awaken), ("Attack", w.attack) }.Where(x => x.Item2 == null).Select(x => x.Item1)));
                Debug.Log("[Monstruo]\n" + sb + "\n" + w.log);
            }
            finally { DestroyImmediate(w); }
        }

        static string N(AnimationClip c) { return c != null ? c.name : "—"; }

        static Bounds Measure(Transform root, Transform model)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) Encapsulate(ref b, ref any, root, mf.transform, mf.sharedMesh.bounds);
            foreach (var sk in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (sk.sharedMesh != null) Encapsulate(ref b, ref any, root, sk.transform, sk.sharedMesh.bounds);
            return b;
        }

        /// <summary>Amplía "b" con las 8 esquinas de "mb" (espacio local de t) expresadas en el espacio de "root".</summary>
        static void Encapsulate(ref Bounds b, ref bool any, Transform root, Transform t, Bounds mb)
        {
            Vector3 mn = mb.min, mx = mb.max;
            for (int i = 0; i < 8; i++)
            {
                var p = new Vector3((i & 1) == 0 ? mn.x : mx.x, (i & 2) == 0 ? mn.y : mx.y, (i & 4) == 0 ? mn.z : mx.z);
                var lp = root.InverseTransformPoint(t.TransformPoint(p));
                if (!any) { b = new Bounds(lp, Vector3.zero); any = true; }
                else b.Encapsulate(lp);
            }
        }
    }
}
