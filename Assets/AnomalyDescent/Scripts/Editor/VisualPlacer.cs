using UnityEditor;
using UnityEngine;

namespace SomethingIsWrong.EditorTools
{
    /// <summary>
    /// Ayuda de Editor para separar MODELO VISUAL y LÓGICA (equivale a arrastrar un modelo desde Project y colocarlo a mano):
    ///  1) "Preparar raíz lógica": el objeto con Reportable/Anomaly/HideSpot/collider conserva todos sus componentes;
    ///     su escala se pasa al BoxCollider (escala 1) y su MeshRenderer provisional se desactiva (no se borra nada).
    ///  2) "Añadir modelo como hijo Visual": instancia el modelo importado (prefab del FBX) como hijo, opcionalmente girado,
    ///     escalado para encajar en el collider y apoyado en su base. Después se puede retocar a mano en el Inspector.
    /// No crea geometría: solo instancia assets importados.
    /// </summary>
    public class VisualPlacer : EditorWindow
    {
        Transform target;
        GameObject model;
        float yaw;
        bool fitToCollider = false;
        bool keepProportions = true;
        Vector3 extraOffset;
        float extraScale = 1f;

        [MenuItem("ANOMALY DESCENT/Arte/5. Colocar modelo visual en objeto lógico", priority = 44)]
        static void Open() { GetWindow<VisualPlacer>("Colocar visual"); }

        void OnSelectionChange() { if (Selection.activeTransform != null) target = Selection.activeTransform; Repaint(); }

        void OnGUI()
        {
            EditorGUILayout.HelpBox("Selecciona en la jerarquía el objeto lógico (Silla, Cama...). El modelo se añade como hijo 'Visual_*'; la raíz conserva Reportable, Anomaly, colliders y referencias.", MessageType.Info);
            target = (Transform)EditorGUILayout.ObjectField("Objeto lógico", target, typeof(Transform), true);
            model = (GameObject)EditorGUILayout.ObjectField("Modelo (FBX/prefab)", model, typeof(GameObject), false);
            yaw = EditorGUILayout.FloatField("Giro Y del modelo (°)", yaw);
            fitToCollider = EditorGUILayout.Toggle("Escalar para encajar en el collider", fitToCollider);
            keepProportions = EditorGUILayout.Toggle("  Mantener proporciones", keepProportions);
            extraScale = EditorGUILayout.FloatField("Escala extra", extraScale);
            extraOffset = EditorGUILayout.Vector3Field("Desplazamiento extra", extraOffset);

            using (new EditorGUI.DisabledScope(target == null))
                if (GUILayout.Button("1. Preparar raíz lógica (escala→collider, ocultar bloque)", GUILayout.Height(26))) PrepareRoot(target);
            using (new EditorGUI.DisabledScope(target == null || model == null))
                if (GUILayout.Button("2. Añadir modelo como hijo Visual", GUILayout.Height(30))) AddVisual();
        }

        // ---- Variante por menú (sin ventana): selecciona el objeto lógico en la jerarquía y, con Ctrl, el modelo en Project.
        [MenuItem("ANOMALY DESCENT/Visual/Añadir modelo seleccionado como Visual (0°)", priority = 1)]
        static void Add0() { AddFromSelection(0f); }
        [MenuItem("ANOMALY DESCENT/Visual/Añadir modelo seleccionado como Visual (90°)", priority = 2)]
        static void Add90() { AddFromSelection(90f); }
        [MenuItem("ANOMALY DESCENT/Visual/Añadir modelo seleccionado como Visual (180°)", priority = 3)]
        static void Add180() { AddFromSelection(180f); }
        [MenuItem("ANOMALY DESCENT/Visual/Añadir modelo seleccionado como Visual (270°)", priority = 4)]
        static void Add270() { AddFromSelection(270f); }
        [MenuItem("ANOMALY DESCENT/Visual/Añadir ajustando al collider (proporcional)", priority = 10)]
        static void AddFitProp() { AddFromSelection(0f, true, true); }
        [MenuItem("ANOMALY DESCENT/Visual/Añadir ajustando al collider (estirar)", priority = 11)]
        static void AddFitStretch() { AddFromSelection(0f, true, false); }
        [MenuItem("ANOMALY DESCENT/Visual/Cerrar ventana 'Colocar visual'", priority = 30)]
        static void CloseAll() { foreach (var w in Resources.FindObjectsOfTypeAll<VisualPlacer>()) w.Close(); }
        [MenuItem("ANOMALY DESCENT/Visual/Preparar raíz lógica seleccionada", priority = 20)]
        static void PrepSel() { foreach (var t in Selection.transforms) PrepareRoot(t); }

        // Equivale a arrastrar el modelo desde Project sobre un grupo de la jerarquía: crea una raíz vacía "Prop_<modelo>"
        // dentro del grupo seleccionado (p. ej. Props) con el modelo como hijo Visual. Luego se coloca a mano.
        [MenuItem("ANOMALY DESCENT/Visual/Nuevo prop en el grupo seleccionado", priority = 5)]
        static void NewProp()
        {
            Transform group = null; GameObject m = null;
            foreach (var o in Selection.objects)
            {
                var go = o as GameObject;
                if (go == null) continue;
                if (EditorUtility.IsPersistent(go)) { if (m == null) m = go; }
                else if (group == null) group = go.transform;
            }
            if (group == null || m == null) { EditorUtility.DisplayDialog("Visual", "Selecciona el grupo (Props) en la jerarquía y, con Ctrl, el modelo en Project.", "OK"); return; }
            var root = new GameObject("Prop_" + m.name.Replace("_1k", ""));
            Undo.RegisterCreatedObjectUndo(root, "Nuevo prop");
            root.transform.SetParent(group, false);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(m, root.transform);
            Undo.RegisterCreatedObjectUndo(inst, "Nuevo prop");
            inst.name = "Visual_" + m.name.Replace("_1k", "");
            inst.transform.localPosition = Vector3.zero;
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Undo.DestroyObjectImmediate(c);
            // apoyar la base del modelo en el pivote de la raíz
            var b = LocalBounds(root.transform, inst);
            if (b.size.sqrMagnitude > 1e-6f) inst.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
            Selection.activeGameObject = root;
        }

        [MenuItem("ANOMALY DESCENT/Visual/Ajustar BoxCollider a la visual (raíz seleccionada)", priority = 21)]
        static void FitColliderSel()
        {
            foreach (var sel in Selection.transforms)
            {
                var t = sel.name.StartsWith("Visual_") && sel.parent != null ? sel.parent : sel;
                var b = LocalBoundsAll(t);
                if (b.size.sqrMagnitude < 1e-6f) continue;
                var box = t.GetComponent<BoxCollider>();
                if (box == null) box = Undo.AddComponent<BoxCollider>(t.gameObject);
                Undo.RecordObject(box, "Collider ajustado");
                box.center = b.center; box.size = b.size;
                Selection.activeTransform = t;
            }
        }

        static Bounds LocalBoundsAll(Transform root)
        {
            bool has = false; Bounds b = new Bounds();
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r.transform == root) continue;
                var bb = LocalBounds(root, r.gameObject);
                if (bb.size.sqrMagnitude < 1e-8f) continue;
                if (!has) { b = bb; has = true; } else b.Encapsulate(bb);
            }
            return b;
        }

        static void AddFromSelection(float yawDeg, bool fit = false, bool prop = true)
        {
            Transform t = null; GameObject m = null;
            foreach (var o in Selection.objects)
            {
                var go = o as GameObject;
                if (go == null) continue;
                if (EditorUtility.IsPersistent(go)) { if (m == null) m = go; }
                else if (t == null) t = go.transform;
            }
            if (t == null || m == null)
            {
                EditorUtility.DisplayDialog("Visual", "Selecciona el objeto lógico en la jerarquía y, con Ctrl, el modelo en la ventana Project.", "OK");
                return;
            }
            var w = CreateInstance<VisualPlacer>();
            w.target = t; w.model = m; w.yaw = yawDeg; w.fitToCollider = fit; w.keepProportions = prop;
            w.AddVisual();
            DestroyImmediate(w);
        }

        public static void PrepareRoot(Transform t)
        {
            var box = t.GetComponent<BoxCollider>();
            Vector3 s = t.localScale;
            Undo.RecordObject(t, "Preparar raíz lógica");
            if (box != null)
            {
                Undo.RecordObject(box, "Preparar raíz lógica");
                box.size = Vector3.Scale(box.size, s);
                box.center = Vector3.Scale(box.center, s);
            }
            // los hijos existentes conservan su posición en el mundo
            var kids = new System.Collections.Generic.List<Transform>();
            foreach (Transform c in t) kids.Add(c);
            foreach (var c in kids) Undo.SetTransformParent(c, null, "Preparar raíz lógica");
            t.localScale = Vector3.one;
            foreach (var c in kids) Undo.SetTransformParent(c, t, "Preparar raíz lógica");
            var mr = t.GetComponent<MeshRenderer>();
            if (mr != null) { Undo.RecordObject(mr, "Preparar raíz lógica"); mr.enabled = false; }
            EditorUtility.SetDirty(t.gameObject);
        }

        void AddVisual()
        {
            if (target.localScale != Vector3.one) PrepareRoot(target);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, target);
            Undo.RegisterCreatedObjectUndo(inst, "Añadir visual");
            inst.name = "Visual_" + model.name.Replace("_1k", "");
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * inst.transform.localRotation;
            inst.transform.localScale = inst.transform.localScale * extraScale;
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Undo.DestroyObjectImmediate(c);

            var box = target.GetComponent<BoxCollider>();
            if (box != null)
            {
                Bounds b = LocalBounds(target, inst);
                if (b.size.sqrMagnitude > 1e-6f)
                {
                    if (fitToCollider)
                    {
                        Vector3 k = new Vector3(box.size.x / Mathf.Max(1e-4f, b.size.x), box.size.y / Mathf.Max(1e-4f, b.size.y), box.size.z / Mathf.Max(1e-4f, b.size.z));
                        if (keepProportions)
                        {
                            float u = Mathf.Min(k.x, Mathf.Min(k.y, k.z));
                            inst.transform.localScale *= u;
                        }
                        else inst.transform.localScale = Vector3.Scale(inst.transform.localScale, k);
                    }
                    // siempre: apoyado en la base del collider y centrado en planta
                    b = LocalBounds(target, inst);
                    Vector3 wantBottom = box.center - new Vector3(0f, box.size.y * 0.5f, 0f);
                    Vector3 haveBottom = new Vector3(b.center.x, b.min.y, b.center.z);
                    inst.transform.localPosition += wantBottom - haveBottom;
                }
            }
            inst.transform.localPosition += extraOffset;
            Selection.activeGameObject = inst;
            EditorUtility.SetDirty(target.gameObject);
        }

        static Bounds LocalBounds(Transform root, GameObject go)
        {
            bool has = false;
            Bounds b = new Bounds();
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                Mesh mesh = null;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null) mesh = mf.sharedMesh;
                var smr = r as SkinnedMeshRenderer;
                if (smr != null) mesh = smr.sharedMesh;   // modelos con rig (p. ej. reloj con agujas)
                if (mesh == null) continue;
                var mb = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = root.InverseTransformPoint(r.transform.TransformPoint(c));
                    if (!has) { b = new Bounds(p, Vector3.zero); has = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }
    }
}
