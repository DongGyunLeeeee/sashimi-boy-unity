using UnityEngine;

namespace SashimiBoy
{
    public sealed class PlaceholderBob : MonoBehaviour
    {
        public float amplitude = 0.1f;
        public float speed = 1f;

        private Vector3 start;

        private void Awake()
        {
            start = transform.localPosition;
        }

        private void Update()
        {
            transform.localPosition = start + Vector3.up * (Mathf.Sin(Time.time * speed) * amplitude);
        }
    }
}
