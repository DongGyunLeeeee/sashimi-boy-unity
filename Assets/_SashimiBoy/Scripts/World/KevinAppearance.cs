using UnityEngine;

namespace SashimiBoy
{
    // Every world/cooking/practice body shares this prefab. Only the connected head surface changes.
    public sealed class KevinAppearance : MonoBehaviour
    {
        public KevinFaceCatalog catalog;
        public SkinnedMeshRenderer faceRenderer;
        public string SelectedFaceId { get; private set; } = "CuteFace";

        private void Awake() => ApplyFace(SaveManager.Instance != null ? SaveManager.Instance.Current?.kevinFaceId : null);

        public void ApplyFace(string id)
        {
            var choice = catalog != null ? catalog.Resolve(id) : null;
            if (choice == null || faceRenderer == null) return;
            faceRenderer.sharedMesh = choice.mesh;
            faceRenderer.sharedMaterial = choice.material;
            faceRenderer.localBounds = choice.mesh.bounds;
            SelectedFaceId = choice.id;
        }
    }
}
