using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    // The supplied models replace visuals only. Existing scene objects own travel, waking, sleeping and saves.
    public static class DayWorldHomeAuthoring
    {
        public const float HalfWidth = 2.4f, HalfDepth = 2.3f, RoomHeight = 2.65f;
        const string Owned = "OwnerHomeAssets";
        const string Art = DayWorldAssetAuthoring.Output;

        [MenuItem("Sashimi Boy/Day 01 + Day 02/Apply Owner Home Assets Only")]
        public static void ApplyBatch()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            DayWorldAssetAuthoring.BuildHomeModels();
            foreach (string room in new[] { "Street", "KevinHome" })
            {
                var scene = EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/" + room + ".unity", OpenSceneMode.Single);
                Apply(scene, scene.GetRootGameObjects().Single(g => g.name == "DayWorld_Integration").transform);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[OwnerHome] Applied provided exterior, bed and doors to existing Street and KevinHome; retained all original objects and gameplay references.");
        }

        public static void Apply(Scene scene, Transform worldRoot)
        {
            if (scene.name != "Street" && scene.name != "KevinHome") return;
            var previous = worldRoot.Find(Owned);
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            var visuals = new GameObject(Owned).transform;
            visuals.SetParent(worldRoot, false);
            var trim = Material("MAT_HomeOwner_DoorSurround", new Color(.23f, .25f, .22f));
            if (scene.name == "Street")
            {
                worldRoot.Find("HomeFacade").gameObject.SetActive(false);
                worldRoot.Find("HomeRoof").gameObject.SetActive(false);
                var door = worldRoot.Find("Door_To_Home");
                var house = Instance(visuals, "HomeExterior", Vector3.zero);
                var entry = house.transform.Find("EntryAnchor");
                if (entry == null) throw new InvalidOperationException("The supplied house needs its measured entry anchor.");
                house.transform.position = new Vector3(door.position.x, .08f, door.position.z - .03f) - entry.localPosition;
                ApplyDoor(visuals, door, trim, .08f);
                var sign = worldRoot.Find("HomeSign");
                sign.position = new Vector3(door.position.x, 2.55f, door.position.z + .18f);
                Box(visuals, "HomeNameplate", sign.position + Vector3.back * .06f, new Vector3(2.05f, .48f, .055f), trim);
            }
            else
            {
                ApplyCompactLayout(scene, worldRoot);
                foreach (string name in new[] { "BedFrame", "Bed", "Pillow" })
                    worldRoot.Find(name).GetComponent<Renderer>().enabled = false;
                var bed = worldRoot.Find("Bed");
                Instance(visuals, "HomeBed", new Vector3(bed.position.x, .05f, bed.position.z));
                ApplyDoor(visuals, worldRoot.Find("Door_To_Street"), trim);
                // The real headboard is taller than the original placeholder. Lift its retained label and backing together.
                var label = worldRoot.Find("BedLabel");
                Vector3 delta = new Vector3(0f, 1.63f - label.position.y, 0f);
                var shell = worldRoot.Find("InteriorShell");
                if (shell != null)
                    foreach (var backing in shell.GetComponentsInChildren<Transform>())
                        if (backing.name.StartsWith("LabelPlaque", StringComparison.Ordinal) && Vector3.Distance(backing.position, label.position) < .1f)
                            backing.position += delta;
                label.position += delta;
            }
        }

        public static void ApplyCompactLayout(Scene scene, Transform root)
        {
            if (scene.name != "KevinHome") return;
            root.Find("HomeFloor").localScale = new Vector3(HalfWidth * 2f, .1f, HalfDepth * 2f);
            foreach (var sofa in root.Cast<Transform>().Where(t => t.name == "PF_Sofa" || t.name == "Sofa").ToArray())
                Object.DestroyImmediate(sofa.gameObject);
            root.Find("BedFrame").position = new Vector3(1.38f, .3f, .85f);
            root.Find("Bed").position = new Vector3(1.38f, .61f, .85f);
            root.Find("Pillow").position = new Vector3(1.38f, .78f, 1.60f);
            root.Find("BedLabel").position = new Vector3(1.38f, 1.63f, 2.08f);
            var ownedBed = root.Find(Owned + "/HomeBed");
            if (ownedBed != null) ownedBed.position = new Vector3(1.38f, .05f, .85f);
            var director = root.GetComponent<DayWorldSceneDirector>();
            var wake = root.Find("Wake");
            if (wake != null) wake.SetPositionAndRotation(new Vector3(.08f, .10f, .45f), Quaternion.Euler(34f, 90f, 0f));
            var player = root.GetComponentInChildren<SimpleTopDownPlayerController>();
            if (player != null) player.transform.position = new Vector3(.08f, .10f, .45f);
            var light = root.Find("HomeWarmLight").GetComponent<Light>();
            light.transform.position = new Vector3(0f, 2.38f, 0f); light.range = 5f; light.intensity = .50f;
            foreach (var station in root.GetComponentsInChildren<HomeEquipmentStation>(true))
            {
                bool drum = station.equipmentId == EquipmentId.SamplePackDrumKit;
                Vector3 oldCenter = drum ? new Vector3(-2.5f, 0f, 1.65f) : new Vector3(.1f, 0f, 2.1f);
                Vector3 center = drum ? new Vector3(-1.22f, 0f, 1.1f) : new Vector3(-1.42f, 0f, -1.68f);
                Quaternion rotation = Quaternion.Euler(0f, drum ? 0f : 180f, 0f);
                station.transform.SetPositionAndRotation(center - rotation * oldCenter, rotation);
                station.practiceCamera.position = drum ? new Vector3(.35f, 1.83f, 1.75f) : new Vector3(.15f, 1.85f, -1.9f);
                station.practiceCamera.LookAt(station.playerPosition.position + new Vector3(0f, 1.02f, 0f));
            }
        }

        static void ApplyDoor(Transform visuals, Transform retainedDoor, Material surround, float bottom = .05f)
        {
            retainedDoor.GetComponent<Renderer>().enabled = false;
            // Keep the original collider/SceneDoor and its destination. The new asset sits in its existing 1.25 x 2 m opening.
            Instance(visuals, "HomeDoor", new Vector3(retainedDoor.position.x, bottom, retainedDoor.position.z));
            Box(visuals, "DoorRecess", new Vector3(retainedDoor.position.x, bottom + 1f, retainedDoor.position.z - .09f), new Vector3(1.25f, 2f, .035f), surround);
        }

        static GameObject Instance(Transform parent, string id, Vector3 position)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/PF_" + id + ".prefab");
            if (prefab == null) throw new InvalidOperationException("Missing owner home prefab: " + id);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = id;
            instance.transform.position = position;
            return instance;
        }

        static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Material material)
        {
            var result = GameObject.CreatePrimitive(PrimitiveType.Cube);
            result.name = name;
            result.transform.SetParent(parent, false);
            result.transform.position = position;
            result.transform.localScale = size;
            result.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(result.GetComponent<Collider>());
            return result;
        }

        static Material Material(string name, Color color)
        {
            string path = Art + "/" + name + ".mat";
            var result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (result == null) { result = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(result, path); }
            result.color = color;
            result.SetFloat("_Glossiness", .12f);
            EditorUtility.SetDirty(result);
            return result;
        }
    }
}
