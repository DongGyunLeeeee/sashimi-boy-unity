#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SashimiBoy.EditorTools
{
    public static class Stage01SalmonPresentationPipeline
    {
        private const string StageScenePath =
            "Assets/_SashimiBoy/Scenes/Stage01_Salmon.unity";
        private const string BackupScenePath =
            "Assets/_SashimiBoy/Scenes/Backups/" +
            "Stage01_Salmon_ScaffoldBackup.unity";
        private const string MusicPath =
            "Assets/_SashimiBoy/Audio/Music/Stage_01_Salmon/" +
            "stage01_salmon_main.mp3";
        private const string GeneratedRoot =
            "Assets/_SashimiBoy/Art/Generated";
        private const string PrefabRoot =
            GeneratedRoot + "/Prefabs/Stage01";
        private const string MaterialRoot =
            GeneratedRoot + "/Materials/Stage01";
        private const string MeshRoot =
            GeneratedRoot + "/Meshes/Stage01";
        private const string SalmonPrefabPath =
            PrefabRoot + "/PF_Stage01_ProceduralSalmon.prefab";
        private const string KnifePrefabPath =
            PrefabRoot + "/PF_Stage01_ProceduralKnife.prefab";
        private const string JudgementPrefabPath =
            GeneratedRoot + "/Prefabs/UI/PF_UI_JudgementFeedback.prefab";
        private const string JudgementLibraryPath =
            GeneratedRoot + "/Data/JudgementVisualLibrary.asset";
        private const string PatternAssetPath =
            "Assets/_SashimiBoy/Data/Generated/" +
            "Stage01NotePattern.asset";

        private static readonly int[][] ManualPatternSteps =
        {
            new[] { 0, 2, 4, 6 },
            new[] { 0, 2, 4, 6 },
            new[] { 0, 2, 4, 6 },
            new[] { 0, 2, 4, 6 },
            new[] { 0, 2, 6 },
            new[] { 0, 3, 6 },
            new[] { 0, 4, 6 },
            new[] { 1, 2, 5, 6 },
            new[] { 0, 3, 4, 6 },
            new[] { 0, 2, 3, 6 },
            new[] { 1, 2, 4, 5 },
            new[] { 0, 4, 7 },
            new[] { 0, 1, 4, 6 },
            new[] { 0, 3, 5, 6 },
            new[] { 1, 2, 4, 7 },
            new[] { 0, 2, 3, 5, 6 }
        };

        private static readonly Color SalmonOrange =
            new Color(0.91f, 0.31f, 0.20f, 1f);
        private static readonly Color SalmonBack =
            new Color(0.16f, 0.25f, 0.26f, 1f);
        private static readonly Color SalmonBelly =
            new Color(1f, 0.73f, 0.55f, 1f);

        [MenuItem("Sashimi Boy/Art/Build Stage 01 Visual Prototype")]
        public static void BuildStage01VisualPrototype()
        {
            BuildAll(true);
        }

        [MenuItem("Sashimi Boy/Art/Open Stage 01 Salmon")]
        public static void OpenStage01Salmon()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(StageScenePath) ==
                null)
            {
                throw new FileNotFoundException(
                    "Stage01_Salmon scene is missing.",
                    StageScenePath);
            }

            EditorSceneManager.OpenScene(StageScenePath, OpenSceneMode.Single);
        }

        public static void BuildStage01VisualPrototypeBatch()
        {
            BuildAll(false);
        }

        public static void RebuildGeneratedPresentationForPrototypeGenerator()
        {
            BuildAll(false);
        }

        private static void BuildAll(bool showDialog)
        {
            EnsureFolders();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            CreateBackupOnce();

            StageMaterials materials = BuildMaterials();
            MeshAssets meshes = BuildMeshes();
            Stage01NotePatternDefinition pattern = BuildPatternDefinition();
            GameObject salmonPrefab = BuildSalmonPrefab(materials, meshes);
            GameObject knifePrefab = BuildKnifePrefab(materials, meshes);
            IntegrateStageScene(
                materials,
                meshes,
                salmonPrefab,
                knifePrefab,
                pattern);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                "[Sashimi Boy] Stage 01 visual prototype build complete: " +
                StageScenePath);

            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "Sashimi Boy",
                    "Stage 01 visual prototype was generated and validated.",
                    "OK");
            }
        }

        private static void CreateBackupOnce()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BackupScenePath) !=
                null || File.Exists(BackupScenePath))
            {
                return;
            }

            if (!AssetDatabase.CopyAsset(StageScenePath, BackupScenePath))
            {
                throw new InvalidOperationException(
                    "Could not create the one-time Stage01 scaffold backup.");
            }
        }

        private static Stage01NotePatternDefinition BuildPatternDefinition()
        {
            Stage01NotePatternDefinition pattern =
                AssetDatabase.LoadAssetAtPath<Stage01NotePatternDefinition>(
                    PatternAssetPath);
            if (pattern == null)
            {
                pattern = ScriptableObject.CreateInstance<
                    Stage01NotePatternDefinition>();
                AssetDatabase.CreateAsset(pattern, PatternAssetPath);
            }

            pattern.manualBarCount = ManualPatternSteps.Length;
            pattern.repeatFromBar = 9;
            pattern.subdivisionsPerBeat = 2;
            if (pattern.notes == null)
            {
                pattern.notes = new List<Stage01PatternNote>();
            }

            pattern.notes.Clear();
            for (int bar = 0; bar < ManualPatternSteps.Length; bar++)
            {
                int[] steps = ManualPatternSteps[bar];
                for (int i = 0; i < steps.Length; i++)
                {
                    int step = steps[i];
                    pattern.notes.Add(new Stage01PatternNote
                    {
                        barIndex = bar + 1,
                        eighthStepInBar = step,
                        label = EighthStepLabel(step)
                    });
                }
            }

            EditorUtility.SetDirty(pattern);
            return pattern;
        }

        private static string EighthStepLabel(int step)
        {
            int beat = step / 2 + 1;
            return step % 2 == 0 ? beat.ToString() : beat + "&";
        }

        private static StageMaterials BuildMaterials()
        {
            return new StageMaterials
            {
                background = Material("M_Stage01_Background", new Color(
                    0.025f, 0.032f, 0.037f, 1f), 0.12f),
                counter = Material("M_Stage01_Counter", new Color(
                    0.075f, 0.085f, 0.09f, 1f), 0.18f),
                boardBorder = Material("M_Stage01_BoardBorder", new Color(
                    0.23f, 0.12f, 0.055f, 1f), 0.22f),
                board = Material("M_Stage01_Board", new Color(
                    0.72f, 0.45f, 0.21f, 1f), 0.28f),
                boardGrain = Material("M_Stage01_BoardGrain", new Color(
                    0.39f, 0.20f, 0.08f, 1f), 0.18f),
                salmonBody = Material(
                    "M_Stage01_SalmonBody", SalmonOrange, 0.34f),
                salmonBack = Material(
                    "M_Stage01_SalmonBack", SalmonBack, 0.26f),
                salmonBelly = Material(
                    "M_Stage01_SalmonBelly", SalmonBelly, 0.3f),
                salmonStripe = Material("M_Stage01_SalmonStripe", new Color(
                    0.08f, 0.15f, 0.16f, 1f), 0.24f),
                salmonFin = Material("M_Stage01_SalmonFin", new Color(
                    0.30f, 0.12f, 0.09f, 1f), 0.2f),
                eyeWhite = Material("M_Stage01_EyeWhite", new Color(
                    0.96f, 0.94f, 0.84f, 1f), 0.4f),
                eyeDark = Material("M_Stage01_EyeDark", new Color(
                    0.015f, 0.018f, 0.02f, 1f), 0.5f),
                gill = Material("M_Stage01_Gill", new Color(
                    0.32f, 0.035f, 0.025f, 1f), 0.2f),
                cut = Material("M_Stage01_Cut", new Color(
                    0.92f, 0.97f, 1f, 1f), 0.15f),
                blade = Material("M_Stage01_Blade", new Color(
                    0.62f, 0.72f, 0.77f, 1f), 0.72f, 0.78f),
                bladeEdge = Material("M_Stage01_BladeEdge", new Color(
                    0.96f, 0.99f, 1f, 1f), 0.9f, 0.9f),
                handle = Material("M_Stage01_KnifeHandle", new Color(
                    0.035f, 0.045f, 0.05f, 1f), 0.2f),
                highlight = Material("M_Stage01_KnifeHighlight", new Color(
                    1f, 0.96f, 0.52f, 1f), 0.75f, 0.85f),
                cue = Material("M_Stage01_SliceCue", new Color(
                    0.35f, 0.92f, 1f, 1f), 0.4f, 0.5f),
                silhouette = Material("M_Stage01_BossSilhouette", new Color(
                    0.015f, 0.018f, 0.02f, 1f), 0.08f),
                plate = Material("M_Stage01_Plate", new Color(
                    0.62f, 0.70f, 0.72f, 1f), 0.45f)
            };
        }

        private static MeshAssets BuildMeshes()
        {
            Vector2[] tailPolygon =
            {
                new Vector2(-0.5f, 0f),
                new Vector2(0.2f, 0.12f),
                new Vector2(0.95f, 0.62f),
                new Vector2(0.72f, 0f),
                new Vector2(0.95f, -0.62f),
                new Vector2(0.2f, -0.12f)
            };
            Vector2[] finPolygon =
            {
                new Vector2(-0.45f, -0.08f),
                new Vector2(0.48f, 0f),
                new Vector2(-0.22f, 0.58f)
            };
            Vector2[] knifePolygon =
            {
                new Vector2(-0.18f, -0.85f),
                new Vector2(0.18f, -0.85f),
                new Vector2(0.24f, 0.62f),
                new Vector2(0f, 1f),
                new Vector2(-0.19f, 0.62f)
            };

            return new MeshAssets
            {
                tail = SaveMesh(
                    "Stage01_SalmonTail.asset",
                    CreateExtrudedPolygon(tailPolygon, 0.11f)),
                fin = SaveMesh(
                    "Stage01_SalmonFin.asset",
                    CreateExtrudedPolygon(finPolygon, 0.08f)),
                knifeBlade = SaveMesh(
                    "Stage01_KnifeBlade.asset",
                    CreateExtrudedPolygon(knifePolygon, 0.06f)),
                roundedBoard = SaveMesh(
                    "Stage01_RoundedBoard.asset",
                    CreateRoundedBoardMesh(9.25f, 2.65f, 0.28f, 0.16f))
            };
        }

        private static GameObject BuildSalmonPrefab(
            StageMaterials materials,
            MeshAssets meshes)
        {
            GameObject root = new GameObject(
                "PF_Stage01_ProceduralSalmon");
            try
            {
                ProceduralSalmonView view =
                    root.AddComponent<ProceduralSalmonView>();
                GameObject visualRoot = Child(root.transform, "VisualRoot");
                view.visualRoot = visualRoot.transform;

                Primitive(
                    "Body",
                    visualRoot.transform,
                    PrimitiveType.Sphere,
                    new Vector3(-0.05f, 0f, 0f),
                    new Vector3(5.6f, 0.38f, 1.2f),
                    Vector3.zero,
                    materials.salmonBody);
                Primitive(
                    "DarkBack",
                    visualRoot.transform,
                    PrimitiveType.Sphere,
                    new Vector3(-0.05f, 0.18f, 0.3f),
                    new Vector3(4.85f, 0.12f, 0.62f),
                    Vector3.zero,
                    materials.salmonBack);
                Primitive(
                    "Belly",
                    visualRoot.transform,
                    PrimitiveType.Sphere,
                    new Vector3(-0.28f, 0.2f, -0.33f),
                    new Vector3(4.45f, 0.11f, 0.48f),
                    Vector3.zero,
                    materials.salmonBelly);

                GameObject head = Child(visualRoot.transform, "Head");
                head.transform.localPosition = new Vector3(-2.78f, 0f, 0f);
                Primitive(
                    "HeadForm",
                    head.transform,
                    PrimitiveType.Sphere,
                    Vector3.zero,
                    new Vector3(1.28f, 0.42f, 1.04f),
                    Vector3.zero,
                    materials.salmonBody);
                Primitive(
                    "Snout",
                    head.transform,
                    PrimitiveType.Sphere,
                    new Vector3(-0.53f, -0.01f, -0.01f),
                    new Vector3(0.62f, 0.34f, 0.72f),
                    Vector3.zero,
                    materials.salmonBody);
                GameObject eye = Child(head.transform, "Eye");
                eye.transform.localPosition = new Vector3(-0.2f, 0.27f, -0.33f);
                Primitive(
                    "EyeWhite",
                    eye.transform,
                    PrimitiveType.Sphere,
                    Vector3.zero,
                    new Vector3(0.21f, 0.09f, 0.21f),
                    Vector3.zero,
                    materials.eyeWhite);
                Primitive(
                    "Pupil",
                    eye.transform,
                    PrimitiveType.Sphere,
                    new Vector3(-0.025f, 0.055f, 0f),
                    new Vector3(0.10f, 0.065f, 0.10f),
                    Vector3.zero,
                    materials.eyeDark);
                Primitive(
                    "Gill",
                    head.transform,
                    PrimitiveType.Cube,
                    new Vector3(0.23f, 0.26f, -0.36f),
                    new Vector3(0.045f, 0.035f, 0.58f),
                    new Vector3(0f, -18f, 0f),
                    materials.gill);

                MeshObject(
                    "Tail",
                    visualRoot.transform,
                    meshes.tail,
                    new Vector3(2.9f, 0.02f, 0f),
                    Vector3.one,
                    Vector3.zero,
                    materials.salmonFin);
                MeshObject(
                    "DorsalFin",
                    visualRoot.transform,
                    meshes.fin,
                    new Vector3(0.35f, 0.24f, 0.42f),
                    new Vector3(0.8f, 0.85f, 0.78f),
                    new Vector3(0f, 7f, 0f),
                    materials.salmonFin);
                MeshObject(
                    "SideFin",
                    visualRoot.transform,
                    meshes.fin,
                    new Vector3(-1.05f, 0.25f, -0.47f),
                    new Vector3(0.7f, 0.8f, 0.72f),
                    new Vector3(0f, 188f, 0f),
                    materials.salmonFin);

                GameObject stripes =
                    Child(visualRoot.transform, "SalmonStripes");
                for (int i = 0; i < 6; i++)
                {
                    Primitive(
                        $"Stripe_{i + 1:00}",
                        stripes.transform,
                        PrimitiveType.Capsule,
                        new Vector3(-1.45f + i * 0.58f, 0.27f, 0.16f),
                        new Vector3(0.095f, 0.21f, 0.075f),
                        new Vector3(90f, i % 2 == 0 ? -14f : 14f, 0f),
                        materials.salmonStripe);
                }

                GameObject cutMarksRoot =
                    Child(visualRoot.transform, "CutMarks");
                var marks = new GameObject[8];
                var renderers = new Renderer[8];
                for (int i = 0; i < marks.Length; i++)
                {
                    marks[i] = Primitive(
                        $"CutMark_{i + 1:00}",
                        cutMarksRoot.transform,
                        PrimitiveType.Cube,
                        Vector3.zero,
                        new Vector3(0.045f, 0.025f, 1.05f),
                        Vector3.zero,
                        materials.cut);
                    renderers[i] = marks[i].GetComponent<Renderer>();
                    marks[i].SetActive(false);
                }

                GameObject scratch = Primitive(
                    "WhackScratch",
                    cutMarksRoot.transform,
                    PrimitiveType.Cube,
                    Vector3.zero,
                    new Vector3(0.07f, 0.028f, 0.76f),
                    Vector3.zero,
                    materials.gill);
                scratch.SetActive(false);
                Child(visualRoot.transform, "SlicePieces");

                view.cutMarks = marks;
                view.cutMarkRenderers = renderers;
                view.whackScratch = scratch;
                view.whackScratchRenderer = scratch.GetComponent<Renderer>();
                view.cutsPerFish = 8;
                view.cutStartLocalX = -1.85f;
                view.cutEndLocalX = 1.85f;
                view.cutLocalY = 0.25f;
                view.cutLocalZ = 0f;
                view.completedFishOffset = new Vector3(3.1f, 0.25f, 1.25f);

                return PrefabUtility.SaveAsPrefabAsset(root, SalmonPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static GameObject BuildKnifePrefab(
            StageMaterials materials,
            MeshAssets meshes)
        {
            GameObject root = new GameObject("PF_Stage01_ProceduralKnife");
            try
            {
                KnifeVisualController controller =
                    root.AddComponent<KnifeVisualController>();
                GameObject visualRoot = Child(root.transform, "VisualRoot");
                GameObject motionRoot = Child(visualRoot.transform, "MotionRoot");
                GameObject blade = MeshObject(
                    "Blade",
                    motionRoot.transform,
                    meshes.knifeBlade,
                    new Vector3(0f, 0f, 0.28f),
                    new Vector3(0.95f, 1f, 0.95f),
                    Vector3.zero,
                    materials.blade);
                Primitive(
                    "BladeEdge",
                    motionRoot.transform,
                    PrimitiveType.Cube,
                    new Vector3(0.18f, 0.065f, 0.22f),
                    new Vector3(0.035f, 0.035f, 1.45f),
                    new Vector3(0f, 2f, 0f),
                    materials.bladeEdge);
                Primitive(
                    "Handle",
                    motionRoot.transform,
                    PrimitiveType.Capsule,
                    new Vector3(0f, 0f, -0.92f),
                    new Vector3(0.29f, 0.38f, 0.24f),
                    new Vector3(90f, 0f, 0f),
                    materials.handle);
                GameObject highlight = Primitive(
                    "Highlight",
                    motionRoot.transform,
                    PrimitiveType.Cube,
                    new Vector3(-0.08f, 0.075f, 0.38f),
                    new Vector3(0.025f, 0.025f, 1.05f),
                    Vector3.zero,
                    materials.highlight);
                highlight.SetActive(false);

                controller.visualRoot = visualRoot;
                controller.motionRoot = motionRoot.transform;
                controller.highlight = highlight;
                controller.slashDownDuration = 0.1f;
                controller.returnDuration = 0.15f;
                controller.slashTravel = 1.15f;
                controller.windupTravel = 0.18f;

                EditorUtility.SetDirty(blade);
                return PrefabUtility.SaveAsPrefabAsset(root, KnifePrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void IntegrateStageScene(
            StageMaterials materials,
            MeshAssets meshes,
            GameObject salmonPrefab,
            GameObject knifePrefab,
            Stage01NotePatternDefinition pattern)
        {
            Scene scene = EditorSceneManager.OpenScene(
                StageScenePath,
                OpenSceneMode.Single);

            DestroyNamedSceneObjects(
                scene,
                "Stage01_CuttingBoard",
                "Stage01_SalmonPlaceholder",
                "Stage01_KnifeLine",
                "Stage01_SliceLinesRoot",
                "Stage01_BoardRoot",
                "Stage01_VisualPrototype",
                "Stage01_Counter_BackBand",
                "Stage01_KitchenSurround",
                "Stage01Salmon_HUDCanvas");

            Stage01SalmonTimingScaffold timing =
                FindInScene<Stage01SalmonTimingScaffold>(scene);
            if (timing == null)
            {
                throw new InvalidOperationException(
                    "Stage01Salmon_TimingScaffold is missing.");
            }

            ConfigureStageCameraAndAudio(scene, timing);
            ConfigureBackground(scene, materials, meshes);

            GameObject visualRoot = new GameObject("Stage01_VisualPrototype");
            SceneManager.MoveGameObjectToScene(visualRoot, scene);

            GameObject boardRoot = BuildBoard(
                visualRoot.transform,
                materials,
                meshes);
            boardRoot.name = "Stage01_BoardRoot";

            GameObject salmonInstance = PrefabUtility.InstantiatePrefab(
                salmonPrefab,
                scene) as GameObject;
            salmonInstance.name = "Stage01_ProceduralSalmon";
            salmonInstance.transform.SetParent(visualRoot.transform, false);
            salmonInstance.transform.position = new Vector3(0f, 0.42f, 0.12f);
            ProceduralSalmonView salmon =
                salmonInstance.GetComponent<ProceduralSalmonView>();

            GameObject playerKnifeObject = PrefabUtility.InstantiatePrefab(
                knifePrefab,
                scene) as GameObject;
            playerKnifeObject.name = "Stage01_PlayerKnife";
            playerKnifeObject.transform.SetParent(visualRoot.transform, true);
            playerKnifeObject.transform.position = new Vector3(
                -1.85f,
                0.82f,
                -1.48f);
            KnifeVisualController playerKnife =
                playerKnifeObject.GetComponent<KnifeVisualController>();

            GameObject presentationRoot = new GameObject(
                "Stage01_Presentation");
            presentationRoot.transform.SetParent(visualRoot.transform, false);

            Stage01NotePatternProvider patternProvider =
                presentationRoot.AddComponent<Stage01NotePatternProvider>();
            patternProvider.pattern = pattern;
            patternProvider.timing = timing;
            Stage01ActiveNoteTracker noteTracker =
                presentationRoot.AddComponent<Stage01ActiveNoteTracker>();
            noteTracker.provider = patternProvider;

            CueObjects cue = BuildSliceCue(
                presentationRoot.transform,
                materials.cue);
            SliceCuePresenter cuePresenter =
                presentationRoot.AddComponent<SliceCuePresenter>();
            cuePresenter.cueRoot = cue.root;
            cuePresenter.cutGuideLine = cue.line;
            cuePresenter.leftBracket = cue.left;
            cuePresenter.rightBracket = cue.right;
            cuePresenter.forecastLeftBracket = cue.forecastLeft;
            cuePresenter.forecastRightBracket = cue.forecastRight;
            cuePresenter.bracketRenderers = cue.renderers.ToArray();
            cuePresenter.upcomingGhostLines = cue.ghostLines.ToArray();
            cuePresenter.upcomingGhostRenderers =
                cue.ghostRenderers.ToArray();
            cuePresenter.sourceMode = SliceCueSourceMode.StagePattern;
            cuePresenter.tutorialTargetCount = 12;

            BossObjects boss = BuildBossDemo(
                visualRoot.transform,
                materials,
                knifePrefab,
                scene);

            HudObjects hudObjects = BuildHud(scene);
            cuePresenter.spacePromptText = hudObjects.spacePrompt;
            cuePresenter.nowText = hudObjects.nowPrompt;
            cuePresenter.restPromptText = hudObjects.restPrompt;
            cuePresenter.rhythmLane = hudObjects.rhythmLane;
            cuePresenter.rhythmLaneBackground =
                hudObjects.rhythmLaneBackground;
            cuePresenter.hitCursor = hudObjects.hitCursor;
            cuePresenter.upcomingNoteDots = hudObjects.upcomingNoteDots;

            BossDemoPresenter bossPresenter =
                presentationRoot.AddComponent<BossDemoPresenter>();
            bossPresenter.bossSilhouetteRoot = boss.silhouetteRoot;
            bossPresenter.bossKnife = boss.knife;
            bossPresenter.hud = hudObjects.hud;
            bossPresenter.patternProvider = patternProvider;
            bossPresenter.salmon = salmon;

            Stage01SalmonPresentationController presentation =
                presentationRoot.AddComponent<
                    Stage01SalmonPresentationController>();
            presentation.timing = timing;
            presentation.salmon = salmon;
            presentation.playerKnife = playerKnife;
            presentation.sliceCue = cuePresenter;
            presentation.hud = hudObjects.hud;
            presentation.bossDemo = bossPresenter;
            presentation.judgementFeedback = hudObjects.feedback;
            presentation.notePatternProvider = patternProvider;
            presentation.activeNoteTracker = noteTracker;

            cuePresenter.timing = timing;
            cuePresenter.salmon = salmon;
            cuePresenter.playerKnife = playerKnife;
            cuePresenter.activeNoteTracker = noteTracker;
            cuePresenter.bossDemo = bossPresenter;
            bossPresenter.timing = timing;
            patternProvider.Initialize(timing);
            noteTracker.Initialize(patternProvider);
            bossPresenter.Bind(
                timing,
                hudObjects.hud,
                patternProvider,
                salmon);
            hudObjects.hud.Bind(timing, salmon);

            timing.presentationController = presentation;
            timing.notePatternProvider = patternProvider;
            timing.activeNoteTracker = noteTracker;
            timing.createDebugUiIfMissing = false;
            timing.hudText = null;
            timing.judgeText = null;
            timing.dialogueText = null;
            timing.progressFill = null;
            timing.yieldFill = null;
            timing.warningFlash = null;
            timing.missingClipWarningText = hudObjects.missingClip;
            timing.resultRoot = hudObjects.hud.resultRoot;
            timing.resultText = hudObjects.hud.resultText;
            timing.judgementFeedback = hudObjects.feedback;
            timing.judgementVisualLibrary =
                AssetDatabase.LoadAssetAtPath<JudgementVisualLibrary>(
                    JudgementLibraryPath);
            if (hudObjects.feedback != null)
            {
                hudObjects.feedback.visualLibrary =
                    timing.judgementVisualLibrary;
                hudObjects.feedback.HideImmediate();
            }

            DisableLegacyWorldLabels(scene);
            EnsureOneEventSystem(scene);
            ValidateScene(
                scene,
                timing,
                salmon,
                playerKnife,
                pattern,
                patternProvider,
                noteTracker,
                cuePresenter,
                bossPresenter,
                hudObjects);

            EditorUtility.SetDirty(timing);
            EditorUtility.SetDirty(presentation);
            EditorUtility.SetDirty(cuePresenter);
            EditorUtility.SetDirty(bossPresenter);
            EditorUtility.SetDirty(patternProvider);
            EditorUtility.SetDirty(noteTracker);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, StageScenePath);
        }

        private static void ConfigureStageCameraAndAudio(
            Scene scene,
            Stage01SalmonTimingScaffold timing)
        {
            Camera camera = FindInScene<Camera>(scene);
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                camera = cameraObject.AddComponent<Camera>();
            }

            camera.gameObject.name = "Main Camera";
            camera.gameObject.tag = "MainCamera";
            camera.orthographic = true;
            camera.orthographicSize = 3.25f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.03f, 0.035f, 1f);
            camera.transform.SetPositionAndRotation(
                new Vector3(0f, 8f, 0f),
                Quaternion.Euler(90f, 0f, 0f));

            AudioListener mainListener = camera.GetComponent<AudioListener>();
            if (mainListener == null)
            {
                mainListener = camera.gameObject.AddComponent<AudioListener>();
            }

            mainListener.enabled = true;
            AudioListener[] listeners = UnityEngine.Object.FindObjectsByType<
                AudioListener>(
                FindObjectsInactive.Include);
            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] != mainListener)
                {
                    listeners[i].enabled = false;
                }
            }

            AudioClip music = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);
            if (timing.audioSource == null)
            {
                timing.audioSource = timing.GetComponent<AudioSource>();
            }

            if (timing.audioSource == null)
            {
                timing.audioSource = timing.gameObject.AddComponent<AudioSource>();
            }

            timing.audioSource.clip = music;
            timing.audioSource.playOnAwake = false;
            timing.audioSource.volume = 1f;
            timing.audioSource.mute = false;
            timing.audioSource.spatialBlend = 0f;
            timing.musicClip = music;

            if (timing.audioClock == null)
            {
                timing.audioClock = timing.GetComponent<AudioClock>();
            }

            if (timing.audioClock == null)
            {
                timing.audioClock = timing.gameObject.AddComponent<AudioClock>();
            }

            timing.audioClock.audioSource = timing.audioSource;
            timing.audioClock.playOnStart = false;
        }

        private static void ConfigureBackground(
            Scene scene,
            StageMaterials materials,
            MeshAssets meshes)
        {
            GameObject floor = FindNamed(scene, "Stage01_Floor");
            if (floor == null)
            {
                floor = Primitive(
                    "Stage01_Floor",
                    null,
                    PrimitiveType.Cube,
                    Vector3.zero,
                    new Vector3(12f, 0.08f, 7f),
                    Vector3.zero,
                    materials.background);
                SceneManager.MoveGameObjectToScene(floor, scene);
            }
            else
            {
                floor.transform.position = Vector3.zero;
                floor.transform.rotation = Quaternion.identity;
                floor.transform.localScale = new Vector3(12f, 0.08f, 7f);
                Renderer renderer = floor.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = materials.background;
                }
            }

            GameObject counterBack = Primitive(
                "Stage01_Counter_BackBand",
                null,
                PrimitiveType.Cube,
                new Vector3(0f, 0.06f, 2.72f),
                new Vector3(12f, 0.1f, 1.15f),
                Vector3.zero,
                materials.counter);
            SceneManager.MoveGameObjectToScene(counterBack, scene);

            GameObject surround = new GameObject("Stage01_KitchenSurround");
            SceneManager.MoveGameObjectToScene(surround, scene);
            Primitive(
                "PrepCounter_Left",
                surround.transform,
                PrimitiveType.Cube,
                new Vector3(-5.45f, 0.08f, 0.1f),
                new Vector3(1.05f, 0.12f, 5.8f),
                Vector3.zero,
                materials.counter);
            Primitive(
                "PrepCounter_Right",
                surround.transform,
                PrimitiveType.Cube,
                new Vector3(5.45f, 0.08f, 0.1f),
                new Vector3(1.05f, 0.12f, 5.8f),
                Vector3.zero,
                materials.counter);
            Primitive(
                "CounterFrontRail",
                surround.transform,
                PrimitiveType.Cube,
                new Vector3(0f, 0.09f, -2.72f),
                new Vector3(12f, 0.13f, 0.42f),
                Vector3.zero,
                materials.boardBorder);
            for (int i = 0; i < 7; i++)
            {
                Primitive(
                    $"BackTileJoint_{i + 1:00}",
                    surround.transform,
                    PrimitiveType.Cube,
                    new Vector3(-5.1f + i * 1.7f, 0.125f, 2.72f),
                    new Vector3(0.018f, 0.018f, 1.08f),
                    Vector3.zero,
                    materials.plate);
            }

            Primitive(
                "IngredientTray_Left",
                surround.transform,
                PrimitiveType.Cube,
                new Vector3(-4.72f, 0.22f, 1.58f),
                new Vector3(0.76f, 0.1f, 1.14f),
                Vector3.zero,
                materials.plate);
            Primitive(
                "IngredientTray_Right",
                surround.transform,
                PrimitiveType.Cube,
                new Vector3(4.72f, 0.22f, 1.58f),
                new Vector3(0.76f, 0.1f, 1.14f),
                Vector3.zero,
                materials.plate);
            Primitive(
                "KnifeRest",
                surround.transform,
                PrimitiveType.Cube,
                new Vector3(3.95f, 0.2f, -1.92f),
                new Vector3(1.3f, 0.1f, 0.26f),
                new Vector3(0f, -8f, 0f),
                materials.boardBorder);
        }

        private static GameObject BuildBoard(
            Transform parent,
            StageMaterials materials,
            MeshAssets meshes)
        {
            GameObject root = Child(parent, "Stage01_BoardRoot");
            MeshObject(
                "BoardShadow",
                root.transform,
                meshes.roundedBoard,
                new Vector3(0.08f, 0.12f, -0.08f),
                new Vector3(1.025f, 1f, 1.055f),
                Vector3.zero,
                materials.background);
            MeshObject(
                "BoardBorder",
                root.transform,
                meshes.roundedBoard,
                new Vector3(0f, 0.17f, 0f),
                Vector3.one,
                Vector3.zero,
                materials.boardBorder);
            MeshObject(
                "BoardSurface",
                root.transform,
                meshes.roundedBoard,
                new Vector3(0f, 0.255f, 0f),
                new Vector3(0.965f, 0.65f, 0.89f),
                Vector3.zero,
                materials.board);

            float[] grainZ = { -0.72f, -0.28f, 0.24f, 0.69f };
            for (int i = 0; i < grainZ.Length; i++)
            {
                Primitive(
                    $"WoodGrain_{i + 1:00}",
                    root.transform,
                    PrimitiveType.Cube,
                    new Vector3((i % 2 == 0 ? -0.2f : 0.22f), 0.32f,
                        grainZ[i]),
                    new Vector3(7.4f - i * 0.35f, 0.018f, 0.025f),
                    new Vector3(0f, i % 2 == 0 ? 1.5f : -1.2f, 0f),
                    materials.boardGrain);
            }

            GameObject plate = Primitive(
                "CompletedFishPlate",
                root.transform,
                PrimitiveType.Cylinder,
                new Vector3(4.15f, 0.25f, 1.55f),
                new Vector3(0.88f, 0.035f, 0.64f),
                Vector3.zero,
                materials.plate);
            DisableCollider(plate);
            return root;
        }

        private static CueObjects BuildSliceCue(
            Transform parent,
            Material cueMaterial)
        {
            var cue = new CueObjects();
            cue.root = Child(parent, "Stage01_SliceCueWorld");
            cue.line = Primitive(
                "CutGuideLine",
                cue.root.transform,
                PrimitiveType.Cube,
                Vector3.zero,
                new Vector3(0.035f, 0.035f, 1.28f),
                Vector3.zero,
                cueMaterial).GetComponent<Renderer>();
            cue.left = BuildBracket(
                cue.root.transform,
                "LeftBracket",
                false,
                1f,
                cueMaterial,
                cue.renderers);
            cue.right = BuildBracket(
                cue.root.transform,
                "RightBracket",
                true,
                1f,
                cueMaterial,
                cue.renderers);
            cue.forecastLeft = BuildBracket(
                cue.root.transform,
                "ForecastLeftBracket",
                false,
                0.68f,
                cueMaterial,
                cue.renderers);
            cue.forecastRight = BuildBracket(
                cue.root.transform,
                "ForecastRightBracket",
                true,
                0.68f,
                cueMaterial,
                cue.renderers);
            for (int i = 0; i < 3; i++)
            {
                GameObject ghost = Primitive(
                    $"UpcomingGhostGuide_{i + 1:00}",
                    cue.root.transform,
                    PrimitiveType.Cube,
                    Vector3.zero,
                    new Vector3(
                        0.024f - i * 0.003f,
                        0.026f,
                        1.05f - i * 0.12f),
                    Vector3.zero,
                    cueMaterial);
                cue.ghostLines.Add(ghost.transform);
                cue.ghostRenderers.Add(ghost.GetComponent<Renderer>());
                ghost.SetActive(false);
            }

            cue.root.SetActive(false);
            return cue;
        }

        private static Transform BuildBracket(
            Transform parent,
            string name,
            bool facesLeft,
            float scale,
            Material material,
            List<Renderer> renderers)
        {
            GameObject root = Child(parent, name);
            float capX = facesLeft ? -0.08f : 0.08f;
            GameObject vertical = Primitive(
                "Stem",
                root.transform,
                PrimitiveType.Cube,
                Vector3.zero,
                new Vector3(0.035f, 0.03f, 0.72f * scale),
                Vector3.zero,
                material);
            GameObject top = Primitive(
                "TopCap",
                root.transform,
                PrimitiveType.Cube,
                new Vector3(capX, 0f, 0.34f * scale),
                new Vector3(0.18f, 0.03f, 0.035f),
                Vector3.zero,
                material);
            GameObject bottom = Primitive(
                "BottomCap",
                root.transform,
                PrimitiveType.Cube,
                new Vector3(capX, 0f, -0.34f * scale),
                new Vector3(0.18f, 0.03f, 0.035f),
                Vector3.zero,
                material);
            renderers.Add(vertical.GetComponent<Renderer>());
            renderers.Add(top.GetComponent<Renderer>());
            renderers.Add(bottom.GetComponent<Renderer>());
            return root.transform;
        }

        private static BossObjects BuildBossDemo(
            Transform parent,
            StageMaterials materials,
            GameObject knifePrefab,
            Scene scene)
        {
            var result = new BossObjects();
            result.silhouetteRoot = Child(parent, "Stage01_BossDemoVisuals");
            result.silhouetteRoot.transform.localPosition =
                new Vector3(-4.5f, 0.34f, 1.45f);
            Primitive(
                "ApronSilhouette",
                result.silhouetteRoot.transform,
                PrimitiveType.Capsule,
                Vector3.zero,
                new Vector3(1.05f, 0.18f, 0.72f),
                new Vector3(90f, 0f, -18f),
                materials.silhouette);
            Primitive(
                "HandSilhouette",
                result.silhouetteRoot.transform,
                PrimitiveType.Sphere,
                new Vector3(0.72f, 0.08f, -0.38f),
                new Vector3(0.52f, 0.2f, 0.34f),
                Vector3.zero,
                materials.silhouette);
            for (int i = 0; i < 3; i++)
            {
                Primitive(
                    $"Finger_{i + 1}",
                    result.silhouetteRoot.transform,
                    PrimitiveType.Capsule,
                    new Vector3(0.9f + i * 0.12f, 0.08f,
                        -0.52f + i * 0.12f),
                    new Vector3(0.11f, 0.24f, 0.1f),
                    new Vector3(90f, 0f, -35f),
                    materials.silhouette);
            }

            GameObject knife = PrefabUtility.InstantiatePrefab(
                knifePrefab,
                scene) as GameObject;
            knife.name = "Stage01_BossKnife_Demo";
            knife.transform.SetParent(parent, true);
            knife.transform.position = new Vector3(-1.45f, 0.86f, 0.48f);
            knife.transform.localScale = Vector3.one * 0.84f;
            result.knife = knife.GetComponent<KnifeVisualController>();
            return result;
        }

        private static HudObjects BuildHud(Scene scene)
        {
            var result = new HudObjects();
            GameObject canvasObject = new GameObject(
                "Stage01Salmon_HUDCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(Stage01SalmonHUD));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2200;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            result.hud = canvasObject.GetComponent<Stage01SalmonHUD>();

            RectTransform flash = Panel(
                canvasObject.transform,
                "ScreenReactionFlash",
                Vector2.zero,
                Vector2.one,
                Color.clear);
            result.hud.screenFlash = flash.GetComponent<Image>();
            result.hud.screenFlash.raycastTarget = false;

            RectTransform topLeft = FixedPanel(
                canvasObject.transform,
                "TopLeft_Status",
                new Vector2(0f, 1f),
                new Vector2(34f, -28f),
                new Vector2(440f, 166f),
                new Vector2(0f, 1f));
            AddBackground(topLeft, new Color(0.015f, 0.02f, 0.025f, 0.78f));
            result.hud.stageTitleText = FixedText(
                topLeft,
                "StageTitle",
                new Vector2(390f, 42f),
                new Vector2(24f, -20f),
                new Vector2(0f, 1f),
                27,
                TextAnchor.MiddleLeft,
                new Color(1f, 0.78f, 0.27f, 1f));
            result.hud.fishTypeText = FixedText(
                topLeft,
                "FishTypeText",
                new Vector2(390f, 30f),
                new Vector2(24f, -58f),
                new Vector2(0f, 1f),
                17,
                TextAnchor.MiddleLeft,
                new Color(0.62f, 0.9f, 0.94f, 1f));
            result.hud.yieldText = FixedText(
                topLeft,
                "YieldText",
                new Vector2(390f, 34f),
                new Vector2(24f, -92f),
                new Vector2(0f, 1f),
                22,
                TextAnchor.MiddleLeft,
                Color.white);
            result.hud.yieldFill = Bar(
                topLeft,
                "YieldBar",
                new Vector2(24f, -136f),
                new Vector2(390f, 12f),
                new Color(1f, 0.69f, 0.19f, 1f));

            RectTransform topCenter = FixedPanel(
                canvasObject.transform,
                "TopCenter_Rhythm",
                new Vector2(0.5f, 1f),
                new Vector2(0f, -28f),
                new Vector2(680f, 176f),
                new Vector2(0.5f, 1f));
            AddBackground(topCenter, new Color(0.015f, 0.02f, 0.025f, 0.72f));
            result.hud.songProgressFill = Bar(
                topCenter,
                "SongProgress",
                new Vector2(0f, -25f),
                new Vector2(620f, 14f),
                new Color(0.28f, 0.84f, 0.94f, 1f),
                new Vector2(0.5f, 1f));
            result.hud.beatDots = new Image[4];
            Sprite dotSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(
                "UI/Skin/Knob.psd");
            for (int i = 0; i < 4; i++)
            {
                RectTransform dot = FixedPanel(
                    topCenter,
                    $"Beat_{i + 1}",
                    new Vector2(0.5f, 0.5f),
                    new Vector2(-66f + i * 44f, 18f),
                    new Vector2(22f, 22f),
                    new Vector2(0.5f, 0.5f));
                Image image = AddBackground(dot, Color.white);
                image.sprite = dotSprite;
                image.preserveAspect = true;
                result.hud.beatDots[i] = image;
            }

            result.rhythmLane = FixedPanel(
                topCenter,
                "UpcomingRhythmLane",
                new Vector2(0.5f, 0f),
                new Vector2(0f, 24f),
                new Vector2(500f, 44f),
                new Vector2(0.5f, 0.5f));
            result.rhythmLaneBackground = AddBackground(
                result.rhythmLane,
                new Color(0.035f, 0.055f, 0.065f, 0.92f));
            RectTransform hitCursor = FixedPanel(
                result.rhythmLane,
                "HitCursor",
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(4f, 30f),
                new Vector2(0.5f, 0.5f));
            result.hitCursor = AddBackground(
                hitCursor,
                new Color(1f, 0.78f, 0.24f, 0.92f));
            result.hitCursor.raycastTarget = false;
            result.restPrompt = FixedText(
                result.rhythmLane,
                "RestPrompt",
                new Vector2(160f, 30f),
                new Vector2(-130f, 0f),
                new Vector2(0.5f, 0.5f),
                16,
                TextAnchor.MiddleCenter,
                new Color(0.5f, 0.78f, 0.84f, 0.72f));
            result.restPrompt.text = "REST / WAIT";
            result.restPrompt.raycastTarget = false;
            result.restPrompt.gameObject.SetActive(false);
            result.upcomingNoteDots = new Image[4];
            for (int i = 0; i < result.upcomingNoteDots.Length; i++)
            {
                RectTransform noteDot = FixedPanel(
                    result.rhythmLane,
                    $"UpcomingNote_{i + 1}",
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(18f, 18f),
                    new Vector2(0.5f, 0.5f));
                Image image = AddBackground(noteDot, Color.white);
                image.sprite = dotSprite;
                image.preserveAspect = true;
                image.raycastTarget = false;
                noteDot.gameObject.SetActive(false);
                result.upcomingNoteDots[i] = image;
            }

            result.rhythmLane.gameObject.SetActive(false);

            RectTransform topRight = FixedPanel(
                canvasObject.transform,
                "TopRight_Score",
                new Vector2(1f, 1f),
                new Vector2(-34f, -28f),
                new Vector2(400f, 166f),
                Vector2.one);
            AddBackground(topRight, new Color(0.015f, 0.02f, 0.025f, 0.78f));
            result.hud.scoreText = FixedText(
                topRight,
                "ScoreText",
                new Vector2(350f, 36f),
                new Vector2(-24f, -24f),
                Vector2.one,
                25,
                TextAnchor.MiddleRight,
                Color.white);
            result.hud.comboText = FixedText(
                topRight,
                "ComboText",
                new Vector2(350f, 36f),
                new Vector2(-24f, -70f),
                Vector2.one,
                24,
                TextAnchor.MiddleRight,
                new Color(0.36f, 0.9f, 1f, 1f));
            result.hud.fishProgressText = FixedText(
                topRight,
                "FishProgressText",
                new Vector2(350f, 32f),
                new Vector2(-24f, -116f),
                Vector2.one,
                19,
                TextAnchor.MiddleRight,
                new Color(1f, 0.78f, 0.38f, 1f));

            RectTransform dialogue = Panel(
                canvasObject.transform,
                "DialogueBox",
                new Vector2(0.09f, 0.025f),
                new Vector2(0.91f, 0.155f),
                new Color(0.01f, 0.015f, 0.02f, 0.86f));
            RectTransform dialogueAccent = FixedPanel(
                dialogue,
                "DialogueAccent",
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0f),
                new Vector2(6f, 94f),
                new Vector2(0f, 0.5f));
            AddBackground(
                dialogueAccent,
                new Color(1f, 0.46f, 0.18f, 1f));
            result.hud.dialogueText = StretchText(
                dialogue,
                "DialogueText",
                new Vector2(38f, 20f),
                new Vector2(-38f, -20f),
                27,
                TextAnchor.MiddleLeft,
                Color.white);

            RectTransform inspirationPopup = FixedPanel(
                canvasObject.transform,
                "InspirationPopup",
                new Vector2(0.5f, 1f),
                new Vector2(0f, -212f),
                new Vector2(470f, 62f),
                new Vector2(0.5f, 1f));
            AddBackground(
                inspirationPopup,
                new Color(0.06f, 0.075f, 0.08f, 0.94f));
            result.hud.inspirationRoot = inspirationPopup.gameObject;
            result.hud.inspirationText = StretchText(
                inspirationPopup,
                "InspirationMessage",
                new Vector2(22f, 8f),
                new Vector2(-22f, -8f),
                28,
                TextAnchor.MiddleCenter,
                new Color(1f, 0.82f, 0.28f, 1f));
            inspirationPopup.gameObject.SetActive(false);
            result.hud.countdownText = FixedText(
                canvasObject.transform,
                "CountdownText",
                new Vector2(380f, 130f),
                new Vector2(0f, 70f),
                new Vector2(0.5f, 0.5f),
                78,
                TextAnchor.MiddleCenter,
                new Color(1f, 0.82f, 0.28f, 1f));
            result.hud.noteEventText = FixedText(
                canvasObject.transform,
                "NoteEventText",
                new Vector2(360f, 44f),
                new Vector2(0f, 138f),
                new Vector2(0.5f, 0.5f),
                27,
                TextAnchor.MiddleCenter,
                new Color(1f, 0.2f, 0.12f, 1f));
            result.spacePrompt = FixedText(
                canvasObject.transform,
                "SpacePrompt",
                new Vector2(340f, 56f),
                new Vector2(0f, -172f),
                new Vector2(0.5f, 0.5f),
                30,
                TextAnchor.MiddleCenter,
                Color.white);
            result.spacePrompt.text = "[ SPACE ]";
            result.nowPrompt = FixedText(
                canvasObject.transform,
                "NowPrompt",
                new Vector2(260f, 70f),
                new Vector2(0f, -78f),
                new Vector2(0.5f, 0.5f),
                40,
                TextAnchor.MiddleCenter,
                new Color(1f, 0.85f, 0.22f, 1f));
            result.nowPrompt.text = "NOW!";
            result.spacePrompt.gameObject.SetActive(false);
            result.nowPrompt.gameObject.SetActive(false);

            result.missingClip = FixedText(
                canvasObject.transform,
                "MissingClipWarning",
                new Vector2(720f, 90f),
                Vector2.zero,
                new Vector2(0.5f, 0.5f),
                42,
                TextAnchor.MiddleCenter,
                new Color(1f, 0.08f, 0.04f, 1f));
            result.missingClip.gameObject.SetActive(false);
            result.hud.missingClipText = result.missingClip;

            RectTransform resultPanel = FixedPanel(
                canvasObject.transform,
                "ResultPlaceholder",
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(600f, 380f),
                new Vector2(0.5f, 0.5f));
            AddBackground(resultPanel, new Color(0.01f, 0.015f, 0.02f, 0.94f));
            result.hud.resultRoot = resultPanel.gameObject;
            result.hud.resultText = StretchText(
                resultPanel,
                "ResultText",
                new Vector2(35f, 30f),
                new Vector2(-35f, -30f),
                34,
                TextAnchor.MiddleCenter,
                Color.white);
            resultPanel.gameObject.SetActive(false);

            result.feedback = InstantiateJudgementFeedback(
                canvasObject.transform,
                scene);
            return result;
        }

        private static JudgementFeedbackView InstantiateJudgementFeedback(
            Transform canvas,
            Scene scene)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                JudgementPrefabPath);
            JudgementFeedbackView feedback;
            if (prefab != null)
            {
                GameObject instance = PrefabUtility.InstantiatePrefab(
                    prefab,
                    scene) as GameObject;
                instance.name = "JudgementFeedback";
                instance.transform.SetParent(canvas, false);
                feedback = instance.GetComponent<JudgementFeedbackView>();
            }
            else
            {
                RectTransform root = FixedPanel(
                    canvas,
                    "JudgementFeedback",
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0f, 145f),
                    new Vector2(420f, 260f),
                    new Vector2(0.5f, 0.5f));
                CanvasGroup group = root.gameObject.AddComponent<CanvasGroup>();
                Text fallback = StretchText(
                    root,
                    "FallbackText",
                    new Vector2(10f, 10f),
                    new Vector2(-10f, -90f),
                    48,
                    TextAnchor.MiddleCenter,
                    Color.white);
                Text offset = FixedText(
                    root,
                    "OffsetText",
                    new Vector2(360f, 42f),
                    new Vector2(0f, -70f),
                    new Vector2(0.5f, 0.5f),
                    27,
                    TextAnchor.MiddleCenter,
                    Color.white);
                Text direction = FixedText(
                    root,
                    "DirectionText",
                    new Vector2(360f, 36f),
                    new Vector2(0f, -112f),
                    new Vector2(0.5f, 0.5f),
                    22,
                    TextAnchor.MiddleCenter,
                    Color.white);
                feedback = root.gameObject.AddComponent<JudgementFeedbackView>();
                feedback.canvasGroup = group;
                feedback.fallbackText = fallback;
                feedback.offsetText = offset;
                feedback.directionText = direction;
            }

            RectTransform rect = feedback.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 250f);
            rect.sizeDelta = new Vector2(420f, 300f);
            rect.localScale = Vector3.one * 0.62f;
            RectTransform backdrop = FixedPanel(
                rect,
                "FeedbackBackdrop",
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(410f, 330f),
                new Vector2(0.5f, 0.5f));
            Image backdropImage = AddBackground(
                backdrop,
                new Color(0.008f, 0.012f, 0.016f, 0.64f));
            backdropImage.raycastTarget = false;
            backdrop.SetAsFirstSibling();
            feedback.visualLibrary =
                AssetDatabase.LoadAssetAtPath<JudgementVisualLibrary>(
                    JudgementLibraryPath);
            feedback.HideImmediate();
            return feedback;
        }

        private static void EnsureOneEventSystem(Scene scene)
        {
            EventSystem[] systems = UnityEngine.Object.FindObjectsByType<
                EventSystem>(
                FindObjectsInactive.Include);
            EventSystem keeper = null;
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i].gameObject.scene != scene)
                {
                    continue;
                }

                if (keeper == null)
                {
                    keeper = systems[i];
                    keeper.gameObject.SetActive(true);
                }
                else
                {
                    systems[i].gameObject.SetActive(false);
                }
            }

            if (keeper == null)
            {
                GameObject eventObject = new GameObject(
                    "EventSystem",
                    typeof(EventSystem),
                    typeof(StandaloneInputModule));
                SceneManager.MoveGameObjectToScene(eventObject, scene);
            }
        }

        private static void ValidateScene(
            Scene scene,
            Stage01SalmonTimingScaffold timing,
            ProceduralSalmonView salmon,
            KnifeVisualController knife,
            Stage01NotePatternDefinition pattern,
            Stage01NotePatternProvider patternProvider,
            Stage01ActiveNoteTracker noteTracker,
            SliceCuePresenter cuePresenter,
            BossDemoPresenter bossPresenter,
            HudObjects hud)
        {
            Require(File.Exists(BackupScenePath), "Stage backup is missing.");
            Require(salmon != null, "Procedural salmon is missing.");
            Require(knife != null, "Procedural player knife is missing.");
            Require(hud.hud != null, "Stage HUD is missing.");
            Require(
                hud.hud.fishTypeText != null,
                "Stage fish type label is missing.");
            Require(hud.feedback != null, "Judgement feedback is missing.");
            Require(hud.rhythmLane != null, "Upcoming rhythm lane is missing.");
            Require(
                hud.rhythmLaneBackground != null &&
                hud.hitCursor != null &&
                hud.restPrompt != null,
                "Rhythm lane visibility references are incomplete.");
            Require(
                hud.upcomingNoteDots != null &&
                hud.upcomingNoteDots.Length == 4,
                "Four upcoming note dots are required.");
            Require(pattern != null, "Stage 01 note pattern is missing.");
            Require(
                AssetDatabase.GetAssetPath(pattern) == PatternAssetPath,
                "Stage 01 note pattern asset path changed.");
            Require(patternProvider != null, "Pattern provider is missing.");
            Require(noteTracker != null, "Active note tracker is missing.");
            Require(
                timing.notePatternProvider == patternProvider &&
                timing.activeNoteTracker == noteTracker,
                "Timing scaffold pattern references are incomplete.");
            Require(
                cuePresenter != null &&
                cuePresenter.sourceMode == SliceCueSourceMode.StagePattern,
                "Slice cue must use the Stage 01 pattern.");
            Require(
                cuePresenter.upcomingGhostLines != null &&
                cuePresenter.upcomingGhostLines.Length == 3,
                "Three upcoming world guides are required.");
            Require(timing.audioClock != null, "AudioClock is missing.");
            Require(timing.audioSource != null, "AudioSource is missing.");
            Require(timing.audioSource.clip != null, "Stage music clip is missing.");
            Require(
                AssetDatabase.GetAssetPath(timing.audioSource.clip) == MusicPath,
                "Stage music reference changed unexpectedly.");
            Require(Mathf.Approximately(timing.bpm, 88f), "BPM changed.");
            Require(
                Math.Abs(timing.firstDownbeatSec - 0.683d) < 0.000001d,
                "firstDownbeatSec changed.");
            Require(
                Math.Abs(timing.gameplayStartSec - 11.592d) < 0.000001d,
                "gameplayStartSec changed.");
            Require(
                Math.Abs(timing.gameplayEndSec - 120.683d) < 0.000001d,
                "gameplayEndSec changed.");
            ValidatePattern(timing, pattern, patternProvider);
            Require(
                bossPresenter != null &&
                bossPresenter.DemoNoteTimes.Count == 4,
                "Boss demo must use the first four pattern notes.");

            int listeners = CountActiveInScene<AudioListener>(scene);
            int eventSystems = CountActiveInScene<EventSystem>(scene);
            Require(listeners == 1, "Stage must have exactly one AudioListener.");
            Require(eventSystems == 1, "Stage must have exactly one EventSystem.");

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform[] transforms = root.GetComponentsInChildren<Transform>(
                    true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    Require(
                        GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                            transforms[i].gameObject) == 0,
                        "Stage scene contains a missing MonoBehaviour script.");
                }
            }
        }

        private static void ValidatePattern(
            Stage01SalmonTimingScaffold timing,
            Stage01NotePatternDefinition pattern,
            Stage01NotePatternProvider patternProvider)
        {
            Require(pattern.manualBarCount == 16,
                "Stage 01 manual pattern must contain 16 bars.");
            Require(pattern.repeatFromBar == 9,
                "Stage 01 repeat must begin at pattern bar 9.");
            Require(pattern.subdivisionsPerBeat == 2,
                "Stage 01 pattern must use eighth-note steps.");
            Require(pattern.notes != null && pattern.notes.Count > 0,
                "Stage 01 pattern has no notes.");

            bool hasRestBar = false;
            bool hasEighthDouble = false;
            int previousBar = -1;
            int previousStep = -2;
            for (int bar = 1; bar <= pattern.manualBarCount; bar++)
            {
                int notesInBar = 0;
                for (int i = 0; i < pattern.notes.Count; i++)
                {
                    Stage01PatternNote note = pattern.notes[i];
                    if (note == null || note.barIndex != bar)
                    {
                        continue;
                    }

                    notesInBar++;
                    if (previousBar == bar &&
                        note.eighthStepInBar == previousStep + 1)
                    {
                        hasEighthDouble = true;
                    }

                    previousBar = bar;
                    previousStep = note.eighthStepInBar;
                }

                if (notesInBar > 0 && notesInBar < 4)
                {
                    hasRestBar = true;
                }
            }

            Require(hasRestBar, "Stage 01 pattern needs intentional rests.");
            Require(hasEighthDouble,
                "Stage 01 pattern needs an eighth-note double.");
            double manualDuration =
                pattern.manualBarCount * timing.BarLengthSeconds;
            Require(manualDuration >= 40d && manualDuration <= 60d,
                "Manual Stage 01 pattern must cover 40-60 seconds.");

            patternProvider.Initialize(timing);
            IReadOnlyList<Stage01RuntimeNote> runtimeNotes =
                patternProvider.RuntimeNotes;
            Require(patternProvider.IsInitialized,
                "Stage 01 runtime pattern did not initialize.");
            Require(runtimeNotes.Count > pattern.notes.Count,
                "Stage 01 repeat pattern was not generated.");
            Require(runtimeNotes[0].songTimeSeconds >= timing.gameplayStartSec,
                "First playable note begins before gameplay.");
            Require(
                runtimeNotes[runtimeNotes.Count - 1].songTimeSeconds >=
                timing.gameplayEndSec - timing.BarLengthSeconds,
                "Runtime pattern does not cover the gameplay ending.");
            for (int i = 1; i < 4; i++)
            {
                Require(
                    Math.Abs(
                        runtimeNotes[i].songTimeSeconds -
                        runtimeNotes[i - 1].songTimeSeconds -
                        timing.BeatLengthSeconds) < 0.000001d,
                    "Tutorial opening must begin with quarter notes.");
            }
        }

        private static int CountActiveInScene<T>(Scene scene)
            where T : Behaviour
        {
            T[] components = UnityEngine.Object.FindObjectsByType<T>(
                FindObjectsInactive.Include);
            int count = 0;
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i].gameObject.scene == scene &&
                    components[i].isActiveAndEnabled)
                {
                    count++;
                }
            }

            return count;
        }

        private static Material Material(
            string name,
            Color color,
            float smoothness,
            float metallic = 0f)
        {
            string path = MaterialRoot + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            SetMaterialColor(material, color);
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", smoothness);
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", metallic);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        private static Mesh SaveMesh(string fileName, Mesh generated)
        {
            string path = MeshRoot + "/" + fileName;
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                generated.name = Path.GetFileNameWithoutExtension(fileName);
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }

            EditorUtility.CopySerialized(generated, existing);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static Mesh CreateRoundedBoardMesh(
            float width,
            float depth,
            float radius,
            float thickness)
        {
            var points = new List<Vector2>();
            const int segments = 5;
            Vector2[] centers =
            {
                new Vector2(width * 0.5f - radius, depth * 0.5f - radius),
                new Vector2(-width * 0.5f + radius, depth * 0.5f - radius),
                new Vector2(-width * 0.5f + radius, -depth * 0.5f + radius),
                new Vector2(width * 0.5f - radius, -depth * 0.5f + radius)
            };
            float[] starts = { 0f, 90f, 180f, 270f };
            for (int corner = 0; corner < centers.Length; corner++)
            {
                for (int i = 0; i <= segments; i++)
                {
                    float radians = (starts[corner] +
                        i * 90f / segments) * Mathf.Deg2Rad;
                    points.Add(centers[corner] + new Vector2(
                        Mathf.Cos(radians),
                        Mathf.Sin(radians)) * radius);
                }
            }

            return CreateExtrudedPolygon(points.ToArray(), thickness);
        }

        private static Mesh CreateExtrudedPolygon(
            Vector2[] polygon,
            float thickness)
        {
            int count = polygon.Length;
            var vertices = new Vector3[count * 2];
            float half = thickness * 0.5f;
            for (int i = 0; i < count; i++)
            {
                vertices[i] = new Vector3(polygon[i].x, half, polygon[i].y);
                vertices[i + count] = new Vector3(
                    polygon[i].x,
                    -half,
                    polygon[i].y);
            }

            var triangles = new List<int>();
            for (int i = 1; i < count - 1; i++)
            {
                triangles.Add(0);
                triangles.Add(i + 1);
                triangles.Add(i);
                triangles.Add(count);
                triangles.Add(count + i);
                triangles.Add(count + i + 1);
            }

            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                triangles.Add(i);
                triangles.Add(next);
                triangles.Add(i + count);
                triangles.Add(next);
                triangles.Add(next + count);
                triangles.Add(i + count);
            }

            var mesh = new Mesh
            {
                vertices = vertices,
                triangles = triangles.ToArray()
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static GameObject Primitive(
            string name,
            Transform parent,
            PrimitiveType type,
            Vector3 localPosition,
            Vector3 localScale,
            Vector3 localEuler,
            Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            DisableCollider(go);
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return go;
        }

        private static GameObject MeshObject(
            string name,
            Transform parent,
            Mesh mesh,
            Vector3 localPosition,
            Vector3 localScale,
            Vector3 localEuler,
            Material material)
        {
            GameObject go = Child(parent, name);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        private static GameObject Child(Transform parent, string name)
        {
            var child = new GameObject(name);
            if (parent != null)
            {
                child.transform.SetParent(parent, false);
            }

            return child;
        }

        private static RectTransform Panel(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Color color)
        {
            RectTransform rect = Rect(parent, name);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            AddBackground(rect, color);
            return rect;
        }

        private static RectTransform FixedPanel(
            Transform parent,
            string name,
            Vector2 anchor,
            Vector2 anchoredPosition,
            Vector2 size,
            Vector2 pivot)
        {
            RectTransform rect = Rect(parent, name);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return rect;
        }

        private static RectTransform Rect(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static Image AddBackground(RectTransform rect, Color color)
        {
            Image image = rect.GetComponent<Image>();
            if (image == null)
            {
                image = rect.gameObject.AddComponent<Image>();
            }

            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text StretchText(
            Transform parent,
            string name,
            Vector2 offsetMin,
            Vector2 offsetMax,
            int fontSize,
            TextAnchor alignment,
            Color color)
        {
            RectTransform rect = Rect(parent, name);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return ConfigureText(rect, fontSize, alignment, color);
        }

        private static Text FixedText(
            Transform parent,
            string name,
            Vector2 size,
            Vector2 anchoredPosition,
            Vector2 anchor,
            int fontSize,
            TextAnchor alignment,
            Color color)
        {
            RectTransform rect = FixedPanel(
                parent,
                name,
                anchor,
                anchoredPosition,
                size,
                anchor);
            return ConfigureText(rect, fontSize, alignment, color);
        }

        private static Text ConfigureText(
            RectTransform rect,
            int fontSize,
            TextAnchor alignment,
            Color color)
        {
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            Shadow shadow = rect.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.75f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            return text;
        }

        private static Image Bar(
            Transform parent,
            string name,
            Vector2 anchoredPosition,
            Vector2 size,
            Color fillColor,
            Vector2? anchorOverride = null)
        {
            Vector2 anchor = anchorOverride ?? new Vector2(0f, 1f);
            RectTransform background = FixedPanel(
                parent,
                name + "_Background",
                anchor,
                anchoredPosition,
                size,
                anchor);
            Image backgroundImage = AddBackground(
                background,
                new Color(1f, 1f, 1f, 0.14f));
            Sprite barSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(
                "UI/Skin/UISprite.psd");
            backgroundImage.sprite = barSprite;
            backgroundImage.type = Image.Type.Sliced;
            RectTransform fillRect = Panel(
                background,
                name + "_Fill",
                Vector2.zero,
                Vector2.one,
                fillColor);
            Image fill = fillRect.GetComponent<Image>();
            fill.sprite = barSprite;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 0f;
            return fill;
        }

        private static void DestroyNamedSceneObjects(
            Scene scene,
            params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                GameObject target = FindNamed(scene, names[i]);
                if (target != null)
                {
                    UnityEngine.Object.DestroyImmediate(target);
                }
            }
        }

        private static void DisableLegacyWorldLabels(Scene scene)
        {
            string[] names =
            {
                "Label_Stage01",
                "Label_Demo",
                "Label_Gameplay",
                "Boss_Demo_Placeholder",
                "Kevin_Input_Placeholder",
                "Stage01Salmon_DebugCanvas"
            };
            for (int i = 0; i < names.Length; i++)
            {
                GameObject target = FindNamed(scene, names[i]);
                if (target != null)
                {
                    target.SetActive(false);
                }
            }
        }

        private static GameObject FindNamed(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform[] transforms = root.GetComponentsInChildren<Transform>(
                    true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    if (transforms[i].name == name)
                    {
                        return transforms[i].gameObject;
                    }
                }
            }

            return null;
        }

        private static T FindInScene<T>(Scene scene)
            where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static void DisableCollider(GameObject target)
        {
            Collider collider = target.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/_SashimiBoy/Scenes/Backups");
            EnsureFolder("Assets/_SashimiBoy/Data/Generated");
            EnsureFolder(PrefabRoot);
            EnsureFolder(MaterialRoot);
            EnsureFolder(MeshRoot);
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
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

        private sealed class StageMaterials
        {
            public Material background;
            public Material counter;
            public Material boardBorder;
            public Material board;
            public Material boardGrain;
            public Material salmonBody;
            public Material salmonBack;
            public Material salmonBelly;
            public Material salmonStripe;
            public Material salmonFin;
            public Material eyeWhite;
            public Material eyeDark;
            public Material gill;
            public Material cut;
            public Material blade;
            public Material bladeEdge;
            public Material handle;
            public Material highlight;
            public Material cue;
            public Material silhouette;
            public Material plate;
        }

        private sealed class MeshAssets
        {
            public Mesh tail;
            public Mesh fin;
            public Mesh knifeBlade;
            public Mesh roundedBoard;
        }

        private sealed class CueObjects
        {
            public GameObject root;
            public Renderer line;
            public Transform left;
            public Transform right;
            public Transform forecastLeft;
            public Transform forecastRight;
            public readonly List<Renderer> renderers = new List<Renderer>();
            public readonly List<Transform> ghostLines =
                new List<Transform>();
            public readonly List<Renderer> ghostRenderers =
                new List<Renderer>();
        }

        private sealed class BossObjects
        {
            public GameObject silhouetteRoot;
            public KnifeVisualController knife;
        }

        private sealed class HudObjects
        {
            public Stage01SalmonHUD hud;
            public Text spacePrompt;
            public Text nowPrompt;
            public Text restPrompt;
            public Text missingClip;
            public RectTransform rhythmLane;
            public Image rhythmLaneBackground;
            public Image hitCursor;
            public Image[] upcomingNoteDots;
            public JudgementFeedbackView feedback;
        }
    }
}
#endif
