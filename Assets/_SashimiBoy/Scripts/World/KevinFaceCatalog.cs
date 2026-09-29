using System;
using UnityEngine;

namespace SashimiBoy
{
    [Serializable]
    public sealed class KevinFaceChoice
    {
        public string id, displayName;
        public Mesh mesh;
        public Material material;
    }

    [CreateAssetMenu(menuName = "Sashimi Boy/Art/Kevin Face Catalog")]
    public sealed class KevinFaceCatalog : ScriptableObject
    {
        public KevinFaceChoice[] choices = Array.Empty<KevinFaceChoice>();
        public KevinFaceChoice Resolve(string id)
        {
            foreach (var choice in choices)
                if (choice.id == id && choice.mesh != null && choice.material != null) return choice;
            foreach (var choice in choices)
                if (choice.id == "CuteFace" && choice.mesh != null && choice.material != null) return choice;
            return null;
        }
    }
}
