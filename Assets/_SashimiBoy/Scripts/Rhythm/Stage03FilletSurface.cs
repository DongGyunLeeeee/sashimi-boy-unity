using System;
using UnityEngine;

namespace SashimiBoy
{
    /// <summary>
    /// Replaces immutable precomputed meshes only. The stage controller owns
    /// note resolution, slice instantiation, plate placement and completion.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Stage03FilletSurface : MonoBehaviour
    {
        public Stage03FilletCutCache cutCache;
        public MeshFilter meshFilter;
        public MeshRenderer meshRenderer;
        public int CutCount { get; private set; }
        public int CutsPerFillet => Stage03FilletCutCache.ApprovedCutsPerFillet;
        public bool IsEmpty => CutCount == CutsPerFillet;
        public bool CuttingEnabled { get; private set; } = true;
        private Material[] cutMaterials;

        private void Awake()
        {
            if (cutCache != null) Bind(cutCache);
        }

        public void Bind(Stage03FilletCutCache cache)
        {
            if (cache == null || !cache.IsValid)
                throw new ArgumentException("A complete Stage03 fillet cut cache is required.", nameof(cache));
            if (meshFilter == null) meshFilter = GetComponentInChildren<MeshFilter>(true);
            if (meshRenderer == null) meshRenderer = GetComponentInChildren<MeshRenderer>(true);
            if (meshFilter == null || meshRenderer == null || meshFilter.gameObject != meshRenderer.gameObject)
                throw new InvalidOperationException("Bind the same fillet MeshFilter and MeshRenderer.");
            cutCache = cache;
            cutMaterials = new[] { cache.surfaceMaterial, cache.capMaterial };
            ResetCuts();
        }

        public void SetCuttingEnabled(bool enabled) => CuttingEnabled = enabled;

        public void ResetCuts()
        {
            CutCount = 0;
            CuttingEnabled = true;
            ApplyMesh();
        }

        // The caller supplies the per-fillet ordinal (0..5). Repeating the same
        // resolved note cannot remove a second piece. No object is spawned here.
        public bool TryCut(int expectedCutCount)
        {
            if (cutCache == null || !CuttingEnabled || !isActiveAndEnabled ||
                Time.timeScale <= 0f || IsEmpty || expectedCutCount != CutCount)
                return false;
            CutCount++;
            ApplyMesh();
            return true;
        }

        public Vector3 NextCutPosition
        {
            get
            {
                if (cutCache == null || meshFilter == null) return transform.position;
                float z = cutCache.cutPositions[Mathf.Clamp(CutCount, 0, CutsPerFillet - 1)];
                Bounds bounds = cutCache.remainingMeshes[0].bounds;
                return meshFilter.transform.TransformPoint(new Vector3(bounds.center.x, bounds.max.y, z));
            }
        }

        private void ApplyMesh()
        {
            if (cutCache == null || meshFilter == null || meshRenderer == null) return;
            meshFilter.sharedMesh = cutCache.remainingMeshes[CutCount];
            meshRenderer.enabled = !IsEmpty;
            if (!IsEmpty)
                meshRenderer.sharedMaterials = CutCount == 0
                    ? new[] { cutCache.surfaceMaterial }
                    : cutMaterials ?? (cutMaterials = new[] { cutCache.surfaceMaterial, cutCache.capMaterial });
        }
    }
}
