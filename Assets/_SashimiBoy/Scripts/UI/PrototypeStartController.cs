using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SashimiBoy
{
    public sealed class PrototypeStartController : MonoBehaviour
    {
        public Button startButton;
        public string streetSceneName = SashimiBoyConstants.Scenes.Street;

        private void Awake()
        {
            if (startButton != null)
            {
                startButton.onClick.AddListener(StartGame);
            }
        }

        private void OnDestroy()
        {
            if (startButton != null)
            {
                startButton.onClick.RemoveListener(StartGame);
            }
        }

        public void StartGame()
        {
            if (SceneTransitionService.Instance != null)
            {
                SceneTransitionService.Instance.LoadScene(streetSceneName);
                return;
            }

            SceneManager.LoadScene(streetSceneName, LoadSceneMode.Single);
        }
    }
}
