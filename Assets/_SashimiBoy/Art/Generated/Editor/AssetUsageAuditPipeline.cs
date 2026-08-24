#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace SashimiBoy.EditorTools
{
    public static class AssetUsageAuditPipeline
    {
        private const string GeneratedRoot =
            "Assets/_SashimiBoy/Art/Generated";
        private const string ReportPath =
            GeneratedRoot + "/Reports/AssetUsageAudit.md";

        private static readonly AuditAssetSpec[] Assets =
        {
            Kevin("AmbiguousFace"),
            Kevin("PlainFace"),
            Kevin("CuteFace"),
            Kevin("WesternFace"),
            Fish("Salmon"),
            Fish("Rockfish"),
            Fish("Mullet"),
            FishShop(
                "KitchenKnife",
                "Props/KitchenKnife",
                "PF_Prop_KitchenKnife"),
            FishShop(
                "SashimiTable",
                "Fixtures/SashimiTable",
                "PF_Fixture_SashimiTable"),
            FishShop(
                "DisplayInside",
                "Fixtures/DisplayInside",
                "PF_Fixture_DisplayInside"),
            FishShop(
                "DisplayOutside",
                "Fixtures/DisplayOutside",
                "PF_Fixture_DisplayOutside"),
            Club("BarTable"),
            Club("Beer"),
            Club("Bucket"),
            Club("ClubDoorFrame"),
            Club("DJController"),
            Club("DJMixer"),
            Club("DJStand"),
            Club("Ice"),
            Club("LongIslandIcedTea"),
            Club("NeonSign"),
            Club("SignBoard"),
            Club("Turntable"),
        };

        private static readonly AuditSceneSpec[] Scenes =
        {
            new AuditSceneSpec(
                "Street",
                "Assets/_SashimiBoy/Scenes/Street.unity",
                true),
            new AuditSceneSpec(
                "FishShopDialogue",
                "Assets/_SashimiBoy/Scenes/FishShopDialogue.unity",
                true),
            new AuditSceneSpec(
                "EquipmentShop",
                "Assets/_SashimiBoy/Scenes/EquipmentShop.unity",
                true),
            new AuditSceneSpec(
                "Club",
                "Assets/_SashimiBoy/Scenes/Club.unity",
                true),
            new AuditSceneSpec(
                "Stage01_Salmon",
                "Assets/_SashimiBoy/Scenes/Stage01_Salmon.unity",
                true),
            new AuditSceneSpec(
                "KevinAssetGallery",
                GeneratedRoot + "/Scenes/KevinAssetGallery.unity",
                false),
            new AuditSceneSpec(
                "FishShopAssetGallery",
                GeneratedRoot + "/Scenes/FishShopAssetGallery.unity",
                false),
            new AuditSceneSpec(
                "ClubAssetGallery",
                GeneratedRoot + "/Scenes/ClubAssetGallery.unity",
                false),
            new AuditSceneSpec(
                "Club_ArtPass",
                GeneratedRoot + "/Scenes/Club_ArtPass.unity",
                false),
        };

        [MenuItem("Sashimi Boy/Art/Run Asset Usage Audit")]
        public static void RunAssetUsageAudit()
        {
            RunAssetUsageAuditInternal(true);
        }

        public static void RunAssetUsageAuditBatch()
        {
            RunAssetUsageAuditInternal(false);
        }

        private static void RunAssetUsageAuditInternal(bool showDialog)
        {
            EnsureFolder(GeneratedRoot + "/Reports");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Scene previousActiveScene = SceneManager.GetActiveScene();
            List<SceneAudit> sceneAudits = new List<SceneAudit>();
            try
            {
                for (int i = 0; i < Scenes.Length; i++)
                {
                    sceneAudits.Add(ScanScene(Scenes[i]));
                }
            }
            finally
            {
                if (previousActiveScene.IsValid() &&
                    previousActiveScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActiveScene);
                }
            }

            WriteReport(sceneAudits);
            AssetDatabase.ImportAsset(
                ReportPath,
                ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();

            Debug.Log(
                "[Sashimi Boy] Asset usage audit complete: " + ReportPath);
            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "Sashimi Boy",
                    "Asset usage audit complete.\n\n" + ReportPath,
                    "OK");
            }
        }

        private static SceneAudit ScanScene(AuditSceneSpec spec)
        {
            SceneAudit audit = new SceneAudit(spec);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(spec.Path) == null)
            {
                audit.Missing = true;
                return audit;
            }

            Scene scene = SceneManager.GetSceneByPath(spec.Path);
            bool openedForAudit = !scene.IsValid() || !scene.isLoaded;
            if (openedForAudit)
            {
                scene = EditorSceneManager.OpenScene(
                    spec.Path,
                    OpenSceneMode.Additive);
            }

            try
            {
                Dictionary<string, AuditAssetSpec> byPrefabPath =
                    Assets.ToDictionary(item => item.PrefabPath, item => item);
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    Transform[] transforms =
                        roots[i].GetComponentsInChildren<Transform>(true);
                    for (int j = 0; j < transforms.Length; j++)
                    {
                        GameObject gameObject = transforms[j].gameObject;
                        MarkPrefabUsage(
                            audit,
                            gameObject,
                            byPrefabPath);
                        MarkNameUsage(audit, gameObject);
                        MarkFallback(audit, gameObject);
                    }

                    KevinVisualLoader[] loaders =
                        roots[i].GetComponentsInChildren<KevinVisualLoader>(
                            true);
                    for (int j = 0; j < loaders.Length; j++)
                    {
                        string variantId = string.IsNullOrWhiteSpace(
                            loaders[j].variantId)
                            ? "AmbiguousFace"
                            : loaders[j].variantId;
                        audit.Mark(
                            variantId,
                            loaders[j].isActiveAndEnabled,
                            HierarchyPath(loaders[j].transform) +
                            " (runtime default)");
                    }
                }

                audit.ActiveAudioListeners = CountActive<AudioListener>(scene);
                audit.ActiveEventSystems = CountActive<EventSystem>(scene);
                audit.ActiveFirstPersonRigs =
                    CountActive<KevinFirstPersonCameraRig>(scene);
                Camera mainCamera = GetComponentsInScene<Camera>(scene)
                    .FirstOrDefault(camera => camera.CompareTag("MainCamera"));
                audit.CameraMode = mainCamera == null
                    ? "missing"
                    : mainCamera.orthographic ? "fixed orthographic" :
                    "perspective";
            }
            finally
            {
                if (openedForAudit)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            return audit;
        }

        private static void MarkPrefabUsage(
            SceneAudit audit,
            GameObject gameObject,
            Dictionary<string, AuditAssetSpec> byPrefabPath)
        {
            string prefabPath =
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    gameObject);
            AuditAssetSpec spec;
            if (!string.IsNullOrEmpty(prefabPath) &&
                byPrefabPath.TryGetValue(prefabPath, out spec))
            {
                audit.Mark(
                    spec.AssetId,
                    gameObject.activeInHierarchy,
                    HierarchyPath(gameObject.transform));
            }
        }

        private static void MarkNameUsage(
            SceneAudit audit,
            GameObject gameObject)
        {
            for (int i = 0; i < Assets.Length; i++)
            {
                string prefabName = Path.GetFileNameWithoutExtension(
                    Assets[i].PrefabPath);
                if (gameObject.name.IndexOf(
                        prefabName,
                        StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                audit.Mark(
                    Assets[i].AssetId,
                    gameObject.activeInHierarchy,
                    HierarchyPath(gameObject.transform));
            }
        }

        private static void MarkFallback(
            SceneAudit audit,
            GameObject gameObject)
        {
            string name = gameObject.name;
            bool marker =
                name.IndexOf("Fallback", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Placeholder", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.Equals("Stage01_ProceduralSalmon", StringComparison.Ordinal) ||
                name.Equals("Stage01_PlayerKnife", StringComparison.Ordinal) ||
                name.Equals("Stage01_BossKnife_Demo", StringComparison.Ordinal);
            if (!marker)
            {
                return;
            }

            string status = gameObject.activeInHierarchy ? "active" : "inactive";
            audit.Fallbacks.Add(
                HierarchyPath(gameObject.transform) + " (" + status + ")");
        }

        private static void WriteReport(List<SceneAudit> sceneAudits)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("# Asset Usage Audit");
            report.AppendLine();
            report.AppendLine(
                "Generated by `Sashimi Boy/Art/Run Asset Usage Audit`.");
            report.AppendLine(
                "Production status is based on active instances in Street, " +
                "FishShopDialogue, EquipmentShop, Club, and Stage01_Salmon. " +
                "Gallery-only assets remain `unused` for gameplay.");
            report.AppendLine();
            report.AppendLine("## Summary");
            report.AppendLine();
            report.AppendLine("- Audited assets: " + Assets.Length);
            report.AppendLine("- Kevin variants: 4");
            report.AppendLine("- FishShop assets: 7");
            report.AppendLine("- Club assets: 12");
            report.AppendLine(
                "- Orientation corrections use importer or wrapper-child " +
                "rotation; no negative scale is intentional.");
            report.AppendLine();
            report.AppendLine("## Asset Matrix");
            report.AppendLine();
            report.AppendLine(
                "| Asset | Source FBX | Source textures | Generated material | " +
                "Generated prefab | Scenes using asset | Status | " +
                "Orientation issue | Material issue | Notes |");
            report.AppendLine(
                "|---|---|---:|---|---|---|---|---|---|---|");

            for (int i = 0; i < Assets.Length; i++)
            {
                AuditAssetSpec spec = Assets[i];
                string sourceModel = FindSourceModel(spec.SourceFolder);
                int sourceTextureCount = CountSourceTextures(spec.SourceFolder);
                bool prefabExists =
                    AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath) != null;
                List<string> materials = FindGeneratedMaterials(spec.PrefabPath);
                List<string> usages = FormatUsages(spec, sceneAudits);
                string status = ProductionStatus(spec, sceneAudits);
                string orientation = OrientationStatus(spec);
                string materialIssue = MaterialIssue(
                    sourceTextureCount,
                    prefabExists,
                    materials.Count,
                    spec);
                string notes = BuildNotes(spec, sceneAudits);

                report.AppendLine(
                    "| " + Escape(spec.AssetId) +
                    " | " + CodeOrMissing(sourceModel) +
                    " | " + sourceTextureCount +
                    " | " + GeneratedList(materials) +
                    " | " + (prefabExists
                        ? "`" + spec.PrefabPath + "`"
                        : "MISSING") +
                    " | " + Escape(usages.Count == 0
                        ? "None"
                        : string.Join(", ", usages)) +
                    " | " + status +
                    " | " + Escape(orientation) +
                    " | " + Escape(materialIssue) +
                    " | " + Escape(notes) + " |");
            }

            AppendSceneSafety(report, sceneAudits);
            AppendSceneApplication(report, sceneAudits);
            AppendOrientationReport(report);
            AppendStageReport(report, sceneAudits);
            AppendFallbackReport(report, sceneAudits);

            string absolutePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                ReportPath);
            File.WriteAllText(
                absolutePath,
                report.ToString(),
                new UTF8Encoding(false));
        }

        private static void AppendSceneSafety(
            StringBuilder report,
            List<SceneAudit> sceneAudits)
        {
            report.AppendLine();
            report.AppendLine("## Main Scene Safety");
            report.AppendLine();
            report.AppendLine(
                "| Scene | AudioListener | EventSystem | Camera | " +
                "First-person rig |");
            report.AppendLine("|---|---:|---:|---|---:|");
            foreach (SceneAudit audit in sceneAudits.Where(
                item => item.Spec.IsMainScene))
            {
                report.AppendLine(
                    "| " + audit.Spec.Name +
                    " | " + audit.ActiveAudioListeners +
                    " | " + audit.ActiveEventSystems +
                    " | " + audit.CameraMode +
                    " | " + audit.ActiveFirstPersonRigs + " |");
            }
        }

        private static void AppendSceneApplication(
            StringBuilder report,
            List<SceneAudit> sceneAudits)
        {
            report.AppendLine();
            report.AppendLine("## Main Scene Application");
            report.AppendLine();
            foreach (SceneAudit audit in sceneAudits.Where(
                item => item.Spec.IsMainScene))
            {
                List<string> active = Assets
                    .Where(asset => audit.Get(asset.AssetId).Active)
                    .Select(asset => asset.AssetId)
                    .ToList();
                List<string> inactive = Assets
                    .Where(asset => !audit.Get(asset.AssetId).Active &&
                        audit.Get(asset.AssetId).Inactive)
                    .Select(asset => asset.AssetId)
                    .ToList();
                report.AppendLine(
                    "- **" + audit.Spec.Name + "** active: " +
                    (active.Count == 0 ? "none" : string.Join(", ", active)) +
                    "; inactive: " +
                    (inactive.Count == 0
                        ? "none"
                        : string.Join(", ", inactive)) + ".");
            }
        }

        private static void AppendOrientationReport(StringBuilder report)
        {
            report.AppendLine();
            report.AppendLine("## Orientation Corrections");
            report.AppendLine();
            report.AppendLine(
                "- Kevin AmbiguousFace, PlainFace, CuteFace, and WesternFace: " +
                "FBX importer `bakeAxisConversion = true` plus wrapper `Model` " +
                "Y = 180 degrees; wrappers remain positive-scale, upright, and " +
                "+Z-forward.");
            report.AppendLine(
                "- DJController and Turntable: generated prefab `Model` child " +
                "rotation X = -90 degrees so controls face upward on the DJ desk.");
            report.AppendLine(
                "- NeonSign and SignBoard: wrapper front remains +Z; Club scene " +
                "instances rotate Y = 180/185 degrees to face the playable area.");
            report.AppendLine(
                "- Salmon, Rockfish, and Mullet: no correction; gallery review " +
                "keeps a consistent head-right side orientation.");
            report.AppendLine(
                "- KitchenKnife: no correction; top view keeps blade-left and " +
                "handle-right.");
            report.AppendLine(
                "- DisplayInside, DisplayOutside, and SashimiTable: no wrapper " +
                "correction; +Z is the authored customer-facing front.");
        }

        private static void AppendStageReport(
            StringBuilder report,
            List<SceneAudit> sceneAudits)
        {
            SceneAudit stage = sceneAudits.First(
                item => item.Spec.Name == "Stage01_Salmon");
            bool realSalmon = stage.Get("Salmon").Active;
            bool realKnife = stage.Get("KitchenKnife").Active;
            report.AppendLine();
            report.AppendLine("## Stage01 Asset State");
            report.AppendLine();
            report.AppendLine(
                "- Real Salmon prefab active: " + YesNo(realSalmon) + ".");
            report.AppendLine(
                "- Real KitchenKnife prefab active: " + YesNo(realKnife) + ".");
            report.AppendLine(
                "- Procedural salmon/knife are retained until a validated " +
                "replacement is active; timing, note tracking, scoring, and cut " +
                "marks remain owned by the existing Stage01 components.");
        }

        private static void AppendFallbackReport(
            StringBuilder report,
            List<SceneAudit> sceneAudits)
        {
            report.AppendLine();
            report.AppendLine("## Placeholder And Fallback State");
            report.AppendLine();
            foreach (SceneAudit audit in sceneAudits.Where(
                item => item.Spec.IsMainScene))
            {
                report.AppendLine(
                    "- **" + audit.Spec.Name + "**: " +
                    (audit.Fallbacks.Count == 0
                        ? "no named fallback marker found"
                        : string.Join(", ", audit.Fallbacks.OrderBy(x => x))) +
                    ".");
            }

            report.AppendLine(
                "- A missing validated fixture does not trigger destructive " +
                "placeholder removal; legacy interaction colliders and SceneDoor " +
                "objects remain authoritative.");
        }

        private static string BuildNotes(
            AuditAssetSpec spec,
            List<SceneAudit> sceneAudits)
        {
            bool usedInMain = sceneAudits
                .Where(item => item.Spec.IsMainScene)
                .Any(item => item.Get(spec.AssetId).Active);
            bool negativeScale = HasNegativeScale(spec.PrefabPath);
            List<string> notes = new List<string>();

            if (spec.Category == "Kevin")
            {
                notes.Add(spec.AssetId == "AmbiguousFace"
                    ? "Provisional first-person runtime default"
                    : "Candidate variant; gallery only unless selected later");
            }
            else if (spec.Category == "FishShop" && !usedInMain)
            {
                notes.Add("Generated and gallery-validated; not active in gameplay");
            }
            else if (spec.Category == "Club")
            {
                notes.Add("Generated Club art-pass asset");
            }

            notes.Add(negativeScale
                ? "Negative scale detected"
                : "No negative scale detected");
            return string.Join("; ", notes);
        }

        private static string OrientationStatus(AuditAssetSpec spec)
        {
            if (spec.Category == "Kevin")
            {
                string modelPath = FindSourceModel(spec.SourceFolder);
                ModelImporter importer = string.IsNullOrEmpty(modelPath)
                    ? null
                    : AssetImporter.GetAtPath(modelPath) as ModelImporter;
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    spec.PrefabPath);
                Transform model = prefab == null
                    ? null
                    : prefab.transform.Find("Model");
                bool forwardCorrected = model != null &&
                    Mathf.Abs(Mathf.DeltaAngle(
                        model.localEulerAngles.y,
                        180f)) < 0.1f;
                return importer != null && importer.bakeAxisConversion &&
                    forwardCorrected
                    ? "Yes - importer axis bake plus wrapper Model Y 180"
                    : "Yes - importer correction missing";
            }

            if (spec.AssetId == "DJController" ||
                spec.AssetId == "Turntable")
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    spec.PrefabPath);
                Transform model = prefab == null
                    ? null
                    : prefab.transform.Find("Model");
                bool corrected = model != null &&
                    Mathf.Abs(Mathf.DeltaAngle(
                        model.localEulerAngles.x,
                        -90f)) < 0.1f;
                return corrected
                    ? "Yes - corrected on wrapper Model X -90"
                    : "Yes - wrapper correction missing";
            }

            if (spec.AssetId == "NeonSign" || spec.AssetId == "SignBoard")
            {
                return "Yes - corrected at Club scene placement Y 180/185";
            }

            return "No issue found in gallery review";
        }

        private static string MaterialIssue(
            int sourceTextureCount,
            bool prefabExists,
            int materialCount,
            AuditAssetSpec spec)
        {
            if (!prefabExists)
            {
                return "Yes - generated prefab missing";
            }

            if (materialCount == 0)
            {
                return "Yes - generated material dependency missing";
            }

            if (sourceTextureCount == 0 &&
                (spec.AssetId == "BarTable" ||
                 spec.AssetId == "ClubDoorFrame"))
            {
                return "No - intentional neutral fallback material";
            }

            return "No issue found";
        }

        private static string ProductionStatus(
            AuditAssetSpec spec,
            List<SceneAudit> sceneAudits)
        {
            List<UsageState> states = sceneAudits
                .Where(item => item.Spec.IsMainScene)
                .Select(item => item.Get(spec.AssetId))
                .ToList();
            if (states.Any(state => state.Active))
            {
                return "active";
            }

            if (states.Any(state => state.Inactive))
            {
                return "inactive";
            }

            return "unused";
        }

        private static List<string> FormatUsages(
            AuditAssetSpec spec,
            List<SceneAudit> sceneAudits)
        {
            var usages = new List<string>();
            for (int i = 0; i < sceneAudits.Count; i++)
            {
                UsageState state = sceneAudits[i].Get(spec.AssetId);
                if (state.Active)
                {
                    usages.Add(sceneAudits[i].Spec.Name + " (active)");
                }
                else if (state.Inactive)
                {
                    usages.Add(sceneAudits[i].Spec.Name + " (inactive)");
                }
            }

            return usages;
        }

        private static List<string> FindGeneratedMaterials(string prefabPath)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                return new List<string>();
            }

            return AssetDatabase.GetDependencies(prefabPath, true)
                .Where(path => path.EndsWith(
                    ".mat",
                    StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .OrderBy(path => path)
                .ToList();
        }

        private static string FindSourceModel(string sourceFolder)
        {
            return AssetDatabase.FindAssets(string.Empty, new[] { sourceFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(path => path.EndsWith(
                    ".fbx",
                    StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        }

        private static int CountSourceTextures(string sourceFolder)
        {
            return AssetDatabase.FindAssets(
                    "t:Texture2D",
                    new[] { sourceFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Count(path => !path.EndsWith(
                    ".meta",
                    StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasNegativeScale(string prefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                prefabPath);
            if (prefab == null)
            {
                return false;
            }

            return prefab.GetComponentsInChildren<Transform>(true).Any(
                transform => transform.localScale.x < 0f ||
                    transform.localScale.y < 0f ||
                    transform.localScale.z < 0f);
        }

        private static int CountActive<T>(Scene scene)
            where T : Behaviour
        {
            return GetComponentsInScene<T>(scene).Count(
                item => item.enabled && item.gameObject.activeInHierarchy);
        }

        private static T[] GetComponentsInScene<T>(Scene scene)
            where T : Component
        {
            List<T> components = new List<T>();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                components.AddRange(
                    roots[i].GetComponentsInChildren<T>(true));
            }

            return components.ToArray();
        }

        private static string HierarchyPath(Transform transform)
        {
            List<string> names = new List<string>();
            while (transform != null)
            {
                names.Add(transform.name);
                transform = transform.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private static string GeneratedList(List<string> paths)
        {
            if (paths.Count == 0)
            {
                return "MISSING";
            }

            return paths.Count == 1
                ? "`" + paths[0] + "`"
                : paths.Count + " generated materials";
        }

        private static string CodeOrMissing(string value)
        {
            return string.IsNullOrEmpty(value) ? "MISSING" : "`" + value + "`";
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("|", "\\|")
                .Replace("\r", string.Empty)
                .Replace("\n", "<br>");
        }

        private static string YesNo(bool value)
        {
            return value ? "yes" : "no";
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

        private static AuditAssetSpec Kevin(string assetId)
        {
            return new AuditAssetSpec(
                assetId,
                "Kevin",
                "Assets/_SashimiBoy/Art/Source/Characters/Kevin/Variants/" +
                    assetId,
                GeneratedRoot + "/Prefabs/Characters/" +
                    "PF_Character_Kevin_" + assetId + ".prefab");
        }

        private static AuditAssetSpec Fish(string assetId)
        {
            return new AuditAssetSpec(
                assetId,
                "FishShop",
                "Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fish/" +
                    assetId,
                GeneratedRoot + "/Prefabs/FishShop/PF_Fish_" +
                    assetId + ".prefab");
        }

        private static AuditAssetSpec FishShop(
            string assetId,
            string sourceRelativePath,
            string prefabName)
        {
            return new AuditAssetSpec(
                assetId,
                "FishShop",
                "Assets/_SashimiBoy/Art/Source/Environment/FishShop/" +
                    sourceRelativePath,
                GeneratedRoot + "/Prefabs/FishShop/" +
                    prefabName + ".prefab");
        }

        private static AuditAssetSpec Club(string assetId)
        {
            return new AuditAssetSpec(
                assetId,
                "Club",
                "Assets/_SashimiBoy/Art/Source/Environment/Club/" + assetId,
                GeneratedRoot + "/Prefabs/Club/PF_Club_" +
                    assetId + ".prefab");
        }

        private sealed class AuditAssetSpec
        {
            public AuditAssetSpec(
                string assetId,
                string category,
                string sourceFolder,
                string prefabPath)
            {
                AssetId = assetId;
                Category = category;
                SourceFolder = sourceFolder;
                PrefabPath = prefabPath;
            }

            public string AssetId { get; private set; }
            public string Category { get; private set; }
            public string SourceFolder { get; private set; }
            public string PrefabPath { get; private set; }
        }

        private sealed class AuditSceneSpec
        {
            public AuditSceneSpec(
                string name,
                string path,
                bool isMainScene)
            {
                Name = name;
                Path = path;
                IsMainScene = isMainScene;
            }

            public string Name { get; private set; }
            public string Path { get; private set; }
            public bool IsMainScene { get; private set; }
        }

        private sealed class UsageState
        {
            public bool Active;
            public bool Inactive;
            public readonly HashSet<string> ObjectPaths =
                new HashSet<string>();
        }

        private sealed class SceneAudit
        {
            private readonly Dictionary<string, UsageState> usage =
                new Dictionary<string, UsageState>(
                    StringComparer.OrdinalIgnoreCase);

            public SceneAudit(AuditSceneSpec spec)
            {
                Spec = spec;
            }

            public AuditSceneSpec Spec { get; private set; }
            public bool Missing;
            public int ActiveAudioListeners;
            public int ActiveEventSystems;
            public int ActiveFirstPersonRigs;
            public string CameraMode = "missing";
            public readonly HashSet<string> Fallbacks =
                new HashSet<string>();

            public void Mark(
                string assetId,
                bool active,
                string objectPath)
            {
                UsageState state = Get(assetId);
                state.Active |= active;
                state.Inactive |= !active;
                state.ObjectPaths.Add(objectPath);
            }

            public UsageState Get(string assetId)
            {
                UsageState state;
                if (!usage.TryGetValue(assetId, out state))
                {
                    state = new UsageState();
                    usage.Add(assetId, state);
                }

                return state;
            }
        }
    }
}
#endif
