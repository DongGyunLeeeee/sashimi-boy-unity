using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SashimiBoy.EditorTools
{
    public static class SalmonButcheryArtPipeline
    {
        private const string SourceDropRoot =
            @"C:\Dev\SashimiBoyAssetDrops\Stage01\SalmonButchery\Release_20260903_v1";
        private const string SalmonSourceRoot =
            "Assets/_SashimiBoy/Art/Source/Stage01/SalmonButchery";
        private const string SharedPinBoneSourceRoot =
            "Assets/_SashimiBoy/Art/Source/Shared/FishButchery/PinBone";
        private const string GeneratedRoot =
            "Assets/_SashimiBoy/Art/Generated";
        private const string SalmonMaterialsRoot =
            GeneratedRoot + "/Materials/Stage01/SalmonButchery";
        private const string SharedMaterialsRoot =
            GeneratedRoot + "/Materials/Shared/FishButchery";
        private const string SalmonPackedMapsRoot =
            GeneratedRoot + "/PackedMaps/Stage01/SalmonButchery";
        private const string SharedPackedMapsRoot =
            GeneratedRoot + "/PackedMaps/Shared/FishButchery";
        private const string SalmonPrefabsRoot =
            GeneratedRoot + "/Prefabs/Stage01/SalmonButchery";
        private const string SharedPrefabsRoot =
            GeneratedRoot + "/Prefabs/Shared/FishButchery";
        private const string ReportsRoot = GeneratedRoot + "/Reports";
        private const string PreviewRoot =
            GeneratedRoot + "/Previews/Stage01";
        private const string ManifestPath =
            ReportsRoot + "/Stage01SalmonButcherySourceManifest.csv";
        private const string SourceRoleMapPath =
            ReportsRoot + "/Stage01SalmonButcherySourceRoleMap.md";
        private const string InventoryReportPath =
            ReportsRoot + "/Stage01SalmonAssemblyInventory.md";
        private const string AssemblyPrefabPath =
            SalmonPrefabsRoot + "/PF_Stage01_SalmonAssembly.prefab";
        private const string ProceduralFallbackPath =
            GeneratedRoot + "/Prefabs/Stage01/PF_Stage01_ProceduralSalmon.prefab";

        private static readonly Vector3 WrapperModelRotation =
            new Vector3(0f, 270f, 0f);

        private static readonly SourceSpec[] SourceSpecs =
        {
            new SourceSpec(
                "Head",
                SalmonAssemblyPieceRole.Head,
                SalmonSourceRoot + "/Head",
                "Salmon_Head",
                0.84f,
                false),
            new SourceSpec(
                "Body",
                SalmonAssemblyPieceRole.Body,
                SalmonSourceRoot + "/Body",
                "Salmon_Body",
                2.40f,
                false),
            new SourceSpec(
                "Fins",
                SalmonAssemblyPieceRole.Fins,
                SalmonSourceRoot + "/Fins",
                "Salmon_Fins",
                0.62f,
                false),
            new SourceSpec(
                "Spine",
                SalmonAssemblyPieceRole.Spine,
                SalmonSourceRoot + "/Spine",
                "Salmon_Spine",
                2.15f,
                false),
            new SourceSpec(
                "Fillet",
                SalmonAssemblyPieceRole.Fillet,
                SalmonSourceRoot + "/Fillet",
                "Salmon_Fillet",
                2.15f,
                false),
            new SourceSpec(
                "PinBone",
                SalmonAssemblyPieceRole.PinBone,
                SharedPinBoneSourceRoot,
                "PinBone",
                0.14f,
                true),
        };

        [MenuItem("Sashimi Boy/Art/Build Stage 01 Salmon Assembly")]
        public static void BuildSalmonAssembly()
        {
            BuildAll();
        }

        [MenuItem("Sashimi Boy/Art/Capture Stage 01 Salmon Assembly Previews")]
        public static void CaptureSalmonAssemblyPreviews()
        {
            BuildAll();
            CapturePreviews();
        }

        public static void BuildSalmonAssemblyBatch()
        {
            BuildAll();
        }

        public static void BuildSalmonAssemblyAndPreviewBatch()
        {
            BuildAll();
            CapturePreviews();
        }

        private static void BuildAll()
        {
            EnsureFolders();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateSourceManifest();

            List<BuildResult> results = new List<BuildResult>();
            for (int i = 0; i < SourceSpecs.Length; i++)
            {
                SourceSpec spec = SourceSpecs[i];
                ConfigureImporters(spec);
                BuildResult result = InspectSource(spec);
                result.Material = BuildMaterial(spec, result);
                result.WrapperPrefab = BuildWrapper(spec, result);
                results.Add(result);
            }

            BuildAssembly(results);
            WriteInventoryReport(results);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                "[Sashimi Boy] Stage01 salmon assembly build complete: " +
                AssemblyPrefabPath);
        }

        private static void ConfigureImporters(SourceSpec spec)
        {
            ModelImporter modelImporter =
                AssetImporter.GetAtPath(spec.ModelPath) as ModelImporter;
            Require(modelImporter != null, "Missing ModelImporter: " + spec.ModelPath);

            bool modelChanged = false;
            modelChanged |= SetIfDifferent(
                modelImporter.globalScale,
                1f,
                value => modelImporter.globalScale = value);
            modelChanged |= SetIfDifferent(
                modelImporter.useFileScale,
                true,
                value => modelImporter.useFileScale = value);
            modelChanged |= SetIfDifferent(
                modelImporter.bakeAxisConversion,
                false,
                value => modelImporter.bakeAxisConversion = value);
            modelChanged |= SetIfDifferent(
                modelImporter.importAnimation,
                false,
                value => modelImporter.importAnimation = value);
            modelChanged |= SetIfDifferent(
                modelImporter.importCameras,
                false,
                value => modelImporter.importCameras = value);
            modelChanged |= SetIfDifferent(
                modelImporter.importLights,
                false,
                value => modelImporter.importLights = value);
            modelChanged |= SetIfDifferent(
                modelImporter.materialImportMode,
                ModelImporterMaterialImportMode.None,
                value => modelImporter.materialImportMode = value);
            if (modelChanged)
            {
                modelImporter.SaveAndReimport();
            }

            foreach (string texturePath in spec.TexturePaths)
            {
                TextureImporter importer =
                    AssetImporter.GetAtPath(texturePath) as TextureImporter;
                Require(importer != null, "Missing TextureImporter: " + texturePath);
                TextureKind kind = TextureKindFromPath(texturePath);
                TextureImporterType desiredType = kind == TextureKind.Normal
                    ? TextureImporterType.NormalMap
                    : TextureImporterType.Default;
                bool textureChanged = false;
                textureChanged |= SetIfDifferent(
                    importer.textureType,
                    desiredType,
                    value => importer.textureType = value);
                textureChanged |= SetIfDifferent(
                    importer.sRGBTexture,
                    kind == TextureKind.BaseColor,
                    value => importer.sRGBTexture = value);
                textureChanged |= SetIfDifferent(
                    importer.mipmapEnabled,
                    true,
                    value => importer.mipmapEnabled = value);
                textureChanged |= SetIfDifferent(
                    importer.maxTextureSize,
                    2048,
                    value => importer.maxTextureSize = value);
                if (textureChanged)
                {
                    importer.SaveAndReimport();
                }
            }
        }

        private static BuildResult InspectSource(SourceSpec spec)
        {
            GameObject source =
                AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
            Require(source != null, "Missing source model: " + spec.ModelPath);
            GameObject instance = UnityEngine.Object.Instantiate(source);
            instance.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                Renderer[] renderers =
                    instance.GetComponentsInChildren<Renderer>(true);
                Require(renderers.Length > 0, "No renderer in " + spec.ModelPath);
                Bounds bounds;
                Require(
                    TryGetRendererBounds(instance, out bounds),
                    "No finite bounds in " + spec.ModelPath);
                Require(IsFinite(bounds.center), "Invalid center in " + spec.ModelPath);
                Require(
                    IsFinite(bounds.size) && bounds.size.sqrMagnitude > 0.000001f,
                    "Invalid size in " + spec.ModelPath);

                int slotCount = 0;
                List<string> slotNames = new List<string>();
                for (int i = 0; i < renderers.Length; i++)
                {
                    Material[] slots = renderers[i].sharedMaterials;
                    slotCount += slots.Length;
                    for (int j = 0; j < slots.Length; j++)
                    {
                        slotNames.Add(
                            renderers[i].name + "[" + j + "]=" +
                            (slots[j] != null ? slots[j].name : "<none>"));
                    }
                }

                Require(slotCount > 0, "No material slot in " + spec.ModelPath);
                return new BuildResult(spec)
                {
                    SourceBounds = bounds,
                    RendererCount = renderers.Length,
                    MaterialSlotCount = slotCount,
                    SlotNames = slotNames,
                };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static Material BuildMaterial(
            SourceSpec spec,
            BuildResult result)
        {
            string materialPath = spec.MaterialPath;
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            Shader shader = Shader.Find("Standard");
            Require(shader != null, "Built-in Standard shader is unavailable.");
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else
            {
                material.shader = shader;
            }

            Texture2D baseColor = LoadTexture(spec.BaseColorPath);
            Texture2D normal = LoadTexture(spec.NormalPath);
            Texture2D packed = GeneratePackedMap(spec);
            Require(baseColor != null, "Missing base color: " + spec.BaseColorPath);
            Require(normal != null, "Missing normal map: " + spec.NormalPath);
            Require(packed != null, "Packed map generation failed: " + spec.Id);

            material.SetColor("_Color", Color.white);
            material.SetTexture("_MainTex", baseColor);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            material.SetTexture("_MetallicGlossMap", packed);
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_Glossiness", 1f);
            material.SetFloat("_GlossMapScale", 1f);
            material.SetColor("_EmissionColor", Color.black);
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_METALLICGLOSSMAP");
            material.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);

            result.PackedMapPath = spec.PackedMapPath;
            return material;
        }

        private static Texture2D GeneratePackedMap(SourceSpec spec)
        {
            TextureImporter metallicImporter =
                AssetImporter.GetAtPath(spec.MetallicPath) as TextureImporter;
            TextureImporter roughnessImporter =
                AssetImporter.GetAtPath(spec.RoughnessPath) as TextureImporter;
            Require(metallicImporter != null, "Missing metallic importer: " + spec.Id);
            Require(roughnessImporter != null, "Missing roughness importer: " + spec.Id);
            bool metallicReadable = metallicImporter.isReadable;
            bool roughnessReadable = roughnessImporter.isReadable;

            try
            {
                if (!metallicReadable)
                {
                    metallicImporter.isReadable = true;
                    metallicImporter.SaveAndReimport();
                }

                if (!roughnessReadable)
                {
                    roughnessImporter =
                        AssetImporter.GetAtPath(spec.RoughnessPath)
                        as TextureImporter;
                    roughnessImporter.isReadable = true;
                    roughnessImporter.SaveAndReimport();
                }

                Texture2D metallic = LoadTexture(spec.MetallicPath);
                Texture2D roughness = LoadTexture(spec.RoughnessPath);
                Require(metallic != null && roughness != null, "PBR maps failed to load.");
                int width = Mathf.Min(metallic.width, roughness.width);
                int height = Mathf.Min(metallic.height, roughness.height);
                Color32[] metallicPixels = ReadPixelsAtSize(metallic, width, height);
                Color32[] roughnessPixels = ReadPixelsAtSize(roughness, width, height);
                Color32[] packedPixels = new Color32[metallicPixels.Length];
                for (int i = 0; i < packedPixels.Length; i++)
                {
                    packedPixels[i] = new Color32(
                        metallicPixels[i].r,
                        0,
                        0,
                        (byte)(255 - roughnessPixels[i].r));
                }

                Texture2D output = new Texture2D(
                    width,
                    height,
                    TextureFormat.RGBA32,
                    false,
                    true);
                output.SetPixels32(packedPixels);
                output.Apply(false, false);
                File.WriteAllBytes(
                    AssetPathToAbsolutePath(spec.PackedMapPath),
                    output.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(output);
                AssetDatabase.ImportAsset(
                    spec.PackedMapPath,
                    ImportAssetOptions.ForceSynchronousImport);

                TextureImporter outputImporter =
                    AssetImporter.GetAtPath(spec.PackedMapPath)
                    as TextureImporter;
                Require(outputImporter != null, "Generated PBR importer is missing.");
                outputImporter.textureType = TextureImporterType.Default;
                outputImporter.sRGBTexture = false;
                outputImporter.mipmapEnabled = true;
                outputImporter.alphaSource = TextureImporterAlphaSource.FromInput;
                outputImporter.alphaIsTransparency = false;
                outputImporter.isReadable = false;
                outputImporter.maxTextureSize = 2048;
                outputImporter.SaveAndReimport();
                return LoadTexture(spec.PackedMapPath);
            }
            finally
            {
                metallicImporter =
                    AssetImporter.GetAtPath(spec.MetallicPath) as TextureImporter;
                roughnessImporter =
                    AssetImporter.GetAtPath(spec.RoughnessPath) as TextureImporter;
                if (metallicImporter != null &&
                    metallicImporter.isReadable != metallicReadable)
                {
                    metallicImporter.isReadable = metallicReadable;
                    metallicImporter.SaveAndReimport();
                }

                if (roughnessImporter != null &&
                    roughnessImporter.isReadable != roughnessReadable)
                {
                    roughnessImporter.isReadable = roughnessReadable;
                    roughnessImporter.SaveAndReimport();
                }
            }
        }

        private static GameObject BuildWrapper(SourceSpec spec, BuildResult result)
        {
            GameObject source =
                AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
            GameObject root = new GameObject("PF_" + spec.PrefabStem);
            try
            {
                GameObject model = new GameObject("Model");
                model.transform.SetParent(root.transform, false);
                model.transform.localRotation = Quaternion.Euler(WrapperModelRotation);

                GameObject imported = PrefabUtility.InstantiatePrefab(source)
                    as GameObject;
                Require(imported != null, "Could not instantiate " + spec.ModelPath);
                imported.name = spec.ModelName;
                imported.transform.SetParent(model.transform, false);
                Renderer[] renderers =
                    imported.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Material[] slots = renderers[i].sharedMaterials;
                    for (int j = 0; j < slots.Length; j++)
                    {
                        slots[j] = result.Material;
                    }

                    renderers[i].sharedMaterials = slots;
                    renderers[i].shadowCastingMode = ShadowCastingMode.On;
                    renderers[i].receiveShadows = true;
                }

                Bounds rawBounds;
                Require(TryGetRendererBounds(root, out rawBounds), "Wrapper has no bounds.");
                float largest = Mathf.Max(
                    rawBounds.size.x,
                    Mathf.Max(rawBounds.size.y, rawBounds.size.z));
                float scale = spec.TargetLargestDimension / largest;
                Require(float.IsFinite(scale) && scale > 0f, "Invalid wrapper scale.");
                model.transform.localScale = Vector3.one * scale;

                Bounds scaledBounds;
                Require(TryGetRendererBounds(root, out scaledBounds), "Scaled wrapper has no bounds.");
                model.transform.position += new Vector3(
                    -scaledBounds.center.x,
                    -scaledBounds.min.y,
                    -scaledBounds.center.z);
                root.transform.position = Vector3.zero;
                root.transform.rotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;

                Bounds finalBounds;
                Require(TryGetRendererBounds(root, out finalBounds), "Final wrapper has no bounds.");
                result.WrapperBounds = finalBounds;
                result.WrapperScale = scale;
                return PrefabUtility.SaveAsPrefabAsset(root, spec.PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void BuildAssembly(List<BuildResult> results)
        {
            Dictionary<string, BuildResult> byId =
                results.ToDictionary(item => item.Spec.Id, StringComparer.Ordinal);
            GameObject root = new GameObject("PF_Stage01_SalmonAssembly");
            try
            {
                SalmonAssemblyView view = root.AddComponent<SalmonAssemblyView>();
                GameObject visualRoot = Child(root.transform, "VisualRoot");
                GameObject partsRoot = Child(visualRoot.transform, "Parts");
                GameObject pinBonesRoot = Child(visualRoot.transform, "PinBones");
                GameObject anchorsRoot = Child(root.transform, "Anchors");
                GameObject partAnchors = Child(anchorsRoot.transform, "Parts");
                GameObject pinBoneAnchors = Child(anchorsRoot.transform, "PinBones");
                GameObject toolAnchors = Child(anchorsRoot.transform, "Tools");
                GameObject outputAnchors = Child(anchorsRoot.transform, "Outputs");

                view.body = AddPiece(
                    partsRoot.transform,
                    byId["Body"],
                    SalmonAssemblyView.BodyId,
                    Vector3.zero,
                    Quaternion.identity,
                    true);
                view.head = AddPiece(
                    partsRoot.transform,
                    byId["Head"],
                    SalmonAssemblyView.HeadId,
                    new Vector3(0f, 0.13f, 1.42f),
                    Quaternion.identity,
                    true);
                view.fins = AddPiece(
                    partsRoot.transform,
                    byId["Fins"],
                    SalmonAssemblyView.FinsId,
                    new Vector3(0f, 0.56f, -0.12f),
                    Quaternion.Euler(0f, 0f, 8f),
                    false);
                view.spine = AddPiece(
                    partsRoot.transform,
                    byId["Spine"],
                    SalmonAssemblyView.SpineId,
                    new Vector3(0f, 0.08f, -0.18f),
                    Quaternion.identity,
                    false);
                view.fillet = AddPiece(
                    partsRoot.transform,
                    byId["Fillet"],
                    SalmonAssemblyView.FilletId,
                    new Vector3(0f, 0.10f, -0.05f),
                    Quaternion.identity,
                    false);

                view.bodyAnchor = Anchor(partAnchors.transform, "BodyAnchor", view.body.transform);
                view.headAnchor = Anchor(partAnchors.transform, "HeadAnchor", view.head.transform);
                view.finsAnchor = Anchor(partAnchors.transform, "FinsAnchor", view.fins.transform);
                view.spineAnchor = Anchor(partAnchors.transform, "SpineAnchor", view.spine.transform);
                view.filletAnchor = Anchor(partAnchors.transform, "FilletAnchor", view.fillet.transform);

                const int pinBoneCount = 8;
                view.pinBones = new SalmonAssemblyPieceView[pinBoneCount];
                view.pinBoneAnchors = new Transform[pinBoneCount];
                for (int i = 0; i < pinBoneCount; i++)
                {
                    float normalized = i / (float)(pinBoneCount - 1);
                    Vector3 position = new Vector3(
                        i % 2 == 0 ? -0.07f : 0.07f,
                        0.52f,
                        Mathf.Lerp(-0.72f, 0.68f, normalized));
                    Quaternion rotation = Quaternion.Euler(
                        0f,
                        i % 2 == 0 ? 82f : 98f,
                        i % 2 == 0 ? -6f : 6f);
                    string stableId = "PinBone." + i.ToString("00");
                    view.pinBones[i] = AddPiece(
                        pinBonesRoot.transform,
                        byId["PinBone"],
                        stableId,
                        position,
                        rotation,
                        false);
                    view.pinBoneAnchors[i] = Anchor(
                        pinBoneAnchors.transform,
                        "PinBoneAnchor_" + i.ToString("00"),
                        view.pinBones[i].transform);
                }

                view.knifeAttachmentAnchor = Anchor(
                    toolAnchors.transform,
                    "KnifeAttachmentAnchor",
                    new Vector3(-0.72f, 0.48f, 0.25f),
                    Quaternion.Euler(0f, 24f, 0f));
                view.handAttachmentAnchor = Anchor(
                    toolAnchors.transform,
                    "HandAttachmentAnchor",
                    new Vector3(-0.92f, 0.72f, -0.20f),
                    Quaternion.Euler(0f, 18f, 0f));
                view.pinBoneWorkAnchor = Anchor(
                    toolAnchors.transform,
                    "PinBoneWorkAnchor",
                    new Vector3(0f, 0.50f, 0f),
                    Quaternion.identity);
                view.sashimiOutputAnchor = Anchor(
                    outputAnchors.transform,
                    "SashimiOutputAnchor",
                    new Vector3(1.35f, 0.12f, -0.15f),
                    Quaternion.identity);
                view.plateOutputAnchor = Anchor(
                    outputAnchors.transform,
                    "PlateOutputAnchor",
                    new Vector3(1.65f, 0.12f, 0.75f),
                    Quaternion.identity);
                view.proceduralSalmonFallbackPrefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralFallbackPath);
                Require(
                    view.proceduralSalmonFallbackPrefab != null,
                    "Procedural salmon fallback is missing.");
                EditorUtility.SetDirty(view);

                PrefabUtility.SaveAsPrefabAsset(root, AssemblyPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static SalmonAssemblyPieceView AddPiece(
            Transform parent,
            BuildResult build,
            string stableId,
            Vector3 localPosition,
            Quaternion localRotation,
            bool initiallyVisible)
        {
            GameObject piece = Child(parent, stableId);
            piece.transform.localPosition = localPosition;
            piece.transform.localRotation = localRotation;
            GameObject wrapper = PrefabUtility.InstantiatePrefab(build.WrapperPrefab)
                as GameObject;
            Require(wrapper != null, "Could not instantiate " + build.Spec.PrefabPath);
            wrapper.name = build.WrapperPrefab.name;
            wrapper.transform.SetParent(piece.transform, false);
            SalmonAssemblyPieceView pieceView =
                piece.AddComponent<SalmonAssemblyPieceView>();
            pieceView.Configure(
                stableId,
                build.Spec.Role,
                wrapper,
                initiallyVisible);
            EditorUtility.SetDirty(pieceView);
            return pieceView;
        }

        private static Transform Anchor(
            Transform parent,
            string name,
            Transform source)
        {
            return Anchor(
                parent,
                name,
                source.localPosition,
                source.localRotation);
        }

        private static Transform Anchor(
            Transform parent,
            string name,
            Vector3 localPosition,
            Quaternion localRotation)
        {
            GameObject anchor = Child(parent, name);
            anchor.transform.localPosition = localPosition;
            anchor.transform.localRotation = localRotation;
            return anchor.transform;
        }

        private static void ValidateSourceManifest()
        {
            string manifestAbsolute = AssetPathToAbsolutePath(ManifestPath);
            Require(File.Exists(manifestAbsolute), "Source manifest is missing.");
            string[] lines = File.ReadAllLines(manifestAbsolute);
            int validated = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                string trimmed = lines[i].Trim();
                Require(
                    trimmed.Length >= 2 && trimmed[0] == '"' &&
                    trimmed[trimmed.Length - 1] == '"',
                    "Malformed source manifest row " + (i + 1) + ".");
                string[] columns = trimmed.Substring(1, trimmed.Length - 2)
                    .Split(new[] { "\",\"" }, StringSplitOptions.None);
                Require(columns.Length == 3, "Malformed source manifest row " + (i + 1) + ".");
                string importedPath = ImportedPathForManifestEntry(columns[0]);
                string absolutePath = AssetPathToAbsolutePath(importedPath);
                Require(File.Exists(absolutePath), "Manifest asset is missing: " + importedPath);
                long expectedBytes;
                Require(
                    long.TryParse(
                        columns[1],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out expectedBytes),
                    "Invalid byte count for " + columns[0]);
                FileInfo file = new FileInfo(absolutePath);
                Require(file.Length == expectedBytes, "Byte count changed: " + importedPath);
                Require(
                    string.Equals(
                        ComputeSha256(absolutePath),
                        columns[2],
                        StringComparison.OrdinalIgnoreCase),
                    "SHA-256 changed: " + importedPath);
                validated++;
            }

            Require(validated == 37, "Expected 37 source manifest entries.");
        }

        private static string ImportedPathForManifestEntry(string relativePath)
        {
            string normalized = relativePath.Replace('\\', '/');
            if (string.Equals(
                    normalized,
                    "ASSET_ROLE_MAP.md",
                    StringComparison.Ordinal))
            {
                return SourceRoleMapPath;
            }

            const string sharedPrefix = "Shared/PinBone/";
            if (normalized.StartsWith(sharedPrefix, StringComparison.Ordinal))
            {
                return SharedPinBoneSourceRoot + "/" +
                    normalized.Substring(sharedPrefix.Length);
            }

            return SalmonSourceRoot + "/" + normalized;
        }

        private static void WriteInventoryReport(List<BuildResult> results)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("# Stage01 Salmon Assembly Inventory");
            report.AppendLine();
            report.AppendLine("- Issue: `#20`");
            report.AppendLine("- Source drop: `" + SourceDropRoot + "`");
            report.AppendLine("- Manifest validation: **PASS (37/37)**");
            report.AppendLine("- Canonical forward/up: `+Z / +Y`");
            report.AppendLine("- Hierarchy scale contract: positive scale only");
            report.AppendLine("- Shader: built-in `Standard`");
            report.AppendLine(
                "- Metallic/smoothness: generated from separate Metallic (R) " +
                "and inverted Roughness (A) maps");
            report.AppendLine(
                "- Source `_RM` files are preserved but intentionally not bound; " +
                "their channel packing is undocumented.");
            report.AppendLine();
            report.AppendLine("## Canonical assets");
            report.AppendLine();
            report.AppendLine(
                "| Role | Source model | GUID | Source bounds | Wrapper target | " +
                "Material slots | Canonical wrapper |");
            report.AppendLine("|---|---|---|---|---:|---:|---|");
            foreach (BuildResult result in results)
            {
                report.AppendLine(
                    "| " + result.Spec.Id +
                    " | `" + result.Spec.ModelPath +
                    "` | `" + AssetDatabase.AssetPathToGUID(result.Spec.ModelPath) +
                    "` | `" + FormatVector(result.SourceBounds.size) +
                    "` | " + result.Spec.TargetLargestDimension.ToString(
                        "0.00",
                        CultureInfo.InvariantCulture) +
                    " m | " + result.MaterialSlotCount +
                    " | `" + result.Spec.PrefabPath + "` |");
            }

            report.AppendLine();
            report.AppendLine("## Assembly contract");
            report.AppendLine();
            report.AppendLine("- Prefab: `" + AssemblyPrefabPath + "`");
            report.AppendLine(
                "- Initial whole-fish view: Body + Head. The Body source already " +
                "contains its authored tail and exterior fins; the standalone Fins " +
                "wrapper is staged hidden to prevent mesh overlap/z-fighting.");
            report.AppendLine(
                "- Spine, Fillet, and eight shared PinBone instances are staged " +
                "hidden and can be shown, detached, and reset independently.");
            report.AppendLine(
                "- Stable IDs: `Head`, `Body`, `Fins`, `Spine`, `Fillet`, " +
                "`PinBone.00` through `PinBone.07`.");
            report.AppendLine(
                "- Required anchors: part anchors, eight PinBone anchors, " +
                "`KnifeAttachmentAnchor`, `HandAttachmentAnchor`, " +
                "`PinBoneWorkAnchor`, `SashimiOutputAnchor`, and " +
                "`PlateOutputAnchor`.");
            report.AppendLine(
                "- Explicit fallback: `" + ProceduralFallbackPath + "`.");
            report.AppendLine();
            report.AppendLine("## Source integrity");
            report.AppendLine();
            report.AppendLine(
                "The checked-in `Stage01SalmonButcherySourceManifest.csv` stores " +
                "the original byte counts and SHA-256 values. The generator " +
                "validates every row before it writes generated assets.");
            report.AppendLine();
            report.AppendLine("## Source slots");
            report.AppendLine();
            foreach (BuildResult result in results)
            {
                report.AppendLine("- " + result.Spec.Id + ": `" +
                    string.Join("`; `", result.SlotNames) + "`");
            }

            File.WriteAllText(
                AssetPathToAbsolutePath(InventoryReportPath),
                report.ToString(),
                new UTF8Encoding(false));
            AssetDatabase.ImportAsset(
                InventoryReportPath,
                ImportAssetOptions.ForceSynchronousImport);
        }

        private static void CapturePreviews()
        {
            Require(
                SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
                "Preview capture requires a graphics device; omit -nographics.");
            EnsureFolder(PreviewRoot);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            CapturePreview(
                "SalmonAssembly_Initial.png",
                PreviewMode.Initial);
            CapturePreview(
                "SalmonAssembly_Parts.png",
                PreviewMode.Parts);
            CapturePreview(
                "SalmonAssembly_Anchors.png",
                PreviewMode.Anchors);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                "[Sashimi Boy] Stage01 salmon assembly previews captured: " +
                PreviewRoot);
        }

        private static void CapturePreview(
            string fileName,
            PreviewMode mode)
        {
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            GameObject assemblyPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(AssemblyPrefabPath);
            Require(assemblyPrefab != null, "Assembly prefab is missing.");
            Camera camera = new GameObject("PreviewCamera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.06f, 1f);
            camera.fieldOfView = 32f;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            Light key = new GameObject("KeyLight").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 0.82f;
            key.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            Light fill = new GameObject("FillLight").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.36f;
            fill.transform.rotation = Quaternion.Euler(62f, 152f, 0f);
            RenderSettings.ambientLight = new Color(0.28f, 0.30f, 0.34f, 1f);
            GameObject assembly = PrefabUtility.InstantiatePrefab(assemblyPrefab)
                as GameObject;
            Require(assembly != null, "Could not instantiate assembly preview.");
            SalmonAssemblyView view = assembly.GetComponent<SalmonAssemblyView>();
            Require(view != null, "Assembly view component is missing.");
            GameObject previewSubject = assembly;

            Material groundMaterial = new Material(Shader.Find("Standard"));
            groundMaterial.color = new Color(0.11f, 0.13f, 0.16f, 1f);
            groundMaterial.SetFloat("_Glossiness", 0.16f);
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "PreviewGround";
            ground.transform.position = new Vector3(0f, -0.055f, 0f);
            ground.transform.localScale = new Vector3(8f, 0.1f, 8f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());

            List<GameObject> markers = new List<GameObject>();
            if (mode == PreviewMode.Parts)
            {
                UnityEngine.Object.DestroyImmediate(assembly);
                previewSubject = CreateSeparatedPartsPreview();
            }
            else if (mode == PreviewMode.Anchors)
            {
                AddAnchorMarkers(view, markers);
            }

            Bounds bounds;
            Require(
                TryGetRendererBounds(previewSubject, out bounds),
                "Preview has no bounds.");
            if (mode == PreviewMode.Anchors)
            {
                for (int i = 0; i < markers.Count; i++)
                {
                    Bounds markerBounds;
                    if (TryGetRendererBounds(markers[i], out markerBounds))
                    {
                        bounds.Encapsulate(markerBounds);
                    }
                }
            }

            FitCamera(camera, bounds, mode == PreviewMode.Parts);
            RenderCamera(
                camera,
                PreviewRoot + "/" + fileName,
                1600,
                1000);
            UnityEngine.Object.DestroyImmediate(groundMaterial);
            EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
        }

        private static GameObject CreateSeparatedPartsPreview()
        {
            GameObject root = new GameObject("SeparatedCanonicalParts");
            Dictionary<string, Vector3> positions =
                new Dictionary<string, Vector3>(StringComparer.Ordinal)
                {
                    { "Head", new Vector3(-1.65f, 0f, 1.55f) },
                    { "Body", new Vector3(0f, 0f, -0.85f) },
                    { "Fins", new Vector3(1.55f, 0f, 1.60f) },
                    { "Spine", new Vector3(-1.45f, 0f, -1.15f) },
                    { "Fillet", new Vector3(1.45f, 0f, -1.15f) },
                    { "PinBone", new Vector3(2.45f, 0f, 1.45f) },
                };
            for (int i = 0; i < SourceSpecs.Length; i++)
            {
                SourceSpec spec = SourceSpecs[i];
                GameObject prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
                Require(prefab != null, "Missing wrapper preview: " + spec.PrefabPath);
                GameObject instance = PrefabUtility.InstantiatePrefab(prefab)
                    as GameObject;
                Require(instance != null, "Could not preview " + spec.Id);
                instance.name = spec.Id;
                instance.transform.SetParent(root.transform, false);
                instance.transform.localPosition = positions[spec.Id];
            }

            return root;
        }

        private static void AddAnchorMarkers(
            SalmonAssemblyView view,
            List<GameObject> markers)
        {
            List<Transform> anchors = new List<Transform>
            {
                view.headAnchor,
                view.bodyAnchor,
                view.finsAnchor,
                view.spineAnchor,
                view.filletAnchor,
                view.knifeAttachmentAnchor,
                view.handAttachmentAnchor,
                view.pinBoneWorkAnchor,
                view.sashimiOutputAnchor,
                view.plateOutputAnchor,
            };
            anchors.AddRange(view.pinBoneAnchors);
            Shader shader = Shader.Find("Standard");
            for (int i = 0; i < anchors.Count; i++)
            {
                Transform anchor = anchors[i];
                if (anchor == null)
                {
                    continue;
                }

                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "Marker_" + anchor.name;
                marker.transform.position = anchor.position;
                marker.transform.localScale = Vector3.one *
                    (anchor.name.Contains("Output") ? 0.12f : 0.07f);
                UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
                Material material = new Material(shader);
                material.color = anchor.name.Contains("Output")
                    ? new Color(1f, 0.52f, 0.16f, 1f)
                    : anchor.name.Contains("Attachment")
                        ? new Color(0.92f, 0.22f, 0.36f, 1f)
                        : new Color(0.12f, 0.84f, 0.92f, 1f);
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", material.color * 0.8f);
                marker.GetComponent<Renderer>().sharedMaterial = material;
                markers.Add(marker);
            }
        }

        private static void FitCamera(Camera camera, Bounds bounds, bool wide)
        {
            float radius = Mathf.Max(bounds.extents.magnitude, 0.5f);
            Vector3 direction = wide
                ? new Vector3(1.1f, 1.2f, -1.35f)
                : new Vector3(1.25f, 0.92f, -1.55f);
            camera.transform.position =
                bounds.center + direction.normalized * radius * 3.0f;
            camera.transform.rotation = Quaternion.LookRotation(
                bounds.center - camera.transform.position,
                Vector3.up);
            camera.nearClipPlane = Mathf.Max(0.01f, radius * 0.01f);
            camera.farClipPlane = radius * 12f;
        }

        private static void RenderCamera(
            Camera camera,
            string assetPath,
            int width,
            int height)
        {
            RenderTexture target = new RenderTexture(width, height, 24);
            target.antiAliasing = 4;
            target.Create();
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            Texture2D image = new Texture2D(
                width,
                height,
                TextureFormat.RGB24,
                false);
            image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            image.Apply(false, false);
            File.WriteAllBytes(AssetPathToAbsolutePath(assetPath), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            camera.targetTexture = null;
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
        }

        private static Color32[] ReadPixelsAtSize(
            Texture2D source,
            int width,
            int height)
        {
            if (source.width == width && source.height == height)
            {
                return source.GetPixels32();
            }

            RenderTexture temporary = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                Texture2D resized = new Texture2D(
                    width,
                    height,
                    TextureFormat.RGBA32,
                    false,
                    true);
                resized.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                resized.Apply(false, false);
                Color32[] pixels = resized.GetPixels32();
                UnityEngine.Object.DestroyImmediate(resized);
                return pixels;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private static Texture2D LoadTexture(string path)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool found = false;
            bounds = new Bounds(root.transform.position, Vector3.zero);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i].enabled)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderers[i].bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            return found && IsFinite(bounds.center) && IsFinite(bounds.size);
        }

        private static TextureKind TextureKindFromPath(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.EndsWith("_BaseColor", StringComparison.OrdinalIgnoreCase))
            {
                return TextureKind.BaseColor;
            }

            if (name.EndsWith("_Normal", StringComparison.OrdinalIgnoreCase))
            {
                return TextureKind.Normal;
            }

            if (name.EndsWith("_Metallic", StringComparison.OrdinalIgnoreCase))
            {
                return TextureKind.Metallic;
            }

            if (name.EndsWith("_Roughness", StringComparison.OrdinalIgnoreCase))
            {
                return TextureKind.Roughness;
            }

            return name.EndsWith("_RM", StringComparison.OrdinalIgnoreCase)
                ? TextureKind.Rm
                : TextureKind.Unknown;
        }

        private static string ComputeSha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(stream))
                    .Replace("-", string.Empty);
            }
        }

        private static string AssetPathToAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            Require(!string.IsNullOrEmpty(projectRoot), "Unity project root is missing.");
            return Path.Combine(
                projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string FormatVector(Vector3 value)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "({0:0.######}, {1:0.######}, {2:0.######})",
                value.x,
                value.y,
                value.z);
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) &&
                   float.IsFinite(value.y) &&
                   float.IsFinite(value.z);
        }

        private static bool SetIfDifferent<T>(
            T current,
            T desired,
            Action<T> setter)
        {
            if (EqualityComparer<T>.Default.Equals(current, desired))
            {
                return false;
            }

            setter(desired);
            return true;
        }

        private static GameObject Child(Transform parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static void EnsureFolders()
        {
            EnsureFolder(SalmonMaterialsRoot);
            EnsureFolder(SharedMaterialsRoot);
            EnsureFolder(SalmonPackedMapsRoot);
            EnsureFolder(SharedPackedMapsRoot);
            EnsureFolder(SalmonPrefabsRoot);
            EnsureFolder(SharedPrefabsRoot);
            EnsureFolder(ReportsRoot);
            EnsureFolder(PreviewRoot);
        }

        private static void EnsureFolder(string folderPath)
        {
            string[] segments = folderPath.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[i]);
                }

                current = next;
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private enum TextureKind
        {
            Unknown,
            BaseColor,
            Normal,
            Metallic,
            Roughness,
            Rm,
        }

        private enum PreviewMode
        {
            Initial,
            Parts,
            Anchors,
        }

        private sealed class SourceSpec
        {
            public SourceSpec(
                string id,
                SalmonAssemblyPieceRole role,
                string sourceRoot,
                string modelName,
                float targetLargestDimension,
                bool isShared)
            {
                Id = id;
                Role = role;
                SourceRoot = sourceRoot;
                ModelName = modelName;
                TargetLargestDimension = targetLargestDimension;
                IsShared = isShared;
            }

            public string Id { get; private set; }
            public SalmonAssemblyPieceRole Role { get; private set; }
            public string SourceRoot { get; private set; }
            public string ModelName { get; private set; }
            public float TargetLargestDimension { get; private set; }
            public bool IsShared { get; private set; }
            public string ModelPath =>
                SourceRoot + "/Models/" + ModelName + ".fbx";
            public string TextureRoot => SourceRoot + "/Textures";
            public string BaseColorPath => TextureRoot + "/" + ModelName + "_BaseColor.JPEG";
            public string NormalPath => TextureRoot + "/" + ModelName + "_Normal.JPEG";
            public string MetallicPath => TextureRoot + "/" + ModelName + "_Metallic.JPEG";
            public string RoughnessPath => TextureRoot + "/" + ModelName + "_Roughness.JPEG";
            public string RmPath => TextureRoot + "/" + ModelName + "_RM.JPEG";
            public IEnumerable<string> TexturePaths
            {
                get
                {
                    yield return BaseColorPath;
                    yield return NormalPath;
                    yield return MetallicPath;
                    yield return RoughnessPath;
                    yield return RmPath;
                }
            }

            public string PrefabStem => IsShared ? "PinBone" : "Salmon_" + Id;
            public string MaterialPath =>
                (IsShared ? SharedMaterialsRoot : SalmonMaterialsRoot) +
                "/MAT_" + PrefabStem + ".mat";
            public string PackedMapPath =>
                (IsShared ? SharedPackedMapsRoot : SalmonPackedMapsRoot) +
                "/MS_" + PrefabStem + ".png";
            public string PrefabPath =>
                (IsShared ? SharedPrefabsRoot : SalmonPrefabsRoot) +
                "/PF_" + PrefabStem + ".prefab";
        }

        private sealed class BuildResult
        {
            public BuildResult(SourceSpec spec)
            {
                Spec = spec;
            }

            public SourceSpec Spec { get; private set; }
            public Bounds SourceBounds;
            public Bounds WrapperBounds;
            public float WrapperScale;
            public int RendererCount;
            public int MaterialSlotCount;
            public List<string> SlotNames = new List<string>();
            public string PackedMapPath = string.Empty;
            public Material Material;
            public GameObject WrapperPrefab;
        }
    }
}
