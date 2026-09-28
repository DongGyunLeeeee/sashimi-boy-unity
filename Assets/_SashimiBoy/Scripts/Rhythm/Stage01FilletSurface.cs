using UnityEngine;

namespace SashimiBoy
{
    /// <summary>Pre-authored cross sections of the supplied half. The flesh top stays intact.</summary>
    public sealed class Stage01FilletSurface : MonoBehaviour
    {
        public Renderer[] sourceRenderers;
        public GameObject[] cutCaps;
        public float[] cutPositions;
        public float workHeight;
        private int shownCuts;

        private void OnEnable() => ShowCut(shownCuts);

        public Vector3 NextCutPosition(int sliceCount)
        {
            int index = Mathf.Clamp(sliceCount, 0, cutPositions.Length - 1);
            return transform.TransformPoint(new Vector3(cutPositions[index], workHeight, 0f));
        }

        public void ShowCut(int count)
        {
            shownCuts = Mathf.Clamp(count, 0, cutPositions != null ? cutPositions.Length : 0);
            if (cutPositions == null || cutPositions.Length == 0) return;
            Vector3 normal = transform.right;
            float cut = shownCuts > 0 ? cutPositions[shownCuts - 1] : 1000f;
            Vector3 point = transform.TransformPoint(new Vector3(cut, 0f, 0f));
            var block = new MaterialPropertyBlock();
            block.SetVector("_CutPlane", new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, point)));
            if (sourceRenderers != null)
                foreach (var target in sourceRenderers) if (target != null) target.SetPropertyBlock(block);
            if (cutCaps != null)
                for (int i = 0; i < cutCaps.Length; i++)
                    if (cutCaps[i] != null) cutCaps[i].SetActive(shownCuts > 0 && i == shownCuts - 1);
        }
    }
}
