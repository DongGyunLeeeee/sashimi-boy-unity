using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SashimiBoy.Tests
{
    public sealed class KevinRiggedBodyEditModeTests
    {
        private const string Body = "Assets/_SashimiBoy/Art/Source/Characters/Stage03/RiggedBody/RiggedBody.fbx";
        private const string Prefab = "Assets/_SashimiBoy/Art/Generated/Stage01Playable/Kevin/PF_Kevin_Complete.prefab";
        private const string MeshPath = "Assets/_SashimiBoy/Art/Generated/Stage01Playable/Kevin/MS_RiggedBody_WithoutHead.asset";

        [Test]
        public void OwnerSourceBytesRemainIntact_AndHumanoidImportRetainsEveryFinger()
        {
            using (var hash = SHA256.Create())
                Assert.That(BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(Body))).Replace("-", "").ToLowerInvariant(),
                    Is.EqualTo("3235dbba38a616d33c39474754685396607b2111e65118fbcd9ee25580101d2a"));
            Assert.That(AssetDatabase.AssetPathToGUID(Body), Is.EqualTo("b905555ab3206ee4e9d306ab55c16922"));
            var imported = AssetDatabase.LoadAssetAtPath<GameObject>(Body);
            var animator = imported.GetComponent<Animator>();
            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.avatar, Is.Not.Null);
            Assert.That(animator.avatar.isValid && animator.isHuman, Is.True);
            var skin = imported.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
            string[] digits = { "Thumb", "Index", "Middle", "Ring", "Little" };
            foreach (string side in new[] { "Right", "Left" })
                foreach (string digit in digits)
                    foreach (string joint in new[] { "Proximal", "Intermediate", "Distal" })
                    {
                        var id = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + digit + joint);
                        var bone = animator.GetBoneTransform(id);
                        Assert.That(bone, Is.Not.Null, id.ToString());
                        int index = Array.IndexOf(skin.bones, bone);
                        Assert.That(index, Is.GreaterThanOrEqualTo(0), id.ToString());
                        Assert.That(skin.sharedMesh.boneWeights.Any(w => Weight(w, index) > 0f), Is.True, "Unweighted finger: " + id);
                    }
        }

        [Test]
        public void GeneratedBodyPreservesArtistUvsAndSkin_WithoutTheOldSourceHead()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            var animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.That(AssetDatabase.GetAssetPath(animator.avatar), Is.EqualTo(Body));
            var body = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => AssetDatabase.GetAssetPath(r.sharedMesh) == MeshPath);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Body).GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
            Assert.That(body.sharedMesh.vertexCount, Is.GreaterThan(100));
            Assert.That(body.sharedMesh.vertexCount, Is.LessThan(source.sharedMesh.vertexCount), "Only the supplied head geometry is removed.");
            Assert.That(body.sharedMesh.boneWeights.Length, Is.EqualTo(body.sharedMesh.vertexCount));
            Assert.That(body.sharedMesh.bindposes, Is.EqualTo(source.sharedMesh.bindposes));
            Assert.That(body.sharedMesh.bindposes.Length, Is.EqualTo(body.bones.Length));
            Assert.That(body.sharedMesh.normals.Length, Is.EqualTo(body.sharedMesh.vertexCount));
            Assert.That(body.sharedMesh.tangents.Length, Is.EqualTo(body.sharedMesh.vertexCount));
            Assert.That(body.sharedMesh.boneWeights.All(w => Mathf.Abs(w.weight0 + w.weight1 + w.weight2 + w.weight3 - 1f) < .001f), Is.True);
            Assert.That(body.bones.All(b => b != null), Is.True);
            Assert.That(body.sharedMesh.uv.Length, Is.EqualTo(body.sharedMesh.vertexCount));
            Assert.That(body.sharedMesh.vertices.Max(p => body.transform.TransformPoint(p).y), Is.EqualTo(1.55f).Within(.003f));
            Assert.That(AssetDatabase.GetAssetPath(body.sharedMaterial.mainTexture), Does.EndWith("mannequin_3d_model_basecolor.JPEG"));
            Assert.That(AssetDatabase.GetAssetPath(body.sharedMaterial.GetTexture("_BumpMap")), Does.EndWith("mannequin_3d_model_normal.JPEG"));
            Assert.That(prefab.GetComponentsInChildren<Transform>(true).All(t => t.localScale.x > 0f && t.localScale.y > 0f && t.localScale.z > 0f), Is.True);
            var rig = prefab.GetComponent(RuntimeReflection.RuntimeType("SashimiBoy.KevinBodyRig"));
            var spine = (Transform)RuntimeReflection.GetField(rig, "cookingSpine");
            var bendAxis = (Vector3)RuntimeReflection.GetField(rig, "spineBendAxis");
            Assert.That(Vector3.Dot(spine.TransformDirection(bendAxis).normalized, prefab.transform.right), Is.GreaterThan(.999f), "Cooking must bend forwards around character right, not sideways along a source-specific bone axis.");
        }

        [Test]
        public void RealWeightedFingersDeform_AndConnectedPalmsSupportAWorkingGripPose()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
            var before = new Mesh(); var after = new Mesh();
            try
            {
                var rig = instance.GetComponent(RuntimeReflection.RuntimeType("SashimiBoy.KevinBodyRig"));
                var fingers = (Transform[])RuntimeReflection.GetField(rig, "fingerBones");
                var axes = (Vector3[])RuntimeReflection.GetField(rig, "fingerAxes");
                var rest = (Quaternion[])RuntimeReflection.GetField(rig, "fingerRest");
                var angles = (float[])RuntimeReflection.GetField(rig, "fingerAngles");
                Assert.That(fingers.Length, Is.EqualTo(30));
                Assert.That(fingers.Distinct().Count(), Is.EqualTo(30));
                Assert.That(axes.Length, Is.EqualTo(30)); Assert.That(rest.Length, Is.EqualTo(30)); Assert.That(angles.Length, Is.EqualTo(30));
                Assert.That(axes.All(a => Mathf.Abs(a.magnitude - 1f) < .001f), Is.True);
                var skin = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => AssetDatabase.GetAssetPath(r.sharedMesh) == MeshPath);
                skin.BakeMesh(before);
                Assert.That(before.vertexCount, Is.EqualTo(skin.sharedMesh.vertexCount));
                Bounds actual = new Bounds(skin.transform.TransformPoint(before.vertices[0]), Vector3.zero);
                foreach (var vertex in before.vertices) actual.Encapsulate(skin.transform.TransformPoint(vertex));
                Assert.That(actual.size.y, Is.GreaterThan(1.4f), "The actual baked supplied body must include the complete torso and legs.");
                var animator = instance.GetComponentInChildren<Animator>(true);
                var indexJoint = animator.GetBoneTransform(HumanBodyBones.RightIndexIntermediate);
                int index = Array.FindIndex(fingers, b => b == indexJoint);
                Assert.That(index, Is.GreaterThanOrEqualTo(0));
                fingers[index].localRotation = rest[index] * Quaternion.AngleAxis(angles[index], axes[index]);
                skin.BakeMesh(after);
                Assert.That(before.vertices.Zip(after.vertices, Vector3.Distance).Max(), Is.GreaterThan(.001f), "The finger must deform the actual supplied skin.");
                instance.transform.position = new Vector3(.12f, 0f, -.82f);
                RuntimeReflection.SetField(rig, "working", true);
                RuntimeReflection.SetField(rig, "rightTarget", new Vector3(.24f, 1.02f, -.4f));
                RuntimeReflection.SetField(rig, "leftTarget", new Vector3(-.13f, 1.02f, -.4f));
                object right = RuntimeReflection.GetField(rig, "right"), left = RuntimeReflection.GetField(rig, "left");
                RuntimeReflection.SetField(rig, "rightRotation", Quaternion.Euler(0f, 180f, 0f) * (Quaternion)RuntimeReflection.GetField(right, "handWorldRest"));
                RuntimeReflection.SetField(rig, "leftRotation", Quaternion.Euler(0f, 90f, 0f) * (Quaternion)RuntimeReflection.GetField(left, "handWorldRest"));
                Vector3 preferredRoot = instance.transform.position, rootScale = instance.transform.localScale;
                var jointPositions = instance.GetComponentsInChildren<Transform>(true)
                    .Where(t => t != instance.transform).ToDictionary(t => t, t => t.localPosition);
                var jointScales = jointPositions.Keys.ToDictionary(t => t, t => t.localScale);
                RuntimeReflection.Invoke(rig, "ApplyWorkingPoseAtRoot", preferredRoot);
                Assert.That(instance.transform.position.y, Is.EqualTo(preferredRoot.y).Within(.0001f));
                Assert.That(instance.transform.localScale, Is.EqualTo(rootScale));
                Assert.That(jointPositions.All(p => p.Key.localPosition == p.Value && p.Key.localScale == jointScales[p.Key]), Is.True,
                    "Reach must rotate the connected skeleton without translating joints or stretching bones.");
                Assert.That((float)RuntimeReflection.Invoke(rig, "get_MaximumGripError"), Is.LessThan(.045f));
                foreach (var entry in new[] { (arm: right, bone: HumanBodyBones.RightShoulder), (arm: left, bone: HumanBodyBones.LeftShoulder) })
                {
                    object arm = entry.arm;
                    var shoulder = animator.GetBoneTransform(entry.bone);
                    var upper = (Transform)RuntimeReflection.GetField(arm, "upper");
                    var lower = (Transform)RuntimeReflection.GetField(arm, "lower");
                    var hand = (Transform)RuntimeReflection.GetField(arm, "hand");
                    var palm = (Transform)RuntimeReflection.GetField(arm, "palm");
                    Assert.That(upper.IsChildOf(shoulder) && lower.IsChildOf(upper) && hand.IsChildOf(lower) && palm.IsChildOf(hand), Is.True);
                    Assert.That(Vector3.Distance(upper.position, lower.position), Is.EqualTo((float)RuntimeReflection.GetField(arm, "upperLength")).Within(.0001f));
                    Assert.That(Vector3.Distance(lower.position, hand.position), Is.EqualTo((float)RuntimeReflection.GetField(arm, "lowerLength")).Within(.0001f));
                }
            }
            finally { Object.DestroyImmediate(instance); Object.DestroyImmediate(before); Object.DestroyImmediate(after); }
        }

        [Test]
        public void ImpossibleTargets_KeepGroundedHeightAndRealErrorWithoutStretchingTheSkin()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
            try
            {
                var rig = instance.GetComponent(RuntimeReflection.RuntimeType("SashimiBoy.KevinBodyRig"));
                var animator = instance.GetComponentInChildren<Animator>(true);
                var bones = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r => r.bones).Distinct().ToArray();
                Assert.That(bones.All(b => b != null), Is.True);
                var positions = bones.ToDictionary(t => t, t => t.localPosition);
                var scales = bones.ToDictionary(t => t, t => t.localScale);
                Vector3 preferred = new Vector3(.12f, 0f, -.82f), rootScale = instance.transform.localScale;
                instance.transform.position = preferred;
                RuntimeReflection.SetField(rig, "working", true);
                RuntimeReflection.SetField(rig, "rightTarget", new Vector3(.25f, 100f, .1f));
                RuntimeReflection.SetField(rig, "leftTarget", new Vector3(-.25f, 100f, .1f));
                RuntimeReflection.SetField(rig, "rightRotation", animator.GetBoneTransform(HumanBodyBones.RightHand).rotation);
                RuntimeReflection.SetField(rig, "leftRotation", animator.GetBoneTransform(HumanBodyBones.LeftHand).rotation);
                RuntimeReflection.Invoke(rig, "ApplyWorkingPoseAtRoot", preferred);
                Assert.That(instance.transform.position.y, Is.EqualTo(preferred.y).Within(.0001f));
                Assert.That(instance.transform.localScale, Is.EqualTo(rootScale));
                Assert.That(bones.All(b => b.localPosition == positions[b] && b.localScale == scales[b]), Is.True);
                float error = (float)RuntimeReflection.Invoke(rig, "get_MaximumGripError");
                Assert.That(float.IsNaN(error) || float.IsInfinity(error), Is.False);
                Assert.That(error, Is.GreaterThan(1f), "An impossible target must retain measurable error, not move an anchor or lengthen an arm to manufacture a pass.");
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void ExistingMenuReferenceAndPreviewLayerSurviveTheBodyReplacement()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            var appearance = prefab.GetComponent(RuntimeReflection.RuntimeType("SashimiBoy.KevinAppearance"));
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(appearance, out string guid, out long localId), Is.True);
            Assert.That(guid, Is.EqualTo("9528feda91a928449805e6ceff699f28"));
            Assert.That(localId, Is.EqualTo(7194363315950917319L), "Bootstrap's persisted preview reference must keep resolving without rewriting the Scene.");
            var holder = new GameObject("InactivePortraitRegression"); holder.SetActive(false);
            try
            {
                var actor = Object.Instantiate(prefab, holder.transform);
                foreach (var child in actor.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 0;
                var screen = holder.AddComponent(RuntimeReflection.RuntimeType("SashimiBoy.KevinCustomizationScreen"));
                var label = new GameObject("SelectedName", typeof(RectTransform), typeof(UnityEngine.UI.Text)); label.transform.SetParent(holder.transform, false);
                RuntimeReflection.SetField(screen, "catalog", AssetDatabase.LoadMainAssetAtPath("Assets/_SashimiBoy/Art/Generated/Data/KevinFaceCatalog.asset"));
                RuntimeReflection.SetField(screen, "preview", actor.GetComponent(RuntimeReflection.RuntimeType("SashimiBoy.KevinAppearance")));
                RuntimeReflection.SetField(screen, "selectedName", label.GetComponent<UnityEngine.UI.Text>());
                RuntimeReflection.SetField(screen, "choiceButtons", Array.Empty<UnityEngine.UI.Button>());
                RuntimeReflection.Invoke(screen, "SelectFace", 0);
                Assert.That(actor.GetComponentsInChildren<Renderer>(true).All(r => r.gameObject.layer == 31), Is.True,
                    "Both the replacement body and Owner face must be visible to the dedicated portrait camera.");
            }
            finally { Object.DestroyImmediate(holder); }
        }
        [Test]
        public void ReapplyingAuthoritativeBody_PreservesGeneratedBytesAndHierarchyReferences()
        {
            const string folder = "Assets/_SashimiBoy/Art/Generated/Stage01Playable/Kevin/";
            string[] generated = { Prefab, MeshPath, folder + "MS_OwnerHead.asset", folder + "MS_OwnerHead_PlainFace.asset",
                folder + "MS_OwnerHead_AmbiguousFace.asset", folder + "MS_OwnerHead_WesternFace.asset" };
            string[] assets = generated.Concat(generated.Select(p => p + ".meta")).ToArray();
            string[] before = assets.Select(FileDigest).ToArray();
            const string sourceFolder = "Assets/_SashimiBoy/Art/Source/Characters/Stage03/RiggedBody/";
            string[] sources = Directory.GetFiles(sourceFolder).Where(p => !p.EndsWith(".meta", StringComparison.Ordinal)).OrderBy(p => p).ToArray();
            string[] sourceBefore = sources.Select(FileDigest).ToArray();
            const string bootstrap = "Assets/_SashimiBoy/Scenes/Bootstrap.unity";
            string bootstrapBefore = FileDigest(bootstrap);
            var authoring = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("SashimiBoy.EditorTools.KevinRiggedBodyAuthoring")).FirstOrDefault(t => t != null);
            Assert.That(authoring, Is.Not.Null);
            var build = authoring.GetMethod("BuildBodyPrefab", System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            Assert.That(build, Is.Not.Null);
            build.Invoke(null, null);
            Assert.That(assets.Select(FileDigest).ToArray(), Is.EqualTo(before), "Repeated generation must not churn bone/face/palm identities or change the retained skin.");
            Assert.That(Directory.GetFiles(sourceFolder).Where(p => !p.EndsWith(".meta", StringComparison.Ordinal)).OrderBy(p => p).ToArray(),
                Is.EqualTo(sources), "Authoring must not add or remove received source files.");
            Assert.That(sources.Select(FileDigest).ToArray(), Is.EqualTo(sourceBefore), "Authoring must retain every supplied body/texture byte.");
            Assert.That(FileDigest(bootstrap), Is.EqualTo(bootstrapBefore), "Replacing the prefab must not require rewriting Bootstrap's persisted preview reference.");
        }

        private static string FileDigest(string path)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path)));
        }
        private static float Weight(BoneWeight w, int index) =>
            (w.boneIndex0 == index ? w.weight0 : 0f) + (w.boneIndex1 == index ? w.weight1 : 0f) +
            (w.boneIndex2 == index ? w.weight2 : 0f) + (w.boneIndex3 == index ? w.weight3 : 0f);
    }
}
