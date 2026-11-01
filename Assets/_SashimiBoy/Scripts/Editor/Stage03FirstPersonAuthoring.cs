using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    // Stage3-only first-person index variant. The source skin, every vertex
    // channel, skin weights, bindposes, skeleton and world prefab are preserved.
    public static class Stage03FirstPersonAuthoring
    {
        public const string Root = "Assets/_SashimiBoy/Art/Generated/Stage03FirstPerson";
        public const string MeshPath = Root + "/Meshes/MS_Stage03_FirstPersonArms.asset";
        public const string ReportPath = Root + "/Stage03FirstPersonGeometry.json";
        public const string RendererName = "Stage03_FirstPersonArms";
        private const float MinimumArmWeight = .5f;

        [Serializable] public sealed class Report
        {
            public string algorithm = "Stage03FirstPerson-2";
            public string sourceMeshPath, sourceMeshGuid, sourceMeshSha256;
            public string sourcePrefabPath, sourcePrefabGuid, sourcePrefabSha256;
            public string rawBodyPath, rawBodyGuid, rawBodySha256, rawBodyMetaSha256;
            public string generatedMeshPath, generatedMeshGuid;
            public int sourceVertices, sourceTriangles, keptTriangles, removedTriangles;
            public int leftArmTriangles, rightArmTriangles, leftHandIncidentVertices, rightHandIncidentVertices;
            public float minimumArmWeight = MinimumArmWeight;
            public bool vertexStreamsPreserved, weightsPreserved, bindposesPreserved;
            public string filterContract = "Keep a source triangle only if every corner has total weight >=0.5 in the same actual left.lower or right.lower descendant chain. This first-person forearm/hand surface excludes upper-arm/shoulder clothing at its original skin-weight boundary. Preserve all source vertices/channels/skin data and original retained indices; do not construct new anatomy or a second skeleton. Every nonzero hand-influenced triangle and its incident vertices must remain represented.";
        }

        public static void BuildMeshSourceOwner()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Build the first-person arm index variant outside Play Mode.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KevinEmbodimentAuthoring.PrefabPath);
            if (prefab == null) throw new InvalidOperationException("Generate the provided Kevin body before the Stage3 arm variant.");
            var rig = prefab.GetComponent<KevinBodyRig>();
            if (rig == null || rig.left.lower == null || rig.right.lower == null || rig.left.hand == null || rig.right.hand == null)
                throw new InvalidOperationException("The provided body has incomplete attached arm chains.");
            var sourceRenderer = SourceBody(rig);
            Mesh source = sourceRenderer.sharedMesh;
            if (!source.isReadable || source.vertexCount != 18976 || source.blendShapeCount != 0)
                throw new InvalidOperationException("The pinned readable provided body geometry changed; re-verify the arm index contract.");
            string sourcePath = AssetDatabase.GetAssetPath(source);
            string rawDependencyBefore = AssetDatabase.GetAssetDependencyHash(KevinRiggedBodyAuthoring.BodyPath).ToString();
            var report = new Report
            {
                sourceMeshPath = sourcePath, sourceMeshGuid = AssetDatabase.AssetPathToGUID(sourcePath), sourceMeshSha256 = Hash(sourcePath),
                sourcePrefabPath = KevinEmbodimentAuthoring.PrefabPath,
                sourcePrefabGuid = AssetDatabase.AssetPathToGUID(KevinEmbodimentAuthoring.PrefabPath), sourcePrefabSha256 = Hash(KevinEmbodimentAuthoring.PrefabPath),
                rawBodyPath = KevinRiggedBodyAuthoring.BodyPath, rawBodyGuid = AssetDatabase.AssetPathToGUID(KevinRiggedBodyAuthoring.BodyPath),
                rawBodySha256 = Hash(KevinRiggedBodyAuthoring.BodyPath), rawBodyMetaSha256 = Hash(KevinRiggedBodyAuthoring.BodyPath + ".meta"),
                sourceVertices = source.vertexCount, generatedMeshPath = MeshPath
            };
            Transform[] bones = sourceRenderer.bones;
            BoneWeight[] weights = source.boneWeights;
            if (weights.Length != source.vertexCount) throw new InvalidOperationException("The provided body skin representation changed.");
            var influenceCounts = source.GetBonesPerVertex();
            if (influenceCounts.Length != source.vertexCount) throw new InvalidOperationException("The provided body's exact skin influence counts are missing.");
            foreach (byte count in influenceCounts) if (count == 0 || count > 4)
                throw new InvalidOperationException("The Stage3 arm index variant requires the original four-weight body representation.");
            var left = new float[weights.Length]; var right = new float[weights.Length];
            var leftHand = new bool[weights.Length]; var rightHand = new bool[weights.Length];
            bool Belongs(int index, Transform ancestor) => index >= 0 && index < bones.Length && bones[index] != null &&
                (bones[index] == ancestor || bones[index].IsChildOf(ancestor));
            float Influence(BoneWeight w, Transform ancestor) =>
                (Belongs(w.boneIndex0, ancestor) ? w.weight0 : 0) + (Belongs(w.boneIndex1, ancestor) ? w.weight1 : 0) +
                (Belongs(w.boneIndex2, ancestor) ? w.weight2 : 0) + (Belongs(w.boneIndex3, ancestor) ? w.weight3 : 0);
            for (int i = 0; i < weights.Length; i++)
            {
                left[i] = Influence(weights[i], rig.left.lower); right[i] = Influence(weights[i], rig.right.lower);
                leftHand[i] = Influence(weights[i], rig.left.hand) > 0; rightHand[i] = Influence(weights[i], rig.right.hand) > 0;
            }
            var leftIncident = (bool[])leftHand.Clone(); var rightIncident = (bool[])rightHand.Clone();
            var retained = new int[source.subMeshCount][];
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                if (source.GetTopology(sub) != MeshTopology.Triangles) throw new InvalidOperationException("The supplied body must use triangle submeshes.");
                int[] triangles = source.GetTriangles(sub); var kept = new List<int>();
                report.sourceTriangles += triangles.Length / 3;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    bool leftArm = left[a] >= MinimumArmWeight && left[b] >= MinimumArmWeight && left[c] >= MinimumArmWeight;
                    bool rightArm = right[a] >= MinimumArmWeight && right[b] >= MinimumArmWeight && right[c] >= MinimumArmWeight;
                    bool incidentLeft = leftHand[a] || leftHand[b] || leftHand[c];
                    bool incidentRight = rightHand[a] || rightHand[b] || rightHand[c];
                    if (incidentLeft) leftIncident[a] = leftIncident[b] = leftIncident[c] = true;
                    if (incidentRight) rightIncident[a] = rightIncident[b] = rightIncident[c] = true;
                    if (!leftArm && !rightArm)
                    {
                        if (incidentLeft || incidentRight) throw new InvalidOperationException("The first-person arm predicate would remove actual hand/incident-triangle geometry.");
                        continue;
                    }
                    if (leftArm) report.leftArmTriangles++; if (rightArm) report.rightArmTriangles++;
                    kept.Add(a); kept.Add(b); kept.Add(c);
                }
                retained[sub] = kept.ToArray(); report.keptTriangles += kept.Count / 3;
            }
            report.leftHandIncidentVertices = leftIncident.Count(value => value);
            report.rightHandIncidentVertices = rightIncident.Count(value => value);
            report.removedTriangles = report.sourceTriangles - report.keptTriangles;
            if (report.leftArmTriangles == 0 || report.rightArmTriangles == 0 || report.removedTriangles == 0 || report.leftHandIncidentVertices != 1190)
                throw new InvalidOperationException("The provided two-arm/complete support-hand geometry contract was not met.");
            EnsureFolder(Root); EnsureFolder(Root + "/Meshes");
            Mesh result = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            bool created = result == null;
            if (created) result = new Mesh();
            CopyNativeVertexData(source, result);
            result.name = "MS_Stage03_FirstPersonArms";
            result.subMeshCount = retained.Length;
            for (int sub = 0; sub < retained.Length; sub++) result.SetTriangles(retained[sub], sub, false);
            result.bounds = source.bounds;
            result.UploadMeshData(false);
            if (created) AssetDatabase.CreateAsset(result, MeshPath);
            EditorUtility.SetDirty(result); AssetDatabase.SaveAssetIfDirty(result);
            report.generatedMeshGuid = AssetDatabase.AssetPathToGUID(MeshPath);
            report.vertexStreamsPreserved = VertexStreamsEqual(source, result);
            report.weightsPreserved = source.boneWeights.SequenceEqual(result.boneWeights);
            report.bindposesPreserved = source.bindposes.SequenceEqual(result.bindposes);
            if (!report.vertexStreamsPreserved || !report.weightsPreserved || !report.bindposesPreserved)
                throw new InvalidOperationException("The first-person index variant altered original vertex or skin data.");
            if (Hash(sourcePath) != report.sourceMeshSha256 || Hash(KevinEmbodimentAuthoring.PrefabPath) != report.sourcePrefabSha256 ||
                Hash(KevinRiggedBodyAuthoring.BodyPath) != report.rawBodySha256 || Hash(KevinRiggedBodyAuthoring.BodyPath + ".meta") != report.rawBodyMetaSha256 ||
                AssetDatabase.GetAssetDependencyHash(KevinRiggedBodyAuthoring.BodyPath).ToString() != rawDependencyBefore)
                throw new InvalidOperationException("The first-person generator modified a protected body source/prefab.");
            WriteText(ReportPath, JsonUtility.ToJson(report, true) + "\n");
        }

        public static void Bind(Stage03GameController controller)
        {
            if (controller == null || controller.cooking == null || controller.cooking.body == null ||
                controller.gameObject.scene.path != Stage03PlayableAuthoring.ScenePath)
                throw new InvalidOperationException("Bind first-person rendering only in the authored Stage3 scene.");
            BuildMeshSourceOwner();
            var source = SourceBody(controller.cooking.body);
            Transform existing = source.transform.parent.Find(RendererName);
            GameObject node;
            if (existing != null) node = existing.gameObject;
            else
            {
                node = EditorUtility.CreateGameObjectWithHideFlags(RendererName, HideFlags.HideAndDontSave);
                SceneManager.MoveGameObjectToScene(node, source.gameObject.scene);
                node.transform.SetParent(source.transform.parent, false); node.hideFlags = HideFlags.None;
            }
            node.layer = source.gameObject.layer;
            node.transform.localPosition = source.transform.localPosition; node.transform.localRotation = source.transform.localRotation;
            node.transform.localScale = source.transform.localScale;
            if (node.GetComponent<Animator>() != null) throw new InvalidOperationException("The first-person arm renderer must share the original skeleton, not an Animator.");
            var arms = node.GetComponent<SkinnedMeshRenderer>(); if (arms == null) arms = node.AddComponent<SkinnedMeshRenderer>();
            arms.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            arms.bones = source.bones; arms.rootBone = source.rootBone; arms.sharedMaterials = source.sharedMaterials;
            arms.quality = source.quality; arms.localBounds = source.localBounds; arms.updateWhenOffscreen = source.updateWhenOffscreen;
            arms.skinnedMotionVectors = source.skinnedMotionVectors; arms.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
            arms.receiveShadows = source.receiveShadows; arms.lightProbeUsage = source.lightProbeUsage;
            arms.reflectionProbeUsage = source.reflectionProbeUsage; arms.probeAnchor = source.probeAnchor;
            arms.motionVectorGenerationMode = source.motionVectorGenerationMode; arms.renderingLayerMask = source.renderingLayerMask;
            arms.shadowCastingMode = ShadowCastingMode.Off;
            // Runtime owns enabling this Stage3-only render policy and restoring
            // the original full body. Asset/world defaults remain untouched.
            arms.enabled = false;
            controller.cooking.firstPersonBody = source; controller.cooking.firstPersonArms = arms;
            EditorUtility.SetDirty(arms); EditorUtility.SetDirty(controller.cooking);
            if (PrefabUtility.IsPartOfPrefabInstance(controller.cooking)) PrefabUtility.RecordPrefabInstancePropertyModifications(controller.cooking);
        }

        private static SkinnedMeshRenderer SourceBody(KevinBodyRig rig)
        {
            var bodies = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null &&
                AssetDatabase.GetAssetPath(r.sharedMesh) == KevinRiggedBodyAuthoring.BodyMeshPath).ToArray();
            if (bodies.Length != 1) throw new InvalidOperationException("Exactly one original provided body skin is required.");
            return bodies[0];
        }

        private static void CopyNativeVertexData(Mesh source, Mesh result)
        {
            result.Clear(false); result.indexFormat = source.indexFormat;
            result.SetVertexBufferParams(source.vertexCount, source.GetVertexAttributes());
            result.bindposes = source.bindposes;
            // Match the original body generator's four-weight setter, then
            // restore every native vertex-stream byte without quantizing or
            // rebuilding the source skin representation.
            result.boneWeights = source.boneWeights;
            // Copy every source stream byte, including tangent/UV layouts and
            // blend weights/indices, after creating the native skin buffers.
            using (var data = Mesh.AcquireReadOnlyMeshData(source))
                for (int stream = 0; stream < source.vertexBufferCount; stream++)
                {
                    var bytes = data[0].GetVertexData<byte>(stream);
                    result.SetVertexBufferData(bytes, 0, 0, bytes.Length, stream,
                        MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                }
        }

        private static bool VertexStreamsEqual(Mesh first, Mesh second)
        {
            if (first.vertexCount != second.vertexCount || first.vertexBufferCount != second.vertexBufferCount ||
                !first.GetVertexAttributes().SequenceEqual(second.GetVertexAttributes())) return false;
            using (var a = Mesh.AcquireReadOnlyMeshData(first))
            using (var b = Mesh.AcquireReadOnlyMeshData(second))
                for (int stream = 0; stream < first.vertexBufferCount; stream++)
                {
                    var x = a[0].GetVertexData<byte>(stream); var y = b[0].GetVertexData<byte>(stream);
                    if (x.Length != y.Length) return false;
                    for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
                }
            return true;
        }
        private static string Hash(string path)
        {
            using var sha = SHA256.Create(); using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/')); EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }
        private static void WriteText(string path, string text)
        {
            if (!File.Exists(path) || File.ReadAllText(path) != text) File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
