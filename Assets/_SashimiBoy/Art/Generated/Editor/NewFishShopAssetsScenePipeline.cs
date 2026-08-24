#if UNITY_EDITOR
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
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace SashimiBoy.EditorTools
{
    public static class NewFishShopAssetsScenePipeline
    {
        private const string GeneratedRoot =
            "Assets/_SashimiBoy/Art/Generated";
        private const string PrefabRoot =
            GeneratedRoot + "/Prefabs/FishShop";
        private const string ReportPath =
            GeneratedRoot +
            "/Reports/NewFishShopAssetsSceneApplicationReport.md";
        private const string SceneRoot = "Assets/_SashimiBoy/Scenes";
        private const string BackupRoot = SceneRoot + "/Backups";
        private const string StreetScenePath = SceneRoot + "/Street.unity";
        private const string FishShopScenePath =
            SceneRoot + "/FishShopDialogue.unity";
        private const string StageScenePath =
            SceneRoot + "/Stage01_Salmon.unity";

        private const string DisplayOutsidePath =
            PrefabRoot + "/PF_Fixture_DisplayOutside.prefab";
        private const string DisplayInsidePath =
            PrefabRoot + "/PF_Fixture_DisplayInside.prefab";
        private const string SalmonPath =
            PrefabRoot + "/PF_Fish_Salmon.prefab";
        private const string RockfishPath =
            PrefabRoot + "/PF_Fish_Rockfish.prefab";
        private const string MulletPath =
            PrefabRoot + "/PF_Fish_Mullet.prefab";
        private const string KnifePath =
            PrefabRoot + "/PF_Prop_KitchenKnife.prefab";

        private const string StreetBackup =
            BackupRoot + "/Street_PreNewFishShopAssets.unity";
        private const string FishShopBackup =
            BackupRoot + "/FishShop_PreNewFishShopAssets.unity";
        private const string StageBackup =
            BackupRoot + "/Stage01_Salmon_PreRealSalmonAssets.unity";

        [MenuItem("Sashimi Boy/Art/Apply Validated FishShop Assets To Scenes")]
        public static void ApplyValidatedFishShopAssetsToScenes()
        {
            ApplyAll(true);
        }

        public static void ApplyValidatedFishShopAssetsToScenesBatch()
        {
            ApplyAll(false);
        }

        public static void BuildTask1AndApplyTask2Batch()
        {
            NewAssetsKevinCameraPipeline.
                BuildNewCharacterAndFishShopPrefabsBatch();
            ApplyAll(false);
        }

        private static void ApplyAll(bool showDialog)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Dictionary<string, GameObject> prefabs = LoadRequiredPrefabs();
            EnsureBackups();

            SceneSnapshot stageBefore = CaptureStageSnapshot();
            ApplyStreet(prefabs[DisplayOutsidePath]);
            ApplyFishShop(
                prefabs[DisplayInsidePath],
                prefabs[SalmonPath],
                prefabs[RockfishPath],
                prefabs[MulletPath]);
            ApplyStage(
                prefabs[SalmonPath],
                prefabs[KnifePath]);
            SceneSnapshot stageAfter = CaptureStageSnapshot();
            ValidateTimingUnchanged(stageBefore, stageAfter);
            List<SceneAudit> audits = AuditScenes();
            WriteReport(stageBefore, stageAfter, audits);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorSceneManager.OpenScene(
                StreetScenePath,
                OpenSceneMode.Single);
            Debug.Log(
                "[Sashimi Boy] TASK 2 validated FishShop assets were " +
                "applied without changing Stage01 timing or scoring data.");
            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "Sashimi Boy",
                    "Display assets, fish displays, real Salmon, and real " +
                    "knife were applied. Stage01 timing data was preserved.",
                    "OK");
            }
        }

        private static Dictionary<string, GameObject> LoadRequiredPrefabs()
        {
            string[] paths =
            {
                DisplayOutsidePath,
                DisplayInsidePath,
                SalmonPath,
                RockfishPath,
                MulletPath,
                KnifePath,
            };
            Dictionary<string, GameObject> prefabs =
                new Dictionary<string, GameObject>();
            for (int i = 0; i < paths.Length; i++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    paths[i]);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        "Required generated prefab is missing: `" +
                        paths[i] + "`. Run Build New Character And FishShop " +
                        "Prefabs first.");
                }

                prefabs.Add(paths[i], prefab);
            }

            return prefabs;
        }

        private static void EnsureBackups()
        {
            EnsureFolder(BackupRoot);
            CopyBackupOnce(StreetScenePath, StreetBackup);
            CopyBackupOnce(FishShopScenePath, FishShopBackup);
            CopyBackupOnce(StageScenePath, StageBackup);
        }

        private static void CopyBackupOnce(string source, string target)
        {
            if (File.Exists(target))
            {
                return;
            }

            if (!File.Exists(source) || !AssetDatabase.CopyAsset(source, target))
            {
                throw new InvalidOperationException(
                    "Could not create one-time scene backup: `" +
                    target + "`.");
            }
        }

        private static void ApplyStreet(GameObject displayOutsidePrefab)
        {
            Scene scene = EditorSceneManager.OpenScene(
                StreetScenePath,
                OpenSceneMode.Single);
            RequireFirstPersonCamera(scene, "Street");
            DestroyNamedRoot(scene, "Task2_FishShopExteriorAssets");
            GameObject root = CreateSceneRoot(
                scene,
                "Task2_FishShopExteriorAssets");

            GameObject display = InstantiatePrefab(
                displayOutsidePrefab,
                scene,
                root.transform,
                "DisplayOutside_Validated");
            display.transform.SetPositionAndRotation(
                new Vector3(-5.05f, 0.12f, -2.15f),
                Quaternion.Euler(0f, 180f, 0f));
            RemoveDecorativeColliders(display);

            SceneDoor fishDoor = GetSceneComponents<SceneDoor>(scene)
                .FirstOrDefault(door => door.sceneName ==
                    SashimiBoyConstants.Scenes.FishShopDialogue);
            if (fishDoor == null)
            {
                throw new InvalidOperationException(
                    "Street FishShop SceneDoor is missing.");
            }

            if (BoundsOverlapExpanded(
                    display,
                    fishDoor.gameObject,
                    new Vector3(0.45f, 1f, 0.45f)))
            {
                display.transform.position =
                    new Vector3(-5.25f, 0.12f, -1.85f);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, StreetScenePath);
        }

        private static void ApplyFishShop(
            GameObject displayInsidePrefab,
            GameObject salmonPrefab,
            GameObject rockfishPrefab,
            GameObject mulletPrefab)
        {
            Scene scene = EditorSceneManager.OpenScene(
                FishShopScenePath,
                OpenSceneMode.Single);
            RequireFirstPersonCamera(scene, "FishShopDialogue");
            DestroyNamedRoot(scene, "Task2_FishShopInteriorAssets");
            GameObject root = CreateSceneRoot(
                scene,
                "Task2_FishShopInteriorAssets");

            GameObject display = InstantiatePrefab(
                displayInsidePrefab,
                scene,
                root.transform,
                "DisplayInside_Validated");
            display.transform.SetPositionAndRotation(
                new Vector3(4.45f, 0.08f, 2.85f),
                Quaternion.Euler(0f, 180f, 0f));
            RemoveDecorativeColliders(display);

            GameObject fishShelf = new GameObject("FishDisplay_Shelf");
            fishShelf.transform.SetParent(root.transform, false);
            fishShelf.transform.position = new Vector3(0f, 2.34f, 3.18f);

            CreateDisplayFish(
                salmonPrefab,
                scene,
                fishShelf.transform,
                "DisplayFish_Salmon",
                new Vector3(-2f, 0.04f, 0f),
                Quaternion.Euler(0f, 90f, 0f));
            CreateDisplayFish(
                rockfishPrefab,
                scene,
                fishShelf.transform,
                "DisplayFish_Rockfish",
                new Vector3(0f, 0.04f, 0f),
                Quaternion.Euler(0f, 90f, 0f));
            CreateDisplayFish(
                mulletPrefab,
                scene,
                fishShelf.transform,
                "DisplayFish_Mullet",
                new Vector3(2f, 0.04f, 0f),
                Quaternion.Euler(0f, 90f, 0f));

            GameObject placeholder = FindNamed(scene, "Placeholder_Fish");
            if (placeholder != null)
            {
                placeholder.name = "Placeholder_Fish_Fallback";
                placeholder.SetActive(false);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, FishShopScenePath);
        }

        private static void CreateDisplayFish(
            GameObject prefab,
            Scene scene,
            Transform parent,
            string name,
            Vector3 localPosition,
            Quaternion localRotation)
        {
            GameObject fish = InstantiatePrefab(
                prefab,
                scene,
                parent,
                name);
            fish.transform.localPosition = localPosition;
            fish.transform.localRotation = localRotation;
            fish.transform.localScale = Vector3.one;
            RemoveDecorativeColliders(fish);
        }

        private static void ApplyStage(
            GameObject salmonPrefab,
            GameObject knifePrefab)
        {
            Scene scene = EditorSceneManager.OpenScene(
                StageScenePath,
                OpenSceneMode.Single);
            if (GetSceneComponents<KevinFirstPersonCameraRig>(scene)
                .Any(camera => camera.enabled &&
                    camera.gameObject.activeInHierarchy))
            {
                throw new InvalidOperationException(
                    "Stage01 must not use KevinFirstPersonCameraRig.");
            }

            Camera camera = FindMainCamera(scene);
            if (camera == null || !camera.orthographic)
            {
                throw new InvalidOperationException(
                    "Stage01 fixed orthographic camera is missing.");
            }

            ProceduralSalmonView salmon =
                GetSceneComponents<ProceduralSalmonView>(scene)
                    .FirstOrDefault();
            Stage01SalmonPresentationController presentation =
                GetSceneComponents<Stage01SalmonPresentationController>(scene)
                    .FirstOrDefault();
            if (salmon == null || presentation == null ||
                presentation.salmon != salmon)
            {
                throw new InvalidOperationException(
                    "Stage01 salmon presentation references are invalid.");
            }

            ReplaceStageSalmonVisual(scene, salmon, salmonPrefab);

            KnifeVisualController[] knives =
                GetSceneComponents<KnifeVisualController>(scene);
            for (int i = 0; i < knives.Length; i++)
            {
                ReplaceStageKnifeVisual(scene, knives[i], knifePrefab);
            }

            if (presentation.playerKnife == null ||
                presentation.playerKnife != knives.FirstOrDefault(knife =>
                    knife.gameObject.name == "Stage01_PlayerKnife"))
            {
                throw new InvalidOperationException(
                    "Stage01 player knife controller reference changed.");
            }

            EnsureSceneSingletons(scene);
            EditorUtility.SetDirty(salmon);
            for (int i = 0; i < knives.Length; i++)
            {
                EditorUtility.SetDirty(knives[i]);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, StageScenePath);
        }

        private static void ReplaceStageSalmonVisual(
            Scene scene,
            ProceduralSalmonView salmon,
            GameObject salmonPrefab)
        {
            Transform originalVisualRoot = salmon.visualRoot != null
                ? salmon.visualRoot
                : salmon.transform;
            Transform adapter = originalVisualRoot.Find(
                "RealSalmon_VisualAdapter");
            if (adapter == null)
            {
                adapter = new GameObject("RealSalmon_VisualAdapter").transform;
                adapter.SetParent(originalVisualRoot, false);
            }

            DestroyChildren(adapter);
            GameObject realSalmon = InstantiatePrefab(
                salmonPrefab,
                scene,
                adapter,
                "PF_Fish_Salmon_Stage01");
            realSalmon.transform.localPosition = Vector3.zero;
            realSalmon.transform.localRotation =
                Quaternion.Euler(0f, 90f, 0f);
            realSalmon.transform.localScale = Vector3.one * 4.25f;
            RemoveDecorativeColliders(realSalmon);

            Transform fallback = originalVisualRoot.Find(
                "ProceduralSalmon_Fallback");
            if (fallback == null)
            {
                fallback = new GameObject(
                    "ProceduralSalmon_Fallback").transform;
                fallback.SetParent(originalVisualRoot, false);
            }

            HashSet<Transform> keep = new HashSet<Transform>
            {
                adapter,
                fallback,
            };
            for (int i = originalVisualRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = originalVisualRoot.GetChild(i);
                if (keep.Contains(child) || child.name == "CutMarks" ||
                    child.name == "SlicePieces")
                {
                    continue;
                }

                child.SetParent(fallback, true);
            }

            fallback.gameObject.SetActive(false);
            adapter.gameObject.SetActive(true);
            salmon.visualRoot = originalVisualRoot;
            salmon.cutStartLocalX = -1.85f;
            salmon.cutEndLocalX = 1.85f;
            salmon.cutLocalY = 0.25f;
            salmon.cutLocalZ = 0f;
        }

        private static void ReplaceStageKnifeVisual(
            Scene scene,
            KnifeVisualController knife,
            GameObject knifePrefab)
        {
            if (knife == null)
            {
                return;
            }

            Transform motionRoot = knife.motionRoot != null
                ? knife.motionRoot
                : knife.transform;
            Transform adapter = motionRoot.Find("RealKnife_VisualAdapter");
            if (adapter == null)
            {
                adapter = new GameObject("RealKnife_VisualAdapter").transform;
                adapter.SetParent(motionRoot, false);
            }

            DestroyChildren(adapter);
            GameObject realKnife = InstantiatePrefab(
                knifePrefab,
                scene,
                adapter,
                "PF_Prop_KitchenKnife_Stage01");
            realKnife.transform.localPosition =
                new Vector3(0f, 0.02f, -0.28f);
            realKnife.transform.localRotation =
                Quaternion.Euler(0f, 0f, 0f);
            realKnife.transform.localScale = Vector3.one * 4.2f;
            RemoveDecorativeColliders(realKnife);

            Transform fallback = motionRoot.Find("ProceduralKnife_Fallback");
            if (fallback == null)
            {
                fallback = new GameObject(
                    "ProceduralKnife_Fallback").transform;
                fallback.SetParent(motionRoot, false);
            }

            HashSet<Transform> keep = new HashSet<Transform>
            {
                adapter,
                fallback,
            };
            if (knife.highlight != null)
            {
                keep.Add(knife.highlight.transform);
            }

            for (int i = motionRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = motionRoot.GetChild(i);
                if (keep.Contains(child))
                {
                    continue;
                }

                child.SetParent(fallback, true);
            }

            fallback.gameObject.SetActive(false);
            adapter.gameObject.SetActive(true);
            knife.visualRoot = adapter.gameObject;
            knife.motionRoot = motionRoot;
        }

        private static SceneSnapshot CaptureStageSnapshot()
        {
            if (!File.Exists(StageScenePath))
            {
                throw new InvalidOperationException(
                    "Stage01_Salmon scene is missing.");
            }

            Scene scene = EditorSceneManager.OpenScene(
                StageScenePath,
                OpenSceneMode.Single);
            Stage01SalmonTimingScaffold timing =
                GetSceneComponents<Stage01SalmonTimingScaffold>(scene)
                    .FirstOrDefault();
            if (timing == null)
            {
                throw new InvalidOperationException(
                    "Stage01 timing scaffold is missing.");
            }

            Stage01NotePatternDefinition pattern =
                timing.notePatternProvider != null
                    ? timing.notePatternProvider.pattern
                    : null;
            SceneSnapshot snapshot = new SceneSnapshot
            {
                MusicClipPath = timing.musicClip != null
                    ? AssetDatabase.GetAssetPath(timing.musicClip)
                    : string.Empty,
                AudioSourceClipPath = timing.audioSource != null &&
                    timing.audioSource.clip != null
                    ? AssetDatabase.GetAssetPath(timing.audioSource.clip)
                    : string.Empty,
                Bpm = timing.bpm,
                FirstDownbeatSec = timing.firstDownbeatSec,
                GameplayStartSec = timing.gameplayStartSec,
                GameplayEndSec = timing.gameplayEndSec,
                ManualAudioOffsetMs = timing.manualAudioOffsetMs,
                ManualInputLatencyMs = timing.manualInputLatencyMs,
                InputKey = timing.inputKey,
                TimingComponentId = GlobalObjectId.GetGlobalObjectIdSlow(
                    timing).ToString(),
                AudioClockComponentId = timing.audioClock != null
                    ? GlobalObjectId.GetGlobalObjectIdSlow(
                        timing.audioClock).ToString()
                    : string.Empty,
                PresentationComponentId = timing.presentationController != null
                    ? GlobalObjectId.GetGlobalObjectIdSlow(
                        timing.presentationController).ToString()
                    : string.Empty,
                NotePatternPath = pattern != null
                    ? AssetDatabase.GetAssetPath(pattern)
                    : string.Empty,
                PatternJson = pattern != null
                    ? EditorJsonUtility.ToJson(pattern, true)
                    : string.Empty,
                RhythmJudgeScriptGuid = AssetDatabase.AssetPathToGUID(
                    "Assets/_SashimiBoy/Scripts/Rhythm/RhythmJudge.cs"),
                RhythmJudgeScriptHash = ComputeSha256(
                    "Assets/_SashimiBoy/Scripts/Rhythm/RhythmJudge.cs"),
                TimingScriptHash = ComputeSha256(
                    "Assets/_SashimiBoy/Scripts/Rhythm/" +
                    "Stage01SalmonTimingScaffold.cs"),
            };
            return snapshot;
        }

        private static void ValidateTimingUnchanged(
            SceneSnapshot before,
            SceneSnapshot after)
        {
            List<string> changed = new List<string>();
            Compare(changed, "music clip", before.MusicClipPath,
                after.MusicClipPath);
            Compare(changed, "AudioSource clip", before.AudioSourceClipPath,
                after.AudioSourceClipPath);
            Compare(changed, "BPM", before.Bpm, after.Bpm);
            Compare(changed, "first downbeat", before.FirstDownbeatSec,
                after.FirstDownbeatSec);
            Compare(changed, "gameplay start", before.GameplayStartSec,
                after.GameplayStartSec);
            Compare(changed, "gameplay end", before.GameplayEndSec,
                after.GameplayEndSec);
            Compare(changed, "manual audio offset",
                before.ManualAudioOffsetMs, after.ManualAudioOffsetMs);
            Compare(changed, "manual input latency",
                before.ManualInputLatencyMs, after.ManualInputLatencyMs);
            Compare(changed, "input key", before.InputKey, after.InputKey);
            Compare(changed, "timing component", before.TimingComponentId,
                after.TimingComponentId);
            Compare(changed, "AudioClock component",
                before.AudioClockComponentId, after.AudioClockComponentId);
            Compare(changed, "presentation component",
                before.PresentationComponentId,
                after.PresentationComponentId);
            Compare(changed, "note pattern path", before.NotePatternPath,
                after.NotePatternPath);
            Compare(changed, "note pattern JSON", before.PatternJson,
                after.PatternJson);
            Compare(changed, "RhythmJudge script GUID",
                before.RhythmJudgeScriptGuid,
                after.RhythmJudgeScriptGuid);
            Compare(changed, "RhythmJudge script hash",
                before.RhythmJudgeScriptHash,
                after.RhythmJudgeScriptHash);
            Compare(changed, "timing script hash",
                before.TimingScriptHash,
                after.TimingScriptHash);
            if (changed.Count > 0)
            {
                throw new InvalidOperationException(
                    "TASK 2 changed protected Stage01 timing/scoring data: " +
                    string.Join(", ", changed));
            }
        }

        private static void Compare<T>(
            List<string> changed,
            string label,
            T before,
            T after)
        {
            if (!EqualityComparer<T>.Default.Equals(before, after))
            {
                changed.Add(label);
            }
        }

        private static List<SceneAudit> AuditScenes()
        {
            return new List<SceneAudit>
            {
                AuditScene(StreetScenePath, true),
                AuditScene(FishShopScenePath, true),
                AuditScene(StageScenePath, false),
            };
        }

        private static SceneAudit AuditScene(
            string scenePath,
            bool expectsFirstPerson)
        {
            Scene scene = EditorSceneManager.OpenScene(
                scenePath,
                OpenSceneMode.Single);
            Camera mainCamera = FindMainCamera(scene);
            return new SceneAudit
            {
                SceneName = scene.name,
                ActiveCameras = CountActive<Camera>(scene),
                ActiveListeners = CountActive<AudioListener>(scene),
                ActiveEventSystems = CountActive<EventSystem>(scene),
                ActiveFirstPersonRigs =
                    CountActive<KevinFirstPersonCameraRig>(scene),
                IsOrthographic = mainCamera != null &&
                    mainCamera.orthographic,
                ExpectsFirstPerson = expectsFirstPerson,
            };
        }

        private static void WriteReport(
            SceneSnapshot before,
            SceneSnapshot after,
            List<SceneAudit> audits)
        {
            EnsureFolder(Path.GetDirectoryName(ReportPath)
                .Replace('\\', '/'));
            StringBuilder report = new StringBuilder();
            report.AppendLine("# TASK 2 FishShop Asset Scene Application");
            report.AppendLine();
            report.AppendLine("## Applied Scenes");
            report.AppendLine();
            report.AppendLine("- Street: `PF_Fixture_DisplayOutside` at the FishShop facade; SceneDoor and navigation colliders retained.");
            report.AppendLine("- FishShopDialogue: `PF_Fixture_DisplayInside`; Salmon, Rockfish, and Mullet display prefabs; decorative colliders removed.");
            report.AppendLine("- Stage01_Salmon: `PF_Fish_Salmon` and `PF_Prop_KitchenKnife` connected beneath existing presentation controllers.");
            report.AppendLine("- Rockfish and Mullet remain display/future-stage assets; Stage01 remains Salmon.");
            report.AppendLine();
            report.AppendLine("## Fallback Policy");
            report.AppendLine();
            report.AppendLine("- FishShop `Placeholder_Fish_Fallback` remains disabled.");
            report.AppendLine("- Stage01 `ProceduralSalmon_Fallback` and each `ProceduralKnife_Fallback` remain disabled under the original roots.");
            report.AppendLine("- Existing cut marks, scratch, slice cue, movement controllers, and presentation references remain active.");
            report.AppendLine();
            report.AppendLine("## Protected Stage01 Values");
            report.AppendLine();
            report.AppendLine("| Value | Before | After |");
            report.AppendLine("|---|---|---|");
            AppendValue(report, "Music clip", before.MusicClipPath,
                after.MusicClipPath);
            AppendValue(report, "AudioSource clip", before.AudioSourceClipPath,
                after.AudioSourceClipPath);
            AppendValue(report, "BPM", before.Bpm, after.Bpm);
            AppendValue(report, "First downbeat", before.FirstDownbeatSec,
                after.FirstDownbeatSec);
            AppendValue(report, "Gameplay start", before.GameplayStartSec,
                after.GameplayStartSec);
            AppendValue(report, "Gameplay end", before.GameplayEndSec,
                after.GameplayEndSec);
            AppendValue(report, "Manual audio offset",
                before.ManualAudioOffsetMs, after.ManualAudioOffsetMs);
            AppendValue(report, "Manual input latency",
                before.ManualInputLatencyMs, after.ManualInputLatencyMs);
            AppendValue(report, "Input key", before.InputKey, after.InputKey);
            AppendValue(report, "Note pattern", before.NotePatternPath,
                after.NotePatternPath);
            report.AppendLine("- Note-pattern serialized JSON: unchanged");
            report.AppendLine("- `RhythmJudge.cs`: not edited by TASK 2; GUID `" +
                after.RhythmJudgeScriptGuid + "`");
            report.AppendLine("- `RhythmJudge.cs` SHA-256: `" +
                after.RhythmJudgeScriptHash + "`");
            report.AppendLine("- `Stage01SalmonTimingScaffold.cs` SHA-256: `" +
                after.TimingScriptHash + "`");
            report.AppendLine("- Score/combo/yield/window constants remain owned by the unchanged `Stage01SalmonTimingScaffold` runtime code.");
            report.AppendLine();
            report.AppendLine("## Scene Audit");
            report.AppendLine();
            report.AppendLine("| Scene | Cameras | Listeners | EventSystems | FirstPerson | Orthographic |");
            report.AppendLine("|---|---:|---:|---:|---:|---|");
            for (int i = 0; i < audits.Count; i++)
            {
                SceneAudit audit = audits[i];
                report.AppendLine("| " + audit.SceneName + " | " +
                    audit.ActiveCameras + " | " +
                    audit.ActiveListeners + " | " +
                    audit.ActiveEventSystems + " | " +
                    audit.ActiveFirstPersonRigs + " | " +
                    audit.IsOrthographic + " |");
            }

            report.AppendLine();
            report.AppendLine("## Play Mode Checklist");
            report.AppendLine();
            report.AppendLine("1. Bootstrap -> START -> Street: the capsule renderer is hidden and the first-person camera starts at Kevin eye height.");
            report.AppendLine("2. Walk around DisplayOutside and confirm it does not block the FishShop door or camera path; press `E` at the door.");
            report.AppendLine("3. FishShopDialogue: inspect DisplayInside plus Salmon/Rockfish/Mullet display row; verify dialogue and return door still work.");
            report.AppendLine("4. Stage01_Salmon: confirm fixed camera, original music/timing, real Salmon, and real knife for both player and boss demo.");
            report.AppendLine("5. During gameplay, verify cut marks and scratch overlays still appear, score/combo/yield update, and no runtime mesh slicing occurs.");

            File.WriteAllText(
                AssetPathToAbsolutePath(ReportPath),
                report.ToString(),
                new UTF8Encoding(false));
            AssetDatabase.ImportAsset(
                ReportPath,
                ImportAssetOptions.ForceSynchronousImport);
        }

        private static void AppendValue<T>(
            StringBuilder report,
            string name,
            T before,
            T after)
        {
            report.AppendLine("| " + name + " | `" + before + "` | `" +
                after + "` |");
        }

        private static void RequireFirstPersonCamera(
            Scene scene,
            string label)
        {
            KevinFirstPersonCameraRig firstPerson =
                GetSceneComponents<KevinFirstPersonCameraRig>(scene)
                    .FirstOrDefault(component => component.enabled &&
                        component.gameObject.activeInHierarchy);
            KevinVisualLoader player =
                GetSceneComponents<KevinVisualLoader>(scene)
                    .FirstOrDefault();
            if (firstPerson == null || player == null ||
                player.visualRoot == null)
            {
                throw new InvalidOperationException(
                    label +
                    " is missing the TASK 1 Kevin first-person setup.");
            }

            Renderer capsule = player.GetComponent<Renderer>();
            if (capsule != null)
            {
                capsule.enabled = false;
                player.fallbackRenderer = capsule;
            }
        }

        private static void EnsureSceneSingletons(Scene scene)
        {
            Camera main = FindMainCamera(scene);
            if (main == null)
            {
                throw new InvalidOperationException(
                    scene.name + " Main Camera is missing.");
            }

            Camera[] cameras = GetSceneComponents<Camera>(scene);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != main)
                {
                    cameras[i].gameObject.SetActive(false);
                }
            }

            AudioListener keeper = main.GetComponent<AudioListener>();
            if (keeper == null)
            {
                keeper = main.gameObject.AddComponent<AudioListener>();
            }

            keeper.enabled = true;
            AudioListener[] listeners =
                GetSceneComponents<AudioListener>(scene);
            for (int i = 0; i < listeners.Length; i++)
            {
                listeners[i].enabled = listeners[i] == keeper;
            }

            EventSystem[] systems = GetSceneComponents<EventSystem>(scene);
            if (systems.Length == 0)
            {
                throw new InvalidOperationException(
                    scene.name + " EventSystem is missing.");
            }

            EventSystem eventKeeper = systems[0];
            eventKeeper.gameObject.SetActive(true);
            for (int i = 1; i < systems.Length; i++)
            {
                systems[i].gameObject.SetActive(false);
            }
        }

        private static GameObject InstantiatePrefab(
            GameObject prefab,
            Scene scene,
            Transform parent,
            string name)
        {
            GameObject instance = PrefabUtility.InstantiatePrefab(
                prefab,
                scene) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    "Could not instantiate prefab `" + prefab.name + "`.");
            }

            instance.name = name;
            instance.transform.SetParent(parent, false);
            return instance;
        }

        private static GameObject CreateSceneRoot(Scene scene, string name)
        {
            GameObject root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }

        private static void RemoveDecorativeColliders(GameObject root)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                UnityEngine.Object.DestroyImmediate(colliders[i]);
            }
        }

        private static bool BoundsOverlapExpanded(
            GameObject first,
            GameObject second,
            Vector3 expansion)
        {
            Bounds firstBounds;
            Bounds secondBounds;
            if (!TryGetBounds(first, out firstBounds) ||
                !TryGetBounds(second, out secondBounds))
            {
                return false;
            }

            secondBounds.Expand(expansion);
            return firstBounds.Intersects(secondBounds);
        }

        private static bool TryGetBounds(GameObject root, out Bounds bounds)
        {
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            bool found = false;
            bounds = new Bounds(root.transform.position, Vector3.zero);
            for (int i = 0; i < renderers.Length; i++)
            {
                bounds = Encapsulate(bounds, renderers[i].bounds, ref found);
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                bounds = Encapsulate(bounds, colliders[i].bounds, ref found);
            }

            return found;
        }

        private static Bounds Encapsulate(
            Bounds current,
            Bounds value,
            ref bool found)
        {
            if (!found)
            {
                found = true;
                return value;
            }

            current.Encapsulate(value);
            return current;
        }

        private static Camera FindMainCamera(Scene scene)
        {
            Camera[] cameras = GetSceneComponents<Camera>(scene);
            return cameras.FirstOrDefault(camera => camera.CompareTag(
                       "MainCamera")) ?? cameras.FirstOrDefault();
        }

        private static GameObject FindNamed(Scene scene, string name)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Select(item => item.gameObject)
                .FirstOrDefault(item => item.name == name);
        }

        private static T[] GetSceneComponents<T>(Scene scene)
            where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .ToArray();
        }

        private static int CountActive<T>(Scene scene)
            where T : Behaviour
        {
            return GetSceneComponents<T>(scene).Count(component =>
                component.enabled && component.gameObject.activeInHierarchy);
        }

        private static void DestroyNamedRoot(Scene scene, string name)
        {
            GameObject root = scene.GetRootGameObjects()
                .FirstOrDefault(item => item.name == name);
            if (root != null)
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void DestroyChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(
                    parent.GetChild(i).gameObject);
            }
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

        private static string AssetPathToAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(
                Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath);
        }

        private static string ComputeSha256(string assetPath)
        {
            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = File.ReadAllBytes(
                    AssetPathToAbsolutePath(assetPath));
                return string.Concat(hash.ComputeHash(bytes)
                    .Select(value => value.ToString("x2",
                        CultureInfo.InvariantCulture)));
            }
        }

        private sealed class SceneSnapshot
        {
            public string MusicClipPath;
            public string AudioSourceClipPath;
            public float Bpm;
            public double FirstDownbeatSec;
            public double GameplayStartSec;
            public double GameplayEndSec;
            public float ManualAudioOffsetMs;
            public float ManualInputLatencyMs;
            public KeyCode InputKey;
            public string TimingComponentId;
            public string AudioClockComponentId;
            public string PresentationComponentId;
            public string NotePatternPath;
            public string PatternJson;
            public string RhythmJudgeScriptGuid;
            public string RhythmJudgeScriptHash;
            public string TimingScriptHash;
        }

        private sealed class SceneAudit
        {
            public string SceneName;
            public int ActiveCameras;
            public int ActiveListeners;
            public int ActiveEventSystems;
            public int ActiveFirstPersonRigs;
            public bool IsOrthographic;
            public bool ExpectsFirstPerson;
        }
    }
}
#endif
