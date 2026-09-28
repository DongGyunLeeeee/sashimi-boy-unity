using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    public static class DayWorldStreetAuthoring
    {
        public const string Art = DayWorldAssetAuthoring.Output + "/StreetDoors";
        private static readonly string[] Shops = { "FishShop", "EquipmentShop", "Club" };
        private static readonly string[] Rooms = { "Street", "FishShopDialogue", "EquipmentShop", "Club", "KevinHome" };

        [MenuItem("Sashimi Boy/Day 01 + Day 02/Apply Street Block And Matching Doors")]
        public static void ApplyBatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before authoring.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save unsaved scenes before authoring.");
            BuildDoorModels();
            foreach (string room in Rooms)
            {
                var scene = EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/" + room + ".unity", OpenSceneMode.Single);
                Apply(scene, scene.GetRootGameObjects().Single(g => g.name == "DayWorld_Integration").transform);
                foreach (var t in All(scene))
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0)
                        throw new InvalidOperationException("Missing script: " + room + "/" + t.name);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Failed to save " + room);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[StreetBlock] Applied connected ground, building parcels, boundaries and shared interior/exterior doors to existing scenes.");
        }

        public static void BuildDoorModels()
        {
            Directory.CreateDirectory(Art); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string id in Shops) BuildDoor(id);
            AssetDatabase.SaveAssets();
        }

        public static void InspectBatch()
        {
            foreach(string room in new[]{"FishShopDialogue","EquipmentShop","Club"})
            {
                var scene=EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/"+room+".unity",OpenSceneMode.Single);
                var door=All(scene).Single(t=>t.name=="Door_To_Street");
                foreach(var renderer in All(scene).Select(t=>t.GetComponent<Renderer>()).Where(r=>r!=null && (r.bounds.center-door.position).magnitude<3f))
                    Debug.Log("[DoorInspect] "+room+"/"+renderer.name+" active="+renderer.gameObject.activeInHierarchy+" enabled="+renderer.enabled+" bounds="+renderer.bounds+
                        " mats="+string.Join(",",renderer.sharedMaterials.Select(m=>m==null?"NULL":m.name+" "+(m.HasProperty("_Color")?m.color.ToString():""))));
            }
        }

        private static void BuildDoor(string id)
        {
            var original = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(DayWorldAssetAuthoring.Output + "/PF_" + id + ".prefab"));
            var root = new GameObject("PF_SharedDoor_" + id);
            try
            {
                original.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
                Bounds bounds = DayWorldAssetAuthoring.HomeGeometryBounds(original);
                original.transform.localScale *= Mathf.Min(1f, 6.4f / bounds.size.x);
                bounds = DayWorldAssetAuthoring.HomeGeometryBounds(original);
                float depth = id == "Club" ? 2.8f : 1.45f;
                var crop = new Bounds(new Vector3(0f, 1.325f, bounds.min.z + depth * .5f), new Vector3(1.5f, 2.35f, depth));
                int part = 0;
                foreach (var filter in original.GetComponentsInChildren<MeshFilter>())
                {
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (renderer == null || !renderer.bounds.Intersects(crop)) continue;
                    Mesh mesh = GeneratedMeshCrop.Crop(filter.sharedMesh, filter.transform.localToWorldMatrix, crop, id + "_DoorPart_" + part);
                    if (mesh.vertexCount == 0) { Object.DestroyImmediate(mesh); continue; }
                    // Retain the owner's door surfaces and UVs in a shallow portal fitting both openings.
                    var points = mesh.vertices;
                    for (int i = 0; i < points.Length; i++) points[i] = new Vector3(points[i].x * .94f,
                        (points[i].y - crop.min.y) * (2.05f / crop.size.y), (points[i].z - crop.min.z) * (.20f / depth));
                    mesh.vertices = points; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                    string path = Art + "/MS_" + mesh.name + ".asset";
                    var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (saved == null) { AssetDatabase.CreateAsset(mesh, path); saved = mesh; }
                    else { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); }
                    var child = new GameObject("OwnerDoorSurface_" + part, typeof(MeshFilter), typeof(MeshRenderer));
                    child.transform.SetParent(root.transform, false);
                    child.GetComponent<MeshFilter>().sharedMesh = saved;
                    child.GetComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                    part++;
                }
                if (part == 0) throw new InvalidOperationException("No supplied door geometry found for " + id);
                Color frame = id == "FishShop" ? new Color(.47f, .51f, .52f) : id == "EquipmentShop" ? new Color(.09f, .12f, .13f) : new Color(.23f, .24f, .23f);
                Box(root.transform, "InnerDoorBacking", new Vector3(0f, 1.025f, .22f), new Vector3(1.46f, 2.05f, .035f),
                    id == "Club" ? frame : new Color(.26f, .34f, .37f), false);
                foreach (float side in new[] { -1f, 1f })
                    Box(root.transform, "Jamb_" + (side < 0 ? "Left" : "Right"), new Vector3(side * .75f, 1.025f, .09f), new Vector3(.065f, 2.12f, .28f), frame, false);
                Box(root.transform, "Header", new Vector3(0f, 2.085f, .09f), new Vector3(1.57f, .07f, .28f), frame, false);
                Box(root.transform, "Sill", new Vector3(0f, .018f, .09f), new Vector3(1.57f, .035f, .30f), frame, false);
                if(id=="Club") foreach(float side in new[]{-1f,1f})
                {
                    Box(root.transform,"PullHandle_"+side,new Vector3(side*.13f,1.03f,-.045f),new Vector3(.035f,.30f,.065f),new Color(.66f,.65f,.59f),false);
                }
                PrefabUtility.SaveAsPrefabAsset(root, Art + "/PF_SharedDoor_" + id + ".prefab");
                Debug.Log("[SharedDoor] " + id + ": " + part + " cropped source surfaces with original materials.");
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(original); }
        }

        public static void Apply(Scene scene, Transform worldRoot)
        {
            if (!Rooms.Contains(scene.name)) return;
            if (scene.name == "Street")
            {
                Street(scene, worldRoot);
                foreach (string id in Shops)
                {
                    string room = id == "FishShop" ? "FishShopDialogue" : id;
                    AttachDoor(worldRoot, worldRoot.Find("Door_To_" + room + "_DayWorld"), id, false);
                }
            }
            else if (scene.name != "KevinHome")
            {
                AttachDoor(worldRoot, All(scene).Single(t => t.name == "Door_To_Street"), scene.name == "FishShopDialogue" ? "FishShop" : scene.name, true);
                var transom = worldRoot.Find("InteriorShell/DoorTransom");
                if (transom != null) transom.gameObject.SetActive(false);
                foreach(var legacy in All(scene).Where(t=>t.name=="DoorGlow" || t.name.StartsWith("DoorFrame_") || t.name=="club_door_frame"))
                    legacy.gameObject.SetActive(false);
            }
            // KevinHome already uses the supplied rusty_metal_door prefab on both sides.
        }

        private static void AttachDoor(Transform worldRoot, Transform retainedDoor, string id, bool interior)
        {
            var root = Child(worldRoot, "MatchingDoors");
            var door = root.Find("SharedDoor_" + id);
            if (door == null)
            {
                string path = Art + "/PF_SharedDoor_" + id + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) throw new InvalidOperationException("Generate shared doors first: " + path);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                instance.name = "SharedDoor_" + id; door = instance.transform;
            }
            retainedDoor.GetComponent<Renderer>().enabled = false;
            door.SetPositionAndRotation(new Vector3(retainedDoor.position.x, .05f, retainedDoor.position.z + (interior ? .075f : -.075f)),
                Quaternion.Euler(0f, interior ? 180f : 0f, 0f));
            PrefabUtility.RecordPrefabInstancePropertyModifications(door);
        }

        private static void Street(Scene scene, Transform worldRoot)
        {
            var root = Child(worldRoot, "StreetBlock");
            var floor = All(scene).Single(t => t.name == "Street_Ground");
            floor.position = new Vector3(0f, -.45f, 0f); floor.localScale = new Vector3(48f, 1f, 36f);
            floor.GetComponent<Collider>().enabled = true;
            if (root.GetComponent<StreetGroundSafety>() == null) root.gameObject.AddComponent<StreetGroundSafety>();
            foreach (string id in Shops)
            {
                string room = id == "FishShop" ? "FishShopDialogue" : id;
                var door = worldRoot.Find("Door_To_" + room + "_DayWorld");
                var facade=worldRoot.Find("PF_"+id);
                if(facade==null) throw new InvalidOperationException("Missing existing facade "+id);
                float front=facade.GetComponent<Collider>().bounds.min.z;
                Vector3 delta = Vector3.forward * (3.30f - front);
                facade.position+=delta; PrefabUtility.RecordPrefabInstancePropertyModifications(facade);
                float previousDoorZ=door.position.z;
                door.position=new Vector3(door.position.x,door.position.y,3.2f);
                foreach (string name in new[] { id + "ReadableSign", room })
                {
                    var t = worldRoot.Find(name); if (t != null) { t.position += Vector3.forward*(3.2f-previousDoorZ); PrefabUtility.RecordPrefabInstancePropertyModifications(t); }
                }
                var signBacking = worldRoot.Find("VenueAssets/" + id + "SignBacking");
                if (signBacking != null) signBacking.position = worldRoot.Find(id+"ReadableSign").position + Vector3.forward*.035f;
            }
            foreach (string name in new[] { "Road", "NorthCurb", "SouthCurb", "NorthSidewalk", "SouthPlaza" })
            {
                var t = All(scene).Single(item => item.name == name);
                Vector3 size = t.localScale; size.x = 40f; t.localScale = size;
            }
            Color stone = new Color(.30f, .34f, .34f), wall = new Color(.37f, .33f, .29f), metal = new Color(.13f, .18f, .18f);
            Boundary(root, "North", new Vector3(0f, .05f, 14.6f), new Vector3(43f, 1.4f, .32f), wall, metal);
            Boundary(root, "South", new Vector3(0f, .05f, -14.6f), new Vector3(43f, 1.4f, .32f), wall, metal);
            Boundary(root, "West", new Vector3(-21.5f, .05f, 0f), new Vector3(.32f, 1.4f, 29.5f), wall, metal);
            Boundary(root, "East", new Vector3(21.5f, .05f, 0f), new Vector3(.32f, 1.4f, 29.5f), wall, metal);
            Block(root, "NorthWest", new Vector3(-16f, .05f, 8.0f), new Vector3(7f, 5.8f, 8f), new Color(.52f, .48f, .40f), false);
            Block(root, "NorthEast", new Vector3(16.4f, .05f, 8.0f), new Vector3(7f, 7.6f, 8f), new Color(.37f, .45f, .46f), false);
            Block(root, "SouthCenter", new Vector3(0f, .05f, -9.5f), new Vector3(6.8f, 5.2f, 8f), new Color(.58f, .48f, .38f), true);
            Block(root, "SouthEast", new Vector3(8f, .05f, -9.5f), new Vector3(6.8f, 6.8f, 8f), new Color(.43f, .47f, .40f), true);
            Block(root, "SouthWest", new Vector3(-16f, .05f, -9.5f), new Vector3(7f, 4.6f, 8f), new Color(.52f, .40f, .34f), true);
            Block(root, "FarSouthEast", new Vector3(16.4f, .05f, -9.5f), new Vector3(7f, 4.8f, 8f), new Color(.47f, .46f, .45f), true);
            foreach (float x in new[] { -12f, 12f, 19.5f })
            {
                Box(root, "LampBase_" + x, new Vector3(x, .2f, -3.85f), new Vector3(.38f, .3f, .38f), stone);
                Box(root, "LampPost_" + x, new Vector3(x, 1.8f, -3.85f), new Vector3(.09f, 3.4f, .09f), metal);
                Box(root, "LampHead_" + x, new Vector3(x, 3.55f, -3.85f), new Vector3(.45f, .16f, .45f), new Color(.88f, .80f, .56f), false);
            }
            for (int i = 0; i < 8; i++)
            {
                float x = (i < 4 ? -1f : 1f) * (11f + i % 4 * 2f);
                Box(root, "RoadExtensionDash_" + i, new Vector3(x, .07f, -.8f), new Vector3(.85f, .014f, .07f), new Color(1f, .68f, .2f), false);
            }
        }

        private static void Block(Transform root, string name, Vector3 position, Vector3 size, Color color, bool northFacing)
        {
            var building = Child(root, "Neighbor_" + name); building.position = position;
            Box(building, "Building", Vector3.up * (size.y * .5f), size, color);
            Box(building, "Roof", Vector3.up * (size.y + .08f), new Vector3(size.x + .25f, .16f, size.z + .25f), color * .65f);
            float front = (northFacing ? 1f : -1f) * (size.z * .5f + .025f);
            Box(building, "BaseCourse", new Vector3(0f, .48f, front), new Vector3(size.x, .8f, .12f), color * .65f);
            for (int row = 0; row < (size.y > 6f ? 3 : 2); row++)
                for (int col = 0; col < 3; col++)
                {
                    Vector3 point = new Vector3((col - 1) * 1.9f, 1.65f + row * 1.9f, front);
                    Box(building, "WindowFrame_" + row + "_" + col, point, new Vector3(1.4f, 1.15f, .12f), color * .58f, false);
                    Box(building, "WindowGlass_" + row + "_" + col, point + Vector3.forward * (northFacing ? .07f : -.07f), new Vector3(1.21f, .96f, .025f), new Color(.12f, .22f, .27f), false);
                    Box(building, "WindowDivider_" + row + "_" + col, point + Vector3.forward * (northFacing ? .09f : -.09f), new Vector3(.05f, 1.0f, .05f), color * .75f, false);
                }
        }

        private static void Boundary(Transform root, string name, Vector3 bottom, Vector3 size, Color wall, Color metal)
        {
            Box(root, "Boundary_" + name, bottom + Vector3.up * (size.y * .5f), size, wall);
            Box(root, "BoundaryCap_" + name, bottom + Vector3.up * (size.y + .06f), new Vector3(size.x + .10f, .12f, size.z + .10f), metal);
        }

        private static Transform Child(Transform root, string name)
        {
            var result = root.Find(name); if (result != null) return result;
            result = new GameObject(name).transform; result.SetParent(root, false); return result;
        }

        private static GameObject Box(Transform root, string name, Vector3 center, Vector3 size, Color color, bool solid = true)
        {
            var previous = root.Find(name); GameObject go;
            if (previous == null) { go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(root, false); }
            else go = previous.gameObject;
            go.transform.localPosition = center; go.transform.localScale = size;
            go.GetComponent<Collider>().enabled = solid;
            string path = Art + "/MAT_Block_" + ColorUtility.ToHtmlStringRGB(color) + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Standard")); material.color = color; material.SetFloat("_Glossiness", .25f); AssetDatabase.CreateAsset(material, path); }
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        private static Transform[] All(Scene scene) => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
    }
}
