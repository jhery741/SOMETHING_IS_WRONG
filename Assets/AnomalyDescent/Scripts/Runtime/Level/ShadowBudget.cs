using System.Collections.Generic;
using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>
    /// Presupuesto de sombras para equipos de gama media / gráficos integrados.
    /// Las luces puntuales con sombra son caras en Built-in (cada una renderiza un cubemap).
    /// Este componente deja proyectar sombra solo a las N luces más cercanas a la cámara;
    /// las demás siguen iluminando, pero sin sombra. No toca intensidad ni color
    /// (eso lo sigue controlando RoomLight), así que no interfiere con parpadeos ni alertas.
    /// La linterna del jugador no se gestiona aquí: siempre conserva su sombra.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShadowBudget : MonoBehaviour
    {
        [Tooltip("Cuántas luces de habitación pueden proyectar sombra a la vez (además de la linterna).")]
        [Range(0, 8)] public int maxShadowedLights = 2;
        [Tooltip("Tipo de sombra para las luces seleccionadas. Hard es más barato que Soft.")]
        public LightShadows shadowType = LightShadows.Soft;
        [Tooltip("Segundos entre recálculos.")]
        public float interval = 0.25f;
        [Tooltip("Desactivado = no hace nada (todas las luces conservan su configuración original).")]
        public bool active = true;

        readonly List<Light> managed = new List<Light>();
        readonly List<Light> sorted = new List<Light>();
        float next;
        Transform viewer;

        void Start()
        {
            Collect();
        }

        /// <summary>Recoge las luces de habitación (RoomLight) y las lámparas del nivel. Llamar si se añaden luces.</summary>
        public void Collect()
        {
            managed.Clear();
            var level = LevelRoot.Instance;
            if (level == null) return;
            foreach (var l in level.GetComponentsInChildren<Light>(true))
            {
                if (l.type == LightType.Directional) continue;
                if (l.GetComponentInParent<PlayerController>() != null) continue;   // nunca la linterna
                if (l.shadows == LightShadows.None && l.GetComponent<RoomLight>() == null) continue; // lámparas sin sombra: se quedan así
                managed.Add(l);
            }
        }

        void Update()
        {
            if (!active || Time.unscaledTime < next) return;
            next = Time.unscaledTime + interval;
            if (viewer == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                viewer = cam.transform;
            }
            Vector3 p = viewer.position;
            sorted.Clear();
            foreach (var l in managed) if (l != null && l.isActiveAndEnabled) sorted.Add(l);
            sorted.Sort((a, b) => (a.transform.position - p).sqrMagnitude.CompareTo((b.transform.position - p).sqrMagnitude));
            for (int i = 0; i < sorted.Count; i++)
                sorted[i].shadows = i < maxShadowedLights ? shadowType : LightShadows.None;
        }
    }
}
