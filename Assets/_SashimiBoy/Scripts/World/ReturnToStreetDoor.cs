using UnityEngine;

namespace SashimiBoy
{
    public sealed class ReturnToStreetDoor : MonoBehaviour, IInteractable
    {
        public string prompt = "거리로 나가기";
        public string sceneName = SashimiBoyConstants.Scenes.Street;

        public string Prompt => prompt;

        public void Interact(GameObject actor)
        {
            if (DayWorldFlow.Active)
            {
                if (!DayWorldFlow.InputSuppressed) DayWorldFlow.Instance.LoadWorld(sceneName, gameObject.scene.name);
                return;
            }
            if (SceneTransitionService.Instance != null)
            {
                SceneTransitionService.Instance.LoadScene(sceneName);
            }
        }
    }
}
