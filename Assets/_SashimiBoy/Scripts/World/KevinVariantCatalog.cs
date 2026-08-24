using System;
using System.Collections.Generic;
using UnityEngine;

namespace SashimiBoy
{
    [Serializable]
    public sealed class KevinVariantEntry
    {
        public string variantId;
        public string displayName;
        public GameObject prefab;
        public Avatar avatar;
        public float heightMeters;
        public float defaultScale = 1f;
        public bool isHumanoid;
        public bool hasAnimationClips;
        public AnimationClip idleClip;
        public AnimationClip walkClip;
        [TextArea] public string notes;

        public float NormalizedHeight => heightMeters * defaultScale;
    }

    [CreateAssetMenu(
        fileName = "KevinVariantCatalog",
        menuName = "Sashimi Boy/Art/Kevin Variant Catalog")]
    public sealed class KevinVariantCatalog : ScriptableObject
    {
        public string provisionalDefaultVariantId = "AmbiguousFace";
        public List<KevinVariantEntry> variants =
            new List<KevinVariantEntry>();

        public KevinVariantEntry ProvisionalDefault
        {
            get
            {
                KevinVariantEntry selected =
                    Find(provisionalDefaultVariantId);
                return selected ?? (variants.Count > 0 ? variants[0] : null);
            }
        }

        public KevinVariantEntry Find(string variantId)
        {
            if (string.IsNullOrWhiteSpace(variantId))
            {
                return null;
            }

            for (int i = 0; i < variants.Count; i++)
            {
                KevinVariantEntry entry = variants[i];
                if (entry != null && string.Equals(
                        entry.variantId,
                        variantId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }
    }
}
