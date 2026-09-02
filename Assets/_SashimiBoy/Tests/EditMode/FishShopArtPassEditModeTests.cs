using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SashimiBoy.Tests
{
    public sealed class FishShopArtPassEditModeTests
    {
        private const string PrefabRoot =
            "Assets/_SashimiBoy/Art/Generated/Prefabs/FishShop/";
        private const string ScenePath =
            "Assets/_SashimiBoy/Scenes/FishShopDialogue.unity";

        [TestCase("PF_Fish_Salmon.prefab", 1.25f)]
        [TestCase("PF_Fish_Rockfish.prefab", 1.15f)]
        [TestCase("PF_Fish_Mullet.prefab", 1.2f)]
        public void CanonicalFishWrapper_UsesForwardPivotAndScaleContract(
            string prefabName,
            float expectedLongestDimension)
        {
            string path = PrefabRoot + prefabName;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab)
                as GameObject;
            Assert.That(instance, Is.Not.Null);
            try
            {
                Assert.That(instance.transform.localPosition, Is.EqualTo(
                    Vector3.zero));
                Assert.That(instance.transform.localRotation, Is.EqualTo(
                    Quaternion.identity));
                AssertPositiveScale(instance.transform);

                Transform model = instance.transform.Find("Model");
                Assert.That(model, Is.Not.Null);
                Assert.That(
                    Mathf.Abs(Mathf.DeltaAngle(
                        model.localEulerAngles.y,
                        270f)),
                    Is.LessThan(0.01f),
                    "Source +X must be normalized to wrapper +Z forward.");

                Bounds bounds = RendererBounds(instance);
                float longest = Mathf.Max(
                    bounds.size.x,
                    Mathf.Max(bounds.size.y, bounds.size.z));
                Assert.That(
                    longest,
                    Is.EqualTo(expectedLongestDimension).Within(0.015f));
                Assert.That(bounds.min.y, Is.EqualTo(0f).Within(0.01f));
                Assert.That(bounds.center.x, Is.EqualTo(0f).Within(0.01f));
                Assert.That(bounds.center.z, Is.EqualTo(0f).Within(0.01f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void FishShopScene_UsesCanonicalArtAndPreservesGameplayAnchors()
        {
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Additive);
            try
            {
                GameObject artRoot = FindNamed(
                    scene,
                    "Task2_FishShopInteriorAssets");
                Assert.That(artRoot, Is.Not.Null);
                AssertPositiveScale(artRoot.transform);

                AssertPrefabPath(
                    scene,
                    "DisplayInside_Validated",
                    PrefabRoot + "PF_Fixture_DisplayInside.prefab");
                AssertPrefabPath(
                    scene,
                    "SashimiTable_Left_Validated",
                    PrefabRoot + "PF_Fixture_SashimiTable.prefab");
                AssertPrefabPath(
                    scene,
                    "SashimiTable_Right_Validated",
                    PrefabRoot + "PF_Fixture_SashimiTable.prefab");
                AssertPrefabPath(
                    scene,
                    "DisplayFish_Salmon",
                    PrefabRoot + "PF_Fish_Salmon.prefab");
                AssertPrefabPath(
                    scene,
                    "DisplayFish_Rockfish",
                    PrefabRoot + "PF_Fish_Rockfish.prefab");
                AssertPrefabPath(
                    scene,
                    "DisplayFish_Mullet",
                    PrefabRoot + "PF_Fish_Mullet.prefab");

                AssertPosition(scene, "Boss", new Vector3(-2.3f, 1f, 2.7f));
                AssertPosition(scene, "Kevin", new Vector3(2.1f, 1f, -0.6f));
                AssertPosition(
                    scene,
                    "StartStage01_Placeholder",
                    new Vector3(-2.1f, 0.5f, -1.8f));
                AssertPosition(
                    scene,
                    "Door_To_Street",
                    new Vector3(4.8f, 0.8f, -2.8f));
                AssertPosition(
                    scene,
                    "PlayerSpawnPoint",
                    new Vector3(1.5f, 0.6f, -2.3f));

                Assert.That(
                    Components(scene, "SashimiBoy.DialogueTrigger").Length,
                    Is.EqualTo(1));
                Assert.That(
                    Components(
                        scene,
                        "SashimiBoy.StageStarterInteractable").Length,
                    Is.EqualTo(1));
                Assert.That(
                    Components(
                        scene,
                        "SashimiBoy.ReturnToStreetDoor").Length,
                    Is.EqualTo(1));

                string[] legacyVisuals =
                {
                    "Counter",
                    "Cutting_Board",
                    "Placeholder_Fish_Fallback",
                    "Boss",
                    "Kevin",
                    "CounterFront",
                    "CounterTrim",
                    "PrepBench_Left",
                    "PrepBench_Right",
                    "Shelf",
                    "KnifeRail",
                    "Boss_Visual",
                    "Kevin_Visual",
                };
                for (int i = 0; i < legacyVisuals.Length; i++)
                {
                    AssertNoEnabledRenderer(scene, legacyVisuals[i]);
                }

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
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void RebuildFishShopArtPass_FirstAndSecondRun_KeepSceneBytes()
        {
            string absoluteScenePath = AssetPathToAbsolutePath(ScenePath);
            byte[] committedBytes = File.ReadAllBytes(absoluteScenePath);
            byte[] firstRunBytes = null;
            byte[] secondRunBytes = null;

            try
            {
                RuntimeReflection.InvokeStatic(
                    "SashimiBoy.EditorTools.NewFishShopAssetsScenePipeline",
                    "RebuildFishShopArtPassBatch");
                firstRunBytes = File.ReadAllBytes(absoluteScenePath);

                RuntimeReflection.InvokeStatic(
                    "SashimiBoy.EditorTools.NewFishShopAssetsScenePipeline",
                    "RebuildFishShopArtPassBatch");
                secondRunBytes = File.ReadAllBytes(absoluteScenePath);
            }
            finally
            {
                try
                {
                    EditorSceneManager.NewScene(
                        NewSceneSetup.EmptyScene,
                        NewSceneMode.Single);
                }
                finally
                {
                    RestoreSceneBytes(
                        absoluteScenePath,
                        committedBytes);
                }
            }

            bool firstRunMatches = firstRunBytes != null &&
                committedBytes.SequenceEqual(firstRunBytes);
            bool secondRunMatches = secondRunBytes != null &&
                committedBytes.SequenceEqual(secondRunBytes);
            Assert.That(
                firstRunMatches && secondRunMatches,
                Is.True,
                "The authoritative generator rewrote " + ScenePath +
                ". First run clean: " + firstRunMatches +
                "; second run clean: " + secondRunMatches + ".");
        }

        private static void AssertPrefabPath(
            Scene scene,
            string objectName,
            string expectedPath)
        {
            GameObject instance = FindNamed(scene, objectName);
            Assert.That(instance, Is.Not.Null, objectName);
            Assert.That(
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance),
                Is.EqualTo(expectedPath));
        }

        private static void AssertPosition(
            Scene scene,
            string objectName,
            Vector3 expected)
        {
            GameObject target = FindNamed(scene, objectName);
            Assert.That(target, Is.Not.Null, objectName);
            Assert.That(target.transform.position, Is.EqualTo(expected));
        }

        private static void AssertNoEnabledRenderer(
            Scene scene,
            string objectName)
        {
            GameObject target = FindNamed(scene, objectName);
            Assert.That(target, Is.Not.Null, objectName);
            Assert.That(
                target.GetComponentsInChildren<Renderer>(true)
                    .Any(renderer => renderer.enabled),
                Is.False,
                objectName + " is still visibly rendered.");
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

        private static Bounds RendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            Bounds result = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                result.Encapsulate(renderers[i].bounds);
            }

            return result;
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

        private static void RestoreSceneBytes(
            string absoluteScenePath,
            byte[] expectedBytes)
        {
            if (File.Exists(absoluteScenePath) &&
                File.ReadAllBytes(absoluteScenePath)
                    .SequenceEqual(expectedBytes))
            {
                return;
            }

            File.WriteAllBytes(absoluteScenePath, expectedBytes);
            AssetDatabase.ImportAsset(
                ScenePath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
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
