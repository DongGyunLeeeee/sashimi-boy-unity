using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    public static partial class DayWorldAssetAuthoring
    {
        public static void InspectHomeModelsBatch()
        {
            Directory.CreateDirectory("Logs/DayWorld/HomeAssetRevision");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            PrepareHomeTextures();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            InspectHomeModel("HomeBed", "Bed/rusty_metal_bed.fbx");
            InspectHomeModel("HomeDoor", "Door/rusty_metal_door.fbx");
            InspectHomeModel("HomeExterior", "Exterior/shipping_container.fbx");
            AssetDatabase.SaveAssets();
        }

        static void PrepareHomeTextures()
        {
            foreach (string file in Directory.GetFiles(Source + "/KevinHome", "*.JPEG", SearchOption.AllDirectories))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(file.Replace('\\', '/'));
                var type = file.Contains("_normal") ? TextureImporterType.NormalMap : TextureImporterType.Default;
                bool srgb = file.Contains("_basecolor"), readable = file.Contains("_metallic") || file.Contains("_roughness");
                if (importer.maxTextureSize == 1024 && importer.textureType == type &&
                    importer.sRGBTexture == srgb && importer.isReadable == readable) continue;
                importer.maxTextureSize = 1024;
                importer.textureType = type;
                importer.sRGBTexture = srgb;
                importer.isReadable = readable;
                importer.SaveAndReimport();
            }
        }

        [MenuItem("Sashimi Boy/Day 01 + Day 02/Build Owner Home Prefabs")]
        public static void BuildHomeModels()
        {
            Directory.CreateDirectory(Output);
            Directory.CreateDirectory("Logs/DayWorld/HomeAssetRevision");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            PrepareHomeTextures();
            BuildHomeWrapper("HomeBed", "Bed/rusty_metal_bed.fbx", 90f, 2.35f, true);
            BuildHomeWrapper("HomeDoor", "Door/rusty_metal_door.fbx", -60f, 2f, false);
            BuildHomeWrapper("HomeExterior", "Exterior/shipping_container.fbx", 90f, 3f, false);
            AssetDatabase.SaveAssets();
        }

        static void BuildHomeWrapper(string id, string relativePath, float yaw, float size, bool fitLength)
        {
            string path = Source + "/KevinHome/" + relativePath;
            var root = new GameObject("PF_DayWorld_" + id);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), root.transform);
                BindMaterials(model, path, id);
                // Retain the FBX's imported up-axis conversion and rotate only the project instance.
                model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * model.transform.localRotation;
                var bounds = HomeGeometryBounds(model);
                model.transform.localScale *= size / (fitLength ? Mathf.Max(bounds.size.x, bounds.size.z) : bounds.size.y);
                bounds = HomeGeometryBounds(model);
                model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                if (id == "HomeExterior")
                {
                    // Keep the multi-million-triangle render mesh out of PhysX. The closed buildings need two solid volumes;
                    // their recessed courtyard stays walkable and the existing SceneDoor remains in front of the rear volume.
                    HomeBlocker(root.transform, "MainBuildingCollision", new Vector3(0f, 1.5f, -1.815f), new Vector3(8.08f, 3f, 5.23f));
                    HomeBlocker(root.transform, "SideWingCollision", new Vector3(-2.745f, 1.5f, 2.615f), new Vector3(2.59f, 3f, 3.63f));
                    // Measured from the supplied recessed opening, not the outer bounds of its L-shaped courtyard.
                    var entry = new GameObject("EntryAnchor").transform;
                    entry.SetParent(root.transform, false);
                    entry.localPosition = new Vector3(.2725f, .24f, .78f);
                }
                PrefabUtility.SaveAsPrefabAsset(root, Output + "/PF_" + id + ".prefab");
                Debug.Log("[HomeAsset] " + id + " assembled geometry " + HomeGeometryBounds(root));
                RenderHomeInspection(root, "Logs/DayWorld/HomeAssetRevision/" + id + "-assembled.png");
                if (id != "HomeBed")
                    for (int angle = 0; angle < 360; angle += 90)
                    {
                        root.transform.rotation = Quaternion.Euler(0f, angle, 0f);
                        RenderHomeInspection(root, "Logs/DayWorld/HomeAssetRevision/" + id + "-assembled-yaw-" + angle + ".png", true);
                    }
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void HomeBlocker(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var collider = new GameObject(name).AddComponent<BoxCollider>();
            collider.transform.SetParent(parent, false);
            collider.center = center;
            collider.size = size;
        }

        public static Bounds HomeGeometryBounds(GameObject model)
        {
            // A rotated source mesh's renderer AABB is too loose for fitting a door into its existing opening.
            var result = new Bounds();
            bool first = true;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
                foreach (var vertex in filter.sharedMesh.vertices)
                {
                    Vector3 point = filter.transform.TransformPoint(vertex);
                    if (first) { result = new Bounds(point, Vector3.zero); first = false; }
                    else result.Encapsulate(point);
                }
            if (first) throw new System.InvalidOperationException("No owner home geometry: " + model.name);
            return result;
        }

        static void InspectHomeModel(string id, string relativePath)
        {
            string path = Source + "/KevinHome/" + relativePath;
            var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            try
            {
                BindMaterials(model, path, id);
                Debug.Log("[HomeAsset] " + id + " raw " + BoundsOf(model));
                foreach (int pitch in new[] { 0, 90, -90 })
                {
                    model.transform.rotation = Quaternion.Euler(pitch, 0f, 0f);
                    Debug.Log("[HomeAsset] " + id + " pitch " + pitch + " " + BoundsOf(model));
                    RenderHomeInspection(model, "Logs/DayWorld/HomeAssetRevision/" + id + "-pitch-" + pitch + ".png");
                }
            }
            finally { Object.DestroyImmediate(model); }
        }

        static void RenderHomeInspection(GameObject model, string path, bool front = false)
        {
            var bounds = BoundsOf(model);
            var camera = new GameObject("HomeAssetInspectionCamera").AddComponent<Camera>();
            var light = new GameObject("HomeAssetInspectionLight").AddComponent<Light>();
            var target = new RenderTexture(1200, 800, 24);
            var output = new Texture2D(1200, 800, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            var oldAmbientMode = RenderSettings.ambientMode;
            var oldAmbient = RenderSettings.ambientLight;
            try
            {
                camera.orthographic = true;
                camera.orthographicSize = bounds.extents.magnitude * .85f;
                camera.transform.position = bounds.center + (front ? Vector3.forward : new Vector3(1.2f, .9f, 2f).normalized) * bounds.size.magnitude * 2f;
                camera.transform.LookAt(bounds.center);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.12f, .14f, .16f);
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(45f, -140f, 0f);
                light.intensity = .8f;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.35f, .35f, .35f);
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                output.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0);
                output.Apply();
                File.WriteAllBytes(path, output.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = old;
                RenderSettings.ambientMode = oldAmbientMode;
                RenderSettings.ambientLight = oldAmbient;
                camera.targetTexture = null;
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(light.gameObject);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(output);
            }
        }
    }
}
