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
        private const string SashimiTablePath =
            PrefabRoot + "/PF_Fixture_SashimiTable.prefab";
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

        public static void RebuildFishShopArtPassBatch()
        {
            NewAssetsKevinCameraPipeline.
                RebuildCanonicalFishWrappersForArtPassBatch();
            ApplyFishShopArtPassForPrototypeGenerator();
        }

        public static void ApplyFishShopArtPassForPrototypeGenerator()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Dictionary<string, GameObject> prefabs = LoadRequiredPrefabs();
            ApplyFishShop(
                prefabs[DisplayInsidePath],
                prefabs[SashimiTablePath],
                prefabs[SalmonPath],
                prefabs[RockfishPath],
                prefabs[MulletPath]);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                "[Sashimi Boy] FishShop canonical wrappers and art pass " +
                "were applied without changing gameplay anchors.");
        }

        public static void ApplyStreetFrontageForPrototypeGenerator()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Dictionary<string, GameObject> prefabs = LoadRequiredPrefabs();
            ApplyStreet(prefabs[DisplayOutsidePath]);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                "[Sashimi Boy] Street FishShop frontage, story anchors, " +
                "and canonical exterior display were applied without " +
                "changing SceneDoor state.");
        }

        public static void RebuildStreetFishShopFrontageBatch()
        {
            VerticalSlicePresentationPipeline.
                ApplyFishShopSignFacingToStreetBatch();
            ApplyStreetFrontageForPrototypeGenerator();
        }

        private static void ApplyAll(bool showDialog)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Dictionary<string, GameObject> prefabs = LoadRequiredPrefabs();
            EnsureBackups();

            SceneSnapshot stageBefore = CaptureStageSnapshot();
            VerticalSlicePresentationPipeline.
                ApplyFishShopSignFacingToStreetBatch();
            ApplyStreet(prefabs[DisplayOutsidePath]);
            ApplyFishShop(
                prefabs[DisplayInsidePath],
                prefabs[SashimiTablePath],
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
                SashimiTablePath,
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
            SceneDoor fishDoor = GetSceneComponents<SceneDoor>(scene)
                .FirstOrDefault(door => door.sceneName ==
                    SashimiBoyConstants.Scenes.FishShopDialogue);
            if (fishDoor == null)
            {
                throw new InvalidOperationException(
                    "Street FishShop SceneDoor is missing.");
            }

            StreetDoorSnapshot doorBefore =
                StreetDoorSnapshot.Capture(fishDoor);
            GameObject root = GetOrCreateSceneRoot(
                scene,
                "Task2_FishShopExteriorAssets");
            GameObject display = GetOrCreatePrefabInstance(
                displayOutsidePrefab,
                scene,
                root.transform,
                "DisplayOutside_Validated");
            ConfigureGeneratedTransform(
                display.transform,
                new Vector3(-5.6f, 0.12f, -1.65f),
                Quaternion.identity,
                0);
            display.transform.localScale = Vector3.one * 0.72f;
            RemoveDecorativeColliders(display);

            GameObject storyAnchors = GetOrCreateSceneChild(
                scene,
                root.transform,
                "StreetStoryAnchors");
            ConfigureGeneratedTransform(
                storyAnchors.transform,
                Vector3.zero,
                Quaternion.identity,
                1);
            ConfigureStreetStoryAnchors(scene, storyAnchors.transform);

            ValidatePrefabSource(display, displayOutsidePrefab);
            ValidatePositiveScale(root.transform);
            ValidateStreetFrontage(
                scene,
                root,
                display,
                fishDoor,
                doorBefore);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, StreetScenePath);
        }

        private static void ConfigureStreetStoryAnchors(
            Scene scene,
            Transform parent)
        {
            ConfigureStreetStoryAnchor(
                scene,
                parent,
                "NPCSpawn_Morning",
                new Vector3(-6.7f, 0.18f, -0.65f),
                Quaternion.Euler(0f, 90f, 0f),
                0);
            ConfigureStreetStoryAnchor(
                scene,
                parent,
                "NPCDialogueStanding_Morning",
                new Vector3(-5.75f, 0.18f, -0.65f),
                Quaternion.Euler(0f, 270f, 0f),
                1);
            ConfigureStreetStoryAnchor(
                scene,
                parent,
                "NPCDialogueCamera_Morning",
                new Vector3(-6.2f, 1.65f, -1.55f),
                Quaternion.Euler(8f, 0f, 0f),
                2);
            ConfigureStreetStoryAnchor(
                scene,
                parent,
                "NPCSpawn_AfterWork",
                new Vector3(3.25f, 0.18f, 1.55f),
                Quaternion.Euler(0f, 180f, 0f),
                3);
            ConfigureStreetStoryAnchor(
                scene,
                parent,
                "NPCDialogueStanding_AfterWork",
                new Vector3(3.25f, 0.18f, 0.55f),
                Quaternion.identity,
                4);
            ConfigureStreetStoryAnchor(
                scene,
                parent,
                "NPCDialogueCamera_AfterWork",
                new Vector3(4.15f, 1.65f, 1.05f),
                Quaternion.Euler(8f, 270f, 0f),
                5);
        }

        private static void ConfigureStreetStoryAnchor(
            Scene scene,
            Transform parent,
            string name,
            Vector3 position,
            Quaternion rotation,
            int siblingIndex)
        {
            GameObject anchor = GetOrCreateSceneChild(scene, parent, name);
            ConfigureGeneratedTransform(
                anchor.transform,
                position,
                rotation,
                siblingIndex);
        }

        private static void ValidateStreetFrontage(
            Scene scene,
            GameObject root,
            GameObject display,
            SceneDoor fishDoor,
            StreetDoorSnapshot doorBefore)
        {
            doorBefore.ValidateUnchanged(fishDoor);
            GameObject displayCounter = FindNamed(scene, "DisplayCounter");
            GameObject signBoard = FindNamed(scene, "FishShop_Sign_Board");
            GameObject signText = FindNamed(scene, "FishShop_Sign_Text");
            if (displayCounter == null || signBoard == null ||
                signText == null)
            {
                throw new InvalidOperationException(
                    "Street FishShop facade is incomplete.");
            }

            TextMesh text = signText.GetComponent<TextMesh>();
            if (text == null || text.text != "SASHIMI" ||
                signText.transform.position.z <= signBoard.transform.position.z ||
                Vector3.Dot(-signText.transform.forward, Vector3.forward) <
                0.999f)
            {
                throw new InvalidOperationException(
                    "Street FishShop SASHIMI text does not face +Z.");
            }

            if (display.transform.rotation != Quaternion.identity)
            {
                throw new InvalidOperationException(
                    "DisplayOutside must keep the canonical wrapper +Z front.");
            }

            RequireBoundsClearance(
                display,
                fishDoor.gameObject,
                new Vector3(0.45f, 1f, 0.45f),
                "FishShop door approach");
            RequireBoundsClearance(
                display,
                displayCounter,
                new Vector3(0.12f, 0.12f, 0.12f),
                "existing storefront display counter");

            Collider[] decorativeColliders =
                root.GetComponentsInChildren<Collider>(true);
            if (decorativeColliders.Length != 0)
            {
                throw new InvalidOperationException(
                    "Street frontage art must not add decorative colliders.");
            }

            string[] anchorNames =
            {
                "NPCSpawn_Morning",
                "NPCDialogueStanding_Morning",
                "NPCDialogueCamera_Morning",
                "NPCSpawn_AfterWork",
                "NPCDialogueStanding_AfterWork",
                "NPCDialogueCamera_AfterWork",
            };
            for (int i = 0; i < anchorNames.Length; i++)
            {
                GameObject anchor = FindNamed(scene, anchorNames[i]);
                if (anchor == null ||
                    anchor.GetComponentsInChildren<Renderer>(true).Length != 0 ||
                    anchor.GetComponentsInChildren<Collider>(true).Length != 0)
                {
                    throw new InvalidOperationException(
                        "Street story anchor is missing or obstructive: `" +
                        anchorNames[i] + "`.");
                }
            }
        }

        private static void RequireBoundsClearance(
            GameObject first,
            GameObject second,
            Vector3 expansion,
            string label)
        {
            Bounds firstBounds;
            Bounds secondBounds;
            if (!TryGetBounds(first, out firstBounds) ||
                !TryGetBounds(second, out secondBounds))
            {
                throw new InvalidOperationException(
                    "Could not measure Street frontage bounds for " + label +
                    ".");
            }

            secondBounds.Expand(expansion);
            if (firstBounds.Intersects(secondBounds))
            {
                throw new InvalidOperationException(
                    "DisplayOutside overlaps the " + label + ". Display=" +
                    firstBounds + "; protected=" + secondBounds + ".");
            }
        }

        private static void ApplyFishShop(
            GameObject displayInsidePrefab,
            GameObject sashimiTablePrefab,
            GameObject salmonPrefab,
            GameObject rockfishPrefab,
            GameObject mulletPrefab)
        {
            Scene scene = EditorSceneManager.OpenScene(
                FishShopScenePath,
                OpenSceneMode.Single);
            RequireFirstPersonCamera(scene, "FishShopDialogue");
            Dictionary<string, Vector3> preservedAnchors =
                CaptureNamedPositions(
                    scene,
                    "Boss",
                    "Kevin",
                    "StartStage01_Placeholder",
                    "Door_To_Street",
                    "PlayerSpawnPoint",
                    "Kevin_Player");
            // Reuse valid generated objects so their scene-local file IDs stay
            // stable across authoritative rebuilds.
            GameObject root = GetOrCreateSceneRoot(
                scene,
                "Task2_FishShopInteriorAssets");

            GameObject display = GetOrCreatePrefabInstance(
                displayInsidePrefab,
                scene,
                root.transform,
                "DisplayInside_Validated");
            ConfigureGeneratedTransform(
                display.transform,
                new Vector3(4.45f, 0.08f, 2.85f),
                Quaternion.Euler(0f, 180f, 0f),
                0);
            RemoveDecorativeColliders(display);

            GameObject tableLeft = GetOrCreatePrefabInstance(
                sashimiTablePrefab,
                scene,
                root.transform,
                "SashimiTable_Left_Validated");
            ConfigureGeneratedTransform(
                tableLeft.transform,
                new Vector3(-1.35f, 0.08f, 1.72f),
                Quaternion.Euler(0f, 180f, 0f),
                1);
            RemoveDecorativeColliders(tableLeft);

            GameObject tableRight = GetOrCreatePrefabInstance(
                sashimiTablePrefab,
                scene,
                root.transform,
                "SashimiTable_Right_Validated");
            ConfigureGeneratedTransform(
                tableRight.transform,
                new Vector3(1.35f, 0.08f, 1.72f),
                Quaternion.Euler(0f, 180f, 0f),
                2);
            RemoveDecorativeColliders(tableRight);

            GameObject fishShelf = GetOrCreateSceneChild(
                scene,
                root.transform,
                "FishDisplay_SurfaceAnchor");
            ConfigureGeneratedTransform(
                fishShelf.transform,
                new Vector3(0f, 1.38f, 1.4f),
                Quaternion.identity,
                3);

            GameObject salmon = GetOrCreateDisplayFish(
                salmonPrefab,
                scene,
                fishShelf.transform,
                "DisplayFish_Salmon",
                new Vector3(-1.55f, 0.02f, 0f),
                Quaternion.Euler(0f, 90f, 0f),
                0);
            GameObject rockfish = GetOrCreateDisplayFish(
                rockfishPrefab,
                scene,
                fishShelf.transform,
                "DisplayFish_Rockfish",
                new Vector3(0f, 0.02f, 0f),
                Quaternion.Euler(0f, 90f, 0f),
                1);
            GameObject mullet = GetOrCreateDisplayFish(
                mulletPrefab,
                scene,
                fishShelf.transform,
                "DisplayFish_Mullet",
                new Vector3(1.55f, 0.02f, 0f),
                Quaternion.Euler(0f, 90f, 0f),
                2);

            GameObject placeholder = FindNamed(scene, "Placeholder_Fish");
            if (placeholder != null)
            {
                placeholder.name = "Placeholder_Fish_Fallback";
                placeholder.SetActive(false);
            }

            HideLegacyFishShopPresentation(scene);
            ValidatePrefabSource(display, displayInsidePrefab);
            ValidatePrefabSource(tableLeft, sashimiTablePrefab);
            ValidatePrefabSource(tableRight, sashimiTablePrefab);
            ValidatePrefabSource(salmon, salmonPrefab);
            ValidatePrefabSource(rockfish, rockfishPrefab);
            ValidatePrefabSource(mullet, mulletPrefab);
            ValidatePositiveScale(root.transform);
            ValidateFishDisplay(
                fishShelf.transform.position.y,
                salmon,
                rockfish,
                mullet);
            ValidateNamedPositions(scene, preservedAnchors);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, FishShopScenePath);
        }

        private static GameObject GetOrCreateDisplayFish(
            GameObject prefab,
            Scene scene,
            Transform parent,
            string name,
            Vector3 localPosition,
            Quaternion localRotation,
            int siblingIndex)
        {
            GameObject fish = GetOrCreatePrefabInstance(
                prefab,
                scene,
                parent,
                name);
            ConfigureGeneratedTransform(
                fish.transform,
                localPosition,
                localRotation,
                siblingIndex);
            RemoveDecorativeColliders(fish);
            return fish;
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

        private static GameObject GetOrCreatePrefabInstance(
            GameObject prefab,
            Scene scene,
            Transform parent,
            string name)
        {
            GameObject instance = FindUniqueDirectChild(parent, name);
            if (instance != null &&
                PrefabUtility.GetCorrespondingObjectFromSource(instance) !=
                prefab)
            {
                UnityEngine.Object.DestroyImmediate(instance);
                instance = null;
            }

            if (instance == null)
            {
                instance = InstantiatePrefab(prefab, scene, parent, name);
            }

            instance.name = name;
            instance.SetActive(true);
            return instance;
        }

        private static GameObject GetOrCreateSceneRoot(
            Scene scene,
            string name)
        {
            GameObject root = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name != name)
                {
                    continue;
                }

                if (root != null)
                {
                    throw new InvalidOperationException(
                        "Duplicate generated FishShop scene root: `" +
                        name + "`.");
                }

                root = roots[i];
            }

            if (root == null)
            {
                root = CreateSceneRoot(scene, name);
            }
            else if (PrefabUtility.IsPartOfPrefabInstance(root))
            {
                throw new InvalidOperationException(
                    "Generated FishShop scene root must not be a prefab " +
                    "instance: `" + name + "`.");
            }

            root.name = name;
            root.SetActive(true);
            root.transform.SetPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            root.transform.localScale = Vector3.one;
            root.transform.SetAsLastSibling();
            return root;
        }

        private static GameObject GetOrCreateSceneChild(
            Scene scene,
            Transform parent,
            string name)
        {
            GameObject child = FindUniqueDirectChild(parent, name);
            if (child != null && PrefabUtility.IsPartOfPrefabInstance(child))
            {
                UnityEngine.Object.DestroyImmediate(child);
                child = null;
            }

            if (child == null)
            {
                child = CreateSceneRoot(scene, name);
                child.transform.SetParent(parent, false);
            }

            child.name = name;
            child.SetActive(true);
            return child;
        }

        private static GameObject FindUniqueDirectChild(
            Transform parent,
            string name)
        {
            GameObject result = null;
            for (int i = 0; i < parent.childCount; i++)
            {
                GameObject child = parent.GetChild(i).gameObject;
                if (child.name != name)
                {
                    continue;
                }

                if (result != null)
                {
                    throw new InvalidOperationException(
                        "Duplicate generated FishShop child under `" +
                        parent.name + "`: `" + name + "`.");
                }

                result = child;
            }

            return result;
        }

        private static void ConfigureGeneratedTransform(
            Transform target,
            Vector3 localPosition,
            Quaternion localRotation,
            int siblingIndex)
        {
            target.localPosition = localPosition;
            target.localRotation = localRotation;
            target.localScale = Vector3.one;
            target.SetSiblingIndex(siblingIndex);
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

        private static void HideLegacyFishShopPresentation(Scene scene)
        {
            string[] names =
            {
                "Counter",
                "Cutting_Board",
                "Placeholder_Fish",
                "Placeholder_Fish_Fallback",
                "Boss",
                "Kevin",
                "CounterFront",
                "CounterTrim",
                "PrepBench_Left",
                "PrepBench_Right",
                "Shelf",
                "KnifeRail",
                "Boss_Visual",
                "Kevin_Visual",
            };
            for (int i = 0; i < names.Length; i++)
            {
                HideRenderers(FindNamed(scene, names[i]));
            }

            for (int i = 1; i <= 4; i++)
            {
                HideRenderers(FindNamed(
                    scene,
                    "IngredientTray_" + i.ToString("00")));
                HideRenderers(FindNamed(
                    scene,
                    "WallKnife_" + i.ToString("00")));
            }
        }

        private static void HideRenderers(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            Renderer[] renderers =
                target.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = false;
            }
        }

        private static Dictionary<string, Vector3> CaptureNamedPositions(
            Scene scene,
            params string[] names)
        {
            Dictionary<string, Vector3> result =
                new Dictionary<string, Vector3>();
            for (int i = 0; i < names.Length; i++)
            {
                GameObject target = FindNamed(scene, names[i]);
                if (target == null)
                {
                    throw new InvalidOperationException(
                        "Required FishShop anchor is missing: `" +
                        names[i] + "`.");
                }

                result.Add(names[i], target.transform.position);
            }

            return result;
        }

        private static void ValidateNamedPositions(
            Scene scene,
            Dictionary<string, Vector3> expected)
        {
            foreach (KeyValuePair<string, Vector3> item in expected)
            {
                GameObject target = FindNamed(scene, item.Key);
                if (target == null || target.transform.position != item.Value)
                {
                    throw new InvalidOperationException(
                        "FishShop gameplay anchor changed: `" +
                        item.Key + "`.");
                }
            }
        }

        private static void ValidatePrefabSource(
            GameObject instance,
            GameObject expectedPrefab)
        {
            GameObject source =
                PrefabUtility.GetCorrespondingObjectFromSource(instance);
            if (source != expectedPrefab)
            {
                throw new InvalidOperationException(
                    "Scene object is not connected to canonical prefab `" +
                    AssetDatabase.GetAssetPath(expectedPrefab) + "`.");
            }
        }

        private static void ValidatePositiveScale(Transform root)
        {
            Transform[] transforms =
                root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Vector3 scale = transforms[i].localScale;
                if (scale.x <= 0f || scale.y <= 0f || scale.z <= 0f)
                {
                    throw new InvalidOperationException(
                        "FishShop art uses non-positive scale at `" +
                        transforms[i].name + "`.");
                }
            }
        }

        private static void ValidateFishDisplay(
            float surfaceY,
            params GameObject[] fish)
        {
            Bounds[] bounds = new Bounds[fish.Length];
            for (int i = 0; i < fish.Length; i++)
            {
                if (!TryGetBounds(fish[i], out bounds[i]))
                {
                    throw new InvalidOperationException(
                        "Display fish has no renderer bounds: `" +
                        fish[i].name + "`.");
                }

                if (Mathf.Abs(bounds[i].min.y - surfaceY) > 0.04f)
                {
                    throw new InvalidOperationException(
                        "Display fish pivot is not aligned to its surface: `" +
                        fish[i].name + "`.");
                }
            }

            for (int first = 0; first < bounds.Length; first++)
            {
                for (int second = first + 1;
                     second < bounds.Length;
                     second++)
                {
                    if (bounds[first].Intersects(bounds[second]))
                    {
                        throw new InvalidOperationException(
                            "Display fish bounds overlap: `" +
                            fish[first].name + "` and `" +
                            fish[second].name + "`.");
                    }
                }
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

        private sealed class StreetDoorSnapshot
        {
            private Transform parent;
            private int siblingIndex;
            private Vector3 localPosition;
            private Quaternion localRotation;
            private Vector3 localScale;
            private bool activeSelf;
            private bool enabled;
            private string prompt;
            private string sceneName;
            private GameLocation destinationLocation;

            public static StreetDoorSnapshot Capture(SceneDoor door)
            {
                return new StreetDoorSnapshot
                {
                    parent = door.transform.parent,
                    siblingIndex = door.transform.GetSiblingIndex(),
                    localPosition = door.transform.localPosition,
                    localRotation = door.transform.localRotation,
                    localScale = door.transform.localScale,
                    activeSelf = door.gameObject.activeSelf,
                    enabled = door.enabled,
                    prompt = door.prompt,
                    sceneName = door.sceneName,
                    destinationLocation = door.destinationLocation,
                };
            }

            public void ValidateUnchanged(SceneDoor door)
            {
                if (door.transform.parent != parent ||
                    door.transform.GetSiblingIndex() != siblingIndex ||
                    door.transform.localPosition != localPosition ||
                    door.transform.localRotation != localRotation ||
                    door.transform.localScale != localScale ||
                    door.gameObject.activeSelf != activeSelf ||
                    door.enabled != enabled ||
                    door.prompt != prompt ||
                    door.sceneName != sceneName ||
                    door.destinationLocation != destinationLocation)
                {
                    throw new InvalidOperationException(
                        "Street FishShop SceneDoor transform, component, " +
                        "or destination reference changed.");
                }
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
