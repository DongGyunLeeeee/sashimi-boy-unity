using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SashimiBoy.Tests
{
    public sealed class SalmonAssemblyEditModeTests
    {
        private const string SourceRoot =
            "Assets/_SashimiBoy/Art/Source/Stage01/SalmonButchery";
        private const string PinBoneSourceRoot =
            "Assets/_SashimiBoy/Art/Source/Shared/FishButchery/PinBone";
        private const string PrefabRoot =
            "Assets/_SashimiBoy/Art/Generated/Prefabs/Stage01/SalmonButchery";
        private const string SharedPrefabRoot =
            "Assets/_SashimiBoy/Art/Generated/Prefabs/Shared/FishButchery";
        private const string MaterialRoot =
            "Assets/_SashimiBoy/Art/Generated/Materials/Stage01/SalmonButchery";
        private const string SharedMaterialRoot =
            "Assets/_SashimiBoy/Art/Generated/Materials/Shared/FishButchery";
        private const string AssemblyPath =
            PrefabRoot + "/PF_Stage01_SalmonAssembly.prefab";
        private const string ManifestPath =
            "Assets/_SashimiBoy/Art/Generated/Reports/" +
            "Stage01SalmonButcherySourceManifest.csv";
        private const string RoleMapPath =
            "Assets/_SashimiBoy/Art/Generated/Reports/" +
            "Stage01SalmonButcherySourceRoleMap.md";
        private const string InventoryPath =
            "Assets/_SashimiBoy/Art/Generated/Reports/" +
            "Stage01SalmonAssemblyInventory.md";
        private const string StageScenePath =
            "Assets/_SashimiBoy/Scenes/Stage01_Salmon.unity";
        private const string FallbackPath =
            "Assets/_SashimiBoy/Art/Generated/Prefabs/Stage01/" +
            "PF_Stage01_ProceduralSalmon.prefab";

        [Test]
        public void SourceManifest_AllImportedBytesAndGuidsArePreserved()
        {
            string[] lines = File.ReadAllLines(
                AssetPathToAbsolutePath(ManifestPath));
            HashSet<string> guids = new HashSet<string>(StringComparer.Ordinal);
            int validated = 0;

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                string[] columns = ParseManifestRow(lines[i]);
                string assetPath = ImportedPath(columns[0]);
                string absolutePath = AssetPathToAbsolutePath(assetPath);
                Assert.That(File.Exists(absolutePath), Is.True, assetPath);
                Assert.That(
                    new FileInfo(absolutePath).Length,
                    Is.EqualTo(long.Parse(
                        columns[1],
                        CultureInfo.InvariantCulture)),
                    assetPath);
                Assert.That(
                    ComputeSha256(absolutePath),
                    Is.EqualTo(columns[2]).IgnoreCase,
                    assetPath);

                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                Assert.That(guid, Is.Not.Empty, assetPath);
                Assert.That(guids.Add(guid), Is.True, "Duplicate source GUID: " + guid);
                validated++;
            }

            Assert.That(validated, Is.EqualTo(37));
        }

        [TestCase("Head", 0.84f)]
        [TestCase("Body", 2.40f)]
        [TestCase("Fins", 0.62f)]
        [TestCase("Spine", 2.15f)]
        [TestCase("Fillet", 2.15f)]
        public void SalmonWrapper_UsesCanonicalForwardPivotScaleAndMaterial(
            string id,
            float targetLargestDimension)
        {
            AssertWrapper(
                PrefabRoot + "/PF_Salmon_" + id + ".prefab",
                MaterialRoot,
                targetLargestDimension);
        }

        [Test]
        public void SharedPinBoneWrapper_UsesCanonicalForwardPivotScaleAndMaterial()
        {
            AssertWrapper(
                SharedPrefabRoot + "/PF_PinBone.prefab",
                SharedMaterialRoot,
                0.14f);
        }

        [Test]
        public void SalmonAssembly_ExposesStablePiecesAnchorsAndFallback()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssemblyPath);
            Assert.That(prefab, Is.Not.Null, AssemblyPath);
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            Assert.That(instance, Is.Not.Null);

            try
            {
                Component view = instance.GetComponent(
                    RuntimeReflection.RuntimeType("SashimiBoy.SalmonAssemblyView"));
                Assert.That(view, Is.Not.Null);
                AssertPositiveScale(instance.transform);
                Component head = FieldComponent(view, "head");
                Component body = FieldComponent(view, "body");
                Component fins = FieldComponent(view, "fins");
                Component spine = FieldComponent(view, "spine");
                Component fillet = FieldComponent(view, "fillet");
                Component[] pinBones = FieldComponents(view, "pinBones");
                AssertPiece(head, "Head", "Head", true);
                AssertPiece(body, "Body", "Body", true);
                AssertPiece(fins, "Fins", "Fins", false);
                AssertPiece(spine, "Spine", "Spine", false);
                AssertPiece(fillet, "Fillet", "Fillet", false);
                Assert.That(pinBones, Has.Length.EqualTo(8));
                for (int i = 0; i < pinBones.Length; i++)
                {
                    AssertPiece(
                        pinBones[i],
                        "PinBone." + i.ToString("00"),
                        "PinBone",
                        false);
                }

                string[] ids = Property<IEnumerable>(view, "Pieces")
                    .Cast<object>()
                    .Select(piece => Property<string>(piece, "StableId"))
                    .ToArray();
                Assert.That(ids, Has.Length.EqualTo(13));
                Assert.That(ids.Distinct(StringComparer.Ordinal).Count(),
                    Is.EqualTo(ids.Length));
                Assert.That(
                    RuntimeReflection.Invoke(view, "FindPiece", "PinBone.07"),
                    Is.SameAs(pinBones[7]));
                Assert.That(
                    RuntimeReflection.Invoke(view, "FindPiece", "Unknown"),
                    Is.Null);

                AssertAnchor(FieldTransform(view, "headAnchor"), "HeadAnchor", head.transform);
                AssertAnchor(FieldTransform(view, "bodyAnchor"), "BodyAnchor", body.transform);
                AssertAnchor(FieldTransform(view, "finsAnchor"), "FinsAnchor", fins.transform);
                AssertAnchor(FieldTransform(view, "spineAnchor"), "SpineAnchor", spine.transform);
                AssertAnchor(FieldTransform(view, "filletAnchor"), "FilletAnchor", fillet.transform);
                Transform[] pinBoneAnchors = FieldTransforms(view, "pinBoneAnchors");
                Assert.That(pinBoneAnchors, Has.Length.EqualTo(8));
                for (int i = 0; i < pinBoneAnchors.Length; i++)
                {
                    AssertAnchor(
                        pinBoneAnchors[i],
                        "PinBoneAnchor_" + i.ToString("00"),
                        pinBones[i].transform);
                }

                AssertNamedAnchor(FieldTransform(view, "knifeAttachmentAnchor"), "KnifeAttachmentAnchor");
                AssertNamedAnchor(FieldTransform(view, "handAttachmentAnchor"), "HandAttachmentAnchor");
                AssertNamedAnchor(FieldTransform(view, "pinBoneWorkAnchor"), "PinBoneWorkAnchor");
                AssertNamedAnchor(FieldTransform(view, "sashimiOutputAnchor"), "SashimiOutputAnchor");
                AssertNamedAnchor(FieldTransform(view, "plateOutputAnchor"), "PlateOutputAnchor");
                Assert.That(
                    AssetDatabase.GetAssetPath(
                        RuntimeReflection.GetField(
                            view,
                            "proceduralSalmonFallbackPrefab") as GameObject),
                    Is.EqualTo(FallbackPath));

                Bounds bodyBounds = RendererBounds(body.gameObject);
                Bounds headBounds = RendererBounds(head.gameObject);
                Assert.That(
                    bodyBounds.max.z - headBounds.min.z,
                    Is.GreaterThan(0.15f),
                    "Head and Body do not overlap enough to close the authored seam.");
                Assert.That(
                    Mathf.Abs(bodyBounds.center.x - headBounds.center.x),
                    Is.LessThan(0.02f));
                Assert.That(
                    Mathf.Abs(bodyBounds.center.y - headBounds.center.y),
                    Is.LessThan(0.04f));
                AssertNoMissingScripts(instance);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void StageScene_DoesNotDirectlyReferenceIssue20RawModels()
        {
            string sceneYaml = File.ReadAllText(
                AssetPathToAbsolutePath(StageScenePath));
            string[] modelPaths =
            {
                SourceRoot + "/Head/Models/Salmon_Head.fbx",
                SourceRoot + "/Body/Models/Salmon_Body.fbx",
                SourceRoot + "/Fins/Models/Salmon_Fins.fbx",
                SourceRoot + "/Spine/Models/Salmon_Spine.fbx",
                SourceRoot + "/Fillet/Models/Salmon_Fillet.fbx",
                PinBoneSourceRoot + "/Models/PinBone.fbx",
            };
            foreach (string modelPath in modelPaths)
            {
                string guid = AssetDatabase.AssetPathToGUID(modelPath);
                Assert.That(guid, Is.Not.Empty, modelPath);
                Assert.That(
                    sceneYaml.Contains("guid: " + guid),
                    Is.False,
                    "Stage01 directly references raw FBX: " + modelPath);
            }

            Assert.That(
                AssetDatabase.LoadAssetAtPath<GameObject>(FallbackPath),
                Is.Not.Null,
                "The explicit procedural fallback was removed.");
        }

        [Test]
        public void AssetMetaFiles_ContainUniqueGuids()
        {
            Dictionary<string, string> ownerByGuid =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string assetPath in AssetDatabase.GetAllAssetPaths()
                         .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal)))
            {
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                Assert.That(guid, Is.Not.Empty, assetPath);
                string previous;
                Assert.That(
                    ownerByGuid.TryGetValue(guid, out previous),
                    Is.False,
                    "Duplicate GUID " + guid + ": " + previous + " and " + assetPath);
                ownerByGuid.Add(guid, assetPath);
            }
        }

        [Test]
        public void RebuildSalmonAssembly_FirstAndSecondRunKeepGeneratedBytes()
        {
            string[] assetPaths =
            {
                AssemblyPath,
                PrefabRoot + "/PF_Salmon_Head.prefab",
                PrefabRoot + "/PF_Salmon_Body.prefab",
                PrefabRoot + "/PF_Salmon_Fins.prefab",
                PrefabRoot + "/PF_Salmon_Spine.prefab",
                PrefabRoot + "/PF_Salmon_Fillet.prefab",
                SharedPrefabRoot + "/PF_PinBone.prefab",
                InventoryPath,
            };
            Dictionary<string, byte[]> committed = assetPaths.ToDictionary(
                path => path,
                path => File.ReadAllBytes(AssetPathToAbsolutePath(path)));

            RuntimeReflection.InvokeStatic(
                "SashimiBoy.EditorTools.SalmonButcheryArtPipeline",
                "BuildSalmonAssemblyBatch");
            foreach (string path in assetPaths)
            {
                Assert.That(
                    File.ReadAllBytes(AssetPathToAbsolutePath(path)),
                    Is.EqualTo(committed[path]),
                    "First generator run changed " + path);
            }

            RuntimeReflection.InvokeStatic(
                "SashimiBoy.EditorTools.SalmonButcheryArtPipeline",
                "BuildSalmonAssemblyBatch");
            foreach (string path in assetPaths)
            {
                Assert.That(
                    File.ReadAllBytes(AssetPathToAbsolutePath(path)),
                    Is.EqualTo(committed[path]),
                    "Second generator run changed " + path);
            }
        }

        private static void AssertWrapper(
            string prefabPath,
            string expectedMaterialRoot,
            float targetLargestDimension)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, prefabPath);
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            Assert.That(instance, Is.Not.Null);
            try
            {
                Assert.That(instance.transform.position, Is.EqualTo(Vector3.zero));
                Assert.That(instance.transform.rotation, Is.EqualTo(Quaternion.identity));
                Assert.That(instance.transform.localScale, Is.EqualTo(Vector3.one));
                AssertPositiveScale(instance.transform);
                Transform model = instance.transform.Find("Model");
                Assert.That(model, Is.Not.Null);
                Assert.That(
                    Mathf.Abs(Mathf.DeltaAngle(model.localEulerAngles.y, 270f)),
                    Is.LessThan(0.01f),
                    "Source +X must become canonical wrapper +Z forward.");

                Bounds bounds = RendererBounds(instance);
                float longest = Mathf.Max(
                    bounds.size.x,
                    Mathf.Max(bounds.size.y, bounds.size.z));
                Assert.That(
                    longest,
                    Is.EqualTo(targetLargestDimension).Within(0.015f));
                Assert.That(bounds.min.y, Is.EqualTo(0f).Within(0.01f));
                Assert.That(bounds.center.x, Is.EqualTo(0f).Within(0.01f));
                Assert.That(bounds.center.z, Is.EqualTo(0f).Within(0.01f));

                foreach (Renderer renderer in
                         instance.GetComponentsInChildren<Renderer>(true))
                {
                    Assert.That(renderer.sharedMaterials, Is.Not.Empty);
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Assert.That(material, Is.Not.Null, renderer.name);
                        Assert.That(
                            AssetDatabase.GetAssetPath(material).StartsWith(
                                expectedMaterialRoot,
                                StringComparison.Ordinal),
                            Is.True,
                            renderer.name);
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void AssertPiece(
            Component piece,
            string id,
            string role,
            bool initiallyVisible)
        {
            Assert.That(piece, Is.Not.Null, id);
            Assert.That(Property<string>(piece, "StableId"), Is.EqualTo(id));
            Assert.That(Property<object>(piece, "Role").ToString(), Is.EqualTo(role));
            Assert.That(
                Property<bool>(piece, "InitiallyVisible"),
                Is.EqualTo(initiallyVisible));
            Assert.That(
                Property<bool>(piece, "IsVisible"),
                Is.EqualTo(initiallyVisible));
            Assert.That(Property<bool>(piece, "IsAttached"), Is.True);
            Assert.That(piece.transform.childCount, Is.EqualTo(1));
            string expectedPath = role == "PinBone"
                ? SharedPrefabRoot + "/PF_PinBone.prefab"
                : PrefabRoot + "/PF_Salmon_" + id + ".prefab";
            Assert.That(
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    piece.transform.GetChild(0).gameObject),
                Is.EqualTo(expectedPath));
        }

        private static Component FieldComponent(object target, string name)
        {
            return RuntimeReflection.GetField(target, name) as Component;
        }

        private static Component[] FieldComponents(object target, string name)
        {
            return ((Array)RuntimeReflection.GetField(target, name))
                .Cast<Component>()
                .ToArray();
        }

        private static Transform FieldTransform(object target, string name)
        {
            return RuntimeReflection.GetField(target, name) as Transform;
        }

        private static Transform[] FieldTransforms(object target, string name)
        {
            return ((Array)RuntimeReflection.GetField(target, name))
                .Cast<Transform>()
                .ToArray();
        }

        private static T Property<T>(object target, string name)
        {
            PropertyInfo property = target.GetType().GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null, target.GetType().FullName + "." + name);
            return (T)property.GetValue(target);
        }

        private static void AssertAnchor(
            Transform anchor,
            string name,
            Transform piece)
        {
            AssertNamedAnchor(anchor, name);
            Assert.That(anchor.position, Is.EqualTo(piece.position));
            Assert.That(anchor.rotation, Is.EqualTo(piece.rotation));
        }

        private static void AssertNamedAnchor(Transform anchor, string name)
        {
            Assert.That(anchor, Is.Not.Null, name);
            Assert.That(anchor.name, Is.EqualTo(name));
            Assert.That(anchor.localScale, Is.EqualTo(Vector3.one));
        }

        private static void AssertNoMissingScripts(GameObject root)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                        child.gameObject),
                    Is.Zero,
                    child.name);
            }
        }

        private static void AssertPositiveScale(Transform root)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(child.localScale.x, Is.GreaterThan(0f), child.name);
                Assert.That(child.localScale.y, Is.GreaterThan(0f), child.name);
                Assert.That(child.localScale.z, Is.GreaterThan(0f), child.name);
            }
        }

        private static Bounds RendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty, root.name);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static string[] ParseManifestRow(string line)
        {
            string trimmed = line.Trim();
            Assert.That(trimmed.StartsWith("\"", StringComparison.Ordinal), Is.True);
            Assert.That(trimmed.EndsWith("\"", StringComparison.Ordinal), Is.True);
            string[] columns = trimmed.Substring(1, trimmed.Length - 2)
                .Split(new[] { "\",\"" }, StringSplitOptions.None);
            Assert.That(columns, Has.Length.EqualTo(3));
            return columns;
        }

        private static string ImportedPath(string relativePath)
        {
            string normalized = relativePath.Replace('\\', '/');
            if (normalized == "ASSET_ROLE_MAP.md")
            {
                return RoleMapPath;
            }

            const string sharedPrefix = "Shared/PinBone/";
            return normalized.StartsWith(sharedPrefix, StringComparison.Ordinal)
                ? PinBoneSourceRoot + "/" + normalized.Substring(sharedPrefix.Length)
                : SourceRoot + "/" + normalized;
        }

        private static string AssetPathToAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
            {
                throw new InvalidOperationException("Unity project root is missing.");
            }

            return Path.Combine(
                projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ComputeSha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(stream))
                    .Replace("-", string.Empty);
            }
        }
    }
}
