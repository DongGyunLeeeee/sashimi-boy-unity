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
using Object = UnityEngine.Object;

namespace SashimiBoy.Tests
{
    public sealed class FlounderAssemblyEditModeTests
    {
        private const string Root = "Assets/_SashimiBoy/Art/Generated/Stage03Flounder";
        private const string Source = "Assets/_SashimiBoy/Art/Source/DayWorld/Fish/Stage03";
        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        private static readonly string[] Roles = { "WholeFish", "HeadRemovedFish", "FilletRemoved1", "FilletRemoved2", "FilletRemoved3", "FilletRemoved4", "FishHead", "FrontFillet", "BackFillet", "SashimiSlice" };
        [Serializable] private sealed class Report { public Vector3 wholeFootprint; public Entry[] models; }
        [Serializable] private sealed class Entry
        {
            public string role, sourcePath, sourceDependencyHash, meshPath, generatedMeshSha256, materialPath, wrapperPath;
            public string baseColorPath, normalSourcePath, metallicSourcePath, roughnessSourcePath;
            public int sourceVertices, derivedVertices, vertexBudget;
            public float relativeP99Error, relativeMaximumError, maximumBoundsDeviation;
            public Vector3 derivedSize, derivedCenter, positiveScale, orientedSize;
            public bool sourceUnchanged;
        }
        private static Report ReadReport()
        {
            string path = Absolute(Root + "/Stage03FlounderAuthoring.json");
            Assert.That(File.Exists(path), Is.True);
            var report = JsonUtility.FromJson<Report>(File.ReadAllText(path));
            Assert.That(report, Is.Not.Null);
            Assert.That(report.models, Is.Not.Null);
            return report;
        }

        [Test]
        public void AllTenProvidedModelsHaveDistinctSourceBackedWrappersAndCompleteMaterials()
        {
            var report = ReadReport();
            Assert.That(report.models.Select(m => m.role), Is.EquivalentTo(Roles));
            Assert.That(report.models.Select(m => m.wrapperPath).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(10));
            Assert.That(report.models.Select(m => m.meshPath).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(10));
            Assert.That(report.models.Select(m => m.materialPath).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(10));
            foreach (var entry in report.models)
            {
                Assert.That(entry.sourcePath, Is.EqualTo(Source + "/" + entry.role + "/" + entry.role + ".fbx"));
                Assert.That(entry.wrapperPath, Is.EqualTo(Root + "/Prefabs/PF_" + entry.role + ".prefab"));
                Assert.That(entry.meshPath, Is.EqualTo(Root + "/Meshes/MS_" + entry.role + ".asset"));
                Assert.That(entry.materialPath, Is.EqualTo(Root + "/Materials/MAT_" + entry.role + ".mat"));
                Assert.That(AssetDatabase.GUIDToAssetPath(AssetDatabase.AssetPathToGUID(entry.sourcePath)), Is.EqualTo(entry.sourcePath));
                Assert.That(AssetDatabase.GetAssetDependencyHash(entry.sourcePath).ToString(),
                    Is.EqualTo(entry.sourceDependencyHash), "Source dependency must match the generation receipt: " + entry.role);
                Assert.That(entry.sourceUnchanged, Is.True, entry.role);
                AssertSourceTexture(entry.role, "_basecolor", entry.baseColorPath);
                AssertSourceTexture(entry.role, "_normal", entry.normalSourcePath);
                AssertSourceTexture(entry.role, "_metallic", entry.metallicSourcePath);
                AssertSourceTexture(entry.role, "_roughness", entry.roughnessSourcePath);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.wrapperPath);
                Assert.That(prefab, Is.Not.Null, entry.role);
                var renderer = prefab.GetComponentInChildren<MeshRenderer>(true);
                var mesh = prefab.GetComponentInChildren<MeshFilter>(true);
                Assert.That(renderer, Is.Not.Null, entry.role);
                Assert.That(mesh, Is.Not.Null, entry.role);
                Assert.That(renderer.sharedMaterial, Is.Not.Null, entry.role);
                Assert.That(AssetDatabase.GetAssetPath(mesh.sharedMesh), Is.EqualTo(entry.meshPath));
                Assert.That(AssetDatabase.GetAssetPath(renderer.sharedMaterial), Is.EqualTo(entry.materialPath));
                Assert.That(AssetDatabase.GetAssetPath(renderer.sharedMaterial.mainTexture), Is.EqualTo(entry.baseColorPath));
                string normalPath = AssetDatabase.GetAssetPath(renderer.sharedMaterial.GetTexture("_BumpMap"));
                Assert.That(normalPath, Is.EqualTo(Root + "/Textures/N_" + entry.role + Path.GetExtension(entry.normalSourcePath)));
                Assert.That(Hash(normalPath), Is.EqualTo(Hash(entry.normalSourcePath)));
                string packedPath = AssetDatabase.GetAssetPath(renderer.sharedMaterial.GetTexture("_MetallicGlossMap"));
                Assert.That(packedPath, Is.EqualTo(Root + "/Textures/MS_" + entry.role + ".png"));
                var material = renderer.sharedMaterial;
                Assert.That(material.shader, Is.Not.Null, entry.role);
                Assert.That(material.shader.name, Is.EqualTo("Standard"), entry.role);
                Assert.That(material.IsKeywordEnabled("_NORMALMAP"), Is.True, entry.role);
                Assert.That(material.IsKeywordEnabled("_METALLICGLOSSMAP"), Is.True, entry.role);
                Assert.That(material.IsKeywordEnabled("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A"), Is.False, entry.role);
                foreach (string property in new[] { "_BumpScale", "_Metallic", "_Glossiness", "_GlossMapScale" })
                    Assert.That(material.GetFloat(property), Is.EqualTo(1f).Within(.00001f), entry.role + " " + property);
                Assert.That(material.GetFloat("_SmoothnessTextureChannel"), Is.EqualTo(0f), entry.role);
                var normalImporter = AssetImporter.GetAtPath(normalPath) as TextureImporter;
                Assert.That(normalImporter, Is.Not.Null, normalPath);
                Assert.That(normalImporter.textureType, Is.EqualTo(TextureImporterType.NormalMap), entry.role);
                Assert.That(normalImporter.convertToNormalmap, Is.False, entry.role);
                Assert.That(normalImporter.sRGBTexture, Is.False, entry.role);
                var packedImporter = AssetImporter.GetAtPath(packedPath) as TextureImporter;
                Assert.That(packedImporter, Is.Not.Null, packedPath);
                Assert.That(packedImporter.textureType, Is.EqualTo(TextureImporterType.Default), entry.role);
                Assert.That(packedImporter.sRGBTexture, Is.False, entry.role);
                Assert.That(packedImporter.alphaSource, Is.EqualTo(TextureImporterAlphaSource.FromInput), entry.role);
                Assert.That(packedImporter.alphaIsTransparency, Is.False, entry.role);
                foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                    Assert.That(Mathf.Min(transform.localScale.x, transform.localScale.y, transform.localScale.z), Is.GreaterThan(0f), entry.role);
            }
        }

        [Test]
        public void DerivedMeshesMeetVertexBudgetAndRecordedShapeEnvelope()
        {
            foreach (var entry in ReadReport().models)
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(entry.meshPath);
                Assert.That(mesh, Is.Not.Null, entry.role);
                Assert.That(Hash(entry.meshPath), Is.EqualTo(entry.generatedMeshSha256),
                    "Measured mesh bytes must match the generation receipt: " + entry.role);
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(entry.sourcePath);
                Assert.That(source, Is.Not.Null, entry.role);
                var sourceFilters = source.GetComponentsInChildren<MeshFilter>(true);
                Assert.That(sourceFilters.Length, Is.GreaterThan(0), entry.role);
                Assert.That(sourceFilters.All(f => f.sharedMesh != null), Is.True, entry.role);
                Assert.That(sourceFilters.Sum(f => f.sharedMesh.vertexCount), Is.EqualTo(entry.sourceVertices), entry.role);
                Assert.That(AssetDatabase.GetAssetDependencyHash(entry.sourcePath).ToString(),
                    Is.EqualTo(entry.sourceDependencyHash), entry.role);
                Assert.That(entry.vertexBudget, Is.InRange(200000, 400000));
                Assert.That(mesh.vertexCount, Is.InRange(1, entry.vertexBudget));
                Assert.That(mesh.vertexCount, Is.EqualTo(entry.derivedVertices));
                Assert.That(mesh.vertexCount, Is.LessThan(entry.sourceVertices));
                Assert.That(mesh.normals.Length, Is.EqualTo(mesh.vertexCount));
                Assert.That(mesh.uv.Length, Is.EqualTo(mesh.vertexCount));
                Assert.That(mesh.triangles.Length, Is.GreaterThan(0));
                Assert.That(entry.relativeP99Error, Is.InRange(0f, .01f));
                Assert.That(entry.relativeMaximumError, Is.InRange(0f, .02f));
                Assert.That(MinComponent(entry.orientedSize), Is.GreaterThan(0f), entry.role);
                Assert.That(MinComponent(entry.positiveScale), Is.GreaterThan(0f), entry.role);
                Assert.That(Finite(entry.orientedSize) && Finite(entry.positiveScale), Is.True, entry.role);
                Vector3 target = Vector3.Scale(entry.orientedSize, entry.positiveScale);
                float longest = MaxAbs(target);
                Assert.That(Finite(target), Is.True, entry.role);
                Assert.That(MaxAbs(mesh.bounds.size - entry.derivedSize), Is.LessThanOrEqualTo(longest * .00001f), entry.role);
                Assert.That(MaxAbs(mesh.bounds.center - entry.derivedCenter), Is.LessThanOrEqualTo(longest * .00001f), entry.role);
                Assert.That(MaxAbs(mesh.bounds.size - target), Is.LessThanOrEqualTo(longest * .01f),
                    "Actual derived bounds must remain within 1% of the calibrated size: " + entry.role);
                Assert.That(MaxAbs(mesh.bounds.center), Is.LessThanOrEqualTo(longest * .01f),
                    "Derived mesh must retain the shared centered pivot: " + entry.role);
                Assert.That(entry.maximumBoundsDeviation, Is.InRange(0f, longest * .01f), entry.role);
                Assert.That(mesh.vertices.All(v => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.sqrMagnitude)), Is.True);
            }
        }

        [Test]
        public void PackedMetallicAndSmoothnessUseSuppliedScalarChannels()
        {
            foreach (var entry in ReadReport().models)
            {
                Texture2D metal = null, rough = null, packed = null;
                try
                {
                    metal = ReadImage(entry.metallicSourcePath);
                    rough = ReadImage(entry.roughnessSourcePath);
                    var material = AssetDatabase.LoadAssetAtPath<Material>(entry.materialPath);
                    packed = ReadImage(AssetDatabase.GetAssetPath(material.GetTexture("_MetallicGlossMap")));
                    Assert.That(packed.width, Is.EqualTo(metal.width));
                    Assert.That(packed.height, Is.EqualTo(metal.height));
                    foreach (var pos in new[] { Vector2Int.zero, new Vector2Int(packed.width / 2, packed.height / 2), new Vector2Int(packed.width - 1, packed.height - 1) })
                    {
                        Color32 actual = packed.GetPixel(pos.x, pos.y);
                        Color32 m = metal.GetPixel(pos.x, pos.y);
                        Color32 r = rough.GetPixel(pos.x * rough.width / packed.width, pos.y * rough.height / packed.height);
                        Assert.That(actual.r, Is.EqualTo(m.r), entry.role);
                        Assert.That(actual.a, Is.EqualTo(255 - r.r), entry.role);
                    }
                }
                finally { if (metal != null) Object.DestroyImmediate(metal); if (rough != null) Object.DestroyImmediate(rough); if (packed != null) Object.DestroyImmediate(packed); }
            }
        }

        [Test]
        public void AssemblyBindsSixBodiesAndFiveIndependentStoredPartsWithoutMissingScripts()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/PF_Stage03_FlounderAssembly.prefab");
            Assert.That(prefab, Is.Not.Null);
            var view = prefab.GetComponent(RuntimeReflection.RuntimeType("SashimiBoy.FlounderAssemblyView"));
            Assert.That(view, Is.Not.Null);
            var bodies = (GameObject[])RuntimeReflection.GetField(view, "bodyStates");
            var fillets = ((Array)RuntimeReflection.GetField(view, "fillets")).Cast<Component>().ToArray();
            var head = (Component)RuntimeReflection.GetField(view, "head");
            Assert.That(bodies.Length, Is.EqualTo(6));
            Assert.That(bodies.Distinct().Count(), Is.EqualTo(6));
            Assert.That(bodies.Count(b => b.activeSelf), Is.EqualTo(1));
            Assert.That(bodies[0].activeSelf, Is.True);
            Assert.That(fillets.Length, Is.EqualTo(4));
            Assert.That(fillets.Append(head).Distinct().Count(), Is.EqualTo(5));
            Assert.That(fillets.All(f => !f.gameObject.activeSelf) && !head.gameObject.activeSelf, Is.True);
            foreach (string field in new[] { "bodyPivot", "detachedPartsRoot", "headStorageAnchor", "filletWorkAnchor", "knifeAttachmentAnchor", "handAttachmentAnchor", "sashimiOutputAnchor", "plateOutputAnchor", "sashimiSlicePrefab" })
                Assert.That(RuntimeReflection.GetField(view, field), Is.Not.Null, field);
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), Is.Zero);
                Assert.That(Mathf.Min(transform.localScale.x, transform.localScale.y, transform.localScale.z), Is.GreaterThan(0f));
            }
            var landingBounds = fillets.Append(head).Select(part =>
            {
                var anchor = (Transform)RuntimeReflection.GetField(part, "storageAnchor");
                Bounds bounds = part.GetComponentInChildren<MeshFilter>(true).sharedMesh.bounds;
                bounds.center += anchor.localPosition;
                Assert.That(bounds.min.y, Is.EqualTo(0f).Within(.0001f), "Stored part must rest on the assembly's board plane.");
                return bounds;
            }).ToArray();
            for (int i = 0; i < landingBounds.Length; i++) for (int j = i + 1; j < landingBounds.Length; j++)
                Assert.That(landingBounds[i].Intersects(landingBounds[j]), Is.False, "Stored parts need independent non-overlapping space.");
            AssertGeneratedPrefabCanActuallyReparent(prefab);
        }

        private static void AssertGeneratedPrefabCanActuallyReparent(GameObject prefab)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                RuntimeReflection.InvokeStatic("SashimiBoy.EditorTools.FlounderPreviewDiagnostics", "PrepareRuntimeClone", instance);
                Assert.That(instance.GetComponentsInChildren<Transform>(true).Any(PrefabUtility.IsPartOfAnyPrefab), Is.False);
                var view = instance.GetComponent(RuntimeReflection.RuntimeType("SashimiBoy.FlounderAssemblyView"));
                RuntimeReflection.Invoke(view, "ResetAssembly");
                Assert.That(RuntimeReflection.Invoke(view, "TryRemoveHead"), Is.EqualTo(true));
                Assert.That(RuntimeReflection.Invoke(view, "TrySeparateFillet", 0), Is.EqualTo(true));
                Assert.That(RuntimeReflection.Invoke(view, "TrySeparateFillet", 1), Is.EqualTo(true));
                Assert.That(RuntimeReflection.Invoke(view, "TryTurnOver"), Is.EqualTo(true));
                RuntimeReflection.Invoke(view, "AdvancePresentation", 5f);
                Assert.That(RuntimeReflection.Invoke(view, "TrySeparateFillet", 2), Is.EqualTo(true));
                Assert.That(RuntimeReflection.Invoke(view, "TrySeparateFillet", 3), Is.EqualTo(true));
                RuntimeReflection.Invoke(view, "AdvancePresentation", 5f);
                Assert.That(view.GetType().GetProperty("BodyState").GetValue(view).ToString(), Is.EqualTo("Hidden"));
                Assert.That(((GameObject[])RuntimeReflection.GetField(view, "bodyStates")).All(b => !b.activeSelf), Is.True);
                var parts = ((Array)RuntimeReflection.GetField(view, "fillets")).Cast<Component>()
                    .Append((Component)RuntimeReflection.GetField(view, "head"));
                foreach (var part in parts)
                {
                    var storage = (Transform)RuntimeReflection.GetField(part, "storageAnchor");
                    Assert.That(part.transform.parent, Is.SameAs(storage));
                    Assert.That(Vector3.Distance(part.transform.position, storage.position), Is.LessThan(.00001f));
                    Assert.That(part.GetType().GetProperty("IsStored").GetValue(part), Is.EqualTo(true));
                    Assert.That(part.gameObject.activeInHierarchy, Is.True);
                }
                Assert.That(instance.GetComponentsInChildren<MeshRenderer>(false).Length, Is.EqualTo(5));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void RegenerationKeepsSourceSceneGuidAndGeneratedBytesAndOpenSceneState()
        {
            var protectedBefore = Snapshot(Source).Concat(Snapshot("Assets/_SashimiBoy/Scenes")).ToDictionary(k => k.Key, v => v.Value);
            var generatedBefore = Snapshot(Root);
            var scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
            var dirty = scenes.Select(s => s.isDirty).ToArray();
            var roots = scenes.Select(s => s.GetRootGameObjects().Select(g => g.GetInstanceID()).OrderBy(i => i).ToArray()).ToArray();
            Scene active = SceneManager.GetActiveScene();
            RuntimeReflection.InvokeStatic("SashimiBoy.EditorTools.Stage03FlounderAuthoring", "BuildBatch");
            Assert.That(Snapshot(Source).Concat(Snapshot("Assets/_SashimiBoy/Scenes")).ToDictionary(k => k.Key, v => v.Value), Is.EquivalentTo(protectedBefore));
            Assert.That(Snapshot(Root), Is.EquivalentTo(generatedBefore));
            Assert.That(SceneManager.sceneCount, Is.EqualTo(scenes.Length));
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(active));
            for (int i = 0; i < scenes.Length; i++)
            {
                Assert.That(scenes[i].isDirty, Is.EqualTo(dirty[i]));
                Assert.That(scenes[i].GetRootGameObjects().Select(g => g.GetInstanceID()).OrderBy(id => id).ToArray(), Is.EqualTo(roots[i]));
            }
        }

        private static Dictionary<string, string> Snapshot(string path)
        {
            string absolute = Absolute(path);
            IEnumerable<string> files = Directory.GetFiles(absolute, "*", SearchOption.AllDirectories);
            if (File.Exists(absolute + ".meta")) files = files.Append(absolute + ".meta");
            return files.ToDictionary(p => p.Replace('\\', '/'), Hash);
        }
        private static string Hash(string path)
        {
            using var stream = File.OpenRead(Absolute(path)); using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        private static Texture2D ReadImage(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            Assert.That(texture.LoadImage(File.ReadAllBytes(Absolute(path))), Is.True);
            return texture;
        }
        private static string Absolute(string path) => Path.GetFullPath(Path.Combine(ProjectRoot, path));
        private static float MinComponent(Vector3 value) => Mathf.Min(value.x, value.y, value.z);
        private static float MaxAbs(Vector3 value) => Mathf.Max(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        private static void AssertSourceTexture(string role, string suffix, string path)
        {
            Assert.That(path, Does.StartWith(Source + "/" + role + "/"), role + suffix);
            Assert.That(Path.GetFileNameWithoutExtension(path), Does.EndWith(suffix), role + suffix);
            Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>(path), Is.Not.Null, path);
        }
    }
}
