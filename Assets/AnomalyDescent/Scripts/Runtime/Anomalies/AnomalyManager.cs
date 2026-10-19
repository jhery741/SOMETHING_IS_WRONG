using System.Collections.Generic;
using UnityEngine;

namespace SomethingIsWrong
{
    public enum ReportResult { Correct, WrongCategory, FalsePositive }

    /// <summary>
    /// Elige las anomalías de la partida (Room.anomalyPicks por habitación; la de tutorial siempre entra
    /// y aparece primero), las activa solo cuando el jugador no mira y resuelve los reportes.
    /// </summary>
    public class AnomalyManager : MonoBehaviour
    {
        public Vector2 interval = new Vector2(28f, 45f);
        /// <summary>Solo desarrollo: adelanta la siguiente anomalía (la aplica el ciclo normal, con sus condiciones).</summary>
        public void DevSkipWait() { nextTime = Time.time; }
        public float ignoreAfter = 150f;
        public float tutorialIgnoreAfter = 240f;

        readonly List<Anomaly> selected = new List<Anomaly>();
        readonly List<Anomaly> pending = new List<Anomaly>();
        float nextTime = -1f;
        GameDirector director;
        System.Random rng;

        public int Total { get { return selected.Count; } }
        public int ReportedCount { get; private set; }
        public bool AllReported { get { return Total > 0 && ReportedCount >= Total; } }
        public IList<Anomaly> Selected { get { return selected; } }

        public int ActiveCount
        {
            get
            {
                int n = 0;
                foreach (var a in selected) if (a.active && !a.reported) n++;
                return n;
            }
        }

        public int CountInRoom(Room r)
        {
            int n = 0;
            foreach (var a in selected) if (a.room == r) n++;
            return n;
        }

        public void Init(GameDirector d, LevelRoot level, int seed)
        {
            director = d;
            rng = seed == 0 ? new System.Random() : new System.Random(seed);
            selected.Clear();
            pending.Clear();
            ReportedCount = 0;

            var byRoom = new Dictionary<Room, List<Anomaly>>();
            foreach (var a in level.anomalies)
            {
                if (a == null) continue;
                string err;
                if (!a.IsValid(out err)) { Debug.LogError("[Anomalías] '" + a.anomalyName + "' descartada: " + err, a); continue; }
                if (a.EnsureLinked() == null) { Debug.LogError("[Anomalías] '" + a.anomalyName + "' descartada: no hay ningún Reportable al que apuntar.", a); continue; }
                List<Anomaly> list;
                if (!byRoom.TryGetValue(a.room, out list)) { list = new List<Anomaly>(); byRoom[a.room] = list; }
                list.Add(a);
            }

            Anomaly tutorial = null;
            var touched = new HashSet<Object>();
            foreach (var room in level.rooms)
            {
                List<Anomaly> list;
                if (!byRoom.TryGetValue(room, out list)) continue;
                Shuffle(list);
                int picks = Mathf.Min(room.anomalyPicks, list.Count);
                var t = list.Find(x => x.tutorial);
                if (t != null && picks > 0 && tutorial == null)
                {
                    list.Remove(t);
                    list.Insert(0, t);
                    tutorial = t;
                }
                int got = 0;
                foreach (var a in list)
                {
                    if (got >= picks) break;
                    // Nunca dos anomalías sobre el mismo objeto en la misma partida (se pisarían al aplicar/revertir).
                    bool clash = false;
                    foreach (var o in a.TouchedObjects()) if (touched.Contains(o)) { clash = true; break; }
                    if (clash) continue;
                    foreach (var o in a.TouchedObjects()) touched.Add(o);
                    selected.Add(a);
                    got++;
                }
                if (got < room.anomalyPicks)
                    Debug.LogWarning("[Anomalías] " + room.displayName + ": se pedían " + room.anomalyPicks + " y solo hay " + got + " compatibles.");
            }
            if (tutorial != null && !selected.Contains(tutorial)) tutorial = null;

            pending.AddRange(selected);
            Shuffle(pending);
            if (tutorial != null)
            {
                pending.Remove(tutorial);
                pending.Insert(0, tutorial);
            }
        }

        void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T t = list[i]; list[i] = list[j]; list[j] = t;
            }
        }

        float Range(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

        /// <summary>Se llama cada frame mientras la casa "está viva" (investigación y monstruo activo).</summary>
        public void Tick(PlayerController player, LevelRoot level)
        {
            if (nextTime < 0f) nextTime = Time.time + Range(5f, 9f);
            if (pending.Count > 0 && Time.time >= nextTime)
            {
                bool applied = false;
                for (int i = 0; i < pending.Count; i++)
                {
                    var a = pending[i];
                    if (!CanApply(a, player, level)) { if (a.tutorial) break; continue; }
                    a.Apply();
                    if (Application.isEditor || Debug.isDebugBuild)
                        Debug.Log("[Anomalía] Activada: «" + a.anomalyName + "» (" + a.kind + ", " + a.primary + ") en " + (a.room != null ? a.room.displayName : "?"));
                    pending.RemoveAt(i);
                    applied = true;
                    nextTime = Time.time + Range(interval.x, interval.y);
                    if (a.tutorial) director.OnTutorialAnomaly(a);
                    if (a.critical) director.OnCriticalAnomaly(a);
                    break;
                }
                if (!applied) nextTime = Time.time + 1.5f;
            }

            foreach (var a in selected)
            {
                if (!a.active || a.reported) continue;
                a.unreportedTime += Time.deltaTime;
                float limit = a.tutorial ? tutorialIgnoreAfter : ignoreAfter;
                if (!a.ignoredPenalized && a.unreportedTime > limit)
                {
                    a.ignoredPenalized = true;
                    if (a.critical) director.AddError("Una anomalía crítica ignorada se ha asentado en la casa (+2).", 2);
                    else director.AddError("Una anomalía ignorada se ha asentado en la casa.", 1);
                    break;
                }
            }
        }

        bool CanApply(Anomaly a, PlayerController player, LevelRoot level)
        {
            if (player.IsHidden) return false;
            Vector3 pp = player.transform.position;
            Vector3 anchor = a.AnchorPosition;
            Room playerRoom = level.RoomAt(pp);
            float flatDist = Vector2.Distance(new Vector2(pp.x, pp.z), new Vector2(anchor.x, anchor.z));

            if (a.kind == AnomalyKind.CeilingLower && playerRoom == a.room) return false;
            if (a.kind == AnomalyKind.DoorVanish && a.door != null && playerRoom != null && a.door.Connects(playerRoom)) return false;
            if (playerRoom == a.room && flatDist < 3.5f) return false;
            if (flatDist < 2.5f) return false;
            if (Visibility.IsVisible(player.Cam, anchor, 1.2f)) return false;
            return true;
        }

        public ReportResult Report(Reportable r, AnomalyCategory c, out Anomaly found)
        {
            found = null;
            if (r == null) return ReportResult.FalsePositive;
            Anomaly candidate = null;
            foreach (var a in r.linked)
            {
                if (a == null || !a.active || a.reported || !selected.Contains(a)) continue;
                candidate = a;
                if (a.Accepts(c)) { found = a; break; }
            }
            if (found != null)
            {
                found.reported = true;
                found.Revert();
                ReportedCount++;
                return ReportResult.Correct;
            }
            if (candidate != null) { found = candidate; return ReportResult.WrongCategory; }
            return ReportResult.FalsePositive;
        }
    }
}
