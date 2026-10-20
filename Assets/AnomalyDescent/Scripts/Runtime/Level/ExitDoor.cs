using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>
    /// Puerta principal de la casa (va en la hoja, junto al componente Door). Está cerrada con llave toda la partida;
    /// se abre al cerrar la última vuelta y cruzarla ([E]) termina el juego.
    /// </summary>
    [RequireComponent(typeof(Door))]
    public class ExitDoor : MonoBehaviour
    {
        public string GetPrompt()
        {
            var d = GameDirector.Instance;
            if (d != null && d.Phase == GamePhase.Escape) return "[E] Salir de la casa";
            return "Puerta principal — cerrada. Se abrirá cuando el informe de la noche esté completo.";
        }

        public void Interact()
        {
            var d = GameDirector.Instance;
            if (d != null && d.Phase == GamePhase.Escape) { d.OnExitReached(); return; }
            SoundBank.PlayAt("click", transform.position, 0.6f, 8f);
        }
    }
}
