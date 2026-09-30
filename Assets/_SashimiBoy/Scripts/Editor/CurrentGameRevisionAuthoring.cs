using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    // Focused migration of existing scenes. Does not rebuild the world, charts or source assets.
    public static class CurrentGameRevisionAuthoring
    {
        public const string Evidence = "Logs/OwnerFixes68";

        [MenuItem("Sashimi Boy/Day 01 + Day 02/Apply Current Game Corrections")]
        public static void ApplyBatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            foreach (string room in new[] { "KevinHome", "FishShopDialogue", "EquipmentShop", "Club", "Street" })
            {
                var scene = EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/" + room + ".unity", OpenSceneMode.Single);
                var root = scene.GetRootGameObjects().Single(g => g.name == "DayWorld_Integration").transform;
                DayWorldInteriorAuthoring.Apply(scene, root);
                DayWorldStreetAuthoring.Apply(scene, root);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + room);
            }
            Stage02RockfishAuthoring.ApplyOwnerModelsBatch();
            AssetDatabase.SaveAssets();
            InspectBatch();
            Debug.Log("[Issue68] Applied home capacity, clear screen, entrances and Rockfish assembly.");
        }

        public static void ApplyCheolsuSeatingBatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Evidence);
            var scene = EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/FishShopDialogue.unity", OpenSceneMode.Single);
            var root = scene.GetRootGameObjects().Single(g => g.name == "DayWorld_Integration").transform;
            DayWorldVenueAuthoring.SeatCheolsuAtDiningTable(scene, root.Find("VenueAssets"));
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save FishShopDialogue.");
            string before = File.ReadAllText(scene.path);
            DayWorldVenueAuthoring.SeatCheolsuAtDiningTable(scene, root.Find("VenueAssets"));
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene) || File.ReadAllText(scene.path) != before)
                throw new InvalidOperationException("Cheolsu seating reapply changed scene bytes.");
            var customer = root.GetComponentsInChildren<DayWorldNpc>(true).Single(n => n.npcId == "cheolsu");
            customer.gameObject.SetActive(true);
            Vector3 target = customer.transform.position + Vector3.up * .65f;
            CaptureView(target + new Vector3(-1.1f, .45f, -1.65f), target, "cheolsu-seated-side");
            CaptureView(target + new Vector3(1.7f, .65f, -1.8f), target, "cheolsu-seated-front");
            Debug.Log("[Issue68] Cheolsu seated at " + customer.transform.position + "; repeated placement preserves scene bytes.");
        }

        public static void InspectBatch()
        {
            Directory.CreateDirectory(Evidence);
            foreach (string room in new[] { "Street", "KevinHome", "FishShopDialogue", "EquipmentShop", "Club" })
            {
                var scene = EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/" + room + ".unity", OpenSceneMode.Single);
                var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
                if (room == "Street")
                    foreach (string id in new[] { "FishShopDialogue", "EquipmentShop", "Club" })
                    {
                        var door = all.Single(t => t.name == "Door_To_" + id + "_DayWorld");
                        CaptureView(door.position + new Vector3(.65f, .60f, -3f), door.position + Vector3.up * .15f, room + "-" + id);
                    }
                else
                {
                    var door = all.Single(t => t.name == "Door_To_Street");
                    CaptureView(door.position + new Vector3(.75f, .55f, 3.1f), door.position + Vector3.up * .05f, room + "-exit");
                    if (room == "KevinHome") CaptureHomeCapacity(all);
                }
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var fish = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Stage02RockfishAuthoring.Root + "/PF_Stage02_RockfishAssembly.prefab"));
            var light = new GameObject("InspectionLight").AddComponent<Light>();
            light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(48, -28, 0); light.intensity = 1f;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.4f, .4f, .4f);
            var bounds = DayWorldAssetAuthoring.HomeGeometryBounds(fish);
            CaptureView(bounds.center + new Vector3(.15f, .95f, -1.25f).normalized * bounds.size.magnitude * 1.55f, bounds.center, "rockfish-assembled");
            Object.DestroyImmediate(fish); Object.DestroyImmediate(light.gameObject);
        }

        static void CaptureHomeCapacity(Transform[] all)
        {
            // Evidence-only: these ten examples are never saved into the scene or unlocked for the player.
            foreach (var t in all.Where(t => t.name == "Ceiling" || t.name.StartsWith("TimberCeilingJoint") || t.name.StartsWith("CeilingFixture") || t.name.StartsWith("LampDiffuser")))
                t.gameObject.SetActive(false);
            CaptureView(new Vector3(0, 12, -.1f), Vector3.zero, "home-layout");
            foreach (var station in all.Select(t => t.GetComponent<HomeEquipmentStation>()).Where(c => c != null))
                station.gameObject.SetActive(false);
            string[] examples = { "ElectronicDrumKit", "MidiKeyboardController", "ModularSynthesizer", "EffectsPedals", "Loudspeaker", "SpeakerBox", "StageSpotlight", "StackedSpeaker", "StereoSpeaker", "VintageSpeaker" };
            var root = new GameObject("UnsavedCapacityPreview");
            for (int i = 0; i < examples.Length; i++)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(DayWorldAssetAuthoring.VenueOutput + "/PF_" + examples[i] + ".prefab");
                var instance = Object.Instantiate(asset, root.transform);
                instance.transform.SetPositionAndRotation(DayWorldHomeAuthoring.EquipmentPositions[i], Quaternion.Euler(0, DayWorldHomeAuthoring.EquipmentYaw[i], 0));
                Debug.Log("[Issue68Capacity] " + examples[i] + " " + DayWorldAssetAuthoring.HomeGeometryBounds(instance));
            }
            CaptureView(new Vector3(0, 12, -.1f), Vector3.zero, "home-ten-equipment-capacity-preview");
            Object.DestroyImmediate(root);
        }

        static void CaptureView(Vector3 position, Vector3 target, string name)
        {
            var camera = new GameObject("RevisionCapture").AddComponent<Camera>();
            var texture = new RenderTexture(1200, 800, 24);
            var pixels = new Texture2D(1200, 800, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
                camera.fieldOfView = 48f; camera.nearClipPlane = .01f; camera.farClipPlane = 1000f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.11f, .14f, .16f);
                camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0); pixels.Apply();
                File.WriteAllBytes(Evidence + "/" + name + ".png", pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = null;
                Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(texture); Object.DestroyImmediate(pixels);
            }
        }
    }
}
