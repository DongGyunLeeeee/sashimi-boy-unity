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
    // Additive art correction for the current #42 scenes. Never regenerates a scene or replaces gameplay objects.
    public static partial class DayWorldVenueAuthoring
    {
        const string Art = DayWorldAssetAuthoring.VenueOutput;
        static readonly string[] Rooms = { "EquipmentShop", "FishShopDialogue", "Club", "Street" };

        [MenuItem("Sashimi Boy/Day 01 + Day 02/Apply Existing Venue Assets Only")]
        public static void ApplyBatch()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            DayWorldAssetAuthoring.BuildVenueModels();
            DayWorldStreetAuthoring.BuildDoorModels();
            foreach (string room in Rooms)
            {
                var scene = EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/" + room + ".unity", OpenSceneMode.Single);
                Apply(scene, scene.GetRootGameObjects().Single(g => g.name == "DayWorld_Integration").transform);
                DayWorldStreetAuthoring.Apply(scene, scene.GetRootGameObjects().Single(g => g.name == "DayWorld_Integration").transform);
                foreach (var t in All(scene))
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0)
                        throw new InvalidOperationException("Missing script: " + room + "/" + t.name);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[DayWorldVenue] Applied owner venue assets to three existing interiors and the street floor; gameplay objects retained.");
        }

        public static void Apply(Scene scene, Transform worldRoot)
        {
            if (!Rooms.Contains(scene.name)) return;
            var old = worldRoot.Find("VenueAssets");
            var root = old != null ? old : new GameObject("VenueAssets").transform; root.SetParent(worldRoot, false);
            if (scene.name == "EquipmentShop") Shop(scene, root);
            if (scene.name == "FishShopDialogue") Fish(scene, root);
            if (scene.name == "Club") Club(scene, root);
            if (scene.name == "Street") Street(scene, root);
        }

        static void Shop(Scene scene, Transform root)
        {
            foreach (var t in All(scene).Where(t => t.name.StartsWith("DisplayBay_") || t.name.StartsWith("WallGear_") || t.name.StartsWith("CaseStack_") || t.name == "Shopkeeper_Visual" || t.name == "Shopkeeper"))
                Hide(t.gameObject);
            var prompt = Named(scene, "ShopPrompt").GetComponent<TextMesh>();
            prompt.text = "장비 구매"; prompt.characterSize = .012f;
            prompt.transform.position = new Vector3(0f, .94f, 2.025f);
            // Keep the service target and its purchase controller. Its visible proxy box is no longer shop furniture.
            var service = Named(scene, "ShopService");
            foreach (var r in service.GetComponentsInChildren<Renderer>()) r.enabled = false;
            var counterTop = Named(scene, "CounterTop").GetComponent<Renderer>();
            counterTop.sharedMaterial = Material("CounterWalnut", new Color(.26f, .15f, .085f), .22f);

            Place(root, "EquipmentShopOwner", new Vector3(0f, .03f, 3.15f), 180f, true);
            Place(root, "MidiKeyboardController", new Vector3(-1.58f, 1.266f, 2.06f), 180f);
            Place(root, "EffectsPedals", new Vector3(1.15f, 1.266f, 2.07f), 180f);
            Place(root, "GuitarPedal", new Vector3(2.18f, 1.266f, 2.07f), 180f);
            Place(root, "ElectronicDrumKit", new Vector3(-3.55f, .09f, .65f), 165f, true);
            Box(root, "DrumDisplayRug", new Vector3(-3.55f, .055f, .65f), new Vector3(2f, .05f, 1.6f), new Color(.22f, .10f, .065f), false);
            Place(root, "ModularSynthesizer", new Vector3(-3.55f, .40f, 3.25f), 180f, true);
            Box(root, "SynthDisplayPlinth", new Vector3(-3.55f, .20f, 3.25f), new Vector3(1.1f, .4f, .7f), new Color(.12f, .10f, .085f));
            Place(root, "StackedSpeaker", new Vector3(-5.45f, .04f, 3.30f), 0f, true);
            Place(root, "Loudspeaker", new Vector3(5.45f, .04f, 3.30f), 0f, true);
            Place(root, "SpeakerBox", new Vector3(3.45f, .04f, 3.25f), 180f, true);
            Place(root, "StereoSpeaker", new Vector3(4.45f, .04f, 3.25f), 180f, true);
            Place(root, "VintageSpeaker", new Vector3(-5.50f, .04f, 1.45f), 135f, true);
            Place(root, "StageSpotlight", new Vector3(-4.25f, 1.38f, 3.3f), 160f);
            Box(root, "LightingShelf", new Vector3(-4.25f, 1.34f, 3.3f), new Vector3(.40f, .08f, .46f), new Color(.20f, .14f, .09f));
            Place(root, "WoodenSofa", new Vector3(5.45f, .04f, .55f), 270f, true);
            var light = Named(scene, "CounterWarmLight")?.GetComponent<Light>();
            if (light != null) { light.intensity = .85f; light.color = new Color(1f, .87f, .70f); }
            FillShopAndBindOwner(scene, root);
        }

        static void Fish(Scene scene, Transform root)
        {
            ApplyFishPlacement(scene);
            // A hygienic continuous top connects the owner's two cabinets and supports the fish display.
            Box(root, "FishDisplayWorktop", new Vector3(0f, 1.15f, 1.72f), new Vector3(5.35f, .08f, 1.12f), new Color(.57f, .64f, .64f));
            foreach (string fish in new[] { "Salmon", "Rockfish", "Mullet" })
            {
                float x = fish == "Salmon" ? -1.55f : fish == "Mullet" ? 1.55f : 0f;
                Box(root, "FishTray_" + fish, new Vector3(x, 1.205f, 1.52f), new Vector3(1.35f, .03f, .78f), new Color(.82f, .85f, .82f), false);
            }
            DressStageWorkbench(scene, root);
            ArrangeDiningAndKitchen(scene, root);
        }

        public static void ApplyFishPlacement(Scene scene)
        {
            // The older FishShop generator cleared the source's FBX up-axis conversion.
            // Restore it on the retained prefab instances and fit by actual vertices, without changing their GUIDs.
            foreach (string name in new[] { "SashimiTable_Left_Validated", "SashimiTable_Right_Validated", "DisplayInside_Validated" })
            {
                var instance = Named(scene, name);
                // Canonicalize signed zero in the retained root quaternion as the legacy generator does.
                instance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
                var model = instance.transform.Find("Model");
                model.localPosition = Vector3.zero; model.localRotation = Quaternion.Euler(-90f, 0f, 0f); model.localScale = Vector3.one;
                Bounds bounds = Geometry(instance);
                model.localScale *= (name.StartsWith("SashimiTable") ? 1.03f : 1.70f) / bounds.size.y;
                bounds = Geometry(instance);
                model.position += instance.transform.position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                PrefabUtility.RecordPrefabInstancePropertyModifications(model);
            }
            Named(scene, "FishDisplay_SurfaceAnchor").transform.position = new Vector3(0f, 1.225f, 1.52f);
            foreach (string fish in new[] { "Salmon", "Rockfish", "Mullet" })
            {
                var instance = Named(scene, "DisplayFish_" + fish);
                float x = fish == "Salmon" ? -1.55f : fish == "Mullet" ? 1.55f : 0f;
                instance.transform.position = new Vector3(x, 1.225f, 1.52f);
                // The canonical wrapper is already side-lying; a further X quarter-turn stands it on its fins.
                instance.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                instance.transform.position += Vector3.up * (1.225f - Geometry(instance).min.y);
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            }
            var display = Named(scene, "DisplayInside_Validated");
            display.transform.position = new Vector3(4.45f, .08f, 2.85f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(display.transform);
            var key = Named(scene, "PrepCounter_KeyLight")?.GetComponent<Light>();
            if (key != null)
            {
                key.shadows = LightShadows.Soft; key.shadowCustomResolution = 512;
                key.shadowBias = .02f; key.shadowNormalBias = .06f;
            }
        }

        static void DressStageWorkbench(Scene scene, Transform root)
        {
            // Dress the existing StageStarter target; do not move, replace or invoke it.
            Box(root, "StagePrepTop", new Vector3(-2.1f, 1.00f, -1.8f), new Vector3(1.6f, .10f, 1.25f), new Color(.51f, .56f, .55f), false);
            Box(root, "StageCuttingBoard", new Vector3(-2.1f, 1.065f, -1.8f), new Vector3(1.18f, .03f, .78f), new Color(.48f, .31f, .15f), false);
            int leg = 0;
            foreach (float x in new[] { -2.76f, -1.44f })
                foreach (float z in new[] { -2.28f, -1.32f })
                    Box(root, "PrepLeg_" + leg++, new Vector3(x, .48f, z), new Vector3(.07f, .96f, .07f), new Color(.34f, .39f, .38f));
            var knife = Spawn(root, "Assets/_SashimiBoy/Art/Generated/Prefabs/FishShop/PF_Prop_KitchenKnife.prefab", "PrepKnife", new Vector3(-1.87f, 1.085f, -1.90f), 105f);
            knife.transform.localScale *= .80f;
            knife.transform.rotation = Quaternion.Euler(90f, 15f, 0f);
            knife.transform.position += Vector3.up * (1.085f - Geometry(knife).min.y);
            foreach (var collider in knife.GetComponentsInChildren<Collider>()) collider.enabled = false;
            var key = Named(scene, "PrepCounter_KeyLight")?.GetComponent<Light>();
            if (key != null) { key.intensity = .55f; key.color = new Color(.92f, 1f, .97f); }
        }

        static void Club(Scene scene, Transform root)
        {
            ApplyClubPlacement(scene);
            Place(root, "StackedSpeaker", new Vector3(-2.75f, .05f, 4.20f), 0f, true, "_Left");
            Place(root, "StackedSpeaker", new Vector3(2.75f, .05f, 4.20f), 0f, true, "_Right");
            var fill = new GameObject("DJEquipmentFill").AddComponent<Light>(); fill.transform.SetParent(root, false);
            fill.transform.position = new Vector3(0f, 2.5f, 2.5f); fill.type = LightType.Point; fill.range = 5f; fill.intensity = .85f; fill.color = new Color(.65f, .72f, 1f);
        }

        // The legacy Club art generator also calls this, so reapplying it cannot restore the old high booth.
        public static void ApplyClubPlacement(Scene scene)
        {
            var platform = Named(scene, "Stage").transform;
            platform.position = new Vector3(0f, .125f, 3.6f); platform.localScale = new Vector3(5f, .25f, 2f);
            Named(scene, "ExistingStage_Anchor").transform.position = platform.position;
            Named(scene, "StageFrontTrim").transform.position = new Vector3(0f, .25f, 2.65f);
            var stand = Named(scene, "DJStand_PF_Club_DJStand");
            stand.transform.SetPositionAndRotation(new Vector3(0f, .252f, 3.75f), Quaternion.identity);
            stand.transform.localScale = Vector3.one * .68f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(stand.transform);
            float surface = Geometry(stand).max.y + .002f;
            foreach (var item in new[] { ("DJController_PF_Club_DJController", 0f, 4.01f), ("DJMixer_PF_Club_DJMixer", 0f, 3.49f), ("Turntable_Left_PF_Club_Turntable", -.52f, 3.49f), ("Turntable_Right_PF_Club_Turntable", .52f, 3.49f) })
            {
                var equipment = Named(scene, item.Item1);
                equipment.transform.localScale = Vector3.one * .50f;
                equipment.transform.position = new Vector3(item.Item2, surface, item.Item3);
                equipment.transform.position += Vector3.up * (surface - Geometry(equipment).min.y);
                PrefabUtility.RecordPrefabInstancePropertyModifications(equipment.transform);
            }
            var key = Named(scene, "DJ_Key_Spot")?.GetComponent<Light>();
            if (key != null) { key.transform.position = new Vector3(0f, 3.85f, 1.6f); key.transform.LookAt(new Vector3(0f, 1.2f, 3.7f)); key.color = new Color(.60f, .55f, 1f); key.intensity = 2.5f; }
        }

        static void Street(Scene scene, Transform root)
        {
            ApplyStreetDisplay(scene, root.parent);
            // The retained ground already supplies collision; reveal its surface beneath gaps between pavement and owner facades.
            var ground = Named(scene, "Street_Ground").GetComponent<Renderer>();
            ground.enabled = true; ground.sharedMaterial = Material("StreetConcrete", new Color(.33f, .36f, .37f));
            foreach (string id in new[] { "FishShop", "EquipmentShop", "Club" })
            {
                var sign = Named(scene, id + "ReadableSign").GetComponent<TextMesh>();
                sign.color = new Color(1f, .88f, .60f);
                Box(root, id + "SignBacking", sign.transform.position + Vector3.forward * .035f,
                    new Vector3(id == "EquipmentShop" ? 1.9f : 1.2f, .55f, .05f), new Color(.07f, .09f, .10f), false);
            }
        }

        static GameObject Place(Transform root, string id, Vector3 position, float yaw, bool solid = false, string suffix = "")
        {
            var go = Spawn(root, Art + "/PF_" + id + ".prefab", "Owner_" + id + suffix, position, yaw);
            if (solid)
            {
                var bounds = Geometry(go);
                // Bounds are measured in world space; keep the invisible blocker axis-aligned there.
                var existing = root.Find(go.name + "_Collision");
                var blocker = existing != null ? existing.GetComponent<BoxCollider>() : new GameObject(go.name + "_Collision").AddComponent<BoxCollider>();
                blocker.transform.SetParent(root, false); blocker.transform.position = bounds.center; blocker.size = bounds.size;
            }
            return go;
        }
        static GameObject Spawn(Transform root, string path, string name, Vector3 position, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException("Required venue prefab: " + path);
            var previous = root.Find(name);
            var go = previous != null ? previous.gameObject : (GameObject)PrefabUtility.InstantiatePrefab(prefab, root); go.name = name;
            go.transform.localScale = Vector3.one;
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
            return go;
        }
        static Bounds Geometry(GameObject go) => DayWorldAssetAuthoring.HomeGeometryBounds(go);
        static Transform[] All(Scene scene) => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        static GameObject Named(Scene scene, string name) => All(scene).FirstOrDefault(t => t.name == name)?.gameObject;
        static void Hide(GameObject go)
        { foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false; foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false; }
        static Material Material(string name, Color color, float gloss = .15f)
        {
            string path = Art + "/MAT_" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, path); }
            mat.color = color; mat.SetFloat("_Glossiness", gloss); EditorUtility.SetDirty(mat); return mat;
        }
        static GameObject Box(Transform root, string name, Vector3 center, Vector3 size, Color color, bool solid = true)
        {
            var previous = root.Find(name);
            var go = previous != null ? previous.gameObject : GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(root, false);
            go.transform.position = center; go.transform.localScale = size; go.GetComponent<Renderer>().sharedMaterial = Material(name, color);
            if (!solid) Object.DestroyImmediate(go.GetComponent<Collider>()); return go;
        }
    }
}
