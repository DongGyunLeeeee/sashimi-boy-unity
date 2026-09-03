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
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace SashimiBoy.EditorTools
{
    public static class EquipmentShopArtPassPipeline
    {
        private const string GeneratedRoot =
            "Assets/_SashimiBoy/Art/Generated";
        private const string MaterialsRoot =
            GeneratedRoot + "/Materials/EquipmentShop";
        private const string PackedMapsRoot =
            GeneratedRoot + "/PackedMaps/EquipmentShop";
        private const string PrefabsRoot =
            GeneratedRoot + "/Prefabs/EquipmentShop";
        private const string ScenesRoot = GeneratedRoot + "/Scenes";
        private const string PreviewsRoot =
            GeneratedRoot + "/Previews/EquipmentShop";
        private const string ReportsRoot = GeneratedRoot + "/Reports";
        private const string MainScenePath =
            "Assets/_SashimiBoy/Scenes/EquipmentShop.unity";
        private const string GalleryScenePath =
            ScenesRoot + "/EquipmentShopAssetGallery.unity";
        private const string GalleryPreviewPath =
            PreviewsRoot + "/EquipmentShopAssetGallery.png";
        private const string MainPreviewPath =
            PreviewsRoot + "/EquipmentShopArtPass.png";
        private const string ReportPath =
            ReportsRoot + "/EquipmentShopArtPassReport.md";
        private const string ArtRootName = "EquipmentShopArtRoot";

        private static readonly Regex PartRegex = new Regex(
            @"tripo_part_(\d+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly AssetSpec[] AssetSpecs =
        {
            AssetSpec.Character("EquipmentShopOwner", 1.85f, 2048, 180f),
            AssetSpec.Furniture("WoodenSofa", 2.4f, 2048),
            AssetSpec.Equipment("EffectsPedals", 0.55f, 1024),
            AssetSpec.Equipment("ElectronicDrumKit", 2.15f, 2048),
            AssetSpec.Equipment("GuitarPedal", 0.32f, 1024),
            AssetSpec.Equipment("Loudspeaker", 0.9f, 2048),
            AssetSpec.Equipment("MidiKeyboardController", 1.35f, 2048),
            AssetSpec.Equipment("ModularSynthesizer", 1.4f, 2048),
            AssetSpec.Equipment("SpeakerBox", 0.8f, 2048),
            AssetSpec.Equipment("StackedSpeaker", 1.75f, 2048),
            AssetSpec.Equipment("StageSpotlight", 0.6f, 1024),
            AssetSpec.Equipment("StereoSpeaker", 1.05f, 2048),
            AssetSpec.Equipment("VintageSpeaker", 0.95f, 2048),
        };

        [MenuItem("Sashimi Boy/Art/Build EquipmentShop Art Pass")]
        public static void BuildEquipmentShopArtPass()
        {
            BuildAll();
        }

        [MenuItem("Sashimi Boy/Art/Open EquipmentShop Asset Gallery")]
        public static void OpenEquipmentShopAssetGallery()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GalleryScenePath) ==
                null)
            {
                BuildAll();
            }

            EditorSceneManager.OpenScene(GalleryScenePath, OpenSceneMode.Single);
        }

        public static void BuildEquipmentShopArtPassBatch()
        {
            BuildAll();
        }

        public static void ApplyEquipmentShopArtToMainSceneBatch()
        {
            EnsureGeneratedFolders();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ApplyToMainScene(false);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            NormalizeSerializedOutputs();
        }

        private static void BuildAll()
        {
            EnsureGeneratedFolders();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            try
            {
                List<AssetBuildResult> results = BuildCanonicalAssets();
                BuildGalleryScene(results);
                ApplyToMainScene(true);
                WriteReport(results);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(
                    ImportAssetOptions.ForceSynchronousImport);
                NormalizeSerializedOutputs();
                Debug.Log(
                    "[Sashimi Boy] EquipmentShop art pass generated: " +
                    AssetSpecs.Length + " wrappers, gallery, and scene; " +
                    (Application.isBatchMode
                        ? "previews preserved in batch mode."
                        : "previews generated."));
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

        private static List<AssetBuildResult> BuildCanonicalAssets()
        {
            List<AssetBuildResult> results = new List<AssetBuildResult>();
            for (int i = 0; i < AssetSpecs.Length; i++)
            {
                AssetSpec spec = AssetSpecs[i];
                ShowProgress(
                    "Building EquipmentShop Art",
                    spec.AssetId,
                    (float)i / AssetSpecs.Length);
                ConfigureTextureImporters(spec);
                results.Add(BuildCanonicalAsset(spec));
            }

            return results;
        }

        private static AssetBuildResult BuildCanonicalAsset(AssetSpec spec)
        {
            GameObject source =
                AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
            Require(source != null, "Required FBX is missing: " + spec.ModelPath);

            Dictionary<int, TextureSet> textureSets = FindTextureSets(spec);
            Dictionary<int, Material> materials =
                BuildMaterials(spec, textureSets);
            Require(materials.Count > 0,
                "No material could be built for " + spec.AssetId + ".");

            GameObject root = new GameObject(spec.PrefabName);
            GameObject modelContainer = new GameObject("Model");
            modelContainer.transform.SetParent(root.transform, false);
            GameObject imported =
                PrefabUtility.InstantiatePrefab(source) as GameObject;
            Require(imported != null,
                "Could not instantiate source FBX: " + spec.ModelPath);

            try
            {
                imported.name = source.name;
                imported.transform.SetParent(modelContainer.transform, false);
                imported.transform.localPosition = Vector3.zero;
                imported.transform.localRotation = Quaternion.identity;
                imported.transform.localScale = Vector3.one;
                RemoveImportedColliders(imported);

                int fallbackSlots = AssignGeneratedMaterials(
                    imported,
                    materials);
                Vector3 wrapperRotation = spec.AutoUpright
                    ? FindUprightRotation(root, modelContainer)
                    : Vector3.zero;
                wrapperRotation.y = spec.WrapperYaw;
                modelContainer.transform.localRotation =
                    Quaternion.Euler(wrapperRotation);

                Bounds rawBounds = RendererBounds(root);
                float largest = Mathf.Max(
                    rawBounds.size.x,
                    Mathf.Max(rawBounds.size.y, rawBounds.size.z));
                Require(float.IsFinite(largest) && largest > 0.0001f,
                    "Invalid renderer bounds for " + spec.AssetId + ".");
                float scale = Mathf.Clamp(
                    spec.TargetLargestDimension / largest,
                    0.001f,
                    1000f);
                modelContainer.transform.localScale = Vector3.one * scale;

                Bounds scaledBounds = RendererBounds(root);
                modelContainer.transform.position += new Vector3(
                    -scaledBounds.center.x,
                    -scaledBounds.min.y,
                    -scaledBounds.center.z);
                root.transform.position = Vector3.zero;
                root.transform.rotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;

                GameObject prefab =
                    PrefabUtility.SaveAsPrefabAsset(root, spec.PrefabPath);
                Require(prefab != null,
                    "Could not save wrapper prefab: " + spec.PrefabPath);
                return new AssetBuildResult(
                    spec,
                    textureSets.Count,
                    materials.Count,
                    fallbackSlots,
                    RendererBounds(root).size);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Dictionary<int, TextureSet> FindTextureSets(
            AssetSpec spec)
        {
            Dictionary<int, TextureSet> sets =
                new Dictionary<int, TextureSet>();
            string[] guids = AssetDatabase.FindAssets(
                "t:Texture2D",
                new[] { spec.TextureRoot });
            string[] paths = guids.Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            bool multipart = paths.Any(path => ExtractPartIndex(path) >= 0);

            for (int i = 0; i < paths.Length; i++)
            {
                TextureKind kind = GetTextureKind(paths[i]);
                if (kind == TextureKind.Unknown)
                {
                    continue;
                }

                int partIndex = multipart ? ExtractPartIndex(paths[i]) : -1;
                if (multipart && partIndex < 0)
                {
                    continue;
                }

                TextureSet set;
                if (!sets.TryGetValue(partIndex, out set))
                {
                    set = new TextureSet(partIndex);
                    sets.Add(partIndex, set);
                }

                set.Assign(kind, paths[i]);
            }

            return sets;
        }

        private static void ConfigureTextureImporters(AssetSpec spec)
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:Texture2D",
                new[] { spec.TextureRoot });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                TextureKind kind = GetTextureKind(path);
                if (kind == TextureKind.Unknown)
                {
                    continue;
                }

                TextureImporter importer =
                    AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                TextureImporterType desiredType = kind == TextureKind.Normal
                    ? TextureImporterType.NormalMap
                    : TextureImporterType.Default;
                bool desiredSrgb = kind == TextureKind.BaseColor;
                bool changed = false;
                changed |= SetIfDifferent(importer.textureType, desiredType,
                    value => importer.textureType = value);
                changed |= SetIfDifferent(importer.sRGBTexture, desiredSrgb,
                    value => importer.sRGBTexture = value);
                changed |= SetIfDifferent(importer.mipmapEnabled, true,
                    value => importer.mipmapEnabled = value);
                changed |= SetIfDifferent(importer.isReadable, false,
                    value => importer.isReadable = value);
                changed |= SetIfDifferent(
                    importer.maxTextureSize,
                    spec.MaximumTextureSize,
                    value => importer.maxTextureSize = value);
                if (changed)
                {
                    importer.SaveAndReimport();
                }
            }

            ModelImporter modelImporter =
                AssetImporter.GetAtPath(spec.ModelPath) as ModelImporter;
            if (modelImporter != null && modelImporter.importAnimation)
            {
                modelImporter.importAnimation = false;
                modelImporter.SaveAndReimport();
            }
        }

        private static Dictionary<int, Material> BuildMaterials(
            AssetSpec spec,
            Dictionary<int, TextureSet> textureSets)
        {
            Dictionary<int, Material> materials =
                new Dictionary<int, Material>();
            if (textureSets.Count == 0)
            {
                string fallbackPath = MaterialsRoot + "/MAT_EquipmentShop_" +
                    spec.AssetId + ".mat";
                Material fallback = LoadOrCreateMaterial(fallbackPath);
                ConfigureMaterial(fallback, null, null, null);
                materials.Add(-1, fallback);
                return materials;
            }

            foreach (KeyValuePair<int, TextureSet> pair in
                     textureSets.OrderBy(item => item.Key))
            {
                string suffix = textureSets.Count > 1
                    ? "_Part" + pair.Key.ToString(
                        "00",
                        CultureInfo.InvariantCulture)
                    : string.Empty;
                string materialPath = MaterialsRoot +
                    "/MAT_EquipmentShop_" + spec.AssetId + suffix + ".mat";
                Material material = LoadOrCreateMaterial(materialPath);
                Texture2D packed = BuildPackedMap(spec, pair.Value, suffix);
                ConfigureMaterial(
                    material,
                    LoadTexture(pair.Value.BaseColorPath),
                    LoadTexture(pair.Value.NormalPath),
                    packed);
                materials.Add(pair.Key, material);
            }

            return materials;
        }

        private static Material LoadOrCreateMaterial(string path)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Standard");
            Require(shader != null, "Built-in Standard shader is unavailable.");
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            return material;
        }

        private static void ConfigureMaterial(
            Material material,
            Texture2D baseColor,
            Texture2D normal,
            Texture2D packed)
        {
            material.SetColor(
                "_Color",
                baseColor != null
                    ? Color.white
                    : new Color(0.28f, 0.3f, 0.33f, 1f));
            material.SetTexture("_MainTex", baseColor);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            material.SetTexture("_MetallicGlossMap", packed);
            material.SetFloat("_Metallic", packed != null ? 1f : 0.05f);
            material.SetFloat("_Glossiness", packed != null ? 1f : 0.3f);
            material.SetFloat("_GlossMapScale", packed != null ? 1f : 0.3f);
            material.SetFloat("_Mode", 0f);
            material.SetInt(
                "_SrcBlend",
                (int)UnityEngine.Rendering.BlendMode.One);
            material.SetInt(
                "_DstBlend",
                (int)UnityEngine.Rendering.BlendMode.Zero);
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
        }

        private static Texture2D BuildPackedMap(
            AssetSpec spec,
            TextureSet set,
            string suffix)
        {
            if (string.IsNullOrEmpty(set.MetallicPath) ||
                string.IsNullOrEmpty(set.RoughnessPath))
            {
                return null;
            }

            TextureImporter metallicImporter =
                AssetImporter.GetAtPath(set.MetallicPath) as TextureImporter;
            TextureImporter roughnessImporter =
                AssetImporter.GetAtPath(set.RoughnessPath) as TextureImporter;
            Require(metallicImporter != null && roughnessImporter != null,
                "Metallic/roughness importer is unavailable for " +
                spec.AssetId + ".");
            bool metallicReadable = metallicImporter.isReadable;
            bool roughnessReadable = roughnessImporter.isReadable;
            string outputPath = PackedMapsRoot +
                "/MS_EquipmentShop_" + spec.AssetId + suffix + ".png";

            try
            {
                if (!metallicReadable)
                {
                    metallicImporter.isReadable = true;
                    metallicImporter.SaveAndReimport();
                }

                roughnessImporter = AssetImporter.GetAtPath(
                    set.RoughnessPath) as TextureImporter;
                if (roughnessImporter != null && !roughnessReadable)
                {
                    roughnessImporter.isReadable = true;
                    roughnessImporter.SaveAndReimport();
                }

                Texture2D metallic = LoadTexture(set.MetallicPath);
                Texture2D roughness = LoadTexture(set.RoughnessPath);
                Require(metallic != null && roughness != null,
                    "Metallic/roughness texture failed to load for " +
                    spec.AssetId + ".");
                int width = Mathf.Min(metallic.width, roughness.width);
                int height = Mathf.Min(metallic.height, roughness.height);
                Color32[] metallicPixels =
                    ReadPixelsAtSize(metallic, width, height);
                Color32[] roughnessPixels =
                    ReadPixelsAtSize(roughness, width, height);
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
                    AssetPathToAbsolutePath(outputPath),
                    output.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(output);

                AssetDatabase.ImportAsset(
                    outputPath,
                    ImportAssetOptions.ForceSynchronousImport);
                TextureImporter outputImporter =
                    AssetImporter.GetAtPath(outputPath) as TextureImporter;
                Require(outputImporter != null,
                    "Packed-map importer is unavailable: " + outputPath);
                outputImporter.textureType = TextureImporterType.Default;
                outputImporter.sRGBTexture = false;
                outputImporter.mipmapEnabled = true;
                outputImporter.alphaSource =
                    TextureImporterAlphaSource.FromInput;
                outputImporter.alphaIsTransparency = false;
                outputImporter.isReadable = false;
                outputImporter.maxTextureSize = spec.MaximumTextureSize;
                outputImporter.SaveAndReimport();
                return LoadTexture(outputPath);
            }
            finally
            {
                metallicImporter = AssetImporter.GetAtPath(
                    set.MetallicPath) as TextureImporter;
                roughnessImporter = AssetImporter.GetAtPath(
                    set.RoughnessPath) as TextureImporter;
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

        private static int AssignGeneratedMaterials(
            GameObject imported,
            Dictionary<int, Material> materials)
        {
            int fallbackSlots = 0;
            Material first = materials.OrderBy(item => item.Key).First().Value;
            Renderer[] renderers =
                imported.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                Material[] slots = renderer.sharedMaterials;
                if (slots.Length == 0)
                {
                    slots = new Material[1];
                }

                Mesh mesh = RendererMesh(renderer);
                for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                {
                    Material selected = null;
                    if (materials.Count == 1)
                    {
                        selected = first;
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

                        materials.TryGetValue(partIndex, out selected);
                    }

                    if (selected == null)
                    {
                        selected = first;
                        fallbackSlots++;
                    }

                    slots[slotIndex] = selected;
                }

                renderer.sharedMaterials = slots;
            }

            return fallbackSlots;
        }

        private static void BuildGalleryScene(
            List<AssetBuildResult> results)
        {
            SceneAsset galleryAsset =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(GalleryScenePath);
            Scene scene = galleryAsset == null
                ? EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Single)
                : EditorSceneManager.OpenScene(
                    GalleryScenePath,
                    OpenSceneMode.Single);
            Material floor = BuildArchitectureMaterial(
                "GalleryFloor",
                new Color(0.08f, 0.095f, 0.11f, 1f),
                0.18f,
                0.45f);
            Material plinth = BuildArchitectureMaterial(
                "GalleryPlinth",
                new Color(0.21f, 0.18f, 0.14f, 1f),
                0.05f,
                0.28f);
            Material accent = BuildArchitectureMaterial(
                "GalleryAccent",
                new Color(0.92f, 0.48f, 0.12f, 1f),
                0.15f,
                0.5f);

            GameObject galleryRoot = GetOrCreateSceneRoot(
                scene,
                "EquipmentShopGalleryRoot");
            CreatePrimitive(
                galleryRoot.transform,
                "GalleryFloor",
                PrimitiveType.Cube,
                new Vector3(0f, -0.12f, 3.5f),
                new Vector3(16f, 0.2f, 11f),
                Vector3.zero,
                floor,
                false);
            for (int i = 0; i < results.Count; i++)
            {
                int column = i % 5;
                int row = i / 5;
                Vector3 position = new Vector3(
                    -6f + column * 3f,
                    0.12f,
                    row * 3.5f);
                Transform cell = GetOrCreateChild(
                    galleryRoot.transform,
                    "Cell_" + (i + 1).ToString("00"));
                CreatePrimitive(
                    cell,
                    "Plinth",
                    PrimitiveType.Cube,
                    position,
                    new Vector3(2.5f, 0.22f, 2.35f),
                    Vector3.zero,
                    i % 2 == 0 ? plinth : accent,
                    false);
                PlacePrefab(
                    scene,
                    cell,
                    results[i].Spec,
                    results[i].Spec.AssetId + "_Gallery",
                    position + new Vector3(0f, 0.24f, 0f),
                    new Vector3(0f, 180f, 0f));
                CreateLabel(
                    cell,
                    results[i].Spec.AssetId + "_Label",
                    FormatGalleryLabel(results[i].Spec.AssetId),
                    position + new Vector3(0f, 0.18f, -1.28f));
            }

            GameObject cameraObject = GetOrCreateSceneRoot(scene, "Main Camera");
            Camera camera = cameraObject.GetComponent<Camera>();
            if (camera == null)
            {
                camera = cameraObject.AddComponent<Camera>();
            }

            cameraObject.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.03f, 0.04f, 1f);
            camera.fieldOfView = 42f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            cameraObject.transform.position = new Vector3(0f, 11f, -14.5f);
            cameraObject.transform.rotation = Quaternion.LookRotation(
                new Vector3(0f, 0.9f, 3.3f) -
                cameraObject.transform.position,
                Vector3.up);
            CreateDirectionalLight(scene, "GalleryKey", new Vector3(42f, -28f, 0f), 1.1f);
            CreateDirectionalLight(scene, "GalleryFill", new Vector3(30f, 150f, 0f), 0.55f);
            EditorSceneManager.SaveScene(scene, GalleryScenePath);
            RenderPreview(camera, GalleryPreviewPath);
        }

        private static void ApplyToMainScene(bool renderPreview)
        {
            Scene scene = EditorSceneManager.OpenScene(
                MainScenePath,
                OpenSceneMode.Single);
            Dictionary<string, TransformState> protectedTransforms =
                CaptureProtectedTransforms(scene);
            int controllerCount = GetSceneComponents<EquipmentShopController>(scene)
                .Length;
            int returnDoorCount = GetSceneComponents<ReturnToStreetDoor>(scene)
                .Length;

            HideLegacyVisuals(scene);
            GameObject artRoot = GetOrCreateSceneRoot(scene, ArtRootName);
            artRoot.transform.position = Vector3.zero;
            artRoot.transform.rotation = Quaternion.identity;
            artRoot.transform.localScale = Vector3.one;

            Material wall = BuildArchitectureMaterial(
                "Wall",
                new Color(0.14f, 0.12f, 0.105f, 1f),
                0.05f,
                0.22f);
            Material wood = BuildArchitectureMaterial(
                "Wood",
                new Color(0.28f, 0.14f, 0.07f, 1f),
                0.03f,
                0.32f);
            Material trim = BuildArchitectureMaterial(
                "Trim",
                new Color(0.68f, 0.33f, 0.09f, 1f),
                0.18f,
                0.48f);
            Material steel = BuildArchitectureMaterial(
                "Steel",
                new Color(0.16f, 0.18f, 0.2f, 1f),
                0.72f,
                0.52f);

            BuildArchitecture(artRoot.transform, wall, wood, trim, steel);
            BuildZones(scene, artRoot.transform);
            BuildStoryAnchors(scene, artRoot.transform);
            CreatePointLight(
                artRoot.transform,
                "CounterWarmLight",
                new Vector3(0f, 3.1f, 1.6f),
                new Color(1f, 0.58f, 0.28f),
                1.2f,
                6f);
            CreatePointLight(
                artRoot.transform,
                "DemoCoolLight",
                new Vector3(-4.2f, 2.7f, 0.8f),
                new Color(0.32f, 0.62f, 1f),
                0.85f,
                5f);

            ValidateMainScene(
                scene,
                artRoot,
                protectedTransforms,
                controllerCount,
                returnDoorCount);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, MainScenePath);
            if (renderPreview)
            {
                RenderMainScenePreview(scene);
            }
        }

        private static void BuildArchitecture(
            Transform artRoot,
            Material wall,
            Material wood,
            Material trim,
            Material steel)
        {
            Transform architecture = GetOrCreateChild(artRoot, "Architecture");
            CreatePrimitive(architecture, "BackWall", PrimitiveType.Cube,
                new Vector3(0f, 1.75f, 4.02f),
                new Vector3(12.4f, 3.5f, 0.2f),
                Vector3.zero, wall, true);
            CreatePrimitive(architecture, "LeftWall", PrimitiveType.Cube,
                new Vector3(-6.12f, 1.75f, 0f),
                new Vector3(0.2f, 3.5f, 8f),
                Vector3.zero, wall, true);
            CreatePrimitive(architecture, "RightWall", PrimitiveType.Cube,
                new Vector3(6.12f, 1.75f, 0f),
                new Vector3(0.2f, 3.5f, 8f),
                Vector3.zero, wall, true);
            CreatePrimitive(architecture, "BackWallTrim", PrimitiveType.Cube,
                new Vector3(0f, 3.15f, 3.88f),
                new Vector3(12f, 0.16f, 0.12f),
                Vector3.zero, trim, false);
            CreatePrimitive(architecture, "LeftCeilingBeam", PrimitiveType.Cube,
                new Vector3(-3.8f, 3.25f, 0.4f),
                new Vector3(0.14f, 0.14f, 7f),
                Vector3.zero, steel, false);
            CreatePrimitive(architecture, "RightCeilingBeam", PrimitiveType.Cube,
                new Vector3(3.8f, 3.25f, 0.4f),
                new Vector3(0.14f, 0.14f, 7f),
                Vector3.zero, steel, false);

            Transform counter = GetOrCreateChild(artRoot, "CounterRepairZone");
            CreatePrimitive(counter, "CounterFace", PrimitiveType.Cube,
                new Vector3(0f, 0.55f, 2.42f),
                new Vector3(4.6f, 1.1f, 0.62f),
                Vector3.zero, wood, true);
            CreatePrimitive(counter, "CounterTop", PrimitiveType.Cube,
                new Vector3(0f, 1.14f, 2.34f),
                new Vector3(4.9f, 0.12f, 0.86f),
                Vector3.zero, trim, false);
            CreatePrimitive(counter, "RepairBacksplash", PrimitiveType.Cube,
                new Vector3(0f, 1.82f, 3.82f),
                new Vector3(5.4f, 1.1f, 0.12f),
                Vector3.zero, steel, false);
        }

        private static void BuildZones(Scene scene, Transform artRoot)
        {
            Transform counter = GetOrCreateChild(artRoot, "CounterRepairZone");
            Place(scene, counter, "EquipmentShopOwner", "Owner_Canonical",
                new Vector3(0f, 0f, 3.12f), new Vector3(0f, 0f, 0f));
            Place(scene, counter, "EffectsPedals", "Repair_EffectsPedals",
                new Vector3(-1.05f, 1.21f, 2.16f),
                new Vector3(0f, 180f, 0f));
            Place(scene, counter, "GuitarPedal", "Repair_GuitarPedal",
                new Vector3(0.75f, 1.21f, 2.15f),
                new Vector3(0f, 180f, 0f));

            Transform demo = GetOrCreateChild(artRoot, "DemoZone");
            Place(scene, demo, "StackedSpeaker", "Demo_StackedSpeaker",
                new Vector3(-4.85f, 0.05f, 2.25f),
                new Vector3(0f, 90f, 0f));
            Place(scene, demo, "SpeakerBox", "Demo_SpeakerBox",
                new Vector3(-4.9f, 0.05f, 0.65f),
                new Vector3(0f, 90f, 0f));
            Place(scene, demo, "Loudspeaker", "Demo_Loudspeaker",
                new Vector3(-4.75f, 0.05f, -0.85f),
                new Vector3(0f, 90f, 0f));
            Place(scene, demo, "StereoSpeaker", "Demo_StereoSpeaker",
                new Vector3(-3.55f, 0.05f, 3.2f),
                new Vector3(0f, 180f, 0f));
            Place(scene, demo, "VintageSpeaker", "Demo_VintageSpeaker",
                new Vector3(-5.2f, 0.05f, 3.2f),
                new Vector3(0f, 180f, 0f));
            Place(scene, demo, "StageSpotlight", "Demo_StageSpotlight",
                new Vector3(-3.2f, 0.05f, 2.7f),
                new Vector3(0f, 145f, 0f));

            Transform instrument = GetOrCreateChild(artRoot, "InstrumentZone");
            Place(scene, instrument, "ElectronicDrumKit",
                "Instrument_ElectronicDrumKit",
                new Vector3(4.35f, 0.05f, 2.35f),
                new Vector3(0f, 205f, 0f));
            Place(scene, instrument, "MidiKeyboardController",
                "Instrument_MidiKeyboard",
                new Vector3(2.35f, 0.78f, 3.62f),
                new Vector3(0f, 180f, 0f));
            Place(scene, instrument, "ModularSynthesizer",
                "Instrument_ModularSynth",
                new Vector3(3.95f, 1.2f, 3.55f),
                new Vector3(0f, 180f, 0f));

            Transform waiting = GetOrCreateChild(artRoot, "WaitingZone");
            Place(scene, waiting, "WoodenSofa", "Waiting_WoodenSofa",
                new Vector3(4.7f, 0.05f, -0.45f),
                new Vector3(0f, -90f, 0f));
        }

        private static void BuildStoryAnchors(Scene scene, Transform artRoot)
        {
            Transform anchors = GetOrCreateChild(artRoot, "StoryAnchors");
            CopyAnchor(scene, anchors, "PlayerEntryExitAnchor", "PlayerSpawnPoint");
            CopyAnchor(scene, anchors, "OwnerDialogueAnchor", "Shopkeeper");
            CopyAnchor(scene, anchors, "PurchaseInteractionAnchor", "Counter");
            CreateAnchor(anchors, "EquipmentInspectAnchor_Demo",
                new Vector3(-3.2f, 0.6f, 0.1f),
                Quaternion.Euler(0f, -90f, 0f));
            CreateAnchor(anchors, "EquipmentInspectAnchor_Instrument",
                new Vector3(2.8f, 0.6f, 0.7f),
                Quaternion.Euler(0f, 90f, 0f));

            Transform spawn = FindNamed(scene, "PlayerSpawnPoint").transform;
            Transform owner = FindNamed(scene, "Owner_Canonical").transform;
            CreateAnchor(anchors, "StoryObjectiveSightline_FromEntry",
                spawn.position + Vector3.up,
                Quaternion.LookRotation(
                    owner.position + Vector3.up * 1.55f -
                    (spawn.position + Vector3.up),
                    Vector3.up));
            CreateAnchor(anchors, "StoryObjectiveSightline_ToOwner",
                owner.position + Vector3.up * 1.55f,
                Quaternion.LookRotation(
                    spawn.position + Vector3.up -
                    (owner.position + Vector3.up * 1.55f),
                    Vector3.up));
        }

        private static void HideLegacyVisuals(Scene scene)
        {
            string[] targets =
            {
                "EquipmentShop_PresentationRoot",
                "Counter",
                "Shopkeeper",
                "Label_ReturnDoor",
                "WallGear_0",
                "WallGear_1",
                "WallGear_2",
                "WallGear_3",
                "WallGear_4",
                "WallGear_5",
            };
            for (int i = 0; i < targets.Length; i++)
            {
                GameObject target = FindNamed(scene, targets[i]);
                Require(target != null, "Legacy scene object is missing: " + targets[i]);
                foreach (Renderer renderer in
                         target.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = false;
                }

                foreach (Collider collider in
                         target.GetComponentsInChildren<Collider>(true))
                {
                    collider.enabled = false;
                }
            }
        }

        private static void ValidateMainScene(
            Scene scene,
            GameObject artRoot,
            Dictionary<string, TransformState> protectedTransforms,
            int controllerCount,
            int returnDoorCount)
        {
            Require(controllerCount == 1 &&
                    GetSceneComponents<EquipmentShopController>(scene).Length == 1,
                "EquipmentShopController count changed.");
            Require(returnDoorCount == 1 &&
                    GetSceneComponents<ReturnToStreetDoor>(scene).Length == 1,
                "ReturnToStreetDoor count changed.");
            Require(GetSceneComponents<KevinFirstPersonCameraRig>(scene).Length == 1,
                "First-person camera rig count changed.");
            Require(GetSceneComponents<AudioListener>(scene).Count(item =>
                    item.enabled && item.gameObject.activeInHierarchy) == 1,
                "EquipmentShop must have exactly one active AudioListener.");
            Require(GetSceneComponents<EventSystem>(scene).Count(item =>
                    item.enabled && item.gameObject.activeInHierarchy) == 1,
                "EquipmentShop must have exactly one active EventSystem.");

            foreach (KeyValuePair<string, TransformState> pair in
                     protectedTransforms)
            {
                Transform current = FindNamed(scene, pair.Key).transform;
                Require(pair.Value.Matches(current),
                    pair.Key + " transform changed during the art pass.");
            }

            for (int i = 0; i < AssetSpecs.Length; i++)
            {
                Renderer[] prefabRenderers = AssetDatabase
                    .LoadAssetAtPath<GameObject>(AssetSpecs[i].PrefabPath)
                    .GetComponentsInChildren<Renderer>(true);
                Require(prefabRenderers.Length > 0,
                    "Wrapper has no renderer: " + AssetSpecs[i].PrefabPath);
                Require(prefabRenderers.All(renderer =>
                    renderer.sharedMaterials.Length > 0 &&
                    renderer.sharedMaterials.All(material => material != null)),
                    "Wrapper has a missing material: " +
                    AssetSpecs[i].PrefabPath);
            }

            ValidatePositiveScales(artRoot.transform);
            ValidateProtectedRoutes(artRoot);
            ValidateNoMissingScripts(scene);
        }

        private static Dictionary<string, TransformState>
            CaptureProtectedTransforms(Scene scene)
        {
            string[] names =
            {
                "PlayerSpawnPoint",
                "Kevin_Player",
                "Door_To_Street",
                "Shopkeeper",
                "Counter",
            };
            Dictionary<string, TransformState> states =
                new Dictionary<string, TransformState>();
            for (int i = 0; i < names.Length; i++)
            {
                GameObject target = FindNamed(scene, names[i]);
                Require(target != null,
                    "Protected EquipmentShop object is missing: " + names[i]);
                states.Add(names[i], new TransformState(target.transform));
            }

            return states;
        }

        private static void ValidateProtectedRoutes(GameObject artRoot)
        {
            Physics.SyncTransforms();
            Bounds[] routes =
            {
                new Bounds(
                    new Vector3(0f, 1.2f, -0.35f),
                    new Vector3(1.8f, 2.4f, 3.8f)),
                new Bounds(
                    new Vector3(2.65f, 1.2f, -2.35f),
                    new Vector3(5.3f, 2.4f, 1.4f)),
                new Bounds(
                    new Vector3(0f, 1.2f, 1.15f),
                    new Vector3(2.2f, 2.4f, 0.8f)),
            };
            foreach (Collider collider in
                     artRoot.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.enabled || !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Require(!routes.Any(route => collider.bounds.Intersects(route)),
                    collider.name + " blocks a protected EquipmentShop route.");
            }
        }

        private static void ValidateNoMissingScripts(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform child in
                         root.GetComponentsInChildren<Transform>(true))
                {
                    Require(
                        GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                            child.gameObject) == 0,
                        "Missing Script under " + child.name + ".");
                }
            }
        }

        private static void ValidatePositiveScales(Transform root)
        {
            foreach (Transform item in
                     root.GetComponentsInChildren<Transform>(true))
            {
                Vector3 scale = item.localScale;
                Require(scale.x > 0f && scale.y > 0f && scale.z > 0f,
                    "Non-positive scale under " + item.name + ".");
            }
        }

        private static void RenderMainScenePreview(Scene scene)
        {
            GameObject cameraObject = new GameObject("EquipmentShopPreviewCamera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.03f, 0.025f, 0.02f, 1f);
            camera.fieldOfView = 48f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            cameraObject.transform.position = new Vector3(0f, 7.2f, -10.5f);
            cameraObject.transform.rotation = Quaternion.LookRotation(
                new Vector3(0f, 1f, 0.85f) - cameraObject.transform.position,
                Vector3.up);
            try
            {
                RenderPreview(camera, MainPreviewPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private static void RenderPreview(Camera camera, string assetPath)
        {
            if (Application.isBatchMode)
            {
                Debug.Log(
                    "[Sashimi Boy] Skipping EquipmentShop preview rendering " +
                    "in batch mode: " + assetPath);
                return;
            }

            const int width = 1600;
            const int height = 900;
            RenderTexture renderTexture = new RenderTexture(width, height, 24);
            RenderTexture previous = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.aspect = (float)width / height;
                camera.targetTexture = renderTexture;
                RenderTexture.active = renderTexture;
                camera.Render();
                Texture2D image = new Texture2D(
                    width,
                    height,
                    TextureFormat.RGB24,
                    false,
                    false);
                image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                image.Apply(false, false);
                File.WriteAllBytes(
                    AssetPathToAbsolutePath(assetPath),
                    image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previous;
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }

            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = false;
                importer.isReadable = false;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
        }

        private static void WriteReport(List<AssetBuildResult> results)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("# EquipmentShop Art Pass Report");
            report.AppendLine();
            report.AppendLine("Generated from the source assets approved by issue #26.");
            report.AppendLine();
            report.AppendLine("| Asset | Wrapper | Texture sets | Materials | Fallback slots | Final size |");
            report.AppendLine("|---|---|---:|---:|---:|---|");
            for (int i = 0; i < results.Count; i++)
            {
                AssetBuildResult result = results[i];
                report.Append("| ").Append(result.Spec.AssetId)
                    .Append(" | `").Append(result.Spec.PrefabPath)
                    .Append("` | ").Append(result.TextureSetCount)
                    .Append(" | ").Append(result.MaterialCount)
                    .Append(" | ").Append(result.FallbackSlots)
                    .Append(" | ").Append(FormatVector(result.FinalSize))
                    .AppendLine(" |");
            }

            report.AppendLine();
            report.AppendLine("## Scene contract");
            report.AppendLine();
            report.AppendLine("- Zones: Counter/Repair, Demo, Instrument, Waiting.");
            report.AppendLine("- Preserved references: player entry/exit, owner dialogue, purchase interaction, equipment inspection, and story objective sightline.");
            report.AppendLine("- Legacy placeholder renderers and colliders are disabled; existing gameplay components and protected transforms are preserved.");
            report.AppendLine("- No source FBX is placed directly in the scene; all source visuals use generated wrapper prefabs.");
            report.AppendLine();
            report.AppendLine("## Human verification");
            report.AppendLine();
            report.AppendLine("1. Open `EquipmentShopAssetGallery` and inspect all 13 wrappers and materials.");
            report.AppendLine("2. Enter EquipmentShop from Street and confirm the counter and owner read clearly from the entrance.");
            report.AppendLine("3. Walk to the counter, both display zones, waiting area, and return door without obstruction.");
            report.AppendLine("4. Exercise purchase and leave controls and confirm the first-person camera remains suspended while UI is open.");
            File.WriteAllText(
                AssetPathToAbsolutePath(ReportPath),
                report.ToString().Replace("\r\n", "\n"),
                new UTF8Encoding(false));
            AssetDatabase.ImportAsset(
                ReportPath,
                ImportAssetOptions.ForceSynchronousImport);
        }

        private static Material BuildArchitectureMaterial(
            string name,
            Color color,
            float metallic,
            float smoothness)
        {
            string path = MaterialsRoot + "/MAT_EquipmentShop_" + name + ".mat";
            Material material = LoadOrCreateMaterial(path);
            ConfigureMaterial(material, null, null, null);
            material.SetColor("_Color", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void Place(
            Scene scene,
            Transform parent,
            string assetId,
            string instanceName,
            Vector3 position,
            Vector3 rotation)
        {
            AssetSpec spec = AssetSpecs.First(item => item.AssetId == assetId);
            PlacePrefab(scene, parent, spec, instanceName, position, rotation);
        }

        private static GameObject PlacePrefab(
            Scene scene,
            Transform parent,
            AssetSpec spec,
            string instanceName,
            Vector3 position,
            Vector3 rotation)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
            Require(prefab != null,
                "Required wrapper prefab is missing: " + spec.PrefabPath);
            Transform existing = parent.Find(instanceName);
            GameObject instance = existing != null ? existing.gameObject : null;
            if (instance != null &&
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance) !=
                spec.PrefabPath)
            {
                UnityEngine.Object.DestroyImmediate(instance);
                instance = null;
            }

            if (instance == null)
            {
                instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                Require(instance != null,
                    "Could not instantiate wrapper: " + spec.PrefabPath);
                instance.name = instanceName;
                instance.transform.SetParent(parent, false);
            }

            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(rotation);
            instance.transform.localScale = Vector3.one;
            return instance;
        }

        private static GameObject CreatePrimitive(
            Transform parent,
            string name,
            PrimitiveType primitiveType,
            Vector3 position,
            Vector3 scale,
            Vector3 rotation,
            Material material,
            bool colliderEnabled)
        {
            Transform existing = parent.Find(name);
            GameObject instance = existing != null
                ? existing.gameObject
                : GameObject.CreatePrimitive(primitiveType);
            instance.name = name;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(rotation);
            instance.transform.localScale = scale;
            Renderer renderer = instance.GetComponent<Renderer>();
            Require(renderer != null, "Primitive renderer is missing: " + name);
            renderer.sharedMaterial = material;
            Collider collider = instance.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = colliderEnabled;
            }

            return instance;
        }

        private static void CreateLabel(
            Transform parent,
            string name,
            string value,
            Vector3 position)
        {
            Transform existing = parent.Find(name);
            GameObject labelObject = existing != null
                ? existing.gameObject
                : new GameObject(name);
            labelObject.name = name;
            labelObject.transform.SetParent(parent, false);
            labelObject.transform.localPosition = position;
            labelObject.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
            labelObject.transform.localScale = Vector3.one * 0.065f;
            TextMesh label = labelObject.GetComponent<TextMesh>();
            if (label == null)
            {
                label = labelObject.AddComponent<TextMesh>();
            }

            label.text = value;
            label.fontSize = 42;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = new Color(1f, 0.9f, 0.7f, 1f);
        }

        private static string FormatGalleryLabel(string assetId)
        {
            string label = Regex.Replace(assetId, "([a-z])([A-Z])", "$1 $2");
            if (label.Length <= 15)
            {
                return label;
            }

            int midpoint = label.Length / 2;
            int after = label.IndexOf(' ', midpoint);
            int before = label.LastIndexOf(' ', midpoint);
            int split = after >= 0 &&
                (before < 0 || after - midpoint <= midpoint - before)
                ? after
                : before;
            return split > 0
                ? label.Substring(0, split) + "\n" +
                  label.Substring(split + 1)
                : label;
        }

        private static void CopyAnchor(
            Scene scene,
            Transform parent,
            string anchorName,
            string targetName)
        {
            Transform target = FindNamed(scene, targetName).transform;
            CreateAnchor(parent, anchorName, target.position, target.rotation);
        }

        private static Transform CreateAnchor(
            Transform parent,
            string name,
            Vector3 position,
            Quaternion rotation)
        {
            Transform anchor = GetOrCreateChild(parent, name);
            anchor.position = position;
            anchor.rotation = rotation;
            anchor.localScale = Vector3.one;
            return anchor;
        }

        private static void CreatePointLight(
            Transform parent,
            string name,
            Vector3 position,
            Color color,
            float intensity,
            float range)
        {
            Transform child = GetOrCreateChild(parent, name);
            child.localPosition = position;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            Light light = child.GetComponent<Light>();
            if (light == null)
            {
                light = child.gameObject.AddComponent<Light>();
            }

            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.Soft;
        }

        private static void CreateDirectionalLight(
            Scene scene,
            string name,
            Vector3 rotation,
            float intensity)
        {
            GameObject lightObject = GetOrCreateSceneRoot(scene, name);
            lightObject.transform.position = Vector3.zero;
            lightObject.transform.rotation = Quaternion.Euler(rotation);
            lightObject.transform.localScale = Vector3.one;
            Light light = lightObject.GetComponent<Light>();
            if (light == null)
            {
                light = lightObject.AddComponent<Light>();
            }

            light.type = LightType.Directional;
            light.color = new Color(1f, 0.86f, 0.72f);
            light.intensity = intensity;
            light.shadows = LightShadows.Soft;
        }

        private static GameObject GetOrCreateSceneRoot(Scene scene, string name)
        {
            GameObject existing = scene.GetRootGameObjects()
                .FirstOrDefault(item => item.name == name);
            if (existing != null)
            {
                return existing;
            }

            GameObject root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }

        private static Transform GetOrCreateChild(Transform parent, string name)
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

        private static GameObject FindNamed(Scene scene, string name)
        {
            GameObject result = scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(item => item.name == name)
                ?.gameObject;
            Require(result != null, "Scene object is missing: " + name);
            return result;
        }

        private static T[] GetSceneComponents<T>(Scene scene)
            where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .ToArray();
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

        private static Bounds RendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.enabled)
                .ToArray();
            Require(renderers.Length > 0,
                "Renderer bounds are unavailable under " + root.name + ".");
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static Mesh RendererMesh(Renderer renderer)
        {
            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null)
            {
                return skinned.sharedMesh;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        private static Vector3 FindUprightRotation(
            GameObject root,
            GameObject modelContainer)
        {
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
                Bounds bounds = RendererBounds(root);
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

        private static int ExtractPartIndex(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return -1;
            }

            Match match = PartRegex.Match(value);
            int partIndex;
            return match.Success && int.TryParse(
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
            return "(" + value.x.ToString("0.###", CultureInfo.InvariantCulture) +
                ", " + value.y.ToString("0.###", CultureInfo.InvariantCulture) +
                ", " + value.z.ToString("0.###", CultureInfo.InvariantCulture) +
                ")";
        }

        private static string AssetPathToAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(
                projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void NormalizeSerializedOutputs()
        {
            NormalizeSourceImporterMetadata();
            NormalizeSerializedFile(AssetPathToAbsolutePath(MainScenePath));
            NormalizeSerializedFile(AssetPathToAbsolutePath(GalleryScenePath));
            NormalizeSerializedFiles(MaterialsRoot, "*.mat");
            NormalizeSerializedFiles(PackedMapsRoot, "*.meta");
            NormalizeSerializedFiles(PrefabsRoot, "*.prefab");
        }

        private static void NormalizeSourceImporterMetadata()
        {
            HashSet<string> assetPaths = new HashSet<string>(
                StringComparer.Ordinal);
            for (int i = 0; i < AssetSpecs.Length; i++)
            {
                foreach (TextureSet set in FindTextureSets(AssetSpecs[i]).Values)
                {
                    if (!string.IsNullOrEmpty(set.MetallicPath))
                    {
                        assetPaths.Add(set.MetallicPath + ".meta");
                    }

                    if (!string.IsNullOrEmpty(set.RoughnessPath))
                    {
                        assetPaths.Add(set.RoughnessPath + ".meta");
                    }
                }
            }

            string[] orderedPaths = assetPaths
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                List<string> unavailable = new List<string>();
                for (int i = 0; i < orderedPaths.Length; i++)
                {
                    string absolutePath =
                        AssetPathToAbsolutePath(orderedPaths[i]);
                    try
                    {
                        if (!File.Exists(absolutePath))
                        {
                            throw new FileNotFoundException(
                                "Source importer metadata is unavailable.",
                                absolutePath);
                        }

                        NormalizeSerializedFile(absolutePath);
                    }
                    catch (IOException)
                    {
                        unavailable.Add(orderedPaths[i]);
                    }
                }

                if (unavailable.Count == 0)
                {
                    return;
                }

                if (attempt < 2)
                {
                    AssetDatabase.Refresh(
                        ImportAssetOptions.ForceSynchronousImport);
                    continue;
                }

                throw new IOException(
                    "Source importer metadata remained unavailable: " +
                    string.Join(", ", unavailable));
            }
        }

        private static void NormalizeSerializedFiles(
            string assetDirectory,
            string searchPattern)
        {
            string absoluteDirectory =
                AssetPathToAbsolutePath(assetDirectory);
            if (!Directory.Exists(absoluteDirectory))
            {
                return;
            }

            foreach (string path in Directory
                         .EnumerateFiles(
                             absoluteDirectory,
                             searchPattern,
                             SearchOption.AllDirectories)
                         .OrderBy(item => item, StringComparer.Ordinal))
            {
                NormalizeSerializedFile(path);
            }
        }

        private static void NormalizeSerializedFile(string absolutePath)
        {
            if (!File.Exists(absolutePath))
            {
                return;
            }

            string original = File.ReadAllText(absolutePath);
            string normalized = Regex.Replace(
                original,
                @"[ \t]+(?=\r?$)",
                string.Empty,
                RegexOptions.Multiline);
            if (string.Equals(original, normalized, StringComparison.Ordinal))
            {
                return;
            }

            File.WriteAllText(
                absolutePath,
                normalized,
                new UTF8Encoding(false));
        }

        private static void EnsureGeneratedFolders()
        {
            EnsureFolder(MaterialsRoot);
            EnsureFolder(PackedMapsRoot);
            EnsureFolder(PrefabsRoot);
            EnsureFolder(ScenesRoot);
            EnsureFolder(PreviewsRoot);
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

        private sealed class AssetSpec
        {
            private AssetSpec(
                string assetId,
                string sourceFolder,
                float targetLargestDimension,
                int maximumTextureSize,
                float wrapperYaw,
                bool autoUpright)
            {
                AssetId = assetId;
                SourceFolder = sourceFolder;
                TargetLargestDimension = targetLargestDimension;
                MaximumTextureSize = maximumTextureSize;
                WrapperYaw = wrapperYaw;
                AutoUpright = autoUpright;
            }

            public string AssetId { get; private set; }
            public string SourceFolder { get; private set; }
            public float TargetLargestDimension { get; private set; }
            public int MaximumTextureSize { get; private set; }
            public float WrapperYaw { get; private set; }
            public bool AutoUpright { get; private set; }
            public string ModelPath
            {
                get { return SourceFolder + "/Models/" + AssetId + ".fbx"; }
            }
            public string TextureRoot
            {
                get { return SourceFolder + "/Textures"; }
            }
            public string PrefabName
            {
                get { return "PF_EquipmentShop_" + AssetId; }
            }
            public string PrefabPath
            {
                get { return PrefabsRoot + "/" + PrefabName + ".prefab"; }
            }

            public static AssetSpec Character(
                string assetId,
                float targetLargestDimension,
                int maximumTextureSize,
                float wrapperYaw)
            {
                return new AssetSpec(
                    assetId,
                    "Assets/_SashimiBoy/Art/Source/Characters/" + assetId,
                    targetLargestDimension,
                    maximumTextureSize,
                    wrapperYaw,
                    true);
            }

            public static AssetSpec Furniture(
                string assetId,
                float targetLargestDimension,
                int maximumTextureSize)
            {
                return new AssetSpec(
                    assetId,
                    "Assets/_SashimiBoy/Art/Source/Environment/" +
                    "EquipmentShop/Furniture/" + assetId,
                    targetLargestDimension,
                    maximumTextureSize,
                    0f,
                    false);
            }

            public static AssetSpec Equipment(
                string assetId,
                float targetLargestDimension,
                int maximumTextureSize)
            {
                return new AssetSpec(
                    assetId,
                    "Assets/_SashimiBoy/Art/Source/Environment/" +
                    "EquipmentShop/Equipment/" + assetId,
                    targetLargestDimension,
                    maximumTextureSize,
                    0f,
                    false);
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

        private sealed class AssetBuildResult
        {
            public AssetBuildResult(
                AssetSpec spec,
                int textureSetCount,
                int materialCount,
                int fallbackSlots,
                Vector3 finalSize)
            {
                Spec = spec;
                TextureSetCount = textureSetCount;
                MaterialCount = materialCount;
                FallbackSlots = fallbackSlots;
                FinalSize = finalSize;
            }

            public AssetSpec Spec { get; private set; }
            public int TextureSetCount { get; private set; }
            public int MaterialCount { get; private set; }
            public int FallbackSlots { get; private set; }
            public Vector3 FinalSize { get; private set; }
        }

        private struct TransformState
        {
            public TransformState(Transform transform)
            {
                Position = transform.position;
                Rotation = transform.rotation;
                LocalScale = transform.localScale;
            }

            private Vector3 Position { get; set; }
            private Quaternion Rotation { get; set; }
            private Vector3 LocalScale { get; set; }

            public bool Matches(Transform transform)
            {
                return transform.position == Position &&
                    transform.rotation == Rotation &&
                    transform.localScale == LocalScale;
            }
        }
    }
}
