using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>
    /// Final del piso: al llegar el jugador al rellano inferior de la escalera, el piso se completa.
    /// En la escena: pon este componente en un objeto con un BoxCollider marcado como "Is Trigger" que cubra el rellano.
    /// (Compatibilidad: si no hay trigger, usa el rectángulo "area" y la altura "belowY".)
    /// Solo cuenta si la reja ya está abierta, para que no se pueda completar el piso saltándose el informe.
    /// </summary>
    public class StairDescent : MonoBehaviour
    {
        public Rect area;
        public float belowY = -1.2f;
        bool fired;
        Collider trigger;

        void Awake()
        {
            trigger = GetComponent<Collider>();
            if (trigger != null) trigger.isTrigger = true;
        }

        bool GateOpen
        {
            get
            {
                var d = GameDirector.Instance;
                return d != null && d.Level != null && (d.Level.stairGate == null || d.Level.stairGate.isOpen);
            }
        }

        void Fire()
        {
            if (fired || !GateOpen) return;
            var d = GameDirector.Instance;
            if (d == null || !d.IsPlaying) return;
            fired = true;
            d.OnStairReached();
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerController>() != null) Fire();
        }

        void OnTriggerStay(Collider other)
        {
            if (!fired && other.GetComponentInParent<PlayerController>() != null) Fire();
        }

        void Update()
        {
            if (fired || trigger != null || area.width <= 0f) return;
            var d = GameDirector.Instance;
            if (d == null || d.Player == null) return;
            Vector3 p = d.Player.transform.position;
            if (p.y < belowY && p.x >= area.xMin && p.x <= area.xMax && p.z >= area.yMin && p.z <= area.yMax) Fire();
        }
    }
}
