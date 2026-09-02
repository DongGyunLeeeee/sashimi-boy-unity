#if UNITY_EDITOR
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
    public static class SashimiBoyPrototypeGenerator
    {
        private const string Root = "Assets/_SashimiBoy";
        private const string ScenesPath = Root + "/Scenes";
        private const string DataPath = Root + "/Data/Generated";
        private const string Stage01SalmonMusicPath = Root + "/Audio/Music/Stage_01_Salmon/stage01_salmon_main.mp3";

        [MenuItem("Sashimi Boy/Generate Non-Stage Prototype")]
        public static void GenerateNonStagePrototype()
        {
            GenerateNonStagePrototypeInternal(true);
        }

        public static void GenerateNonStagePrototypeNoDialog()
        {
            GenerateNonStagePrototypeInternal(false);
        }

        private static void GenerateNonStagePrototypeInternal(bool showDialog)
        {
            EnsureFolders();
            CreateDefaultDataAssets();
            CreateBootstrapScene();
            CreateStreetScene();
            CreateFishShopDialogueScene();
            CreateEquipmentShopScene();
            CreateClubScene();
            CreateStage01SalmonScene();
            Stage01SalmonPresentationPipeline.
                RebuildGeneratedPresentationForPrototypeGenerator();
            VerticalSlicePresentationPipeline.
                RebuildForPrototypeGenerator();
            NewAssetsKevinCameraPipeline.
                RebuildForPrototypeGenerator();
            NewFishShopAssetsScenePipeline.
                RebuildStreetFishShopFrontageBatch();
            NewFishShopAssetsScenePipeline.
                ApplyFishShopArtPassForPrototypeGenerator();
            AddScenesToBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "Sashimi Boy",
                    "Non-stage prototype scenes and data were generated.",
                    "OK");
            }
        }

        [MenuItem("Sashimi Boy/Generate Stage 01 Salmon Scaffold")]
        public static void GenerateStage01SalmonScaffold()
        {
            GenerateStage01SalmonScaffoldInternal(true);
        }

        public static void GenerateStage01SalmonScaffoldNoDialog()
        {
            GenerateStage01SalmonScaffoldInternal(false);
        }

        private static void GenerateStage01SalmonScaffoldInternal(bool showDialog)
        {
            EnsureFolders();
            CreateStage01SalmonScene();
            Stage01SalmonPresentationPipeline.
                RebuildGeneratedPresentationForPrototypeGenerator();
            AddScenesToBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (showDialog)
            {
                EditorUtility.DisplayDialog("Sashimi Boy", "Stage 01 Salmon timing scaffold was generated.", "OK");
            }
        }

        [MenuItem("Sashimi Boy/Open Street Scene")]
        public static void OpenStreetScene()
        {
            string path = ScenesPath + "/" + SashimiBoyConstants.Scenes.Street + ".unity";
            if (File.Exists(path))
            {
                EditorSceneManager.OpenScene(path);
            }
            else
            {
                EditorUtility.DisplayDialog("Sashimi Boy", "Generate the prototype first.", "OK");
            }
        }

        [MenuItem("Sashimi Boy/Migrate Equipment Shop Purchase Defaults")]
        public static void MigrateEquipmentShopPurchaseDefaults()
        {
            string path = ScenesPath + "/" +
                SashimiBoyConstants.Scenes.EquipmentShop + ".unity";
            MigrateEquipmentShopScenePurchaseDefaults(path);

            AssetDatabase.SaveAssets();
        }

        private static void MigrateEquipmentShopScenePurchaseDefaults(
            string path)
        {
            if (!File.Exists(path))
            {
                throw new UnityException(
                    "EquipmentShop scene is missing at " + path);
            }

            Scene scene = EditorSceneManager.OpenScene(
                path,
                OpenSceneMode.Single);
            var controllers = new List<EquipmentShopController>();
            int activeAudioListeners = 0;
            int activeEventSystems = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                controllers.AddRange(
                    root.GetComponentsInChildren<EquipmentShopController>(true));

                AudioListener[] listeners =
                    root.GetComponentsInChildren<AudioListener>(true);
                for (int i = 0; i < listeners.Length; i++)
                {
                    if (listeners[i].isActiveAndEnabled)
                    {
                        activeAudioListeners++;
                    }
                }

                EventSystem[] eventSystems =
                    root.GetComponentsInChildren<EventSystem>(true);
                for (int i = 0; i < eventSystems.Length; i++)
                {
                    if (eventSystems[i].isActiveAndEnabled)
                    {
                        activeEventSystems++;
                    }
                }

                Transform[] transforms =
                    root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    if (GameObjectUtility
                            .GetMonoBehavioursWithMissingScriptCount(
                                transforms[i].gameObject) != 0)
                    {
                        throw new UnityException(
                            "EquipmentShop contains a missing script under " +
                            transforms[i].name + ".");
                    }
                }
            }

            if (controllers.Count != 1 ||
                controllers[0].titleText == null ||
                controllers[0].kevinRequestText == null ||
                controllers[0].shopkeeperText == null ||
                controllers[0].itemNameText == null ||
                controllers[0].itemDescriptionText == null ||
                controllers[0].priceText == null ||
                controllers[0].ownedText == null ||
                controllers[0].buyButton == null ||
                controllers[0].leaveButton == null)
            {
                throw new UnityException(
                    "EquipmentShop controller references are incomplete.");
            }

            if (activeAudioListeners != 1 || activeEventSystems != 1)
            {
                throw new UnityException(
                    "EquipmentShop must contain exactly one active " +
                    "AudioListener and EventSystem.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, path))
            {
                throw new UnityException(
                    "EquipmentShop scene could not be saved.");
            }
        }

        private static void EnsureFolders()
        {
            CreateFolder("Assets", "_SashimiBoy");
            CreateFolder(Root, "Scenes");
            CreateFolder(Root, "Data");
            CreateFolder(Root + "/Data", "Generated");
        }

        private static void CreateFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }

        private static void CreateBootstrapScene()
        {
            NewScene(SashimiBoyConstants.Scenes.Bootstrap, GameLocation.Unknown);
            var systems = new GameObject("Bootstrap_Systems");
            systems.AddComponent<SaveManager>();
            systems.AddComponent<GameFlowManager>();
            systems.AddComponent<SceneTransitionService>();
            systems.AddComponent<PrototypeDebugHotkeys>();
            CreateBasicCamera(new Vector3(0f, 8f, -8f), Quaternion.Euler(55f, 0f, 0f));
            CreateDirectionalLight();
            SaveActiveScene(SashimiBoyConstants.Scenes.Bootstrap);
        }

        private static void CreateStreetScene()
        {
            NewScene(SashimiBoyConstants.Scenes.Street, GameLocation.Street);
            CreateBasicCamera(new Vector3(0f, 12f, -10f), Quaternion.Euler(60f, 0f, 0f));
            CreateDirectionalLight();
            Cube("Street_Ground", Vector3.zero, new Vector3(20f, 0.1f, 12f), new Color(0.18f, 0.18f, 0.18f));
            Cube("Fish_Market_Left", new Vector3(-7f, 0.8f, 2f), new Vector3(4f, 1.6f, 5f), new Color(0.1f, 0.35f, 0.55f));
            Cube("Equipment_Shop_Back", new Vector3(0f, 0.8f, 4.5f), new Vector3(4f, 1.6f, 2f), new Color(0.45f, 0.35f, 0.2f));
            Cube("Club_Right", new Vector3(7f, 0.8f, 2f), new Vector3(4f, 1.6f, 5f), new Color(0.23f, 0.05f, 0.28f));
            Cube("Sashimi_Shop_Front", new Vector3(-3.5f, 0.8f, -4f), new Vector3(4.5f, 1.6f, 2f), new Color(0.15f, 0.45f, 0.5f));

            CreatePlayer(new Vector3(0f, 0.6f, -1.5f));
            CreateSceneDoor("Door_To_FishShopDialogue", "횟집으로 들어가기", SashimiBoyConstants.Scenes.FishShopDialogue, GameLocation.FishShop, new Vector3(-3.5f, 0.8f, -2.6f));
            CreateSceneDoor("Door_To_EquipmentShop", "장비 가게로 들어가기", SashimiBoyConstants.Scenes.EquipmentShop, GameLocation.EquipmentShop, new Vector3(0f, 0.8f, 3.2f));
            CreateSceneDoor("Door_To_Club", "클럽으로 들어가기", SashimiBoyConstants.Scenes.Club, GameLocation.Club, new Vector3(7f, 0.8f, -0.8f));

            CreateTextMeshLabel("Label_FishShop", "회 써는 곳", new Vector3(-3.5f, 2.1f, -2.6f));
            CreateTextMeshLabel("Label_EquipmentShop", "장비 가게", new Vector3(0f, 2.1f, 3.2f));
            CreateTextMeshLabel("Label_Club", "클럽", new Vector3(7f, 2.1f, -0.8f));
            CreateSharedWorldUI();
            AddDebugHotkeys();
            SaveActiveScene(SashimiBoyConstants.Scenes.Street);
        }

        private static void CreateFishShopDialogueScene()
        {
            NewScene(SashimiBoyConstants.Scenes.FishShopDialogue, GameLocation.FishShop);
            CreateBasicCamera(new Vector3(0f, 6f, -7f), Quaternion.Euler(45f, 0f, 0f));
            CreateDirectionalLight();
            Cube("FishShop_Floor", Vector3.zero, new Vector3(12f, 0.1f, 8f), new Color(0.22f, 0.22f, 0.2f));
            Cube("Counter", new Vector3(0f, 0.6f, 1.8f), new Vector3(7f, 1.2f, 0.5f), new Color(0.35f, 0.26f, 0.18f));
            Cube("Cutting_Board", new Vector3(0f, 1.25f, 1.45f), new Vector3(4.5f, 0.08f, 1.2f), new Color(0.85f, 0.75f, 0.55f));
            Cube("Placeholder_Fish", new Vector3(-0.3f, 1.37f, 1.45f), new Vector3(2.5f, 0.18f, 0.5f), new Color(0.95f, 0.65f, 0.6f));
            Cube("Boss", new Vector3(-2.3f, 1f, 2.7f), new Vector3(0.8f, 2f, 0.8f), new Color(0.55f, 0.45f, 0.34f));
            Cube("Kevin", new Vector3(2.1f, 1f, -0.6f), new Vector3(0.8f, 2f, 0.8f), new Color(0.2f, 0.55f, 0.65f));

            CreatePlayer(new Vector3(1.5f, 0.6f, -2.3f));
            CreateReturnDoor(new Vector3(4.8f, 0.8f, -2.8f));

            var stageStart = Cube("StartStage01_Placeholder", new Vector3(-2.1f, 0.5f, -1.8f), new Vector3(1.5f, 1f, 1.5f), new Color(0.9f, 0.75f, 0.15f));
            StageStarterInteractable starter = stageStart.AddComponent<StageStarterInteractable>();
            starter.prompt = "1스테이지 타이밍 스캐폴드";
            starter.stageSceneName = SashimiBoyConstants.Scenes.Stage01Salmon;
            starter.loadStageScene = true;
            CreateTextMeshLabel("Label_StagePlaceholder", "1스테이지 타이밍\n음악/클럭 스캐폴드", new Vector3(-2.1f, 1.5f, -1.8f));

            DialogueRunner runner = CreateDialogueUI();
            var bossTrigger = GameObject.Find("Boss").AddComponent<DialogueTrigger>();
            bossTrigger.runner = runner;
            bossTrigger.prompt = "사장과 대화";
            bossTrigger.fallbackSpeaker = "리듬감 있는 사장";
            bossTrigger.fallbackLine = "칼질은 박자야. 음악이 들어오면 내가 한 번 보여주고, 넌 다음 마디에 따라 해.";

            CreateSharedWorldUI(true);
            AddDebugHotkeys();
            SaveActiveScene(SashimiBoyConstants.Scenes.FishShopDialogue);
        }

        private static void CreateEquipmentShopScene()
        {
            NewScene(SashimiBoyConstants.Scenes.EquipmentShop, GameLocation.EquipmentShop);
            CreateBasicCamera(new Vector3(0f, 6.5f, -8f), Quaternion.Euler(45f, 0f, 0f));
            CreateDirectionalLight();
            Cube("Shop_Floor", Vector3.zero, new Vector3(13f, 0.1f, 8f), new Color(0.17f, 0.16f, 0.14f));
            Cube("Counter", new Vector3(0f, 0.6f, 2.3f), new Vector3(5f, 1.2f, 0.6f), new Color(0.35f, 0.25f, 0.14f));
            Cube("Shopkeeper", new Vector3(0f, 1f, 3.2f), new Vector3(0.8f, 2f, 0.8f), new Color(0.42f, 0.36f, 0.3f));
            for (int i = 0; i < 6; i++)
            {
                Cube("WallGear_" + i, new Vector3(-5f + i * 2f, 1.6f, 3.8f), new Vector3(0.7f, 1.1f, 0.15f), new Color(0.08f + i * 0.04f, 0.08f, 0.1f + i * 0.05f));
            }

            CreatePlayer(new Vector3(0f, 0.6f, -2.2f));
            CreateReturnDoor(new Vector3(5.2f, 0.8f, -2.6f));
            CreateEquipmentShopUI();
            CreateSharedWorldUI(true);
            AddDebugHotkeys();
            SaveActiveScene(SashimiBoyConstants.Scenes.EquipmentShop);
        }

        private static void CreateClubScene()
        {
            NewScene(SashimiBoyConstants.Scenes.Club, GameLocation.Club);
            CreateBasicCamera(new Vector3(0f, 9f, -9f), Quaternion.Euler(55f, 0f, 0f));
            CreateDirectionalLight(0.35f);
            Cube("Club_Floor", Vector3.zero, new Vector3(16f, 0.1f, 12f), new Color(0.08f, 0.06f, 0.09f));
            Cube("DJ_Booth", new Vector3(-5.5f, 0.7f, 2.7f), new Vector3(3f, 1.4f, 1.2f), new Color(0.1f, 0.08f, 0.12f));
            Cube("Stage", new Vector3(0f, 0.4f, 3.6f), new Vector3(5f, 0.8f, 2f), new Color(0.18f, 0.05f, 0.12f));
            Cube("Bar", new Vector3(6.6f, 0.8f, 1.5f), new Vector3(1.2f, 1.6f, 4f), new Color(0.26f, 0.05f, 0.05f));

            var audience = new List<AudiencePulse>();
            for (int i = 0; i < 14; i++)
            {
                float x = -4f + (i % 7) * 1.35f;
                float z = -1f - (i / 7) * 1.8f;
                var crowd = Cube("Audience_" + i, new Vector3(x, 0.85f, z), new Vector3(0.55f, 1.7f, 0.55f), new Color(0.2f + (i % 3) * 0.08f, 0.15f, 0.25f + (i % 2) * 0.08f));
                audience.Add(crowd.AddComponent<AudiencePulse>());
            }

            CreatePlayer(new Vector3(0f, 0.6f, -4f));
            CreateReturnDoor(new Vector3(6.4f, 0.8f, -4.5f));
            var gate = Cube("PerformanceGate", new Vector3(0f, 0.6f, 1.8f), new Vector3(2f, 1.2f, 0.4f), new Color(0.75f, 0.15f, 0.2f));
            ClubPerformanceGate clubGate = gate.AddComponent<ClubPerformanceGate>();
            clubGate.prompt = "무대 확인";
            CreateTextMeshLabel("Label_PerformanceGate", "공연 스테이지", new Vector3(0f, 1.7f, 1.8f));

            ClubController clubController = CreateClubUI();
            clubController.audience = audience.ToArray();
            clubGate.clubController = clubController;
            CreateSharedWorldUI(true);
            AddDebugHotkeys();
            SaveActiveScene(SashimiBoyConstants.Scenes.Club);
        }

        private static void CreateStage01SalmonScene()
        {
            NewScene(SashimiBoyConstants.Scenes.Stage01Salmon, GameLocation.FishStage);
            Camera camera = CreateBasicCamera(new Vector3(0f, 7.2f, 0f), Quaternion.Euler(90f, 0f, 0f));
            camera.orthographic = true;
            camera.orthographicSize = 3.4f;
            camera.gameObject.AddComponent<AudioListener>();
            CreateDirectionalLight();

            Cube("Stage01_Floor", Vector3.zero, new Vector3(8f, 0.08f, 5f), new Color(0.1f, 0.12f, 0.13f));
            Cube("Stage01_CuttingBoard", new Vector3(0f, 0.22f, 0f), new Vector3(5.3f, 0.16f, 1.75f), new Color(0.74f, 0.62f, 0.42f));
            Cube("Stage01_SalmonPlaceholder", new Vector3(0f, 0.45f, 0f), new Vector3(3.7f, 0.14f, 0.58f), new Color(0.95f, 0.43f, 0.36f));
            Cube("Stage01_KnifeLine", new Vector3(0f, 0.66f, 0f), new Vector3(0.06f, 0.08f, 1.65f), new Color(0.92f, 0.98f, 1f));
            new GameObject("Stage01_SliceLinesRoot");

            var scaffoldObject = new GameObject("Stage01Salmon_TimingScaffold");
            AudioSource source = scaffoldObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.volume = 1f;
            source.mute = false;
            source.spatialBlend = 0f;
            source.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Stage01SalmonMusicPath);
            AudioClock clock = scaffoldObject.AddComponent<AudioClock>();
            clock.audioSource = source;
            clock.playOnStart = false;
            Stage01SalmonTimingScaffold scaffold = scaffoldObject.AddComponent<Stage01SalmonTimingScaffold>();
            scaffold.musicClip = source.clip;
            scaffold.audioSource = source;
            scaffold.audioClock = clock;
            scaffold.bpm = 88f;
            scaffold.firstDownbeatSec = 0.683d;
            scaffold.gameplayStartSec = 11.592d;
            scaffold.gameplayEndSec = 120.683d;

            if (source.clip == null)
            {
                Debug.LogWarning("Stage 01 Salmon music clip was not found at " + Stage01SalmonMusicPath);
            }

            CreateSharedWorldUI(false);
            AddDebugHotkeys();
            SaveActiveScene(SashimiBoyConstants.Scenes.Stage01Salmon);
        }

        private static void NewScene(string name, GameLocation location)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var marker = new GameObject("SceneLocationMarker").AddComponent<SceneLocationMarker>();
            marker.location = location;
        }

        private static void SaveActiveScene(string sceneName)
        {
            string path = ScenesPath + "/" + sceneName + ".unity";
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), path);
        }

        private static Camera CreateBasicCamera(Vector3 position, Quaternion rotation)
        {
            var camObj = new GameObject("Main Camera");
            camObj.tag = "MainCamera";
            camObj.transform.SetPositionAndRotation(position, rotation);
            Camera cam = camObj.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.04f, 0.05f);
            return cam;
        }

        private static Light CreateDirectionalLight(float intensity = 0.8f)
        {
            var lightObj = new GameObject("Directional Light");
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            return light;
        }

        private static GameObject CreatePlayer(Vector3 position)
        {
            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Kevin_Player";
            player.transform.position = position;
            player.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
            SetColor(player, new Color(0.2f, 0.55f, 0.65f));
            player.AddComponent<CharacterController>();
            player.AddComponent<SimpleTopDownPlayerController>();
            player.AddComponent<InteractionSensor>();
            return player;
        }

        private static GameObject CreateSceneDoor(string name, string prompt, string sceneName, GameLocation location, Vector3 position)
        {
            var door = Cube(name, position, new Vector3(1.5f, 1.6f, 0.35f), new Color(0.92f, 0.82f, 0.25f));
            SceneDoor sceneDoor = door.AddComponent<SceneDoor>();
            sceneDoor.prompt = prompt;
            sceneDoor.sceneName = sceneName;
            sceneDoor.destinationLocation = location;
            return door;
        }

        private static GameObject CreateReturnDoor(Vector3 position)
        {
            var door = Cube("Door_To_Street", position, new Vector3(1.5f, 1.6f, 0.35f), new Color(0.9f, 0.8f, 0.2f));
            door.AddComponent<ReturnToStreetDoor>();
            CreateTextMeshLabel("Label_ReturnDoor", "거리로", position + new Vector3(0f, 1.3f, 0f));
            return door;
        }

        private static GameObject Cube(string name, Vector3 position, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            SetColor(go, color);
            return go;
        }

        private static void SetColor(GameObject go, Color color)
        {
            Renderer r = go.GetComponent<Renderer>();
            if (r == null)
            {
                return;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            var material = new Material(shader);
            material.color = color;
            r.sharedMaterial = material;
        }

        private static TextMesh CreateTextMeshLabel(string name, string text, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            TextMesh mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.characterSize = 0.28f;
            mesh.fontSize = 48;
            mesh.color = Color.white;
            go.AddComponent<BillboardLabel>();
            return mesh;
        }

        private static Canvas CreateCanvas(string name)
        {
            var canvasObj = new GameObject(name);
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
            if (Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var eventSystem = new GameObject("EventSystem");
                eventSystem.AddComponent<EventSystem>();
                eventSystem.AddComponent<StandaloneInputModule>();
            }

            return canvas;
        }

        private static RectTransform AddPanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            Image image = go.AddComponent<Image>();
            image.color = color;
            return rect;
        }

        private static Text AddText(Transform parent, string name, string content, int fontSize, TextAnchor alignment, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Text text = go.AddComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Button AddButton(Transform parent, string name, string label)
        {
            RectTransform panel = AddPanel(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.82f, 0.82f, 0.82f, 0.95f));
            Button button = panel.gameObject.AddComponent<Button>();
            AddText(panel, "Label", label, 18, TextAnchor.MiddleCenter, Color.black);
            return button;
        }

        private static void CreateSharedWorldUI(bool includePrompt = true)
        {
            Canvas canvas = CreateCanvas("World_UI");
            if (includePrompt)
            {
                RectTransform promptRect = AddPanel(canvas.transform, "InteractionPrompt", new Vector2(0.35f, 0.02f), new Vector2(0.65f, 0.1f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.65f));
                Text promptText = AddText(promptRect, "PromptText", "E  상호작용", 20, TextAnchor.MiddleCenter, Color.white);
                InteractionPromptUI prompt = promptRect.gameObject.AddComponent<InteractionPromptUI>();
                prompt.root = promptRect.gameObject;
                prompt.promptText = promptText;
            }

            RectTransform toastRect = AddPanel(canvas.transform, "Toast", new Vector2(0.25f, 0.82f), new Vector2(0.75f, 0.94f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.65f));
            Text toastText = AddText(toastRect, "ToastText", "", 18, TextAnchor.MiddleCenter, Color.white);
            ToastUI toast = toastRect.gameObject.AddComponent<ToastUI>();
            toast.root = toastRect.gameObject;
            toast.messageText = toastText;
        }

        private static DialogueRunner CreateDialogueUI()
        {
            Canvas canvas = CreateCanvas("Dialogue_Canvas");
            RectTransform panel = AddPanel(canvas.transform, "DialoguePanel", new Vector2(0.06f, 0.04f), new Vector2(0.94f, 0.28f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.75f));
            Text speaker = AddText(panel, "SpeakerText", "사장", 20, TextAnchor.UpperLeft, Color.white);
            speaker.rectTransform.anchorMin = new Vector2(0.04f, 0.62f);
            speaker.rectTransform.anchorMax = new Vector2(0.28f, 0.92f);
            speaker.rectTransform.offsetMin = Vector2.zero;
            speaker.rectTransform.offsetMax = Vector2.zero;
            Text body = AddText(panel, "BodyText", "대사", 24, TextAnchor.MiddleLeft, Color.white);
            body.rectTransform.anchorMin = new Vector2(0.04f, 0.08f);
            body.rectTransform.anchorMax = new Vector2(0.86f, 0.68f);
            body.rectTransform.offsetMin = Vector2.zero;
            body.rectTransform.offsetMax = Vector2.zero;
            RectTransform buttonRect = AddPanel(panel, "NextButton", new Vector2(0.86f, 0.12f), new Vector2(0.97f, 0.42f), Vector2.zero, Vector2.zero, new Color(0.85f, 0.85f, 0.85f, 0.95f));
            Button nextButton = buttonRect.gameObject.AddComponent<Button>();
            AddText(buttonRect, "Label", "다음", 18, TextAnchor.MiddleCenter, Color.black);

            DialogueUI ui = panel.gameObject.AddComponent<DialogueUI>();
            ui.root = panel.gameObject;
            ui.speakerText = speaker;
            ui.bodyText = body;
            ui.nextButton = nextButton;

            DialogueRunner runner = canvas.gameObject.AddComponent<DialogueRunner>();
            runner.dialogueUI = ui;
            return runner;
        }

        private static void CreateEquipmentShopUI()
        {
            Canvas canvas = CreateCanvas("EquipmentShop_UI");
            RectTransform panel = AddPanel(canvas.transform, "ShopPanel", new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.46f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.78f));
            Text title = AddText(panel, "Title", "장비 가게", 28, TextAnchor.UpperLeft, Color.white);
            title.rectTransform.anchorMin = new Vector2(0.03f, 0.78f);
            title.rectTransform.anchorMax = new Vector2(0.25f, 0.96f);
            Text request = AddText(panel, "KevinRequest", "케빈:", 20, TextAnchor.UpperLeft, Color.white);
            request.rectTransform.anchorMin = new Vector2(0.03f, 0.55f);
            request.rectTransform.anchorMax = new Vector2(0.48f, 0.78f);
            Text npc = AddText(panel, "ShopkeeperText", "사장:", 20, TextAnchor.UpperLeft, Color.white);
            npc.rectTransform.anchorMin = new Vector2(0.03f, 0.28f);
            npc.rectTransform.anchorMax = new Vector2(0.48f, 0.55f);
            Text itemName = AddText(panel, "ItemName", "장비", 24, TextAnchor.UpperLeft, Color.white);
            itemName.rectTransform.anchorMin = new Vector2(0.53f, 0.72f);
            itemName.rectTransform.anchorMax = new Vector2(0.78f, 0.94f);
            Text itemDesc = AddText(panel, "ItemDesc", "설명", 18, TextAnchor.UpperLeft, Color.white);
            itemDesc.rectTransform.anchorMin = new Vector2(0.53f, 0.25f);
            itemDesc.rectTransform.anchorMax = new Vector2(0.95f, 0.72f);
            Text price = AddText(panel, "Price", "가격", 17, TextAnchor.UpperLeft, Color.white);
            price.rectTransform.anchorMin = new Vector2(0.53f, 0.08f);
            price.rectTransform.anchorMax = new Vector2(0.82f, 0.24f);
            Text owned = AddText(panel, "Owned", "", 17, TextAnchor.UpperRight, Color.white);
            owned.rectTransform.anchorMin = new Vector2(0.82f, 0.78f);
            owned.rectTransform.anchorMax = new Vector2(0.95f, 0.94f);
            RectTransform buyRect = AddPanel(panel, "BuyButton", new Vector2(0.82f, 0.06f), new Vector2(0.94f, 0.22f), Vector2.zero, Vector2.zero, new Color(0.85f, 0.85f, 0.85f, 0.95f));
            Button buy = buyRect.gameObject.AddComponent<Button>();
            AddText(buyRect, "Label", "교환하기", 18, TextAnchor.MiddleCenter, Color.black);
            RectTransform leaveRect = AddPanel(panel, "LeaveButton", new Vector2(0.86f, 0.83f), new Vector2(0.96f, 0.96f), Vector2.zero, Vector2.zero, new Color(0.85f, 0.85f, 0.85f, 0.95f));
            Button leave = leaveRect.gameObject.AddComponent<Button>();
            AddText(leaveRect, "Label", "나가기", 18, TextAnchor.MiddleCenter, Color.black);

            EquipmentShopController controller = canvas.gameObject.AddComponent<EquipmentShopController>();
            controller.titleText = title;
            controller.kevinRequestText = request;
            controller.shopkeeperText = npc;
            controller.itemNameText = itemName;
            controller.itemDescriptionText = itemDesc;
            controller.priceText = price;
            controller.ownedText = owned;
            controller.buyButton = buy;
            controller.leaveButton = leave;
        }

        private static ClubController CreateClubUI()
        {
            Canvas canvas = CreateCanvas("Club_UI");
            RectTransform panel = AddPanel(canvas.transform, "ClubPanel", new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.26f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.75f));
            Text title = AddText(panel, "Title", "클럽", 28, TextAnchor.UpperLeft, Color.white);
            title.rectTransform.anchorMin = new Vector2(0.03f, 0.62f);
            title.rectTransform.anchorMax = new Vector2(0.22f, 0.94f);
            Text status = AddText(panel, "Status", "상태", 19, TextAnchor.MiddleLeft, Color.white);
            status.rectTransform.anchorMin = new Vector2(0.03f, 0.12f);
            status.rectTransform.anchorMax = new Vector2(0.72f, 0.68f);
            RectTransform performRect = AddPanel(panel, "PerformButton", new Vector2(0.74f, 0.18f), new Vector2(0.86f, 0.58f), Vector2.zero, Vector2.zero, new Color(0.85f, 0.85f, 0.85f, 0.95f));
            Button perform = performRect.gameObject.AddComponent<Button>();
            AddText(performRect, "Label", "공연", 18, TextAnchor.MiddleCenter, Color.black);
            RectTransform leaveRect = AddPanel(panel, "LeaveButton", new Vector2(0.87f, 0.18f), new Vector2(0.97f, 0.58f), Vector2.zero, Vector2.zero, new Color(0.85f, 0.85f, 0.85f, 0.95f));
            Button leave = leaveRect.gameObject.AddComponent<Button>();
            AddText(leaveRect, "Label", "나가기", 18, TextAnchor.MiddleCenter, Color.black);

            ClubController controller = canvas.gameObject.AddComponent<ClubController>();
            controller.titleText = title;
            controller.statusText = status;
            controller.performButton = perform;
            controller.leaveButton = leave;
            return controller;
        }

        private static void AddDebugHotkeys()
        {
            var debug = new GameObject("PrototypeDebugHotkeys");
            debug.AddComponent<PrototypeDebugHotkeys>();
        }

        private static void CreateDefaultDataAssets()
        {
            List<StageDefinition> stageAssets = new List<StageDefinition>();
            foreach (StageRuntimeData runtime in ContentDefaults.CreateRewardStagesIncludingStageOneStub())
            {
                string path = DataPath + "/Stage_" + runtime.order.ToString("00") + "_" + runtime.fishType + ".asset";
                StageDefinition asset = AssetDatabase.LoadAssetAtPath<StageDefinition>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<StageDefinition>();
                    AssetDatabase.CreateAsset(asset, path);
                }

                asset.stageId = runtime.stageId;
                asset.displayName = runtime.displayName;
                asset.order = runtime.order;
                asset.fishType = runtime.fishType;
                asset.hiphopGenre = runtime.hiphopGenre;
                asset.bpm = runtime.bpm;
                asset.requiredPreviousStageId = runtime.requiredPreviousStageId;
                asset.nextStageId = runtime.nextStageId;
                asset.rewardEquipment = runtime.rewardEquipment;
                asset.rewardPlates = runtime.rewardPlates;
                asset.requiredPlatesForExchange = runtime.requiredPlatesForExchange;
                asset.inspirationPopup = runtime.inspirationPopup;
                asset.kevinShopRequest = runtime.kevinShopRequest;
                asset.shopkeeperRecommendation = runtime.shopkeeperRecommendation;
                asset.implementedInPrototype = runtime.order != 1;
                asset.distractionCues = new List<DistractionCue>(runtime.distractionCues);
                EditorUtility.SetDirty(asset);
                stageAssets.Add(asset);
            }

            string catalogPath = DataPath + "/StageCatalog.asset";
            StageCatalog catalog = AssetDatabase.LoadAssetAtPath<StageCatalog>(catalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<StageCatalog>();
                AssetDatabase.CreateAsset(catalog, catalogPath);
            }

            catalog.stages = stageAssets;
            EditorUtility.SetDirty(catalog);

            List<EquipmentDefinition> equipmentAssets = new List<EquipmentDefinition>();
            foreach (EquipmentRuntimeData runtime in ContentDefaults.CreateEquipment())
            {
                string path = DataPath + "/Equipment_" + runtime.equipmentId + ".asset";
                EquipmentDefinition asset = AssetDatabase.LoadAssetAtPath<EquipmentDefinition>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<EquipmentDefinition>();
                    AssetDatabase.CreateAsset(asset, path);
                }

                asset.equipmentId = runtime.equipmentId;
                asset.displayName = runtime.displayName;
                asset.role = runtime.role;
                asset.experienceChange = runtime.experienceChange;
                asset.visualFeedback = runtime.visualFeedback;
                EditorUtility.SetDirty(asset);
                equipmentAssets.Add(asset);
            }

            string equipmentCatalogPath = DataPath + "/EquipmentCatalog.asset";
            EquipmentCatalog equipmentCatalog = AssetDatabase.LoadAssetAtPath<EquipmentCatalog>(equipmentCatalogPath);
            if (equipmentCatalog == null)
            {
                equipmentCatalog = ScriptableObject.CreateInstance<EquipmentCatalog>();
                AssetDatabase.CreateAsset(equipmentCatalog, equipmentCatalogPath);
            }

            equipmentCatalog.equipment = equipmentAssets;
            EditorUtility.SetDirty(equipmentCatalog);
        }

        private static void AddScenesToBuildSettings()
        {
            string[] names =
            {
                SashimiBoyConstants.Scenes.Bootstrap,
                SashimiBoyConstants.Scenes.Street,
                SashimiBoyConstants.Scenes.FishShopDialogue,
                SashimiBoyConstants.Scenes.EquipmentShop,
                SashimiBoyConstants.Scenes.Club,
                SashimiBoyConstants.Scenes.Stage01Salmon
            };

            var scenes = new List<EditorBuildSettingsScene>();
            foreach (string name in names)
            {
                string path = ScenesPath + "/" + name + ".unity";
                if (File.Exists(path))
                {
                    scenes.Add(new EditorBuildSettingsScene(path, true));
                }
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
#endif
