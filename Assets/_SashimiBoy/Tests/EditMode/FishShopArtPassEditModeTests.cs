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
                    new Vector3(-4f, 0.5f, 2.7f));
                AssertPosition(
                    scene,
                    "Door_To_Street",
                    new Vector3(0f, 0.8f, -3.9f));
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

        [TestCase("Salmon")]
        [TestCase("Rockfish")]
        [TestCase("Mullet")]
        public void PackedMaskReadback_PreservesSourceFilesAndExistingPackedPixels(
            string fish)
        {
            string textureRoot = "Assets/_SashimiBoy/Art/Source/Environment/" +
                "FishShop/Fish/" + fish + "/Textures";
            string[] sources = AssetDatabase.FindAssets("t:Texture2D", new[] { textureRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => Path.GetFileNameWithoutExtension(path).EndsWith(
                    "_metallic", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileNameWithoutExtension(path).EndsWith(
                        "_roughness", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            Assert.That(sources.Length, Is.EqualTo(2));
            string[] protectedPaths = sources.SelectMany(path => new[] { path, path + ".meta" })
                .Select(AssetPathToAbsolutePath).ToArray();
            byte[][] protectedBytes = protectedPaths.Select(File.ReadAllBytes).ToArray();
            FileAttributes[] protectedAttributes = protectedPaths.Select(File.GetAttributes).ToArray();
            string[] importerSettings = sources.Select(path =>
                EditorJsonUtility.ToJson(AssetImporter.GetAtPath(path))).ToArray();
            string generatedRoot = AssetPathToAbsolutePath("Assets/_SashimiBoy/Art/Generated");
            Assert.That(Directory.GetDirectories(generatedRoot, "__PackedMapReadback_*"), Is.Empty);

            Texture2D expected = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                string packedPath = "Assets/_SashimiBoy/Art/Generated/PackedMaps/FishShop/" +
                    "MS_FishShop_" + fish + "_MetallicSmoothness.png";
                Assert.That(expected.LoadImage(File.ReadAllBytes(
                    AssetPathToAbsolutePath(packedPath)), false), Is.True);
                Color32[] packed = expected.GetPixels32();
                for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
                {
                    string path = sources[sourceIndex];
                    Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    Assert.That(source.isReadable, Is.False,
                        "Exercise the protected, non-readable source path: " + path);
                    Color32[] actual = (Color32[])RuntimeReflection.InvokeStatic(
                        "SashimiBoy.EditorTools.NewAssetsKevinCameraPipeline",
                        "ReadPixelsAtSize", source, expected.width, expected.height);
                    Assert.That(actual.Length, Is.EqualTo(packed.Length), path);
                    bool metallic = Path.GetFileNameWithoutExtension(path).EndsWith(
                        "_metallic", StringComparison.OrdinalIgnoreCase);
                    for (int pixel = 0; pixel < actual.Length; pixel++)
                    {
                        byte expectedChannel = metallic ? packed[pixel].r :
                            (byte)(255 - packed[pixel].a);
                        if (actual[pixel].r != expectedChannel)
                        {
                            Assert.Fail(path + " pixel " + pixel + ": expected " +
                                expectedChannel + ", actual " + actual[pixel].r);
                        }
                    }

                    Assert.That(source.isReadable, Is.False, path);
                    Assert.That(EditorJsonUtility.ToJson(AssetImporter.GetAtPath(path)),
                        Is.EqualTo(importerSettings[sourceIndex]), path + " importer changed");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(expected);
                for (int i = 0; i < protectedPaths.Length; i++)
                {
                    Assert.That(File.ReadAllBytes(protectedPaths[i]),
                        Is.EqualTo(protectedBytes[i]), protectedPaths[i]);
                    Assert.That(File.GetAttributes(protectedPaths[i]),
                        Is.EqualTo(protectedAttributes[i]), protectedPaths[i] + " attributes");
                }

                Assert.That(Directory.GetDirectories(generatedRoot, "__PackedMapReadback_*"),
                    Is.Empty, "Temporary readback assets must be removed, including on failure.");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PackedMaskReadback_ReadOnlyCompressedFixture_PreservesCpuPixels(
            bool srgb)
        {
            const string generatedPath = "Assets/_SashimiBoy/Art/Generated";
            string name = "__PackedMapReadbackFixture_" + Guid.NewGuid().ToString("N");
            string folder = generatedPath + "/" + name;
            string ownedGuid = AssetDatabase.CreateFolder(generatedPath, name);
            Assert.That(ownedGuid, Is.Not.Empty);
            string path = folder + "/Mask.png";
            string absolute = AssetPathToAbsolutePath(path);
            string[] ownedFiles = { absolute, absolute + ".meta" };
            Texture2D image = new Texture2D(64, 64, TextureFormat.RGBA32, false, true);
            try
            {
                Color32[] pixels = new Color32[64 * 64];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = new Color32((byte)(i * 17), (byte)(i / 7), (byte)(i / 29), 255);
                }

                image.SetPixels32(pixels);
                image.Apply(false, false);
                File.WriteAllBytes(absolute, image.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.sRGBTexture = srgb;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 32;
                importer.textureCompression = TextureImporterCompression.Compressed;
                TextureImporterPlatformSettings platform =
                    importer.GetPlatformTextureSettings("Standalone");
                platform.overridden = true;
                platform.format = TextureImporterFormat.DXT1;
                platform.maxTextureSize = 32;
                importer.SetPlatformTextureSettings(platform);
                importer.isReadable = true;
                importer.SaveAndReimport();
                Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.That(imported.width, Is.EqualTo(32), "Retain importer max-size behavior.");
                Assert.That(imported.format, Is.EqualTo(TextureFormat.DXT1),
                    "Compare compressed imported pixels, rather than raw PNG decode.");
                Color32[] expected = imported.GetPixels32();
                importer.isReadable = false;
                importer.SaveAndReimport();
                imported = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                string settings = EditorJsonUtility.ToJson(importer);
                foreach (string file in ownedFiles)
                {
                    File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
                }

                byte[][] before = ownedFiles.Select(File.ReadAllBytes).ToArray();
                FileAttributes[] attributes = ownedFiles.Select(File.GetAttributes).ToArray();
                try
                {
                    Color32[] actual = (Color32[])RuntimeReflection.InvokeStatic(
                        "SashimiBoy.EditorTools.NewAssetsKevinCameraPipeline",
                        "ReadPixelsAtSize", imported, imported.width, imported.height);
                    Assert.That(actual, Is.EqualTo(expected),
                        "Readback must equal legacy readable CPU decoding, including sRGB.");
                    Assert.That(imported.isReadable, Is.False);
                    Assert.That(EditorJsonUtility.ToJson(AssetImporter.GetAtPath(path)),
                        Is.EqualTo(settings));
                }
                finally
                {
                    for (int i = 0; i < ownedFiles.Length; i++)
                    {
                        Assert.That(File.ReadAllBytes(ownedFiles[i]), Is.EqualTo(before[i]));
                        Assert.That(File.GetAttributes(ownedFiles[i]), Is.EqualTo(attributes[i]));
                    }

                    Assert.That(Directory.GetDirectories(AssetPathToAbsolutePath(generatedPath),
                        "__PackedMapReadback_*"), Is.Empty);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(image);
                string generatedAbsolute = Path.GetFullPath(AssetPathToAbsolutePath(generatedPath));
                string fixtureAbsolute = Path.GetFullPath(AssetPathToAbsolutePath(folder));
                if (!string.Equals(Path.GetDirectoryName(fixtureAbsolute), generatedAbsolute,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(fixtureAbsolute, Path.Combine(generatedAbsolute, name),
                        StringComparison.OrdinalIgnoreCase) ||
                    AssetDatabase.AssetPathToGUID(folder) != ownedGuid ||
                    (File.GetAttributes(fixtureAbsolute) & FileAttributes.ReparsePoint) != 0 ||
                    Directory.EnumerateFileSystemEntries(fixtureAbsolute).Any(file =>
                        (File.GetAttributes(file) &
                            (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0))
                {
                    throw new IOException("Readback fixture ownership/path validation failed.");
                }

                foreach (string file in ownedFiles.Where(File.Exists))
                {
                    File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
                }

                Assert.That(AssetDatabase.DeleteAsset(folder), Is.True);
            }
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
