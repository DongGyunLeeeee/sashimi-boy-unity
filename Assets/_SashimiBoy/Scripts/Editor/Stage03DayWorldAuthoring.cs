using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    // Focused Day3 additions. The Day1/2 world, original doors and Cheolsu's seated transform remain authoritative.
    public static class Stage03DayWorldAuthoring
    {
        public const string GeneratedRoot = "Assets/_SashimiBoy/Art/Generated/Stage03DayWorld";
        public const string OwnedRoot = "Stage03Story";
        private const string Scenes = "Assets/_SashimiBoy/Scenes/";
        private const string NpcPrefabs = "Assets/_SashimiBoy/Art/Generated/Stage03NpcAssets/Prefabs/";

        [MenuItem("Sashimi Boy/Stage 03/Apply Day3 Characters And Plugin Station")]
        public static void ApplyBatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before Day3 authoring.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save unsaved scenes before Day3 authoring.");
            EnsureFolder(GeneratedRoot);
            foreach (string room in new[] { "Street", "KevinHome" })
            {
                Scene scene = EditorSceneManager.OpenScene(Scenes + room + ".unity", OpenSceneMode.Single);
                Apply(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException("Could not save Day3 additions in " + room);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Stage03DayWorld] Added supplied Junseok/Jaehoon and Plugin station; retained Day1/2 content and seated Cheolsu.");
        }

        public static void Apply(Scene scene)
        {
            var world = scene.GetRootGameObjects().Single(g => g.name == "DayWorld_Integration").transform;
            var director = world.GetComponent<DayWorldSceneDirector>();
            if (director == null) throw new InvalidOperationException("Existing DayWorld director is required.");
            if (scene.name == "Street")
            {
                var owned = Child(world, OwnedRoot);
                var runner = world.Find("DayWorldDialogue").GetComponent<DialogueRunner>();
                if (runner == null) throw new InvalidOperationException("Existing DayWorld dialogue runner is required.");
                Npc(owned, "Junseok_Day3", "Junseok", "junseok", "준석",
                    new Vector3(-4.3f, .05f, -.8f), 180f, runner);
                Npc(owned, "Jaehoon_Day3", "Jaehoon", "jaehoon", "재훈",
                    new Vector3(-4.1f, .05f, -2.5f), 90f, runner);
                director.npcs = world.GetComponentsInChildren<DayWorldNpc>(true);
                EditorUtility.SetDirty(director);
            }
            else if (scene.name == "KevinHome")
            {
                PluginStation(world);
                director.equipmentStations = world.GetComponentsInChildren<HomeEquipmentStation>(true);
                EditorUtility.SetDirty(director);
            }
        }

        private static void Npc(Transform parent, string name, string sourceName, string id, string displayName,
            Vector3 position, float yaw, DialogueRunner runner)
        {
            Transform root = Child(parent, name);
            root.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            root.localScale = Vector3.one;
            Transform visual = root.Find("ProvidedModel");
            if (visual == null)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(NpcPrefabs + "PF_" + sourceName + ".prefab");
                if (source == null) throw new InvalidOperationException("Supplied NPC prefab missing: " + sourceName);
                visual = ((GameObject)PrefabUtility.InstantiatePrefab(source, root)).transform;
                visual.name = "ProvidedModel";
            }
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one;
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
            var npc = root.GetComponent<DayWorldNpc>() ?? root.gameObject.AddComponent<DayWorldNpc>();
            npc.npcId = id; npc.displayName = displayName; npc.day = 3; npc.runner = runner;
            npc.faceAnchor = Child(root, "FaceAnchor");
            npc.faceAnchor.localPosition = new Vector3(0f, 1.56f, .10f);
            npc.faceAnchor.localRotation = Quaternion.identity;
            var collision = root.GetComponent<BoxCollider>() ?? root.gameObject.AddComponent<BoxCollider>();
            collision.center = new Vector3(0f, .875f, 0f);
            collision.size = new Vector3(.65f, 1.75f, .55f);
            collision.isTrigger = false;
            Label(root, "NpcName", displayName, new Vector3(0f, 1.98f, 0f), 180f, .022f);
            EditorUtility.SetDirty(npc);
        }

        private static void PluginStation(Transform world)
        {
            Transform root = Child(world, "HomeStation_Plugin");
            root.SetPositionAndRotation(DayWorldHomeAuthoring.EquipmentPositions[2], Quaternion.identity);
            root.localScale = Vector3.one;
            var station = root.GetComponent<HomeEquipmentStation>() ?? root.gameObject.AddComponent<HomeEquipmentStation>();
            station.equipmentId = EquipmentId.Plugin;
            var collision = root.GetComponent<BoxCollider>() ?? root.gameObject.AddComponent<BoxCollider>();
            collision.center = new Vector3(0f, .65f, 0f);
            collision.size = new Vector3(1.5f, 1.3f, 1.5f);
            collision.isTrigger = true;
            station.playerPosition = Anchor(root, "PracticePosition", new Vector3(0f, .10f, -.63f));
            station.rightHandTarget = Anchor(root, "RightHand", new Vector3(.24f, .96f, -.18f));
            station.leftHandTarget = Anchor(root, "LeftHand", new Vector3(-.24f, .96f, -.18f));
            station.practiceCamera = Anchor(root, "PracticeCamera", new Vector3(1.55f, 1.83f, .65f));
            station.practiceCamera.LookAt(station.playerPosition.position + new Vector3(0f, 1.02f, 0f));
            station.practiceSeconds = 4.5f;
            Transform visual = Child(root, "PluginWorkstation");
            station.equipmentVisual = visual.gameObject;
            Color wood = new Color(.34f, .25f, .16f), dark = new Color(.035f, .065f, .08f);
            Box(visual, "Desk", new Vector3(0f, .82f, 0f), new Vector3(1.45f, .12f, .75f), wood);
            for (int i = 0; i < 4; i++)
                Box(visual, "DeskLeg" + i, new Vector3(i % 2 == 0 ? -.62f : .62f, .42f, i < 2 ? -.26f : .26f),
                    new Vector3(.08f, .8f, .08f), dark);
            Box(visual, "MonitorFrame", new Vector3(0f, 1.30f, .17f), new Vector3(.92f, .55f, .09f), dark);
            Box(visual, "PluginDisplay", new Vector3(0f, 1.30f, .11f), new Vector3(.85f, .47f, .012f), new Color(.055f, .14f, .17f));
            for (int i = 0; i < 9; i++)
            {
                float height = .06f + .18f * Mathf.Sin((i + 1f) * Mathf.PI / 10f);
                Box(visual, "EqualizerBand" + i, new Vector3(-.32f + i * .08f, 1.17f + height * .5f, .095f),
                    new Vector3(.045f, height, .014f), new Color(.28f, .7f, .64f));
            }
            Box(visual, "Keyboard", new Vector3(0f, .92f, -.15f), new Vector3(.70f, .04f, .22f), new Color(.18f, .20f, .22f));
            Label(root, "StationName", "플러그인 · E", new Vector3(0f, 1.75f, .45f), 0f, .022f);
            visual.gameObject.SetActive(false);
            EditorUtility.SetDirty(station);
        }

        private static Transform Anchor(Transform parent, string name, Vector3 position)
        {
            Transform result = Child(parent, name);
            result.localPosition = position;
            result.localRotation = Quaternion.identity;
            result.localScale = Vector3.one;
            return result;
        }

        private static Transform Child(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null) return existing;
            var result = new GameObject(name).transform;
            result.SetParent(parent, false);
            return result;
        }

        private static void Label(Transform parent, string name, string value, Vector3 position, float yaw, float size)
        {
            Transform root = Anchor(parent, name, position);
            root.localRotation = Quaternion.Euler(0f, yaw, 0f);
            TextMesh text = root.GetComponent<TextMesh>() ?? root.gameObject.AddComponent<TextMesh>();
            text.text = value;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 64; text.characterSize = size;
            text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center; text.color = Color.white;
            root.GetComponent<Renderer>().sharedMaterial = text.font.material;
        }

        private static void Box(Transform parent, string name, Vector3 position, Vector3 size, Color color)
        {
            Transform existing = parent.Find(name);
            GameObject box;
            if (existing == null)
            {
                box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = name; box.transform.SetParent(parent, false);
                Object.DestroyImmediate(box.GetComponent<Collider>());
            }
            else box = existing.gameObject;
            box.transform.localPosition = position; box.transform.localRotation = Quaternion.identity;
            box.transform.localScale = size;
            string path = GeneratedRoot + "/MAT_Plugin_" + ColorUtility.ToHtmlStringRGB(color) + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Standard")) { color = color };
                material.SetFloat("_Glossiness", .2f);
                AssetDatabase.CreateAsset(material, path);
            }
            box.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
