using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SomethingIsWrong.EditorTools
{
    /// <summary>
    /// Revisión de la escena abierta (NO crea ni modifica nada). Comprueba lo que la lógica necesita de un piso
    /// construido a mano: habitaciones, puertas, grafo, refugio, escondites, anomalías y sus reportes, salida,
    /// monstruo del piso, scripts perdidos y presupuesto de luces con sombra.
    /// Resultado en la Console: errores (rompen la partida), avisos (conviene revisarlos) e información.
    /// </summary>
    public static class SceneValidator
    {
        const float DoorClearHeight = 2.2f;

        [MenuItem("ANOMALY DESCENT/Validar escena abierta", priority = 10)]
        public static void ValidateMenu()
        {
            int e, w;
            string summary = Validate(out e, out w);
            EditorUtility.DisplayDialog("Validación de escena", summary, "OK");
        }

        public static string Validate(out int errors, out int warnings)
        {
            int err = 0, warn = 0;
            var log = new StringBuilder();
            System.Action<string, Object> E = (m, o) => { err++; Debug.LogError("[Validar] " + m, o); };
            System.Action<string, Object> W = (m, o) => { warn++; Debug.LogWarning("[Validar] " + m, o); };

            var scene = EditorSceneManager.GetActiveScene();

            // 1. Scripts perdidos
            int missing = 0;
            foreach (var go in scene.GetRootGameObjects())
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    int n = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    if (n > 0) { missing += n; E("Script perdido en '" + t.name + "'.", t.gameObject); }
                }

            // 2. Sistemas
            var levels = Object.FindObjectsByType<LevelRoot>(FindObjectsInactive.Include);
            var boots = Object.FindObjectsByType<GameBootstrap>(FindObjectsInactive.Include);
            if (levels.Length != 1) E("Debe haber exactamente 1 LevelRoot (hay " + levels.Length + ").", null);
            if (boots.Length != 1) E("Debe haber exactamente 1 GameBootstrap (hay " + boots.Length + ").", null);
            if (levels.Length == 0) return Finish(scene.name, err, warn, out errors, out warnings);
            var level = levels[0];
            var boot = boots.Length > 0 ? boots[0] : null;

            level.Refresh();   // rellena referencias automáticas (spawn, luces, habitación de anomalías) igual que en Play
            var rooms = new List<Room>(level.GetComponentsInChildren<Room>(true));
            var doors = new List<DoorLink>(level.GetComponentsInChildren<DoorLink>(true));
            var hides = new List<HideSpot>(level.GetComponentsInChildren<HideSpot>(true));
            var anomalies = new List<Anomaly>(level.GetComponentsInChildren<Anomaly>(true));
            foreach (var r in rooms) r.SyncFromVolume();

            // 3. Habitaciones
            if (rooms.Count == 0) E("No hay habitaciones (componentes Room) bajo el LevelRoot.", level);
            var ids = new HashSet<string>();
            int safe = 0, picks = 0;
            foreach (var r in rooms)
            {
                if (string.IsNullOrEmpty(r.id)) E("Habitación sin 'id'.", r);
                else if (!ids.Add(r.id)) E("Id de habitación repetido: " + r.id, r);
                if (r.area.width < 0.5f || r.area.height < 0.5f) E("Habitación '" + r.name + "' sin área: asigna un BoxCollider en 'Volume'.", r);
                if (r.volume == null) W("Habitación '" + r.name + "' usa 'Area' escrita a mano; es más fiable un 'Volume'.", r);
                if (r.roomLight == null) W("Habitación '" + r.name + "' sin RoomLight (no parpadeará ni avisará de la amenaza).", r);
                if (r.isSafe) safe++;
                picks += r.isSafe ? 0 : Mathf.Max(0, r.anomalyPicks);
                for (int j = rooms.IndexOf(r) + 1; j < rooms.Count; j++)
                    if (r.area.Overlaps(ShrinkRect(rooms[j].area, 0.05f)))
                        W("Las áreas de '" + r.name + "' y '" + rooms[j].name + "' se solapan: RoomAt puede devolver la equivocada.", r);
            }
            if (safe == 0) W("No hay refugio (Room con 'Is Safe').", level);

            // 4. Puertas y grafo
            foreach (var d in doors)
            {
                if (d.roomA == null || d.roomB == null) { E("Puerta '" + d.name + "' sin roomA/roomB.", d); continue; }
                if (!d.roomA.Contains(d.approachA, 0.05f)) E("Puerta '" + d.name + "': approachA no cae dentro de " + d.roomA.name + ".", d);
                if (!d.roomB.Contains(d.approachB, 0.05f)) E("Puerta '" + d.name + "': approachB no cae dentro de " + d.roomB.name + ".", d);
                if (d.vanishPatch != null && d.vanishPatch.activeSelf) E("Puerta '" + d.name + "': 'vanishPatch' debe empezar desactivado.", d);
                if (d.leaf != null && d.leaf.GetComponentInChildren<Collider>(true) == null) W("Hoja de '" + d.name + "' sin colisionador.", d.leaf);
            }
            if (rooms.Count > 1)
            {
                var start = level.playerSpawn != null ? RoomAtList(rooms, level.playerSpawn.position) : rooms[0];
                if (start == null) { E("El 'Player Spawn' no está dentro de ninguna habitación.", level.playerSpawn); start = rooms[0]; }
                var seen = new HashSet<Room> { start };
                var q = new Queue<Room>(); q.Enqueue(start);
                while (q.Count > 0)
                {
                    var r = q.Dequeue();
                    foreach (var d in doors)
                    {
                        if (d.roomA == null || d.roomB == null || (d.roomA != r && d.roomB != r)) continue;
                        var n = d.roomA == r ? d.roomB : d.roomA;
                        if (seen.Add(n)) q.Enqueue(n);
                    }
                }
                foreach (var r in rooms) if (!seen.Contains(r)) E("La habitación '" + r.name + "' no está conectada por puertas con el resto.", r);
                foreach (var r in rooms)
                {
                    int exits = 0;
                    foreach (var d in doors) if (d.Connects(r)) exits++;
                    if (!r.isSafe && exits < 2) W("'" + r.name + "' solo tiene " + exits + " salida(s): el jugador puede quedar acorralado.", r);
                    if (r.isSafe && exits != 1) W("El refugio '" + r.name + "' tiene " + exits + " puertas; se diseñó con una sola entrada.", r);
                }
            }
            else if (level.playerSpawn == null) W("LevelRoot sin 'Player Spawn'.", level);

            // 5. Escondites
            foreach (var h in hides)
            {
                var vp = h.viewPoint != null ? h.viewPoint : h.transform.Find("VistaEscondite");
                var ep = h.exitPoint != null ? h.exitPoint : h.transform.Find("SalidaEscondite");
                if (vp == null || ep == null) { E("Escondite '" + h.name + "' sin viewPoint/exitPoint (o hijos 'VistaEscondite'/'SalidaEscondite').", h); continue; }
                if (RoomAtList(rooms, ep.position) == null) E("Escondite '" + h.name + "': la salida no cae dentro de una habitación.", h);
                if (h.room != null && h.room.isSafe) W("Escondite '" + h.name + "' dentro del refugio (no aporta nada).", h);
                if (h.GetComponentInChildren<Collider>(true) == null) E("Escondite '" + h.name + "' sin colisionador: no se puede interactuar.", h);
            }

            // 6. Anomalías
            int valid = 0;
            var touched = new Dictionary<Object, Anomaly>();
            foreach (var a in anomalies)
            {
                string why;
                if (!a.IsValid(out why)) { E("Anomalía '" + a.anomalyName + "': " + why, a); continue; }
                var rep = a.reportTarget;
                if (rep == null && a.target != null) rep = a.target.GetComponentInParent<Reportable>(true);
                if (rep == null && a.showObject != null) rep = a.showObject.GetComponentInParent<Reportable>(true);
                if (rep == null && a.door != null && a.door.vanishPatch != null) rep = a.door.vanishPatch.GetComponentInChildren<Reportable>(true);
                if (rep == null && a.kind == AnomalyKind.CeilingLower && a.room.ceilingRoot != null) rep = a.room.ceilingRoot.GetComponentInChildren<Reportable>(true);
                if (rep == null && a.swapRenderer != null) rep = a.swapRenderer.GetComponentInParent<Reportable>(true);
                if (rep == null) { E("Anomalía '" + a.anomalyName + "': no hay Reportable al que apuntar (añade 'Report Target').", a); continue; }
                if (rep.GetComponentInChildren<Collider>(true) == null) E("Anomalía '" + a.anomalyName + "': su Reportable '" + rep.name + "' no tiene colisionador (el rayo de [R] no lo alcanza).", rep);
                foreach (var o in a.TouchedObjects())
                {
                    Anomaly other;
                    if (touched.TryGetValue(o, out other)) W("'" + a.anomalyName + "' y '" + other.anomalyName + "' modifican el mismo objeto; nunca saldrán juntas.", a);
                    else touched[o] = a;
                }
                valid++;
            }
            int tut = 0;
            foreach (var a in anomalies) if (a.tutorial) tut++;
            if (valid == 0) E("No hay ninguna anomalía válida: la reja se abriría sin informe.", level);
            if (tut != 1) W("Debe haber exactamente 1 anomalía de tutorial (hay " + tut + ").", level);
            if (picks == 0 && valid > 0) E("La suma de 'Anomaly Picks' de las habitaciones es 0.", level);

            // 7. Salida, notas y reportables sin colisión
            if (level.GetComponentInChildren<StairGate>(true) == null) E("No hay StairGate (reja de salida).", level);
            var sd = level.GetComponentInChildren<StairDescent>(true);
            if (sd == null) E("No hay StairDescent (final del piso).", level);
            else if (sd.GetComponent<Collider>() == null && sd.area.width <= 0f) E("StairDescent sin BoxCollider trigger.", sd);
            foreach (var r in level.GetComponentsInChildren<Reportable>(true))
                if (r.GetComponentInChildren<Collider>(true) == null) W("Reportable '" + r.name + "' sin colisionador: no se puede apuntar.", r);

            // 8. Monstruo
            var monsters = Object.FindObjectsByType<MonsterBase>(FindObjectsInactive.Include);
            if (monsters.Length == 0) W("No hay monstruo en la escena.", null);
            foreach (var m in monsters)
            {
                if (boot != null && m.floor != boot.floorNumber) E("'" + m.name + "' es del Piso " + m.floor + " y la escena es el Piso " + boot.floorNumber + ".", m);
                if (m.capsuleHeight >= DoorClearHeight) E("'" + m.name + "': cápsula de " + m.capsuleHeight + " m no cabe por puertas de " + DoorClearHeight + " m.", m);
                if (m.visualPrefab == null && m.transform.Find("Visual") == null) W("'" + m.name + "' sin modelo: usará la figura PROVISIONAL.", m);
                if (m.GetComponent("NavMeshAgent") != null) E("'" + m.name + "' tiene NavMeshAgent: competiría con su CharacterController.", m);
            }

            // 9. Luces
            int shadowed = 0;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (l.shadows != LightShadows.None && l.lightmapBakeType != LightmapBakeType.Baked) shadowed++;
            var budget = Object.FindAnyObjectByType<ShadowBudget>();
            if (shadowed > 3 && budget == null) W(shadowed + " luces dinámicas con sombra y sin ShadowBudget: caro en gráficos integrados.", null);

            // 10. Piezas provisionales (primitivas de Unity) en el entorno
            int prims = 0;
            foreach (var mf in level.GetComponentsInChildren<MeshFilter>(true))
            {
                var path = mf.sharedMesh != null ? AssetDatabase.GetAssetPath(mf.sharedMesh) : "";
                if (path == "Library/unity default resources") prims++;
            }
            if (prims > 0) W(prims + " objetos del escenario usan primitivas de Unity (cubos/cilindros…): son PROVISIONALES.", level);

            log.AppendLine("Errores: " + err + "   Avisos: " + warn + "   Scripts perdidos: " + missing);
            log.AppendLine("Habitaciones: " + rooms.Count + " · Puertas: " + doors.Count + " · Escondites: " + hides.Count + " · Anomalías válidas: " + valid + "/" + anomalies.Count);
            Debug.Log("[Validar] " + scene.name + ": " + log);
            return Finish(scene.name, err, warn, out errors, out warnings) + "\n" + log;
        }

        static string Finish(string scene, int err, int warn, out int errors, out int warnings)
        {
            errors = err; warnings = warn;
            return "Escena: " + scene + (err == 0 ? "  ✔ sin errores" : "  ✖ con errores") + " (detalle en la Console).";
        }

        static Rect ShrinkRect(Rect r, float m) { return new Rect(r.x + m, r.y + m, Mathf.Max(0f, r.width - 2 * m), Mathf.Max(0f, r.height - 2 * m)); }

        static Room RoomAtList(List<Room> rooms, Vector3 p)
        {
            foreach (var r in rooms) if (r.Contains(p)) return r;
            return null;
        }

        // ---------------------------------------------------------------- texturas provisionales para anomalías
        [MenuItem("ANOMALY DESCENT/Arte/Exportar texturas PROVISIONALES de anomalías (PNG)", priority = 46)]
        public static void ExportPlaceholderTextures()
        {
            string dir = SiwProjectSetup.Root + "/Art/Textures/Provisional";
            SiwProjectSetup.EnsureFolder(dir);
            Write(dir + "/PROVISIONAL_Retrato_Normal.png", ProceduralTextures.Portrait(false));
            Write(dir + "/PROVISIONAL_Retrato_Anomalia.png", ProceduralTextures.Portrait(true));
            Write(dir + "/PROVISIONAL_Paisaje.png", ProceduralTextures.Landscape(2));
            Write(dir + "/PROVISIONAL_Paisaje_Figura.png", ProceduralTextures.Landscape(2, true));
            Write(dir + "/PROVISIONAL_Espejo_Normal.png", ProceduralTextures.MirrorGlass(false));
            Write(dir + "/PROVISIONAL_Espejo_Huellas.png", ProceduralTextures.MirrorGlass(true));
            Write(dir + "/PROVISIONAL_EsferaReloj.png", ProceduralTextures.ClockFace());
            AssetDatabase.Refresh();
            Debug.Log("[ANOMALY DESCENT] Texturas provisionales en " + dir + ". Sustitúyelas por arte propio (mismo nombre) cuando lo tengas.");
        }

        static void Write(string path, Texture2D t)
        {
            if (t == null) return;
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
        }
    }
}
