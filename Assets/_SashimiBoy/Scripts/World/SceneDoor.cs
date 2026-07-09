using UnityEngine;

namespace SashimiBoy
{
    public sealed class SceneDoor : MonoBehaviour, IInteractable
    {
        public string prompt = "들어가기";
        public string sceneName;
        public GameLocation destinationLocation = GameLocation.Unknown;

        public string Prompt => prompt;

        public void Interact(GameObject actor)
        {
            if (GameFlowManager.Instance != null && destinationLocation != GameLocation.Unknown)
            {
                GameFlowManager.Instance.SetLocation(destinationLocation);
            }

            if (SceneTransitionService.Instance == null)
            {
                Debug.LogWarning("SceneTransitionService is missing.");
                return;
            }

            SceneTransitionService.Instance.LoadScene(sceneName);
        }
    }
}
