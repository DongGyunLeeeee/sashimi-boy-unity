using UnityEngine;

namespace SashimiBoy
{
    public sealed class InteractionSensor : MonoBehaviour
    {
        public float radius = 1.5f;
        public KeyCode interactKey = KeyCode.E;
        public LayerMask interactableLayers = ~0;

        private IInteractable current;
        private Collider currentCollider;

        private void Update()
        {
            FindNearest();
            UpdatePrompt();

            if (current != null && Input.GetKeyDown(interactKey))
            {
                current.Interact(gameObject);
            }
        }

        private void FindNearest()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, radius, interactableLayers, QueryTriggerInteraction.Collide);
            float bestDistance = float.MaxValue;
            IInteractable best = null;
            Collider bestCollider = null;

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                IInteractable interactable = FindInteractable(hit);
                if (interactable == null)
                {
                    continue;
                }

                float distance = Vector3.SqrMagnitude(hit.transform.position - transform.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = interactable;
                    bestCollider = hit;
                }
            }

            current = best;
            currentCollider = bestCollider;
        }


        private static IInteractable FindInteractable(Collider hit)
        {
            MonoBehaviour[] behaviours = hit.GetComponentsInParent<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IInteractable interactable)
                {
                    return interactable;
                }
            }

            return null;
        }

        private void UpdatePrompt()
        {
            if (InteractionPromptUI.Instance == null)
            {
                return;
            }

            if (current == null)
            {
                InteractionPromptUI.Instance.Hide();
                return;
            }

            InteractionPromptUI.Instance.Show($"E  {current.Prompt}");
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = currentCollider != null ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
