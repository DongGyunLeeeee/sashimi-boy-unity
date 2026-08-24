using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SashimiBoy.EditorTools
{
    public static class ClubArtPipeline
    {
        private const string SourceRoot =
            "Assets/_SashimiBoy/Art/Source/Environment/Club";
        private const string GeneratedRoot =
            "Assets/_SashimiBoy/Art/Generated";
        private const string MaterialsRoot =
            GeneratedRoot + "/Materials/Club";
        private const string PackedMapsRoot =
            GeneratedRoot + "/PackedMaps/Club";
        private const string PrefabsRoot =
            GeneratedRoot + "/Prefabs/Club";
        private const string DataRoot =
            GeneratedRoot + "/Data";
        private const string ScenesRoot =
            GeneratedRoot + "/Scenes";
        private const string ReportsRoot =
            GeneratedRoot + "/Reports";
        private const string CatalogPath =
            DataRoot + "/ClubAssetCatalog.asset";
        private const string GalleryScenePath =
            ScenesRoot + "/ClubAssetGallery.unity";
        private const string ReportPath =
            ReportsRoot + "/ClubAssetImportReport.md";

        private static readonly Regex PartRegex = new Regex(
            @"tripo_part_(\d+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly AssetSpec[] AssetSpecs =
        {
            new AssetSpec("BarTable", "bar_table.fbx", 2048, 1.05f, true, false),
            new AssetSpec("Beer", "beer.fbx", 1024, 0.28f, false, false),
            new AssetSpec("Bucket", "bucket.fbx", 1024, 0.45f, false, false),
            new AssetSpec(
                "ClubDoorFrame",
                "club_door_frame.fbx",
                2048,
                3.0f,
                true,
                false),
            new AssetSpec(
                "DJController",
                "dj_controller.fbx",
                2048,
                1.2f,
                false,
                false),
            new AssetSpec("DJMixer", "dj_mixer.fbx", 2048, 0.8f, false, false),
            new AssetSpec("DJStand", "dj_stand.fbx", 2048, 2.4f, true, true),
            new AssetSpec("Ice", "ice.fbx", 1024, 0.16f, false, false),
            new AssetSpec(
                "LongIslandIcedTea",
                "long_island_iced_tea.fbx",
                1024,
                0.3f,
                false,
                false),
            new AssetSpec("NeonSign", "neon_sign.fbx", 2048, 2.2f, false, true),
            new AssetSpec("SignBoard", "sign_board.fbx", 2048, 2.2f, false, true),
            new AssetSpec("Turntable", "turntable.fbx", 2048, 0.55f, false, false),
        };

        [MenuItem("Sashimi Boy/Art/Validate Club Source Assets")]
        public static void ValidateClubSourceAssets()
        {
            EnsureGeneratedFolders();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            try
            {
                List<AssetInspection> inspections = InspectAllAssets(true);
                WriteReport(inspections, null);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log(
                    "[Sashimi Boy] Club source validation complete. Report: " +
                    ReportPath);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("Sashimi Boy/Art/Build Club Materials And Prefabs")]
        public static void BuildClubMaterialsAndPrefabs()
        {
            BuildAll();
        }

        [MenuItem("Sashimi Boy/Art/Open Club Asset Gallery")]
        public static void OpenClubAssetGallery()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GalleryScenePath) == null)
            {
                BuildAll();
            }

            EditorSceneManager.OpenScene(GalleryScenePath, OpenSceneMode.Single);
        }

        public static void BuildClubMaterialsAndPrefabsBatch()
        {
            BuildAll();
        }

        private static void BuildAll()
        {
            EnsureGeneratedFolders();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            try
            {
                List<AssetInspection> inspections = InspectAllAssets(true);
                List<AssetBuildResult> buildResults = new List<AssetBuildResult>();

                for (int i = 0; i < AssetSpecs.Length; i++)
                {
                    AssetSpec spec = AssetSpecs[i];
                    ShowProgress(
                        "Building Club Art",
                        "Materials and prefab: " + spec.DisplayName,
                        (float)i / AssetSpecs.Length);

                    AssetInspection inspection =
                        inspections.First(item => item.Spec == spec);
                    Dictionary<int, TextureSet> textureSets = FindTextureSets(spec);
                    AssetBuildResult result =
                        BuildAsset(spec, inspection, textureSets);
                    buildResults.Add(result);
                }

                ClubAssetCatalog catalog = BuildCatalog(buildResults);
                BuildGalleryScene(catalog);
                WriteReport(inspections, buildResults);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log(
                    "[Sashimi Boy] Club art build complete. " +
                    "12 wrapper prefabs, catalog, and gallery are ready.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static List<AssetInspection> InspectAllAssets(
            bool applyImporterRules)
        {
            List<AssetInspection> inspections = new List<AssetInspection>();

            for (int i = 0; i < AssetSpecs.Length; i++)
            {
                AssetSpec spec = AssetSpecs[i];
                ShowProgress(
                    "Validating Club Art",
                    spec.DisplayName,
                    (float)i / AssetSpecs.Length);

                if (applyImporterRules)
                {
                    ConfigureTextureImporters(spec);
                }

                AssetInspection inspection = InspectModel(spec, applyImporterRules);
                inspections.Add(inspection);
            }

            return inspections;
        }

        private static AssetInspection InspectModel(
            AssetSpec spec,
            bool applyImporterRules)
        {
            AssetInspection inspection = new AssetInspection(spec);
            GameObject sourceModel =
                AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
            inspection.SourceModel = sourceModel;

            if (sourceModel == null)
            {
                inspection.Warnings.Add("Missing FBX or failed model import.");
                return inspection;
            }

            ModelImporter importer =
                AssetImporter.GetAtPath(spec.ModelPath) as ModelImporter;
            if (importer == null)
            {
                inspection.Warnings.Add("ModelImporter is unavailable.");
                return inspection;
            }

            bool hasAnimationData = HasAnimationData(spec.ModelPath);
            if (applyImporterRules &&
                importer.importAnimation &&
                !hasAnimationData)
            {
                importer.importAnimation = false;
                importer.SaveAndReimport();
                importer = AssetImporter.GetAtPath(spec.ModelPath) as ModelImporter;
            }

            inspection.GlobalScale = importer != null ? importer.globalScale : 1f;
            inspection.FileScale = importer != null ? importer.fileScale : 1f;
            inspection.UseFileScale = importer != null && importer.useFileScale;
            inspection.BakeAxisConversion =
                importer != null && importer.bakeAxisConversion;
            inspection.ImportAnimation =
                importer != null && importer.importAnimation;
            inspection.HasAnimationData = hasAnimationData;

            GameObject instance = UnityEngine.Object.Instantiate(sourceModel);
            instance.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                inspection.RootPosition = instance.transform.position;
                inspection.RootRotation = instance.transform.localEulerAngles;
                inspection.RootScale = instance.transform.localScale;

                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                inspection.RendererCount = renderers.Length;

                HashSet<Mesh> meshes = new HashSet<Mesh>();
                bool foundBounds = false;
                Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);

                for (int rendererIndex = 0;
                     rendererIndex < renderers.Length;
                     rendererIndex++)
                {
                    Renderer renderer = renderers[rendererIndex];
                    Mesh mesh = GetRendererMesh(renderer);
                    if (mesh != null)
                    {
                        meshes.Add(mesh);
                    }

                    Material[] slots = renderer.sharedMaterials;
                    inspection.MaterialSlotCount += slots.Length;
                    for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                    {
                        Material material = slots[slotIndex];
                        string materialName =
                            material != null ? material.name : "<missing>";
                        inspection.SlotDetails.Add(
                            renderer.name + " | " +
                            (mesh != null ? mesh.name : "<missing mesh>") + " | " +
                            "slot " + slotIndex.ToString(CultureInfo.InvariantCulture) +
                            " | " + materialName);

                        if (material == null)
                        {
                            inspection.Warnings.Add(
                                "Missing material in renderer " + renderer.name +
                                ", slot " +
                                slotIndex.ToString(CultureInfo.InvariantCulture) + ".");
                        }
                    }

                    if (mesh == null)
                    {
                        inspection.Warnings.Add(
                            "Missing mesh on renderer " + renderer.name + ".");
                    }

                    if (!foundBounds)
                    {
                        bounds = renderer.bounds;
                        foundBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }

                inspection.MeshCount = meshes.Count;
                inspection.HasBounds = foundBounds;
                inspection.Bounds = bounds;

                if (meshes.Count == 0)
                {
                    inspection.Warnings.Add("No Mesh was found in the imported FBX.");
                }

                if (!foundBounds ||
                    !IsFinite(bounds.center) ||
                    !IsFinite(bounds.size) ||
                    bounds.size.sqrMagnitude < 0.00000001f)
                {
                    inspection.Warnings.Add("Bounds are missing or invalid.");
                }
                else if (Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) > 1000f)
                {
                    inspection.Warnings.Add(
                        "Source bounds exceed 1000 Unity units and require review.");
                }

                if (!IsFinite(instance.transform.localScale) ||
                    instance.transform.localScale.x <= 0f ||
                    instance.transform.localScale.y <= 0f ||
                    instance.transform.localScale.z <= 0f)
                {
                    inspection.Warnings.Add("Imported root has an invalid scale.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }

            Dictionary<int, TextureSet> textureSets = FindTextureSets(spec);
            inspection.HasExternalTextures = textureSets.Count > 0;
            inspection.TextureSetCount = textureSets.Count;

            if (spec.IsMultipart)
            {
                HashSet<int> texturePartIndices =
                    new HashSet<int>(textureSets.Keys.Where(key => key >= 0));
                HashSet<int> slotPartIndices = new HashSet<int>();

                for (int i = 0; i < inspection.SlotDetails.Count; i++)
                {
                    int partIndex = ExtractPartIndex(inspection.SlotDetails[i]);
                    if (partIndex >= 0)
                    {
                        slotPartIndices.Add(partIndex);
                    }
                }

                inspection.TexturePartIndices.AddRange(
                    texturePartIndices.OrderBy(index => index));
                inspection.SlotPartIndices.AddRange(
                    slotPartIndices.OrderBy(index => index));

                foreach (int partIndex in texturePartIndices)
                {
                    if (!slotPartIndices.Contains(partIndex))
                    {
                        inspection.Warnings.Add(
                            "Texture part " + partIndex +
                            " has no confident FBX slot/mesh name match.");
                    }
                }
            }

            return inspection;
        }

        private static bool HasAnimationData(string modelPath)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(modelPath);
            for (int i = 0; i < assets.Length; i++)
            {
                AnimationClip clip = assets[i] as AnimationClip;
                if (clip == null ||
                    clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                {
                    continue;
                }

                if (AnimationUtility.GetCurveBindings(clip).Length > 0 ||
                    AnimationUtility.GetObjectReferenceCurveBindings(clip).Length > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ConfigureTextureImporters(AssetSpec spec)
        {
            string textureRoot = spec.TextureRoot;
            if (!AssetDatabase.IsValidFolder(textureRoot))
            {
                return;
            }

            string[] textureGuids =
                AssetDatabase.FindAssets("t:Texture2D", new[] { textureRoot });
            for (int i = 0; i < textureGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(textureGuids[i]);
                TextureKind kind = GetTextureKind(path);
                TextureImporter importer =
                    AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                TextureImporterType desiredType =
                    kind == TextureKind.Normal
                        ? TextureImporterType.NormalMap
                        : TextureImporterType.Default;
                bool desiredSrgb = kind == TextureKind.BaseColor;
                bool changed = false;

                changed |= SetIfDifferent(
                    importer.textureType,
                    desiredType,
                    value => importer.textureType = value);
                changed |= SetIfDifferent(
                    importer.sRGBTexture,
                    desiredSrgb,
                    value => importer.sRGBTexture = value);
                changed |= SetIfDifferent(
                    importer.mipmapEnabled,
                    true,
                    value => importer.mipmapEnabled = value);
                changed |= SetIfDifferent(
                    importer.maxTextureSize,
                    spec.MaximumTextureSize,
                    value => importer.maxTextureSize = value);

                if (changed)
                {
                    importer.SaveAndReimport();
                }
            }
        }

        private static AssetBuildResult BuildAsset(
            AssetSpec spec,
            AssetInspection inspection,
            Dictionary<int, TextureSet> textureSets)
        {
            AssetBuildResult result = new AssetBuildResult(spec, inspection);
            Dictionary<int, Material> generatedMaterials =
                BuildMaterials(spec, textureSets, result);

            GameObject sourceModel = inspection.SourceModel;
            if (sourceModel == null)
            {
                result.MaterialStatus = "Failed: source model is missing.";
                result.Notes.Add("Prefab was not generated.");
                return result;
            }

            GameObject root = new GameObject("PF_Club_" + spec.AssetId);
            GameObject modelContainer = new GameObject("Model");
            modelContainer.transform.SetParent(root.transform, false);

            GameObject importedInstance =
                PrefabUtility.InstantiatePrefab(sourceModel) as GameObject;
            if (importedInstance == null)
            {
                UnityEngine.Object.DestroyImmediate(root);
                result.MaterialStatus = "Failed: FBX instance could not be created.";
                result.Notes.Add("Prefab was not generated.");
                return result;
            }

            importedInstance.name = sourceModel.name;
            importedInstance.transform.SetParent(modelContainer.transform, false);

            int mappedSlots = AssignGeneratedMaterials(
                importedInstance,
                spec,
                generatedMaterials,
                result);
            int totalSlots = CountMaterialSlots(importedInstance);

            result.WrapperRotation = GetWrapperModelRotation(spec);
            modelContainer.transform.localRotation =
                Quaternion.Euler(result.WrapperRotation);
            bool hasWrapperCorrection =
                result.WrapperRotation != Vector3.zero;
            if (hasWrapperCorrection)
            {
                result.Notes.Add(
                    "Wrapper Model rotation " +
                    result.WrapperRotation +
                    " aligns the control surface with the tabletop.");
            }

            Bounds sourceBounds = new Bounds(Vector3.zero, Vector3.one);
            bool hasBounds = hasWrapperCorrection
                ? TryGetRendererBounds(root, out sourceBounds)
                : TryGetRendererBounds(importedInstance, out sourceBounds);
            if (hasBounds)
            {
                if (!hasWrapperCorrection)
                {
                    importedInstance.transform.position += new Vector3(
                        -sourceBounds.center.x,
                        -sourceBounds.min.y,
                        -sourceBounds.center.z);
                }

                result.SourceBounds = sourceBounds;
                result.HasSourceBounds = true;
            }
            else
            {
                result.Notes.Add(
                    "Renderer bounds were unavailable; model centering was skipped.");
            }

            float defaultScale = CalculateDefaultScale(spec, sourceBounds);
            result.DefaultScale =
                new Vector3(defaultScale, defaultScale, defaultScale);
            result.DefaultRotation = Vector3.zero;
            modelContainer.transform.localScale = result.DefaultScale;

            Bounds scaledBounds;
            if (hasWrapperCorrection &&
                TryGetRendererBounds(root, out scaledBounds))
            {
                modelContainer.transform.position += new Vector3(
                    -scaledBounds.center.x,
                    -scaledBounds.min.y,
                    -scaledBounds.center.z);
            }

            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            if (spec.AddBoxCollider)
            {
                Bounds finalBounds;
                if (TryGetRendererBounds(root, out finalBounds))
                {
                    BoxCollider collider = root.AddComponent<BoxCollider>();
                    collider.center = finalBounds.center;
                    collider.size = finalBounds.size;
                }
            }

            string prefabPath =
                PrefabsRoot + "/PF_Club_" + spec.AssetId + ".prefab";
            result.GeneratedPrefab =
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            result.PrefabPath = prefabPath;

            UnityEngine.Object.DestroyImmediate(root);

            if (textureSets.Count == 0)
            {
                result.MaterialStatus = "Fallback material";
            }
            else if (totalSlots == 0)
            {
                result.MaterialStatus = "No material slots";
            }
            else if (mappedSlots == totalSlots)
            {
                result.MaterialStatus =
                    "Mapped " + mappedSlots + "/" + totalSlots + " slots";
            }
            else
            {
                result.MaterialStatus =
                    "Partial " + mappedSlots + "/" + totalSlots + " slots";
                result.Notes.Add(
                    "Unmatched slots retain their imported FBX material.");
            }

            return result;
        }

        private static Vector3 GetWrapperModelRotation(AssetSpec spec)
        {
            if (spec.AssetId == "DJController" ||
                spec.AssetId == "Turntable")
            {
                return new Vector3(-90f, 0f, 0f);
            }

            return Vector3.zero;
        }

        private static Dictionary<int, Material> BuildMaterials(
            AssetSpec spec,
            Dictionary<int, TextureSet> textureSets,
            AssetBuildResult result)
        {
            Dictionary<int, Material> materials =
                new Dictionary<int, Material>();

            if (textureSets.Count == 0)
            {
                string fallbackName =
                    "MAT_Club_" + spec.AssetId + "_Fallback";
                Material fallback = LoadOrCreateMaterial(
                    MaterialsRoot + "/" + fallbackName + ".mat");
                ConfigureFallbackMaterial(fallback);
                materials[-1] = fallback;
                result.MaterialPaths.Add(
                    AssetDatabase.GetAssetPath(fallback));
                result.UsesFallback = true;
                result.Notes.Add(
                    "No external textures found; neutral fallback material used.");
                return materials;
            }

            foreach (KeyValuePair<int, TextureSet> pair in
                     textureSets.OrderBy(item => item.Key))
            {
                int partIndex = pair.Key;
                TextureSet textureSet = pair.Value;
                string materialName = spec.IsMultipart
                    ? "MAT_Club_" + spec.AssetId + "_Part" +
                      partIndex.ToString("00", CultureInfo.InvariantCulture)
                    : "MAT_Club_" + spec.AssetId;
                string materialPath =
                    MaterialsRoot + "/" + materialName + ".mat";
                Material material = LoadOrCreateMaterial(materialPath);
                ConfigureTexturedMaterial(
                    material,
                    spec,
                    textureSet,
                    result);

                materials[partIndex] = material;
                result.MaterialPaths.Add(materialPath);
            }

            if (spec.AssetId == "NeonSign")
            {
                result.Notes.Add(
                    "Emission remains disabled: tripo_part names do not " +
                    "identify a neon tube with sufficient confidence.");
            }

            return materials;
        }

        private static Material LoadOrCreateMaterial(string materialPath)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Built-in Standard shader was not found.");
            }

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else
            {
                material.shader = shader;
            }

            return material;
        }

        private static void ConfigureFallbackMaterial(Material material)
        {
            material.SetColor("_Color", new Color(0.52f, 0.54f, 0.56f, 1f));
            material.SetTexture("_MainTex", null);
            material.SetTexture("_BumpMap", null);
            material.SetTexture("_MetallicGlossMap", null);
            material.SetFloat("_Metallic", 0.1f);
            material.SetFloat("_Glossiness", 0.32f);
            material.SetFloat("_GlossMapScale", 0.32f);
            material.DisableKeyword("_NORMALMAP");
            material.DisableKeyword("_METALLICGLOSSMAP");
            material.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
        }

        private static void ConfigureTexturedMaterial(
            Material material,
            AssetSpec spec,
            TextureSet textureSet,
            AssetBuildResult result)
        {
            Texture2D baseColor =
                LoadTexture(textureSet.BaseColorPath);
            Texture2D normal =
                LoadTexture(textureSet.NormalPath);

            material.SetColor("_Color", Color.white);
            material.SetTexture("_MainTex", baseColor);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);

            if (normal != null)
            {
                material.EnableKeyword("_NORMALMAP");
            }
            else
            {
                material.DisableKeyword("_NORMALMAP");
                result.Notes.Add(
                    GetPartLabel(spec, textureSet.PartIndex) +
                    " has no normal map.");
            }

            Texture2D packedMap = null;
            if (!string.IsNullOrEmpty(textureSet.MetallicPath) &&
                !string.IsNullOrEmpty(textureSet.RoughnessPath))
            {
                packedMap = GeneratePackedMap(spec, textureSet, result);
            }

            material.SetTexture("_MetallicGlossMap", packedMap);
            if (packedMap != null)
            {
                material.SetFloat("_Metallic", 1f);
                material.SetFloat("_Glossiness", 1f);
                material.SetFloat("_GlossMapScale", 1f);
                material.EnableKeyword("_METALLICGLOSSMAP");
            }
            else
            {
                material.SetFloat("_Metallic", 0.05f);
                material.SetFloat("_Glossiness", 0.35f);
                material.SetFloat("_GlossMapScale", 0.35f);
                material.DisableKeyword("_METALLICGLOSSMAP");
                result.Notes.Add(
                    GetPartLabel(spec, textureSet.PartIndex) +
                    " uses scalar metallic/smoothness fallback.");
            }

            material.SetColor("_EmissionColor", Color.black);
            material.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);

            if (baseColor == null)
            {
                result.Notes.Add(
                    GetPartLabel(spec, textureSet.PartIndex) +
                    " has no base color texture.");
            }
        }

        private static Texture2D GeneratePackedMap(
            AssetSpec spec,
            TextureSet textureSet,
            AssetBuildResult result)
        {
            string suffix = spec.IsMultipart
                ? "_Part" +
                  textureSet.PartIndex.ToString("00", CultureInfo.InvariantCulture)
                : string.Empty;
            string assetPath =
                PackedMapsRoot + "/MS_Club_" + spec.AssetId + suffix + ".png";

            TextureImporter metallicImporter =
                AssetImporter.GetAtPath(textureSet.MetallicPath) as TextureImporter;
            TextureImporter roughnessImporter =
                AssetImporter.GetAtPath(textureSet.RoughnessPath) as TextureImporter;
            if (metallicImporter == null || roughnessImporter == null)
            {
                result.Notes.Add(
                    "Packed map failed: source TextureImporter is unavailable.");
                return null;
            }

            bool metallicWasReadable = metallicImporter.isReadable;
            bool roughnessWasReadable = roughnessImporter.isReadable;

            try
            {
                if (!metallicWasReadable)
                {
                    metallicImporter.isReadable = true;
                    metallicImporter.SaveAndReimport();
                }

                if (!roughnessWasReadable)
                {
                    roughnessImporter =
                        AssetImporter.GetAtPath(textureSet.RoughnessPath)
                        as TextureImporter;
                    roughnessImporter.isReadable = true;
                    roughnessImporter.SaveAndReimport();
                }

                Texture2D metallic = LoadTexture(textureSet.MetallicPath);
                Texture2D roughness = LoadTexture(textureSet.RoughnessPath);
                if (metallic == null || roughness == null)
                {
                    throw new InvalidOperationException(
                        "Metallic or roughness texture failed to load.");
                }

                int targetWidth = Mathf.Min(metallic.width, roughness.width);
                int targetHeight = Mathf.Min(metallic.height, roughness.height);
                if (targetWidth <= 0 || targetHeight <= 0)
                {
                    throw new InvalidOperationException(
                        "Packed map target resolution is invalid.");
                }

                Color32[] metallicPixels =
                    ReadPixelsAtSize(metallic, targetWidth, targetHeight);
                Color32[] roughnessPixels =
                    ReadPixelsAtSize(roughness, targetWidth, targetHeight);
                Color32[] packedPixels = new Color32[metallicPixels.Length];

                for (int i = 0; i < packedPixels.Length; i++)
                {
                    packedPixels[i] = new Color32(
                        metallicPixels[i].r,
                        0,
                        0,
                        (byte)(255 - roughnessPixels[i].r));
                }

                Texture2D packed = new Texture2D(
                    targetWidth,
                    targetHeight,
                    TextureFormat.RGBA32,
                    false,
                    true);
                packed.SetPixels32(packedPixels);
                packed.Apply(false, false);

                string absolutePath = AssetPathToAbsolutePath(assetPath);
                File.WriteAllBytes(absolutePath, packed.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(packed);

                AssetDatabase.ImportAsset(
                    assetPath,
                    ImportAssetOptions.ForceSynchronousImport);
                TextureImporter packedImporter =
                    AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (packedImporter == null)
                {
                    throw new InvalidOperationException(
                        "Generated packed map importer is unavailable.");
                }

                packedImporter.textureType = TextureImporterType.Default;
                packedImporter.sRGBTexture = false;
                packedImporter.mipmapEnabled = true;
                packedImporter.alphaSource = TextureImporterAlphaSource.FromInput;
                packedImporter.alphaIsTransparency = false;
                packedImporter.isReadable = false;
                packedImporter.maxTextureSize =
                    Mathf.Min(spec.MaximumTextureSize, 2048);
                packedImporter.SaveAndReimport();

                result.PackedMapPaths.Add(assetPath);
                return LoadTexture(assetPath);
            }
            catch (Exception exception)
            {
                result.Notes.Add(
                    GetPartLabel(spec, textureSet.PartIndex) +
                    " packed map failed: " + exception.Message);
                Debug.LogWarning(
                    "[Sashimi Boy] " + spec.AssetId +
                    " packed map fallback: " + exception.Message);
                return null;
            }
            finally
            {
                metallicImporter =
                    AssetImporter.GetAtPath(textureSet.MetallicPath)
                    as TextureImporter;
                roughnessImporter =
                    AssetImporter.GetAtPath(textureSet.RoughnessPath)
                    as TextureImporter;

                if (metallicImporter != null &&
                    metallicImporter.isReadable != metallicWasReadable)
                {
                    metallicImporter.isReadable = metallicWasReadable;
                    metallicImporter.SaveAndReimport();
                }

                if (roughnessImporter != null &&
                    roughnessImporter.isReadable != roughnessWasReadable)
                {
                    roughnessImporter.isReadable = roughnessWasReadable;
                    roughnessImporter.SaveAndReimport();
                }
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

        private static int AssignGeneratedMaterials(
            GameObject importedInstance,
            AssetSpec spec,
            Dictionary<int, Material> generatedMaterials,
            AssetBuildResult result)
        {
            int mappedSlots = 0;
            Renderer[] renderers =
                importedInstance.GetComponentsInChildren<Renderer>(true);

            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                Material[] slots = renderer.sharedMaterials;
                Mesh mesh = GetRendererMesh(renderer);

                for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                {
                    Material selectedMaterial = null;

                    if (!spec.IsMultipart)
                    {
                        generatedMaterials.TryGetValue(-1, out selectedMaterial);
                    }
                    else
                    {
                        int partIndex = ExtractPartIndex(
                            slots[slotIndex] != null
                                ? slots[slotIndex].name
                                : string.Empty);
                        if (partIndex < 0)
                        {
                            partIndex = ExtractPartIndex(renderer.name);
                        }

                        if (partIndex < 0 && mesh != null)
                        {
                            partIndex = ExtractPartIndex(mesh.name);
                        }

                        if (partIndex >= 0)
                        {
                            generatedMaterials.TryGetValue(
                                partIndex,
                                out selectedMaterial);
                        }
                    }

                    if (selectedMaterial == null)
                    {
                        result.UnmatchedSlots.Add(
                            renderer.name + " slot " +
                            slotIndex.ToString(CultureInfo.InvariantCulture) +
                            " (" +
                            (slots[slotIndex] != null
                                ? slots[slotIndex].name
                                : "missing") +
                            ")");
                        continue;
                    }

                    slots[slotIndex] = selectedMaterial;
                    mappedSlots++;
                }

                renderer.sharedMaterials = slots;
            }

            return mappedSlots;
        }

        private static float CalculateDefaultScale(
            AssetSpec spec,
            Bounds rawBounds)
        {
            float largestDimension =
                Mathf.Max(rawBounds.size.x, rawBounds.size.y, rawBounds.size.z);
            if (!float.IsFinite(largestDimension) || largestDimension < 0.0001f)
            {
                return 1f;
            }

            return Mathf.Clamp(
                spec.TargetLargestDimension / largestDimension,
                0.001f,
                1000f);
        }

        private static ClubAssetCatalog BuildCatalog(
            List<AssetBuildResult> results)
        {
            ClubAssetCatalog catalog =
                AssetDatabase.LoadAssetAtPath<ClubAssetCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ClubAssetCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.assets.Clear();
            for (int i = 0; i < results.Count; i++)
            {
                AssetBuildResult result = results[i];
                ClubAssetCatalogEntry entry = new ClubAssetCatalogEntry
                {
                    assetId = result.Spec.AssetId,
                    displayName = result.Spec.DisplayName,
                    sourceModel = result.Inspection.SourceModel,
                    generatedPrefab = result.GeneratedPrefab,
                    defaultScale = result.DefaultScale,
                    defaultRotation = result.DefaultRotation,
                    hasExternalTextures = result.Inspection.HasExternalTextures,
                    materialStatus = result.MaterialStatus,
                    notes = string.Join("\n", result.Notes.Distinct()),
                };
                catalog.assets.Add(entry);
            }

            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        private static void BuildGalleryScene(ClubAssetCatalog catalog)
        {
            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene galleryScene = SceneManager.GetSceneByPath(GalleryScenePath);
            bool wasAlreadyLoaded = galleryScene.IsValid() && galleryScene.isLoaded;
            bool createdAsSingleScene = false;

            if (!wasAlreadyLoaded)
            {
                bool hasUntitledScene =
                    previousActiveScene.IsValid() &&
                    string.IsNullOrEmpty(previousActiveScene.path);

                if (!Application.isBatchMode &&
                    hasUntitledScene &&
                    previousActiveScene.isDirty &&
                    !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    throw new OperationCanceledException(
                        "Gallery build was canceled before replacing an " +
                        "unsaved untitled scene.");
                }

                hasUntitledScene =
                    previousActiveScene.IsValid() &&
                    string.IsNullOrEmpty(previousActiveScene.path);
                createdAsSingleScene = Application.isBatchMode || hasUntitledScene;
                galleryScene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    createdAsSingleScene
                        ? NewSceneMode.Single
                        : NewSceneMode.Additive);
            }

            SceneManager.SetActiveScene(galleryScene);
            GameObject[] oldRoots = galleryScene.GetRootGameObjects();
            for (int i = 0; i < oldRoots.Length; i++)
            {
                UnityEngine.Object.DestroyImmediate(oldRoots[i]);
            }

            Material floorMaterial = LoadOrCreateMaterial(
                MaterialsRoot + "/MAT_Club_GalleryFloor.mat");
            ConfigureGalleryFloorMaterial(floorMaterial);

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Gallery_Floor";
            floor.transform.position = new Vector3(0f, -0.12f, 0f);
            floor.transform.localScale = new Vector3(20f, 0.2f, 15f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.075f, 0.085f, 0.095f, 1f);
            camera.fieldOfView = 50f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.transform.position = new Vector3(11f, 10f, -18f);
            cameraObject.transform.LookAt(new Vector3(0f, 1f, 0f));

            GameObject lightObject = new GameObject("Directional Light");
            Light directionalLight = lightObject.AddComponent<Light>();
            directionalLight.type = LightType.Directional;
            directionalLight.color = new Color(1f, 0.95f, 0.88f, 1f);
            directionalLight.intensity = 1.15f;
            directionalLight.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

            GameObject fillLightObject = new GameObject("Gallery Fill Light");
            Light fillLight = fillLightObject.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.color = new Color(0.42f, 0.62f, 0.85f, 1f);
            fillLight.intensity = 0.45f;
            fillLightObject.transform.rotation = Quaternion.Euler(35f, 145f, 0f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.32f, 0.34f, 0.38f, 1f);

            const int columns = 4;
            const float horizontalSpacing = 4.6f;
            const float verticalSpacing = 4.4f;
            for (int i = 0; i < catalog.assets.Count; i++)
            {
                ClubAssetCatalogEntry entry = catalog.assets[i];
                if (entry == null || entry.generatedPrefab == null)
                {
                    continue;
                }

                int column = i % columns;
                int row = i / columns;
                Vector3 position = new Vector3(
                    (column - 1.5f) * horizontalSpacing,
                    0f,
                    (1f - row) * verticalSpacing);

                GameObject instance =
                    PrefabUtility.InstantiatePrefab(
                        entry.generatedPrefab,
                        galleryScene) as GameObject;
                instance.name = "Gallery_" + entry.assetId;
                instance.transform.position = position;
                instance.transform.rotation = Quaternion.Euler(
                    entry.defaultRotation + new Vector3(0f, 180f, 0f));
                instance.transform.localScale = Vector3.one;

                ClubAssetGalleryMarker marker =
                    instance.AddComponent<ClubAssetGalleryMarker>();
                marker.assetId = entry.assetId;
                marker.displayName = entry.displayName;
                marker.defaultScale = entry.defaultScale;
            }

            Text selectionText = CreateGalleryHud(galleryScene);
            GameObject controllerObject = new GameObject("GalleryController");
            ClubAssetGalleryController controller =
                controllerObject.AddComponent<ClubAssetGalleryController>();
            controller.galleryCamera = camera;
            controller.selectionText = selectionText;
            controller.distance = 9f;
            controller.minimumDistance = 2.5f;
            controller.maximumDistance = 30f;

            EditorSceneManager.MarkSceneDirty(galleryScene);
            EditorSceneManager.SaveScene(galleryScene, GalleryScenePath);

            if (!wasAlreadyLoaded && !createdAsSingleScene)
            {
                EditorSceneManager.CloseScene(galleryScene, true);
            }

            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(previousActiveScene);
            }
        }

        private static Text CreateGalleryHud(Scene galleryScene)
        {
            GameObject canvasObject = new GameObject(
                "ClubAssetGallery_HUDCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(canvasObject, galleryScene);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject panelObject = new GameObject(
                "SelectionPanel",
                typeof(RectTransform),
                typeof(Image));
            panelObject.transform.SetParent(canvasObject.transform, false);
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(24f, -24f);
            panelRect.sizeDelta = new Vector2(360f, 100f);
            panelObject.GetComponent<Image>().color =
                new Color(0.025f, 0.03f, 0.035f, 0.88f);

            GameObject textObject = new GameObject(
                "SelectedAssetText",
                typeof(RectTransform),
                typeof(Text));
            textObject.transform.SetParent(panelObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18f, 12f);
            textRect.offsetMax = new Vector2(-18f, -12f);

            Text text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 24;
            text.color = new Color(0.94f, 0.95f, 0.96f, 1f);
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.text = "Club Asset Gallery";
            return text;
        }

        private static void ConfigureGalleryFloorMaterial(Material material)
        {
            material.SetColor("_Color", new Color(0.24f, 0.255f, 0.27f, 1f));
            material.SetTexture("_MainTex", null);
            material.SetTexture("_BumpMap", null);
            material.SetTexture("_MetallicGlossMap", null);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Glossiness", 0.18f);
            material.DisableKeyword("_NORMALMAP");
            material.DisableKeyword("_METALLICGLOSSMAP");
            material.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
        }

        private static Dictionary<int, TextureSet> FindTextureSets(AssetSpec spec)
        {
            Dictionary<int, TextureSet> sets =
                new Dictionary<int, TextureSet>();
            if (!AssetDatabase.IsValidFolder(spec.TextureRoot))
            {
                return sets;
            }

            string[] textureGuids =
                AssetDatabase.FindAssets("t:Texture2D", new[] { spec.TextureRoot });
            for (int i = 0; i < textureGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(textureGuids[i]);
                TextureKind kind = GetTextureKind(path);
                if (kind == TextureKind.Unknown)
                {
                    continue;
                }

                int partIndex = ExtractPartIndex(path);
                if (spec.IsMultipart && partIndex < 0)
                {
                    continue;
                }

                int key = spec.IsMultipart ? partIndex : -1;
                TextureSet set;
                if (!sets.TryGetValue(key, out set))
                {
                    set = new TextureSet(key);
                    sets.Add(key, set);
                }

                set.Assign(kind, path);
            }

            return sets;
        }

        private static TextureKind GetTextureKind(string path)
        {
            string name =
                Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (name.EndsWith("_basecolor", StringComparison.Ordinal))
            {
                return TextureKind.BaseColor;
            }

            if (name.EndsWith("_normal", StringComparison.Ordinal))
            {
                return TextureKind.Normal;
            }

            if (name.EndsWith("_metallic", StringComparison.Ordinal))
            {
                return TextureKind.Metallic;
            }

            if (name.EndsWith("_roughness", StringComparison.Ordinal))
            {
                return TextureKind.Roughness;
            }

            if (name.EndsWith("_rm", StringComparison.Ordinal))
            {
                return TextureKind.Rm;
            }

            return TextureKind.Unknown;
        }

        private static int ExtractPartIndex(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return -1;
            }

            Match match = PartRegex.Match(value);
            int partIndex;
            return match.Success &&
                   int.TryParse(
                       match.Groups[1].Value,
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out partIndex)
                ? partIndex
                : -1;
        }

        private static Texture2D LoadTexture(string path)
        {
            return string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Mesh GetRendererMesh(Renderer renderer)
        {
            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null)
            {
                return skinned.sharedMesh;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        private static int CountMaterialSlots(GameObject root)
        {
            int count = 0;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                count += renderers[i].sharedMaterials.Length;
            }

            return count;
        }

        private static bool TryGetRendererBounds(
            GameObject root,
            out Bounds bounds)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool found = false;
            bounds = new Bounds(root.transform.position, Vector3.zero);

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!renderer.enabled)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return found;
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

        private static string GetPartLabel(AssetSpec spec, int partIndex)
        {
            return spec.IsMultipart
                ? spec.AssetId + " part " +
                  partIndex.ToString(CultureInfo.InvariantCulture)
                : spec.AssetId;
        }

        private static string AssetPathToAbsolutePath(string assetPath)
        {
            string projectRoot =
                Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath);
        }

        private static void EnsureGeneratedFolders()
        {
            EnsureFolder(GeneratedRoot);
            EnsureFolder(MaterialsRoot);
            EnsureFolder(PackedMapsRoot);
            EnsureFolder(PrefabsRoot);
            EnsureFolder(DataRoot);
            EnsureFolder(ScenesRoot);
            EnsureFolder(ReportsRoot);
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

        private static void ShowProgress(
            string title,
            string info,
            float progress)
        {
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayProgressBar(title, info, progress);
            }
        }

        private static void WriteReport(
            List<AssetInspection> inspections,
            List<AssetBuildResult> buildResults)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("# Sashimi Boy Club Asset Import Report");
            report.AppendLine();
            report.AppendLine("Generated by `Sashimi Boy > Art` tools.");
            report.AppendLine();
            report.AppendLine("## Project");
            report.AppendLine();
            report.AppendLine("- Render pipeline: Built-in Render Pipeline");
            report.AppendLine("- Material shader: `Standard`");
            report.AppendLine("- Source root: `" + SourceRoot + "`");
            report.AppendLine("- Generated root: `" + GeneratedRoot + "`");
            report.AppendLine("- Source files moved/deleted/overwritten: No");
            report.AppendLine();
            report.AppendLine("## Source Hygiene");
            report.AppendLine();

            List<string> junkFiles = FindMacJunkFiles();
            if (junkFiles.Count == 0)
            {
                report.AppendLine("- No `__MACOSX` or `._*` files found.");
            }
            else
            {
                for (int i = 0; i < junkFiles.Count; i++)
                {
                    report.AppendLine("- Removable: `" + junkFiles[i] + "`");
                }
            }

            report.AppendLine();
            report.AppendLine("## Asset Summary");
            report.AppendLine();
            report.AppendLine(
                "| Asset | FBX | Slots | Meshes | Bounds size | " +
                "Importer scale | Animation | Default scale | Material |");
            report.AppendLine(
                "|---|---|---:|---:|---|---|---|---|---|");

            for (int i = 0; i < inspections.Count; i++)
            {
                AssetInspection inspection = inspections[i];
                AssetBuildResult result = buildResults != null
                    ? buildResults.FirstOrDefault(
                        item => item.Spec == inspection.Spec)
                    : null;
                string bounds = inspection.HasBounds
                    ? FormatVector(inspection.Bounds.size)
                    : "missing";
                string defaultScale = result != null
                    ? FormatVector(result.DefaultScale)
                    : "not built";
                string materialStatus = result != null
                    ? result.MaterialStatus
                    : "not built";

                report.AppendLine(
                    "| " + inspection.Spec.AssetId +
                    " | `" + inspection.Spec.ModelPath +
                    "` | " + inspection.MaterialSlotCount +
                    " | " + inspection.MeshCount +
                    " | " + bounds +
                    " | global " +
                    inspection.GlobalScale.ToString(
                        "0.#####",
                        CultureInfo.InvariantCulture) +
                    ", file " +
                    inspection.FileScale.ToString(
                        "0.#####",
                        CultureInfo.InvariantCulture) +
                    " | " +
                    (inspection.HasAnimationData
                        ? "kept"
                        : "none / import off") +
                    " | " + defaultScale +
                    " | " + materialStatus + " |");
            }

            report.AppendLine();
            report.AppendLine("## Detailed Inspection");
            report.AppendLine();

            for (int i = 0; i < inspections.Count; i++)
            {
                AssetInspection inspection = inspections[i];
                report.AppendLine("### " + inspection.Spec.AssetId);
                report.AppendLine();
                report.AppendLine("- FBX: `" + inspection.Spec.ModelPath + "`");
                report.AppendLine(
                    "- Unit/import scale: globalScale=" +
                    inspection.GlobalScale.ToString(
                        "0.#####",
                        CultureInfo.InvariantCulture) +
                    ", fileScale=" +
                    inspection.FileScale.ToString(
                        "0.#####",
                        CultureInfo.InvariantCulture) +
                    ", useFileScale=" + inspection.UseFileScale +
                    ", bakeAxisConversion=" +
                    inspection.BakeAxisConversion);
                report.AppendLine(
                    "- Imported root transform: position " +
                    FormatVector(inspection.RootPosition) +
                    ", rotation " +
                    FormatVector(inspection.RootRotation) +
                    ", scale " +
                    FormatVector(inspection.RootScale));
                report.AppendLine(
                    "- Bounds: " +
                    (inspection.HasBounds
                        ? "center " +
                          FormatVector(inspection.Bounds.center) +
                          ", size " +
                          FormatVector(inspection.Bounds.size)
                        : "missing"));
                report.AppendLine(
                    "- Renderers / meshes / material slots: " +
                    inspection.RendererCount + " / " +
                    inspection.MeshCount + " / " +
                    inspection.MaterialSlotCount);
                report.AppendLine(
                    "- Animation: data=" + inspection.HasAnimationData +
                    ", Import Animation=" + inspection.ImportAnimation);
                report.AppendLine(
                    "- External texture sets: " +
                    inspection.TextureSetCount);

                if (inspection.Spec.IsMultipart)
                {
                    report.AppendLine(
                        "- Texture part indices: " +
                        FormatIndices(inspection.TexturePartIndices));
                    report.AppendLine(
                        "- Slot/mesh part indices: " +
                        FormatIndices(inspection.SlotPartIndices));
                }

                report.AppendLine("- Renderer / mesh / slot details:");
                if (inspection.SlotDetails.Count == 0)
                {
                    report.AppendLine("  - None");
                }
                else
                {
                    for (int detailIndex = 0;
                         detailIndex < inspection.SlotDetails.Count;
                         detailIndex++)
                    {
                        report.AppendLine(
                            "  - " + inspection.SlotDetails[detailIndex]);
                    }
                }

                if (inspection.Warnings.Count == 0)
                {
                    report.AppendLine("- Validation warnings: None");
                }
                else
                {
                    report.AppendLine("- Validation warnings:");
                    foreach (string warning in inspection.Warnings.Distinct())
                    {
                        report.AppendLine("  - " + warning);
                    }
                }

                report.AppendLine();
            }

            if (buildResults != null)
            {
                report.AppendLine("## Generated Materials");
                report.AppendLine();
                foreach (string path in buildResults
                             .SelectMany(item => item.MaterialPaths)
                             .Distinct()
                             .OrderBy(path => path))
                {
                    report.AppendLine("- `" + path + "`");
                }
                report.AppendLine(
                    "- `" + MaterialsRoot + "/MAT_Club_GalleryFloor.mat`");

                report.AppendLine();
                report.AppendLine("## Generated Packed Maps");
                report.AppendLine();
                List<string> packedMaps = buildResults
                    .SelectMany(item => item.PackedMapPaths)
                    .Distinct()
                    .OrderBy(path => path)
                    .ToList();
                if (packedMaps.Count == 0)
                {
                    report.AppendLine("- None; scalar fallbacks were retained.");
                }
                else
                {
                    for (int i = 0; i < packedMaps.Count; i++)
                    {
                        report.AppendLine("- `" + packedMaps[i] + "`");
                    }
                }

                report.AppendLine();
                report.AppendLine("## Generated Prefabs");
                report.AppendLine();
                for (int i = 0; i < buildResults.Count; i++)
                {
                    AssetBuildResult result = buildResults[i];
                    report.AppendLine(
                        "- `" + result.PrefabPath + "`; defaultScale=" +
                        FormatVector(result.DefaultScale) +
                        "; wrapperModelRotation=" +
                        FormatVector(result.WrapperRotation));
                }

                report.AppendLine();
                report.AppendLine("## Fallback And Unresolved Assignments");
                report.AppendLine();
                for (int i = 0; i < buildResults.Count; i++)
                {
                    AssetBuildResult result = buildResults[i];
                    if (result.UsesFallback)
                    {
                        report.AppendLine(
                            "- " + result.Spec.AssetId +
                            ": neutral fallback material.");
                    }

                    for (int slotIndex = 0;
                         slotIndex < result.UnmatchedSlots.Count;
                         slotIndex++)
                    {
                        report.AppendLine(
                            "- " + result.Spec.AssetId +
                            ": unresolved `" +
                            result.UnmatchedSlots[slotIndex] +
                            "`; imported FBX material retained.");
                    }
                }

                report.AppendLine();
                report.AppendLine("## Build Notes");
                report.AppendLine();
                for (int i = 0; i < buildResults.Count; i++)
                {
                    AssetBuildResult result = buildResults[i];
                    foreach (string note in result.Notes.Distinct())
                    {
                        report.AppendLine(
                            "- " + result.Spec.AssetId + ": " + note);
                    }
                }

                report.AppendLine();
                report.AppendLine("## Gallery");
                report.AppendLine();
                report.AppendLine("- Scene: `" + GalleryScenePath + "`");
                report.AppendLine("- Layout: 4 columns x 3 rows");
                report.AppendLine(
                    "- UI: `ClubAssetGallery_HUDCanvas` (Screen Space Overlay)");
                report.AppendLine(
                    "- Camera: right mouse drag orbits, wheel zooms, " +
                    "Q/E changes the selected asset.");
            }

            File.WriteAllText(
                AssetPathToAbsolutePath(ReportPath),
                report.ToString(),
                new UTF8Encoding(false));
            AssetDatabase.ImportAsset(
                ReportPath,
                ImportAssetOptions.ForceSynchronousImport);
        }

        private static List<string> FindMacJunkFiles()
        {
            string absoluteRoot = AssetPathToAbsolutePath(SourceRoot);
            if (!Directory.Exists(absoluteRoot))
            {
                return new List<string>();
            }

            return Directory
                .GetFiles(absoluteRoot, "*", SearchOption.AllDirectories)
                .Where(path =>
                    Path.GetFileName(path)
                        .StartsWith("._", StringComparison.Ordinal) ||
                    path.Split(Path.DirectorySeparatorChar)
                        .Any(segment =>
                            string.Equals(
                                segment,
                                "__MACOSX",
                                StringComparison.Ordinal)))
                .Select(path =>
                    path.Replace(
                        Directory.GetParent(Application.dataPath).FullName +
                        Path.DirectorySeparatorChar,
                        string.Empty))
                .OrderBy(path => path)
                .ToList();
        }

        private static string FormatVector(Vector3 value)
        {
            return "(" +
                   value.x.ToString("0.###", CultureInfo.InvariantCulture) +
                   ", " +
                   value.y.ToString("0.###", CultureInfo.InvariantCulture) +
                   ", " +
                   value.z.ToString("0.###", CultureInfo.InvariantCulture) +
                   ")";
        }

        private static string FormatIndices(List<int> indices)
        {
            return indices.Count == 0
                ? "none"
                : string.Join(
                    ", ",
                    indices.Select(index =>
                        index.ToString(CultureInfo.InvariantCulture)));
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

        private sealed class AssetSpec
        {
            public AssetSpec(
                string assetId,
                string modelFileName,
                int maximumTextureSize,
                float targetLargestDimension,
                bool addBoxCollider,
                bool isMultipart)
            {
                AssetId = assetId;
                DisplayName = assetId;
                ModelFileName = modelFileName;
                MaximumTextureSize = maximumTextureSize;
                TargetLargestDimension = targetLargestDimension;
                AddBoxCollider = addBoxCollider;
                IsMultipart = isMultipart;
            }

            public string AssetId { get; private set; }
            public string DisplayName { get; private set; }
            public string ModelFileName { get; private set; }
            public int MaximumTextureSize { get; private set; }
            public float TargetLargestDimension { get; private set; }
            public bool AddBoxCollider { get; private set; }
            public bool IsMultipart { get; private set; }
            public string ModelPath
            {
                get
                {
                    return SourceRoot + "/" + AssetId +
                           "/Models/" + ModelFileName;
                }
            }

            public string TextureRoot
            {
                get { return SourceRoot + "/" + AssetId + "/Textures"; }
            }
        }

        private sealed class TextureSet
        {
            public TextureSet(int partIndex)
            {
                PartIndex = partIndex;
            }

            public int PartIndex { get; private set; }
            public string BaseColorPath { get; private set; }
            public string NormalPath { get; private set; }
            public string MetallicPath { get; private set; }
            public string RoughnessPath { get; private set; }
            public string RmPath { get; private set; }

            public void Assign(TextureKind kind, string path)
            {
                switch (kind)
                {
                    case TextureKind.BaseColor:
                        BaseColorPath = path;
                        break;
                    case TextureKind.Normal:
                        NormalPath = path;
                        break;
                    case TextureKind.Metallic:
                        MetallicPath = path;
                        break;
                    case TextureKind.Roughness:
                        RoughnessPath = path;
                        break;
                    case TextureKind.Rm:
                        RmPath = path;
                        break;
                }
            }
        }

        private sealed class AssetInspection
        {
            public AssetInspection(AssetSpec spec)
            {
                Spec = spec;
            }

            public AssetSpec Spec { get; private set; }
            public GameObject SourceModel;
            public float GlobalScale = 1f;
            public float FileScale = 1f;
            public bool UseFileScale;
            public bool BakeAxisConversion;
            public bool ImportAnimation;
            public bool HasAnimationData;
            public Vector3 RootPosition;
            public Vector3 RootRotation;
            public Vector3 RootScale = Vector3.one;
            public int RendererCount;
            public int MeshCount;
            public int MaterialSlotCount;
            public bool HasBounds;
            public Bounds Bounds;
            public bool HasExternalTextures;
            public int TextureSetCount;
            public readonly List<int> TexturePartIndices = new List<int>();
            public readonly List<int> SlotPartIndices = new List<int>();
            public readonly List<string> SlotDetails = new List<string>();
            public readonly List<string> Warnings = new List<string>();
        }

        private sealed class AssetBuildResult
        {
            public AssetBuildResult(
                AssetSpec spec,
                AssetInspection inspection)
            {
                Spec = spec;
                Inspection = inspection;
            }

            public AssetSpec Spec { get; private set; }
            public AssetInspection Inspection { get; private set; }
            public GameObject GeneratedPrefab;
            public string PrefabPath = string.Empty;
            public Vector3 DefaultScale = Vector3.one;
            public Vector3 DefaultRotation;
            public Vector3 WrapperRotation;
            public bool HasSourceBounds;
            public Bounds SourceBounds;
            public bool UsesFallback;
            public string MaterialStatus = "Not built";
            public readonly List<string> MaterialPaths = new List<string>();
            public readonly List<string> PackedMapPaths = new List<string>();
            public readonly List<string> UnmatchedSlots = new List<string>();
            public readonly List<string> Notes = new List<string>();
        }
    }
}
