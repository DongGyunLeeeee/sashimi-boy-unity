using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    public sealed class ToastUI : MonoBehaviour
    {
        public static ToastUI Instance { get; private set; }

        public GameObject root;
        public Text messageText;
        public float defaultDuration = 2f;

        private Coroutine routine;

        private void Awake()
        {
            Instance = this;
            SetVisible(false);
        }

        public void Show(string message)
        {
            Show(message, defaultDuration);
        }

        public void Show(string message, float duration)
        {
            if (messageText != null)
            {
                messageText.text = message;
            }

            if (routine != null)
            {
                StopCoroutine(routine);
            }

            SetVisible(true);
            routine = StartCoroutine(Routine(duration));
        }

        private IEnumerator Routine(float duration)
        {
            yield return new WaitForSecondsRealtime(duration);
            SetVisible(false);
            routine = null;
        }

        private void SetVisible(bool visible)
        {
            if (root != null)
            {
                root.SetActive(visible);
            }
            else
            {
                gameObject.SetActive(visible);
            }
        }
    }
}
