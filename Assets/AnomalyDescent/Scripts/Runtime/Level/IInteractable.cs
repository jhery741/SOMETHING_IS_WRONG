using UnityEngine;

namespace SomethingIsWrong
{
    public interface IInteractable
    {
        string GetPrompt(PlayerController player);
        void Interact(PlayerController player);
    }
}
