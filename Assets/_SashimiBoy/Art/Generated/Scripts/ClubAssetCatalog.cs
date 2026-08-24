using System;
using System.Collections.Generic;
using UnityEngine;

namespace SashimiBoy
{
    [Serializable]
    public sealed class ClubAssetCatalogEntry
    {
        public string assetId;
        public string displayName;
        public GameObject sourceModel;
        public GameObject generatedPrefab;
        public Vector3 defaultScale = Vector3.one;
        public Vector3 defaultRotation;
        public bool hasExternalTextures;
        public string materialStatus;
        [TextArea(2, 6)] public string notes;
    }

    [CreateAssetMenu(
        menuName = "Sashimi Boy/Art/Club Asset Catalog",
        fileName = "ClubAssetCatalog")]
    public sealed class ClubAssetCatalog : ScriptableObject
    {
        public List<ClubAssetCatalogEntry> assets = new List<ClubAssetCatalogEntry>();

        public ClubAssetCatalogEntry Find(string assetId)
        {
            for (int i = 0; i < assets.Count; i++)
            {
                ClubAssetCatalogEntry entry = assets[i];
                if (entry != null &&
                    string.Equals(entry.assetId, assetId, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }
    }
}
