using UnityEngine;

namespace SashimiBoy
{
    public sealed class SceneLocationMarker : MonoBehaviour
    {
        public GameLocation location;

        private void Start()
        {
            if (GameFlowManager.Instance != null)
            {
                GameFlowManager.Instance.SetLocation(location);
            }
        }
    }
}
