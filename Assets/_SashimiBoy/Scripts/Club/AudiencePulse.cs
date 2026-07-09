using UnityEngine;

namespace SashimiBoy
{
    public sealed class AudiencePulse : MonoBehaviour
    {
        public float idleEnergy = 0.25f;
        public float pulseScale = 0.2f;
        public float pulseSpeed = 2f;

        private Vector3 startScale;
        private float energy;

        private void Awake()
        {
            startScale = transform.localScale;
            energy = idleEnergy;
        }

        private void Update()
        {
            float amount = Mathf.Sin(Time.time * pulseSpeed) * pulseScale * energy;
            transform.localScale = startScale * (1f + amount);
        }

        public void SetEnergy(float value)
        {
            energy = Mathf.Clamp01(value);
        }
    }
}
