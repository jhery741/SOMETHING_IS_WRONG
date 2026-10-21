using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.ProBuilder;

namespace SomethingIsWrong.EditorTools
{
    /// <summary>
    /// Pinta con materiales las caras de la arquitectura ProBuilder YA CONSTRUIDA a mano (no crea ni mueve geometría).
    /// Cada cara recibe el material de suelo / pared / techo de la habitación hacia la que mira (según los volúmenes Room).
    /// Caras de la escalera (objetos con "Esc" en el nombre y fuera de toda habitación) usan el juego "Escalera".
    /// Después se puede retocar cualquier cara a mano con el Material Editor de ProBuilder.
    /// </summary>
    public static class SurfaceMaterialAssigner
    {
        const string Mats = "Assets/AnomalyDescent/Art/Materials/";

        static readonly Dictionary<string, string[]> ByRoom = new Dictionary<string, string[]>
        {
            // room id → { pared, suelo, techo }
            { "dormitorio", new[] { "M_Pared_Dormitorio", "M_Suelo_Dormitorio", "M_Techo" } },
            { "pasillo",    new[] { "M_Pared_Pasillo",    "M_Suelo_Pasillo",    "M_Techo" } },
            { "refugio",    new[] { "M_Pared_Refugio",    "M_Suelo_Refugio",    "M_Techo" } },
            { "escalera",   new[] { "M_Pared_Escalera",   "M_Suelo_Escalera",   "M_Techo" } },
        };

        [MenuItem("ANOMALY DESCENT/Arte/4. Asignar materiales de superficie por habitación", priority = 43)]
        public static void Assign()
        {
            var level = Object.FindAnyObjectByType<LevelRoot>();
            if (level == null) { EditorUtility.DisplayDialog("Materiales", "No hay LevelRoot en la escena abierta.", "OK"); return; }
            var rooms = level.GetComponentsInChildren<Room>(true).ToList();
            foreach (var r in rooms) r.SyncFromVolume();
            var cache = new Dictionary<string, Material>();
            System.Func<string, Material> M = n =>
            {
                Material m;
                if (!cache.TryGetValue(n, out m)) { m = AssetDatabase.LoadAssetAtPath<Material>(Mats + n + ".mat"); cache[n] = m; }
                return m;
            };
            var stairsMat = M("M_Escalones");
            int faces = 0, meshes = 0;
            foreach (var pb in level.GetComponentsInChildren<ProBuilderMesh>(true))
            {
                string goName = pb.gameObject.name;
                bool isStairArch = goName.Contains("Esc");
                bool isStairs = goName.StartsWith("Escalera");
                // solo arquitectura (suelos, paredes, techos, escalera)
                var t = pb.transform;
                bool arch = false;
                for (var p = t.parent; p != null; p = p.parent) if (p.name == "Architecture") { arch = true; break; }
                if (!arch) continue;
                // hojas de puerta y rejas: material propio (M_Puerta, metal), no se tocan
                if (pb.GetComponentInParent<Door>(true) != null || pb.GetComponentInParent<DoorLink>(true) != null || pb.GetComponentInParent<StairGate>(true) != null) continue;
                Undo.RecordObject(pb, "Asignar materiales de superficie");
                Undo.RecordObject(pb.GetComponent<MeshRenderer>(), "Asignar materiales de superficie");
                var groups = new Dictionary<Material, List<Face>>();
                foreach (var f in pb.faces)
                {
                    Material mat;
                    if (isStairs) mat = stairsMat;
                    else
                    {
                        Vector3 n = t.TransformDirection(UnityEngine.ProBuilder.Math.Normal(pb, f)).normalized;
                        Vector3 c = Vector3.zero;
                        foreach (int i in f.distinctIndexes) c += pb.positions[i];
                        c = t.TransformPoint(c / f.distinctIndexes.Count);
                        Vector3 probe = c + n * 0.25f;
                        Room room = null;
                        foreach (var r in rooms)
                            if (r.Contains(probe, -0.01f) && probe.y > r.transform.position.y - 0.3f && probe.y < r.transform.position.y + r.height + 0.3f) { room = r; break; }
                        // el hueco de la escalera no es una habitación jugable: lo que queda al sur del refugio se trata como escalera
                        string key = room != null ? room.id : ((isStairArch || (probe.z < 2.45f && probe.x > 12f)) ? "escalera" : null);
                        if (key == null)
                        {
                            // cara exterior/no visible: usa la habitación más cercana para que no quede en blanco
                            var near = level.NearestRoom(c);
                            key = near != null ? near.id : "pasillo";
                        }
                        string[] set;
                        if (!ByRoom.TryGetValue(key, out set)) set = ByRoom["pasillo"];
                        int idx = n.y > 0.7f ? 1 : (n.y < -0.7f ? 2 : 0);
                        mat = M(set[idx]);
                    }
                    if (mat == null) continue;
                    List<Face> list;
                    if (!groups.TryGetValue(mat, out list)) groups[mat] = list = new List<Face>();
                    list.Add(f);
                }
                foreach (var g in groups) { pb.SetMaterial(g.Value, g.Key); faces += g.Value.Count; }
                // UV en espacio de mundo (1 unidad = 1 m) sin estirar: las piezas escaladas y "congeladas" a mano
                // conservaban UV de la caja original y la textura se veía estirada en los muros largos del pasillo.
                // La escala real de la textura la da el tiling del material (metros por repetición).
                foreach (var f in pb.faces)
                {
                    f.manualUV = false;
                    var u = f.uv;
                    u.useWorldSpace = true;
                    u.fill = AutoUnwrapSettings.Fill.Tile;
                    u.scale = Vector2.one;
                    u.offset = Vector2.zero;
                    u.rotation = 0f;
                    u.anchor = AutoUnwrapSettings.Anchor.None;
                    f.uv = u;
                }
                pb.ToMesh();
                pb.Refresh();
                EditorUtility.SetDirty(pb);
                meshes++;
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(level.gameObject.scene);
            Debug.Log("[Materiales] " + faces + " caras en " + meshes + " mallas de arquitectura.");
        }
    }
}
