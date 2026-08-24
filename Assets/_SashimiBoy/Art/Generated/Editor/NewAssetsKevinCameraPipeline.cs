#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SashimiBoy.EditorTools
{
    public static class NewAssetsKevinCameraPipeline
    {
        private const string SourceRoot =
            "Assets/_SashimiBoy/Art/Source";
        private const string GeneratedRoot =
            "Assets/_SashimiBoy/Art/Generated";
        private const string KevinMaterialRoot =
            GeneratedRoot + "/Materials/Characters/Kevin";
        private const string FishShopMaterialRoot =
            GeneratedRoot + "/Materials/FishShop";
        private const string KevinPackedRoot =
            GeneratedRoot + "/PackedMaps/Characters/Kevin";
        private const string FishShopPackedRoot =
            GeneratedRoot + "/PackedMaps/FishShop";
        private const string CharacterPrefabRoot =
            GeneratedRoot + "/Prefabs/Characters";
        private const string FishShopPrefabRoot =
            GeneratedRoot + "/Prefabs/FishShop";
        private const string DataRoot = GeneratedRoot + "/Data";
        private const string GeneratedScenesRoot =
            GeneratedRoot + "/Scenes";
        private const string ReportsRoot = GeneratedRoot + "/Reports";

        private const string CatalogPath =
            DataRoot + "/KevinVariantCatalog.asset";
        private const string PlayerPrefabPath =
            CharacterPrefabRoot + "/PF_Player_Kevin_FirstPerson.prefab";
        private const string KevinGalleryPath =
            GeneratedScenesRoot + "/KevinAssetGallery.unity";
        private const string FishShopGalleryPath =
            GeneratedScenesRoot + "/FishShopAssetGallery.unity";
        private const string ReportPath =
            ReportsRoot + "/NewAssetsAndKevinCameraReport.md";

        private const string SceneRoot = "Assets/_SashimiBoy/Scenes";
        private const string BackupRoot = SceneRoot + "/Backups";
        private const string BootstrapScenePath =
            SceneRoot + "/Bootstrap.unity";
        private const string StreetScenePath =
            SceneRoot + "/Street.unity";
        private const string FishShopScenePath =
            SceneRoot + "/FishShopDialogue.unity";
        private const string EquipmentShopScenePath =
            SceneRoot + "/EquipmentShop.unity";
        private const string ClubScenePath =
            SceneRoot + "/Club.unity";
        private const string StageScenePath =
            SceneRoot + "/Stage01_Salmon.unity";

        private static readonly AssetSpec[] AssetSpecs =
        {
            AssetSpec.Kevin("AmbiguousFace", "Ambiguous Face"),
            AssetSpec.Kevin("PlainFace", "Plain Face"),
            AssetSpec.Kevin("CuteFace", "Cute Face"),
            AssetSpec.Kevin("WesternFace", "Western Face"),
            AssetSpec.Fish("Salmon", "Salmon", 1.25f),
            AssetSpec.Fish("Rockfish", "Rockfish", 1.15f),
            AssetSpec.Fish("Mullet", "Mullet", 1.2f),
            AssetSpec.FishShop(
                "KitchenKnife",
                "Kitchen Knife",
                "Props/KitchenKnife",
                "PF_Prop_KitchenKnife",
                1024,
                0.4f),
            AssetSpec.FishShop(
                "SashimiTable",
                "Sashimi Table",
                "Fixtures/SashimiTable",
                "PF_Fixture_SashimiTable",
                2048,
                2.6f),
            AssetSpec.FishShop(
                "DisplayInside",
                "Display Inside",
                "Fixtures/DisplayInside",
                "PF_Fixture_DisplayInside",
                2048,
                2.8f),
            AssetSpec.FishShop(
                "DisplayOutside",
                "Display Outside",
                "Fixtures/DisplayOutside",
                "PF_Fixture_DisplayOutside",
                2048,
                2.8f),
        };

        private static readonly ExplorationSceneSpec[] ExplorationScenes =
        {
            new ExplorationSceneSpec(
                "Street",
                StreetScenePath,
                BackupRoot + "/Street_PreKevinCamera.unity",
                false,
                0f),
            new ExplorationSceneSpec(
                "FishShopDialogue",
                FishShopScenePath,
                BackupRoot + "/FishShopDialogue_PreKevinCamera.unity",
                false,
                0f),
            new ExplorationSceneSpec(
                "EquipmentShop",
                EquipmentShopScenePath,
                BackupRoot + "/EquipmentShop_PreKevinCamera.unity",
                true,
                0f),
            new ExplorationSceneSpec(
                "Club",
                ClubScenePath,
                BackupRoot + "/Club_PreKevinCamera.unity",
                true,
                0f),
        };

        [MenuItem(
            "Sashimi Boy/Art/Validate New Character And FishShop Assets")]
        public static void ValidateNewCharacterAndFishShopAssets()
        {
            RunPipeline(false, true);
        }

        [MenuItem(
            "Sashimi Boy/Art/Build New Character And FishShop Prefabs")]
        public static void BuildNewCharacterAndFishShopPrefabs()
        {
            RunPipeline(true, true);
        }

        public static void BuildNewCharacterAndFishShopPrefabsBatch()
        {
            RunPipeline(true, false);
        }

        public static void RebuildForPrototypeGenerator()
        {
            RunPipeline(true, false);
        }

        [MenuItem("Sashimi Boy/Art/Open Kevin Asset Gallery")]
        public static void OpenKevinAssetGallery()
        {
            OpenGeneratedScene(KevinGalleryPath);
        }

        [MenuItem("Sashimi Boy/Art/Open FishShop Asset Gallery")]
        public static void OpenFishShopAssetGallery()
        {
            OpenGeneratedScene(FishShopGalleryPath);
        }

        private static void RunPipeline(bool buildOutputs, bool showDialog)
        {
            List<PipelineLog> capturedLogs = new List<PipelineLog>();
            Application.LogCallback capture = (condition, stackTrace, type) =>
            {
                if (type == LogType.Warning || type == LogType.Error ||
                    type == LogType.Exception || type == LogType.Assert)
                {
                    capturedLogs.Add(new PipelineLog(type, condition));
                }
            };

            Application.logMessageReceived += capture;
            try
            {
                EnsureGeneratedFolders();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                List<AssetInspection> inspections = InspectAllAssets(true);
                List<AssetBuildResult> results = null;
                KevinVariantCatalog catalog =
                    AssetDatabase.LoadAssetAtPath<KevinVariantCatalog>(
                        CatalogPath);

                if (buildOutputs)
                {
                    results = BuildAssets(inspections);
                    catalog = BuildKevinCatalog(results);
                    BuildPlayerPrefab(catalog);
                    BuildKevinGallery(results, catalog);
                    BuildFishShopGallery(results);
                    ConfigureBootstrapStart();
                    CreateSceneBackups();
                    IntegrateExplorationScenes(catalog);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(
                    ImportAssetOptions.ForceSynchronousImport);

                List<SceneAudit> audits = AuditScenes();
                catalog = AssetDatabase.LoadAssetAtPath<KevinVariantCatalog>(
                    CatalogPath);
                WriteReport(
                    inspections,
                    results,
                    catalog,
                    audits,
                    capturedLogs);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(
                    ImportAssetOptions.ForceSynchronousImport);

                if (buildOutputs && File.Exists(StreetScenePath))
                {
                    EditorSceneManager.OpenScene(
                        StreetScenePath,
                        OpenSceneMode.Single);
                }

                string message = buildOutputs
                    ? "New assets, galleries, Kevin visual, and exploration " +
                      "camera were generated."
                    : "New character and FishShop source assets were validated.";
                Debug.Log("[Sashimi Boy] " + message);
                if (showDialog)
                {
                    EditorUtility.DisplayDialog("Sashimi Boy", message, "OK");
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
            finally
            {
                Application.logMessageReceived -= capture;
                EditorUtility.ClearProgressBar();
            }
        }

        private static List<AssetInspection> InspectAllAssets(
            bool applyImporterRules)
        {
            List<AssetInspection> inspections =
                new List<AssetInspection>();
            for (int i = 0; i < AssetSpecs.Length; i++)
            {
                AssetSpec spec = AssetSpecs[i];
                ShowProgress(
                    "Kevin + FishShop Asset Validation",
                    spec.DisplayName,
                    (float)i / AssetSpecs.Length);
                AssetInspection inspection = DiscoverAsset(spec);
                if (applyImporterRules)
                {
                    ConfigureTextureImporters(inspection);
                    ConfigureModelImporter(inspection);
                }

                InspectImportedModel(inspection);
                inspections.Add(inspection);
            }

            return inspections;
        }

        private static AssetInspection DiscoverAsset(AssetSpec spec)
        {
            AssetInspection inspection = new AssetInspection(spec);
            string modelsRoot = spec.SourceFolder + "/Models";
            string absoluteModelsRoot = AssetPathToAbsolutePath(modelsRoot);
            if (!Directory.Exists(absoluteModelsRoot))
            {
                inspection.Warnings.Add("Models folder is missing.");
                return inspection;
            }

            string[] fbxFiles = Directory
                .GetFiles(absoluteModelsRoot, "*", SearchOption.TopDirectoryOnly)
                .Where(path => string.Equals(
                    Path.GetExtension(path),
                    ".fbx",
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (fbxFiles.Length == 0)
            {
                inspection.Warnings.Add("No FBX was discovered.");
            }
            else
            {
                inspection.ModelPath = AbsolutePathToAssetPath(fbxFiles[0]);
                if (fbxFiles.Length > 1)
                {
                    inspection.Warnings.Add(
                        "Multiple FBX files were found; using `" +
                        inspection.ModelPath + "`.");
                }
            }

            inspection.Textures = FindTextureSet(spec.SourceFolder + "/Textures");
            if (string.IsNullOrEmpty(inspection.Textures.BaseColorPath))
            {
                inspection.Warnings.Add("Base color texture is missing.");
            }

            if (string.IsNullOrEmpty(inspection.Textures.NormalPath))
            {
                inspection.Warnings.Add("Normal texture is missing.");
            }

            if (string.IsNullOrEmpty(inspection.Textures.MetallicPath) ||
                string.IsNullOrEmpty(inspection.Textures.RoughnessPath))
            {
                inspection.Warnings.Add(
                    "Separate metallic/roughness pair is incomplete.");
            }

            return inspection;
        }

        private static TextureSet FindTextureSet(string textureRoot)
        {
            TextureSet set = new TextureSet();
            if (!AssetDatabase.IsValidFolder(textureRoot))
            {
                return set;
            }

            string[] guids = AssetDatabase.FindAssets(
                "t:Texture2D",
                new[] { textureRoot });
            List<string> paths = guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                switch (GetTextureKind(path))
                {
                    case TextureKind.BaseColor:
                        set.BaseColorPath = ChooseFirst(
                            set.BaseColorPath,
                            path);
                        break;
                    case TextureKind.Normal:
                        set.NormalPath = ChooseFirst(set.NormalPath, path);
                        break;
                    case TextureKind.Metallic:
                        set.MetallicPath = ChooseFirst(
                            set.MetallicPath,
                            path);
                        break;
                    case TextureKind.Roughness:
                        set.RoughnessPath = ChooseFirst(
                            set.RoughnessPath,
                            path);
                        break;
                    case TextureKind.Rm:
                        set.RmPath = ChooseFirst(set.RmPath, path);
                        break;
                }
            }

            return set;
        }

        private static string ChooseFirst(string current, string candidate)
        {
            return string.IsNullOrEmpty(current) ? candidate : current;
        }

        private static TextureKind GetTextureKind(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (name.Contains("basecolor") || name.Contains("base_color") ||
                name.Contains("albedo"))
            {
                return TextureKind.BaseColor;
            }

            if (name.EndsWith("_normal", StringComparison.Ordinal) ||
                name.Contains("normalmap"))
            {
                return TextureKind.Normal;
            }

            if (name.EndsWith("_metallic", StringComparison.Ordinal) ||
                name.Contains("metalness"))
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

        private static void ConfigureTextureImporters(
            AssetInspection inspection)
        {
            ConfigureTextureImporter(
                inspection.Textures.BaseColorPath,
                TextureKind.BaseColor,
                inspection.Spec.MaximumTextureSize);
            ConfigureTextureImporter(
                inspection.Textures.NormalPath,
                TextureKind.Normal,
                inspection.Spec.MaximumTextureSize);
            ConfigureTextureImporter(
                inspection.Textures.MetallicPath,
                TextureKind.Metallic,
                inspection.Spec.MaximumTextureSize);
            ConfigureTextureImporter(
                inspection.Textures.RoughnessPath,
                TextureKind.Roughness,
                inspection.Spec.MaximumTextureSize);
            ConfigureTextureImporter(
                inspection.Textures.RmPath,
                TextureKind.Rm,
                inspection.Spec.MaximumTextureSize);
        }

        private static void ConfigureTextureImporter(
            string path,
            TextureKind kind,
            int maximumTextureSize)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            TextureImporter importer =
                AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            TextureImporterType desiredType = kind == TextureKind.Normal
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
                importer.isReadable,
                false,
                value => importer.isReadable = value);
            changed |= SetIfDifferent(
                importer.maxTextureSize,
                maximumTextureSize,
                value => importer.maxTextureSize = value);
            if (changed)
            {
                importer.SaveAndReimport();
            }
        }

        private static void ConfigureModelImporter(
            AssetInspection inspection)
        {
            if (string.IsNullOrEmpty(inspection.ModelPath))
            {
                return;
            }

            ModelImporter importer =
                AssetImporter.GetAtPath(inspection.ModelPath) as ModelImporter;
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(
                inspection.ModelPath);
            if (importer == null || source == null)
            {
                inspection.Warnings.Add("ModelImporter is unavailable.");
                return;
            }

            bool hasPotentialSkeleton =
                inspection.Spec.IsKevin && HasPotentialHumanoidSkeleton(source);
            bool changed = false;
            if (inspection.Spec.IsKevin)
            {
                changed |= SetIfDifferent(
                    importer.bakeAxisConversion,
                    true,
                    value => importer.bakeAxisConversion = value);
                if (hasPotentialSkeleton)
                {
                    changed |= SetIfDifferent(
                        importer.animationType,
                        ModelImporterAnimationType.Human,
                        value => importer.animationType = value);
                    changed |= SetIfDifferent(
                        importer.avatarSetup,
                        ModelImporterAvatarSetup.CreateFromThisModel,
                        value => importer.avatarSetup = value);
                }
                else
                {
                    changed |= SetIfDifferent(
                        importer.animationType,
                        ModelImporterAnimationType.Generic,
                        value => importer.animationType = value);
                }
            }
            else
            {
                changed |= SetIfDifferent(
                    importer.importAnimation,
                    false,
                    value => importer.importAnimation = value);
            }

            if (changed)
            {
                importer.SaveAndReimport();
            }

            if (!hasPotentialSkeleton || !inspection.Spec.IsKevin)
            {
                return;
            }

            Avatar avatar = FindAvatar(inspection.ModelPath);
            if (avatar != null && avatar.isValid && avatar.isHuman)
            {
                return;
            }

            importer = AssetImporter.GetAtPath(
                inspection.ModelPath) as ModelImporter;
            if (importer != null &&
                importer.animationType != ModelImporterAnimationType.Generic)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.SaveAndReimport();
                inspection.Warnings.Add(
                    "Humanoid avatar validation failed; retained Generic rig.");
            }
        }

        private static bool HasPotentialHumanoidSkeleton(GameObject source)
        {
            SkinnedMeshRenderer[] skinned =
                source.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinned.Length; i++)
            {
                if (skinned[i].bones != null && skinned[i].bones.Length >= 15)
                {
                    return true;
                }
            }

            Transform[] transforms = source.GetComponentsInChildren<Transform>(true);
            int semanticBones = 0;
            string[] tokens =
            {
                "hip", "spine", "head", "arm", "leg", "foot",
            };
            for (int i = 0; i < transforms.Length; i++)
            {
                string lower = transforms[i].name.ToLowerInvariant();
                if (tokens.Any(token => lower.Contains(token)))
                {
                    semanticBones++;
                }
            }

            return transforms.Length >= 20 && semanticBones >= 8;
        }

        private static void InspectImportedModel(AssetInspection inspection)
        {
            if (string.IsNullOrEmpty(inspection.ModelPath))
            {
                return;
            }

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(
                inspection.ModelPath);
            ModelImporter importer = AssetImporter.GetAtPath(
                inspection.ModelPath) as ModelImporter;
            inspection.SourceModel = source;
            if (source == null || importer == null)
            {
                inspection.Warnings.Add("FBX failed to import as a model.");
                return;
            }

            inspection.ImporterScale = importer.globalScale;
            inspection.FileScale = importer.fileScale;
            inspection.UseFileScale = importer.useFileScale;
            inspection.AnimationType = importer.animationType.ToString();
            inspection.Avatar = FindAvatar(inspection.ModelPath);
            inspection.IsHumanoid = inspection.Avatar != null &&
                inspection.Avatar.isValid && inspection.Avatar.isHuman;
            inspection.AnimationClips.AddRange(FindAnimationClips(
                inspection.ModelPath));
            inspection.HasAnimationClips =
                inspection.AnimationClips.Count > 0;

            GameObject instance = UnityEngine.Object.Instantiate(source);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Renderer[] renderers =
                    instance.GetComponentsInChildren<Renderer>(true);
                inspection.RendererCount = renderers.Length;
                HashSet<Mesh> meshes = new HashSet<Mesh>();
                long triangleCount = 0;
                for (int i = 0; i < renderers.Length; i++)
                {
                    Mesh mesh = GetRendererMesh(renderers[i]);
                    if (mesh != null && meshes.Add(mesh))
                    {
                        triangleCount += GetTriangleCount(mesh);
                    }
                }

                inspection.MeshCount = meshes.Count;
                inspection.TriangleCount = triangleCount;
                Bounds bounds;
                if (TryGetRendererBounds(instance, out bounds))
                {
                    inspection.HasBounds = true;
                    inspection.SourceBounds = bounds;
                    inspection.HeightMeters = bounds.size.y;
                }
                else
                {
                    inspection.Warnings.Add("Renderer bounds are unavailable.");
                }

                if (inspection.MeshCount == 0)
                {
                    inspection.Warnings.Add("No mesh was found in the FBX.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static List<AnimationClip> FindAnimationClips(string modelPath)
        {
            List<AnimationClip> clips = new List<AnimationClip>();
            UnityEngine.Object[] assets =
                AssetDatabase.LoadAllAssetsAtPath(modelPath);
            for (int i = 0; i < assets.Length; i++)
            {
                AnimationClip clip = assets[i] as AnimationClip;
                if (clip == null || clip.name.StartsWith(
                        "__preview__",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (AnimationUtility.GetCurveBindings(clip).Length == 0 &&
                    AnimationUtility.GetObjectReferenceCurveBindings(clip)
                        .Length == 0)
                {
                    continue;
                }

                clips.Add(clip);
            }

            return clips;
        }

        private static Avatar FindAvatar(string modelPath)
        {
            UnityEngine.Object[] assets =
                AssetDatabase.LoadAllAssetsAtPath(modelPath);
            return assets.OfType<Avatar>().FirstOrDefault();
        }

        private static long GetTriangleCount(Mesh mesh)
        {
            long total = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                total += (long)mesh.GetIndexCount(i) / 3L;
            }

            return total;
        }

        private static List<AssetBuildResult> BuildAssets(
            List<AssetInspection> inspections)
        {
            List<AssetBuildResult> results =
                new List<AssetBuildResult>();
            for (int i = 0; i < inspections.Count; i++)
            {
                AssetInspection inspection = inspections[i];
                ShowProgress(
                    "Building Kevin + FishShop Assets",
                    inspection.Spec.DisplayName,
                    (float)i / inspections.Count);
                results.Add(BuildAsset(inspection));
            }

            return results;
        }

        private static AssetBuildResult BuildAsset(
            AssetInspection inspection)
        {
            AssetBuildResult result = new AssetBuildResult(inspection);
            if (inspection.SourceModel == null)
            {
                result.Notes.Add("Prefab skipped because the FBX is missing.");
                return result;
            }

            Texture2D packed = GeneratePackedMap(inspection, result);
            Material material = BuildMaterial(inspection, packed, result);
            result.Material = material;

            GameObject root = new GameObject(inspection.Spec.PrefabName);
            GameObject modelContainer = new GameObject("Model");
            modelContainer.transform.SetParent(root.transform, false);
            GameObject imported = PrefabUtility.InstantiatePrefab(
                inspection.SourceModel) as GameObject;
            if (imported == null)
            {
                UnityEngine.Object.DestroyImmediate(root);
                result.Notes.Add("FBX prefab instance could not be created.");
                return result;
            }

            imported.name = inspection.SourceModel.name;
            imported.transform.SetParent(modelContainer.transform, false);
            imported.transform.localPosition = Vector3.zero;
            imported.transform.localRotation = Quaternion.identity;
            imported.transform.localScale = Vector3.one;
            RemoveImportedColliders(imported);
            AssignSharedMaterial(imported, material);

            Vector3 correction = FindModelRotationCorrection(
                root,
                modelContainer,
                inspection.Spec.IsKevin);
            if (inspection.Spec.IsKevin)
            {
                correction.y = 180f;
            }
            modelContainer.transform.localRotation = Quaternion.Euler(correction);
            result.ModelRotation = correction;

            Bounds correctedBounds;
            if (!TryGetRendererBounds(root, out correctedBounds))
            {
                UnityEngine.Object.DestroyImmediate(root);
                result.Notes.Add("Wrapper bounds could not be calculated.");
                return result;
            }

            result.ImportedBounds = correctedBounds;
            result.HeightMeters = correctedBounds.size.y;
            float scale = CalculateDefaultScale(
                inspection.Spec,
                correctedBounds);
            result.DefaultScale = scale;
            modelContainer.transform.localScale = Vector3.one * scale;

            Bounds scaledBounds;
            if (TryGetRendererBounds(root, out scaledBounds))
            {
                modelContainer.transform.position += new Vector3(
                    -scaledBounds.center.x,
                    -scaledBounds.min.y,
                    -scaledBounds.center.z);
            }

            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            Bounds finalBounds;
            if (TryGetRendererBounds(root, out finalBounds))
            {
                result.FinalBounds = finalBounds;
            }

            string prefabPath = inspection.Spec.IsKevin
                ? CharacterPrefabRoot + "/" +
                  inspection.Spec.PrefabName + ".prefab"
                : FishShopPrefabRoot + "/" +
                  inspection.Spec.PrefabName + ".prefab";
            result.PrefabPath = prefabPath;
            result.Prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return result;
        }

        private static Texture2D GeneratePackedMap(
            AssetInspection inspection,
            AssetBuildResult result)
        {
            TextureSet textures = inspection.Textures;
            if (string.IsNullOrEmpty(textures.MetallicPath) ||
                string.IsNullOrEmpty(textures.RoughnessPath))
            {
                result.Notes.Add(
                    "Packed map skipped: separate metallic/roughness " +
                    "textures are incomplete.");
                return null;
            }

            string outputRoot = inspection.Spec.IsKevin
                ? KevinPackedRoot
                : FishShopPackedRoot;
            string prefix = inspection.Spec.IsKevin
                ? "MS_Kevin_"
                : "MS_FishShop_";
            string assetPath = outputRoot + "/" + prefix +
                inspection.Spec.AssetId + "_MetallicSmoothness.png";
            TextureImporter metallicImporter = AssetImporter.GetAtPath(
                textures.MetallicPath) as TextureImporter;
            TextureImporter roughnessImporter = AssetImporter.GetAtPath(
                textures.RoughnessPath) as TextureImporter;
            if (metallicImporter == null || roughnessImporter == null)
            {
                result.Notes.Add(
                    "Packed map skipped: source importer is unavailable.");
                return null;
            }

            bool metallicReadable = metallicImporter.isReadable;
            bool roughnessReadable = roughnessImporter.isReadable;
            try
            {
                if (!metallicReadable)
                {
                    metallicImporter.isReadable = true;
                    metallicImporter.SaveAndReimport();
                }

                roughnessImporter = AssetImporter.GetAtPath(
                    textures.RoughnessPath) as TextureImporter;
                if (roughnessImporter != null && !roughnessReadable)
                {
                    roughnessImporter.isReadable = true;
                    roughnessImporter.SaveAndReimport();
                }

                Texture2D metallic = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    textures.MetallicPath);
                Texture2D roughness = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    textures.RoughnessPath);
                if (metallic == null || roughness == null)
                {
                    throw new InvalidOperationException(
                        "Metallic or roughness texture could not be loaded.");
                }

                int width = Mathf.Min(metallic.width, roughness.width);
                int height = Mathf.Min(metallic.height, roughness.height);
                if (width <= 0 || height <= 0)
                {
                    throw new InvalidOperationException(
                        "Packed map dimensions are invalid.");
                }

                Color32[] metallicPixels = ReadPixelsAtSize(
                    metallic,
                    width,
                    height);
                Color32[] roughnessPixels = ReadPixelsAtSize(
                    roughness,
                    width,
                    height);
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
                    AssetPathToAbsolutePath(assetPath),
                    output.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(output);

                AssetDatabase.ImportAsset(
                    assetPath,
                    ImportAssetOptions.ForceSynchronousImport);
                TextureImporter outputImporter = AssetImporter.GetAtPath(
                    assetPath) as TextureImporter;
                if (outputImporter == null)
                {
                    throw new InvalidOperationException(
                        "Generated packed-map importer is unavailable.");
                }

                outputImporter.textureType = TextureImporterType.Default;
                outputImporter.sRGBTexture = false;
                outputImporter.mipmapEnabled = true;
                outputImporter.alphaSource =
                    TextureImporterAlphaSource.FromInput;
                outputImporter.alphaIsTransparency = false;
                outputImporter.isReadable = false;
                outputImporter.maxTextureSize =
                    inspection.Spec.MaximumTextureSize;
                outputImporter.SaveAndReimport();
                result.PackedMapPath = assetPath;
                return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            }
            catch (Exception exception)
            {
                result.Notes.Add(
                    "Packed map failed: " + exception.Message);
                return null;
            }
            finally
            {
                metallicImporter = AssetImporter.GetAtPath(
                    textures.MetallicPath) as TextureImporter;
                roughnessImporter = AssetImporter.GetAtPath(
                    textures.RoughnessPath) as TextureImporter;
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

        private static Material BuildMaterial(
            AssetInspection inspection,
            Texture2D packed,
            AssetBuildResult result)
        {
            string materialRoot = inspection.Spec.IsKevin
                ? KevinMaterialRoot
                : FishShopMaterialRoot;
            string prefix = inspection.Spec.IsKevin
                ? "M_Kevin_"
                : "M_FishShop_";
            string materialPath = materialRoot + "/" + prefix +
                inspection.Spec.AssetId + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                materialPath);
            Shader standard = Shader.Find("Standard");
            if (standard == null)
            {
                throw new InvalidOperationException(
                    "Built-in Standard shader was not found.");
            }

            if (material == null)
            {
                material = new Material(standard);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else
            {
                material.shader = standard;
            }

            Texture2D baseColor = LoadTexture(
                inspection.Textures.BaseColorPath);
            Texture2D normal = LoadTexture(
                inspection.Textures.NormalPath);
            material.SetColor("_Color", Color.white);
            material.SetTexture("_MainTex", baseColor);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            material.SetTexture("_MetallicGlossMap", packed);
            material.SetFloat("_Metallic", packed != null ? 1f : 0.05f);
            material.SetFloat("_Glossiness", packed != null ? 1f : 0.35f);
            material.SetFloat("_GlossMapScale", packed != null ? 1f : 0.35f);
            material.SetFloat("_Mode", 0f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            material.SetInt("_ZWrite", 1);
            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = -1;
            SetKeyword(material, "_NORMALMAP", normal != null);
            SetKeyword(material, "_METALLICGLOSSMAP", packed != null);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
            result.MaterialPath = materialPath;
            if (baseColor == null)
            {
                result.Notes.Add("Material uses no base color texture.");
            }

            if (normal == null)
            {
                result.Notes.Add("Material uses no normal texture.");
            }

            if (packed == null)
            {
                result.Notes.Add(
                    "Material uses scalar metallic/smoothness fallback.");
            }

            return material;
        }

        private static void SetKeyword(
            Material material,
            string keyword,
            bool enabled)
        {
            if (enabled)
            {
                material.EnableKeyword(keyword);
            }
            else
            {
                material.DisableKeyword(keyword);
            }
        }

        private static Texture2D LoadTexture(string path)
        {
            return string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void RemoveImportedColliders(GameObject imported)
        {
            Collider[] colliders =
                imported.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                UnityEngine.Object.DestroyImmediate(colliders[i]);
            }
        }

        private static void AssignSharedMaterial(
            GameObject imported,
            Material material)
        {
            if (material == null)
            {
                return;
            }

            Renderer[] renderers =
                imported.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] slots = renderers[i].sharedMaterials;
                if (slots.Length == 0)
                {
                    slots = new Material[1];
                }

                for (int slot = 0; slot < slots.Length; slot++)
                {
                    slots[slot] = material;
                }

                renderers[i].sharedMaterials = slots;
            }
        }

        private static Vector3 FindModelRotationCorrection(
            GameObject root,
            GameObject modelContainer,
            bool character)
        {
            if (!character)
            {
                return Vector3.zero;
            }

            Vector3[] candidates =
            {
                Vector3.zero,
                new Vector3(90f, 0f, 0f),
                new Vector3(-90f, 0f, 0f),
                new Vector3(0f, 0f, 90f),
                new Vector3(0f, 0f, -90f),
            };
            Vector3 best = Vector3.zero;
            float bestScore = float.MinValue;
            for (int i = 0; i < candidates.Length; i++)
            {
                modelContainer.transform.localRotation =
                    Quaternion.Euler(candidates[i]);
                Bounds bounds;
                if (!TryGetRendererBounds(root, out bounds))
                {
                    continue;
                }

                float horizontal = Mathf.Max(bounds.size.x, bounds.size.z);
                float score = bounds.size.y / Mathf.Max(0.0001f, horizontal);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidates[i];
                }
            }

            modelContainer.transform.localRotation = Quaternion.identity;
            return best;
        }

        private static float CalculateDefaultScale(
            AssetSpec spec,
            Bounds bounds)
        {
            if (spec.IsKevin)
            {
                return 1.75f / Mathf.Max(0.0001f, bounds.size.y);
            }

            float longest = Mathf.Max(
                bounds.size.x,
                Mathf.Max(bounds.size.y, bounds.size.z));
            return spec.TargetLongestDimension /
                Mathf.Max(0.0001f, longest);
        }

        private static KevinVariantCatalog BuildKevinCatalog(
            List<AssetBuildResult> results)
        {
            KevinVariantCatalog catalog =
                AssetDatabase.LoadAssetAtPath<KevinVariantCatalog>(
                    CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<KevinVariantCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.variants.Clear();
            List<AssetBuildResult> kevinResults = results
                .Where(result => result.Inspection.Spec.IsKevin &&
                    result.Prefab != null)
                .ToList();
            for (int i = 0; i < kevinResults.Count; i++)
            {
                AssetBuildResult result = kevinResults[i];
                AssetInspection inspection = result.Inspection;
                AnimationClip idle = inspection.AnimationClips.FirstOrDefault(
                    clip => clip.name.IndexOf(
                        "idle",
                        StringComparison.OrdinalIgnoreCase) >= 0);
                AnimationClip walk = inspection.AnimationClips.FirstOrDefault(
                    clip => clip.name.IndexOf(
                        "walk",
                        StringComparison.OrdinalIgnoreCase) >= 0);
                string notes = inspection.HasAnimationClips
                    ? "Imported clips are exposed without modifying source clips."
                    : "No usable animation clips; visual remains static while " +
                      "the player root moves.";
                catalog.variants.Add(new KevinVariantEntry
                {
                    variantId = inspection.Spec.AssetId,
                    displayName = inspection.Spec.DisplayName,
                    prefab = result.Prefab,
                    avatar = inspection.Avatar,
                    heightMeters = result.HeightMeters,
                    defaultScale = result.DefaultScale,
                    isHumanoid = inspection.IsHumanoid,
                    hasAnimationClips = inspection.HasAnimationClips,
                    idleClip = idle,
                    walkClip = walk,
                    notes = notes,
                });
            }

            catalog.provisionalDefaultVariantId = catalog.Find(
                    "AmbiguousFace") != null
                ? "AmbiguousFace"
                : catalog.variants.Count > 0
                    ? catalog.variants[0].variantId
                    : string.Empty;
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        private static void BuildPlayerPrefab(KevinVariantCatalog catalog)
        {
            GameObject root = new GameObject("Player_Kevin");
            CharacterController controller =
                root.AddComponent<CharacterController>();
            ConfigureCharacterController(controller);
            SimpleTopDownPlayerController movement =
                root.AddComponent<SimpleTopDownPlayerController>();
            movement.cameraRelativeMovement = true;
            movement.faceMoveDirection = false;
            InteractionSensor sensor =
                root.AddComponent<InteractionSensor>();
            sensor.radius = 2.8f;
            sensor.requireLookTarget = true;

            Transform visualRoot = new GameObject("VisualRoot").transform;
            visualRoot.SetParent(root.transform, false);

            GameObject fallback = GameObject.CreatePrimitive(
                PrimitiveType.Capsule);
            fallback.name = "FallbackCapsule";
            fallback.transform.SetParent(visualRoot, false);
            fallback.transform.localPosition = new Vector3(0f, 0.88f, 0f);
            fallback.transform.localScale = new Vector3(0.58f, 0.88f, 0.58f);
            Collider fallbackCollider = fallback.GetComponent<Collider>();
            if (fallbackCollider != null)
            {
                UnityEngine.Object.DestroyImmediate(fallbackCollider);
            }

            Renderer fallbackRenderer = fallback.GetComponent<Renderer>();
            fallbackRenderer.enabled = false;

            Transform firstPersonRig =
                new GameObject("FirstPersonRig").transform;
            firstPersonRig.SetParent(root.transform, false);
            firstPersonRig.localPosition = new Vector3(0f, 1.65f, 0f);
            Transform yawRoot = new GameObject("YawRoot").transform;
            yawRoot.SetParent(firstPersonRig, false);
            Transform pitchRoot = new GameObject("PitchRoot").transform;
            pitchRoot.SetParent(yawRoot, false);
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(pitchRoot, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = false;
            camera.fieldOfView = 75f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 250f;
            cameraObject.AddComponent<AudioListener>();

            Transform interactionOrigin =
                new GameObject("InteractionOrigin").transform;
            interactionOrigin.SetParent(pitchRoot, false);
            Transform groundCheck = new GameObject("GroundCheck").transform;
            groundCheck.SetParent(root.transform, false);
            groundCheck.localPosition = new Vector3(0f, 0.05f, 0f);

            KevinVisualLoader loader = root.AddComponent<KevinVisualLoader>();
            loader.catalog = catalog;
            loader.variantId = catalog != null
                ? catalog.provisionalDefaultVariantId
                : "AmbiguousFace";
            loader.visualRoot = visualRoot;
            loader.cameraTarget = firstPersonRig;
            loader.fallbackRenderer = fallbackRenderer;

            KevinFirstPersonCameraRig firstPerson =
                root.AddComponent<KevinFirstPersonCameraRig>();
            firstPerson.firstPersonRig = firstPersonRig;
            firstPerson.yawRoot = yawRoot;
            firstPerson.pitchRoot = pitchRoot;
            firstPerson.controlledCamera = camera;
            firstPerson.movement = movement;
            firstPerson.interactionSensor = sensor;
            firstPerson.visualLoader = loader;
            firstPerson.eyeHeight = 1.65f;
            firstPerson.fieldOfView = 75f;
            firstPerson.minimumPitch = -75f;
            firstPerson.maximumPitch = 80f;
            firstPerson.nearClipPlane = 0.05f;
            firstPerson.hideKevinRenderers = true;

            movement.cameraTransform = camera.transform;
            sensor.viewCamera = camera;
            sensor.interactionOrigin = interactionOrigin;
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static void ConfigureCharacterController(
            CharacterController controller)
        {
            controller.height = 1.78f;
            controller.radius = 0.32f;
            controller.center = new Vector3(0f, 0.89f, 0f);
            controller.stepOffset = 0.28f;
            controller.skinWidth = 0.05f;
            controller.slopeLimit = 50f;
        }

        private static void BuildKevinGallery(
            List<AssetBuildResult> results,
            KevinVariantCatalog catalog)
        {
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            CreateGalleryEnvironment(
                scene,
                new Vector3(0f, 2.35f, -8.2f),
                new Vector3(0f, 0.88f, 0f),
                new Vector3(10.5f, 0.12f, 4.5f));

            List<AssetBuildResult> valid = results
                .Where(result => result.Inspection.Spec.IsKevin &&
                    result.Prefab != null)
                .ToList();
            float spacing = 2.25f;
            float startX = -(valid.Count - 1) * spacing * 0.5f;
            for (int i = 0; i < valid.Count; i++)
            {
                AssetBuildResult result = valid[i];
                GameObject instance = PrefabUtility.InstantiatePrefab(
                    result.Prefab,
                    scene) as GameObject;
                if (instance == null)
                {
                    continue;
                }

                instance.name = "Gallery_" + result.Inspection.Spec.AssetId;
                instance.transform.position = new Vector3(
                    startX + i * spacing,
                    0.06f,
                    0f);
                instance.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            }

            Canvas canvas = CreateGalleryCanvas(scene, "KevinAssetGallery_HUDCanvas");
            CreateOverlayText(
                canvas.transform,
                "GalleryTitle",
                "KEVIN VARIANT GALLERY",
                new Vector2(0.03f, 0.89f),
                new Vector2(0.97f, 0.975f),
                30,
                TextAnchor.MiddleCenter,
                new Color(0.95f, 0.97f, 0.98f));

            for (int i = 0; i < valid.Count; i++)
            {
                AssetBuildResult result = valid[i];
                AssetInspection inspection = result.Inspection;
                float minX = 0.035f + i * (0.93f / Mathf.Max(1, valid.Count));
                float maxX = 0.035f + (i + 1) *
                    (0.93f / Mathf.Max(1, valid.Count));
                bool isDefault = catalog != null && string.Equals(
                    catalog.provisionalDefaultVariantId,
                    inspection.Spec.AssetId,
                    StringComparison.OrdinalIgnoreCase);
                string label = inspection.Spec.DisplayName + "\n" +
                    "height " + result.NormalizedHeight.ToString(
                        "0.00",
                        CultureInfo.InvariantCulture) + " m | " +
                    (inspection.IsHumanoid ? "Humanoid" : "Generic/Static") +
                    (isDefault ? "\nPROVISIONAL DEFAULT" : string.Empty);
                CreateOverlayLabel(
                    canvas.transform,
                    "Label_" + inspection.Spec.AssetId,
                    label,
                    new Vector2(minX, 0.025f),
                    new Vector2(maxX - 0.012f, 0.17f),
                    isDefault
                        ? new Color(0.16f, 0.45f, 0.43f, 0.94f)
                        : new Color(0.035f, 0.045f, 0.052f, 0.92f));
            }

            EnsureOneEventSystem(scene);
            EditorSceneManager.SaveScene(scene, KevinGalleryPath);
        }

        private static void BuildFishShopGallery(
            List<AssetBuildResult> results)
        {
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            CreateGalleryEnvironment(
                scene,
                new Vector3(0f, 6.1f, -11.8f),
                new Vector3(0f, 0.75f, 1.35f),
                new Vector3(13.5f, 0.12f, 8f));

            List<AssetBuildResult> valid = results
                .Where(result => !result.Inspection.Spec.IsKevin &&
                    result.Prefab != null)
                .ToList();
            Vector3[] positions =
            {
                new Vector3(-3.2f, 0.06f, 0f),
                new Vector3(0f, 0.06f, 0f),
                new Vector3(3.2f, 0.06f, 0f),
                new Vector3(-4.8f, 0.06f, 3.25f),
                new Vector3(-1.6f, 0.06f, 3.25f),
                new Vector3(1.6f, 0.06f, 3.25f),
                new Vector3(4.8f, 0.06f, 3.25f),
            };
            for (int i = 0; i < valid.Count && i < positions.Length; i++)
            {
                GameObject instance = PrefabUtility.InstantiatePrefab(
                    valid[i].Prefab,
                    scene) as GameObject;
                if (instance == null)
                {
                    continue;
                }

                instance.name = "Gallery_" +
                    valid[i].Inspection.Spec.AssetId;
                instance.transform.position = positions[i];
                instance.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            }

            Canvas canvas = CreateGalleryCanvas(
                scene,
                "FishShopAssetGallery_HUDCanvas");
            CreateOverlayText(
                canvas.transform,
                "GalleryTitle",
                "FISH SHOP ASSET GALLERY",
                new Vector2(0.03f, 0.89f),
                new Vector2(0.97f, 0.975f),
                30,
                TextAnchor.MiddleCenter,
                new Color(0.95f, 0.97f, 0.98f));
            for (int i = 0; i < valid.Count; i++)
            {
                bool firstRow = i < 3;
                int rowIndex = firstRow ? i : i - 3;
                int rowCount = firstRow ? 3 : 4;
                float width = 0.92f / rowCount;
                float minX = 0.04f + rowIndex * width;
                float minY = firstRow ? 0.17f : 0.025f;
                string label = valid[i].Inspection.Spec.DisplayName + "\n" +
                    "wrapper " + FormatVector(valid[i].FinalBounds.size);
                CreateOverlayLabel(
                    canvas.transform,
                    "Label_" + valid[i].Inspection.Spec.AssetId,
                    label,
                    new Vector2(minX, minY),
                    new Vector2(minX + width - 0.012f, minY + 0.115f),
                    new Color(0.035f, 0.045f, 0.052f, 0.92f));
            }

            EnsureOneEventSystem(scene);
            EditorSceneManager.SaveScene(scene, FishShopGalleryPath);
        }

        private static void CreateGalleryEnvironment(
            Scene scene,
            Vector3 cameraPosition,
            Vector3 cameraLookAt,
            Vector3 groundScale)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.1f, 1f);
            camera.fieldOfView = 48f;
            cameraObject.transform.position = cameraPosition;
            cameraObject.transform.rotation = Quaternion.LookRotation(
                cameraLookAt - cameraPosition,
                Vector3.up);
            cameraObject.AddComponent<AudioListener>();

            GameObject keyObject = new GameObject("Gallery_KeyLight");
            SceneManager.MoveGameObjectToScene(keyObject, scene);
            keyObject.transform.rotation = Quaternion.Euler(46f, -34f, 0f);
            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.05f;
            key.color = new Color(1f, 0.95f, 0.87f);

            GameObject fillObject = new GameObject("Gallery_FillLight");
            SceneManager.MoveGameObjectToScene(fillObject, scene);
            fillObject.transform.position = new Vector3(-3f, 4f, -2f);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 14f;
            fill.intensity = 1.2f;
            fill.color = new Color(0.58f, 0.8f, 0.86f);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Gallery_NeutralGround";
            SceneManager.MoveGameObjectToScene(ground, scene);
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = groundScale;
            Material material = GetOrCreateGalleryGroundMaterial();
            ground.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static Material GetOrCreateGalleryGroundMaterial()
        {
            string path = FishShopMaterialRoot +
                "/M_NewAssets_GalleryGround.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Standard");
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            material.color = new Color(0.22f, 0.235f, 0.245f, 1f);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Glossiness", 0.16f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Canvas CreateGalleryCanvas(Scene scene, string name)
        {
            GameObject canvasObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        private static void CreateOverlayLabel(
            Transform parent,
            string name,
            string value,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Color background)
        {
            GameObject panel = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image));
            panel.transform.SetParent(parent, false);
            SetNormalizedRect(
                panel.GetComponent<RectTransform>(),
                anchorMin,
                anchorMax);
            panel.GetComponent<Image>().color = background;
            CreateOverlayText(
                panel.transform,
                "Text",
                value,
                new Vector2(0.04f, 0.08f),
                new Vector2(0.96f, 0.92f),
                17,
                TextAnchor.MiddleCenter,
                new Color(0.93f, 0.95f, 0.96f));
        }

        private static Text CreateOverlayText(
            Transform parent,
            string name,
            string value,
            Vector2 anchorMin,
            Vector2 anchorMax,
            int fontSize,
            TextAnchor alignment,
            Color color)
        {
            GameObject textObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Text));
            textObject.transform.SetParent(parent, false);
            Text text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.text = value;
            SetNormalizedRect(text.rectTransform, anchorMin, anchorMax);
            return text;
        }

        private static void SetNormalizedRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void ConfigureBootstrapStart()
        {
            if (!File.Exists(BootstrapScenePath))
            {
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(
                BootstrapScenePath,
                OpenSceneMode.Single);
            DestroyNamedRoot(scene, "KevinCamera_StartUI");
            GameObject root = new GameObject("KevinCamera_StartUI");
            SceneManager.MoveGameObjectToScene(root, scene);
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();

            GameObject background = new GameObject(
                "Background",
                typeof(RectTransform),
                typeof(Image));
            background.transform.SetParent(root.transform, false);
            SetNormalizedRect(
                background.GetComponent<RectTransform>(),
                Vector2.zero,
                Vector2.one);
            background.GetComponent<Image>().color =
                new Color(0.025f, 0.035f, 0.04f, 1f);

            CreateOverlayText(
                root.transform,
                "Title",
                "SASHIMI BOY",
                new Vector2(0.22f, 0.62f),
                new Vector2(0.78f, 0.78f),
                58,
                TextAnchor.MiddleCenter,
                new Color(0.9f, 0.96f, 0.96f));
            GameObject buttonObject = new GameObject(
                "StartButton",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(root.transform, false);
            SetNormalizedRect(
                buttonObject.GetComponent<RectTransform>(),
                new Vector2(0.41f, 0.32f),
                new Vector2(0.59f, 0.41f));
            Image buttonImage = buttonObject.GetComponent<Image>();
            buttonImage.color = new Color(0.12f, 0.5f, 0.52f, 1f);
            Button button = buttonObject.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.12f, 0.5f, 0.52f, 1f);
            colors.highlightedColor = new Color(0.18f, 0.7f, 0.71f, 1f);
            colors.pressedColor = new Color(0.08f, 0.38f, 0.4f, 1f);
            colors.disabledColor = new Color(0.18f, 0.22f, 0.23f, 0.65f);
            button.colors = colors;
            CreateOverlayText(
                buttonObject.transform,
                "Label",
                "START",
                new Vector2(0.08f, 0.08f),
                new Vector2(0.92f, 0.92f),
                26,
                TextAnchor.MiddleCenter,
                Color.white);
            PrototypeStartController controller =
                root.AddComponent<PrototypeStartController>();
            controller.startButton = button;
            controller.streetSceneName = SashimiBoyConstants.Scenes.Street;
            EnsureOneEventSystem(scene);
            EnsureSingleSceneAudioListener(scene, null);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, BootstrapScenePath);
        }

        private static void CreateSceneBackups()
        {
            EnsureFolder(BackupRoot);
            for (int i = 0; i < ExplorationScenes.Length; i++)
            {
                ExplorationSceneSpec spec = ExplorationScenes[i];
                if (!File.Exists(spec.ScenePath) ||
                    File.Exists(spec.BackupPath))
                {
                    continue;
                }

                if (!AssetDatabase.CopyAsset(
                        spec.ScenePath,
                        spec.BackupPath))
                {
                    Debug.LogWarning(
                        "[Sashimi Boy] Could not create one-time backup `" +
                        spec.BackupPath + "`.");
                }
            }
        }

        private static void IntegrateExplorationScenes(
            KevinVariantCatalog catalog)
        {
            for (int i = 0; i < ExplorationScenes.Length; i++)
            {
                ExplorationSceneSpec spec = ExplorationScenes[i];
                if (!File.Exists(spec.ScenePath))
                {
                    Debug.LogWarning(
                        "[Sashimi Boy] Exploration scene missing: " +
                        spec.ScenePath);
                    continue;
                }

                Scene scene = EditorSceneManager.OpenScene(
                    spec.ScenePath,
                    OpenSceneMode.Single);
                SimpleTopDownPlayerController movement =
                    FindSceneComponent<SimpleTopDownPlayerController>(scene);
                if (movement == null)
                {
                    Debug.LogWarning(
                        "[Sashimi Boy] Player movement component missing in " +
                        spec.SceneName + ".");
                    continue;
                }

                GameObject player = movement.gameObject;
                CharacterController characterController =
                    player.GetComponent<CharacterController>();
                if (characterController == null)
                {
                    characterController =
                        player.AddComponent<CharacterController>();
                }

                Renderer capsuleRenderer = player.GetComponent<Renderer>();
                if (capsuleRenderer != null)
                {
                    capsuleRenderer.enabled = false;
                }

                CapsuleCollider legacyCapsule =
                    player.GetComponent<CapsuleCollider>();
                if (legacyCapsule != null)
                {
                    legacyCapsule.enabled = false;
                }

                Transform visualRoot = GetOrCreateChild(
                    player.transform,
                    "VisualRoot");
                Transform firstPersonRig = GetOrCreateChild(
                    player.transform,
                    "FirstPersonRig");
                Transform yawRoot = GetOrCreateChild(
                    firstPersonRig,
                    "YawRoot");
                Transform pitchRoot = GetOrCreateChild(
                    yawRoot,
                    "PitchRoot");
                Transform interactionOrigin = GetOrCreateChild(
                    pitchRoot,
                    "InteractionOrigin");
                Transform groundCheck = GetOrCreateChild(
                    player.transform,
                    "GroundCheck");
                float parentScaleY = Mathf.Max(
                    0.0001f,
                    Mathf.Abs(player.transform.lossyScale.y));
                Vector3 parentScale = player.transform.lossyScale;
                float colliderBottom = characterController.center.y -
                    characterController.height * 0.5f;
                visualRoot.localPosition = new Vector3(
                    0f,
                    colliderBottom,
                    0f);
                visualRoot.localRotation = Quaternion.identity;
                visualRoot.localScale = new Vector3(
                    1f / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
                    1f / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
                    1f / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
                firstPersonRig.localPosition = new Vector3(
                    0f,
                    colliderBottom + 1.65f / parentScaleY,
                    0f);
                firstPersonRig.localRotation = Quaternion.identity;
                firstPersonRig.localScale = Vector3.one;
                yawRoot.localPosition = Vector3.zero;
                yawRoot.localRotation = Quaternion.identity;
                yawRoot.localScale = Vector3.one;
                pitchRoot.localPosition = Vector3.zero;
                pitchRoot.localRotation = Quaternion.identity;
                pitchRoot.localScale = Vector3.one;
                interactionOrigin.localPosition = Vector3.zero;
                interactionOrigin.localRotation = Quaternion.identity;
                interactionOrigin.localScale = Vector3.one;
                groundCheck.localPosition = new Vector3(
                    0f,
                    colliderBottom + 0.05f / parentScaleY,
                    0f);
                groundCheck.localRotation = Quaternion.identity;
                groundCheck.localScale = Vector3.one;

                DestroyChildren(visualRoot);
                GameObject defaultPrefab = catalog != null &&
                    catalog.ProvisionalDefault != null
                    ? catalog.ProvisionalDefault.prefab
                    : null;
                GameObject preview = null;
                if (defaultPrefab != null)
                {
                    preview = PrefabUtility.InstantiatePrefab(
                        defaultPrefab,
                        scene) as GameObject;
                    if (preview != null)
                    {
                        preview.name = "KevinVisual_Preview_AmbiguousFace";
                        preview.transform.SetParent(visualRoot, false);
                    }
                }

                KevinVisualLoader loader =
                    player.GetComponent<KevinVisualLoader>();
                if (loader == null)
                {
                    loader = player.AddComponent<KevinVisualLoader>();
                }

                loader.catalog = catalog;
                loader.variantId = catalog != null
                    ? catalog.provisionalDefaultVariantId
                    : "AmbiguousFace";
                loader.visualRoot = visualRoot;
                loader.cameraTarget = firstPersonRig;
                loader.fallbackRenderer = capsuleRenderer;
                loader.editorPreviewVisual = preview;
                loader.loadOnAwake = true;

                movement.cameraRelativeMovement = true;
                movement.faceMoveDirection = false;

                Camera camera = FindMainCamera(scene);
                if (camera == null)
                {
                    GameObject cameraObject = new GameObject("Main Camera");
                    SceneManager.MoveGameObjectToScene(cameraObject, scene);
                    cameraObject.tag = "MainCamera";
                    camera = cameraObject.AddComponent<Camera>();
                }

                camera.gameObject.name = "Main Camera";
                camera.gameObject.tag = "MainCamera";
                camera.transform.SetParent(pitchRoot, false);
                camera.transform.localPosition = Vector3.zero;
                camera.transform.localRotation = Quaternion.identity;
                camera.transform.localScale = Vector3.one;
                camera.orthographic = false;
                camera.fieldOfView = 75f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, 250f);

                InteractionSensor sensor =
                    player.GetComponent<InteractionSensor>();
                if (sensor == null)
                {
                    sensor = player.AddComponent<InteractionSensor>();
                }

                sensor.radius = 2.8f;
                sensor.requireLookTarget = true;
                sensor.viewCamera = camera;
                sensor.interactionOrigin = interactionOrigin;

                KevinFirstPersonCameraRig firstPerson =
                    player.GetComponent<KevinFirstPersonCameraRig>();
                if (firstPerson == null)
                {
                    firstPerson =
                        player.AddComponent<KevinFirstPersonCameraRig>();
                }

                firstPerson.firstPersonRig = firstPersonRig;
                firstPerson.yawRoot = yawRoot;
                firstPerson.pitchRoot = pitchRoot;
                firstPerson.controlledCamera = camera;
                firstPerson.movement = movement;
                firstPerson.interactionSensor = sensor;
                firstPerson.visualLoader = loader;
                firstPerson.eyeHeight = 1.65f;
                firstPerson.modelEyeHeightRatio = 0.9f;
                firstPerson.deriveEyeHeightFromVisual = true;
                firstPerson.fieldOfView = 75f;
                firstPerson.nearClipPlane = 0.05f;
                firstPerson.farClipPlane = 250f;
                firstPerson.minimumPitch = -75f;
                firstPerson.maximumPitch = 80f;
                firstPerson.horizontalSensitivity = 2.2f;
                firstPerson.verticalSensitivity = 2f;
                firstPerson.lookSmoothing = 0.015f;
                firstPerson.lockCursorOnStart = true;
                firstPerson.startWithUiOpen = spec.StartWithUiOpen;
                firstPerson.hideKevinRenderers = true;
                firstPerson.reticleRoot = CreateFirstPersonHud(scene);

                Transform spawnPoint = CreateOrUpdateSpawnPoint(
                    scene,
                    player.transform.position,
                    spec.InitialYaw);
                player.transform.SetPositionAndRotation(
                    spawnPoint.position,
                    spawnPoint.rotation);
                movement.cameraTransform = camera.transform;

                DisableWorldSpaceDebugLabels(scene);
                EnsureSingleSceneCamera(scene, camera);
                EnsureSingleSceneAudioListener(scene, camera);
                EnsureOneEventSystem(scene);
                EditorUtility.SetDirty(movement);
                EditorUtility.SetDirty(sensor);
                EditorUtility.SetDirty(loader);
                EditorUtility.SetDirty(firstPerson);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, spec.ScenePath);
            }

            ValidateStageCameraPolicy();
        }

        private static void ValidateStageCameraPolicy()
        {
            if (!File.Exists(StageScenePath))
            {
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(
                StageScenePath,
                OpenSceneMode.Single);
            Camera camera = FindMainCamera(scene);
            if (camera == null || !camera.orthographic)
            {
                Debug.LogWarning(
                    "[Sashimi Boy] Stage01 fixed orthographic camera must be " +
                    "reviewed. TASK 1 did not modify it.");
            }

            if (FindSceneComponent<KevinFirstPersonCameraRig>(scene) != null)
            {
                Debug.LogError(
                    "[Sashimi Boy] Stage01 unexpectedly contains the " +
                    "exploration first-person rig.");
            }
        }

        private static GameObject CreateFirstPersonHud(Scene scene)
        {
            const string canvasName = "KevinFirstPerson_HUDCanvas";
            DestroyNamedRoot(scene, canvasName);
            GameObject canvasObject = new GameObject(canvasName);
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 85;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GameObject reticle = new GameObject(
                "Reticle",
                typeof(RectTransform));
            reticle.transform.SetParent(canvasObject.transform, false);
            RectTransform reticleRect =
                reticle.GetComponent<RectTransform>();
            reticleRect.anchorMin = new Vector2(0.5f, 0.5f);
            reticleRect.anchorMax = new Vector2(0.5f, 0.5f);
            reticleRect.anchoredPosition = Vector2.zero;
            reticleRect.sizeDelta = new Vector2(20f, 20f);
            CreateReticleLine(
                reticle.transform,
                "Horizontal",
                new Vector2(16f, 2f));
            CreateReticleLine(
                reticle.transform,
                "Vertical",
                new Vector2(2f, 16f));
            return reticle;
        }

        private static void CreateReticleLine(
            Transform parent,
            string name,
            Vector2 size)
        {
            GameObject lineObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image));
            lineObject.transform.SetParent(parent, false);
            RectTransform rect = lineObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            Image image = lineObject.GetComponent<Image>();
            image.color = new Color(0.95f, 0.98f, 0.98f, 0.82f);
            image.raycastTarget = false;
        }

        private static Transform CreateOrUpdateSpawnPoint(
            Scene scene,
            Vector3 position,
            float initialYaw)
        {
            GameObject spawn = scene.GetRootGameObjects().FirstOrDefault(
                item => item.name == "PlayerSpawnPoint");
            if (spawn == null)
            {
                spawn = new GameObject("PlayerSpawnPoint");
                SceneManager.MoveGameObjectToScene(spawn, scene);
            }

            spawn.transform.SetPositionAndRotation(
                position,
                Quaternion.Euler(0f, initialYaw, 0f));
            return spawn.transform;
        }

        private static void DisableWorldSpaceDebugLabels(Scene scene)
        {
            BillboardLabel[] labels = GetSceneComponents<BillboardLabel>(scene);
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i] != null)
                {
                    labels[i].gameObject.SetActive(false);
                }
            }
        }

        private static void EnsureSingleSceneCamera(
            Scene scene,
            Camera keeper)
        {
            Camera[] cameras = GetSceneComponents<Camera>(scene);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != keeper)
                {
                    cameras[i].gameObject.SetActive(false);
                }
            }
        }

        private static void EnsureSingleSceneAudioListener(
            Scene scene,
            Camera preferredCamera)
        {
            Camera camera = preferredCamera ?? FindMainCamera(scene);
            AudioListener keeper = camera != null
                ? camera.GetComponent<AudioListener>()
                : null;
            if (camera != null && keeper == null)
            {
                keeper = camera.gameObject.AddComponent<AudioListener>();
            }

            AudioListener[] listeners =
                GetSceneComponents<AudioListener>(scene);
            if (keeper == null && listeners.Length > 0)
            {
                keeper = listeners[0];
            }

            for (int i = 0; i < listeners.Length; i++)
            {
                listeners[i].enabled = listeners[i] == keeper;
            }

            if (keeper != null)
            {
                keeper.enabled = true;
            }
        }

        private static void EnsureOneEventSystem(Scene scene)
        {
            EventSystem[] systems = GetSceneComponents<EventSystem>(scene);
            EventSystem keeper = systems.FirstOrDefault();
            if (keeper == null)
            {
                GameObject eventObject = new GameObject(
                    "EventSystem",
                    typeof(EventSystem),
                    typeof(StandaloneInputModule));
                SceneManager.MoveGameObjectToScene(eventObject, scene);
                keeper = eventObject.GetComponent<EventSystem>();
                systems = GetSceneComponents<EventSystem>(scene);
            }

            keeper.gameObject.SetActive(true);
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] != keeper)
                {
                    systems[i].gameObject.SetActive(false);
                }
            }
        }

        private static List<SceneAudit> AuditScenes()
        {
            List<SceneAudit> audits = new List<SceneAudit>();
            for (int i = 0; i < ExplorationScenes.Length; i++)
            {
                audits.Add(AuditScene(
                    ExplorationScenes[i].ScenePath,
                    true));
            }

            audits.Add(AuditScene(StageScenePath, false));
            audits.Add(AuditScene(BootstrapScenePath, false));
            return audits;
        }

        private static SceneAudit AuditScene(
            string scenePath,
            bool expectsFirstPerson)
        {
            SceneAudit audit = new SceneAudit(
                scenePath,
                expectsFirstPerson);
            if (!File.Exists(scenePath))
            {
                audit.Warning = "Scene file is missing.";
                return audit;
            }

            Scene scene = EditorSceneManager.OpenScene(
                scenePath,
                OpenSceneMode.Single);
            audit.CameraCount = CountActiveSceneComponents<Camera>(scene);
            audit.ListenerCount =
                CountActiveSceneComponents<AudioListener>(scene);
            audit.EventSystemCount =
                CountActiveSceneComponents<EventSystem>(scene);
            audit.FirstPersonRigCount =
                CountActiveSceneComponents<KevinFirstPersonCameraRig>(scene);
            Camera camera = FindMainCamera(scene);
            audit.Orthographic = camera != null && camera.orthographic;
            GameObject spawn = scene.GetRootGameObjects().FirstOrDefault(
                item => item.name == "PlayerSpawnPoint");
            if (spawn != null)
            {
                audit.SpawnPosition = spawn.transform.position;
                audit.SpawnYaw = spawn.transform.eulerAngles.y;
                audit.HasSpawnPoint = true;
            }
            return audit;
        }

        private static void WriteReport(
            List<AssetInspection> inspections,
            List<AssetBuildResult> results,
            KevinVariantCatalog catalog,
            List<SceneAudit> audits,
            List<PipelineLog> logs)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine(
                "# New Assets And Kevin First-Person Camera Report");
            report.AppendLine();
            report.AppendLine("- Render pipeline: Built-in Render Pipeline");
            report.AppendLine("- Shader: `Standard`");
            report.AppendLine("- Input backend: legacy Unity Input API");
            report.AppendLine("- Source files overwritten: No");
            report.AppendLine("- TASK 2 scene dressing included: No");
            report.AppendLine();
            report.AppendLine("## Discovered FBX");
            report.AppendLine();
            for (int i = 0; i < inspections.Count; i++)
            {
                AssetInspection inspection = inspections[i];
                report.AppendLine("- " + inspection.Spec.AssetId + ": " +
                    (string.IsNullOrEmpty(inspection.ModelPath)
                        ? "MISSING"
                        : "`" + inspection.ModelPath + "`"));
            }

            report.AppendLine();
            report.AppendLine("## Texture Validation");
            report.AppendLine();
            report.AppendLine(
                "Base color is sRGB; normal maps use Normal Map import; " +
                "metallic/roughness data is linear; mipmaps are enabled. " +
                "Source Read/Write is restored after packing.");
            for (int i = 0; i < inspections.Count; i++)
            {
                AssetInspection inspection = inspections[i];
                report.AppendLine("- " + inspection.Spec.AssetId +
                    ": base=`" + ValueOrMissing(
                        inspection.Textures.BaseColorPath) + "`, normal=`" +
                    ValueOrMissing(inspection.Textures.NormalPath) +
                    "`, metallic=`" + ValueOrMissing(
                        inspection.Textures.MetallicPath) +
                    "`, roughness=`" + ValueOrMissing(
                        inspection.Textures.RoughnessPath) + "`");
            }

            report.AppendLine();
            report.AppendLine("## Kevin Rig And Scale");
            report.AppendLine();
            report.AppendLine(
                "| Variant | Imported height | Default scale | " +
                "Normalized | Rig | Avatar | Animation clips |");
            report.AppendLine("|---|---:|---:|---:|---|---|---|");
            for (int i = 0; i < inspections.Count; i++)
            {
                AssetInspection inspection = inspections[i];
                if (!inspection.Spec.IsKevin)
                {
                    continue;
                }

                AssetBuildResult result = results != null
                    ? results.FirstOrDefault(item =>
                        item.Inspection == inspection)
                    : null;
                report.AppendLine("| " + inspection.Spec.DisplayName +
                    " | " + inspection.HeightMeters.ToString(
                        "0.#####",
                        CultureInfo.InvariantCulture) +
                    " | " + (result != null
                        ? result.DefaultScale.ToString(
                            "0.#####",
                            CultureInfo.InvariantCulture)
                        : "not built") +
                    " | " + (result != null
                        ? result.NormalizedHeight.ToString(
                            "0.###",
                            CultureInfo.InvariantCulture)
                        : "not built") +
                    " | " + inspection.AnimationType +
                    " | " + (inspection.IsHumanoid
                        ? "valid Humanoid"
                        : "not Humanoid") +
                    " | " + (inspection.HasAnimationClips
                        ? string.Join(", ", inspection.AnimationClips
                            .Select(clip => clip.name))
                        : "none") + " |");
            }

            report.AppendLine();
            report.AppendLine("## Generated Outputs");
            report.AppendLine();
            if (results == null)
            {
                report.AppendLine("- Validation-only run; outputs were not rebuilt.");
            }
            else
            {
                for (int i = 0; i < results.Count; i++)
                {
                    AssetBuildResult result = results[i];
                    report.AppendLine("- Material: `" +
                        ValueOrMissing(result.MaterialPath) + "`");
                    report.AppendLine("- Packed map: `" +
                        ValueOrMissing(result.PackedMapPath) + "`");
                    report.AppendLine("- Prefab: `" +
                        ValueOrMissing(result.PrefabPath) + "`");
                }

                report.AppendLine("- Catalog: `" + CatalogPath + "`");
                report.AppendLine("- Player reference prefab: `" +
                    PlayerPrefabPath + "`");
                report.AppendLine("- Kevin gallery: `" +
                    KevinGalleryPath + "`");
                report.AppendLine("- FishShop gallery: `" +
                    FishShopGalleryPath + "`");
            }

            report.AppendLine();
            report.AppendLine("## Missing And Warnings");
            report.AppendLine();
            bool anyWarnings = false;
            for (int i = 0; i < inspections.Count; i++)
            {
                foreach (string warning in inspections[i].Warnings.Distinct())
                {
                    anyWarnings = true;
                    report.AppendLine("- " + inspections[i].Spec.AssetId +
                        ": " + warning);
                }
            }

            if (results != null)
            {
                for (int i = 0; i < results.Count; i++)
                {
                    foreach (string note in results[i].Notes.Distinct())
                    {
                        anyWarnings = true;
                        report.AppendLine("- " +
                            results[i].Inspection.Spec.AssetId + ": " + note);
                    }
                }
            }

            if (!anyWarnings)
            {
                report.AppendLine("- None.");
            }

            report.AppendLine();
            report.AppendLine("## Provisional Kevin");
            report.AppendLine();
            report.AppendLine("- Default variant: `" +
                (catalog != null
                    ? catalog.provisionalDefaultVariantId
                    : "catalog not built") + "`");
            report.AppendLine(
                "- Selection lives in `KevinVariantCatalog.asset`; changing " +
                "the default does not require rebuilding scenes.");
            report.AppendLine();
            report.AppendLine("## Camera And Start Flow");
            report.AppendLine();
            report.AppendLine("- Script: `KevinFirstPersonCameraRig`");
            report.AppendLine(
                "- Eye height: 90% of valid Kevin renderer bounds; " +
                "fallback 1.65 m. FOV 75, near clip 0.05 m, pitch " +
                "-75 to +80 degrees.");
            report.AppendLine(
                "- Kevin renderers use `Shadows Only` during first-person " +
                "exploration, so the head/hair cannot cover the camera " +
                "while the model remains available for shadows and previews.");
            report.AppendLine(
                "- `InteractionSensor` keeps the 2.8 m range check and " +
                "selects only the nearest unobstructed `IInteractable` hit " +
                "by the camera-center raycast.");
            report.AppendLine(
                "- Bootstrap `START` calls `PrototypeStartController` and " +
                "loads `Street` through `SceneTransitionService`.");
            report.AppendLine(
                "- Street/FishShop start with a locked cursor. Dialogue, " +
                "manual ESC release, EquipmentShop UI, and Club UI suspend " +
                "movement/look and expose the cursor.");
            report.AppendLine(
                "- Street, FishShopDialogue, EquipmentShop, Club use " +
                "scene-local first-person rigs. Stage01_Salmon stays fixed.");
            SceneAudit streetAudit = audits.FirstOrDefault(audit =>
                audit.ScenePath == StreetScenePath);
            if (streetAudit != null && streetAudit.HasSpawnPoint)
            {
                report.AppendLine(
                    "- Street PlayerSpawnPoint: position `" +
                    FormatVector(streetAudit.SpawnPosition) + "`, yaw `" +
                    streetAudit.SpawnYaw.ToString(
                        "0.##",
                        CultureInfo.InvariantCulture) + "` degrees.");
            }
            report.AppendLine();
            report.AppendLine("## Scene Audit");
            report.AppendLine();
            report.AppendLine(
                "| Scene | Cameras | Listeners | EventSystems | FirstPerson | " +
                "Orthographic | Spawn / Yaw |");
            report.AppendLine("|---|---:|---:|---:|---:|---|---|");
            for (int i = 0; i < audits.Count; i++)
            {
                SceneAudit audit = audits[i];
                report.AppendLine("| " + Path.GetFileNameWithoutExtension(
                        audit.ScenePath) +
                    " | " + audit.CameraCount +
                    " | " + audit.ListenerCount +
                    " | " + audit.EventSystemCount +
                    " | " + audit.FirstPersonRigCount +
                    " | " + audit.Orthographic +
                    " | " + (audit.HasSpawnPoint
                        ? FormatVector(audit.SpawnPosition) + " / " +
                          audit.SpawnYaw.ToString(
                              "0.##",
                              CultureInfo.InvariantCulture)
                        : "n/a") + " |");
            }

            report.AppendLine();
            report.AppendLine("## Captured Console Messages");
            report.AppendLine();
            List<PipelineLog> relevantLogs = logs
                .Where(log => !log.Message.StartsWith(
                    "[Sashimi Boy] New assets",
                    StringComparison.Ordinal))
                .ToList();
            if (relevantLogs.Count == 0)
            {
                report.AppendLine("- No warnings or errors captured by this run.");
            }
            else
            {
                for (int i = 0; i < relevantLogs.Count; i++)
                {
                    report.AppendLine("- " + relevantLogs[i].Type + ": " +
                        relevantLogs[i].Message.Replace("\n", " "));
                }
            }

            report.AppendLine();
            report.AppendLine("## Play Mode Checklist");
            report.AppendLine();
            report.AppendLine("1. Open `KevinAssetGallery` and inspect all four variants, labels, height, and default marker.");
            report.AppendLine("2. Open `FishShopAssetGallery` and inspect all seven wrapper prefabs and materials.");
            report.AppendLine("3. Play `Bootstrap`, press START, and confirm Street opens at Kevin eye height with FOV 75 and no head/hair visible.");
            report.AppendLine("4. Verify WASD follows camera yaw, mouse look clamps vertically, collision blocks buildings, and the center reticle remains stable.");
            report.AppendLine("5. Aim at each Street door inside 2.8 m; verify `E` prompt appears only with clear line of sight and scene transitions still work.");
            report.AppendLine("6. Press ESC to release/relock the cursor. In FishShop dialogue, verify movement/look stop while the Screen Space dialogue UI is active.");
            report.AppendLine("7. Verify EquipmentShop/Club buttons work with the visible cursor and their first-person cameras do not rotate behind UI.");
            report.AppendLine("8. Open Stage01_Salmon and confirm its fixed orthographic slicing camera, rhythm input, HUD, scoring, and audio are unchanged.");

            File.WriteAllText(
                AssetPathToAbsolutePath(ReportPath),
                report.ToString(),
                new UTF8Encoding(false));
            AssetDatabase.ImportAsset(
                ReportPath,
                ImportAssetOptions.ForceSynchronousImport);
        }

        private static string ValueOrMissing(string value)
        {
            return string.IsNullOrEmpty(value) ? "missing" : value;
        }

        private static Camera FindMainCamera(Scene scene)
        {
            Camera[] cameras = GetSceneComponents<Camera>(scene);
            return cameras.FirstOrDefault(camera => camera.CompareTag(
                       "MainCamera")) ?? cameras.FirstOrDefault();
        }

        private static T FindSceneComponent<T>(Scene scene)
            where T : Component
        {
            return GetSceneComponents<T>(scene).FirstOrDefault();
        }

        private static T[] GetSceneComponents<T>(Scene scene)
            where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .ToArray();
        }

        private static int CountActiveSceneComponents<T>(Scene scene)
            where T : Behaviour
        {
            return GetSceneComponents<T>(scene).Count(component =>
                component != null && component.enabled &&
                component.gameObject.activeInHierarchy);
        }

        private static Transform GetOrCreateChild(
            Transform parent,
            string name)
        {
            Transform child = parent.Find(name);
            if (child != null)
            {
                return child;
            }

            child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static void DestroyChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(
                    parent.GetChild(i).gameObject);
            }
        }

        private static void DestroyNamedRoot(Scene scene, string name)
        {
            GameObject root = scene.GetRootGameObjects().FirstOrDefault(
                item => item.name == name);
            if (root != null)
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void OpenGeneratedScene(string path)
        {
            if (!File.Exists(path))
            {
                EditorUtility.DisplayDialog(
                    "Sashimi Boy",
                    "Build the new character and FishShop prefabs first.",
                    "OK");
                return;
            }

            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }

        private static bool TryGetRendererBounds(
            GameObject root,
            out Bounds bounds)
        {
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
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

            return found && IsFinite(bounds.center) && IsFinite(bounds.size) &&
                bounds.size.sqrMagnitude > 0.00000001f;
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) &&
                float.IsFinite(value.z);
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

        private static string FormatVector(Vector3 value)
        {
            return "(" + value.x.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture) + ", " +
                value.y.ToString("0.###", CultureInfo.InvariantCulture) +
                ", " + value.z.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture) + ")";
        }

        private static string AssetPathToAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(
                Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath);
        }

        private static string AbsolutePathToAssetPath(string absolutePath)
        {
            string normalized = absolutePath.Replace('\\', '/');
            string dataPath = Application.dataPath.Replace('\\', '/');
            if (!normalized.StartsWith(
                    dataPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            return "Assets" + normalized.Substring(dataPath.Length);
        }

        private static void EnsureGeneratedFolders()
        {
            EnsureFolder(KevinMaterialRoot);
            EnsureFolder(FishShopMaterialRoot);
            EnsureFolder(KevinPackedRoot);
            EnsureFolder(FishShopPackedRoot);
            EnsureFolder(CharacterPrefabRoot);
            EnsureFolder(FishShopPrefabRoot);
            EnsureFolder(DataRoot);
            EnsureFolder(GeneratedScenesRoot);
            EnsureFolder(ReportsRoot);
            EnsureFolder(BackupRoot);
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

        private enum TextureKind
        {
            Unknown,
            BaseColor,
            Normal,
            Metallic,
            Roughness,
            Rm,
        }

        private sealed class TextureSet
        {
            public string BaseColorPath;
            public string NormalPath;
            public string MetallicPath;
            public string RoughnessPath;
            public string RmPath;
        }

        private sealed class AssetSpec
        {
            private AssetSpec(
                string assetId,
                string displayName,
                string sourceFolder,
                string prefabName,
                int maximumTextureSize,
                float targetLongestDimension,
                bool isKevin)
            {
                AssetId = assetId;
                DisplayName = displayName;
                SourceFolder = sourceFolder;
                PrefabName = prefabName;
                MaximumTextureSize = maximumTextureSize;
                TargetLongestDimension = targetLongestDimension;
                IsKevin = isKevin;
            }

            public string AssetId { get; }
            public string DisplayName { get; }
            public string SourceFolder { get; }
            public string PrefabName { get; }
            public int MaximumTextureSize { get; }
            public float TargetLongestDimension { get; }
            public bool IsKevin { get; }

            public static AssetSpec Kevin(
                string assetId,
                string displayName)
            {
                return new AssetSpec(
                    assetId,
                    displayName,
                    SourceRoot +
                        "/Characters/Kevin/Variants/" + assetId,
                    "PF_Character_Kevin_" + assetId,
                    2048,
                    1.75f,
                    true);
            }

            public static AssetSpec Fish(
                string assetId,
                string displayName,
                float targetLength)
            {
                return FishShop(
                    assetId,
                    displayName,
                    "Fish/" + assetId,
                    "PF_Fish_" + assetId,
                    2048,
                    targetLength);
            }

            public static AssetSpec FishShop(
                string assetId,
                string displayName,
                string relativeFolder,
                string prefabName,
                int maximumTextureSize,
                float targetLongestDimension)
            {
                return new AssetSpec(
                    assetId,
                    displayName,
                    SourceRoot +
                        "/Environment/FishShop/" + relativeFolder,
                    prefabName,
                    maximumTextureSize,
                    targetLongestDimension,
                    false);
            }
        }

        private sealed class AssetInspection
        {
            public AssetInspection(AssetSpec spec)
            {
                Spec = spec;
            }

            public AssetSpec Spec { get; }
            public string ModelPath;
            public TextureSet Textures = new TextureSet();
            public GameObject SourceModel;
            public float ImporterScale;
            public float FileScale;
            public bool UseFileScale;
            public string AnimationType = "Unknown";
            public Avatar Avatar;
            public bool IsHumanoid;
            public bool HasAnimationClips;
            public readonly List<AnimationClip> AnimationClips =
                new List<AnimationClip>();
            public int RendererCount;
            public int MeshCount;
            public long TriangleCount;
            public bool HasBounds;
            public Bounds SourceBounds;
            public float HeightMeters;
            public readonly List<string> Warnings = new List<string>();
        }

        private sealed class AssetBuildResult
        {
            public AssetBuildResult(AssetInspection inspection)
            {
                Inspection = inspection;
            }

            public AssetInspection Inspection { get; }
            public Material Material;
            public string MaterialPath;
            public string PackedMapPath;
            public GameObject Prefab;
            public string PrefabPath;
            public Vector3 ModelRotation;
            public Bounds ImportedBounds;
            public Bounds FinalBounds;
            public float HeightMeters;
            public float DefaultScale;
            public readonly List<string> Notes = new List<string>();
            public float NormalizedHeight => HeightMeters * DefaultScale;
        }

        private sealed class ExplorationSceneSpec
        {
            public ExplorationSceneSpec(
                string sceneName,
                string scenePath,
                string backupPath,
                bool startWithUiOpen,
                float initialYaw)
            {
                SceneName = sceneName;
                ScenePath = scenePath;
                BackupPath = backupPath;
                StartWithUiOpen = startWithUiOpen;
                InitialYaw = initialYaw;
            }

            public string SceneName { get; }
            public string ScenePath { get; }
            public string BackupPath { get; }
            public bool StartWithUiOpen { get; }
            public float InitialYaw { get; }
        }

        private sealed class SceneAudit
        {
            public SceneAudit(
                string scenePath,
                bool expectsFirstPerson)
            {
                ScenePath = scenePath;
                ExpectsFirstPerson = expectsFirstPerson;
            }

            public string ScenePath { get; }
            public bool ExpectsFirstPerson { get; }
            public int CameraCount;
            public int ListenerCount;
            public int EventSystemCount;
            public int FirstPersonRigCount;
            public bool Orthographic;
            public bool HasSpawnPoint;
            public Vector3 SpawnPosition;
            public float SpawnYaw;
            public string Warning;
        }

        private sealed class PipelineLog
        {
            public PipelineLog(LogType type, string message)
            {
                Type = type;
                Message = message;
            }

            public LogType Type { get; }
            public string Message { get; }
        }
    }
}
#endif
