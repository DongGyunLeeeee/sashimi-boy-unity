using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SashimiBoy.Tests
{
    public sealed class KevinEmbodimentEditModeTests
    {
        [Test]
        public void SelectedAndLegacyWorldKevin_UseOwnerCuteFaceAndTheSuppliedHumanoidHands()
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
            var animator = prefab.GetComponentInChildren<Animator>();
            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.isHuman && animator.avatar != null && animator.avatar.isValid, Is.True);
            Assert.That(AssetDatabase.GetAssetPath(animator.avatar), Is.EqualTo(
                "Assets/_SashimiBoy/Art/Source/Characters/Stage03/RiggedBody/RiggedBody.fbx"));
            Assert.That(skin.bones, Is.EquivalentTo(new[] {
                animator.GetBoneTransform(HumanBodyBones.Head),
                animator.GetBoneTransform(HumanBodyBones.Neck),
                animator.GetBoneTransform(HumanBodyBones.Chest) }));
            foreach (string side in new[] { "right", "left" })
            {
                var arm = RuntimeReflection.GetField(rig, side);
                var upper = (Transform)RuntimeReflection.GetField(arm, "upper");
                var hand = (Transform)RuntimeReflection.GetField(arm, "hand");
                var palm = (Transform)RuntimeReflection.GetField(arm, "palm");
                var expectedUpper = animator.GetBoneTransform(side == "right" ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
                var expectedHand = animator.GetBoneTransform(side == "right" ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
                Assert.That(upper, Is.EqualTo(expectedUpper));
                Assert.That(hand, Is.EqualTo(expectedHand));
                Assert.That(hand.IsChildOf(upper), Is.True);
                Assert.That(palm.IsChildOf(hand), Is.True);
            }
        }
    }
}
