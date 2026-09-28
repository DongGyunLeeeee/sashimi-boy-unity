using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    public static partial class DayWorldAssetAuthoring
    {
        public const string VenueOutput = Output + "/Venues";
        const string EquipmentSource = "Assets/_SashimiBoy/Art/Source/Environment/EquipmentShop/Equipment/";

        // These wrappers reference the owner's existing FBX/base colours. Source imports are never edited.
        public static void BuildVenueModels()
        {
            Directory.CreateDirectory(VenueOutput);
            Directory.CreateDirectory("Logs/DayWorld/VenueRevision/Models");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            VenueWrapper("ElectronicDrumKit", EquipmentSource + "ElectronicDrumKit/Models/ElectronicDrumKit.fbx", 1.10f, false);
            VenueWrapper("MidiKeyboardController", EquipmentSource + "MidiKeyboardController/Models/MidiKeyboardController.fbx", 1.15f, true);
            VenueWrapper("ModularSynthesizer", EquipmentSource + "ModularSynthesizer/Models/ModularSynthesizer.fbx", .95f, true);
            VenueWrapper("EffectsPedals", EquipmentSource + "EffectsPedals/Models/EffectsPedals.fbx", .65f, true);
            VenueWrapper("GuitarPedal", EquipmentSource + "GuitarPedal/Models/GuitarPedal.fbx", .22f, true);
            VenueWrapper("Loudspeaker", EquipmentSource + "Loudspeaker/Models/Loudspeaker.fbx", 1.45f, false);
            VenueWrapper("SpeakerBox", EquipmentSource + "SpeakerBox/Models/SpeakerBox.fbx", .85f, false);
            VenueWrapper("StageSpotlight", EquipmentSource + "StageSpotlight/Models/StageSpotlight.fbx", .60f, false);
            VenueWrapper("StackedSpeaker", EquipmentSource + "StackedSpeaker/Models/StackedSpeaker.fbx", 1.65f, false);
            VenueWrapper("StereoSpeaker", EquipmentSource + "StereoSpeaker/Models/StereoSpeaker.fbx", .65f, false);
            VenueWrapper("VintageSpeaker", EquipmentSource + "VintageSpeaker/Models/VintageSpeaker.fbx", .80f, false);
            VenueWrapper("WoodenSofa", "Assets/_SashimiBoy/Art/Source/Environment/EquipmentShop/Furniture/WoodenSofa/Models/WoodenSofa.fbx", .85f, false);
            VenueWrapper("EquipmentShopOwner", "Assets/_SashimiBoy/Art/Source/Characters/EquipmentShopOwner/Models/EquipmentShopOwner.fbx", 1.76f, false);
            BuildFlounderDisplay();
            AssetDatabase.SaveAssets();
        }

        public static void BuildFlounderDisplay()
        {
            VenueWrapper("Flounder", "Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fish/Flounder/Models/Flounder.fbx", .85f, true);
        }

        static void VenueWrapper(string id, string sourcePath, float size, bool fitWidth)
        {
            var root = new GameObject("PF_Venue_" + id);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath), root.transform);
                // This keyboard was authored standing on its back edge; present its keys on the counter.
                if (id == "MidiKeyboardController") model.transform.localRotation = Quaternion.Euler(-75f, 0f, 0f) * model.transform.localRotation;
                BindMaterials(model, sourcePath, "Venue_" + id);
                foreach (var material in model.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Distinct())
                {
                    var normal = material.GetTexture("_BumpMap");
                    if (normal == null) continue;
                    string path = VenueOutput + "/N_" + material.name.Replace("MAT_Venue_", "") + ".JPEG";
                    if (!File.Exists(path)) File.Copy(AssetDatabase.GetAssetPath(normal), path);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    if (importer.textureType != TextureImporterType.NormalMap || importer.maxTextureSize != 1024 || importer.sRGBTexture)
                    {
                        importer.textureType = TextureImporterType.NormalMap;
                        importer.sRGBTexture = false;
                        importer.maxTextureSize = 1024;
                        importer.SaveAndReimport();
                    }
                    material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                    material.SetFloat("_BumpScale", .35f);
                    material.SetFloat("_GlossMapScale", .45f);
                    EditorUtility.SetDirty(material);
                }
                var bounds = HomeGeometryBounds(model);
                model.transform.localScale *= size / (fitWidth ? Mathf.Max(bounds.size.x, bounds.size.z) : bounds.size.y);
                bounds = HomeGeometryBounds(model);
                model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                if (id == "EquipmentShopOwner") RestShopkeeperArms(model);
                PrefabUtility.SaveAsPrefabAsset(root, VenueOutput + "/PF_" + id + ".prefab");
                Debug.Log("[VenueModel] " + id + " " + HomeGeometryBounds(root));
                RenderHomeInspection(root, "Logs/DayWorld/VenueRevision/Models/" + id + ".png");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void RestShopkeeperArms(GameObject model)
        {
            int part = 0;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = Object.Instantiate(filter.sharedMesh);
                var vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 p = filter.transform.TransformPoint(vertices[i]);
                    float weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.23f, .42f, Mathf.Abs(p.x))) * Mathf.InverseLerp(1.02f, 1.16f, p.y);
                    float side = Mathf.Sign(p.x);
                    Vector3 shoulder = new Vector3(side * .25f, 1.40f, 0f);
                    p = shoulder + Quaternion.Euler(0f, 0f, -side * 62f * weight) * (p - shoulder);
                    vertices[i] = filter.transform.InverseTransformPoint(p);
                }
                mesh.vertices = vertices;
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                string path = VenueOutput + "/MS_ShopkeeperRest_" + part++ + ".asset";
                var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (saved == null) { AssetDatabase.CreateAsset(mesh, path); saved = mesh; }
                else { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); }
                filter.sharedMesh = saved;
            }
        }

        public static void InspectVenuesBatch()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildVenueModels();
            foreach (string path in new[] {
                "Assets/_SashimiBoy/Art/Generated/Prefabs/Club/PF_Club_DJStand.prefab",
                "Assets/_SashimiBoy/Art/Generated/Prefabs/Club/PF_Club_DJController.prefab",
                "Assets/_SashimiBoy/Art/Generated/Prefabs/Club/PF_Club_DJMixer.prefab",
                "Assets/_SashimiBoy/Art/Generated/Prefabs/FishShop/PF_Fixture_SashimiTable.prefab",
                "Assets/_SashimiBoy/Art/Generated/Prefabs/FishShop/PF_Fixture_DisplayInside.prefab" })
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                try
                {
                    Debug.Log("[VenueModel] " + model.name + " " + HomeGeometryBounds(model));
                    RenderHomeInspection(model, "Logs/DayWorld/VenueRevision/Models/" + model.name + ".png");
                }
                finally { Object.DestroyImmediate(model); }
            }
            foreach (string room in new[] { "EquipmentShop", "FishShopDialogue", "Club", "Street" })
            {
                var scene = EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/" + room + ".unity", OpenSceneMode.Single);
                var lines = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Select(t =>
                {
                    string hierarchy = t.name;
                    for (var p = t.parent; p != null; p = p.parent) hierarchy = p.name + "/" + hierarchy;
                    var renderer = t.GetComponent<Renderer>();
                    return hierarchy + " | " + t.position + " | " + t.eulerAngles + " | active=" + t.gameObject.activeInHierarchy +
                        (renderer == null ? "" : " | renderer=" + renderer.enabled + " | " + renderer.bounds);
                });
                File.WriteAllLines("Logs/DayWorld/VenueRevision/" + room + "-before-hierarchy.txt", lines);
            }
        }
    }
}
