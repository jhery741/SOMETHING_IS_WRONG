using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>
    /// Área lógica de una habitación (no es geometría). Se usa para saber dónde está el jugador o el monstruo,
    /// para las rutas entre puertas y para repartir anomalías.
    /// En la escena: añade un BoxCollider (Is Trigger) que cubra el suelo de la habitación y asígnalo a "Volume";
    /// el área se calcula sola. Sin volumen se usa el rectángulo "Area" (x, z, ancho, fondo) escrito a mano.
    /// </summary>
    public class Room : MonoBehaviour
    {
        public string id;
        public string displayName;
        [Tooltip("Trigger que delimita la habitación. Si se asigna, sobrescribe 'Area'.")]
        public BoxCollider volume;
        [Tooltip("x/y del Rect = x/z del mundo (solo si no hay Volume).")]
        public Rect area;
        [Tooltip("Refugio: el monstruo no entra ni atrapa dentro mientras GameDirector.RefugeProtects sea cierto.")]
        public bool isSafe;
        public float height = 2.7f;
        [Tooltip("Cuántas anomalías del catálogo de esta habitación entran en cada partida.")]
        public int anomalyPicks = 2;
        [Tooltip("Luz principal de la habitación (parpadeos, alerta roja).")]
        public RoomLight roomLight;
        [Tooltip("Raíz del techo (la anomalía 'techo más bajo' la desplaza con todo lo que cuelga de ella).")]
        public Transform ceilingRoot;

        public Vector3 Center { get { return new Vector3(area.center.x, transform.position.y, area.center.y); } }

        void Awake()
        {
            SyncFromVolume();
            // Capa "Ignore Raycast": el volumen no debe tapar los rayos de interacción/reporte del jugador.
            if (volume != null) volume.gameObject.layer = 2;
        }
        void OnValidate() { SyncFromVolume(); }

        public void SyncFromVolume()
        {
            if (string.IsNullOrEmpty(id)) id = gameObject.name.ToLowerInvariant();
            if (string.IsNullOrEmpty(displayName)) displayName = gameObject.name;
            if (volume == null) volume = GetComponent<BoxCollider>();
            if (volume == null) return;
            volume.isTrigger = true;
            Bounds b = volume.bounds;
            if (b.size.sqrMagnitude < 1e-6f) return;   // colisionador desactivado: se conserva el área anterior
            area = new Rect(b.min.x, b.min.z, b.size.x, b.size.z);
            height = b.size.y;
        }

        public bool Contains(Vector3 p, float margin = 0f)
        {
            return p.x >= area.xMin - margin && p.x <= area.xMax + margin &&
                   p.z >= area.yMin - margin && p.z <= area.yMax + margin;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = isSafe ? new Color(0.3f, 1f, 0.4f, 0.5f) : new Color(0.3f, 0.6f, 1f, 0.35f);
            Gizmos.DrawWireCube(new Vector3(area.center.x, transform.position.y + 0.02f, area.center.y), new Vector3(area.width, 0.04f, area.height));
        }
    }
}
