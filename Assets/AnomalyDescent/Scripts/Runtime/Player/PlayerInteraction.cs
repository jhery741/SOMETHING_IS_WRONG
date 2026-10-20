using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>
    /// [E] interactuar con lo que miras (puertas, escondites, la radio del refugio, la puerta principal).
    /// Los reportes ya no se hacen apuntando: se hacen con fotos desde el carrete (EvidenceCamera).
    /// </summary>
    public class PlayerInteraction : MonoBehaviour
    {
        const float InteractRange = 2.4f;

        PlayerController p;

        public string Prompt { get; private set; }
        public bool LookingAtInteractable { get; private set; }
        /// <summary>Compatibilidad: el panel de reporte ahora es el carrete de la cámara.</summary>
        public bool ReportOpen { get { return p != null && p.Evidence != null && p.Evidence.UIOpen; } }

        public void Setup(PlayerController pc)
        {
            p = pc;
            Prompt = "";
        }

        void Update()
        {
            Prompt = "";
            LookingAtInteractable = false;
            var d = GameDirector.Instance;
            if (d == null || !d.IsPlaying || p == null || p.IsDead || ReportOpen) return;

            if (p.IsHidden)
            {
                Prompt = "[E] Salir del escondite";
                if (GameInput.Down(GKey.E)) p.ExitHide();
                return;
            }

            var ray = new Ray(p.Cam.transform.position, p.Cam.transform.forward);
            RaycastHit hit;
            IInteractable inter = null;
            if (RaycastNotSelf(ray, out hit, InteractRange, QueryTriggerInteraction.Ignore))
                inter = hit.collider.GetComponentInParent<IInteractable>();
            if (inter != null)
            {
                Prompt = inter.GetPrompt(p);
                LookingAtInteractable = true;
                if (GameInput.Down(GKey.E)) inter.Interact(p);
            }

            if (GameInput.Down(GKey.R))
                HUD.Show("Para reportar: fotografía el cambio (clic derecho + clic izquierdo) y repórtalo desde el carrete [Tab].", 3.5f);
        }

        // Rayo de mirada que ignora el propio jugador: al mirar muy abajo (objetos junto a los pies) el rayo
        // chocaba con el CharacterController del jugador y no se podía interactuar ni reportar.
        static readonly RaycastHit[] hitBuffer = new RaycastHit[16];
        bool RaycastNotSelf(Ray ray, out RaycastHit best, float range, QueryTriggerInteraction q)
        {
            best = default(RaycastHit);
            int n = Physics.RaycastNonAlloc(ray, hitBuffer, range, Physics.DefaultRaycastLayers, q);
            float bestD = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = hitBuffer[i];
                if (h.collider == null || h.collider.transform.IsChildOf(p.transform)) continue;
                if (h.distance < bestD) { bestD = h.distance; best = h; found = true; }
            }
            return found;
        }

    }
}
