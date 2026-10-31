using UnityEngine;

namespace SashimiBoy
{
    // Every cooking/practice pose shares the same grounded two-arm solver.
    public static class Stage03CookingStance
    {
        public static Vector3 FitRootPosition(KevinBodyRig body, Vector3 preferredPosition) =>
            KevinGroundedReach.FitRootPosition(body, preferredPosition);
    }
}
