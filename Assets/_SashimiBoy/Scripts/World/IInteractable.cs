using UnityEngine;

namespace SashimiBoy
{
    public interface IInteractable
    {
        string Prompt { get; }
        void Interact(GameObject actor);
    }
}
