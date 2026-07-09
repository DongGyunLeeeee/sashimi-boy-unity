using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    public sealed class InteractionPromptUI : MonoBehaviour
    {
        public static InteractionPromptUI Instance { get; private set; }

        public GameObject root;
        public Text promptText;

        private void Awake()
        {
            Instance = this;
            Hide();
        }

        public void Show(string prompt)
        {
            if (promptText != null)
            {
                promptText.text = prompt;
            }

            if (root != null)
            {
                root.SetActive(true);
            }
            else
            {
                gameObject.SetActive(true);
            }
        }

        public void Hide()
        {
            if (root != null)
            {
                root.SetActive(false);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
    }
}
