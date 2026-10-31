using UnityEngine;

namespace SashimiBoy
{
    /// <summary>
    /// Fits Kevin's grounded root to both actual arm chains. Call after setting
    /// working targets and ApplyPose, so the shoulder offsets include the lean.
    /// Palm targets, bone lengths, scale and the root's authored height stay intact.
    /// </summary>
    public static class Stage03CookingStance
    {
        // KevinBodyRig.Solve has a 1 mm reach clamp. Stay another 2 mm inside
        // that anatomical range rather than increasing it or moving a hand bone.
        private const float ReachMargin = .003f;
        private const float Tolerance = .00001f;

        private struct ReachRegion
        {
            public Vector2 center;
            public float minimum, maximum;
            public bool Contains(Vector2 value)
            {
                float distance = Vector2.Distance(value, center);
                return distance >= minimum - Tolerance && distance <= maximum + Tolerance;
            }
        }

        public static Vector3 FitRootPosition(KevinBodyRig body, Vector3 preferredPosition)
        {
            if (body == null ||
                !TryRegion(body, body.right, body.rightTarget, body.rightRotation, preferredPosition.y, out var right) ||
                !TryRegion(body, body.left, body.leftTarget, body.leftRotation, preferredPosition.y, out var left))
                return preferredPosition;

            Vector2 preferred = new Vector2(preferredPosition.x, preferredPosition.z);
            if (right.Contains(preferred) && left.Contains(preferred)) return preferredPosition;

            Vector2 best = preferred;
            float bestDistance = float.PositiveInfinity;
            ConsiderProjection(preferred, right.center, right.maximum, right, left, ref best, ref bestDistance);
            ConsiderProjection(preferred, left.center, left.maximum, right, left, ref best, ref bestDistance);
            if (right.minimum > 0f)
                ConsiderProjection(preferred, right.center, right.minimum, right, left, ref best, ref bestDistance);
            if (left.minimum > 0f)
                ConsiderProjection(preferred, left.center, left.minimum, right, left, ref best, ref bestDistance);

            ConsiderIntersections(preferred, right.maximum, left.maximum, right, left, ref best, ref bestDistance);
            if (right.minimum > 0f)
                ConsiderIntersections(preferred, right.minimum, left.maximum, right, left, ref best, ref bestDistance);
            if (left.minimum > 0f)
                ConsiderIntersections(preferred, right.maximum, left.minimum, right, left, ref best, ref bestDistance);
            if (right.minimum > 0f && left.minimum > 0f)
                ConsiderIntersections(preferred, right.minimum, left.minimum, right, left, ref best, ref bestDistance);

            // Impossible target pairs cannot be repaired by stretching Kevin.
            // Retain the preferred stance in that case; the real palm error is
            // still observable and the actual-scene reach regression must fail.
            return float.IsPositiveInfinity(bestDistance) ? preferredPosition :
                new Vector3(best.x, preferredPosition.y, best.y);
        }

        private static bool TryRegion(KevinBodyRig body, KevinBodyRig.Arm arm,
            Vector3 target, Quaternion rotation, float rootY, out ReachRegion region)
        {
            region = default;
            if (arm == null || arm.upper == null || arm.lower == null || arm.hand == null || arm.palm == null)
                return false;

            Vector3 wrist = target - rotation * Vector3.Scale(arm.palm.localPosition, arm.hand.lossyScale);
            Vector3 shoulderOffset = arm.upper.position - body.transform.position;
            float upper = Vector3.Distance(arm.upper.position, arm.lower.position);
            float lower = Vector3.Distance(arm.lower.position, arm.hand.position);
            float maximumReach = upper + lower - ReachMargin;
            float minimumReach = Mathf.Abs(upper - lower) + ReachMargin;
            float vertical = wrist.y - (shoulderOffset.y + rootY);
            float maximumSquared = maximumReach * maximumReach - vertical * vertical;
            if (maximumReach <= 0f || maximumSquared < 0f) return false;

            region.center = new Vector2(wrist.x - shoulderOffset.x, wrist.z - shoulderOffset.z);
            region.maximum = Mathf.Sqrt(maximumSquared);
            region.minimum = Mathf.Sqrt(Mathf.Max(0f, minimumReach * minimumReach - vertical * vertical));
            return region.minimum <= region.maximum;
        }

        private static void ConsiderProjection(Vector2 preferred, Vector2 center, float radius,
            ReachRegion right, ReachRegion left, ref Vector2 best, ref float bestDistance)
        {
            Vector2 delta = preferred - center;
            Vector2 direction = delta.sqrMagnitude > Tolerance * Tolerance ? delta.normalized : Vector2.right;
            Consider(center + direction * radius, preferred, right, left, ref best, ref bestDistance);
        }

        private static void ConsiderIntersections(Vector2 preferred, float rightRadius, float leftRadius,
            ReachRegion right, ReachRegion left, ref Vector2 best, ref float bestDistance)
        {
            Vector2 delta = left.center - right.center;
            float distance = delta.magnitude;
            if (distance <= Tolerance || distance > rightRadius + leftRadius + Tolerance ||
                distance < Mathf.Abs(rightRadius - leftRadius) - Tolerance) return;
            Vector2 direction = delta / distance;
            float along = (rightRadius * rightRadius - leftRadius * leftRadius + distance * distance) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(0f, rightRadius * rightRadius - along * along));
            Vector2 midpoint = right.center + direction * along;
            Vector2 perpendicular = new Vector2(-direction.y, direction.x);
            Consider(midpoint + perpendicular * height, preferred, right, left, ref best, ref bestDistance);
            Consider(midpoint - perpendicular * height, preferred, right, left, ref best, ref bestDistance);
        }

        private static void Consider(Vector2 candidate, Vector2 preferred, ReachRegion right, ReachRegion left,
            ref Vector2 best, ref float bestDistance)
        {
            if (!right.Contains(candidate) || !left.Contains(candidate)) return;
            float distance = (candidate - preferred).sqrMagnitude;
            if (distance >= bestDistance) return;
            best = candidate;
            bestDistance = distance;
        }
    }
}
