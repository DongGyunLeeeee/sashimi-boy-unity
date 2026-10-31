using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    /// <summary>Preserves the supplied skin and the existing Kevin prefab/face identities.</summary>
    public static class KevinRiggedBodyAuthoring
    {
        public const string SourceRoot = "Assets/_SashimiBoy/Art/Source/Characters/Stage03/RiggedBody/";
        public const string BodyPath = SourceRoot + "RiggedBody.fbx";
        public const string BodyMeshPath = KevinEmbodimentAuthoring.Output + "/MS_RiggedBody_WithoutHead.asset";
        public const string BodyMaterialPath = KevinEmbodimentAuthoring.Output + "/MAT_RiggedBody_Surface.mat";
        private const float NeckHeight = 1.49f;
        private const float BodyCutHeight = 1.55f;
        private static readonly string DefaultEvidence = Path.GetFullPath(Path.Combine("Logs", "OwnerFeedback87", "RiggedBody",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0,8)));
        public static string EvidenceDirectory
        {
            get
            {
                string configured = Environment.GetEnvironmentVariable("SASHIMI_BODY_EVIDENCE");
                if (string.IsNullOrWhiteSpace(configured)) return DefaultEvidence;
                if (!Path.IsPathRooted(configured)) throw new InvalidOperationException("Body evidence needs an absolute output path.");
                return Path.GetFullPath(configured);
            }
        }

        public static void BuildBatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before body authoring.");
            for (int i=0;i<SceneManager.sceneCount;i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save unsaved scenes before body authoring.");
            string original=SourceHash();
            string guid=AssetDatabase.AssetPathToGUID(BodyPath);
            Directory.CreateDirectory(EvidenceDirectory);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            KevinEmbodimentAuthoring.BuildBody();
            AssetDatabase.SaveAssets();
            if (SourceHash()!=original || AssetDatabase.AssetPathToGUID(BodyPath)!=guid)
                throw new InvalidOperationException("The supplied body source bytes or GUID changed.");
            Debug.Log("[OwnerFeedback87Body] Supplied RiggedBody and all four saved face IDs bound to the existing Kevin prefab. Original FBX/GUID retained; actual-scene reach/input and Owner review remain required. Evidence: "+EvidenceDirectory);
        }

        private static string SourceHash()
        {
            using var sha=SHA256.Create();
            using var stream=File.OpenRead(BodyPath);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }

        public static void ConfigureImporter()
        {
            var importer = AssetImporter.GetAtPath(BodyPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("The supplied RiggedBody importer is missing.");
            bool changed = !importer.isReadable || importer.animationType != ModelImporterAnimationType.Human ||
                importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel || importer.optimizeGameObjects;
            if (changed)
            {
                importer.isReadable = true;
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.optimizeGameObjects = false;
                importer.SaveAndReimport();
            }
            string normalPath = SourceRoot + "mannequin_3d_model_normal.JPEG";
            var normal = AssetImporter.GetAtPath(normalPath) as TextureImporter;
            if (normal == null) throw new InvalidOperationException("The supplied body normal map is missing.");
            if (normal.textureType != TextureImporterType.NormalMap || normal.sRGBTexture)
            {
                normal.textureType = TextureImporterType.NormalMap;
                normal.sRGBTexture = false;
                normal.SaveAndReimport();
            }
        }

        internal static void BuildBodyPrefab()
        {
            ConfigureImporter();
            Directory.CreateDirectory(KevinEmbodimentAuthoring.Output);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath);
            if (asset == null) throw new InvalidOperationException("The supplied RiggedBody model is missing.");
            // Preserve root component fileIDs: Bootstrap directly references KevinAppearance.
            var root = PrefabUtility.LoadPrefabContents(KevinEmbodimentAuthoring.PrefabPath);
            try
            {
                var rig = root.GetComponent<KevinBodyRig>() ?? root.AddComponent<KevinBodyRig>();
                var existing = root.GetComponentInChildren<Animator>(true);
                bool reuse = existing != null && AssetDatabase.GetAssetPath(existing.avatar) == BodyPath;
                GameObject model;
                if (reuse)
                {
                    model = existing.gameObject;
                    RestoreSourcePose(model.transform, asset.transform);
                }
                else
                {
                    for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                    model = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
                }
                var animator = model.GetComponent<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.isHuman)
                    throw new InvalidOperationException("RiggedBody must import with a valid Humanoid Avatar.");
                animator.enabled = false;
                Transform Bone(HumanBodyBones id) => RequireBone(animator, id);
                var sourceSkin = reuse
                    ? model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "RiggedBody_Surface")
                    : model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
                if (!reuse)
                {
                    float feet = sourceSkin.bounds.min.y;
                    float span = Bone(HumanBodyBones.Neck).position.y - feet;
                    if ((float.IsNaN(span) || float.IsInfinity(span)) || span <= .1f) throw new InvalidOperationException("Invalid body neck/feet bounds.");
                    float scale = NeckHeight / span;
                    model.transform.localScale *= scale;
                    model.transform.localPosition += Vector3.up * (-feet * scale);
                }
                else if (Mathf.Abs(Bone(HumanBodyBones.Neck).position.y - NeckHeight) > .001f)
                    throw new InvalidOperationException("Existing generated body has a changed rest pose/scale; inspect before replacing it.");

                // Keep the artist's original lower-body vertices/UVs/weights. Only the supplied
                // head is clipped above the raised shirt collar; the retained Owner neck overlaps it.
                Mesh originalSkin = asset.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single().sharedMesh;
                Mesh mesh = BuildBodyMesh(sourceSkin, originalSkin, BodyCutHeight);
                mesh = SaveMesh(mesh, BodyMeshPath);
                sourceSkin.sharedMesh = mesh;
                sourceSkin.sharedMaterials = Enumerable.Repeat(BuildMaterial(), mesh.subMeshCount).ToArray();
                sourceSkin.localBounds = mesh.bounds;
                sourceSkin.updateWhenOffscreen = true;
                sourceSkin.name = "RiggedBody_Surface";

                ConfigureArm(rig.right, animator, true);
                ConfigureArm(rig.left, animator, false);
                ConfigureFingers(rig, animator);
                rig.head = Bone(HumanBodyBones.Head);
                rig.cookingSpine = Bone(HumanBodyBones.Spine);
                rig.spineRest = rig.cookingSpine.localRotation;
                rig.spineBendAxis = rig.cookingSpine.InverseTransformDirection(root.transform.right).normalized;
                var face = KevinEmbodimentAuthoring.BuildOwnerHead(rig.head, Bone(HumanBodyBones.Neck), Bone(HumanBodyBones.Chest));
                rig.headRenderers = face.GetComponentsInChildren<Renderer>();
                var appearance = root.GetComponent<KevinAppearance>() ?? root.AddComponent<KevinAppearance>();
                appearance.faceRenderer = face.GetComponent<SkinnedMeshRenderer>();
                rig.eyeAnchor = rig.head.Find("KevinEyes") ?? new GameObject("KevinEyes").transform;
                rig.eyeAnchor.SetParent(rig.head, false);
                rig.eyeAnchor.position = new Vector3(0f, 1.645f, .06f);
                rig.ApplyPose();
                if (PrefabUtility.SaveAsPrefabAsset(root, KevinEmbodimentAuthoring.PrefabPath) == null)
                    throw new InvalidOperationException("Kevin prefab could not be saved.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void RestoreSourcePose(Transform model, Transform source)
        {
            // Reset original joints from immutable imported rest transforms, rather than
            // round-tripping the previously solved pose through world quaternions.
            // Retain the normalized model root and added face/palm/eye identities.
            foreach (var original in source.GetComponentsInChildren<Transform>(true))
            {
                if (original == source) continue;
                string path = AnimationUtility.CalculateTransformPath(original, source);
                var target = model.Find(path);
                if (target == null) continue; // the source renderer is deliberately renamed
                target.localPosition = original.localPosition;
                target.localRotation = original.localRotation;
                target.localScale = original.localScale;
            }
        }
        public static Transform RequireBone(Animator animator, HumanBodyBones id)
        {
            var bone = animator != null && animator.isHuman ? animator.GetBoneTransform(id) : null;
            if (bone == null) throw new InvalidOperationException("Required Humanoid bone is missing: " + id);
            return bone;
        }

        private static void ConfigureArm(KevinBodyRig.Arm arm, Animator animator, bool right)
        {
            arm.upper = RequireBone(animator, right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
            arm.lower = RequireBone(animator, right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
            arm.hand = RequireBone(animator, right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            arm.upperRest = arm.upper.localRotation;
            arm.lowerRest = arm.lower.localRotation;
            arm.handRest = arm.hand.localRotation;
            arm.handWorldRest = arm.hand.rotation;
            arm.upperLength = Vector3.Distance(arm.upper.position, arm.lower.position);
            arm.lowerLength = Vector3.Distance(arm.lower.position, arm.hand.position);
            arm.palm = arm.hand.Find("PalmGrip") ?? new GameObject("PalmGrip").transform;
            arm.palm.SetParent(arm.hand, false);
            var middle = RequireBone(animator, right ? HumanBodyBones.RightMiddleIntermediate : HumanBodyBones.LeftMiddleIntermediate);
            arm.palm.position = Vector3.Lerp(arm.hand.position, middle.position, .73f) + Vector3.down * .01f;
        }

        private static void ConfigureFingers(KevinBodyRig rig, Animator animator)
        {
            var bones = new List<Transform>();
            var axes = new List<Vector3>();
            var angles = new List<float>();
            string[] digits = { "Thumb", "Index", "Middle", "Ring", "Little" };
            string[] joints = { "Proximal", "Intermediate", "Distal" };
            foreach (string side in new[] { "Right", "Left" })
                foreach (string digit in digits)
                    for (int joint = 0; joint < joints.Length; joint++)
                    {
                        var id = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + digit + joints[joint]);
                        Transform bone = RequireBone(animator, id);
                        Transform next = joint < 2
                            ? RequireBone(animator, (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + digit + joints[joint + 1]))
                            : bone.childCount > 0 ? bone.GetChild(0) : null;
                        Vector3 direction = next != null ? next.position - bone.position : bone.position - bone.parent.position;
                        // Both T-pose hands curl towards their palm/down side; derive each local
                        // axis from the artist's finger direction, not Quaternius bone axes.
                        Vector3 axis = Vector3.Cross(direction.normalized, Vector3.down).normalized;
                        if (axis.sqrMagnitude < .5f) throw new InvalidOperationException("Finger bend axis is ambiguous: " + id);
                        bones.Add(bone);
                        axes.Add(bone.InverseTransformDirection(axis));
                        float amount = digit == "Thumb" ? 25f : joint == 1 ? 48f : 62f;
                        angles.Add(amount * (side == "Left" ? .27f : 1f));
                    }
            rig.fingerBones = bones.ToArray();
            rig.fingerRest = bones.Select(b => b.localRotation).ToArray();
            rig.fingerAxes = axes.ToArray();
            rig.fingerAngles = angles.ToArray();
        }

        private struct Vertex
        {
            public Vector3 position, normal;
            public Vector2 uv;
            public BoneWeight weights;
            public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex
            {
                position = Vector3.Lerp(a.position, b.position, t), normal = Vector3.Lerp(a.normal, b.normal, t).normalized,
                uv = Vector2.Lerp(a.uv, b.uv, t), weights = InterpolateWeights(a.weights, b.weights, t)
            };
        }

        private static BoneWeight InterpolateWeights(BoneWeight a, BoneWeight b, float t)
        {
            var merged = new Dictionary<int, float>();
            void Add(int index, float value) { if (value > 0f) merged[index] = merged.TryGetValue(index, out float old) ? old + value : value; }
            Add(a.boneIndex0, a.weight0 * (1f - t)); Add(a.boneIndex1, a.weight1 * (1f - t));
            Add(a.boneIndex2, a.weight2 * (1f - t)); Add(a.boneIndex3, a.weight3 * (1f - t));
            Add(b.boneIndex0, b.weight0 * t); Add(b.boneIndex1, b.weight1 * t);
            Add(b.boneIndex2, b.weight2 * t); Add(b.boneIndex3, b.weight3 * t);
            var values = merged.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Take(4).ToArray();
            float sum = values.Sum(p => p.Value);
            if (sum <= 0f) throw new InvalidOperationException("Unweighted generated body vertex.");
            var result = new BoneWeight();
            for (int i = 0; i < values.Length; i++)
            {
                int index = values[i].Key; float weight = values[i].Value / sum;
                switch (i) { case 0: result.boneIndex0 = index; result.weight0 = weight; break; case 1: result.boneIndex1 = index; result.weight1 = weight; break;
                    case 2: result.boneIndex2 = index; result.weight2 = weight; break; case 3: result.boneIndex3 = index; result.weight3 = weight; break; }
            }
            return result;
        }

        private static Mesh BuildBodyMesh(SkinnedMeshRenderer skin, Mesh source, float neckY)
        {
            Vector3[] p = source.vertices, n = source.normals;
            Vector2[] tex = source.uv;
            BoneWeight[] weights = source.boneWeights;
            if (p.Length == 0 || n.Length != p.Length || tex.Length != p.Length || weights.Length != p.Length)
                throw new InvalidOperationException("RiggedBody requires positions, normals, UVs and skin weights.");
            var points = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>(); var boneWeights = new List<BoneWeight>();
            var triangles = new List<int>[source.subMeshCount];
            var retained = new Dictionary<(Vector3, Vector3, Vector2, BoneWeight), int>();
            int Add(Vertex v)
            {
                var key = (v.position, v.normal, v.uv, v.weights);
                if (retained.TryGetValue(key, out int index)) return index;
                index = points.Count; retained.Add(key, index);
                points.Add(v.position); normals.Add(v.normal); uv.Add(v.uv); boneWeights.Add(v.weights);
                return index;
            }
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                triangles[sub] = new List<int>();
                int[] input = source.GetTriangles(sub);
                for (int i = 0; i < input.Length; i += 3)
                {
                    var polygon = new List<Vertex>();
                    for (int j = 0; j < 3; j++)
                    {
                        int a = input[i + j], b = input[i + (j + 1) % 3];
                        Vertex va = new Vertex { position = p[a], normal = n[a], uv = tex[a], weights = weights[a] };
                        Vertex vb = new Vertex { position = p[b], normal = n[b], uv = tex[b], weights = weights[b] };
                        float da = neckY - skin.transform.TransformPoint(va.position).y, db = neckY - skin.transform.TransformPoint(vb.position).y;
                        if (da >= 0f) polygon.Add(va);
                        if ((da >= 0f) != (db >= 0f)) polygon.Add(Vertex.Lerp(va, vb, da / (da - db)));
                    }
                    for (int j = 1; j + 1 < polygon.Count; j++)
                    {
                        triangles[sub].Add(Add(polygon[0])); triangles[sub].Add(Add(polygon[j])); triangles[sub].Add(Add(polygon[j + 1]));
                    }
                }
            }
            if (points.Count < 100 || points.Count >= p.Length) throw new InvalidOperationException("RiggedBody head clipping did not produce a usable body.");
            var mesh = new Mesh { name = "Kevin_RiggedBody_OriginalSkinWithoutHead", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(points); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.boneWeights = boneWeights.ToArray(); mesh.bindposes = source.bindposes;
            mesh.subMeshCount = triangles.Length;
            for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            return mesh;
        }

        private static Mesh SaveMesh(Mesh mesh, string path)
        {
            mesh.name = Path.GetFileNameWithoutExtension(path);
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            // Use native mesh setters when topology changes. CopySerialized can leave
            // an already loaded mesh's render buffers stale after a vertex-count change.
            // Update the existing asset object so all GUID/fileID references survive.
            saved.Clear(false);
            saved.indexFormat = mesh.indexFormat;
            saved.SetVertices(mesh.vertices); saved.SetNormals(mesh.normals); saved.SetUVs(0, mesh.uv);
            saved.boneWeights = mesh.boneWeights; saved.bindposes = mesh.bindposes;
            saved.subMeshCount = mesh.subMeshCount;
            for (int i = 0; i < mesh.subMeshCount; i++) saved.SetTriangles(mesh.GetTriangles(i), i);
            saved.bounds = mesh.bounds; saved.SetTangents(mesh.tangents);
            saved.UploadMeshData(false);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(saved); AssetDatabase.SaveAssetIfDirty(saved);
            return saved;
        }

        private static Material BuildMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(BodyMaterialPath);
            if (material == null) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, BodyMaterialPath); }
            material.color = Color.white;
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(SourceRoot + "mannequin_3d_model_basecolor.JPEG");
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(SourceRoot + "mannequin_3d_model_normal.JPEG"));
            if (material.mainTexture == null || material.GetTexture("_BumpMap") == null) throw new InvalidOperationException("Supplied body textures are missing.");
            material.EnableKeyword("_NORMALMAP"); material.SetFloat("_BumpScale", 1f); material.SetFloat("_Metallic", 0f); material.SetFloat("_Glossiness", .18f);
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }
    }
}
