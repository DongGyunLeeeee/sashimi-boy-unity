using UnityEngine;

namespace SashimiBoy
{
    [RequireComponent(typeof(Camera))]
    public sealed class Stage01FailureEffect : MonoBehaviour
    {
        public Material blurMaterial;
        [Range(0f, 1f)] public float amount;
        private Material runtimeMaterial;

        private void OnDisable()
        {
            if (runtimeMaterial != null) Destroy(runtimeMaterial);
            runtimeMaterial = null;
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (blurMaterial == null || amount <= 0f)
            {
                Graphics.Blit(source, destination);
                return;
            }
            if (runtimeMaterial == null) runtimeMaterial = new Material(blurMaterial);
            runtimeMaterial.SetFloat("_Amount", amount);
            Graphics.Blit(source, destination, runtimeMaterial);
        }
    }
}
