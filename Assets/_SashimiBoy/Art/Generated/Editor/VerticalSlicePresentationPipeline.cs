#if UNITY_EDITOR
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

namespace SashimiBoy.EditorTools
{
    public static class VerticalSlicePresentationPipeline
    {
        private const string ScenesRoot = "Assets/_SashimiBoy/Scenes";
        private const string GeneratedRoot = "Assets/_SashimiBoy/Art/Generated";
        private const string MaterialRoot =
            GeneratedRoot + "/Materials/VerticalSlice";
        private const string CommonPrefabRoot =
            GeneratedRoot + "/Prefabs/UI/Common";
        private const string DataRoot = GeneratedRoot + "/Data";
        private const string ThemePath =
            DataRoot + "/PrototypeUITheme.asset";
        private const string PanelPrefabPath =
            CommonPrefabRoot + "/PF_UI_PrototypePanel.prefab";
        private const string ButtonPrefabPath =
            CommonPrefabRoot + "/PF_UI_PrototypeButton.prefab";

        private const string StreetScenePath = ScenesRoot + "/Street.unity";
        private const string FishShopScenePath =
            ScenesRoot + "/FishShopDialogue.unity";
        private const string EquipmentShopScenePath =
            ScenesRoot + "/EquipmentShop.unity";
        private const string ClubScenePath = ScenesRoot + "/Club.unity";

        private static readonly Vector3[] AudiencePositions =
        {
            new Vector3(-2.9f, 0f, -2.3f),
            new Vector3(-1.8f, 0f, -2.65f),
            new Vector3(-0.55f, 0f, -2.35f),
            new Vector3(0.7f, 0f, -2.7f),
            new Vector3(1.95f, 0f, -2.3f),
            new Vector3(3.0f, 0f, -2.65f),
            new Vector3(-2.45f, 0f, -1.05f),
            new Vector3(-1.2f, 0f, -1.35f),
            new Vector3(0.05f, 0f, -0.95f),
            new Vector3(1.35f, 0f, -1.3f),
            new Vector3(2.55f, 0f, -0.9f),
            new Vector3(-1.75f, 0f, 0.05f),
            new Vector3(-0.25f, 0f, 0.15f),
            new Vector3(1.65f, 0f, 0.02f),
        };

        [MenuItem("Sashimi Boy/Art/Build Vertical Slice Presentation")]
        public static void BuildVerticalSlicePresentation()
        {
            BuildAll(true, true);
        }

        public static void BuildVerticalSlicePresentationBatch()
        {
            BuildAll(true, false);
        }

        public static void RebuildForPrototypeGenerator()
        {
            BuildAll(false, false);
        }

        public static void ApplyFishShopSignFacingToStreetBatch()
        {
            Scene scene = OpenRequiredScene(StreetScenePath);
            GameObject board = FindNamed(scene, "FishShop_Sign_Board");
            GameObject text = FindNamed(scene, "FishShop_Sign_Text");
            if (board == null || text == null ||
                text.GetComponent<TextMesh>() == null)
            {
                throw new InvalidOperationException(
                    "Street FishShop sign hierarchy is incomplete.");
            }

            ConfigureWorldSignTextFacing(
                text.transform,
                board.transform,
                WorldSignFace.PositiveZ);
            EditorSceneManager.MarkSceneDirty(scene);
            SaveScene(scene, StreetScenePath);
        }

        private static void BuildAll(bool rebuildStage, bool showDialog)
        {
            EnsureFolder(MaterialRoot);
            EnsureFolder(CommonPrefabRoot);
            EnsureFolder(DataRoot);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            PrototypeUITheme theme = BuildTheme();
            PresentationMaterials materials = BuildMaterials();
            BuildCommonUiPrefabs(theme);

            if (rebuildStage)
            {
                Stage01SalmonPresentationPipeline.
                    BuildStage01VisualPrototypeBatch();
            }

            ApplyStreet(theme, materials);
            ApplyFishShop(theme, materials);
            ApplyEquipmentShop(theme, materials);
            ClubArtPassPipeline.ApplyClubArtToMainSceneBatch();
            ApplyClub(theme, materials);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorSceneManager.OpenScene(StreetScenePath, OpenSceneMode.Single);
            Debug.Log(
                "[Sashimi Boy] Vertical-slice presentation build complete.");

            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "Sashimi Boy",
                    "Street, FishShop, EquipmentShop, Club, and Stage 01 " +
                    "presentation were generated and validated.",
                    "OK");
            }
        }

        private static PrototypeUITheme BuildTheme()
        {
            PrototypeUITheme theme =
                AssetDatabase.LoadAssetAtPath<PrototypeUITheme>(ThemePath);
            if (theme == null)
            {
                theme = ScriptableObject.CreateInstance<PrototypeUITheme>();
                AssetDatabase.CreateAsset(theme, ThemePath);
            }

            theme.panel = new Color(0.025f, 0.032f, 0.038f, 0.94f);
            theme.panelSoft = new Color(0.035f, 0.045f, 0.052f, 0.88f);
            theme.panelBorder = new Color(0.2f, 0.26f, 0.29f, 1f);
            theme.primaryText = new Color(0.96f, 0.97f, 0.98f, 1f);
            theme.secondaryText = new Color(0.67f, 0.72f, 0.75f, 1f);
            theme.cyanAccent = new Color(0.2f, 0.82f, 0.88f, 1f);
            theme.goldAccent = new Color(1f, 0.68f, 0.2f, 1f);
            theme.dangerAccent = new Color(0.95f, 0.18f, 0.16f, 1f);
            EditorUtility.SetDirty(theme);
            return theme;
        }

        private static PresentationMaterials BuildMaterials()
        {
            return new PresentationMaterials
            {
                road = MaterialAsset("M_VS_Road", new Color(
                    0.075f, 0.085f, 0.09f, 1f), 0.08f),
                sidewalk = MaterialAsset("M_VS_Sidewalk", new Color(
                    0.31f, 0.34f, 0.34f, 1f), 0.16f),
                tile = MaterialAsset("M_VS_Tile", new Color(
                    0.48f, 0.51f, 0.49f, 1f), 0.22f),
                tileJoint = MaterialAsset("M_VS_TileJoint", new Color(
                    0.13f, 0.15f, 0.15f, 1f), 0.1f),
                neutralWall = MaterialAsset("M_VS_NeutralWall", new Color(
                    0.19f, 0.21f, 0.21f, 1f), 0.2f),
                darkWall = MaterialAsset("M_VS_DarkWall", new Color(
                    0.035f, 0.042f, 0.048f, 1f), 0.18f),
                tealWall = MaterialAsset("M_VS_TealWall", new Color(
                    0.055f, 0.31f, 0.31f, 1f), 0.24f),
                cyan = MaterialAsset("M_VS_CyanAccent", new Color(
                    0.12f, 0.78f, 0.82f, 1f), 0.42f,
                    new Color(0.04f, 0.42f, 0.5f, 1f)),
                wood = MaterialAsset("M_VS_Wood", new Color(
                    0.34f, 0.19f, 0.085f, 1f), 0.22f),
                woodLight = MaterialAsset("M_VS_WoodLight", new Color(
                    0.68f, 0.43f, 0.2f, 1f), 0.26f),
                warmWall = MaterialAsset("M_VS_WarmWall", new Color(
                    0.24f, 0.19f, 0.13f, 1f), 0.24f),
                amber = MaterialAsset("M_VS_AmberAccent", new Color(
                    1f, 0.55f, 0.12f, 1f), 0.42f,
                    new Color(0.55f, 0.2f, 0.025f, 1f)),
                gold = MaterialAsset("M_VS_Gold", new Color(
                    0.78f, 0.55f, 0.16f, 1f), 0.55f),
                clubWall = MaterialAsset("M_VS_ClubWall", new Color(
                    0.075f, 0.045f, 0.09f, 1f), 0.2f),
                magenta = MaterialAsset("M_VS_MagentaAccent", new Color(
                    0.82f, 0.05f, 0.43f, 1f), 0.38f,
                    new Color(0.48f, 0.01f, 0.18f, 1f)),
                red = MaterialAsset("M_VS_RedAccent", new Color(
                    0.95f, 0.08f, 0.07f, 1f), 0.34f,
                    new Color(0.5f, 0.01f, 0.005f, 1f)),
                steel = MaterialAsset("M_VS_Steel", new Color(
                    0.48f, 0.55f, 0.57f, 1f), 0.65f),
                salmon = MaterialAsset("M_VS_Salmon", new Color(
                    0.94f, 0.35f, 0.24f, 1f), 0.34f),
                white = MaterialAsset("M_VS_SoftWhite", new Color(
                    0.83f, 0.85f, 0.82f, 1f), 0.3f),
                audienceCyan = MaterialAsset("M_VS_AudienceCyan", new Color(
                    0.1f, 0.42f, 0.46f, 1f), 0.22f),
                audienceRed = MaterialAsset("M_VS_AudienceRed", new Color(
                    0.52f, 0.08f, 0.13f, 1f), 0.22f),
                audienceGold = MaterialAsset("M_VS_AudienceGold", new Color(
                    0.56f, 0.36f, 0.1f, 1f), 0.22f),
            };
        }

        private static void BuildCommonUiPrefabs(PrototypeUITheme theme)
        {
            GameObject panel = new GameObject(
                "PF_UI_PrototypePanel",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Outline));
            try
            {
                RectTransform rect = panel.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(720f, 260f);
                panel.GetComponent<Image>().color = theme.panel;
                Outline outline = panel.GetComponent<Outline>();
                outline.effectColor = theme.panelBorder;
                outline.effectDistance = new Vector2(1f, -1f);
                PrefabUtility.SaveAsPrefabAsset(panel, PanelPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(panel);
            }

            GameObject buttonObject = new GameObject(
                "PF_UI_PrototypeButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            try
            {
                RectTransform rect = buttonObject.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(220f, 64f);
                Image image = buttonObject.GetComponent<Image>();
                image.color = theme.buttonNormal;
                Button button = buttonObject.GetComponent<Button>();
                button.targetGraphic = image;
                button.transition = Selectable.Transition.ColorTint;
                button.colors = theme.CreateButtonColors();
                Text label = CreateText(
                    buttonObject.transform,
                    "Label",
                    "BUTTON",
                    20,
                    TextAnchor.MiddleCenter,
                    theme.primaryText);
                Stretch(label.rectTransform, new Vector2(16f, 8f),
                    new Vector2(-16f, -8f));
                PrefabUtility.SaveAsPrefabAsset(buttonObject, ButtonPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(buttonObject);
            }
        }

        private static void ApplyStreet(
            PrototypeUITheme theme,
            PresentationMaterials materials)
        {
            Scene scene = OpenRequiredScene(StreetScenePath);
            DestroyRoot(scene, "Street_PresentationRoot");
            DestroyRoot(scene, "Street_PresentationUI");
            GameObject root = CreateSceneRoot(scene, "Street_PresentationRoot");

            HideRenderers(
                scene,
                "Street_Ground",
                "Fish_Market_Left",
                "Equipment_Shop_Back",
                "Club_Right",
                "Sashimi_Shop_Front",
                "Label_FishShop",
                "Label_EquipmentShop",
                "Label_Club");
            HideDoorVisual(scene, "Door_To_FishShopDialogue");
            HideDoorVisual(scene, "Door_To_EquipmentShop");
            HideDoorVisual(scene, "Door_To_Club");

            SceneDoor fishDoor = RequireComponentOnNamed<SceneDoor>(
                scene, "Door_To_FishShopDialogue");
            SceneDoor equipmentDoor = RequireComponentOnNamed<SceneDoor>(
                scene, "Door_To_EquipmentShop");
            SceneDoor clubDoor = RequireComponentOnNamed<SceneDoor>(
                scene, "Door_To_Club");
            fishDoor.prompt = "횟집 들어가기";
            equipmentDoor.prompt = "장비가게 들어가기";
            clubDoor.prompt = "클럽 들어가기";

            BuildStreetGround(root.transform, materials);
            BuildFishFacade(root.transform, materials);
            BuildEquipmentFacade(root.transform, materials);
            BuildClubFacade(root.transform, materials);
            CreateLocationHeader(
                scene,
                "Street_PresentationUI",
                "거리 광장",
                "SASHIMI BOY DISTRICT",
                theme,
                theme.cyanAccent);

            ConfigureWorldUi(scene, theme, false);
            ConfigureCamera(
                scene,
                new Vector3(0f, 13f, -9f),
                Quaternion.Euler(54f, 0f, 0f),
                58f,
                new Color(0.055f, 0.075f, 0.08f, 1f));
            Camera streetCamera = GetSceneComponents<Camera>(scene)
                .First(item => item.CompareTag("MainCamera"));
            streetCamera.orthographic = true;
            streetCamera.orthographicSize = 7.1f;
            ConfigurePlayer(scene, materials.audienceCyan);
            EnsureSceneBasics(scene);
            ValidateStreet(scene);
            SaveScene(scene, StreetScenePath);
        }

        private static void BuildStreetGround(
            Transform root,
            PresentationMaterials materials)
        {
            Primitive("Road", root, PrimitiveType.Cube,
                new Vector3(0f, 0.02f, -0.8f),
                new Vector3(21f, 0.08f, 3.25f), materials.road);
            Primitive("SouthPlaza", root, PrimitiveType.Cube,
                new Vector3(0f, 0.045f, -3.35f),
                new Vector3(21f, 0.1f, 1.85f), materials.tile);
            Primitive("NorthSidewalk", root, PrimitiveType.Cube,
                new Vector3(0f, 0.055f, 2.15f),
                new Vector3(21f, 0.11f, 2.55f), materials.sidewalk);
            Primitive("NorthCurb", root, PrimitiveType.Cube,
                new Vector3(0f, 0.13f, 0.78f),
                new Vector3(21f, 0.2f, 0.16f), materials.white);
            Primitive("SouthCurb", root, PrimitiveType.Cube,
                new Vector3(0f, 0.13f, -2.45f),
                new Vector3(21f, 0.2f, 0.16f), materials.white);

            for (int i = 0; i < 12; i++)
            {
                float x = -9.6f + i * 1.75f;
                Primitive($"PlazaJoint_{i + 1:00}", root,
                    PrimitiveType.Cube,
                    new Vector3(x, 0.105f, -3.35f),
                    new Vector3(0.025f, 0.012f, 1.72f),
                    materials.tileJoint);
            }

            for (int i = 0; i < 6; i++)
            {
                Primitive($"Crosswalk_{i + 1:00}", root,
                    PrimitiveType.Cube,
                    new Vector3(-0.9f + i * 0.36f, 0.075f, -0.8f),
                    new Vector3(0.2f, 0.015f, 2.35f),
                    materials.white);
            }

            for (int i = 0; i < 10; i++)
            {
                Primitive($"RoadDash_{i + 1:00}", root,
                    PrimitiveType.Cube,
                    new Vector3(-9f + i * 2f, 0.07f, -0.8f),
                    new Vector3(0.85f, 0.014f, 0.07f),
                    materials.gold);
            }
        }

        private static void BuildFishFacade(
            Transform root,
            PresentationMaterials materials)
        {
            Transform facade = Child(root, "FishShop_Facade");
            Primitive("Building", facade, PrimitiveType.Cube,
                new Vector3(-3.5f, 0.9f, -3.55f),
                new Vector3(4.3f, 1.8f, 1.55f), materials.tealWall);
            Primitive("WoodBase", facade, PrimitiveType.Cube,
                new Vector3(-3.5f, 0.42f, -2.78f),
                new Vector3(4.25f, 0.68f, 0.16f), materials.wood);
            Primitive("Window", facade, PrimitiveType.Cube,
                new Vector3(-4.65f, 1.15f, -2.74f),
                new Vector3(1.15f, 0.8f, 0.12f), materials.darkWall);
            Primitive("DisplayCounter", facade, PrimitiveType.Cube,
                new Vector3(-4.65f, 0.8f, -2.59f),
                new Vector3(1.35f, 0.16f, 0.42f), materials.woodLight);
            CreateDoorFrame(facade, new Vector3(-3.5f, 0f, -2.72f),
                materials.woodLight, materials.cyan);
            for (int i = 0; i < 5; i++)
            {
                Material stripe = i % 2 == 0
                    ? materials.cyan
                    : materials.white;
                Primitive($"Awning_{i + 1:00}", facade,
                    PrimitiveType.Cube,
                    new Vector3(-5.23f + i * 0.3f, 1.72f, -2.54f),
                    new Vector3(0.29f, 0.11f, 0.72f), stripe,
                    new Vector3(12f, 0f, 0f));
            }

            CreateWorldSign(facade, "FishShop_Sign", "SASHIMI",
                new Vector3(-3.5f, 2.32f, -2.7f),
                new Vector3(2.75f, 0.52f, 0.14f),
                materials.wood, new Color(0.62f, 0.95f, 0.95f, 1f),
                WorldSignFace.PositiveZ);
            CreatePointLight(facade, "FishShop_EntryLight",
                new Vector3(-3.5f, 2.4f, -2.3f),
                new Color(0.2f, 0.85f, 0.88f), 1.1f, 3.4f);
        }

        private static void BuildEquipmentFacade(
            Transform root,
            PresentationMaterials materials)
        {
            Transform facade = Child(root, "EquipmentShop_Facade");
            Primitive("Building", facade, PrimitiveType.Cube,
                new Vector3(0f, 1.65f, 4.45f),
                new Vector3(5.3f, 3.3f, 2.55f), materials.warmWall);
            Primitive("ShopFront", facade, PrimitiveType.Cube,
                new Vector3(0f, 1.4f, 3.12f),
                new Vector3(5.15f, 2.5f, 0.14f), materials.darkWall);
            CreateDoorFrame(facade, new Vector3(0f, 0f, 3.02f),
                materials.gold, materials.amber);
            Primitive("DisplayWindow_Left", facade, PrimitiveType.Cube,
                new Vector3(-1.75f, 1.32f, 3.0f),
                new Vector3(1.65f, 1.55f, 0.11f), materials.neutralWall);
            Primitive("DisplayWindow_Right", facade, PrimitiveType.Cube,
                new Vector3(1.75f, 1.32f, 3.0f),
                new Vector3(1.65f, 1.55f, 0.11f), materials.neutralWall);
            for (int side = -1; side <= 1; side += 2)
            {
                Primitive(side < 0 ? "Speaker_Left" : "Speaker_Right",
                    facade, PrimitiveType.Cylinder,
                    new Vector3(side * 1.75f, 1.35f, 2.9f),
                    new Vector3(0.38f, 0.08f, 0.38f), materials.amber,
                    new Vector3(90f, 0f, 0f));
                Primitive(side < 0 ? "SpeakerBox_Left" : "SpeakerBox_Right",
                    facade, PrimitiveType.Cube,
                    new Vector3(side * 1.75f, 1.35f, 2.99f),
                    new Vector3(0.95f, 1.2f, 0.12f), materials.wood);
            }

            CreateWorldSign(facade, "EquipmentShop_Sign", "GEAR SHOP",
                new Vector3(0f, 2.95f, 2.98f),
                new Vector3(3.35f, 0.58f, 0.14f),
                materials.darkWall, new Color(1f, 0.72f, 0.25f, 1f),
                WorldSignFace.NegativeZ);
            CreatePointLight(facade, "EquipmentShop_EntryLight",
                new Vector3(0f, 2.45f, 2.65f),
                new Color(1f, 0.48f, 0.12f), 1.25f, 3.4f);
        }

        private static void BuildClubFacade(
            Transform root,
            PresentationMaterials materials)
        {
            Transform facade = Child(root, "Club_Facade");
            Primitive("Building", facade, PrimitiveType.Cube,
                new Vector3(7f, 1.75f, 1.55f),
                new Vector3(5f, 3.5f, 4.7f), materials.clubWall);
            Primitive("Recess", facade, PrimitiveType.Cube,
                new Vector3(7f, 1.05f, -0.84f),
                new Vector3(2.15f, 2.15f, 0.12f), materials.darkWall);
            CreateDoorFrame(facade, new Vector3(7f, 0f, -0.96f),
                materials.magenta, materials.red);
            Primitive("Neon_Left", facade, PrimitiveType.Cube,
                new Vector3(5.15f, 1.7f, -0.91f),
                new Vector3(0.09f, 2.65f, 0.09f), materials.magenta);
            Primitive("Neon_Right", facade, PrimitiveType.Cube,
                new Vector3(8.85f, 1.7f, -0.91f),
                new Vector3(0.09f, 2.65f, 0.09f), materials.red);
            Primitive("Neon_Canopy", facade, PrimitiveType.Cube,
                new Vector3(7f, 2.55f, -1.08f),
                new Vector3(3.55f, 0.1f, 0.48f), materials.magenta);
            CreateWorldSign(facade, "Club_Sign", "CLUB",
                new Vector3(7f, 3.0f, -0.88f),
                new Vector3(2.55f, 0.62f, 0.14f),
                materials.darkWall, new Color(1f, 0.2f, 0.34f, 1f),
                WorldSignFace.NegativeZ);
            CreatePointLight(facade, "Club_EntryLight",
                new Vector3(7f, 2.35f, -1.45f),
                new Color(0.95f, 0.04f, 0.28f), 1.45f, 3.8f);
        }

        private static void ApplyFishShop(
            PrototypeUITheme theme,
            PresentationMaterials materials)
        {
            Scene scene = OpenRequiredScene(FishShopScenePath);
            DestroyRoot(scene, "FishShop_PresentationRoot");
            DestroyRoot(scene, "FishShop_PresentationUI");
            GameObject root = CreateSceneRoot(
                scene, "FishShop_PresentationRoot");

            HideRenderers(
                scene,
                "Label_StagePlaceholder",
                "Label_ReturnDoor");
            HideDoorVisual(scene, "Door_To_Street");
            AssignMaterial(scene, "FishShop_Floor", materials.tile);
            AssignMaterial(scene, "Counter", materials.wood);
            AssignMaterial(scene, "Cutting_Board", materials.woodLight);
            AssignMaterial(scene, "Placeholder_Fish", materials.salmon);
            AssignMaterial(scene, "Kevin_Player", materials.audienceCyan);
            HideRenderers(scene, "Boss", "Kevin", "StartStage01_Placeholder");

            BuildFishShopEnvironment(root.transform, materials);
            CreateLocationHeader(
                scene,
                "FishShop_PresentationUI",
                "횟집 조리실",
                "PREP COUNTER",
                theme,
                theme.cyanAccent);
            StyleDialogue(scene, theme);
            ConfigureWorldUi(scene, theme, true);
            ConfigureCamera(
                scene,
                new Vector3(0f, 7.4f, -9.2f),
                Quaternion.Euler(43f, 0f, 0f),
                52f,
                new Color(0.06f, 0.075f, 0.075f, 1f));
            EnsureSceneBasics(scene);
            ValidateFishShop(scene);
            SaveScene(scene, FishShopScenePath);
        }

        private static void BuildFishShopEnvironment(
            Transform root,
            PresentationMaterials materials)
        {
            Primitive("BackWall", root, PrimitiveType.Cube,
                new Vector3(0f, 1.8f, 4f),
                new Vector3(12f, 3.6f, 0.22f), materials.neutralWall);
            Primitive("TileBand", root, PrimitiveType.Cube,
                new Vector3(0f, 1.15f, 3.86f),
                new Vector3(11.6f, 1.65f, 0.08f), materials.white);
            for (int i = 0; i < 8; i++)
            {
                Primitive($"TileJoint_{i + 1:00}", root,
                    PrimitiveType.Cube,
                    new Vector3(-5.3f + i * 1.5f, 1.15f, 3.8f),
                    new Vector3(0.025f, 1.55f, 0.04f),
                    materials.tileJoint);
            }

            Primitive("CounterFront", root, PrimitiveType.Cube,
                new Vector3(0f, 0.62f, 1.72f),
                new Vector3(7.35f, 1.2f, 0.18f), materials.wood);
            Primitive("CounterTrim", root, PrimitiveType.Cube,
                new Vector3(0f, 1.18f, 1.59f),
                new Vector3(7.55f, 0.12f, 0.45f), materials.steel);
            Primitive("PrepBench_Left", root, PrimitiveType.Cube,
                new Vector3(-4.65f, 0.72f, 2.65f),
                new Vector3(1.8f, 1.35f, 1.2f), materials.darkWall);
            Primitive("PrepBench_Right", root, PrimitiveType.Cube,
                new Vector3(4.65f, 0.72f, 2.65f),
                new Vector3(1.8f, 1.35f, 1.2f), materials.darkWall);
            Primitive("Shelf", root, PrimitiveType.Cube,
                new Vector3(2.8f, 2.25f, 3.62f),
                new Vector3(4.4f, 0.14f, 0.65f), materials.steel);
            for (int i = 0; i < 4; i++)
            {
                Primitive($"IngredientTray_{i + 1:00}", root,
                    PrimitiveType.Cube,
                    new Vector3(1.55f + i * 0.82f, 2.42f, 3.46f),
                    new Vector3(0.62f, 0.18f, 0.42f),
                    i % 2 == 0 ? materials.salmon : materials.white);
            }

            Primitive("KnifeRail", root, PrimitiveType.Cube,
                new Vector3(-3.25f, 2.2f, 3.68f),
                new Vector3(3.1f, 0.16f, 0.22f), materials.wood);
            for (int i = 0; i < 4; i++)
            {
                Primitive($"WallKnife_{i + 1:00}", root,
                    PrimitiveType.Cube,
                    new Vector3(-4.25f + i * 0.72f, 1.8f, 3.61f),
                    new Vector3(0.09f, 0.72f, 0.08f), materials.steel,
                    new Vector3(0f, 0f, -7f + i * 4f));
            }

            CreateCharacter(root, "Boss_Visual",
                new Vector3(-2.3f, 0f, 2.72f),
                materials.woodLight, materials.white, true);
            CreateCharacter(root, "Kevin_Visual",
                new Vector3(2.1f, 0f, -0.6f),
                materials.audienceCyan, materials.white, false);
            CreateDoorFrame(root, new Vector3(4.8f, 0f, -2.86f),
                materials.wood, materials.cyan);
            Primitive("StageStart_Mat", root, PrimitiveType.Cube,
                new Vector3(-2.1f, 0.1f, -1.8f),
                new Vector3(1.7f, 0.12f, 1.7f), materials.gold);
            Primitive("StageStart_Inset", root, PrimitiveType.Cube,
                new Vector3(-2.1f, 0.17f, -1.8f),
                new Vector3(1.35f, 0.04f, 1.35f), materials.darkWall);
            CreatePointLight(root, "PrepCounter_KeyLight",
                new Vector3(0f, 3.2f, 0.8f),
                new Color(0.65f, 0.9f, 0.88f), 0.8f, 6f);
        }

        private static void ApplyEquipmentShop(
            PrototypeUITheme theme,
            PresentationMaterials materials)
        {
            Scene scene = OpenRequiredScene(EquipmentShopScenePath);
            DestroyRoot(scene, "EquipmentShop_PresentationRoot");
            DestroyRoot(scene, "EquipmentShop_PresentationUI");
            GameObject root = CreateSceneRoot(
                scene, "EquipmentShop_PresentationRoot");

            HideRenderers(scene, "Label_ReturnDoor", "Shopkeeper");
            HideDoorVisual(scene, "Door_To_Street");
            for (int i = 0; i < 6; i++)
            {
                HideRenderers(scene, "WallGear_" + i);
            }

            AssignMaterial(scene, "Shop_Floor", materials.road);
            AssignMaterial(scene, "Counter", materials.wood);
            AssignMaterial(scene, "Kevin_Player", materials.audienceCyan);
            BuildEquipmentShopEnvironment(root.transform, materials);
            CreateLocationHeader(
                scene,
                "EquipmentShop_PresentationUI",
                "장비가게",
                "GEAR EXCHANGE",
                theme,
                theme.goldAccent);
            StyleEquipmentShopUi(scene, theme);
            ConfigureWorldUi(scene, theme, true);
            ConfigureCamera(
                scene,
                new Vector3(0f, 7.6f, -9.5f),
                Quaternion.Euler(43f, 0f, 0f),
                52f,
                new Color(0.055f, 0.047f, 0.04f, 1f));
            EnsureSceneBasics(scene);
            ValidateEquipmentShop(scene);
            SaveScene(scene, EquipmentShopScenePath);
        }

        private static void BuildEquipmentShopEnvironment(
            Transform root,
            PresentationMaterials materials)
        {
            Primitive("BackWall", root, PrimitiveType.Cube,
                new Vector3(0f, 1.85f, 4.02f),
                new Vector3(13f, 3.7f, 0.22f), materials.warmWall);
            Primitive("CounterFront", root, PrimitiveType.Cube,
                new Vector3(0f, 0.62f, 2.16f),
                new Vector3(5.4f, 1.18f, 0.22f), materials.wood);
            Primitive("CounterTop", root, PrimitiveType.Cube,
                new Vector3(0f, 1.2f, 2.08f),
                new Vector3(5.7f, 0.13f, 0.72f), materials.gold);
            CreateCharacter(root, "Shopkeeper_Visual",
                new Vector3(0f, 0f, 3.15f),
                materials.woodLight, materials.white, true);

            for (int i = 0; i < 6; i++)
            {
                float x = -5.25f + i * 2.1f;
                Transform bay = Child(root, $"DisplayBay_{i + 1:00}");
                Primitive("Back", bay, PrimitiveType.Cube,
                    new Vector3(x, 1.72f, 3.74f),
                    new Vector3(1.72f, 2.25f, 0.12f), materials.darkWall);
                Primitive("Shelf", bay, PrimitiveType.Cube,
                    new Vector3(x, 0.72f, 3.55f),
                    new Vector3(1.8f, 0.12f, 0.58f), materials.woodLight);
                if (i % 3 == 0)
                {
                    Primitive("SpeakerBox", bay, PrimitiveType.Cube,
                        new Vector3(x, 1.38f, 3.5f),
                        new Vector3(0.85f, 1.05f, 0.42f), materials.neutralWall);
                    Primitive("SpeakerCone", bay, PrimitiveType.Cylinder,
                        new Vector3(x, 1.4f, 3.25f),
                        new Vector3(0.29f, 0.08f, 0.29f), materials.amber,
                        new Vector3(90f, 0f, 0f));
                }
                else if (i % 3 == 1)
                {
                    Primitive("Mixer", bay, PrimitiveType.Cube,
                        new Vector3(x, 1.05f, 3.38f),
                        new Vector3(1.18f, 0.18f, 0.7f), materials.steel,
                        new Vector3(-12f, 0f, 0f));
                    for (int knob = 0; knob < 3; knob++)
                    {
                        Primitive($"Knob_{knob + 1}", bay,
                            PrimitiveType.Cylinder,
                            new Vector3(x - 0.32f + knob * 0.32f,
                                1.22f, 3.26f),
                            new Vector3(0.08f, 0.05f, 0.08f),
                            materials.cyan);
                    }
                }
                else
                {
                    Primitive("Stand", bay, PrimitiveType.Cylinder,
                        new Vector3(x, 1.28f, 3.45f),
                        new Vector3(0.07f, 0.75f, 0.07f), materials.steel);
                    Primitive("Mic", bay, PrimitiveType.Sphere,
                        new Vector3(x, 2.05f, 3.45f),
                        new Vector3(0.22f, 0.3f, 0.22f), materials.cyan);
                }
            }

            Primitive("CaseStack_Left", root, PrimitiveType.Cube,
                new Vector3(-4.75f, 0.4f, 1.6f),
                new Vector3(1.45f, 0.75f, 1.0f), materials.neutralWall);
            Primitive("CaseStack_Right", root, PrimitiveType.Cube,
                new Vector3(4.75f, 0.4f, 1.6f),
                new Vector3(1.45f, 0.75f, 1.0f), materials.neutralWall);
            CreateDoorFrame(root, new Vector3(5.2f, 0f, -2.65f),
                materials.gold, materials.amber);
            CreatePointLight(root, "CounterWarmLight",
                new Vector3(0f, 3.3f, 1.7f),
                new Color(1f, 0.56f, 0.22f), 1f, 5.5f);
        }

        private static void ApplyClub(
            PrototypeUITheme theme,
            PresentationMaterials materials)
        {
            Scene scene = OpenRequiredScene(ClubScenePath);
            DestroyRoot(scene, "Club_PresentationRoot");
            DestroyRoot(scene, "Club_PresentationUI");
            GameObject root = CreateSceneRoot(scene, "Club_PresentationRoot");

            AssignMaterial(scene, "Club_Floor", materials.darkWall);
            AssignMaterial(scene, "Stage", materials.clubWall);
            AssignMaterial(scene, "Bar", materials.wood);
            ConfigureClubEnvironment(scene, root.transform, materials);
            CreateLocationHeader(
                scene,
                "Club_PresentationUI",
                "클럽",
                "LIVE FLOOR",
                theme,
                theme.dangerAccent);
            StyleClubUi(scene, theme);
            ConfigureWorldUi(scene, theme, true);
            ConfigureCamera(
                scene,
                new Vector3(0f, 10.8f, -12.5f),
                Quaternion.LookRotation(
                    new Vector3(0f, 0.5f, 0.3f) -
                    new Vector3(0f, 10.8f, -12.5f),
                    Vector3.up),
                56f,
                new Color(0.025f, 0.012f, 0.028f, 1f));
            EnsureSceneBasics(scene);
            ValidateClub(scene);
            SaveScene(scene, ClubScenePath);
        }

        private static void ConfigureClubEnvironment(
            Scene scene,
            Transform root,
            PresentationMaterials materials)
        {
            Primitive("DanceFloor", root, PrimitiveType.Cube,
                new Vector3(0f, 0.075f, -1.25f),
                new Vector3(7.4f, 0.08f, 4.75f), materials.clubWall);
            for (int x = -3; x <= 3; x++)
            {
                Primitive($"DanceFloorLine_X_{x + 4:00}", root,
                    PrimitiveType.Cube,
                    new Vector3(x * 1.05f, 0.125f, -1.25f),
                    new Vector3(0.025f, 0.012f, 4.5f),
                    x % 2 == 0 ? materials.magenta : materials.cyan);
            }

            for (int z = 0; z < 4; z++)
            {
                Primitive($"DanceFloorLine_Z_{z + 1:00}", root,
                    PrimitiveType.Cube,
                    new Vector3(0f, 0.125f, -2.95f + z * 1.15f),
                    new Vector3(7.1f, 0.012f, 0.025f),
                    z % 2 == 0 ? materials.red : materials.magenta);
            }

            Primitive("StageFrontTrim", root, PrimitiveType.Cube,
                new Vector3(0f, 0.84f, 2.65f),
                new Vector3(5.5f, 0.12f, 0.16f), materials.red);
            Primitive("AudienceRail_Left", root, PrimitiveType.Cube,
                new Vector3(-3.95f, 0.35f, -1.3f),
                new Vector3(0.1f, 0.7f, 4.8f), materials.steel);
            Primitive("AudienceRail_Right", root, PrimitiveType.Cube,
                new Vector3(3.95f, 0.35f, -1.3f),
                new Vector3(0.1f, 0.7f, 4.8f), materials.steel);

            ClubController controller = FindComponentInScene<ClubController>(scene);
            Require(controller != null, "ClubController is missing.");
            Material[] crowdMaterials =
            {
                materials.audienceCyan,
                materials.audienceRed,
                materials.audienceGold,
            };
            for (int i = 0; i < controller.audience.Length; i++)
            {
                AudiencePulse pulse = controller.audience[i];
                Require(pulse != null, "Club audience reference is missing.");
                Transform audience = pulse.transform;
                Transform oldVisual = audience.Find("VS_AudienceVisual");
                if (oldVisual != null)
                {
                    UnityEngine.Object.DestroyImmediate(oldVisual.gameObject);
                }

                Renderer rootRenderer = audience.GetComponent<Renderer>();
                if (rootRenderer != null)
                {
                    rootRenderer.enabled = false;
                }

                Collider collider = audience.GetComponent<Collider>();
                if (collider != null)
                {
                    collider.enabled = false;
                }

                audience.position = AudiencePositions[i % AudiencePositions.Length];
                audience.rotation = Quaternion.Euler(
                    0f, -18f + (i * 29f) % 36f, 0f);
                audience.localScale = Vector3.one;
                Transform visual = Child(audience, "VS_AudienceVisual");
                float height = 0.88f + (i % 4) * 0.06f;
                Primitive("Body", visual, PrimitiveType.Capsule,
                    new Vector3(0f, height, 0f),
                    new Vector3(0.44f, height * 0.72f, 0.38f),
                    crowdMaterials[i % crowdMaterials.Length]);
                Primitive("Head", visual, PrimitiveType.Sphere,
                    new Vector3(0f, height * 1.78f, 0f),
                    new Vector3(0.42f, 0.42f, 0.42f), materials.white);
            }
        }

        private static void StyleDialogue(
            Scene scene,
            PrototypeUITheme theme)
        {
            DialogueUI dialogue = FindComponentInScene<DialogueUI>(scene);
            Require(dialogue != null, "DialogueUI is missing.");
            Canvas canvas = dialogue.GetComponentInParent<Canvas>();
            ConfigureCanvas(canvas);
            RectTransform panel = dialogue.GetComponent<RectTransform>();
            SetNormalizedRect(panel,
                new Vector2(0.07f, 0.045f),
                new Vector2(0.93f, 0.265f));
            StylePanel(panel, theme.panel, theme.panelBorder);

            SetNormalizedRect(dialogue.speakerText.rectTransform,
                new Vector2(0.045f, 0.62f),
                new Vector2(0.32f, 0.9f));
            StyleText(dialogue.speakerText, 21, TextAnchor.MiddleLeft,
                theme.cyanAccent);
            SetNormalizedRect(dialogue.bodyText.rectTransform,
                new Vector2(0.045f, 0.12f),
                new Vector2(0.82f, 0.64f));
            StyleText(dialogue.bodyText, 23, TextAnchor.MiddleLeft,
                theme.primaryText);
            SetNormalizedRect(dialogue.nextButton.GetComponent<RectTransform>(),
                new Vector2(0.84f, 0.18f),
                new Vector2(0.96f, 0.48f));
            StyleButton(dialogue.nextButton, theme);

            RectTransform accent = GetOrCreateRect(
                panel, "VS_DialogueAccent");
            SetNormalizedRect(accent,
                new Vector2(0f, 0.14f),
                new Vector2(0.006f, 0.86f));
            SetImage(accent, theme.cyanAccent);
        }

        private static void StyleEquipmentShopUi(
            Scene scene,
            PrototypeUITheme theme)
        {
            EquipmentShopController controller =
                FindComponentInScene<EquipmentShopController>(scene);
            Require(controller != null, "EquipmentShopController is missing.");
            Canvas canvas = controller.GetComponent<Canvas>();
            Require(canvas != null, "EquipmentShop UI canvas is missing.");
            ConfigureCanvas(canvas);

            RectTransform panel = controller.titleText.transform.parent as RectTransform;
            Require(panel != null, "EquipmentShop panel is missing.");
            SetNormalizedRect(panel,
                new Vector2(0.61f, 0.07f),
                new Vector2(0.96f, 0.9f));
            StylePanel(panel, theme.panel, theme.panelBorder);

            SetNormalizedRect(controller.titleText.rectTransform,
                new Vector2(0.07f, 0.86f),
                new Vector2(0.64f, 0.96f));
            StyleText(controller.titleText, 29, TextAnchor.MiddleLeft,
                theme.primaryText);

            Text eyebrow = GetOrCreateText(panel, "VS_ItemEyebrow");
            eyebrow.text = "RECOMMENDED GEAR";
            SetNormalizedRect(eyebrow.rectTransform,
                new Vector2(0.07f, 0.79f),
                new Vector2(0.62f, 0.85f));
            StyleText(eyebrow, 14, TextAnchor.MiddleLeft, theme.goldAccent);

            SetNormalizedRect(controller.itemNameText.rectTransform,
                new Vector2(0.07f, 0.68f),
                new Vector2(0.68f, 0.78f));
            StyleText(controller.itemNameText, 25, TextAnchor.MiddleLeft,
                theme.primaryText);

            RectTransform ownedBadge = GetOrCreateRect(panel, "VS_OwnedBadge");
            SetNormalizedRect(ownedBadge,
                new Vector2(0.68f, 0.69f),
                new Vector2(0.93f, 0.78f));
            SetImage(ownedBadge, new Color(0.08f, 0.19f, 0.2f, 1f));
            controller.ownedText.rectTransform.SetParent(ownedBadge, false);
            Stretch(controller.ownedText.rectTransform,
                new Vector2(10f, 3f), new Vector2(-10f, -3f));
            StyleText(controller.ownedText, 15, TextAnchor.MiddleCenter,
                theme.cyanAccent);

            SetNormalizedRect(controller.itemDescriptionText.rectTransform,
                new Vector2(0.07f, 0.39f),
                new Vector2(0.93f, 0.65f));
            StyleText(controller.itemDescriptionText, 18,
                TextAnchor.UpperLeft, theme.primaryText);
            SetNormalizedRect(controller.priceText.rectTransform,
                new Vector2(0.07f, 0.22f),
                new Vector2(0.93f, 0.37f));
            StyleText(controller.priceText, 16,
                TextAnchor.UpperLeft, theme.secondaryText);

            SetNormalizedRect(controller.buyButton.GetComponent<RectTransform>(),
                new Vector2(0.52f, 0.07f),
                new Vector2(0.93f, 0.17f));
            SetNormalizedRect(controller.leaveButton.GetComponent<RectTransform>(),
                new Vector2(0.07f, 0.07f),
                new Vector2(0.47f, 0.17f));
            StyleButton(controller.buyButton, theme, true);
            StyleButton(controller.leaveButton, theme);

            RectTransform dialoguePanel = GetOrCreateRect(
                canvas.transform, "VS_ShopDialoguePanel");
            SetNormalizedRect(dialoguePanel,
                new Vector2(0.045f, 0.045f),
                new Vector2(0.56f, 0.285f));
            StylePanel(dialoguePanel, theme.panelSoft, theme.panelBorder);
            Text dialogueLabel = GetOrCreateText(
                dialoguePanel, "VS_DialogueLabel");
            dialogueLabel.text = "KEVIN / SHOPKEEPER";
            SetNormalizedRect(dialogueLabel.rectTransform,
                new Vector2(0.045f, 0.78f),
                new Vector2(0.62f, 0.94f));
            StyleText(dialogueLabel, 14, TextAnchor.MiddleLeft,
                theme.goldAccent);

            controller.kevinRequestText.rectTransform.SetParent(
                dialoguePanel, false);
            SetNormalizedRect(controller.kevinRequestText.rectTransform,
                new Vector2(0.045f, 0.43f),
                new Vector2(0.95f, 0.76f));
            StyleText(controller.kevinRequestText, 19,
                TextAnchor.MiddleLeft, theme.primaryText);
            controller.shopkeeperText.rectTransform.SetParent(
                dialoguePanel, false);
            SetNormalizedRect(controller.shopkeeperText.rectTransform,
                new Vector2(0.045f, 0.08f),
                new Vector2(0.95f, 0.42f));
            StyleText(controller.shopkeeperText, 18,
                TextAnchor.MiddleLeft, theme.secondaryText);
        }

        private static void StyleClubUi(
            Scene scene,
            PrototypeUITheme theme)
        {
            ClubController controller = FindComponentInScene<ClubController>(scene);
            Require(controller != null, "ClubController is missing.");
            Canvas canvas = controller.GetComponent<Canvas>();
            Require(canvas != null, "Club UI canvas is missing.");
            ConfigureCanvas(canvas);
            RectTransform panel = controller.titleText.transform.parent as RectTransform;
            Require(panel != null, "Club panel is missing.");
            SetNormalizedRect(panel,
                new Vector2(0.055f, 0.04f),
                new Vector2(0.945f, 0.285f));
            StylePanel(panel, theme.panel, theme.panelBorder);

            SetNormalizedRect(controller.titleText.rectTransform,
                new Vector2(0.04f, 0.59f),
                new Vector2(0.19f, 0.91f));
            StyleText(controller.titleText, 30, TextAnchor.MiddleLeft,
                theme.primaryText);
            Text eyebrow = GetOrCreateText(panel, "VS_ClubEyebrow");
            eyebrow.text = "PERFORMANCE CHECK";
            SetNormalizedRect(eyebrow.rectTransform,
                new Vector2(0.04f, 0.36f),
                new Vector2(0.2f, 0.57f));
            StyleText(eyebrow, 13, TextAnchor.MiddleLeft,
                theme.dangerAccent);

            Text headline = GetOrCreateText(panel, "VS_StatusHeadline");
            controller.statusHeadlineText = headline;
            SetNormalizedRect(headline.rectTransform,
                new Vector2(0.23f, 0.59f),
                new Vector2(0.54f, 0.9f));
            StyleText(headline, 26, TextAnchor.MiddleLeft,
                theme.goldAccent);
            SetNormalizedRect(controller.statusText.rectTransform,
                new Vector2(0.23f, 0.1f),
                new Vector2(0.69f, 0.61f));
            StyleText(controller.statusText, 17, TextAnchor.UpperLeft,
                theme.secondaryText);

            SetNormalizedRect(controller.performButton.GetComponent<RectTransform>(),
                new Vector2(0.71f, 0.18f),
                new Vector2(0.84f, 0.5f));
            SetNormalizedRect(controller.leaveButton.GetComponent<RectTransform>(),
                new Vector2(0.85f, 0.18f),
                new Vector2(0.96f, 0.5f));
            StyleButton(controller.performButton, theme, true);
            StyleButton(controller.leaveButton, theme);
            EditorUtility.SetDirty(controller);
        }

        private static void ConfigureWorldUi(
            Scene scene,
            PrototypeUITheme theme,
            bool promptAtTop)
        {
            GameObject worldUi = FindNamed(scene, "World_UI");
            if (worldUi == null)
            {
                return;
            }

            Canvas canvas = worldUi.GetComponent<Canvas>();
            ConfigureCanvas(canvas);
            InteractionPromptUI prompt =
                worldUi.GetComponentInChildren<InteractionPromptUI>(true);
            if (prompt != null)
            {
                RectTransform rect = prompt.GetComponent<RectTransform>();
                SetFixedRect(
                    rect,
                    promptAtTop ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0f),
                    promptAtTop ? new Vector2(0f, -30f) : new Vector2(0f, 36f),
                    new Vector2(470f, 62f),
                    promptAtTop ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0f));
                StylePanel(rect, theme.panel, theme.panelBorder);
                if (prompt.promptText != null)
                {
                    prompt.promptText.text = "E - 상호작용";
                    StyleText(prompt.promptText, 20,
                        TextAnchor.MiddleCenter, theme.primaryText);
                }
            }

            ToastUI toast = worldUi.GetComponentInChildren<ToastUI>(true);
            if (toast != null)
            {
                RectTransform rect = toast.GetComponent<RectTransform>();
                SetFixedRect(rect, new Vector2(0.5f, 1f),
                    new Vector2(0f, -104f), new Vector2(620f, 72f),
                    new Vector2(0.5f, 1f));
                StylePanel(rect, theme.panelSoft, theme.panelBorder);
                if (toast.messageText != null)
                {
                    StyleText(toast.messageText, 18,
                        TextAnchor.MiddleCenter, theme.primaryText);
                }
            }
        }

        private static void CreateLocationHeader(
            Scene scene,
            string canvasName,
            string title,
            string eyebrow,
            PrototypeUITheme theme,
            Color accent)
        {
            Canvas canvas = CreateCanvas(scene, canvasName, 100);
            RectTransform panel = CreateRect(canvas.transform, "LocationHeader");
            SetFixedRect(panel, new Vector2(0f, 1f),
                new Vector2(30f, -28f), new Vector2(390f, 92f),
                new Vector2(0f, 1f));
            StylePanel(panel, theme.panelSoft, theme.panelBorder);
            RectTransform accentRect = CreateRect(panel, "Accent");
            SetNormalizedRect(accentRect,
                new Vector2(0f, 0.12f), new Vector2(0.012f, 0.88f));
            SetImage(accentRect, accent);
            Text eyebrowText = CreateText(panel, "Eyebrow", eyebrow, 13,
                TextAnchor.MiddleLeft, accent);
            SetNormalizedRect(eyebrowText.rectTransform,
                new Vector2(0.07f, 0.58f), new Vector2(0.93f, 0.88f));
            Text titleText = CreateText(panel, "Title", title, 27,
                TextAnchor.MiddleLeft, theme.primaryText);
            SetNormalizedRect(titleText.rectTransform,
                new Vector2(0.07f, 0.12f), new Vector2(0.93f, 0.6f));
        }

        private static void CreateDoorFrame(
            Transform parent,
            Vector3 basePosition,
            Material frame,
            Material accent)
        {
            Primitive("DoorFrame_Left", parent, PrimitiveType.Cube,
                basePosition + new Vector3(-0.88f, 1f, 0f),
                new Vector3(0.18f, 2.05f, 0.28f), frame);
            Primitive("DoorFrame_Right", parent, PrimitiveType.Cube,
                basePosition + new Vector3(0.88f, 1f, 0f),
                new Vector3(0.18f, 2.05f, 0.28f), frame);
            Primitive("DoorFrame_Top", parent, PrimitiveType.Cube,
                basePosition + new Vector3(0f, 2.02f, 0f),
                new Vector3(1.94f, 0.18f, 0.28f), frame);
            Primitive("DoorGlow", parent, PrimitiveType.Cube,
                basePosition + new Vector3(0f, 1.02f, 0.08f),
                new Vector3(1.55f, 1.72f, 0.08f), accent);
        }

        private static void CreateWorldSign(
            Transform parent,
            string name,
            string label,
            Vector3 position,
            Vector3 scale,
            Material material,
            Color textColor,
            WorldSignFace face)
        {
            GameObject board = Primitive(
                name + "_Board",
                parent,
                PrimitiveType.Cube,
                position, scale, material);
            GameObject textObject = new GameObject(name + "_Text");
            textObject.transform.SetParent(parent, false);
            ConfigureWorldSignTextFacing(
                textObject.transform,
                board.transform,
                face);
            TextMesh text = textObject.AddComponent<TextMesh>();
            text.text = label;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 72;
            text.characterSize = 0.045f;
            text.color = textColor;
        }

        private static void ConfigureWorldSignTextFacing(
            Transform text,
            Transform board,
            WorldSignFace face)
        {
            float faceDirection = face == WorldSignFace.PositiveZ ? 1f : -1f;
            text.localPosition = board.localPosition + new Vector3(
                0f,
                0f,
                faceDirection * board.localScale.z * 0.58f);
            text.localRotation = face == WorldSignFace.PositiveZ
                ? Quaternion.Euler(0f, 180f, 0f)
                : Quaternion.identity;
            text.localScale = Vector3.one;
        }

        private enum WorldSignFace
        {
            NegativeZ,
            PositiveZ,
        }

        private static void CreateCharacter(
            Transform parent,
            string name,
            Vector3 basePosition,
            Material bodyMaterial,
            Material headMaterial,
            bool apron)
        {
            Transform character = Child(parent, name);
            Primitive("Body", character, PrimitiveType.Capsule,
                basePosition + new Vector3(0f, 0.9f, 0f),
                new Vector3(0.58f, 0.78f, 0.52f), bodyMaterial);
            Primitive("Head", character, PrimitiveType.Sphere,
                basePosition + new Vector3(0f, 1.82f, 0f),
                new Vector3(0.54f, 0.54f, 0.54f), headMaterial);
            if (apron)
            {
                Primitive("Apron", character, PrimitiveType.Cube,
                    basePosition + new Vector3(0f, 0.85f, -0.31f),
                    new Vector3(0.56f, 0.82f, 0.08f), headMaterial,
                    new Vector3(4f, 0f, 0f));
            }
        }

        private static void ConfigurePlayer(Scene scene, Material material)
        {
            AssignMaterial(scene, "Kevin_Player", material);
        }

        private static void ConfigureCamera(
            Scene scene,
            Vector3 position,
            Quaternion rotation,
            float fieldOfView,
            Color background)
        {
            Camera camera = GetSceneComponents<Camera>(scene)
                .FirstOrDefault(item => item.CompareTag("MainCamera"));
            Require(camera != null, "Main Camera is missing.");
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.orthographic = false;
            camera.fieldOfView = fieldOfView;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
        }

        private static void EnsureSceneBasics(Scene scene)
        {
            Camera[] cameras = GetSceneComponents<Camera>(scene);
            Camera main = cameras.FirstOrDefault(
                item => item.CompareTag("MainCamera"));
            Require(main != null, "Main Camera is missing in " + scene.name);
            AudioListener keeper = main.GetComponent<AudioListener>();
            if (keeper == null)
            {
                keeper = main.gameObject.AddComponent<AudioListener>();
            }

            keeper.enabled = true;
            AudioListener[] listeners = GetSceneComponents<AudioListener>(scene);
            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] != keeper)
                {
                    listeners[i].enabled = false;
                }
            }

            EventSystem[] systems = GetSceneComponents<EventSystem>(scene);
            EventSystem activeSystem = systems.FirstOrDefault();
            if (activeSystem == null)
            {
                GameObject eventObject = new GameObject(
                    "EventSystem",
                    typeof(EventSystem),
                    typeof(StandaloneInputModule));
                SceneManager.MoveGameObjectToScene(eventObject, scene);
                activeSystem = eventObject.GetComponent<EventSystem>();
                systems = GetSceneComponents<EventSystem>(scene);
            }

            activeSystem.enabled = true;
            activeSystem.gameObject.SetActive(true);
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] != activeSystem)
                {
                    systems[i].enabled = false;
                }
            }

            Canvas[] canvases = GetSceneComponents<Canvas>(scene);
            for (int i = 0; i < canvases.Length; i++)
            {
                ConfigureCanvas(canvases[i]);
            }
        }

        private static void ConfigureCanvas(Canvas canvas)
        {
            if (canvas == null)
            {
                return;
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        private static Canvas CreateCanvas(
            Scene scene,
            string name,
            int sortingOrder)
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
            canvas.sortingOrder = sortingOrder;
            ConfigureCanvas(canvas);
            return canvas;
        }

        private static void ValidateStreet(Scene scene)
        {
            ValidateCommon(scene, GameLocation.Street,
                "Street_PresentationRoot");
            SceneDoor[] doors = GetSceneComponents<SceneDoor>(scene);
            Require(doors.Length == 3, "Street must keep three SceneDoors.");
            Require(doors.Any(item =>
                item.sceneName == SashimiBoyConstants.Scenes.FishShopDialogue &&
                item.destinationLocation == GameLocation.FishShop),
                "FishShop Street door changed.");
            Require(doors.Any(item =>
                item.sceneName == SashimiBoyConstants.Scenes.EquipmentShop &&
                item.destinationLocation == GameLocation.EquipmentShop),
                "EquipmentShop Street door changed.");
            Require(doors.Any(item =>
                item.sceneName == SashimiBoyConstants.Scenes.Club &&
                item.destinationLocation == GameLocation.Club),
                "Club Street door changed.");
        }

        private static void ValidateFishShop(Scene scene)
        {
            ValidateCommon(scene, GameLocation.FishShop,
                "FishShop_PresentationRoot");
            Require(GetSceneComponents<ReturnToStreetDoor>(scene).Length == 1,
                "FishShop return door changed.");
            Require(GetSceneComponents<StageStarterInteractable>(scene).Length == 1,
                "Stage 01 starter changed.");
            DialogueUI dialogue = FindComponentInScene<DialogueUI>(scene);
            Require(dialogue != null && dialogue.speakerText != null &&
                dialogue.bodyText != null && dialogue.nextButton != null,
                "FishShop dialogue references are incomplete.");
        }

        private static void ValidateEquipmentShop(Scene scene)
        {
            ValidateCommon(scene, GameLocation.EquipmentShop,
                "EquipmentShop_PresentationRoot");
            EquipmentShopController controller =
                FindComponentInScene<EquipmentShopController>(scene);
            Require(controller != null && controller.titleText != null &&
                controller.kevinRequestText != null &&
                controller.shopkeeperText != null &&
                controller.itemNameText != null &&
                controller.itemDescriptionText != null &&
                controller.priceText != null && controller.ownedText != null &&
                controller.buyButton != null && controller.leaveButton != null,
                "EquipmentShop UI references are incomplete.");
            Require(GetSceneComponents<ReturnToStreetDoor>(scene).Length == 1,
                "EquipmentShop return door changed.");
        }

        private static void ValidateClub(Scene scene)
        {
            ValidateCommon(scene, GameLocation.Club,
                "Club_PresentationRoot");
            ClubController controller = FindComponentInScene<ClubController>(scene);
            ClubPerformanceGate gate =
                FindComponentInScene<ClubPerformanceGate>(scene);
            Require(controller != null && controller.titleText != null &&
                controller.statusHeadlineText != null &&
                controller.statusText != null &&
                controller.performButton != null &&
                controller.leaveButton != null &&
                controller.audience != null && controller.audience.Length == 14 &&
                controller.audience.All(item => item != null),
                "ClubController references are incomplete.");
            Require(gate != null && gate.clubController == controller,
                "Club gate reference changed.");
            Require(GetSceneComponents<ReturnToStreetDoor>(scene).Length == 1,
                "Club return door changed.");
            Require(FindNamed(scene, "ClubArtRoot") != null,
                "Generated Club art is missing from the main scene.");
        }

        private static void ValidateCommon(
            Scene scene,
            GameLocation expectedLocation,
            string presentationRoot)
        {
            SceneLocationMarker[] markers =
                GetSceneComponents<SceneLocationMarker>(scene);
            Require(markers.Length == 1 &&
                markers[0].location == expectedLocation,
                "Scene location marker changed in " + scene.name);
            Require(FindNamed(scene, presentationRoot) != null,
                presentationRoot + " is missing.");
            Require(GetSceneComponents<EventSystem>(scene)
                .Count(item => item.isActiveAndEnabled) == 1,
                scene.name + " must have one active EventSystem.");
            Require(GetSceneComponents<AudioListener>(scene)
                .Count(item => item.isActiveAndEnabled) == 1,
                scene.name + " must have one active AudioListener.");
            Require(GetSceneComponents<Canvas>(scene).All(item =>
                item.renderMode != RenderMode.WorldSpace),
                scene.name + " contains a world-space Canvas.");

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform[] transforms =
                    root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    Require(
                        GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                            transforms[i].gameObject) == 0,
                        scene.name + " contains a missing script.");
                }
            }
        }

        private static Scene OpenRequiredScene(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "Required scene is missing.", path);
            }

            return EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }

        private static void SaveScene(Scene scene, string path)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, path))
            {
                throw new InvalidOperationException("Could not save " + path);
            }
        }

        private static GameObject CreateSceneRoot(Scene scene, string name)
        {
            GameObject root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }

        private static void DestroyRoot(Scene scene, string name)
        {
            GameObject target = scene.GetRootGameObjects()
                .FirstOrDefault(item => item.name == name);
            if (target != null)
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static Transform Child(Transform parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static GameObject Primitive(
            string name,
            Transform parent,
            PrimitiveType type,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            Vector3? localEuler = null)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(
                localEuler ?? Vector3.zero);
            go.transform.localScale = localScale;
            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            Renderer renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        private static void CreatePointLight(
            Transform parent,
            string name,
            Vector3 position,
            Color color,
            float intensity,
            float range)
        {
            GameObject lightObject = new GameObject(name);
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.localPosition = position;
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
        }

        private static void HideDoorVisual(Scene scene, string name)
        {
            GameObject door = FindNamed(scene, name);
            Require(door != null, name + " is missing.");
            Renderer renderer = door.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
            }
        }

        private static void HideRenderers(Scene scene, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                GameObject target = FindNamed(scene, names[i]);
                if (target == null)
                {
                    continue;
                }

                Renderer[] renderers =
                    target.GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0;
                     rendererIndex < renderers.Length;
                     rendererIndex++)
                {
                    renderers[rendererIndex].enabled = false;
                }
            }
        }

        private static void AssignMaterial(
            Scene scene,
            string name,
            Material material)
        {
            GameObject target = FindNamed(scene, name);
            if (target == null)
            {
                return;
            }

            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = true;
                renderer.sharedMaterial = material;
            }
        }

        private static GameObject FindNamed(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform[] transforms =
                    roots[i].GetComponentsInChildren<Transform>(true);
                for (int j = 0; j < transforms.Length; j++)
                {
                    if (transforms[j].name == name)
                    {
                        return transforms[j].gameObject;
                    }
                }
            }

            return null;
        }

        private static T RequireComponentOnNamed<T>(Scene scene, string name)
            where T : Component
        {
            GameObject target = FindNamed(scene, name);
            Require(target != null, name + " is missing.");
            T component = target.GetComponent<T>();
            Require(component != null, typeof(T).Name + " is missing on " + name);
            return component;
        }

        private static T FindComponentInScene<T>(Scene scene)
            where T : Component
        {
            return GetSceneComponents<T>(scene).FirstOrDefault();
        }

        private static T[] GetSceneComponents<T>(Scene scene)
            where T : Component
        {
            var result = new List<T>();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                result.AddRange(roots[i].GetComponentsInChildren<T>(true));
            }

            return result.ToArray();
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            GameObject child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return child.GetComponent<RectTransform>();
        }

        private static RectTransform GetOrCreateRect(
            Transform parent,
            string name)
        {
            Transform existing = parent.Find(name);
            return existing != null
                ? existing.GetComponent<RectTransform>()
                : CreateRect(parent, name);
        }

        private static Text GetOrCreateText(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                Text existingText = existing.GetComponent<Text>();
                if (existingText != null)
                {
                    return existingText;
                }
            }

            return CreateText(parent, name, string.Empty, 18,
                TextAnchor.MiddleLeft, Color.white);
        }

        private static Text CreateText(
            Transform parent,
            string name,
            string value,
            int fontSize,
            TextAnchor alignment,
            Color color)
        {
            GameObject textObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            textObject.transform.SetParent(parent, false);
            Text text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            StyleText(text, fontSize, alignment, color);
            return text;
        }

        private static void StyleText(
            Text text,
            int fontSize,
            TextAnchor alignment,
            Color color)
        {
            text.font = text.font != null
                ? text.font
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.lineSpacing = 1f;
            text.raycastTarget = false;
            text.resizeTextForBestFit = false;
        }

        private static void StylePanel(
            RectTransform rect,
            Color color,
            Color border)
        {
            Image image = SetImage(rect, color);
            image.raycastTarget = false;
            Outline outline = rect.GetComponent<Outline>();
            if (outline == null)
            {
                outline = rect.gameObject.AddComponent<Outline>();
            }

            outline.effectColor = border;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;
        }

        private static Image SetImage(RectTransform rect, Color color)
        {
            Image image = rect.GetComponent<Image>();
            if (image == null)
            {
                image = rect.gameObject.AddComponent<Image>();
            }

            image.color = color;
            return image;
        }

        private static void StyleButton(
            Button button,
            PrototypeUITheme theme,
            bool primary = false)
        {
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = theme.CreateButtonColors();
            if (primary)
            {
                colors.normalColor = new Color(0.08f, 0.42f, 0.46f, 1f);
                colors.highlightedColor =
                    new Color(0.12f, 0.58f, 0.62f, 1f);
            }

            button.colors = colors;
            Image image = button.GetComponent<Image>();
            if (image == null)
            {
                image = button.gameObject.AddComponent<Image>();
            }

            image.color = colors.normalColor;
            button.targetGraphic = image;
            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                StyleText(label, 18, TextAnchor.MiddleCenter,
                    theme.primaryText);
            }
        }

        private static void SetNormalizedRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private static void SetFixedRect(
            RectTransform rect,
            Vector2 anchor,
            Vector2 anchoredPosition,
            Vector2 size,
            Vector2 pivot)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        private static void Stretch(
            RectTransform rect,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.localScale = Vector3.one;
        }

        private static Material MaterialAsset(
            string name,
            Color color,
            float smoothness,
            Color? emission = null)
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

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", smoothness);
            }

            if (emission.HasValue && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
                material.globalIlluminationFlags =
                    MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
            }

            EditorUtility.SetDirty(material);
            return material;
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

        private sealed class PresentationMaterials
        {
            public Material road;
            public Material sidewalk;
            public Material tile;
            public Material tileJoint;
            public Material neutralWall;
            public Material darkWall;
            public Material tealWall;
            public Material cyan;
            public Material wood;
            public Material woodLight;
            public Material warmWall;
            public Material amber;
            public Material gold;
            public Material clubWall;
            public Material magenta;
            public Material red;
            public Material steel;
            public Material salmon;
            public Material white;
            public Material audienceCyan;
            public Material audienceRed;
            public Material audienceGold;
        }
    }
}
#endif
