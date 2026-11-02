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

namespace SashimiBoy.Tests
{
    // The FPS surface is a camera-specific triangle subset of the real skin.
    // These checks do not replace actual stroke/occlusion or Owner visual review.
    public sealed class Stage03FirstPersonGeometryEditModeTests
    {
        const string Root = "Assets/_SashimiBoy/Art/Generated/Stage03FirstPerson";
        const string ArmsMeshPath = Root + "/Meshes/MS_Stage03_FirstPersonArms.asset";
        const string ReportPath = Root + "/Stage03FirstPersonGeometry.json";
        const string BodyMeshPath = "Assets/_SashimiBoy/Art/Generated/Stage01Playable/Kevin/MS_RiggedBody_WithoutHead.asset";
        const string BodyPrefabPath = "Assets/_SashimiBoy/Art/Generated/Stage01Playable/Kevin/PF_Kevin_Complete.prefab";
        const string RawBodyPath = "Assets/_SashimiBoy/Art/Source/Characters/Stage03/RiggedBody/RiggedBody.fbx";
        const string StageScenePath = "Assets/_SashimiBoy/Scenes/Stage03_Flounder.unity";
        static object Get(object target, string name) => RuntimeReflection.GetField(target, name);

        [Serializable] sealed class Receipt
        {
            public string algorithm, sourceMeshPath, sourceMeshGuid, sourceMeshSha256, sourcePrefabPath, sourcePrefabGuid, sourcePrefabSha256;
            public string rawBodyPath, rawBodyGuid, rawBodySha256, rawBodyMetaSha256, generatedMeshPath, generatedMeshGuid, filterContract;
            public int sourceVertices, sourceTriangles, keptTriangles, removedTriangles, leftArmTriangles, rightArmTriangles;
            public int leftHandIncidentVertices, rightHandIncidentVertices;
            public float minimumArmWeight;
            public bool vertexStreamsPreserved, weightsPreserved, bindposesPreserved;
        }
        sealed class Reference
        {
            public Mesh source, arms;
            public SkinnedMeshRenderer renderer;
            public bool[] leftArm, rightArm, leftHand, rightHand;
            public BoneWeight[] weights;
        }

        static Reference ReadReference()
        {
            var source = AssetDatabase.LoadAssetAtPath<Mesh>(BodyMeshPath);
            var arms = AssetDatabase.LoadAssetAtPath<Mesh>(ArmsMeshPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPrefabPath);
            Assert.That(source, Is.Not.Null); Assert.That(arms, Is.Not.Null); Assert.That(prefab, Is.Not.Null);
            var renderer = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.sharedMesh == source);
            var rig = prefab.GetComponent(RuntimeReflection.RuntimeType("SashimiBoy.KevinBodyRig"));
            var left = Get(rig, "left"); var right = Get(rig, "right");
            bool[] Descendants(Transform ancestor) => renderer.bones.Select(b => b != null && (b == ancestor || b.IsChildOf(ancestor))).ToArray();
            return new Reference { source = source, arms = arms, renderer = renderer, weights = source.boneWeights,
                leftArm = Descendants((Transform)Get(left, "lower")), rightArm = Descendants((Transform)Get(right, "lower")),
                leftHand = Descendants((Transform)Get(left, "hand")), rightHand = Descendants((Transform)Get(right, "hand")) };
        }

        [Test]
        public void FirstPersonArms_PreserveEverySourceVertexByteAndSkinBinding_AndOnlyReplaceTheIndexBuffer()
        {
            var r = ReadReference();
            Assert.That(AssetDatabase.AssetPathToGUID(BodyMeshPath), Is.EqualTo("6863f0f2eb51bdd42b96c18eea65d5ee"));
            Assert.That(AssetDatabase.AssetPathToGUID(RawBodyPath), Is.EqualTo("b905555ab3206ee4e9d306ab55c16922"));
            Assert.That(r.source.vertexCount, Is.EqualTo(18976), "The approved full-body source must retain its complete vertex stream.");
            Assert.That(r.arms.vertexCount, Is.EqualTo(r.source.vertexCount));
            Assert.That(r.source.isReadable && r.arms.isReadable, Is.True);
            Assert.That(VertexBytes(ArmsMeshPath), Is.EqualTo(VertexBytes(BodyMeshPath)).IgnoreCase,
                "All native vertex streams, including UVs/weights, must remain byte-identical at the same indices.");
            Assert.That(r.arms.vertices, Is.EqualTo(r.source.vertices));
            Assert.That(r.arms.normals, Is.EqualTo(r.source.normals));
            Assert.That(r.arms.tangents, Is.EqualTo(r.source.tangents));
            Assert.That(r.arms.uv, Is.EqualTo(r.source.uv));
            Assert.That(r.arms.boneWeights, Is.EqualTo(r.source.boneWeights));
            Assert.That(r.arms.bindposes.Length, Is.EqualTo(r.source.bindposes.Length));
            for (int bone = 0; bone < r.source.bindposes.Length; bone++)
                for (int element = 0; element < 16; element++)
                    Assert.That(FloatBits(r.arms.bindposes[bone][element]), Is.EqualTo(FloatBits(r.source.bindposes[bone][element])), "bindpose " + bone + "/" + element);
            Assert.That(r.arms.subMeshCount, Is.EqualTo(r.source.subMeshCount));
            Assert.That(r.arms.triangles.Length, Is.GreaterThan(0));
            Assert.That(r.arms.triangles.Length, Is.LessThan(r.source.triangles.Length), "The camera surface must remove torso/leg faces without replacing or shrinking the full body.");
        }

        [Test]
        public void FilteredTriangles_BelongEntirelyToOneForearmChain_AndRetainAllOriginalHandIncidentGeometry()
        {
            var r = ReadReference();
            int leftCount = 0, rightCount = 0;
            var used = new HashSet<int>();
            var leftHandIncident = new HashSet<int>(Enumerable.Range(0, r.weights.Length).Where(i => Weight(r.weights[i], r.leftHand) > 0));
            var rightHandIncident = new HashSet<int>(Enumerable.Range(0, r.weights.Length).Where(i => Weight(r.weights[i], r.rightHand) > 0));
            for (int sub = 0; sub < r.source.subMeshCount; sub++)
            {
                Assert.That(r.source.GetTopology(sub), Is.EqualTo(MeshTopology.Triangles));
                Assert.That(r.arms.GetTopology(sub), Is.EqualTo(MeshTopology.Triangles));
                int[] source = r.source.GetTriangles(sub), actual = r.arms.GetTriangles(sub);
                var expected = new List<int>();
                var actualFaces = new HashSet<(int, int, int)>();
                for (int i = 0; i < actual.Length; i += 3)
                {
                    actualFaces.Add((actual[i], actual[i + 1], actual[i + 2]));
                    used.Add(actual[i]); used.Add(actual[i + 1]); used.Add(actual[i + 2]);
                }
                for (int i = 0; i < source.Length; i += 3)
                {
                    int a = source[i], b = source[i + 1], c = source[i + 2];
                    bool left = Weight(r.weights[a], r.leftArm) >= .5f && Weight(r.weights[b], r.leftArm) >= .5f && Weight(r.weights[c], r.leftArm) >= .5f;
                    bool right = Weight(r.weights[a], r.rightArm) >= .5f && Weight(r.weights[b], r.rightArm) >= .5f && Weight(r.weights[c], r.rightArm) >= .5f;
                    if (left || right)
                    {
                        expected.Add(a); expected.Add(b); expected.Add(c);
                        if (left) leftCount++; if (right) rightCount++;
                    }
                    foreach (var pair in new[] { (mask: r.leftHand, incident: leftHandIncident), (mask: r.rightHand, incident: rightHandIncident) })
                    {
                        if (Weight(r.weights[a], pair.mask) <= 0 && Weight(r.weights[b], pair.mask) <= 0 && Weight(r.weights[c], pair.mask) <= 0) continue;
                        pair.incident.UnionWith(new[] { a, b, c });
                        Assert.That(actualFaces.Contains((a, b, c)), Is.True, "A source triangle touching the real hand/finger skin must remain intact.");
                    }
                }
                Assert.That(actual, Is.EqualTo(expected.ToArray()), "Only complete same-forearm triangles may survive, in their original index order; upper-arm/shoulder and mixed torso/leg faces must be absent.");
            }
            Assert.That(leftCount, Is.GreaterThan(0)); Assert.That(rightCount, Is.GreaterThan(0));
            Assert.That(leftHandIncident.Count, Is.EqualTo(1190)); Assert.That(rightHandIncident.Count, Is.EqualTo(1129));
            Assert.That(leftHandIncident.All(used.Contains) && rightHandIncident.All(used.Contains), Is.True);
            var receipt = ReadReceipt();
            Assert.That(receipt.leftArmTriangles, Is.EqualTo(leftCount)); Assert.That(receipt.rightArmTriangles, Is.EqualTo(rightCount));
            Assert.That(receipt.leftHandIncidentVertices, Is.EqualTo(leftHandIncident.Count));
            Assert.That(receipt.rightHandIncidentVertices, Is.EqualTo(rightHandIncident.Count));
        }

        [Test]
        public void ActualStage3Scene_BindsTheArmSubsetToTheSameRealBonesTransformMaterialsAndQuality()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(StageScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(StageScenePath, OpenSceneMode.Additive);
            try
            {
                var type = RuntimeReflection.RuntimeType("SashimiBoy.Stage03CookingView");
                var cooking = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren(type, true)).Single();
                var body = (SkinnedMeshRenderer)Get(cooking, "firstPersonBody");
                var arms = (SkinnedMeshRenderer)Get(cooking, "firstPersonArms");
                Assert.That(body, Is.Not.Null); Assert.That(arms, Is.Not.Null); Assert.That(arms, Is.Not.SameAs(body));
                Assert.That(AssetDatabase.GetAssetPath(body.sharedMesh), Is.EqualTo(BodyMeshPath), "The complete body remains bound for world/external views and shadows.");
                Assert.That(AssetDatabase.GetAssetPath(arms.sharedMesh), Is.EqualTo(ArmsMeshPath));
                Assert.That(arms.transform.parent, Is.SameAs(body.transform.parent));
                Assert.That(arms.transform.localPosition, Is.EqualTo(body.transform.localPosition));
                Assert.That(arms.transform.localRotation, Is.EqualTo(body.transform.localRotation));
                Assert.That(arms.transform.localScale, Is.EqualTo(body.transform.localScale));
                Assert.That(arms.bones, Is.EqualTo(body.bones)); Assert.That(arms.rootBone, Is.SameAs(body.rootBone));
                Assert.That(arms.sharedMaterials, Is.EqualTo(body.sharedMaterials)); Assert.That(arms.quality, Is.EqualTo(body.quality));
                Assert.That(arms.GetComponentInChildren<Animator>(true), Is.Null, "The FPS surface follows the original skeleton and must not create an independent animator.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (opened && scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void RegeneratingArms_IsByteStable_AndPreservesTheFullBodyRawAssetsEverySceneAndOpenSceneState()
        {
            var receipt = ReadReceipt();
            Assert.That(receipt.algorithm, Is.EqualTo("Stage03FirstPerson-2"));
            Assert.That(receipt.sourceMeshPath, Is.EqualTo(BodyMeshPath)); Assert.That(receipt.sourcePrefabPath, Is.EqualTo(BodyPrefabPath));
            Assert.That(receipt.rawBodyPath, Is.EqualTo(RawBodyPath)); Assert.That(receipt.generatedMeshPath, Is.EqualTo(ArmsMeshPath));
            Assert.That(receipt.sourceMeshGuid, Is.EqualTo(AssetDatabase.AssetPathToGUID(BodyMeshPath)));
            Assert.That(receipt.sourcePrefabGuid, Is.EqualTo(AssetDatabase.AssetPathToGUID(BodyPrefabPath)));
            Assert.That(receipt.rawBodyGuid, Is.EqualTo(AssetDatabase.AssetPathToGUID(RawBodyPath)));
            Assert.That(receipt.generatedMeshGuid, Is.EqualTo(AssetDatabase.AssetPathToGUID(ArmsMeshPath)));
            Assert.That(receipt.sourceMeshSha256, Is.EqualTo(Hash(BodyMeshPath)));
            Assert.That(receipt.sourcePrefabSha256, Is.EqualTo(Hash(BodyPrefabPath)));
            Assert.That(receipt.rawBodySha256, Is.EqualTo(Hash(RawBodyPath)));
            Assert.That(receipt.rawBodyMetaSha256, Is.EqualTo(Hash(RawBodyPath + ".meta")));
            Assert.That(receipt.minimumArmWeight, Is.EqualTo(.5f)); Assert.That(receipt.filterContract, Is.Not.Empty);
            Assert.That(receipt.vertexStreamsPreserved && receipt.weightsPreserved && receipt.bindposesPreserved, Is.True);
            var r = ReadReference();
            Assert.That(receipt.sourceVertices, Is.EqualTo(r.source.vertexCount));
            Assert.That(receipt.sourceTriangles, Is.EqualTo(r.source.triangles.Length / 3));
            Assert.That(receipt.keptTriangles, Is.EqualTo(r.arms.triangles.Length / 3));
            Assert.That(receipt.keptTriangles + receipt.removedTriangles, Is.EqualTo(receipt.sourceTriangles));
            var protectedFiles = Directory.GetFiles(Path.GetDirectoryName(RawBodyPath), "*", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles("Assets/_SashimiBoy/Scenes", "*", SearchOption.AllDirectories))
                .Concat(new[] { BodyMeshPath, BodyMeshPath + ".meta", BodyPrefabPath, BodyPrefabPath + ".meta" })
                .OrderBy(p => p, StringComparer.Ordinal).ToArray();
            var protectedBefore = protectedFiles.ToDictionary(p => p, Hash);
            AssertCanonicalGeneratedText();
            var generatedBefore = Snapshot(Root);
            Scene previous = SceneManager.GetActiveScene();
            Scene[] scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
            bool[] dirty = scenes.Select(s => s.isDirty).ToArray();
            int[][] roots = scenes.Select(s => s.GetRootGameObjects().Select(go => go.GetInstanceID()).OrderBy(i => i).ToArray()).ToArray();
            RuntimeReflection.InvokeStatic("SashimiBoy.EditorTools.Stage03FirstPersonAuthoring", "BuildMeshSourceOwner");
            AssertCanonicalGeneratedText();
            Assert.That(Snapshot(Root), Is.EquivalentTo(generatedBefore));
            Assert.That(protectedFiles.ToDictionary(p => p, Hash), Is.EquivalentTo(protectedBefore));
            Assert.That(SceneManager.sceneCount, Is.EqualTo(scenes.Length)); Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(previous));
            for (int i = 0; i < scenes.Length; i++)
            {
                Assert.That(scenes[i].isDirty, Is.EqualTo(dirty[i]));
                Assert.That(scenes[i].GetRootGameObjects().Select(go => go.GetInstanceID()).OrderBy(id => id).ToArray(), Is.EqualTo(roots[i]));
            }
        }

        static Receipt ReadReceipt()
        {
            Assert.That(File.Exists(ReportPath), Is.True, ReportPath);
            return JsonUtility.FromJson<Receipt>(File.ReadAllText(ReportPath));
        }
        static float Weight(BoneWeight value, bool[] mask) =>
            Contribution(value.boneIndex0, value.weight0, mask) + Contribution(value.boneIndex1, value.weight1, mask) +
            Contribution(value.boneIndex2, value.weight2, mask) + Contribution(value.boneIndex3, value.weight3, mask);
        static float Contribution(int index, float weight, bool[] mask) => index >= 0 && index < mask.Length && mask[index] ? weight : 0;
        static int FloatBits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        static string VertexBytes(string path)
        {
            foreach (string line in File.ReadLines(path))
            {
                string value = line.TrimStart();
                if (value.StartsWith("_typelessdata:", StringComparison.Ordinal)) return value.Substring(value.IndexOf(':') + 1).Trim();
            }
            Assert.Fail("Readable serialized vertex stream missing: " + path); return null;
        }
        static Dictionary<string, string> Snapshot(string directory)
        {
            var paths = Directory.GetFiles(directory, "*", SearchOption.AllDirectories).AsEnumerable();
            if (File.Exists(directory + ".meta")) paths = paths.Append(directory + ".meta");
            return paths.ToDictionary(p => p, Hash);
        }
        static void AssertCanonicalGeneratedText()
        {
            foreach (string path in Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Append(Root + ".meta"))
            {
                if (!new[] { ".asset", ".meta", ".json" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) continue;
                byte[] bytes = File.ReadAllBytes(path);
                if (Path.GetExtension(path).Equals(".asset", StringComparison.OrdinalIgnoreCase) && bytes.Length > 0 && bytes[0] != '%' && bytes[0] != 0xef) continue;
                Assert.That(bytes.Take(3), Is.Not.EqualTo(new byte[] { 0xef, 0xbb, 0xbf }), "Generated UTF-8 text must omit a BOM: " + path);
                string text = new System.Text.UTF8Encoding(false, true).GetString(bytes);
                Assert.That(text, Does.Not.Contain("\r"), "Generated text must use LF: " + path);
                Assert.That(text.EndsWith("\n", StringComparison.Ordinal), Is.True, "Generated text requires a final newline: " + path);
                Assert.That(text.Split('\n').All(line => line.Length == 0 || (line[line.Length - 1] != ' ' && line[line.Length - 1] != '\t')), Is.True,
                    "Unity empty YAML scalars must not retain trailing whitespace: " + path);
            }
        }
        static string Hash(string path)
        {
            using var hash = SHA256.Create(); using var stream = File.OpenRead(path);
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
