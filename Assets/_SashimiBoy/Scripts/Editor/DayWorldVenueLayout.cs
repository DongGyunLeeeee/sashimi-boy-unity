using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    public static partial class DayWorldVenueAuthoring
    {
        static readonly Vector3 PrepCenter = new Vector3(-4f, 0f, 2.70f);

        static void FillShopAndBindOwner(Scene scene, Transform root)
        {
            // Purchase belongs to the visible person. The retained counter is only solid furniture.
            Named(scene, "ShopService").SetActive(false);
            Named(scene, "ShopPrompt").GetComponent<TextMesh>().text = "";
            var owner = root.Find("Owner_EquipmentShopOwner");
            owner.position = new Vector3(0f, .05f, 1.35f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(owner);
            var oldBlocker = root.Find("Owner_EquipmentShopOwner_Collision");
            if (oldBlocker != null) oldBlocker.GetComponent<Collider>().enabled = false;
            var capsule = owner.GetComponent<CapsuleCollider>();
            if (capsule == null) capsule = owner.gameObject.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, .88f, 0f); capsule.height = 1.76f; capsule.radius = .29f;
            var service = owner.GetComponent<DayWorldInteractable>() ?? owner.gameObject.AddComponent<DayWorldInteractable>();
            service.kind = DayWorldInteractionKind.Shop;
            service.director = root.parent.GetComponent<DayWorldSceneDirector>();

            Color walnut = new Color(.25f, .16f, .10f), metal = new Color(.11f, .14f, .15f);
            Box(root, "KeyboardDemoIsland", new Vector3(3.1f, .48f, -1.05f), new Vector3(2.35f, .92f, 1f), walnut);
            Box(root, "KeyboardDemoTop", new Vector3(3.1f, .97f, -1.05f), new Vector3(2.48f, .08f, 1.12f), metal);
            Place(root, "MidiKeyboardController", new Vector3(2.75f, 1.015f, -1.08f), 180f, false, "_Demo");
            Place(root, "GuitarPedal", new Vector3(3.85f, 1.015f, -1.08f), 180f, false, "_Demo");
            Box(root, "PedalDisplayIsland", new Vector3(-3.6f, .42f, -1.65f), new Vector3(2.1f, .80f, .80f), walnut);
            Box(root, "PedalDisplayTop", new Vector3(-3.6f, .86f, -1.65f), new Vector3(2.22f, .08f, .94f), metal);
            Place(root, "EffectsPedals", new Vector3(-3.95f, .905f, -1.64f), 180f, false, "_Demo");
            Place(root, "GuitarPedal", new Vector3(-2.97f, .905f, -1.64f), 180f, false, "_Shelf");
            Place(root, "VintageSpeaker", new Vector3(-5.55f, .04f, -2.3f), 135f, true, "_Front");
            Place(root, "SpeakerBox", new Vector3(5.35f, .04f, -2.5f), 225f, true, "_Front");
            Box(root, "SpeakerDisplayPlinth", new Vector3(3.45f, .19f, -3.25f), new Vector3(1.65f, .32f, .62f), walnut);
            Place(root, "StereoSpeaker", new Vector3(3.12f, .355f, -3.25f), 0f, false, "_FrontLeft");
            Place(root, "StereoSpeaker", new Vector3(3.82f, .355f, -3.25f), 0f, false, "_FrontRight");
            Box(root, "ListeningTable", new Vector3(4.85f, .42f, .5f), new Vector3(.52f, .08f, 1.2f), walnut);
            // Compact wall shelves add stock without occupying the entry or the owner's approach aisle.
            for (int i = 0; i < 3; i++)
            {
                Box(root, "AccessoryShelf_" + i, new Vector3(-6.18f, .7f + i * .54f, -.4f), new Vector3(.38f, .08f, 1.6f), walnut);
                Place(root, i == 2 ? "GuitarPedal" : "EffectsPedals", new Vector3(-6.15f, .745f + i * .54f, -.4f), 90f, false, "_Wall" + i);
            }
        }

        static void ArrangeDiningAndKitchen(Scene scene, Transform root)
        {
            Color wood = new Color(.35f, .22f, .12f), darkWood = new Color(.17f, .115f, .07f);
            // Retain the actual owner's cabinets, display case and fish. Their rear line is the kitchen.
            foreach (string id in new[] { "SashimiTable_Left_Validated", "SashimiTable_Right_Validated" })
            {
                var cabinet = Named(scene, id).transform;
                cabinet.position = new Vector3(id.Contains("Left") ? -1.34f : 1.34f, .08f, 2.85f);
                PrefabUtility.RecordPrefabInstancePropertyModifications(cabinet);
            }
            foreach (string id in new[] { "Counter", "CounterFront", "CounterTrim", "PrepBench_Left", "PrepBench_Right" })
            {
                var old = Named(scene, id);
                if (old != null) Hide(old);
            }
            root.Find("FishDisplayWorktop").position = new Vector3(0f, 1.15f, 2.85f);
            Named(scene, "FishDisplay_SurfaceAnchor").transform.position = new Vector3(0f, 1.225f, 2.65f);
            foreach (string fish in new[] { "Salmon", "Rockfish", "Mullet" })
            {
                var model = Named(scene, "DisplayFish_" + fish).transform;
                model.position += new Vector3(0f, 0f, 2.65f - model.position.z);
                PrefabUtility.RecordPrefabInstancePropertyModifications(model);
                var tray = root.Find("FishTray_" + fish); tray.position = new Vector3(tray.position.x, tray.position.y, 2.65f);
            }
            // The supplied flatfish was omitted from this interior. It now occupies its own chilled tray.
            var flounder = Spawn(root, Art + "/PF_Flounder.prefab", "DisplayFish_Flounder", new Vector3(3.8f, .55f, 1.15f), 90f);
            flounder.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(-90f, 0f, 0f);
            flounder.transform.position += Vector3.up * (1.18f - Geometry(flounder).min.y);
            PrefabUtility.RecordPrefabInstancePropertyModifications(flounder.transform);
            foreach (var c in flounder.GetComponentsInChildren<Collider>()) c.enabled = false;
            Box(root, "FlounderServingTray", new Vector3(3.8f, 1.16f, 1.15f), new Vector3(.75f, .03f, 1.05f), new Color(.83f, .87f, .84f), false);

            Box(root, "KitchenTileFloor", new Vector3(0f, .072f, 2.45f), new Vector3(11.6f, .04f, 2.8f), new Color(.32f, .46f, .44f), false);
            Box(root, "KitchenServiceCounter", new Vector3(1.85f, .55f, 1.15f), new Vector3(7.3f, 1f, .55f), new Color(.13f, .34f, .33f));
            Box(root, "KitchenServiceTop", new Vector3(1.85f, 1.09f, 1.15f), new Vector3(7.42f, .08f, .65f), new Color(.62f, .69f, .68f));
            Box(root, "KitchenShelf", new Vector3(0f, 2.12f, 3.68f), new Vector3(5.4f, .10f, .4f), new Color(.44f, .51f, .50f), false);
            for (int i = 0; i < 8; i++)
                Box(root, "KitchenStorage_" + i, new Vector3(-2.3f + i * .64f, 2.31f, 3.65f), new Vector3(.43f, .27f, .28f), i % 2 == 0 ? new Color(.83f, .82f, .74f) : new Color(.18f, .31f, .29f), false);

            // Move the existing start target, its collider and return spawn with the cutting station.
            Vector3 offset = PrepCenter - new Vector3(-2.1f, 0f, -1.8f);
            foreach (string name in new[] { "StagePrepTop", "StageCuttingBoard", "PrepLeg_0", "PrepLeg_1", "PrepLeg_2", "PrepLeg_3", "PrepKnife" })
                root.Find(name).position += offset;
            var start = All(scene).Select(t => t.GetComponent<StageStarterInteractable>()).Single(c => c != null).transform;
            // These old prompt/mat helpers are presentation children of the original start target.
            start.position = new Vector3(PrepCenter.x, start.position.y, PrepCenter.z);
            foreach (string name in new[] { "StageStart_Mat", "StageStart_Inset", "Label_StagePlaceholder" })
            {
                var old = Named(scene, name);
                if (old != null && !old.transform.IsChildOf(start)) old.SetActive(false);
            }
            root.parent.GetComponent<DayWorldSceneDirector>().FindSpawn("StageReturn").position = new Vector3(-4f, .94f, 1.25f);

            // Three dining settings occupy the front hall; the central and left kitchen aisles remain walkable.
            DiningTable(root, "FrontLeft", new Vector3(-3.45f, 0f, -2.1f), wood, darkWood);
            DiningTable(root, "FrontRight", new Vector3(3.45f, 0f, -2.1f), wood, darkWood);
            DiningTable(root, "MiddleRight", new Vector3(3.45f, 0f, -.05f), wood, darkWood);
            foreach (string id in new[] { "CustomerTable", "CustomerTableSupport", "CustomerSeat" }) Named(scene, id).SetActive(false);
            // Cheolsu is the restaurant regular. His existing dialogue and meal stay beside the hall table.
            var customer = root.parent.GetComponentsInChildren<DayWorldNpc>(true).First(n => n.npcId == "cheolsu");
            customer.transform.position = new Vector3(3.45f, .05f, -1.03f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(customer.transform);
            var plate = Named(scene, "CheolsuMealPlate"); plate.transform.position = new Vector3(3.45f, .82f, -2.1f);
            foreach (var meal in root.parent.Cast<Transform>().Where(t => t.name.ToLowerInvariant().Contains("salmonpiece")))
            { meal.position += new Vector3(3.45f - Geometry(meal.gameObject).center.x, 0f, -2.1f - Geometry(meal.gameObject).center.z); PrefabUtility.RecordPrefabInstancePropertyModifications(meal); }
        }

        public static void ApplyStreetDisplay(Scene scene, Transform worldRoot)
        {
            var root = worldRoot.Find("VenueAssets");
            if (root == null) { root = new GameObject("VenueAssets").transform; root.SetParent(worldRoot, false); }
            var door = worldRoot.Find("Door_To_FishShopDialogue_DayWorld");
            var instance = Spawn(root, "Assets/_SashimiBoy/Art/Generated/Prefabs/FishShop/PF_Fixture_DisplayOutside.prefab",
                "Owner_FishShopOutdoorDisplay", new Vector3(door.position.x - 2.25f, .09f, door.position.z - .38f), 180f);
            var model = instance.transform.Find("Model");
            model.localPosition = Vector3.zero; model.localScale = Vector3.one; model.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            Bounds bounds = Geometry(instance);
            model.localScale *= Mathf.Min(1.1f / bounds.size.y, 2.15f / bounds.size.x);
            bounds = Geometry(instance);
            model.position += instance.transform.position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            PrefabUtility.RecordPrefabInstancePropertyModifications(model);
            foreach (var c in instance.GetComponentsInChildren<Collider>()) c.enabled = false;
            bounds = Geometry(instance);
            var existing = root.Find("OutdoorDisplayCollision");
            var blocker = existing != null ? existing.GetComponent<BoxCollider>() : new GameObject("OutdoorDisplayCollision").AddComponent<BoxCollider>();
            blocker.transform.SetParent(root, false); blocker.transform.position = bounds.center; blocker.size = bounds.size;
        }

        static void DiningTable(Transform root, string id, Vector3 p, Color wood, Color dark)
        {
            Box(root, "DiningTop_" + id, p + Vector3.up * .75f, new Vector3(1.5f, .1f, .85f), wood);
            for (int i = 0; i < 4; i++)
                Box(root, "DiningLeg_" + id + i, p + new Vector3(i % 2 == 0 ? -.60f : .60f, .38f, i < 2 ? -.28f : .28f), new Vector3(.07f, .74f, .07f), dark);
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 chair = p + new Vector3(side * 1.05f, 0f, 0f);
                Box(root, "DiningSeat_" + id + side, chair + Vector3.up * .47f, new Vector3(.54f, .09f, .56f), wood);
                Box(root, "DiningBack_" + id + side, chair + new Vector3(side * .25f, .78f, 0f), new Vector3(.07f, .60f, .56f), dark);
                for (int i = 0; i < 4; i++)
                    Box(root, "ChairLeg_" + id + side + "_" + i, chair + new Vector3(i % 2 == 0 ? -.21f : .21f, .23f, i < 2 ? -.21f : .21f), new Vector3(.055f, .46f, .055f), dark);
                Box(root, "DiningTray_" + id + side, p + new Vector3(side * .42f, .818f, 0f), new Vector3(.36f, .025f, .44f), new Color(.83f, .87f, .84f), false);
                Box(root, "DiningChopsticks_" + id + side, p + new Vector3(side * .42f, .845f, -.16f), new Vector3(.3f, .016f, .025f), dark, false);
            }
            Box(root, "CondimentTray_" + id, p + new Vector3(0f, .83f, .22f), new Vector3(.29f, .05f, .18f), dark, false);
            Box(root, "SoySauce_" + id, p + new Vector3(.05f, .95f, .22f), new Vector3(.065f, .2f, .065f), new Color(.23f, .09f, .045f), false);
        }
    }
}
