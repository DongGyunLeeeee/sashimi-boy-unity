using System;
using System.Collections.Generic;
using UnityEngine;

namespace SashimiBoy
{
    public enum SalmonAssemblyPieceRole
    {
        Head,
        Body,
        Fins,
        Spine,
        Fillet,
        PinBone,
    }

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

    [DisallowMultipleComponent]
    public sealed class SalmonAssemblyView : MonoBehaviour
    {
        public const string HeadId = "Head";
        public const string BodyId = "Body";
        public const string FinsId = "Fins";
        public const string SpineId = "Spine";
        public const string FilletId = "Fillet";

        [Header("Stable Pieces")]
        public SalmonAssemblyPieceView head;
        public SalmonAssemblyPieceView body;
        public SalmonAssemblyPieceView fins;
        public SalmonAssemblyPieceView spine;
        public SalmonAssemblyPieceView fillet;
        public SalmonAssemblyPieceView[] pinBones =
            Array.Empty<SalmonAssemblyPieceView>();

        [Header("Part Anchors")]
        public Transform headAnchor;
        public Transform bodyAnchor;
        public Transform finsAnchor;
        public Transform spineAnchor;
        public Transform filletAnchor;
        public Transform[] pinBoneAnchors = Array.Empty<Transform>();

        [Header("Tool And Output Anchors")]
        public Transform knifeAttachmentAnchor;
        public Transform handAttachmentAnchor;
        public Transform pinBoneWorkAnchor;
        public Transform sashimiOutputAnchor;
        public Transform plateOutputAnchor;

        [Header("Fallback")]
        public GameObject proceduralSalmonFallbackPrefab;

        public IEnumerable<SalmonAssemblyPieceView> Pieces
        {
            get
            {
                yield return head;
                yield return body;
                yield return fins;
                yield return spine;
                yield return fillet;
                for (int i = 0; i < pinBones.Length; i++)
                {
                    yield return pinBones[i];
                }
            }
        }

        public SalmonAssemblyPieceView FindPiece(string stableId)
        {
            if (string.IsNullOrEmpty(stableId))
            {
                return null;
            }

            foreach (SalmonAssemblyPieceView piece in Pieces)
            {
                if (piece != null &&
                    string.Equals(
                        piece.StableId,
                        stableId,
                        StringComparison.Ordinal))
                {
                    return piece;
                }
            }

            return null;
        }

        public void SetPieceVisible(string stableId, bool visible)
        {
            SalmonAssemblyPieceView piece = RequirePiece(stableId);
            piece.SetVisible(visible);
        }

        public void DetachPiece(string stableId, Transform newParent = null)
        {
            SalmonAssemblyPieceView piece = RequirePiece(stableId);
            piece.Detach(newParent);
        }

        public void ResetAssembly()
        {
            foreach (SalmonAssemblyPieceView piece in Pieces)
            {
                if (piece != null)
                {
                    piece.ResetToAuthoredState();
                }
            }
        }

        private SalmonAssemblyPieceView RequirePiece(string stableId)
        {
            SalmonAssemblyPieceView piece = FindPiece(stableId);
            if (piece == null)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(stableId),
                    stableId,
                    "Unknown salmon assembly piece ID.");
            }

            return piece;
        }
    }
}
