using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>Reja en lo alto de la escalera. Se abre cuando el informe del piso está completo.</summary>
    public class StairGate : MonoBehaviour, IInteractable
    {
        public bool isOpen;

        public string GetPrompt(PlayerController p)
        {
            var d = GameDirector.Instance;
            int left = (d != null && d.Anomalies != null) ? d.Anomalies.Total - d.Anomalies.ReportedCount : 0;
            return "Reja cerrada — faltan " + left + " anomalías por reportar";
        }

        public void Interact(PlayerController p)
        {
            SoundBank.PlayAt("gate_locked", transform.position + Vector3.up, 0.7f, 8f, 1f);
            HUD.Show("La reja no cede. Se abrirá cuando el informe del piso esté completo.", 3f);
        }

        public void Open()
        {
            if (isOpen) return;
            isOpen = true;
            SoundBank.PlayAt("gate", transform.position + Vector3.up, 1f, 25f, 0.9f, "[reja metálica abriéndose]");
            gameObject.SetActive(false);
        }
    }
}
