using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>Conexión entre dos habitaciones (con o sin hoja de puerta). Puede "desaparecer" como anomalía.</summary>
    public class DoorLink : MonoBehaviour
    {
        [Tooltip("Rellena solo approachA/B, roomA/B y leaf a partir de la posición y orientación de la puerta " +
                 "(el eje Z azul del objeto debe atravesar la pared). Desactívalo para escribirlos a mano.")]
        public bool autoSetup = true;
        [Tooltip("Distancia a cada lado de la puerta para los puntos de paso del monstruo.")]
        public float approachDistance = 0.9f;
        public Room roomA, roomB;
        public Vector3 approachA, approachB;
        public Door leaf;
        public GameObject frameRoot;
        public GameObject vanishPatch;
        public bool vanished;

        /// <summary>Se puede cruzar caminando: existe y no está cerrada con llave (cerrada sin llave sí: se abre).</summary>
        public bool Passable { get { return !vanished && !(leaf != null && leaf.locked); } }
        /// <summary>Hueco abierto ahora mismo (para el sonido).</summary>
        public bool IsOpenNow { get { return !vanished && (leaf == null || leaf.isOpen); } }
        public bool TouchesSafeRoom { get { return (roomA != null && roomA.isSafe) || (roomB != null && roomB.isSafe); } }
        public Room Other(Room r) { return r == roomA ? roomB : roomA; }
        public Vector3 ApproachFor(Room r) { return r == roomA ? approachA : approachB; }
        public bool Connects(Room r) { return r == roomA || r == roomB; }

        void Awake() { AutoSetup(); }
        void OnValidate() { AutoSetup(); }

        /// <summary>Calcula puntos de paso y habitaciones a ambos lados (no crea nada: solo rellena referencias).</summary>
        public void AutoSetup()
        {
            if (!autoSetup) return;
            if (leaf == null) leaf = GetComponentInChildren<Door>(true);
            Vector3 n = transform.forward; n.y = 0f;
            if (n.sqrMagnitude < 1e-4f) return;
            n.Normalize();
            Vector3 p = transform.position; p.y = 0f;
            approachA = p + n * approachDistance;
            approachB = p - n * approachDistance;
            var level = GetComponentInParent<LevelRoot>();
            if (level == null) return;
            roomA = roomB = null;   // una puerta duplicada no debe conservar las habitaciones de la original
            foreach (var r in level.GetComponentsInChildren<Room>(true))
            {
                r.SyncFromVolume();
                if (r.Contains(approachA)) roomA = r;
                else if (r.Contains(approachB)) roomB = r;
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(approachA + Vector3.up * 0.1f, 0.15f);
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(approachB + Vector3.up * 0.1f, 0.15f);
        }

        public void SetVanished(bool v)
        {
            vanished = v;
            if (frameRoot != null) frameRoot.SetActive(!v);
            if (leaf != null) leaf.gameObject.SetActive(!v);
            if (vanishPatch != null) vanishPatch.SetActive(v);
        }
    }
}
