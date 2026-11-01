using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SashimiBoy.Tests
{
    // Asset/binding regressions only. These do not claim anatomical accuracy,
    // watertight source scans or final visual acceptance of the prepared fish.
    public sealed class Stage03TwoFilletGeometryEditModeTests
    {
        private const string Root = "Assets/_SashimiBoy/Art/Generated/Stage03Flounder";
        private const string Source = "Assets/_SashimiBoy/Art/Source/DayWorld/Fish/Stage03";
        private const string Assembly = Root + "/Prefabs/PF_Stage03_FlounderAssembly.prefab";
        private static readonly string[] Roles = { "WholeFish", "HeadRemovedFish", "FilletRemoved1", "FilletRemoved2",
            "FilletRemoved3", "FilletRemoved4", "FishHead", "FrontFillet", "BackFillet", "SashimiSlice" };
        private static readonly string[] SourceGuids = { "47ea75a1bb54705479ab832dac1d6296", "0919366d1429c5c4397379fbe550a0ec",
            "f4fb288e1edad474b9edfb1e15561e09", "439e3b8e8e8370249a6815c364893548", "a39846b2326f5c144b6c131691583370",
            "14384254a6223984697a222b501f5fbf", "816ec74b7fc05974d95e11da129bed18", "a13f9913aa0457c4b954c1d97fb19945",
            "329e8a66043c8404f8fea71b3b15732b", "51973189d27a7114aaf1984b833a3373" };
        [Serializable] private sealed class GeometryReport
        {
            public string algorithm, sourceUVObservation, coordinateContract, sourceScope;
            public string[] sourceRoles;
            public PhysicalSideGeometry[] models;
        }
        [Serializable] private sealed class PhysicalSideGeometry
        {
            public string role, sourceRole, sourcePath, sourceDependencyHash, sourceMeshPath, generatedMeshPath, wrapperPath;
            public string operation, fleshUvSourceRole;
            public int sourceVertices, generatedVertices, generatedTriangles, selectedSurfaceTriangles;
            public Vector3 sourceSize, generatedSize, rotationEuler;
            public bool sourceUnchanged;
        }
        static object Get(object target, string name) => RuntimeReflection.GetField(target, name);
        static Component[] Parts(object target, string name) => ((IEnumerable)Get(target, name)).Cast<Component>().ToArray();

        [Test]
        public void GeneratedAssembly_RetainsTheTenSourceIdentitiesAndLegacyLibrary_WhileBindingTwoPhysicalSides()
        {
            for (int i = 0; i < Roles.Length; i++)
            {
                string path = Source + "/" + Roles[i] + "/" + Roles[i] + ".fbx";
                Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(SourceGuids[i]), Roles[i]);
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(path), Is.Not.Null, path);
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/PF_" + Roles[i] + ".prefab"), Is.Not.Null, Roles[i]);
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Assembly);
            Assert.That(prefab, Is.Not.Null);
            var view = prefab.GetComponent(RuntimeReflection.RuntimeType("SashimiBoy.FlounderAssemblyView"));
            Assert.That(view, Is.Not.Null);
            Assert.That(RuntimeReflection.Invoke(view, "get_TwoFilletPreparation"), Is.EqualTo(false), "The asset-library default retains the legacy presentation until Stage3 explicitly selects its mode.");
            var bodies = (GameObject[])Get(view, "bodyStates");
            var legacy = Parts(view, "fillets");
            var skins = Parts(view, "skins");
            var whole = Parts(view, "wholeFillets");
            var skinnedBodies = (GameObject[])Get(view, "skinnedBodyStates");
            Assert.That(bodies.Length, Is.EqualTo(6)); Assert.That(bodies.All(b => b != null), Is.True);
            Assert.That(legacy.Length, Is.EqualTo(4)); Assert.That(legacy.All(p => p != null), Is.True);
            Assert.That(skins.Length, Is.EqualTo(2)); Assert.That(whole.Length, Is.EqualTo(2));
            Assert.That(skins.All(p => p != null) && whole.All(p => p != null), Is.True);
            Assert.That(skinnedBodies.Length, Is.EqualTo(2)); Assert.That(skinnedBodies.All(b => b != null), Is.True);
            Assert.That(skins.Concat(whole).Distinct().Count(), Is.EqualTo(4), "Discarded skins and complete edible sides must be different objects.");
            Assert.That(skins.Concat(whole).Intersect(legacy), Is.Empty, "The old quarter copies must not serve as the new edible inventory.");
            foreach (string field in new[] { "skinStorageAnchors", "wholeFilletStorageAnchors" })
            {
                var anchors = (Transform[])Get(view, field);
                Assert.That(anchors.Length, Is.EqualTo(2)); Assert.That(anchors.All(t => t != null), Is.True);
            }
            foreach (Transform transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), Is.Zero);
                Assert.That(Finite(transform.localPosition) && Finite(transform.localScale), Is.True, transform.name);
                Assert.That(Mathf.Min(transform.localScale.x, transform.localScale.y, transform.localScale.z), Is.GreaterThan(0f), transform.name);
            }
        }

        [Test]
        public void PhysicalSideGeometry_IsFiniteAndSourceTextured_AndEachWholeFilletUsesOneMesh()
        {
            foreach (string role in new[] { "FrontWholeFillet", "BackWholeFillet", "FrontSkin", "BackSkin", "FrontSkinnedBody", "BackSkinnedBody" })
            {
                string path = Root + "/Meshes/MS_" + role + ".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                Assert.That(mesh, Is.Not.Null, path);
                Assert.That(mesh.isReadable, Is.True, role);
                Assert.That(mesh.vertexCount, Is.GreaterThan(0), role);
                Assert.That(mesh.vertices.All(Finite), Is.True, role);
                Assert.That(mesh.normals.Length, Is.EqualTo(mesh.vertexCount), role);
                Assert.That(mesh.normals.All(Finite), Is.True, role);
                Assert.That(mesh.uv.Length, Is.EqualTo(mesh.vertexCount), role);
                Assert.That(mesh.uv.All(v => Finite(v.x) && Finite(v.y)), Is.True, role);
                int[] triangles = mesh.triangles;
                Assert.That(triangles.Length, Is.GreaterThan(0), role);
                Assert.That(triangles.Length % 3, Is.Zero, role);
                Assert.That(triangles.All(index => index >= 0 && index < mesh.vertexCount), Is.True, role);
                Assert.That(Finite(mesh.bounds.center) && Finite(mesh.bounds.size), Is.True, role);
                Assert.That(Mathf.Min(mesh.bounds.size.x, mesh.bounds.size.y, mesh.bounds.size.z), Is.GreaterThan(0f), role);
            }
            for (int side = 0; side < 2; side++)
            {
                string role = side == 0 ? "FrontWholeFillet" : "BackWholeFillet";
                string path = Root + "/Prefabs/PF_" + role + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, path);
                var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
                Assert.That(filters.Length, Is.EqualTo(1), "A complete edible side must be one surface, not two overlapping quarter instances.");
                Assert.That(AssetDatabase.GetAssetPath(filters[0].sharedMesh), Is.EqualTo(Root + "/Meshes/MS_" + role + ".asset"));
                var renderers = prefab.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers.Length, Is.EqualTo(1), role);
                foreach (var renderer in renderers)
                {
                    Assert.That(renderer.sharedMaterials.Length, Is.GreaterThan(0));
                    Assert.That(renderer.sharedMaterials.All(m => m != null && m.shader != null && m.mainTexture != null), Is.True, role);
                    Assert.That(renderer.sharedMaterials.All(m => AssetDatabase.GetAssetPath(m.mainTexture).StartsWith(Source + "/", StringComparison.Ordinal)), Is.True,
                        "Whole-side materials must retain provided fish texture provenance.");
                }
            }
        }

        [Test]
        public void GeometryReceipt_MatchesActualMeshesAndDependencies_AndEveryDerivedUvComesFromAProvidedSurface()
        {
            string path = Root + "/Stage03TwoFilletGeometry.json";
            Assert.That(File.Exists(path), Is.True, path);
            var report = JsonUtility.FromJson<GeometryReport>(File.ReadAllText(path));
            Assert.That(report, Is.Not.Null);
            Assert.That(report.algorithm, Is.EqualTo("Stage03TwoFillet-1"));
            Assert.That(report.sourceRoles, Is.EquivalentTo(new[] { "HeadRemovedFish", "FilletRemoved2", "FrontFillet", "BackFillet" }));
            Assert.That(report.sourceUVObservation, Does.Contain("Front").And.Contain("Back").And.Contain("UV"));
            Assert.That(report.coordinateContract, Is.Not.Empty); Assert.That(report.sourceScope, Is.Not.Empty);
            Assert.That(report.models.Select(m => m.role), Is.EquivalentTo(new[] { "FrontWholeFillet", "BackWholeFillet", "FrontSkin", "BackSkin", "FrontSkinnedBody", "BackSkinnedBody" }));
            foreach (var entry in report.models)
            {
                Assert.That(entry.sourcePath, Is.EqualTo(Source + "/" + entry.sourceRole + "/" + entry.sourceRole + ".fbx"));
                Assert.That(entry.sourceUnchanged, Is.True, entry.role);
                Assert.That(entry.sourceDependencyHash, Is.EqualTo(AssetDatabase.GetAssetDependencyHash(entry.sourcePath).ToString()), entry.role);
                Assert.That(entry.sourceMeshPath, Is.EqualTo(Root + "/Meshes/MS_" + entry.sourceRole + ".asset"));
                Assert.That(entry.generatedMeshPath, Is.EqualTo(Root + "/Meshes/MS_" + entry.role + ".asset"));
                Assert.That(entry.wrapperPath, Is.EqualTo(Root + "/Prefabs/PF_" + entry.role + ".prefab"));
                Assert.That(entry.operation, Is.Not.Empty, entry.role);
                var sourceMesh = AssetDatabase.LoadAssetAtPath<Mesh>(entry.sourceMeshPath);
                var generatedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(entry.generatedMeshPath);
                Assert.That(sourceMesh, Is.Not.Null); Assert.That(generatedMesh, Is.Not.Null);
                Assert.That(sourceMesh.vertexCount, Is.EqualTo(entry.sourceVertices), entry.role);
                Assert.That(generatedMesh.vertexCount, Is.EqualTo(entry.generatedVertices), entry.role);
                Assert.That(generatedMesh.triangles.Length / 3, Is.EqualTo(entry.generatedTriangles), entry.role);
                Assert.That(Vector3.Distance(sourceMesh.bounds.size, entry.sourceSize), Is.LessThan(.00001f), entry.role);
                Assert.That(Vector3.Distance(generatedMesh.bounds.size, entry.generatedSize), Is.LessThan(.00001f), entry.role);
                var providedUv = new HashSet<Vector2>(sourceMesh.uv);
                if (!string.IsNullOrEmpty(entry.fleshUvSourceRole))
                {
                    Assert.That(entry.fleshUvSourceRole, Is.EqualTo("FrontFillet").Or.EqualTo("BackFillet"));
                    var donor = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/Meshes/MS_" + entry.fleshUvSourceRole + ".asset");
                    providedUv.UnionWith(donor.uv);
                }
                Assert.That(generatedMesh.uv.All(providedUv.Contains), Is.True, "Derived UVs must reference the supplied body/flesh surfaces rather than an invented texture region: " + entry.role);
                if (entry.role.EndsWith("SkinnedBody", StringComparison.Ordinal))
                    Assert.That(Vector3.Distance(sourceMesh.bounds.size, generatedMesh.bounds.size), Is.LessThan(.00001f), "Peeling must expose the complete current body without deleting half of its geometry.");
                if (entry.role.EndsWith("Skin", StringComparison.Ordinal))
                {
                    Assert.That(entry.selectedSurfaceTriangles, Is.GreaterThan(0), entry.role);
                    Assert.That(entry.fleshUvSourceRole, Is.Null.Or.Empty, "Discarded skin retains its source body surface.");
                }
                if (entry.role.EndsWith("WholeFillet", StringComparison.Ordinal))
                {
                    Assert.That(generatedMesh.vertexCount, Is.EqualTo(sourceMesh.vertexCount), "A whole edible side retains one provided fillet rather than combining quarter copies.");
                    Assert.That(Vector3.Distance(sourceMesh.bounds.size, generatedMesh.bounds.size), Is.LessThan(.00001f), entry.role);
                }
            }
        }

        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
