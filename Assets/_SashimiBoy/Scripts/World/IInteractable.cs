using UnityEngine;

namespace SashimiBoy
{
    public interface IInteractable
    {
        string Prompt { get; }
        void Interact(GameObject actor);
    }

    // Optional eligibility for prompts and targeting, without changing ordinary travel doors.
    public interface IInteractionAvailability
    {
        bool IsAvailable { get; }
    }
}
