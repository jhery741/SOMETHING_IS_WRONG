using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>Botella que se puede recoger para lanzarla luego como distracción.</summary>
    public class ThrowablePickup : MonoBehaviour, IInteractable
    {
        public string GetPrompt(PlayerController p)
        {
            if (p.throwables >= PlayerController.MaxThrowables) return "Botella (ya llevas " + PlayerController.MaxThrowables + ")";
            return "[E] Coger botella (" + p.throwables + "/" + PlayerController.MaxThrowables + ")";
        }

        public void Interact(PlayerController p)
        {
            if (p.throwables >= PlayerController.MaxThrowables)
            {
                HUD.Show("No puedes llevar más botellas.", 2f);
                return;
            }
            p.throwables++;
            SoundBank.Play2D("pickup", 0.4f);
            HUD.Show("Botella recogida. Lánzala con [G] o clic derecho para desviar a El Ciego.", 3.5f);
            Destroy(gameObject);
        }
    }
}
