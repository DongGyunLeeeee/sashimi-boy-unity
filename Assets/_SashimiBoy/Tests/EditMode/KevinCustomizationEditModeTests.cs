using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SashimiBoy.Tests
{
    public sealed class KevinCustomizationEditModeTests
    {
        [Test]
        public void FourDistinctOwnerHeads_HaveConnectedNecksAndRetainCookingSkeleton()
        {
            var catalog = AssetDatabase.LoadMainAssetAtPath("Assets/_SashimiBoy/Art/Generated/Data/KevinFaceCatalog.asset");
            var choices = (IList)RuntimeReflection.GetField(catalog, "choices");
            Assert.That(choices.Count, Is.EqualTo(4));
            Assert.That(choices.Cast<object>().Select(c => (string)RuntimeReflection.GetField(c, "id")),
                Is.EquivalentTo(new[] { "CuteFace", "PlainFace", "AmbiguousFace", "WesternFace" }));
            var meshes = choices.Cast<object>().Select(c => (Mesh)RuntimeReflection.GetField(c, "mesh")).ToArray();
            Assert.That(meshes.Distinct().Count(), Is.EqualTo(4));
            foreach (var mesh in meshes)
            {
                Assert.That(mesh.bounds.size.y, Is.InRange(.2f, .65f), mesh.name + " head scale");
                Assert.That(mesh.boneWeights.Length, Is.EqualTo(mesh.vertexCount));
                Assert.That(mesh.bindposes.Length, Is.EqualTo(3));
                Assert.That(mesh.vertices.Min(p => p.y), Is.LessThan(-.07f), "The neck extends inside the body collar.");
                Assert.That(mesh.boneWeights.Any(w => w.boneIndex2 == 2 && w.weight2 > .9f), Is.True);
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_SashimiBoy/Art/Generated/Stage01Playable/Kevin/PF_Kevin_Complete.prefab");
            var appearance = prefab.GetComponent(RuntimeReflection.RuntimeType("SashimiBoy.KevinAppearance"));
            Assert.That(RuntimeReflection.GetField(appearance, "catalog"), Is.SameAs(catalog));
            var rig = prefab.GetComponent(RuntimeReflection.RuntimeType("SashimiBoy.KevinBodyRig"));
            Assert.That(RuntimeReflection.GetField(RuntimeReflection.GetField(rig, "right"), "palm"), Is.Not.Null);
            Assert.That(RuntimeReflection.GetField(RuntimeReflection.GetField(rig, "left"), "palm"), Is.Not.Null);
            var animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.That(animator.isHuman && animator.avatar != null && animator.avatar.isValid, Is.True);
            var face = (SkinnedMeshRenderer)RuntimeReflection.GetField(appearance, "faceRenderer");
            Assert.That(face.bones, Is.EquivalentTo(new[] {
                animator.GetBoneTransform(HumanBodyBones.Head),
                animator.GetBoneTransform(HumanBodyBones.Neck),
                animator.GetBoneTransform(HumanBodyBones.Chest) }));
            foreach (var mesh in meshes)
                Assert.That(mesh.bindposes, Is.EqualTo(face.sharedMesh.bindposes),
                    "Every saved face choice must use the replacement body's same head/neck/chest bind frame: " + mesh.name);
        }

        [Test]
        public void LegacyOrInvalidFace_UsesCuteFaceWithoutChangingInventory()
        {
            var type = RuntimeReflection.RuntimeType("SashimiBoy.SaveData");
            var save = JsonUtility.FromJson("{\"ownedEquipmentIds\":[\"SamplePackDrumKit\"],\"clearedStageIds\":[\"STAGE_01_SALMON\"]}", type);
            string before = JsonUtility.ToJson(save);
            var catalog = AssetDatabase.LoadMainAssetAtPath("Assets/_SashimiBoy/Art/Generated/Data/KevinFaceCatalog.asset");
            foreach (string id in new[] { (string)RuntimeReflection.GetField(save, "kevinFaceId"), "removed-or-corrupt-id" })
            {
                var choice = RuntimeReflection.Invoke(catalog, "Resolve", new object[] { id });
                Assert.That(RuntimeReflection.GetField(choice, "id"), Is.EqualTo("CuteFace"));
            }
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            Assert.That(((IList)RuntimeReflection.GetField(save, "ownedEquipmentIds")).Contains("SamplePackDrumKit"), Is.True);
        }

        [TestCase("CuteFace")]
        [TestCase("PlainFace")]
        [TestCase("AmbiguousFace")]
        [TestCase("WesternFace")]
        public void SelectedFaceBakesAVisibleFiniteHeadOnTheReplacementSkeleton(string id)
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_SashimiBoy/Art/Generated/Stage01Playable/Kevin/PF_Kevin_Complete.prefab"));
            var baked = new Mesh();
            try
            {
                var appearance = instance.GetComponent(RuntimeReflection.RuntimeType("SashimiBoy.KevinAppearance"));
                RuntimeReflection.Invoke(appearance, "ApplyFace", id);
                var skin = (SkinnedMeshRenderer)RuntimeReflection.GetField(appearance, "faceRenderer");
                skin.BakeMesh(baked);
                Vector3[] points = baked.vertices.Select(p => skin.transform.TransformPoint(p)).ToArray();
                Assert.That(points.Length, Is.GreaterThan(100), id);
                Assert.That(points.All(p => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z)), Is.True, id);
                var bounds = new Bounds(points[0], Vector3.zero);
                foreach (var point in points) bounds.Encapsulate(point);
                Assert.That(bounds.size.y, Is.InRange(.2f, .65f), id + " actual posed head height");
                Assert.That(bounds.max.y, Is.GreaterThan(1.65f), id + " must bake above Kevin's eyes, rather than an empty/headless body");
                Assert.That(bounds.min.y, Is.InRange(1.25f, 1.55f), id + " neck must remain near the collar");
                Assert.That(skin.bones.All(b => b != null), Is.True, id);
                Assert.That(skin.sharedMesh.tangents.Length, Is.EqualTo(skin.sharedMesh.vertexCount), id);
            }
            finally { Object.DestroyImmediate(baked); Object.DestroyImmediate(instance); }
        }
    }
}
