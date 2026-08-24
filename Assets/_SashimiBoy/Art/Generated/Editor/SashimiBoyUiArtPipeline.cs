using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SashimiBoy.EditorTools
{
    public static class SashimiBoyUiArtPipeline
    {
        private const string LogoPath =
            "Assets/_SashimiBoy/Art/Source/Branding/SashimiBoyLogo.png";
        private const string NastyIconPath =
            "Assets/_SashimiBoy/Art/Source/UI/Judgement/icon_nasty.png";
        private const string CleanIconPath =
            "Assets/_SashimiBoy/Art/Source/UI/Judgement/icon_clean.png";
        private const string SlippedIconPath =
            "Assets/_SashimiBoy/Art/Source/UI/Judgement/icon_slipped.png";
        private const string WhackIconPath =
            "Assets/_SashimiBoy/Art/Source/UI/Judgement/icon_whack.png";

        private const string GeneratedRoot =
            "Assets/_SashimiBoy/Art/Generated";
        private const string PrefabRoot =
            GeneratedRoot + "/Prefabs/UI";
        private const string DataRoot =
            GeneratedRoot + "/Data";
        private const string SceneRoot =
            GeneratedRoot + "/Scenes";
        private const string ReportRoot =
            GeneratedRoot + "/Reports";
        private const string LogoPrefabPath =
            PrefabRoot + "/PF_UI_SashimiBoyLogo.prefab";
        private const string FeedbackPrefabPath =
            PrefabRoot + "/PF_UI_JudgementFeedback.prefab";
        private const string LibraryPath =
            DataRoot + "/JudgementVisualLibrary.asset";
        private const string StageScenePath =
            "Assets/_SashimiBoy/Scenes/Stage01_Salmon.unity";
        private const string TitlePreviewScenePath =
            SceneRoot + "/TitleLogoPreview.unity";
        private const string ReportPath =
            ReportRoot + "/UIAssetIntegrationReport.md";

        [MenuItem("Sashimi Boy/Art/Build UI Assets")]
        public static void BuildUiAssets()
        {
            BuildAll();
        }

        [MenuItem("Sashimi Boy/Art/Open Title Logo Preview")]
        public static void OpenTitleLogoPreview()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    TitlePreviewScenePath) == null)
            {
                BuildAll();
            }

            EditorSceneManager.OpenScene(
                TitlePreviewScenePath,
                OpenSceneMode.Single);
        }

        public static void BuildUiAssetsBatch()
        {
            BuildAll();
        }

        private static void BuildAll()
        {
            EnsureFolders();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            ConfigureSprite(LogoPath, 1024);
            ConfigureSprite(NastyIconPath, 512);
            ConfigureSprite(CleanIconPath, 512);
            ConfigureSprite(SlippedIconPath, 512);
            ConfigureSprite(WhackIconPath, 512);

            JudgementVisualLibrary library = BuildVisualLibrary();
            GameObject logoPrefab = BuildLogoPrefab();
            GameObject feedbackPrefab = BuildFeedbackPrefab(library);

            IntegrateStageScene(library, feedbackPrefab);

            string titleScenePath = FindExistingTitleScene();
            if (string.IsNullOrEmpty(titleScenePath))
            {
                BuildTitlePreviewScene(logoPrefab);
            }
            else
            {
                IntegrateLogoIntoTitleScene(titleScenePath, logoPrefab);
            }

            WriteReport(titleScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                "[Sashimi Boy] UI art build complete. " +
                "Logo, judgement feedback, library, and scene integration are ready.");
        }

        private static void ConfigureSprite(string assetPath, int maximumSize)
        {
            TextureImporter importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    "TextureImporter missing for " + assetPath);
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.isReadable = false;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = maximumSize;
            importer.SaveAndReimport();

            if (AssetDatabase.LoadAssetAtPath<Sprite>(assetPath) == null)
            {
                throw new InvalidOperationException(
                    "Sprite import failed for " + assetPath);
            }
        }

        private static JudgementVisualLibrary BuildVisualLibrary()
        {
            JudgementVisualLibrary library =
                AssetDatabase.LoadAssetAtPath<JudgementVisualLibrary>(
                    LibraryPath);
            if (library == null)
            {
                library =
                    ScriptableObject.CreateInstance<JudgementVisualLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            library.visuals.Clear();
            library.visuals.Add(CreateVisual(
                JudgeGrade.Nasty,
                "NASTY",
                NastyIconPath));
            library.visuals.Add(CreateVisual(
                JudgeGrade.Smooth,
                "CLEAN",
                CleanIconPath));
            library.visuals.Add(CreateVisual(
                JudgeGrade.Slipped,
                "SLIPPED",
                SlippedIconPath));
            library.visuals.Add(CreateVisual(
                JudgeGrade.Whack,
                "WHACK",
                WhackIconPath));

            EditorUtility.SetDirty(library);
            return library;
        }

        private static JudgementVisualDefinition CreateVisual(
            JudgeGrade grade,
            string displayLabel,
            string spritePath)
        {
            return new JudgementVisualDefinition
            {
                grade = grade,
                displayLabel = displayLabel,
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath),
            };
        }

        private static GameObject BuildLogoPrefab()
        {
            GameObject root = new GameObject(
                "PF_UI_SashimiBoyLogo",
                typeof(RectTransform),
                typeof(Image));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(560f, 560f);

            Image image = root.GetComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);
            image.color = Color.white;
            image.preserveAspect = true;
            image.raycastTarget = false;

            GameObject prefab =
                PrefabUtility.SaveAsPrefabAsset(root, LogoPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static GameObject BuildFeedbackPrefab(
            JudgementVisualLibrary library)
        {
            GameObject root = new GameObject(
                "JudgementFeedback",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(JudgementFeedbackView));
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = new Vector2(460f, 380f);
            rootRect.anchoredPosition = new Vector2(0f, 90f);

            CanvasGroup group = root.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            GameObject iconObject = CreateUiObject("Icon", root.transform);
            RectTransform iconRect = iconObject.GetComponent<RectTransform>();
            SetFixedRect(
                iconRect,
                new Vector2(420f, 260f),
                new Vector2(0f, 55f));
            Image icon = iconObject.AddComponent<Image>();
            icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(NastyIconPath);
            icon.color = Color.white;
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            Text fallback = CreateText(
                "FallbackText",
                iconObject.transform,
                52,
                TextAnchor.MiddleCenter,
                Color.white);
            Stretch(fallback.rectTransform, new Vector2(12f, 8f));
            fallback.text = "NASTY";
            fallback.gameObject.SetActive(false);

            Text offset = CreateText(
                "OffsetText",
                root.transform,
                30,
                TextAnchor.MiddleCenter,
                Color.white);
            SetFixedRect(
                offset.rectTransform,
                new Vector2(400f, 44f),
                new Vector2(0f, -105f));
            offset.text = "+23 ms";

            Text direction = CreateText(
                "DirectionText",
                root.transform,
                26,
                TextAnchor.MiddleCenter,
                new Color(0.86f, 0.9f, 0.94f, 1f));
            SetFixedRect(
                direction.rectTransform,
                new Vector2(400f, 40f),
                new Vector2(0f, -150f));
            direction.text = "LATE";

            JudgementFeedbackView view =
                root.GetComponent<JudgementFeedbackView>();
            view.visualLibrary = library;
            view.canvasGroup = group;
            view.icon = icon;
            view.fallbackText = fallback;
            view.offsetText = offset;
            view.directionText = direction;
            view.scaleInDuration = 0.10f;
            view.holdDuration = 0.35f;
            view.fadeOutDuration = 0.15f;
            view.initialScale = 0.72f;

            GameObject prefab =
                PrefabUtility.SaveAsPrefabAsset(root, FeedbackPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static void IntegrateStageScene(
            JudgementVisualLibrary library,
            GameObject feedbackPrefab)
        {
            SceneEditHandle handle = OpenExistingScene(StageScenePath);
            try
            {
                Stage01SalmonTimingScaffold scaffold =
                    FindComponentInScene<Stage01SalmonTimingScaffold>(
                        handle.Scene);
                if (scaffold == null)
                {
                    throw new InvalidOperationException(
                        "Stage01Salmon_TimingScaffold component is missing.");
                }

                Canvas canvas = FindNamedComponentInScene<Canvas>(
                    handle.Scene,
                    "Stage01Salmon_HUDCanvas");
                if (canvas == null)
                {
                    GameObject canvasObject = new GameObject(
                        "Stage01Salmon_HUDCanvas",
                        typeof(RectTransform),
                        typeof(Canvas),
                        typeof(CanvasScaler),
                        typeof(GraphicRaycaster));
                    SceneManager.MoveGameObjectToScene(
                        canvasObject,
                        handle.Scene);
                    canvas = canvasObject.GetComponent<Canvas>();
                }

                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 2200;
                CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler == null)
                {
                    scaler = canvas.gameObject.AddComponent<CanvasScaler>();
                }

                scaler.uiScaleMode =
                    CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode =
                    CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
                if (canvas.GetComponent<GraphicRaycaster>() == null)
                {
                    canvas.gameObject.AddComponent<GraphicRaycaster>();
                }

                JudgementFeedbackView view =
                    canvas.GetComponentInChildren<JudgementFeedbackView>(true);
                if (view == null)
                {
                    GameObject instance =
                        PrefabUtility.InstantiatePrefab(
                            feedbackPrefab,
                            handle.Scene) as GameObject;
                    instance.name = "JudgementFeedback";
                    instance.transform.SetParent(canvas.transform, false);
                    view = instance.GetComponent<JudgementFeedbackView>();
                }

                RectTransform feedbackRect =
                    view.GetComponent<RectTransform>();
                Stage01SalmonPresentationController presentation =
                    FindComponentInScene<
                        Stage01SalmonPresentationController>(handle.Scene);
                bool hasVisualPrototype = presentation != null;
                feedbackRect.anchorMin = new Vector2(0.5f, 0.5f);
                feedbackRect.anchorMax = new Vector2(0.5f, 0.5f);
                feedbackRect.pivot = new Vector2(0.5f, 0.5f);
                feedbackRect.sizeDelta = hasVisualPrototype
                    ? new Vector2(420f, 300f)
                    : new Vector2(460f, 380f);
                feedbackRect.anchoredPosition = hasVisualPrototype
                    ? new Vector2(0f, 250f)
                    : new Vector2(0f, 90f);
                feedbackRect.localScale = Vector3.one *
                    (hasVisualPrototype ? 0.62f : 1f);

                view.visualLibrary = library;
                view.HideImmediate();
                scaffold.judgementVisualLibrary = library;
                scaffold.judgementFeedback = view;
                if (presentation != null)
                {
                    presentation.judgementFeedback = view;
                    EditorUtility.SetDirty(presentation);
                }

                EditorUtility.SetDirty(view);
                EditorUtility.SetDirty(scaffold);
                EditorSceneManager.MarkSceneDirty(handle.Scene);
                EditorSceneManager.SaveScene(
                    handle.Scene,
                    StageScenePath);
            }
            finally
            {
                CloseScene(handle);
            }
        }

        private static void BuildTitlePreviewScene(GameObject logoPrefab)
        {
            SceneEditHandle handle =
                CreateOrReplaceGeneratedScene(TitlePreviewScenePath);
            try
            {
                GameObject cameraObject = new GameObject("Main Camera");
                SceneManager.MoveGameObjectToScene(
                    cameraObject,
                    handle.Scene);
                cameraObject.tag = "MainCamera";
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor =
                    new Color(0.055f, 0.06f, 0.065f, 1f);
                cameraObject.AddComponent<AudioListener>();

                Canvas canvas = CreateOverlayCanvas(
                    handle.Scene,
                    "TitleLogoPreview_Canvas",
                    100);
                Image background = CreateUiObject(
                    "Background",
                    canvas.transform).AddComponent<Image>();
                Stretch(background.rectTransform, Vector2.zero);
                background.color =
                    new Color(0.055f, 0.06f, 0.065f, 1f);
                background.raycastTarget = false;

                GameObject logo =
                    PrefabUtility.InstantiatePrefab(
                        logoPrefab,
                        handle.Scene) as GameObject;
                logo.name = "PF_UI_SashimiBoyLogo";
                logo.transform.SetParent(canvas.transform, false);
                RectTransform logoRect = logo.GetComponent<RectTransform>();
                SetFixedRect(
                    logoRect,
                    new Vector2(560f, 560f),
                    new Vector2(0f, 35f));

                EditorSceneManager.MarkSceneDirty(handle.Scene);
                EditorSceneManager.SaveScene(
                    handle.Scene,
                    TitlePreviewScenePath);
            }
            finally
            {
                CloseScene(handle);
            }
        }

        private static void IntegrateLogoIntoTitleScene(
            string titleScenePath,
            GameObject logoPrefab)
        {
            SceneEditHandle handle = OpenExistingScene(titleScenePath);
            try
            {
                Canvas canvas = FindComponentInScene<Canvas>(handle.Scene);
                if (canvas == null)
                {
                    canvas = CreateOverlayCanvas(
                        handle.Scene,
                        "SashimiBoyTitleLogoCanvas",
                        100);
                }

                Transform existing =
                    FindChildRecursive(
                        canvas.transform,
                        "PF_UI_SashimiBoyLogo");
                GameObject logo = existing != null
                    ? existing.gameObject
                    : PrefabUtility.InstantiatePrefab(
                        logoPrefab,
                        handle.Scene) as GameObject;
                logo.name = "PF_UI_SashimiBoyLogo";
                logo.transform.SetParent(canvas.transform, false);
                SetFixedRect(
                    logo.GetComponent<RectTransform>(),
                    new Vector2(560f, 560f),
                    new Vector2(0f, 35f));

                EditorSceneManager.MarkSceneDirty(handle.Scene);
                EditorSceneManager.SaveScene(
                    handle.Scene,
                    titleScenePath);
            }
            finally
            {
                CloseScene(handle);
            }
        }

        private static Canvas CreateOverlayCanvas(
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

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        private static string FindExistingTitleScene()
        {
            string[] sceneGuids =
                AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
            for (int i = 0; i < sceneGuids.Length; i++)
            {
                string path =
                    AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
                if (string.Equals(
                        Path.GetFileNameWithoutExtension(path),
                        "Title",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }

            return string.Empty;
        }

        private static SceneEditHandle OpenExistingScene(string scenePath)
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            bool openedSingle = false;

            if (!wasLoaded)
            {
                bool hasUntitledScene =
                    previous.IsValid() && string.IsNullOrEmpty(previous.path);
                if (!Application.isBatchMode &&
                    hasUntitledScene &&
                    previous.isDirty &&
                    !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    throw new OperationCanceledException(
                        "Scene integration was canceled.");
                }

                hasUntitledScene =
                    previous.IsValid() && string.IsNullOrEmpty(previous.path);
                openedSingle = Application.isBatchMode || hasUntitledScene;
                scene = EditorSceneManager.OpenScene(
                    scenePath,
                    openedSingle
                        ? OpenSceneMode.Single
                        : OpenSceneMode.Additive);
            }

            SceneManager.SetActiveScene(scene);
            return new SceneEditHandle(
                scene,
                previous,
                wasLoaded,
                openedSingle);
        }

        private static SceneEditHandle CreateOrReplaceGeneratedScene(
            string scenePath)
        {
            Scene previous = SceneManager.GetActiveScene();
            bool hasUntitledScene =
                previous.IsValid() && string.IsNullOrEmpty(previous.path);
            if (!Application.isBatchMode &&
                hasUntitledScene &&
                previous.isDirty &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                throw new OperationCanceledException(
                    "Scene creation was canceled.");
            }

            hasUntitledScene =
                previous.IsValid() && string.IsNullOrEmpty(previous.path);
            bool openedSingle = Application.isBatchMode || hasUntitledScene;
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                openedSingle
                    ? NewSceneMode.Single
                    : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            return new SceneEditHandle(
                scene,
                previous,
                false,
                openedSingle);
        }

        private static void CloseScene(SceneEditHandle handle)
        {
            if (!handle.WasLoaded && !handle.OpenedSingle)
            {
                EditorSceneManager.CloseScene(handle.Scene, true);
            }

            if (handle.Previous.IsValid() && handle.Previous.isLoaded)
            {
                SceneManager.SetActiveScene(handle.Previous);
            }
        }

        private static T FindComponentInScene<T>(Scene scene)
            where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                T component = roots[i].GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static T FindNamedComponentInScene<T>(
            Scene scene,
            string objectName)
            where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform match =
                    FindChildRecursive(roots[i].transform, objectName);
                if (match != null)
                {
                    return match.GetComponent<T>();
                }
            }

            return null;
        }

        private static Transform FindChildRecursive(
            Transform root,
            string objectName)
        {
            if (root.name == objectName)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform match =
                    FindChildRecursive(root.GetChild(i), objectName);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static GameObject CreateUiObject(
            string name,
            Transform parent)
        {
            GameObject child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return child;
        }

        private static Text CreateText(
            string name,
            Transform parent,
            int fontSize,
            TextAnchor alignment,
            Color color)
        {
            GameObject textObject = CreateUiObject(name, parent);
            Text text = textObject.AddComponent<Text>();
            text.font =
                Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static void SetFixedRect(
            RectTransform rect,
            Vector2 size,
            Vector2 anchoredPosition)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
        }

        private static void Stretch(
            RectTransform rect,
            Vector2 padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = padding;
            rect.offsetMax = -padding;
        }

        private static void EnsureFolders()
        {
            EnsureFolder(GeneratedRoot);
            EnsureFolder(PrefabRoot);
            EnsureFolder(DataRoot);
            EnsureFolder(SceneRoot);
            EnsureFolder(ReportRoot);
        }

        private static void EnsureFolder(string path)
        {
            string[] segments = path.Split('/');
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

        private static void WriteReport(string titleScenePath)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("# Sashimi Boy UI Asset Integration");
            report.AppendLine();
            report.AppendLine("- Logo prefab: `" + LogoPrefabPath + "`");
            report.AppendLine(
                "- Judgement prefab: `" + FeedbackPrefabPath + "`");
            report.AppendLine("- Visual library: `" + LibraryPath + "`");
            report.AppendLine("- Stage scene: `" + StageScenePath + "`");
            report.AppendLine(
                "- Logo scene: `" +
                (string.IsNullOrEmpty(titleScenePath)
                    ? TitlePreviewScenePath
                    : titleScenePath) +
                "`");
            report.AppendLine();
            report.AppendLine("## Mapping");
            report.AppendLine();
            report.AppendLine("- Nasty -> `icon_nasty.png` -> `NASTY`");
            report.AppendLine("- Smooth -> `icon_clean.png` -> `CLEAN`");
            report.AppendLine("- Slipped -> `icon_slipped.png` -> `SLIPPED`");
            report.AppendLine("- Whack -> `icon_whack.png` -> `WHACK`");
            report.AppendLine();
            report.AppendLine(
                "Gameplay enum values, timing windows, score values, " +
                "and audio clock settings were not changed.");

            string projectRoot =
                Directory.GetParent(Application.dataPath).FullName;
            string absolutePath = Path.Combine(projectRoot, ReportPath);
            File.WriteAllText(
                absolutePath,
                report.ToString(),
                new UTF8Encoding(false));
            AssetDatabase.ImportAsset(
                ReportPath,
                ImportAssetOptions.ForceSynchronousImport);
        }

        private sealed class SceneEditHandle
        {
            public SceneEditHandle(
                Scene scene,
                Scene previous,
                bool wasLoaded,
                bool openedSingle)
            {
                Scene = scene;
                Previous = previous;
                WasLoaded = wasLoaded;
                OpenedSingle = openedSingle;
            }

            public Scene Scene { get; private set; }
            public Scene Previous { get; private set; }
            public bool WasLoaded { get; private set; }
            public bool OpenedSingle { get; private set; }
        }
    }
}
