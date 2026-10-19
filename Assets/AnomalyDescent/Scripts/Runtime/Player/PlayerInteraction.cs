using System.Collections;
using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>
    /// [E] interactuar con lo que miras. [R] abre el reporte del objeto que miras;
    /// eliges categoría con 1-5 y la radio lo transmite (tarda y hace ruido).
    /// </summary>
    public class PlayerInteraction : MonoBehaviour
    {
        const float InteractRange = 2.4f;
        const float ReportRange = 8f;

        PlayerController p;

        public string Prompt { get; private set; }
        public string ReportHint { get; private set; }
        public bool LookingAtInteractable { get; private set; }
        public bool ReportOpen { get; private set; }
        public Reportable ReportTarget { get; private set; }
        public bool Transmitting { get; private set; }
        public int LastClosedFrame { get; private set; }
        public NoteItem ReadingNote { get; private set; }
        Vector3 noteOrigin;

        public void Setup(PlayerController pc)
        {
            p = pc;
            Prompt = "";
            ReportHint = "";
            LastClosedFrame = -10;
        }

        void Update()
        {
            Prompt = "";
            ReportHint = "";
            LookingAtInteractable = false;
            var d = GameDirector.Instance;
            if (d == null || !d.IsPlaying || p == null || p.IsDead)
            {
                if (ReportOpen && (d == null || d.Phase == GamePhase.Victory || d.Phase == GamePhase.Defeat)) CloseReport();
                return;
            }

            if (ReportOpen) { HandleReportInput(); return; }

            if (ReadingNote != null)
            {
                Prompt = "[E] Cerrar";
                bool moved = (p.transform.position - noteOrigin).sqrMagnitude > 6.25f;
                if (GameInput.Down(GKey.E) || GameInput.Down(GKey.Escape) || GameInput.Down(GKey.R) || moved || p.IsHidden) CloseNote();
                return;
            }

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

            Reportable rep = null;
            // Collide: algunas anomalías (huellas, arañazos, presencias) solo tienen un colisionador "trigger".
            if (RaycastNotSelf(ray, out hit, ReportRange, QueryTriggerInteraction.Collide))
                rep = hit.collider.GetComponentInParent<Reportable>();
            if (rep != null && d.Phase != GamePhase.Observation)
                ReportHint = "[R] Reportar: " + rep.displayName;

            if (GameInput.Down(GKey.R))
            {
                if (d.Phase == GamePhase.Observation) HUD.Show("Aún es la fase de observación: memoriza la casa.", 2.5f);
                else if (Transmitting) HUD.Show("Espera: la radio sigue transmitiendo.", 2f);
                else if (rep == null)
                {
                    HUD.Show("Apunta a lo que creas que ha cambiado.", 2f);
                    if (Application.isEditor || Debug.isDebugBuild)
                    {
                        RaycastHit h2;
                        bool any = RaycastNotSelf(ray, out h2, ReportRange, QueryTriggerInteraction.Collide);
                        Debug.Log("[Reporte] Sin objetivo. Rayo → " + (any ? h2.collider.name + " (capa " + LayerMask.LayerToName(h2.collider.gameObject.layer) + ", " + h2.distance.ToString("0.00") + " m)" : "nada"));
                    }
                }
                else OpenReport(rep);
            }
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

        public void OpenNote(NoteItem n)
        {
            ReadingNote = n;
            noteOrigin = p.transform.position;
            SoundBank.Play2D("pickup", 0.25f, 0.7f);
        }

        void CloseNote()
        {
            ReadingNote = null;
            LastClosedFrame = Time.frameCount;
        }

        void OpenReport(Reportable rep)
        {
            ReportOpen = true;
            ReportTarget = rep;
            p.inputLocked = true;
        }

        void CloseReport()
        {
            ReportOpen = false;
            p.inputLocked = false;
            LastClosedFrame = Time.frameCount;
        }

        void HandleReportInput()
        {
            for (int i = 0; i < 5; i++)
            {
                if (GameInput.Down(GKey.Alpha1 + i))
                {
                    var target = ReportTarget;
                    CloseReport();
                    StartCoroutine(Transmit(target, (AnomalyCategory)i));
                    return;
                }
            }
            if (GameInput.Down(GKey.R) || GameInput.Down(GKey.Escape)) CloseReport();
        }

        IEnumerator Transmit(Reportable target, AnomalyCategory cat)
        {
            Transmitting = true;
            HUD.Show("Transmitiendo por radio: " + target.displayName + " · " + AnomalyText.Name(cat) + "...", 2.2f);
            SoundBank.PlayAt("static", p.transform.position + Vector3.up * 1.3f, 0.5f, 12f);
            NoiseSystem.Emit(p.transform.position, 9f, true);
            yield return new WaitForSeconds(2f);
            Transmitting = false;
            var d = GameDirector.Instance;
            if (d == null || d.Phase == GamePhase.Victory || d.Phase == GamePhase.Defeat || p.IsDead) yield break;
            d.SubmitReport(target, cat);
        }
    }
}
