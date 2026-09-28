using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SashimiBoy.Tests
{
    public sealed class KevinEmbodimentEditModeTests
    {
        [Test]
        public void SelectedAndLegacyWorldKevin_UseOwnerCuteFaceAndTheExistingHumanoidHands()
        {
            var catalog = AssetDatabase.LoadMainAssetAtPath("Assets/_SashimiBoy/Art/Generated/Data/KevinVariantCatalog.asset");
            var selected = RuntimeReflection.Invoke(catalog, "get_ProvisionalDefault");
            Assert.That(RuntimeReflection.GetField(selected, "variantId"), Is.EqualTo("CuteFace"));
            var prefab = (GameObject)RuntimeReflection.GetField(selected, "prefab");
            var legacy = RuntimeReflection.Invoke(catalog, "Find", "AmbiguousFace");
            Assert.That(RuntimeReflection.GetField(legacy, "prefab"), Is.EqualTo(prefab), "Existing serialized Scene selections must receive the repaired Kevin.");
            var rig = prefab.GetComponent(RuntimeReflection.RuntimeType("SashimiBoy.KevinBodyRig"));
            Assert.That(rig, Is.Not.Null);
            var heads = (Renderer[])RuntimeReflection.GetField(rig, "headRenderers");
            var skin = heads.OfType<SkinnedMeshRenderer>().Single();
            Assert.That(AssetDatabase.GetAssetPath(skin.sharedMaterial.mainTexture), Does.Contain("/CuteFace/Textures/"));
            Assert.That(skin.bones.Select(b => b.name), Is.EquivalentTo(new[] { "Head", "Neck", "Chest" }));
            Assert.That(prefab.GetComponentInChildren<Animator>().isHuman, Is.True);
            foreach (string side in new[] { "right", "left" })
            {
                var arm = RuntimeReflection.GetField(rig, side);
                var upper = (Transform)RuntimeReflection.GetField(arm, "upper");
                var hand = (Transform)RuntimeReflection.GetField(arm, "hand");
                var palm = (Transform)RuntimeReflection.GetField(arm, "palm");
                Assert.That(hand.IsChildOf(upper), Is.True);
                Assert.That(palm.IsChildOf(hand), Is.True);
            }
        }
    }
}
