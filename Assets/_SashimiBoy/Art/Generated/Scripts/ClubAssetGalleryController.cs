using System;
using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    public sealed class ClubAssetGalleryController : MonoBehaviour
    {
        [Header("Scene References")]
        public Camera galleryCamera;
        public Text selectionText;

        [Header("Orbit")]
        [Min(1f)] public float distance = 9f;
        [Min(2f)] public float minimumDistance = 2.5f;
        [Min(3f)] public float maximumDistance = 30f;
        [Min(1f)] public float orbitSpeed = 120f;
        [Min(0.1f)] public float zoomSpeed = 4f;
        public float yaw = 35f;
        public float pitch = 24f;

        private ClubAssetGalleryMarker[] markers = Array.Empty<ClubAssetGalleryMarker>();
        private int selectedIndex;

        private void Start()
        {
            if (galleryCamera == null)
            {
                galleryCamera = Camera.main;
            }

            markers = FindObjectsByType<ClubAssetGalleryMarker>();
            Array.Sort(markers, CompareMarkers);
            selectedIndex = Mathf.Clamp(selectedIndex, 0, Mathf.Max(0, markers.Length - 1));
            RefreshSelection(true);
        }

        private void Update()
        {
            if (galleryCamera == null || markers.Length == 0)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.LeftBracket))
            {
                selectedIndex = (selectedIndex - 1 + markers.Length) % markers.Length;
                RefreshSelection(true);
            }

            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.RightBracket))
            {
                selectedIndex = (selectedIndex + 1) % markers.Length;
                RefreshSelection(true);
            }

            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * orbitSpeed * Time.unscaledDeltaTime;
                pitch -= Input.GetAxis("Mouse Y") * orbitSpeed * Time.unscaledDeltaTime;
                pitch = Mathf.Clamp(pitch, -10f, 80f);
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.001f)
            {
                distance = Mathf.Clamp(
                    distance - scroll * zoomSpeed,
                    minimumDistance,
                    maximumDistance);
            }

            RefreshSelection(false);
        }

        private void RefreshSelection(bool fitDistance)
        {
            if (galleryCamera == null || markers.Length == 0)
            {
                return;
            }

            ClubAssetGalleryMarker marker = markers[selectedIndex];
            if (marker == null)
            {
                return;
            }

            Bounds bounds = marker.GetWorldBounds();
            if (fitDistance)
            {
                float extent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
                distance = Mathf.Clamp(Mathf.Max(3f, extent * 3.2f), minimumDistance, maximumDistance);
            }

            Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 target = bounds.center;
            galleryCamera.transform.position =
                target - orbitRotation * Vector3.forward * distance;
            galleryCamera.transform.rotation =
                Quaternion.LookRotation(target - galleryCamera.transform.position, Vector3.up);

            if (selectionText != null)
            {
                Vector3 scale = marker.defaultScale;
                selectionText.text =
                    marker.displayName + "\n" +
                    "Scale  " +
                    scale.x.ToString("0.###") + ", " +
                    scale.y.ToString("0.###") + ", " +
                    scale.z.ToString("0.###");
            }
        }

        private static int CompareMarkers(
            ClubAssetGalleryMarker left,
            ClubAssetGalleryMarker right)
        {
            string leftId = left != null ? left.assetId : string.Empty;
            string rightId = right != null ? right.assetId : string.Empty;
            return string.Compare(leftId, rightId, StringComparison.Ordinal);
        }
    }
}
