using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SashimiBoy.Tests
{
    public sealed class ClubArtPassEditModeTests
    {
        private const string PrefabRoot =
            "Assets/_SashimiBoy/Art/Generated/Prefabs/Club/";
        private const string ScenePath =
            "Assets/_SashimiBoy/Scenes/Club.unity";

        [Test]
        public void ClubScene_UsesCanonicalZonesAndPreservesGameplayAnchors()
        {
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Additive);
            try
            {
                GameObject artRoot = FindNamed(scene, "ClubArtRoot");
                Assert.That(artRoot, Is.Not.Null);
                AssertPositiveScale(artRoot.transform);

                Assert.That(
                    artRoot.transform.Find("Architecture/Entrance"),
                    Is.Not.Null);
                Assert.That(
                    artRoot.transform.Find("Architecture/DJArea"),
                    Is.Not.Null);
                Assert.That(
                    artRoot.transform.Find("Architecture/BarArea"),
                    Is.Not.Null);

                AssertPrefabPath(
                    scene,
                    "DoorFrame_PF_Club_ClubDoorFrame",
                    PrefabRoot + "PF_Club_ClubDoorFrame.prefab");
                AssertPrefabPath(
                    scene,
                    "DJStand_PF_Club_DJStand",
                    PrefabRoot + "PF_Club_DJStand.prefab");
                AssertPrefabPath(
                    scene,
                    "DJController_PF_Club_DJController",
                    PrefabRoot + "PF_Club_DJController.prefab");
                AssertPrefabPath(
                    scene,
                    "DJMixer_PF_Club_DJMixer",
                    PrefabRoot + "PF_Club_DJMixer.prefab");
                AssertPrefabPath(
                    scene,
                    "NeonSign_PF_Club_NeonSign",
                    PrefabRoot + "PF_Club_NeonSign.prefab");
                AssertPrefabPath(
                    scene,
                    "SignBoard_PF_Club_SignBoard",
                    PrefabRoot + "PF_Club_SignBoard.prefab");
                AssertPrefabPath(
                    scene,
                    "BarTable_01_PF_Club_BarTable",
                    PrefabRoot + "PF_Club_BarTable.prefab");

                Transform[] barTables = artRoot
                    .GetComponentsInChildren<Transform>(true)
                    .Where(item =>
                        item.name.StartsWith(
                            "BarTable_",
                            StringComparison.Ordinal))
                    .ToArray();
                Assert.That(barTables, Has.Length.EqualTo(4));

                Transform[] audienceTables = artRoot
                    .GetComponentsInChildren<Transform>(true)
                    .Where(item =>
                        item.name.StartsWith(
                            "Table_",
                            StringComparison.Ordinal))
                    .ToArray();
                Assert.That(audienceTables, Has.Length.EqualTo(6));

                AssertAnchorMatches(
                    scene,
                    "PlayerSpawn_Anchor",
                    "Kevin_Player");
                AssertAnchorMatches(
                    scene,
                    "PerformanceGate_Anchor",
                    "PerformanceGate");
                AssertAnchorMatches(
                    scene,
                    "ReturnDoor_Anchor",
                    "Door_To_Street");
                AssertPosition(
                    scene,
                    "Kevin_Player",
                    new Vector3(0f, 0.6f, -4f));
                AssertPosition(
                    scene,
                    "PerformanceGate",
                    new Vector3(0f, 0.6f, 1.8f));
                AssertPosition(
                    scene,
                    "Door_To_Street",
                    new Vector3(6.4f, 0.8f, -4.5f));

                Assert.That(
                    Components(scene, "SashimiBoy.ClubController").Length,
                    Is.EqualTo(1));
                Assert.That(
                    Components(scene, "SashimiBoy.ClubPerformanceGate").Length,
                    Is.EqualTo(1));
                Assert.That(
                    Components(scene, "SashimiBoy.ReturnToStreetDoor").Length,
                    Is.EqualTo(1));

                string[] hiddenLegacyVisuals =
                {
                    "Label_PerformanceGate",
                    "Label_ReturnDoor",
                    "PerformanceGate",
                    "DJ_Booth",
                    "Bar",
                };
                for (int i = 0; i < hiddenLegacyVisuals.Length; i++)
                {
                    AssertNoEnabledRenderer(scene, hiddenLegacyVisuals[i]);
                }

                AssertNoEnabledCollider(scene, "DJ_Booth");
                AssertNoEnabledCollider(scene, "Bar");
                AssertGeneratedRendererSources(artRoot);
                AssertProtectedRoutesAreClear(artRoot);
                AssertNoMissingScripts(scene);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void ClubBar_DrinkClustersRestOnCanonicalTables()
        {
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Additive);
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    Transform table = FindNamed(
                            scene,
                            $"BarTable_{i + 1:00}_PF_Club_BarTable")
                        .transform;
                    string[] clusterNames =
                    {
                        "Bar_BeerPair",
                        "Bar_BucketBeer",
                        "Bar_CocktailIce",
                        "Bar_MixedDrinks",
                    };
                    Transform cluster = FindNamed(scene, clusterNames[i])
                        .transform;
                    float tableTop = RendererBounds(table.gameObject).max.y;
                    float clusterBottom =
                        RendererBounds(cluster.gameObject).min.y;
                    Assert.That(
                        clusterBottom,
                        Is.EqualTo(tableTop).Within(0.02f),
                        clusterNames[i] + " is not grounded on its table.");
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void RebuildClubArtPass_FirstAndSecondRun_KeepSceneBytes()
        {
            string absoluteScenePath = AssetPathToAbsolutePath(ScenePath);
            byte[] committedBytes = File.ReadAllBytes(absoluteScenePath);
            string committedHash = ComputeSha256(committedBytes);
            byte[] firstRunBytes = null;
            byte[] secondRunBytes = null;

            try
            {
                RuntimeReflection.InvokeStatic(
                    "SashimiBoy.EditorTools.ClubArtPassPipeline",
                    "ApplyClubArtToMainSceneBatch");
                firstRunBytes = File.ReadAllBytes(absoluteScenePath);

                RuntimeReflection.InvokeStatic(
                    "SashimiBoy.EditorTools.ClubArtPassPipeline",
                    "ApplyClubArtToMainSceneBatch");
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
                "The first generator run changed the committed Club scene " +
                "SHA-256.");
            Assert.That(
                firstRunBytes,
                Is.EqualTo(committedBytes),
                "The first generator run changed the committed Club scene " +
                "bytes.");
            Assert.That(
                ComputeSha256(secondRunBytes),
                Is.EqualTo(committedHash),
                "The second generator run changed the committed Club scene " +
                "SHA-256.");
            Assert.That(
                secondRunBytes,
                Is.EqualTo(committedBytes),
                "The second generator run changed the committed Club scene " +
                "bytes.");
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

        private static void AssertNoEnabledCollider(
            Scene scene,
            string objectName)
        {
            GameObject target = FindNamed(scene, objectName);
            Assert.That(target, Is.Not.Null, objectName);
            Assert.That(
                target.GetComponentsInChildren<Collider>(true)
                    .Any(collider => collider.enabled),
                Is.False,
                objectName + " still blocks the Club route.");
        }

        private static void AssertGeneratedRendererSources(GameObject artRoot)
        {
            foreach (Renderer renderer in
                     artRoot.GetComponentsInChildren<Renderer>(true))
            {
                GameObject outermost =
                    PrefabUtility.GetOutermostPrefabInstanceRoot(
                        renderer.gameObject);
                string prefabPath =
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        outermost);
                Assert.That(
                    prefabPath.StartsWith(
                        PrefabRoot,
                        StringComparison.Ordinal),
                    Is.True,
                    renderer.name + " is not sourced from a canonical " +
                    "Club wrapper prefab: " + prefabPath);
            }
        }

        private static void AssertProtectedRoutesAreClear(GameObject artRoot)
        {
            Physics.SyncTransforms();
            Bounds[] protectedRoutes =
            {
                new Bounds(
                    new Vector3(0f, 1.5f, -1.25f),
                    new Vector3(2.4f, 3f, 5.5f)),
                new Bounds(
                    new Vector3(0f, 1.5f, 1.9f),
                    new Vector3(3f, 3f, 1.4f)),
                new Bounds(
                    new Vector3(6.4f, 1.5f, -4.1f),
                    new Vector3(2.4f, 3f, 3f)),
            };

            foreach (Collider collider in
                     artRoot.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.enabled ||
                    !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Assert.That(
                    protectedRoutes.Any(route =>
                        collider.bounds.Intersects(route)),
                    Is.False,
                    collider.name + " blocks a protected Club route.");
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
