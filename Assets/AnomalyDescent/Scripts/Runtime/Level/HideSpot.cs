using UnityEngine;

namespace SomethingIsWrong
{
    public class HideSpot : MonoBehaviour, IInteractable
    {
        public string displayName = "Armario";
        public Room room;
        public Transform viewPoint;
        public Transform exitPoint;
        public int timesUsed;
        public bool occupied;

        void Awake()
        {
            // Se aceptan hijos con estos nombres para no tener que arrastrarlos en el Inspector.
            if (viewPoint == null) viewPoint = transform.Find("VistaEscondite");
            if (exitPoint == null) exitPoint = transform.Find("SalidaEscondite");
        }

        public string GetPrompt(PlayerController p)
        {
            return p.IsHidden ? "[E] Salir del escondite" : "[E] Esconderse: " + displayName;
        }

        public void Interact(PlayerController p)
        {
            if (p.IsHidden) p.ExitHide();
            else p.EnterHide(this);
        }
    }
}
