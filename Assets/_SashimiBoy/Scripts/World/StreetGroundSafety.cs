using UnityEngine;

namespace SashimiBoy
{
    // The visible district boundary is the primary barrier. Recover only from an invalid world position.
    public sealed class StreetGroundSafety : MonoBehaviour
    {
        public Vector3 fallbackPosition = new Vector3(0f, .1f, -3.35f);
        public Vector2 maximumExtent = new Vector2(23f, 17f);
        public int RecoveryCount { get; private set; }
        private CharacterController player;
        private Vector3 lastGrounded;

        private void Start()
        {
            var actor = FindAnyObjectByType<SimpleTopDownPlayerController>();
            player = actor != null ? actor.GetComponent<CharacterController>() : null;
            lastGrounded = fallbackPosition + Vector3.up * (player != null ? Mathf.Max(0f, player.height * .5f - player.center.y) : 0f);
        }

        private void LateUpdate()
        {
            if (player == null || !player.enabled) return;
            Vector3 position = player.transform.position;
            if (!Finite(position) || position.y < -2f || Mathf.Abs(position.x) > maximumExtent.x || Mathf.Abs(position.z) > maximumExtent.y)
            {
                player.enabled = false;
                player.transform.position = lastGrounded;
                player.enabled = true;
                RecoveryCount++;
                return;
            }
            if (player.isGrounded && position.y > -.1f && position.y < 2f &&
                Mathf.Abs(position.x) < maximumExtent.x - 1f && Mathf.Abs(position.z) < maximumExtent.y - 1f)
                lastGrounded = position;
        }

        private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
