using UnityEngine;

namespace SashimiBoy
{
    public sealed class KevinVisualLoader : MonoBehaviour
    {
        public KevinVariantCatalog catalog;
        public string variantId = "AmbiguousFace";
        public Transform visualRoot;
        public Transform cameraTarget;
        public Renderer fallbackRenderer;
        public GameObject editorPreviewVisual;
        public bool loadOnAwake = true;

        private GameObject visualInstance;
        private static bool missingVisualWarningIssued;

        private void Awake()
        {
            if (loadOnAwake)
            {
                LoadSelectedVisual();
            }
        }

        public bool LoadSelectedVisual()
        {
            EnsureAnchors();
            RemoveCurrentVisual();
            if (editorPreviewVisual != null)
            {
                editorPreviewVisual.SetActive(false);
            }

            KevinVariantEntry entry = catalog != null
                ? catalog.Find(variantId) ?? catalog.ProvisionalDefault
                : null;
            if (entry == null || entry.prefab == null)
            {
                SetFallbackVisible(true);
                WarnMissingVisualOnce();
                return false;
            }

            visualInstance = Instantiate(entry.prefab, visualRoot, false);
            visualInstance.name = "KevinVisual_Instance";
            visualInstance.transform.localPosition = Vector3.zero;
            visualInstance.transform.localRotation = Quaternion.identity;
            visualInstance.transform.localScale = Vector3.one;

            bool hasRenderer =
                visualInstance.GetComponentInChildren<Renderer>(true) != null;
            SetFallbackVisible(!hasRenderer);
            if (!hasRenderer)
            {
                WarnMissingVisualOnce();
                RefreshFirstPersonPresentation();
                return false;
            }

            float normalizedHeight = entry.NormalizedHeight > 0.01f
                ? entry.NormalizedHeight
                : 1.75f;
            if (cameraTarget != null)
            {
                float parentScaleY = Mathf.Max(
                    0.0001f,
                    Mathf.Abs(transform.lossyScale.y));
                CharacterController controller =
                    GetComponent<CharacterController>();
                float colliderBottom = controller != null
                    ? controller.center.y - controller.height * 0.5f
                    : 0f;
                Vector3 localPosition = cameraTarget.localPosition;
                localPosition.y = colliderBottom + Mathf.Clamp(
                    normalizedHeight * 0.88f,
                    1.4f,
                    1.65f) / parentScaleY;
                cameraTarget.localPosition = localPosition;
            }

            RefreshFirstPersonPresentation();

            return true;
        }

        private void EnsureAnchors()
        {
            if (visualRoot == null)
            {
                Transform existing = transform.Find("VisualRoot");
                if (existing == null)
                {
                    var rootObject = new GameObject("VisualRoot");
                    existing = rootObject.transform;
                    existing.SetParent(transform, false);
                }

                visualRoot = existing;
            }

            if (cameraTarget == null)
            {
                Transform existing = transform.Find("CameraTarget");
                if (existing == null)
                {
                    var targetObject = new GameObject("CameraTarget");
                    existing = targetObject.transform;
                    existing.SetParent(transform, false);
                    existing.localPosition = new Vector3(0f, 1.55f, 0f);
                }

                cameraTarget = existing;
            }
        }

        private void RemoveCurrentVisual()
        {
            if (visualInstance != null)
            {
                Destroy(visualInstance);
                visualInstance = null;
            }

            if (visualRoot == null)
            {
                return;
            }

            Transform stale = visualRoot.Find("KevinVisual_Instance");
            if (stale != null)
            {
                Destroy(stale.gameObject);
            }
        }

        private void SetFallbackVisible(bool visible)
        {
            if (fallbackRenderer != null)
            {
                fallbackRenderer.enabled = visible;
            }
        }

        private static void WarnMissingVisualOnce()
        {
            if (missingVisualWarningIssued)
            {
                return;
            }

            missingVisualWarningIssued = true;
            Debug.LogWarning(
                "[Sashimi Boy] Kevin visual is unavailable; " +
                "the capsule fallback remains enabled.");
        }

        private void RefreshFirstPersonPresentation()
        {
            KevinFirstPersonCameraRig rig =
                GetComponent<KevinFirstPersonCameraRig>();
            if (rig != null)
            {
                rig.RefreshVisualPresentation();
            }
        }
    }
}
