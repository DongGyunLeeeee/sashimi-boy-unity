using UnityEngine;

namespace SashimiBoy
{
    public sealed class SimplePanel : MonoBehaviour
    {
        [SerializeField] private GameObject root;

        public bool IsVisible => Target.activeSelf;
        private GameObject Target => root != null ? root : gameObject;

        private void Reset()
        {
            root = gameObject;
        }

        public void Show()
        {
            Target.SetActive(true);
        }

        public void Hide()
        {
            Target.SetActive(false);
        }

        public void SetVisible(bool visible)
        {
            Target.SetActive(visible);
        }
    }
}
