using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SashimiBoy.EquipmentShopTests
{
    public sealed class EquipmentShopArtPassEditModeTests
    {
        private const string PrefabRoot =
            "Assets/_SashimiBoy/Art/Generated/Prefabs/EquipmentShop/";
        private const string MaterialRoot =
            "Assets/_SashimiBoy/Art/Generated/Materials/EquipmentShop/";
        private const string ScenePath =
            "Assets/_SashimiBoy/Scenes/EquipmentShop.unity";

        private static readonly string[] AssetIds =
        {
            "EquipmentShopOwner",
            "WoodenSofa",
            "EffectsPedals",
            "ElectronicDrumKit",
            "GuitarPedal",
            "Loudspeaker",
            "MidiKeyboardController",
            "ModularSynthesizer",
            "SpeakerBox",
            "StackedSpeaker",
            "StageSpotlight",
            "StereoSpeaker",
            "VintageSpeaker",
        };

        [Test]
        public void CanonicalWrappers_UseGeneratedMaterialsAndPositiveScale()
        {
            for (int i = 0; i < AssetIds.Length; i++)
            {
                string path = PrefabRoot + "PF_EquipmentShop_" +
                    AssetIds[i] + ".prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, path);
                AssertPositiveScale(prefab.transform);
                Renderer[] renderers =
                    prefab.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers, Is.Not.Empty, path);
                foreach (Renderer renderer in renderers)
                {
                    Assert.That(renderer.sharedMaterials, Is.Not.Empty,
                        renderer.name + " has no material slots.");
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Assert.That(material, Is.Not.Null,
                            renderer.name + " has a missing material.");
                        Assert.That(
                            AssetDatabase.GetAssetPath(material).StartsWith(
                                MaterialRoot,
                                StringComparison.Ordinal),
                            Is.True,
                            renderer.name + " does not use a generated " +
                            "EquipmentShop material.");
                    }
                }
            }
        }

        [Test]
        public void EquipmentShopScene_UsesZonesAndPreservesStoryReferences()
        {
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Additive);
            try
            {
                GameObject artRoot = FindNamed(scene, "EquipmentShopArtRoot");
                Assert.That(artRoot, Is.Not.Null);
                Assert.That(
                    artRoot.transform.Find("CounterRepairZone"),
                    Is.Not.Null);
                Assert.That(artRoot.transform.Find("DemoZone"), Is.Not.Null);
                Assert.That(
                    artRoot.transform.Find("InstrumentZone"),
                    Is.Not.Null);
                Assert.That(artRoot.transform.Find("WaitingZone"), Is.Not.Null);
                Assert.That(
                    artRoot.transform.Find("StoryAnchors"),
                    Is.Not.Null);

                Dictionary<string, string> instances =
                    new Dictionary<string, string>
                    {
                        { "Owner_Canonical", "EquipmentShopOwner" },
                        { "Waiting_WoodenSofa", "WoodenSofa" },
                        { "Repair_EffectsPedals", "EffectsPedals" },
                        { "Instrument_ElectronicDrumKit", "ElectronicDrumKit" },
                        { "Repair_GuitarPedal", "GuitarPedal" },
                        { "Demo_Loudspeaker", "Loudspeaker" },
                        { "Instrument_MidiKeyboard", "MidiKeyboardController" },
                        { "Instrument_ModularSynth", "ModularSynthesizer" },
                        { "Demo_SpeakerBox", "SpeakerBox" },
                        { "Demo_StackedSpeaker", "StackedSpeaker" },
                        { "Demo_StageSpotlight", "StageSpotlight" },
                        { "Demo_StereoSpeaker", "StereoSpeaker" },
                        { "Demo_VintageSpeaker", "VintageSpeaker" },
                    };
                foreach (KeyValuePair<string, string> pair in instances)
                {
                    AssertPrefabPath(
                        scene,
                        pair.Key,
                        PrefabRoot + "PF_EquipmentShop_" + pair.Value +
                        ".prefab");
                }

                AssertAnchorMatches(
                    scene,
                    "PlayerEntryExitAnchor",
                    "PlayerSpawnPoint");
                AssertAnchorMatches(
                    scene,
                    "OwnerDialogueAnchor",
                    "Shopkeeper");
                AssertAnchorMatches(
                    scene,
                    "PurchaseInteractionAnchor",
                    "Counter");
                Assert.That(
                    FindNamed(scene, "EquipmentInspectAnchor_Demo"),
                    Is.Not.Null);
                Assert.That(
                    FindNamed(scene, "EquipmentInspectAnchor_Instrument"),
                    Is.Not.Null);
                AssertStorySightline(scene);

                AssertPosition(
                    scene,
                    "PlayerSpawnPoint",
                    new Vector3(0f, 0.6f, -2.2f));
                AssertPosition(
                    scene,
                    "Kevin_Player",
                    new Vector3(0f, 0.6f, -2.2f));
                AssertPosition(
                    scene,
                    "Door_To_Street",
                    new Vector3(5.2f, 0.8f, -2.6f));
                AssertPosition(
                    scene,
                    "Shopkeeper",
                    new Vector3(0f, 1f, 3.2f));
                AssertPosition(
                    scene,
                    "Counter",
                    new Vector3(0f, 0.6f, 2.3f));

                Assert.That(
                    Components(scene, "SashimiBoy.EquipmentShopController")
                        .Length,
                    Is.EqualTo(1));
                Assert.That(
                    Components(scene, "SashimiBoy.ReturnToStreetDoor").Length,
                    Is.EqualTo(1));
                Assert.That(
                    Components(scene, "SashimiBoy.KevinFirstPersonCameraRig")
                        .Length,
                    Is.EqualTo(1));

                string[] hiddenLegacyVisuals =
                {
                    "EquipmentShop_PresentationRoot",
                    "Counter",
                    "Shopkeeper",
                    "Label_ReturnDoor",
                    "WallGear_0",
                    "WallGear_1",
                    "WallGear_2",
                    "WallGear_3",
                    "WallGear_4",
                    "WallGear_5",
                };
                for (int i = 0; i < hiddenLegacyVisuals.Length; i++)
                {
                    AssertNoEnabledRenderer(scene, hiddenLegacyVisuals[i]);
                    AssertNoEnabledCollider(scene, hiddenLegacyVisuals[i]);
                }

                AssertPositiveScale(artRoot.transform);
                AssertProtectedRoutesAreClear(artRoot);
                AssertNoMissingScripts(scene);
                AssertNoMissingSerializedReferences(scene);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void RebuildEquipmentShopArtPass_TwoRunsKeepSceneBytes()
        {
            string absoluteScenePath = AssetPathToAbsolutePath(ScenePath);
            byte[] committedBytes = File.ReadAllBytes(absoluteScenePath);
            string committedHash = ComputeSha256(committedBytes);
            byte[] firstRunBytes = null;
            byte[] secondRunBytes = null;

            try
            {
                RuntimeReflection.InvokeStatic(
                    "SashimiBoy.EditorTools.EquipmentShopArtPassPipeline",
                    "ApplyEquipmentShopArtToMainSceneBatch");
                firstRunBytes = File.ReadAllBytes(absoluteScenePath);
                RuntimeReflection.InvokeStatic(
                    "SashimiBoy.EditorTools.EquipmentShopArtPassPipeline",
                    "ApplyEquipmentShopArtToMainSceneBatch");
                secondRunBytes = File.ReadAllBytes(absoluteScenePath);
            }
            finally
            {
                EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Single);
            }

            Assert.That(
                ComputeSha256(firstRunBytes),
                Is.EqualTo(committedHash),
                "The first generator run changed the committed scene hash.");
            Assert.That(
                firstRunBytes,
                Is.EqualTo(committedBytes),
                "The first generator run changed the committed scene bytes.");
            Assert.That(
                ComputeSha256(secondRunBytes),
                Is.EqualTo(committedHash),
                "The second generator run changed the committed scene hash.");
            Assert.That(
                secondRunBytes,
                Is.EqualTo(committedBytes),
                "The second generator run changed the committed scene bytes.");
        }

        private static void AssertPrefabPath(
            Scene scene,
            string objectName,
            string expectedPath)
        {
            GameObject instance = FindNamed(scene, objectName);
            Assert.That(instance, Is.Not.Null, objectName);
            GameObject outermost =
                PrefabUtility.GetOutermostPrefabInstanceRoot(instance);
            Assert.That(outermost, Is.Not.Null, objectName);
            Assert.That(
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(outermost),
                Is.EqualTo(expectedPath));
        }

        private static void AssertAnchorMatches(
            Scene scene,
            string anchorName,
            string targetName)
        {
            Transform anchor = FindNamed(scene, anchorName).transform;
            Transform target = FindNamed(scene, targetName).transform;
            Assert.That(anchor.position, Is.EqualTo(target.position));
            Assert.That(anchor.rotation, Is.EqualTo(target.rotation));
        }

        private static void AssertStorySightline(Scene scene)
        {
            Transform from =
                FindNamed(scene, "StoryObjectiveSightline_FromEntry").transform;
            Transform to =
                FindNamed(scene, "StoryObjectiveSightline_ToOwner").transform;
            Vector3 expected = (to.position - from.position).normalized;
            Assert.That(
                Vector3.Dot(from.forward, expected),
                Is.GreaterThan(0.999f));
            Assert.That(
                Vector3.Distance(from.position, to.position),
                Is.GreaterThan(4f));
        }

        private static void AssertPosition(
            Scene scene,
            string objectName,
            Vector3 expected)
        {
            Assert.That(
                FindNamed(scene, objectName).transform.position,
                Is.EqualTo(expected),
                objectName);
        }

        private static void AssertNoEnabledRenderer(
            Scene scene,
            string objectName)
        {
            GameObject target = FindNamed(scene, objectName);
            Assert.That(
                target.GetComponentsInChildren<Renderer>(true)
                    .Any(renderer => renderer.enabled),
                Is.False,
                objectName + " is still visibly rendered.");
        }

        private static void AssertNoEnabledCollider(
            Scene scene,
            string objectName)
        {
            GameObject target = FindNamed(scene, objectName);
            Assert.That(
                target.GetComponentsInChildren<Collider>(true)
                    .Any(collider => collider.enabled),
                Is.False,
                objectName + " still has a legacy collider.");
        }

        private static void AssertProtectedRoutesAreClear(GameObject artRoot)
        {
            Physics.SyncTransforms();
            Bounds[] protectedRoutes =
            {
                new Bounds(
                    new Vector3(0f, 1.2f, -0.35f),
                    new Vector3(1.8f, 2.4f, 3.8f)),
                new Bounds(
                    new Vector3(2.65f, 1.2f, -2.35f),
                    new Vector3(5.3f, 2.4f, 1.4f)),
                new Bounds(
                    new Vector3(0f, 1.2f, 1.15f),
                    new Vector3(2.2f, 2.4f, 0.8f)),
            };
            foreach (Collider collider in
                     artRoot.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.enabled || !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Assert.That(
                    protectedRoutes.Any(route =>
                        collider.bounds.Intersects(route)),
                    Is.False,
                    collider.name + " blocks a protected route.");
            }
        }

        private static void AssertNoMissingScripts(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform child in
                         root.GetComponentsInChildren<Transform>(true))
                {
                    Assert.That(
                        GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                            child.gameObject),
                        Is.Zero,
                        "Missing Script under " + child.name);
                }
            }
        }

        private static void AssertNoMissingSerializedReferences(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Component component in
                         root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null)
                    {
                        Assert.Fail("A scene component is missing.");
                    }

                    SerializedObject serialized = new SerializedObject(component);
                    SerializedProperty property = serialized.GetIterator();
#pragma warning disable CS0618
                    while (property.NextVisible(true))
                    {
                        if (property.propertyType ==
                                SerializedPropertyType.ObjectReference &&
                            property.objectReferenceValue == null &&
                            property.objectReferenceInstanceIDValue != 0)
                        {
                            Assert.Fail(
                                component.name + "." + property.propertyPath +
                                " is a missing serialized reference.");
                        }
                    }
#pragma warning restore CS0618
                }
            }
        }

        private static void AssertPositiveScale(Transform root)
        {
            foreach (Transform item in
                     root.GetComponentsInChildren<Transform>(true))
            {
                Vector3 scale = item.localScale;
                Assert.That(scale.x, Is.GreaterThan(0f), item.name);
                Assert.That(scale.y, Is.GreaterThan(0f), item.name);
                Assert.That(scale.z, Is.GreaterThan(0f), item.name);
            }
        }

        private static string AssetPathToAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)
                ?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
            {
                throw new InvalidOperationException(
                    "Could not resolve the Unity project root.");
            }

            return Path.Combine(
                projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes))
                    .Replace("-", "");
            }
        }

        private static GameObject FindNamed(Scene scene, string name)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(item => item.name == name)
                ?.gameObject;
        }

        private static Component[] Components(
            Scene scene,
            string fullTypeName)
        {
            Type type = RuntimeReflection.RuntimeType(fullTypeName);
            return scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren(type, true)
                        .Cast<Component>())
                .ToArray();
        }
    }
}
