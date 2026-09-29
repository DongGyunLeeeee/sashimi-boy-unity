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
    }
}
