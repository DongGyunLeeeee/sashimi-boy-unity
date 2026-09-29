using System.Collections;
using UnityEngine;

namespace SashimiBoy
{
    /// <summary>Transient entry pose. A failed attempt never writes progression or a save.</summary>
    public static class Stage01ShopReturn
    {
        private static bool hasEntry, returning;
        private static Vector3 position;
        private static Quaternion rotation;
        private static float yaw, pitch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            hasEntry = returning = false;
        }

        public static void Capture(GameObject actor)
        {
            if (actor == null) return;
            position = actor.transform.position;
            rotation = actor.transform.rotation;
            var rig = actor.GetComponent<KevinFirstPersonCameraRig>();
            yaw = rig != null && rig.yawRoot != null ? rig.yawRoot.localEulerAngles.y : 0f;
            pitch = rig != null && rig.pitchRoot != null ? rig.pitchRoot.localEulerAngles.x : 0f;
            hasEntry = true;
            returning = false;
        }

        public static void Request()
        {
            returning = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SceneTransitionService.Instance.LoadScene(SashimiBoyConstants.Scenes.FishShopDialogue);
        }

        public static IEnumerator RestoreAt(StageStarterInteractable starter)
        {
            if (!returning || starter.stageId != SashimiBoyConstants.StageIds.Salmon) yield break;
            returning = false;
            // Let the new scene's player, UI and camera finish their ordinary Start methods.
            yield return null;
            var player = Object.FindAnyObjectByType<SimpleTopDownPlayerController>();
            if (player == null) yield break;
            var character = player.GetComponent<CharacterController>();
            if (character != null) character.enabled = false;
            player.transform.SetPositionAndRotation(
                hasEntry ? position : starter.transform.position + new Vector3(0f, .1f, -1.8f),
                hasEntry ? rotation : Quaternion.identity);
            if (character != null) character.enabled = true;
            var rig = player.GetComponent<KevinFirstPersonCameraRig>();
            if (rig != null) rig.RestoreStageEntryView(hasEntry ? yaw : 0f, hasEntry ? pitch : 40f);
            player.SetInputEnabled(true);
            var sensor = player.GetComponent<InteractionSensor>();
            if (sensor != null) sensor.SetInputEnabled(true);
        }
    }
}
