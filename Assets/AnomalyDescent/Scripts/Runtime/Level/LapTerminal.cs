using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>
    /// Radio de campaña del refugio: es donde se CIERRA cada vuelta.
    /// · Observación: [E] empieza la investigación sin esperar al final del tiempo.
    /// · Investigación: dice cuántas anomalías faltan (no se puede cerrar con anomalías sin reportar).
    /// · Vuelta completa: [E] cierra la vuelta → la casa vuelve a la normalidad y empieza la siguiente.
    /// Póngase en un objeto con colisionador dentro del refugio (Room con isSafe).
    /// </summary>
    public class LapTerminal : MonoBehaviour, IInteractable
    {
        public string displayName = "Radio de campaña";

        public string GetPrompt(PlayerController p)
        {
            var d = GameDirector.Instance;
            if (d == null) return displayName;
            switch (d.Phase)
            {
                case GamePhase.Observation: return "[E] " + displayName + ": empezar la investigación ahora";
                case GamePhase.Investigation:
                    int left = d.Anomalies != null ? d.Anomalies.Total - d.Anomalies.ReportedCount : 0;
                    return displayName + " — faltan " + left + " anomalías por reportar en esta vuelta";
                case GamePhase.LapComplete: return "[E] " + displayName + ": cerrar la vuelta " + d.Lap;
                case GamePhase.Escape: return displayName + " — solo estática. Sal por la puerta principal.";
            }
            return displayName;
        }

        public void Interact(PlayerController p)
        {
            var d = GameDirector.Instance;
            if (d == null) return;
            switch (d.Phase)
            {
                case GamePhase.Observation: d.EndObservation(); break;
                case GamePhase.LapComplete: d.CloseLap(); break;
                default: SoundBank.PlayAt("static", transform.position + Vector3.up, 0.4f, 8f); break;
            }
        }
    }
}
