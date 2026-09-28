using UnityEngine;

namespace SashimiBoy
{
    public sealed class ClubPerformanceGate : MonoBehaviour, IInteractable, IInteractionAvailability
    {
        public string prompt = "무대 확인";
        public ClubController clubController;

        public string Prompt => prompt;
        public bool IsAvailable => !DayWorldFlow.Active;

        public void Interact(GameObject actor)
        {
            if (!IsAvailable) return;
            if (clubController == null)
            {
                clubController = Object.FindAnyObjectByType<ClubController>();
            }

            if (clubController != null)
            {
                clubController.StartPerformancePreview();
            }
        }
    }
}
