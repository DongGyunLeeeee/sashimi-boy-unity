using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    public static partial class Stage01PlayableAuthoring
    {
        public const string Root = "Assets/_SashimiBoy/Art/Generated/Stage01Playable";
        public const string ScenePath = "Assets/_SashimiBoy/Scenes/Stage01_Salmon.unity";
        private const string Source = "Assets/_SashimiBoy/Art/Source/Stage01/SalmonButchery/";
        private const string Shared = "Assets/_SashimiBoy/Art/Source/Shared/FishButchery/";
        private const string AssemblyPath = "Assets/_SashimiBoy/Art/Generated/Prefabs/Stage01/SalmonButchery/PF_Stage01_SalmonAssembly.prefab";

        public static void InspectSourcesBatch()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string path in new[] { "Assets/_SashimiBoy/Art/Source/Shared/Hands/DevMops/arms_low_poly.fbx",
                Source + "FilletHalf/fillet_half.fbx", Source + "SashimiSlice/salmonpiece.fbx" })
            {
                ConfigureModel(path);
                var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                Debug.Log("[SourceInspect] " + path + " bounds=" + GeometryBounds(model));
                foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
                {
                    var v = filter.sharedMesh.vertices;
                    Debug.Log("[SourceInspect] mesh=" + filter.name + " vertices=" + v.Length + " meshBounds=" + filter.sharedMesh.bounds +
                        " rotation=" + filter.transform.eulerAngles + " scale=" + filter.transform.lossyScale);
                    File.WriteAllText("Logs/Stage1Revision/" + Path.GetFileNameWithoutExtension(path) + "-unity-vertices.json",
                        "[" + string.Join(",", v.Select(p => JsonUtility.ToJson(p))) + "]");
                }
                Object.DestroyImmediate(model);
            }
        }

        [MenuItem("Sashimi Boy/Stage 01/Apply Playable Stage1")]
        public static void ApplyPlayableStageBatch()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play mode before authoring.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                Require(!SceneManager.GetSceneAt(i).isDirty, "Save unsaved scenes before authoring.");
            Directory.CreateDirectory(Root);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            SalmonButcheryArtPipeline.BuildSalmonAssemblyBatch();
            BuildAdditionalAssets();
            KevinEmbodimentAuthoring.BuildBody();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ApplyToStageScene(scene);
            ScanScene(scene);
            Require(EditorSceneManager.SaveScene(scene), "Stage1 scene save failed.");
            NormalizeSceneWhitespace();
            Debug.Log("[Stage1Playable] Applied real salmon, semantic visuals, plate and return flow to " + ScenePath);
        }

        // Called by the existing authoritative presentation generator after it builds its base scene.
        public static void ReapplyIfAuthored(Scene scene)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/PF_SashimiSlice.prefab") != null)
                ApplyToStageScene(scene);
        }

        private static void BuildAdditionalAssets()
        {
            BuildModelPrefab("SashimiSlice", Source + "SashimiSlice/salmonpiece.fbx",
                Source + "SashimiSlice/salmonpiece", .64f, false);
            BuildModelPrefab("Tweezers", Shared + "Tweezers/curved_tweezer.fbx",
                Shared + "Tweezers/curved_tweezer_3d_model", .65f, false);
            BuildModelPrefab("FilletHalf", Source + "FilletHalf/fillet_half.fbx",
                Source + "FilletHalf/fillet_half", 2.45f, true);
            BuildGripHandAsset();
            Material blur = GetMaterial("FailureBlur", "SashimiBoy/Stage01FailureBlur");
            AssetDatabase.SaveAssetIfDirty(blur);
        }

        private static void ConfigureModel(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            Require(importer != null, "Missing model " + path);
            bool changed = importer.importAnimation || importer.importCameras || importer.importLights ||
                importer.materialImportMode != ModelImporterMaterialImportMode.None || !importer.isReadable ||
                importer.indexFormat != ModelImporterIndexFormat.UInt32;
            importer.importAnimation = importer.importCameras = importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.isReadable = true; // Fixed cap authoring reads the approved mesh; source bytes stay intact.
            importer.indexFormat = ModelImporterIndexFormat.UInt32;
            if (changed) importer.SaveAndReimport();
        }

        private static Texture2D Texture(string path, bool normal = false, bool color = true)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Require(importer != null, "Missing texture " + path);
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            bool changed = importer.textureType != type || importer.textureShape != TextureImporterShape.Texture2D ||
                importer.sRGBTexture != color || importer.maxTextureSize != 2048;
            importer.textureType = type;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = color;
            importer.maxTextureSize = 2048;
            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && !importer.alphaIsTransparency)
            { importer.alphaIsTransparency = true; changed = true; }
            if (changed) importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Require(texture != null, "Expected a 2D texture: " + path);
            return texture;
        }

        private static GameObject BuildModelPrefab(string id, string modelPath, string textureBase, float length, bool cap)
        {
            ConfigureModel(modelPath);
            Material mat = GetMaterial(id, cap ? "SashimiBoy/Stage01FixedFilletSurface" : "Standard");
            mat.mainTexture = Texture(textureBase + "_basecolor.JPEG");
            mat.SetTexture("_BumpMap", Texture(textureBase + "_normal.JPEG", true, false));
            mat.EnableKeyword("_NORMALMAP");
            mat.SetFloat("_Glossiness", .25f);
            mat.enableInstancing = true;
            // Separate Metallic/Roughness are the documented channels. RM remains unbound.
            mat.SetTexture("_MetallicGlossMap", BuildPackedMap(id, textureBase));
            mat.EnableKeyword("_METALLICGLOSSMAP");
            mat.SetFloat("_GlossMapScale", 1f);
            if (id != "Tweezers")
            {
                mat.DisableKeyword("_METALLICGLOSSMAP"); mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_BumpScale", .35f); mat.SetFloat("_GlossMapScale", .35f);
            }
            AssetDatabase.SaveAssetIfDirty(mat);
            GameObject wrapper = new GameObject("PF_" + id);
            try
            {
                var imported = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                Require(imported != null, "Model not imported: " + modelPath);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(imported, wrapper.transform);
                // The supplied half stands on its narrow edge and has a 12-degree longitudinal lean.
                // Lay its broad side on the board before deriving a single fixed cut surface.
                if (cap || id == "SashimiSlice") model.transform.rotation = Quaternion.Euler(90f, 0f, 0f) *
                    Quaternion.Euler(0f, 12f, 0f) * model.transform.rotation;
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                Require(renderers.Length > 0, "Empty model: " + modelPath);
                Bounds bounds = GeometryBounds(wrapper);
                float scale = length / bounds.size.x;
                model.transform.localScale *= scale;
                model.transform.localPosition = new Vector3(-bounds.center.x * scale, -bounds.min.y * scale, -bounds.center.z * scale);
                foreach (var renderer in renderers)
                    renderer.sharedMaterials = Enumerable.Repeat(mat, renderer.sharedMaterials.Length).ToArray();
                if (cap) BuildFixedCap(wrapper, renderers);
                return PrefabUtility.SaveAsPrefabAsset(wrapper, Root + "/PF_" + id + ".prefab");
            }
            finally { Object.DestroyImmediate(wrapper); }
        }

        private static Texture2D BuildPackedMap(string id, string textureBase)
        {
            string metalPath = textureBase + "_metallic.JPEG", roughPath = textureBase + "_roughness.JPEG";
            Texture(metalPath, false, false); Texture(roughPath, false, false);
            var mi = (TextureImporter)AssetImporter.GetAtPath(metalPath);
            var ri = (TextureImporter)AssetImporter.GetAtPath(roughPath);
            bool mr = mi.isReadable, rr = ri.isReadable;
            Texture2D output = null;
            try
            {
                mi.isReadable = ri.isReadable = true; mi.SaveAndReimport(); ri.SaveAndReimport();
                var metal = AssetDatabase.LoadAssetAtPath<Texture2D>(metalPath);
                var rough = AssetDatabase.LoadAssetAtPath<Texture2D>(roughPath);
                const int size = 1024;
                output = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    float u = (x + .5f) / size, v = (y + .5f) / size;
                    pixels[y * size + x] = new Color(metal.GetPixelBilinear(u, v).r, 0f, 0f, 1f - rough.GetPixelBilinear(u, v).r);
                }
                output.SetPixels32(pixels); output.Apply();
                string path = Root + "/MS_" + id + ".png";
                File.WriteAllBytes(path, output.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                return Texture(path, false, false);
            }
            finally
            {
                if (output != null) Object.DestroyImmediate(output);
                mi.isReadable = mr; ri.isReadable = rr; mi.SaveAndReimport(); ri.SaveAndReimport();
            }
        }

        private static void BuildFixedCap(GameObject wrapper, Renderer[] renderers)
        {
            BuildSliceSurfaces(wrapper, renderers);
        }

        private static Bounds GeometryBounds(GameObject root)
        {
            Bounds bounds = default;
            bool found = false;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                foreach (var vertex in filter.sharedMesh.vertices)
                {
                    var point = root.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                    else bounds.Encapsulate(point);
                }
            Require(found, "Model has no mesh vertices: " + root.name);
            return bounds;
        }

        private static List<Vector2> Hull(List<Vector2> points)
        {
            var ordered = points.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToList();
            var result = new List<Vector2>();
            foreach (var p in ordered)
            {
                while (result.Count >= 2 && Cross(result[result.Count - 2], result[result.Count - 1], p) <= 0f) result.RemoveAt(result.Count - 1);
                result.Add(p);
            }
            int lower = result.Count;
            for (int i = ordered.Count - 2; i >= 0; i--)
            {
                var p = ordered[i];
                while (result.Count > lower && Cross(result[result.Count - 2], result[result.Count - 1], p) <= 0f) result.RemoveAt(result.Count - 1);
                result.Add(p);
            }
            if (result.Count > 1) result.RemoveAt(result.Count - 1);
            return result;
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        private static void ApplyToStageScene(Scene scene)
        {
            RestoreCookingWorkspace(scene);
            var timing = Find<Stage01SalmonTimingScaffold>(scene);
            Require(timing != null && timing.semanticBeatmap != null, "Apply the approved saved phase chart first; no runtime fallback is supplied.");
            Require(timing.ValidatePatternSource().IsValid, "Existing Stage1 timing source is invalid.");
            timing.notePatternProvider.Initialize(timing);
            Require(timing.semanticBeatmap.MatchesSource(timing), "Saved note timing must still match before the Owner's action-only migration.");
            if (SashimiBoy.Semantics.ApprovedPhaseChart.ApplyPlayableActionSequence(timing.semanticBeatmap.chart))
            {
                EditorUtility.SetDirty(timing.semanticBeatmap);
                AssetDatabase.SaveAssetIfDirty(timing.semanticBeatmap);
            }
            Require(SashimiBoy.Semantics.SemanticChartValidator.Validate(timing.semanticBeatmap.chart, timing.notePatternProvider.RuntimeNotes.Count).IsValid &&
                timing.semanticBeatmap.MatchesSource(timing), "Saved chart must match the unchanged Stage1 note schedule.");
            var presentation = Find<Stage01SalmonPresentationController>(scene);
            Require(presentation != null && presentation.playerKnife != null && presentation.hud != null, "Existing Stage1 presentation is incomplete.");
            GameObject root = scene.GetRootGameObjects().FirstOrDefault(o => o.name == "Stage01_Playable");
            if (root != null) Object.DestroyImmediate(root);
            root = new GameObject("Stage01_Playable");
            SceneManager.MoveGameObjectToScene(root, scene);
            var view = root.AddComponent<Stage01ButcheryPresenter>();
            view.timing = timing;
            GameObject salmon = Instance(AssemblyPath, root.transform, "SalmonAssembly");
            salmon.transform.SetPositionAndRotation(new Vector3(-.55f, .45f, .1f), Quaternion.Euler(0f, 270f, 0f));
            salmon.transform.localScale = Vector3.one * 1.4f;
            view.assembly = salmon.GetComponent<SalmonAssemblyView>();
            view.filletHalf = Instance(Root + "/PF_FilletHalf.prefab", root.transform, "FilletHalf");
            view.filletHalf.transform.position = new Vector3(-.55f, .6f, -.15f);
            view.filletHalf.SetActive(false);
            view.slicePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/PF_SashimiSlice.prefab");
            view.sliceRoot = Child(root.transform, "PlatedSlices");
            view.wasteAnchor = Child(root.transform, "RemovedPartsRest");
            view.wasteAnchor.position = new Vector3(-3.6f, .6f, 1.3f);
            view.tweezersRoot = Instance(Root + "/PF_Tweezers.prefab", root.transform, "Tweezers").transform;
            view.tweezersRoot.rotation = Quaternion.Euler(0f, 90f, 0f);
            view.knifeRoot = presentation.playerKnife.transform;
            presentation.playerKnife.enabled = false;
            if (presentation.salmon != null) presentation.salmon.gameObject.SetActive(false);
            presentation.butchery = view;
            presentation.sliceCue.butchery = view;
            presentation.hud.butchery = view;
            view.handRoot = BuildHand(scene, root.transform);
            Transform plate = All<Transform>(scene).FirstOrDefault(t => t.name == "CompletedFishPlate");
            Require(plate != null, "Existing serving plate is missing.");
            plate.position = new Vector3(3.35f, .35f, .55f);
            plate.localScale = new Vector3(2.25f, .04f, 1.45f);
            view.plateSlots = new Transform[12];
            var slots = Child(root.transform, "PlateSlots");
            for (int i = 0; i < view.plateSlots.Length; i++)
            {
                var slot = Child(slots, "SliceSlot_" + i.ToString("00"));
                slot.position = plate.position + new Vector3(-.88f + i % 6 * .34f, .065f, -.32f + i / 6 * .63f);
                slot.rotation = Quaternion.Euler(0f, -15f, 0f);
                view.plateSlots[i] = slot;
            }
            ApplyOwnerVisualRevision(scene, view, presentation);
            ApplyCookingWorkspace(scene, view, presentation);
            CalibrateOpenFish(view);
            var camera = Find<Camera>(scene);
            var effect = camera.GetComponent<Stage01FailureEffect>() ?? camera.gameObject.AddComponent<Stage01FailureEffect>();
            effect.blurMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/MAT_FailureBlur.mat");
            var flow = root.AddComponent<Stage01PlayableFlow>();
            flow.timing = timing; flow.hud = presentation.hud; flow.failureEffect = effect;
            Canvas canvas = presentation.hud.GetComponent<Canvas>();
            flow.failureText = TextObject(canvas.transform, "Stage1FailureLabel", "", new Vector2(0f, 0f), new Vector2(760f, 180f), 38);
            flow.failureText.gameObject.SetActive(false);
            flow.retryButton = ButtonObject(presentation.hud.resultRoot.transform, "Stage1Retry", "R · 다시 손질", new Vector2(-145f, -170f));
            flow.returnButton = ButtonObject(presentation.hud.resultRoot.transform, "Stage1Return", "F · 횟집으로", new Vector2(145f, -170f));
            EditorUtility.SetDirty(timing); EditorUtility.SetDirty(presentation); EditorUtility.SetDirty(presentation.sliceCue);
            EditorUtility.SetDirty(presentation.playerKnife); EditorUtility.SetDirty(presentation.hud);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void PlaceBonesOnFillet(SalmonAssemblyView assembly)
        {
            // Derive visible contact points from the approved fillet mesh in this scene instance.
            // Canonical prefab anchors/GUIDs remain reusable; CaptureAuthoredPose preserves retry.
            var colliders = new List<MeshCollider>();
            try
            {
                foreach (var filter in assembly.fillet.GetComponentsInChildren<MeshFilter>(true))
                {
                    var collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    colliders.Add(collider);
                }
                Physics.SyncTransforms();
                for (int i = 0; i < assembly.pinBones.Length; i++)
                {
                    var bone = assembly.pinBones[i];
                    Vector3 point = bone.transform.position;
                    float height = float.NegativeInfinity;
                    foreach (var collider in colliders)
                        if (collider.Raycast(new Ray(point + Vector3.up * 5f, Vector3.down), out var hit, 10f))
                            height = Mathf.Max(height, hit.point.y);
                    Require(float.IsFinite(height), "PinBone has no fillet surface below it: " + i);
                    point.y = height + .015f;
                    bone.transform.position = point;
                    bone.CaptureAuthoredPose();
                    assembly.pinBoneAnchors[i].position = point;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(bone.transform);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(bone);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(assembly.pinBoneAnchors[i]);
                }
            }
            finally { foreach (var collider in colliders) Object.DestroyImmediate(collider); }
        }

        private static Transform BuildHand(Scene scene, Transform parent) =>
            Instance(Root + "/PF_KnifeGripHand.prefab", parent, "PlayerHand_DevMops").transform;

        private static Text TextObject(Transform parent, string name, string value, Vector2 position, Vector2 size, int fontSize)
        {
            Transform existing = parent.Find(name);
            GameObject obj = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(parent, false);
            var text = obj.GetComponent<Text>();
            var canvas = parent.GetComponentInParent<Canvas>();
            var fontSource = canvas != null ? canvas.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t != text && t.font != null) : null;
            text.font = fontSource != null ? fontSource.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize; text.alignment = TextAnchor.MiddleCenter; text.color = Color.white;
            text.text = value; text.raycastTarget = false;
            var rect = text.rectTransform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position; rect.sizeDelta = size;
            return text;
        }

        private static Button ButtonObject(Transform parent, string name, string label, Vector2 position)
        {
            Transform old = parent.Find(name);
            GameObject obj = old != null ? old.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(parent, false);
            var rect = (RectTransform)obj.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position; rect.sizeDelta = new Vector2(260f, 60f);
            obj.GetComponent<Image>().color = new Color(.12f, .25f, .29f, 1f);
            TextObject(obj.transform, "Label", label, Vector2.zero, rect.sizeDelta, 22);
            return obj.GetComponent<Button>();
        }

        public static void ScanScene(Scene scene)
        {
            foreach (Transform transform in All<Transform>(scene))
            {
                Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0, "Missing Script: " + transform.name);
                foreach (var component in transform.GetComponents<Component>())
                {
                    if (component == null) continue;
                    var serialized = new SerializedObject(component); var property = serialized.GetIterator();
                    while (property.NextVisible(true))
                        if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == null && !property.objectReferenceEntityIdValue.Equals(default(UnityEngine.EntityId)))
                            throw new InvalidOperationException("Broken reference: " + transform.name + "/" + property.propertyPath);
                }
            }
            Require(All<AudioListener>(scene).Count(x => x.isActiveAndEnabled) == 1, "Expected one active AudioListener.");
            Require(All<EventSystem>(scene).Count(x => x.isActiveAndEnabled) == 1, "Expected one active EventSystem.");
            foreach (Renderer renderer in All<Renderer>(scene))
                foreach (Material material in renderer.sharedMaterials) Require(material != null, "Missing material: " + renderer.name);
            Debug.Log("[Stage1Playable] Scene references, materials, AudioListener and EventSystem scanned.");
        }

        public static void BuildWindowsBatch()
        {
            NormalizeSceneWhitespace();
            string output = Path.GetFullPath("Builds/Stage1Validation-" +
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "/SashimiBoyStage1.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            // This focused validation build starts at Stage1. The ordinary shop scene remains
            // included for failure/return/reentry; production build settings are unchanged.
            scenes.Remove(ScenePath); scenes.Insert(0, ScenePath);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes.ToArray(), locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
            Require(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded, "Stage1 validation build failed: " + report.summary.result);
            Debug.Log("[Stage1Playable] Stage1 validation build: " + output);
        }

        internal static void NormalizeSceneWhitespace()
        {
            // Unity writes a trailing space for empty serialized strings on new components.
            // Normalize only this authored scene's line endings; serialized values/GUIDs stay intact.
            string source = File.ReadAllText(ScenePath);
            string normalized = System.Text.RegularExpressions.Regex.Replace(source, @"(?m)[ \t]+(?=\r?$)", "");
            if (source != normalized) File.WriteAllText(ScenePath, normalized, new System.Text.UTF8Encoding(false));
        }

        private static Material GetMaterial(string name, string shader)
        {
            string path = Root + "/MAT_" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader source = Shader.Find(shader); Require(source != null, "Missing shader " + shader);
            if (material == null) { material = new Material(source); AssetDatabase.CreateAsset(material, path); }
            else material.shader = source;
            EditorUtility.SetDirty(material);
            return material;
        }
        private static GameObject Instance(string path, Transform parent, string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path); Require(prefab != null, "Missing prefab " + path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent); instance.name = name; return instance;
        }
        private static Transform Child(Transform parent, string name) { var child = new GameObject(name).transform; child.SetParent(parent, false); return child; }
        private static T Find<T>(Scene scene) where T : Component => All<T>(scene).FirstOrDefault();
        private static IEnumerable<T> All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true));
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
