using System;
using UnityEngine;

namespace SashimiBoy
{
    /// <summary>Offline-clipped meshes in the original fillet coordinate frame.</summary>
    [CreateAssetMenu(menuName = "Sashimi Boy/Stage 03/Fillet Cut Cache")]
    public sealed class Stage03FilletCutCache : ScriptableObject
    {
        public const int ApprovedCutsPerFillet = 6;
        public string modelRole;
        // Index zero references the supplied derived mesh. Index six is empty.
        public Mesh[] remainingMeshes = Array.Empty<Mesh>();
        public Material surfaceMaterial;
        public Material capMaterial;
        public float[] cutPositions = Array.Empty<float>();

        public bool IsValid
        {
            get
            {
                if (remainingMeshes == null || remainingMeshes.Length != ApprovedCutsPerFillet + 1 ||
                    cutPositions == null || cutPositions.Length != ApprovedCutsPerFillet ||
                    surfaceMaterial == null || capMaterial == null || remainingMeshes[ApprovedCutsPerFillet] != null)
                    return false;
                for (int i = 0; i < ApprovedCutsPerFillet; i++)
                    if (remainingMeshes[i] == null || remainingMeshes[i].vertexCount == 0 ||
                        !float.IsFinite(cutPositions[i]) || (i > 0 && cutPositions[i] <= cutPositions[i - 1]))
                        return false;
                return true;
            }
        }
    }
}
