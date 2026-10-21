using System.Collections.Generic;
using UnityEngine;

namespace SomethingIsWrong
{
    public enum ReportResult { Correct, WrongCategory, WrongRoom, FalsePositive, OldPhoto, AlreadyReported }

    /// <summary>
    /// Anomalías de la casa, organizadas por VUELTAS.
    /// · Todas las anomalías están colocadas a mano en la escena (componente Anomaly con referencias explícitas).
    /// · BeginLap(n, cuántas) elige las de esa vuelta: no repite anomalías ya usadas mientras queden otras, intenta que
    ///   sean de habitaciones distintas, nunca dos sobre el mismo objeto, y en la vuelta de aprendizaje entra la de tutorial.
    /// · Tick() las activa de una en una, solo cuando el jugador no mira.
    /// · ReportPhoto() resuelve un reporte hecho desde el carrete: foto válida + habitación + categoría.
    /// · ResetLap() devuelve la casa al estado normal al cerrar la vuelta.
    /// </summary>
    public class AnomalyManager : MonoBehaviour
    {
        public Vector2 interval = new Vector2(20f, 35f);
        public float ignoreAfter = 150f;
        public float tutorialIgnoreAfter = 300f;

        readonly List<Anomaly> all = new List<Anomaly>();
        readonly List<Anomaly> lap = new List<Anomaly>();
        readonly List<Anomaly> pending = new List<Anomaly>();
        float nextTime = -1f;
        GameDirector director;
        System.Random rng;

        public int LapNumber { get; private set; }
        public int Total { get { return lap.Count; } }
        public int ReportedCount { get; private set; }
        public int TotalReportedAllLaps { get; private set; }
        public bool AllReported { get { return Total > 0 && ReportedCount >= Total; } }
        public bool AllApplied { get { return pending.Count == 0; } }
        public IList<Anomaly> Selected { get { return lap; } }
        public int PoolSize { get { return all.Count; } }

        public int ActiveCount
        {
            get
            {
                int n = 0;
                foreach (var a in lap) if (a.active && !a.reported) n++;
                return n;
            }
        }

        public void Init(GameDirector d, LevelRoot level, int seed)
        {
            director = d;
            rng = seed == 0 ? new System.Random() : new System.Random(seed);
            all.Clear();
            foreach (var a in level.anomalies)
            {
                if (a == null) continue;
                string err;
                if (!a.IsValid(out err)) { Debug.LogError("[Anomalías] '" + a.anomalyName + "' descartada: " + err, a); continue; }
                if (a.room != null && a.room.isSafe) { Debug.LogError("[Anomalías] '" + a.anomalyName + "' está en el refugio: se descarta.", a); continue; }
                if (a.EnsureLinked() == null) { Debug.LogError("[Anomalías] '" + a.anomalyName + "' descartada: no hay ningún Reportable al que apuntar.", a); continue; }
                all.Add(a);
            }
            if (Application.isEditor || Debug.isDebugBuild)
            {
                var sb = new System.Text.StringBuilder("[Anomalías] Catálogo de la casa: " + all.Count + " válidas → ");
                foreach (var a in all) sb.Append(a.anomalyName.Trim()).Append(" (").Append(a.room.displayName).Append(", ").Append(a.primary).Append(") · ");
                Debug.Log(sb.ToString());
            }
        }

        /// <summary>Prepara las anomalías de la vuelta <paramref name="lapNumber"/> (1..N).</summary>
        public void BeginLap(int lapNumber, int count, bool tutorialFirst)
        {
            LapNumber = lapNumber;
            ResetLap();
            ReportedCount = 0;
            lap.Clear();
            pending.Clear();
            nextTime = -1f;

            var fresh = all.FindAll(a => a.usedInLap == 0);
            var used = all.FindAll(a => a.usedInLap != 0);
            Shuffle(fresh);
            Shuffle(used);
            var order = new List<Anomaly>();
            Anomaly tutorial = tutorialFirst ? all.Find(a => a.tutorial) : null;
            if (tutorial != null) order.Add(tutorial);
            foreach (var a in fresh) if (a != tutorial && !a.tutorial) order.Add(a);
            foreach (var a in used) if (a != tutorial && !a.tutorial) order.Add(a);

            var touched = new HashSet<Object>();
            var rooms = new HashSet<Room>();
            // Dos pasadas: primero habitaciones distintas, luego lo que falte.
            for (int pass = 0; pass < 2 && lap.Count < count; pass++)
                foreach (var a in order)
                {
                    if (lap.Count >= count) break;
                    if (lap.Contains(a)) continue;
                    if (pass == 0 && rooms.Contains(a.room)) continue;
                    bool clash = false;
                    foreach (var o in a.TouchedObjects()) if (touched.Contains(o)) { clash = true; break; }
                    if (clash) continue;
                    foreach (var o in a.TouchedObjects()) touched.Add(o);
                    rooms.Add(a.room);
                    lap.Add(a);
                }
            foreach (var a in lap)
            {
                a.usedInLap = lapNumber;
                a.reported = false;
                a.unreportedTime = 0f;
                a.ignoredPenalized = false;
            }
            pending.AddRange(lap);
            if (tutorial != null && lap.Contains(tutorial)) { pending.Remove(tutorial); pending.Insert(0, tutorial); }
            if (lap.Count < count) Debug.LogWarning("[Anomalías] Vuelta " + lapNumber + ": se pedían " + count + " y solo hay " + lap.Count + " compatibles.");
            if (Application.isEditor || Debug.isDebugBuild)
            {
                var names = new List<string>();
                foreach (var a in lap) names.Add(a.anomalyName.Trim() + "@" + a.room.displayName);
                Debug.Log("[Vuelta " + lapNumber + "] Anomalías elegidas: " + string.Join(", ", names.ToArray()));
            }
        }

        /// <summary>Cierre de vuelta: todo lo que siga activo vuelve a su estado normal.</summary>
        public void ResetLap()
        {
            foreach (var a in all) if (a.active) a.Revert();
            pending.Clear();
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

        /// <summary>Se llama cada frame mientras la vuelta está en investigación.</summary>
        public void Tick(PlayerController player, LevelRoot level)
        {
            if (nextTime < 0f) nextTime = Time.time + Range(4f, 7f);
            if (pending.Count > 0 && Time.time >= nextTime)
            {
                bool applied = false;
                for (int i = 0; i < pending.Count; i++)
                {
                    var a = pending[i];
                    if (!CanApply(a, player, level)) { if (a.tutorial) break; continue; }
                    a.Apply();
                    if (Application.isEditor || Debug.isDebugBuild)
                        Debug.Log("[Anomalía] Activada: «" + a.anomalyName.Trim() + "» (" + a.kind + ", " + a.primary + ") en " + a.room.displayName);
                    pending.RemoveAt(i);
                    applied = true;
                    nextTime = Time.time + Range(interval.x, interval.y);
                    if (a.tutorial) director.OnTutorialAnomaly(a);
                    if (a.critical) director.OnCriticalAnomaly(a);
                    break;
                }
                if (!applied) nextTime = Time.time + 1.5f;
            }

            foreach (var a in lap)
            {
                if (!a.active || a.reported) continue;
                a.unreportedTime += Time.deltaTime;
                float limit = a.tutorial ? tutorialIgnoreAfter : ignoreAfter;
                if (!a.ignoredPenalized && a.unreportedTime > limit)
                {
                    a.ignoredPenalized = true;
                    director.AddError("Una anomalía lleva demasiado tiempo sin reportar: algo en la casa lo ha notado.", a.critical ? 2 : 1);
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
            // Nunca se cierra ni desaparece una puerta de la habitación donde está el jugador (podría dejarlo encerrado).
            if ((a.kind == AnomalyKind.DoorVanish || a.kind == AnomalyKind.DoorLock) && a.door != null && playerRoom != null && a.door.Connects(playerRoom)) return false;
            if (playerRoom == a.room && flatDist < 3.5f) return false;
            if (flatDist < 2.5f) return false;
            if (Visibility.IsVisible(player.Cam, anchor, 1.2f)) return false;
            return true;
        }

        /// <summary>
        /// Reporte desde el carrete. Es correcto si la foto es válida, muestra un objeto con una anomalía de ESTA vuelta
        /// que estaba activa al hacer la foto y sigue activa, y la habitación y la categoría elegidas coinciden.
        /// </summary>
        public ReportResult ReportPhoto(EvidenceCamera.Photo photo, Room room, AnomalyCategory c, out Anomaly found)
        {
            found = null;
            if (photo == null || photo.subject == null) return ReportResult.FalsePositive;
            if (photo.lap != LapNumber) return ReportResult.OldPhoto;
            Anomaly candidate = null;
            bool sawReported = false;
            foreach (var a in photo.anomaliesAtCapture)
            {
                if (a == null || !lap.Contains(a)) continue;
                if (a.reported) { sawReported = true; continue; }
                if (!a.active) continue;
                if (candidate == null) candidate = a;
                if (a.room == room && a.Accepts(c)) { found = a; break; }
            }
            if (found != null)
            {
                found.reported = true;
                found.Revert();
                ReportedCount++;
                TotalReportedAllLaps++;
                return ReportResult.Correct;
            }
            if (candidate != null)
            {
                found = candidate;
                return candidate.room != room ? ReportResult.WrongRoom : ReportResult.WrongCategory;
            }
            return sawReported ? ReportResult.AlreadyReported : ReportResult.FalsePositive;
        }

        /// <summary>¿El objeto tiene ahora mismo una anomalía activa de esta vuelta? (para la validación de la foto)</summary>
        public void ActiveAnomaliesOn(Reportable r, List<Anomaly> into)
        {
            into.Clear();
            if (r == null) return;
            foreach (var a in r.linked) if (a != null && a.active && lap.Contains(a)) into.Add(a);
        }
    }
}

