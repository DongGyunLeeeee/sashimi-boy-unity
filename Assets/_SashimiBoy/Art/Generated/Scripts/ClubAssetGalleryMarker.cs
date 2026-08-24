using UnityEngine;

namespace SashimiBoy
{
    public sealed class ClubAssetGalleryMarker : MonoBehaviour
    {
        public string assetId;
        public string displayName;
        public Vector3 defaultScale = Vector3.one;

        public Bounds GetWorldBounds()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            bool found = false;
            Bounds bounds = new Bounds(transform.position, Vector3.zero);

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return bounds;
        }
    }
}
