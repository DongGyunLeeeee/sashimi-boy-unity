using UnityEngine;

namespace SashimiBoy
{
    public sealed class ClubPerformanceGate : MonoBehaviour, IInteractable
    {
        public string prompt = "무대 확인";
        public ClubController clubController;

        public string Prompt => prompt;

        public void Interact(GameObject actor)
        {
            if (clubController == null)
            {
                clubController = FindObjectOfType<ClubController>();
            }

            if (clubController != null)
            {
                clubController.StartPerformancePreview();
            }
        }
    }
}
