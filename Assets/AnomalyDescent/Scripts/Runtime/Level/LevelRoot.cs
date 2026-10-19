using System.Collections.Generic;
using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>Raíz de una planta: habitaciones, puertas, escondites, pistas, escalera y rutas entre habitaciones.</summary>
    public class LevelRoot : MonoBehaviour
    {
        public static LevelRoot Instance { get; private set; }

        public string floorName = "PISO 1";
        public List<Room> rooms = new List<Room>();
        public List<DoorLink> doors = new List<DoorLink>();
        public List<HideSpot> hideSpots = new List<HideSpot>();
        public List<NoteItem> notes = new List<NoteItem>();
        public Transform playerSpawn;
        public StairGate stairGate;
        public StairDescent stairDescent;
        [System.NonSerialized] public List<Anomaly> anomalies = new List<Anomaly>();

        void Awake() { Refresh(); }
        void OnDestroy() { if (Instance == this) Instance = null; }

        public void Refresh()
        {
            Instance = this;
            if (anomalies == null) anomalies = new List<Anomaly>();
            GetComponentsInChildren(true, anomalies);
            if (rooms.Count == 0) GetComponentsInChildren(true, rooms);
            if (doors.Count == 0) GetComponentsInChildren(true, doors);
            if (hideSpots.Count == 0) GetComponentsInChildren(true, hideSpots);
            if (notes.Count == 0) GetComponentsInChildren(true, notes);
            if (stairGate == null) stairGate = GetComponentInChildren<StairGate>(true);
            if (stairDescent == null) stairDescent = GetComponentInChildren<StairDescent>(true);
            AutoWire();
        }

        /// <summary>
        /// Completa referencias que falten usando la posición de los objetos (no crea nada):
        /// punto de aparición ("SpawnJugador"/"PlayerSpawn"), luz de cada habitación, habitación de escondites y anomalías.
        /// Lo asignado a mano en el Inspector siempre tiene prioridad.
        /// </summary>
        public void AutoWire()
        {
            foreach (var r in rooms) if (r != null) r.SyncFromVolume();
            if (playerSpawn == null)
                foreach (var t in GetComponentsInChildren<Transform>(true))
                    if (t.name == "SpawnJugador" || t.name == "PlayerSpawn") { playerSpawn = t; break; }
            var lights = GetComponentsInChildren<RoomLight>(true);
            foreach (var r in rooms)
            {
                if (r == null || r.roomLight != null) continue;
                foreach (var l in lights) if (r.Contains(l.transform.position)) { r.roomLight = l; break; }
            }
            foreach (var h in hideSpots)
                if (h != null && h.room == null) h.room = RoomAt(h.transform.position) ?? NearestRoom(h.transform.position);
            foreach (var a in anomalies)
            {
                if (a == null || a.room != null) continue;
                Vector3 p = a.target != null ? a.target.position : (a.showObject != null ? a.showObject.transform.position : a.transform.position);
                a.room = RoomAt(p) ?? NearestRoom(p);
            }
        }

        public Room RoomAt(Vector3 p)
        {
            for (int i = 0; i < rooms.Count; i++) if (rooms[i].Contains(p)) return rooms[i];
            return null;
        }

        public Room NearestRoom(Vector3 p)
        {
            Room best = null;
            float bd = float.MaxValue;
            foreach (var r in rooms)
            {
                float d = (r.Center - new Vector3(p.x, 0f, p.z)).sqrMagnitude;
                if (d < bd) { bd = d; best = r; }
            }
            return best;
        }

        public Room RoomById(string id)
        {
            foreach (var r in rooms) if (r.id == id) return r;
            return null;
        }

        public bool AreAdjacent(Room a, Room b)
        {
            if (a == null || b == null) return false;
            foreach (var d in doors) if (d != null && d.Passable && d.Connects(a) && d.Connects(b)) return true;
            return false;
        }

        /// <summary>Camino de puertas entre dos habitaciones (BFS). soundOnly: ignora cerraduras (el sonido atraviesa puertas cerradas).</summary>
        List<DoorLink> DoorPath(Room a, Room b, bool allowSafe, bool soundOnly = false)
        {
            var prev = new Dictionary<Room, DoorLink>();
            var q = new Queue<Room>();
            q.Enqueue(a);
            prev[a] = null;
            while (q.Count > 0)
            {
                var r = q.Dequeue();
                if (r == b) break;
                foreach (var d in doors)
                {
                    if (d == null) continue;
                    bool ok = soundOnly ? !d.vanished : d.Passable;
                    if (!ok || !d.Connects(r)) continue;
                    var n = d.Other(r);
                    if (n == null || prev.ContainsKey(n)) continue;
                    if (n.isSafe && !allowSafe) continue;
                    prev[n] = d;
                    q.Enqueue(n);
                }
            }
            if (!prev.ContainsKey(b)) return null;
            var list = new List<DoorLink>();
            var cur = b;
            while (cur != a)
            {
                var d = prev[cur];
                list.Add(d);
                cur = d.Other(cur);
            }
            list.Reverse();
            return list;
        }

        /// <summary>Puntos de paso de "from" a "to" cruzando las puertas necesarias (compatibilidad).</summary>
        public List<Vector3> FindPath(Vector3 from, Vector3 to, bool allowSafe)
        {
            var res = new List<Vector3>();
            if (!TryFindPath(from, to, allowSafe, res) && res.Count == 0) res.Add(to);
            return res;
        }

        /// <summary>
        /// Rellena "result" con los puntos de paso. Devuelve false si no hay conexión caminable
        /// (puerta desaparecida o con llave): en ese caso "result" queda vacío y el monstruo debe decidir otra cosa.
        /// </summary>
        public bool TryFindPath(Vector3 from, Vector3 to, bool allowSafe, List<Vector3> result)
        {
            result.Clear();
            Room a = RoomAt(from) ?? NearestRoom(from);
            Room b = RoomAt(to) ?? NearestRoom(to);
            if (a == null || b == null || a == b) { result.Add(to); return true; }
            var path = DoorPath(a, b, allowSafe);
            if (path == null) return false;
            var cur = a;
            foreach (var d in path)
            {
                result.Add(d.ApproachFor(cur));
                var next = d.Other(cur);
                result.Add(d.ApproachFor(next));
                cur = next;
            }
            result.Add(to);
            return true;
        }

        /// <summary>
        /// Atenuación del sonido entre dos puntos (1 = misma habitación). Cada puerta abierta ×0.75,
        /// cada puerta cerrada ×0.45; sin conexión (a través de paredes) ×0.25.
        /// </summary>
        public float NoiseAttenuation(Vector3 from, Vector3 to)
        {
            Room a = RoomAt(from), b = RoomAt(to);
            if (a == null || b == null) return 0.5f;
            if (a == b) return 1f;
            var path = DoorPath(a, b, true, true);
            if (path == null) return 0.25f;
            float k = 1f;
            foreach (var d in path) k *= d.IsOpenNow ? 0.75f : 0.45f;
            return Mathf.Max(k, 0.25f);
        }

        public int GraphDistance(Room a, Room b)
        {
            if (a == null || b == null) return 99;
            if (a == b) return 0;
            var p = DoorPath(a, b, true);
            return p == null ? 99 : p.Count;
        }

        /// <summary>Punto justo fuera del refugio (donde el monstruo se queda).</summary>
        public Vector3 OutsideOfSafeRoom(Room safe)
        {
            foreach (var d in doors)
            {
                if (!d.Connects(safe)) continue;
                var other = d.Other(safe);
                if (other != null && !other.isSafe) return d.ApproachFor(other);
            }
            return safe.Center;
        }
    }
}
