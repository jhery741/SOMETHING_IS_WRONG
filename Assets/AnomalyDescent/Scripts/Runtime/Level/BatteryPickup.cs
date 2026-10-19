using UnityEngine;

namespace SomethingIsWrong
{
    public class BatteryPickup : MonoBehaviour, IInteractable
    {
        public float charge = 50f;

        public string GetPrompt(PlayerController p) { return "[E] Recoger pilas (+" + charge.ToString("0") + "% linterna)"; }

        public void Interact(PlayerController p)
        {
            if (p.Flashlight != null) p.Flashlight.AddCharge(charge);
            SoundBank.Play2D("pickup", 0.5f);
            HUD.Show("Pilas recogidas. Linterna al " + p.Flashlight.battery.ToString("0") + "%.", 2.5f);
            Destroy(gameObject);
        }
    }
}
