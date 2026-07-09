using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    public sealed class DialogueUI : MonoBehaviour
    {
        public GameObject root;
        public Text speakerText;
        public Text bodyText;
        public Button nextButton;

        private GameObject Target => root != null ? root : gameObject;

        private void Awake()
        {
            Hide();
        }

        public void ShowLine(DialogueLine line)
        {
            if (line == null)
            {
                Hide();
                return;
            }

            if (speakerText != null)
            {
                speakerText.text = line.speaker;
            }

            if (bodyText != null)
            {
                bodyText.text = line.text;
            }

            Target.SetActive(true);
        }

        public void Hide()
        {
            Target.SetActive(false);
        }
    }
}
