using System;
using UnityEngine;

namespace SashimiBoy
{
    [DisallowMultipleComponent]
    public sealed class SalmonAssemblyPieceView : MonoBehaviour
    {
        [SerializeField] private string stableId = string.Empty;
        [SerializeField] private SalmonAssemblyPieceRole role;
        [SerializeField] private GameObject visualRoot;
        [SerializeField] private bool initiallyVisible = true;
        [SerializeField] private Transform authoredParent;
        [SerializeField] private Vector3 authoredLocalPosition;
        [SerializeField] private Quaternion authoredLocalRotation =
            Quaternion.identity;
        [SerializeField] private Vector3 authoredLocalScale = Vector3.one;

        public string StableId => stableId;
        public SalmonAssemblyPieceRole Role => role;
        public bool InitiallyVisible => initiallyVisible;
        public bool IsVisible
        {
            get
            {
                if (visualRoot == null)
                {
                    return false;
                }

                Renderer[] renderers =
                    visualRoot.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i].enabled)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
        public bool IsAttached => transform.parent == authoredParent;

        private void Awake()
        {
            EnsureAuthoredPose();
        }

        public void Configure(
            string id,
            SalmonAssemblyPieceRole pieceRole,
            GameObject visuals,
            bool visibleAtStart)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "A salmon assembly piece requires a stable ID.",
                    nameof(id));
            }

            stableId = id;
            role = pieceRole;
            visualRoot = visuals != null ? visuals : gameObject;
            initiallyVisible = visibleAtStart;
            CaptureAuthoredPose();
            SetVisible(visibleAtStart);
        }

        public void CaptureAuthoredPose()
        {
            authoredParent = transform.parent;
            authoredLocalPosition = transform.localPosition;
            authoredLocalRotation = transform.localRotation;
            authoredLocalScale = transform.localScale;
        }

        public void SetVisible(bool visible)
        {
            if (visualRoot == null)
            {
                return;
            }

            Renderer[] renderers =
                visualRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = visible;
            }
        }

        public void Detach(Transform newParent = null)
        {
            EnsureAuthoredPose();
            transform.SetParent(newParent, true);
        }

        public void ResetToAuthoredState()
        {
            EnsureAuthoredPose();
            transform.SetParent(authoredParent, false);
            transform.localPosition = authoredLocalPosition;
            transform.localRotation = authoredLocalRotation;
            transform.localScale = authoredLocalScale;
            SetVisible(initiallyVisible);
        }

        private void EnsureAuthoredPose()
        {
            if (authoredParent != null)
            {
                return;
            }

            CaptureAuthoredPose();
        }
    }
}
