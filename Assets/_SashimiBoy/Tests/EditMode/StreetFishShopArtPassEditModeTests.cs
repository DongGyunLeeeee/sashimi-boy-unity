using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace SashimiBoy.Tests
{
    public sealed class StreetFishShopArtPassEditModeTests
    {
        private const string ScenePath =
            "Assets/_SashimiBoy/Scenes/Street.unity";
        private const string DisplayPrefabPath =
            "Assets/_SashimiBoy/Art/Generated/Prefabs/FishShop/" +
            "PF_Fixture_DisplayOutside.prefab";

        [Test]
        public void StreetFishShopFrontage_UsesCanonicalFacingAndClearRoutes()
        {
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Additive);
            try
            {
                GameObject frontage = FindNamed(
                    scene,
                    "Task2_FishShopExteriorAssets");
                GameObject facade = FindNamed(scene, "FishShop_Facade");
                Assert.That(frontage, Is.Not.Null);
                Assert.That(facade, Is.Not.Null);
                AssertPositiveScale(frontage.transform);
                AssertPositiveScale(facade.transform);

                GameObject display = FindNamed(
                    scene,
                    "DisplayOutside_Validated");
                Assert.That(display, Is.Not.Null);
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        display),
                    Is.EqualTo(DisplayPrefabPath));
                Assert.That(
                    display.transform.position,
                    Is.EqualTo(new Vector3(-5.6f, 0.12f, -1.65f)));
                Assert.That(display.transform.rotation, Is.EqualTo(
                    Quaternion.identity));
                Assert.That(display.transform.localScale, Is.EqualTo(
                    Vector3.one * 0.72f));
                Assert.That(
                    display.GetComponentsInChildren<Collider>(true),
                    Is.Empty,
                    "Decorative display colliders must not block movement.");

                GameObject signBoard = FindNamed(
                    scene,
                    "FishShop_Sign_Board");
                GameObject signObject = FindNamed(
                    scene,
                    "FishShop_Sign_Text");
                Assert.That(signBoard, Is.Not.Null);
                Assert.That(signObject, Is.Not.Null);
                TextMesh sign = signObject.GetComponent<TextMesh>();
                Assert.That(sign, Is.Not.Null);
                Assert.That(sign.text, Is.EqualTo("SASHIMI"));
                Assert.That(
                    signObject.transform.position.z,
                    Is.GreaterThan(signBoard.transform.position.z),
                    "The text must be on the facade's +Z customer face.");
                Assert.That(
                    Vector3.Dot(
                        -signObject.transform.forward,
                        Vector3.forward),
                    Is.GreaterThan(0.999f),
                    "The TextMesh front must face the +Z approach.");

                GameObject door = FindNamed(
                    scene,
                    "Door_To_FishShopDialogue");
                Assert.That(door.transform.position, Is.EqualTo(
                    new Vector3(-3.5f, 0.8f, -2.6f)));
                Assert.That(door.transform.rotation, Is.EqualTo(
                    Quaternion.identity));
                Assert.That(door.transform.localScale, Is.EqualTo(
                    new Vector3(1.5f, 1.6f, 0.35f)));
                AssertSceneDoorContract(door);

                Bounds doorClearance = ObjectBounds(door);
                doorClearance.Expand(new Vector3(0.45f, 1f, 0.45f));
                Assert.That(
                    ObjectBounds(display).Intersects(doorClearance),
                    Is.False,
                    "DisplayOutside blocks the FishShop door approach.");

                Bounds counterClearance = ObjectBounds(
                    FindNamed(scene, "DisplayCounter"));
                counterClearance.Expand(new Vector3(0.12f, 0.12f, 0.12f));
                Assert.That(
                    ObjectBounds(display).Intersects(counterClearance),
                    Is.False,
                    "DisplayOutside overlaps the existing display counter.");

                AssertAnchor(
                    scene,
                    "NPCSpawn_Morning",
                    new Vector3(-6.7f, 0.18f, -0.65f));
                AssertAnchor(
                    scene,
                    "NPCDialogueStanding_Morning",
                    new Vector3(-5.75f, 0.18f, -0.65f));
                AssertAnchor(
                    scene,
                    "NPCDialogueCamera_Morning",
                    new Vector3(-6.2f, 1.65f, -1.55f));
                AssertAnchor(
                    scene,
                    "NPCSpawn_AfterWork",
                    new Vector3(3.25f, 0.18f, 1.55f));
                AssertAnchor(
                    scene,
                    "NPCDialogueStanding_AfterWork",
                    new Vector3(3.25f, 0.18f, 0.55f));
                AssertAnchor(
                    scene,
                    "NPCDialogueCamera_AfterWork",
                    new Vector3(4.15f, 1.65f, 1.05f));

                Assert.That(
                    Components(scene, "SashimiBoy.SceneDoor").Length,
                    Is.EqualTo(3));
                Assert.That(
                    Components<AudioListener>(scene)
                        .Count(item => item.enabled &&
                            item.gameObject.activeInHierarchy),
                    Is.EqualTo(1));
                Assert.That(
                    Components<EventSystem>(scene)
                        .Count(item => item.enabled &&
                            item.gameObject.activeInHierarchy),
                    Is.EqualTo(1));
                AssertNoMissingScripts(scene);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void RebuildStreetFishShopFrontage_Twice_KeepsSceneBytes()
        {
            string absoluteScenePath = AssetPathToAbsolutePath(ScenePath);
            byte[] committedBytes = File.ReadAllBytes(absoluteScenePath);
            byte[] firstRunBytes = null;
            byte[] secondRunBytes = null;

            try
            {
                RuntimeReflection.InvokeStatic(
                    "SashimiBoy.EditorTools.NewFishShopAssetsScenePipeline",
                    "RebuildStreetFishShopFrontageBatch");
                firstRunBytes = File.ReadAllBytes(absoluteScenePath);

                RuntimeReflection.InvokeStatic(
                    "SashimiBoy.EditorTools.NewFishShopAssetsScenePipeline",
                    "RebuildStreetFishShopFrontageBatch");
                secondRunBytes = File.ReadAllBytes(absoluteScenePath);
            }
            finally
            {
                EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Single);
            }

            Assert.That(firstRunBytes, Is.EqualTo(committedBytes),
                "The first generator run changed the committed Street " +
                "scene bytes.");
            Assert.That(secondRunBytes, Is.EqualTo(committedBytes),
                "The second generator run changed the committed Street " +
                "scene bytes.");
        }

        private static void AssertSceneDoorContract(GameObject door)
        {
            Component component = door.GetComponent(
                RuntimeReflection.RuntimeType("SashimiBoy.SceneDoor"));
            Assert.That(component, Is.Not.Null);
            SerializedObject serialized = new SerializedObject(component);
            Assert.That(
                serialized.FindProperty("prompt").stringValue,
                Is.EqualTo("횟집 들어가기"));
            Assert.That(
                serialized.FindProperty("sceneName").stringValue,
                Is.EqualTo("FishShopDialogue"));
            Assert.That(
                serialized.FindProperty("destinationLocation").enumValueIndex,
                Is.EqualTo(2));
        }

        private static void AssertAnchor(
            Scene scene,
            string name,
            Vector3 expectedPosition)
        {
            GameObject anchor = FindNamed(scene, name);
            Assert.That(anchor, Is.Not.Null, name);
            Assert.That(anchor.transform.position, Is.EqualTo(
                expectedPosition));
            Assert.That(
                anchor.GetComponentsInChildren<Renderer>(true),
                Is.Empty,
                name);
            Assert.That(
                anchor.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                name);
        }

        private static Bounds ObjectBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            Assert.That(
                renderers.Length + colliders.Length,
                Is.GreaterThan(0),
                root.name + " has no bounds source.");
            Bounds bounds = renderers.Length > 0
                ? renderers[0].bounds
                : colliders[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            for (int i = renderers.Length > 0 ? 0 : 1;
                 i < colliders.Length;
                 i++)
            {
                bounds.Encapsulate(colliders[i].bounds);
            }

            return bounds;
        }

        private static void AssertNoMissingScripts(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform child in
                         root.GetComponentsInChildren<Transform>(true))
                {
                    Assert.That(
                        GameObjectUtility.
                            GetMonoBehavioursWithMissingScriptCount(
                                child.gameObject),
                        Is.Zero,
                        "Missing Script under " + child.name);
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

        private static T[] Components<T>(Scene scene)
            where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .ToArray();
        }
    }
}
