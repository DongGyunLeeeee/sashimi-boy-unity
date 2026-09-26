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
