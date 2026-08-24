using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace SashimiBoy.EditorTools
{
    public static class ClubArtPassPipeline
    {
        private const string SourceScenePath =
            "Assets/_SashimiBoy/Scenes/Club.unity";
        private const string OutputScenePath =
            "Assets/_SashimiBoy/Art/Generated/Scenes/Club_ArtPass.unity";
        private const string CatalogPath =
            "Assets/_SashimiBoy/Art/Generated/Data/ClubAssetCatalog.asset";
        private const string ClubPrefabRoot =
            "Assets/_SashimiBoy/Art/Generated/Prefabs/Club";
        private const string ClusterRoot =
            ClubPrefabRoot + "/Clusters";
        private const string ReportPath =
            "Assets/_SashimiBoy/Art/Generated/Reports/ClubArtPassReport.md";

        private const string BeerPairCluster =
            ClusterRoot + "/PF_Club_PropCluster_BeerPair.prefab";
        private const string BucketBeerCluster =
            ClusterRoot + "/PF_Club_PropCluster_BucketBeer.prefab";
        private const string CocktailIceCluster =
            ClusterRoot + "/PF_Club_PropCluster_CocktailIce.prefab";
        private const string MixedDrinksCluster =
            ClusterRoot + "/PF_Club_PropCluster_MixedDrinks.prefab";

        private static readonly Vector3[] TablePositions =
        {
            new Vector3(-5.2f, 0f, -2.5f),
            new Vector3(-4.9f, 0f, -0.2f),
            new Vector3(-3.0f, 0f, 0.7f),
            new Vector3(3.1f, 0f, 0.4f),
            new Vector3(4.9f, 0f, -2.2f),
            new Vector3(4.8f, 0f, 1.2f),
        };

        private static readonly float[] TableRotations =
        {
            -12f,
            9f,
            -7f,
            11f,
            -10f,
            8f,
        };

        [MenuItem("Sashimi Boy/Art/Build Club Art Pass")]
        public static void BuildClubArtPass()
        {
            BuildAll();
        }

        [MenuItem("Sashimi Boy/Art/Open Club Art Pass")]
        public static void OpenClubArtPass()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    OutputScenePath) == null)
            {
                BuildAll();
            }

            EditorSceneManager.OpenScene(
                OutputScenePath,
                OpenSceneMode.Single);
        }

        public static void BuildClubArtPassBatch()
        {
            BuildAll();
        }

        [MenuItem("Sashimi Boy/Art/Apply Club Art To Main Scene")]
        public static void ApplyClubArtToMainScene()
        {
            ApplyClubArtToMainSceneInternal(true);
        }

        public static void ApplyClubArtToMainSceneBatch()
        {
            ApplyClubArtToMainSceneInternal(false);
        }

        private static void ApplyClubArtToMainSceneInternal(bool showDialog)
        {
            EnsureFolder(ClusterRoot);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            ClubAssetCatalog catalog =
                AssetDatabase.LoadAssetAtPath<ClubAssetCatalog>(CatalogPath);
            if (catalog == null)
            {
                ClubArtPipeline.BuildClubMaterialsAndPrefabsBatch();
                catalog = AssetDatabase.LoadAssetAtPath<ClubAssetCatalog>(
                    CatalogPath);
            }

            if (catalog == null)
            {
                throw new InvalidOperationException(
                    "ClubAssetCatalog is missing at " + CatalogPath);
            }

            Dictionary<string, GameObject> clusters =
                BuildPropClusters(catalog);
            Scene scene = EditorSceneManager.OpenScene(
                SourceScenePath,
                OpenSceneMode.Single);
            GameObject existingArt = FindRootObject(scene, "ClubArtRoot");
            if (existingArt != null)
            {
                UnityEngine.Object.DestroyImmediate(existingArt);
            }

            ExistingSceneSnapshot snapshot =
                ExistingSceneSnapshot.Capture(scene);
            PlacementStats stats = BuildArtPassScene(
                scene,
                catalog,
                clusters);
            ValidateArtPass(scene, snapshot, stats);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, SourceScenePath))
            {
                throw new InvalidOperationException(
                    "Could not save " + SourceScenePath);
            }

            AssetDatabase.SaveAssets();
            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "Sashimi Boy",
                    "Generated Club art was applied to the main Club scene.",
                    "OK");
            }
        }

        private static void BuildAll()
        {
            EnsureFolder(ClusterRoot);
            EnsureFolder(Path.GetDirectoryName(ReportPath).Replace('\\', '/'));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            ClubAssetCatalog catalog =
                AssetDatabase.LoadAssetAtPath<ClubAssetCatalog>(CatalogPath);
            if (catalog == null)
            {
                throw new InvalidOperationException(
                    "ClubAssetCatalog is missing at " + CatalogPath);
            }

            Dictionary<string, GameObject> clusters =
                BuildPropClusters(catalog);
            string sourceHashBefore = ComputeSha256(SourceScenePath);

            Scene sourceScene = EditorSceneManager.OpenScene(
                SourceScenePath,
                OpenSceneMode.Single);
            ExistingSceneSnapshot snapshot =
                ExistingSceneSnapshot.Capture(sourceScene);
            if (!EditorSceneManager.SaveScene(
                    sourceScene,
                    OutputScenePath,
                    true))
            {
                throw new InvalidOperationException(
                    "Could not copy Club scene to " + OutputScenePath);
            }

            AssetDatabase.ImportAsset(
                OutputScenePath,
                ImportAssetOptions.ForceSynchronousImport);
            Scene artScene = EditorSceneManager.OpenScene(
                OutputScenePath,
                OpenSceneMode.Single);

            PlacementStats stats =
                BuildArtPassScene(artScene, catalog, clusters);
            ValidationResult validation =
                ValidateArtPass(artScene, snapshot, stats);

            EditorSceneManager.MarkSceneDirty(artScene);
            if (!EditorSceneManager.SaveScene(
                    artScene,
                    OutputScenePath))
            {
                throw new InvalidOperationException(
                    "Could not save " + OutputScenePath);
            }

            WriteReport(catalog, stats, validation);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            string sourceHashAfter = ComputeSha256(SourceScenePath);
            if (!string.Equals(
                    sourceHashBefore,
                    sourceHashAfter,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Source Club.unity changed while building the art pass.");
            }

            Debug.Log(
                "[Sashimi Boy] Club art pass build complete: " +
                OutputScenePath);
        }

        private static Dictionary<string, GameObject> BuildPropClusters(
            ClubAssetCatalog catalog)
        {
            var clusters = new Dictionary<string, GameObject>();

            clusters.Add(
                BeerPairCluster,
                BuildCluster(
                    catalog,
                    BeerPairCluster,
                    "PF_Club_PropCluster_BeerPair",
                    root =>
                    {
                        InstantiateCatalogPrefab(
                            catalog,
                            "Beer",
                            root,
                            "Beer_Left",
                            new Vector3(-0.07f, 0f, 0f),
                            new Vector3(0f, -8f, 0f));
                        InstantiateCatalogPrefab(
                            catalog,
                            "Beer",
                            root,
                            "Beer_Right",
                            new Vector3(0.07f, 0f, 0.02f),
                            new Vector3(0f, 9f, 0f));
                    }));

            clusters.Add(
                BucketBeerCluster,
                BuildCluster(
                    catalog,
                    BucketBeerCluster,
                    "PF_Club_PropCluster_BucketBeer",
                    root =>
                    {
                        InstantiateCatalogPrefab(
                            catalog,
                            "Bucket",
                            root,
                            "Bucket",
                            Vector3.zero,
                            Vector3.zero);
                        InstantiateCatalogPrefab(
                            catalog,
                            "Beer",
                            root,
                            "Beer_Left",
                            new Vector3(-0.09f, 0.2f, 0f),
                            new Vector3(0f, 0f, -7f));
                        InstantiateCatalogPrefab(
                            catalog,
                            "Beer",
                            root,
                            "Beer_Right",
                            new Vector3(0.09f, 0.2f, 0f),
                            new Vector3(0f, 0f, 7f));
                    }));

            clusters.Add(
                CocktailIceCluster,
                BuildCluster(
                    catalog,
                    CocktailIceCluster,
                    "PF_Club_PropCluster_CocktailIce",
                    root =>
                    {
                        InstantiateCatalogPrefab(
                            catalog,
                            "LongIslandIcedTea",
                            root,
                            "Cocktail",
                            new Vector3(-0.11f, 0f, 0f),
                            new Vector3(0f, -6f, 0f));
                        InstantiateCatalogPrefab(
                            catalog,
                            "Ice",
                            root,
                            "Ice",
                            new Vector3(0.11f, 0f, 0.02f),
                            new Vector3(0f, 12f, 0f));
                    }));

            clusters.Add(
                MixedDrinksCluster,
                BuildCluster(
                    catalog,
                    MixedDrinksCluster,
                    "PF_Club_PropCluster_MixedDrinks",
                    root =>
                    {
                        InstantiateCatalogPrefab(
                            catalog,
                            "Beer",
                            root,
                            "Beer",
                            new Vector3(-0.12f, 0f, 0f),
                            new Vector3(0f, -9f, 0f));
                        InstantiateCatalogPrefab(
                            catalog,
                            "LongIslandIcedTea",
                            root,
                            "Cocktail",
                            new Vector3(0.11f, 0f, 0.02f),
                            new Vector3(0f, 8f, 0f));
                    }));

            return clusters;
        }

        private static GameObject BuildCluster(
            ClubAssetCatalog catalog,
            string prefabPath,
            string rootName,
            Action<Transform> populate)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject root = new GameObject(rootName);
                SceneManager.MoveGameObjectToScene(root, preview);
                root.transform.SetPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                root.transform.localScale = Vector3.one;
                populate(root.transform);

                GameObject saved =
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (saved == null)
                {
                    throw new InvalidOperationException(
                        "Could not save cluster prefab " + prefabPath);
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        }

        private static PlacementStats BuildArtPassScene(
            Scene scene,
            ClubAssetCatalog catalog,
            Dictionary<string, GameObject> clusters)
        {
            GameObject oldRoot = FindRootObject(scene, "ClubArtRoot");
            if (oldRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(oldRoot);
            }

            GameObject artRoot = CreateSceneObject(
                scene,
                "ClubArtRoot",
                null);
            Transform architecture = CreateChild(
                artRoot.transform,
                "Architecture");
            Transform entrance = CreateChild(architecture, "Entrance");
            Transform djArea = CreateChild(architecture, "DJArea");
            CreateChild(architecture, "AudienceArea");
            CreateChild(architecture, "BarArea");

            Transform props = CreateChild(artRoot.transform, "Props");
            Transform tables = CreateChild(props, "Tables");
            Transform drinks = CreateChild(props, "Drinks");
            CreateChild(props, "Decorations");

            Transform lighting = CreateChild(
                artRoot.transform,
                "Lighting");
            Transform gameplayAnchors = CreateChild(
                artRoot.transform,
                "ExistingGameplayAnchors");

            var stats = new PlacementStats();

            GameObject doorFrame = InstantiateCatalogPrefab(
                catalog,
                "ClubDoorFrame",
                entrance,
                "DoorFrame_PF_Club_ClubDoorFrame",
                new Vector3(6.4f, 0f, -4.5f),
                Vector3.zero);
            DisableColliders(doorFrame);
            SetStaticRecursive(doorFrame);
            stats.Add("PF_Club_ClubDoorFrame");

            GameObject signBoard = InstantiateCatalogPrefab(
                catalog,
                "SignBoard",
                entrance,
                "SignBoard_PF_Club_SignBoard",
                new Vector3(4.45f, 0f, -4.45f),
                new Vector3(0f, 185f, 0f));
            SetStaticRecursive(signBoard);
            stats.Add("PF_Club_SignBoard");

            GameObject djStand = InstantiateCatalogPrefab(
                catalog,
                "DJStand",
                djArea,
                "DJStand_PF_Club_DJStand",
                new Vector3(0f, 0.8f, 3.65f),
                Vector3.zero);
            SetStaticRecursive(djStand);
            stats.Add("PF_Club_DJStand");

            float equipmentY = GetRendererBounds(djStand).max.y + 0.002f;
            InstantiateCatalogPrefab(
                catalog,
                "DJController",
                djArea,
                "DJController_PF_Club_DJController",
                new Vector3(0f, equipmentY, 4.32f),
                Vector3.zero);
            stats.Add("PF_Club_DJController");

            InstantiateCatalogPrefab(
                catalog,
                "DJMixer",
                djArea,
                "DJMixer_PF_Club_DJMixer",
                new Vector3(0f, equipmentY, 3.45f),
                Vector3.zero);
            stats.Add("PF_Club_DJMixer");

            InstantiateCatalogPrefab(
                catalog,
                "Turntable",
                djArea,
                "Turntable_Left_PF_Club_Turntable",
                new Vector3(-0.92f, equipmentY, 3.45f),
                new Vector3(0f, -5f, 0f));
            InstantiateCatalogPrefab(
                catalog,
                "Turntable",
                djArea,
                "Turntable_Right_PF_Club_Turntable",
                new Vector3(0.92f, equipmentY, 3.45f),
                new Vector3(0f, 5f, 0f));
            stats.Add("PF_Club_Turntable", 2);

            GameObject neonSign = InstantiateCatalogPrefab(
                catalog,
                "NeonSign",
                djArea,
                "NeonSign_PF_Club_NeonSign",
                new Vector3(0f, 1.8f, 4.78f),
                new Vector3(0f, 180f, 0f));
            SetStaticRecursive(neonSign);
            stats.Add("PF_Club_NeonSign");

            for (int i = 0; i < TablePositions.Length; i++)
            {
                InstantiateCatalogPrefab(
                    catalog,
                    "BarTable",
                    tables,
                    $"Table_{i + 1:00}_PF_Club_BarTable",
                    TablePositions[i],
                    new Vector3(0f, TableRotations[i], 0f));
            }

            stats.Add("PF_Club_BarTable", TablePositions.Length);

            string[] clusterPaths =
            {
                BeerPairCluster,
                BucketBeerCluster,
                CocktailIceCluster,
                MixedDrinksCluster,
            };
            string[] clusterNames =
            {
                "BeerPair",
                "BucketBeer",
                "CocktailIce",
                "MixedDrinks",
            };

            for (int i = 0; i < clusterPaths.Length; i++)
            {
                PlacePrefab(
                    clusters[clusterPaths[i]],
                    drinks,
                    $"Audience_{clusterNames[i]}",
                    TablePositions[i] + Vector3.up * 1.052f,
                    new Vector3(0f, TableRotations[i], 0f));
                stats.Add(Path.GetFileNameWithoutExtension(
                    clusterPaths[i]));
            }

            Vector3[] barClusterPositions =
            {
                new Vector3(6.55f, 1.602f, -0.05f),
                new Vector3(6.55f, 1.602f, 0.95f),
                new Vector3(6.55f, 1.602f, 1.95f),
                new Vector3(6.55f, 1.602f, 2.95f),
            };
            for (int i = 0; i < clusterPaths.Length; i++)
            {
                PlacePrefab(
                    clusters[clusterPaths[i]],
                    drinks,
                    $"Bar_{clusterNames[i]}",
                    barClusterPositions[i],
                    new Vector3(0f, i % 2 == 0 ? -90f : 90f, 0f));
                stats.Add(Path.GetFileNameWithoutExtension(
                    clusterPaths[i]));
            }

            CreateSpotLight(
                lighting,
                "DJ_Key_Spot",
                new Vector3(0f, 5.8f, -0.5f),
                new Vector3(0f, 1.6f, 3.65f),
                new Color(1f, 0.035f, 0.07f),
                3.5f,
                12f,
                43f);
            CreatePointLight(
                lighting,
                "Audience_Red_Fill",
                new Vector3(0f, 3.2f, -1.2f),
                new Color(0.55f, 0.015f, 0.035f),
                1.05f,
                7.5f);
            CreatePointLight(
                lighting,
                "Entrance_Guide",
                new Vector3(5.9f, 2.2f, -3.6f),
                new Color(1f, 0.12f, 0.045f),
                0.75f,
                3.8f);
            stats.NewRealtimeLights = 3;

            CreateExistingAnchor(
                scene,
                gameplayAnchors,
                "PlayerSpawn_Anchor",
                "Kevin_Player");
            CreateExistingAnchor(
                scene,
                gameplayAnchors,
                "PerformanceGate_Anchor",
                "PerformanceGate");
            CreateExistingAnchor(
                scene,
                gameplayAnchors,
                "ReturnDoor_Anchor",
                "Door_To_Street");
            CreateExistingAnchor(
                scene,
                gameplayAnchors,
                "ExistingStage_Anchor",
                "Stage");
            CreateExistingAnchor(
                scene,
                gameplayAnchors,
                "ExistingBar_Anchor",
                "Bar");

            ConfigureArtPassPresentation(scene, stats);
            EnsureSingleMainCameraAudioListener(scene);
            return stats;
        }

        private static ValidationResult ValidateArtPass(
            Scene scene,
            ExistingSceneSnapshot snapshot,
            PlacementStats stats)
        {
            snapshot.ValidateUnchanged(scene);

            GameObject artRoot = FindRootObject(scene, "ClubArtRoot");
            if (artRoot == null)
            {
                throw new InvalidOperationException(
                    "ClubArtRoot was not created.");
            }

            ValidateRequiredHierarchy(artRoot.transform);

            ClubController[] controllers =
                GetSceneComponents<ClubController>(scene);
            ClubPerformanceGate[] gates =
                GetSceneComponents<ClubPerformanceGate>(scene);
            ReturnToStreetDoor[] returnDoors =
                GetSceneComponents<ReturnToStreetDoor>(scene);
            SceneDoor[] sceneDoors =
                GetSceneComponents<SceneDoor>(scene);
            SaveManager[] saveManagers =
                GetSceneComponents<SaveManager>(scene);
            EventSystem[] eventSystems =
                GetSceneComponents<EventSystem>(scene);
            Camera[] cameras =
                GetSceneComponents<Camera>(scene);
            AudioListener[] listeners =
                GetSceneComponents<AudioListener>(scene);
            Canvas[] canvases =
                GetSceneComponents<Canvas>(scene);
            JudgementFeedbackView[] judgementViews =
                GetSceneComponents<JudgementFeedbackView>(scene);

            RequireCount(controllers, snapshot.ClubControllerCount);
            RequireCount(gates, snapshot.PerformanceGateCount);
            RequireCount(returnDoors, snapshot.ReturnDoorCount);
            RequireCount(sceneDoors, snapshot.SceneDoorCount);
            RequireCount(saveManagers, snapshot.SaveManagerCount);
            RequireCount(eventSystems, 1);
            RequireCount(cameras, snapshot.CameraCount);
            RequireCount(listeners, 1);
            RequireCount(judgementViews, 0);

            Camera mainCamera = cameras.SingleOrDefault(
                camera => camera.CompareTag("MainCamera"));
            if (mainCamera == null ||
                mainCamera.name != "Main Camera" ||
                mainCamera.GetComponent<AudioListener>() == null ||
                Mathf.Abs(
                    mainCamera.fieldOfView -
                    stats.CameraFieldOfView) > 0.001f)
            {
                throw new InvalidOperationException(
                    "Main Camera or its AudioListener is invalid.");
            }

            if (!listeners[0].enabled ||
                !listeners[0].gameObject.activeInHierarchy)
            {
                throw new InvalidOperationException(
                    "The Club AudioListener must be active.");
            }

            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i].renderMode == RenderMode.WorldSpace)
                {
                    throw new InvalidOperationException(
                        "Club UI must remain screen-space.");
                }
            }

            ClubController controller = controllers.Single();
            ClubPerformanceGate gate = gates.Single();
            ReturnToStreetDoor returnDoor = returnDoors.Single();
            if (controller.titleText == null ||
                controller.statusText == null ||
                controller.performButton == null ||
                controller.leaveButton == null ||
                controller.audience == null ||
                controller.audience.Length == 0 ||
                controller.audience.Any(item => item == null))
            {
                throw new InvalidOperationException(
                    "ClubController gameplay references are incomplete.");
            }

            if (gate.clubController != controller)
            {
                throw new InvalidOperationException(
                    "ClubPerformanceGate lost its ClubController reference.");
            }

            Collider gateCollider = gate.GetComponent<Collider>();
            Renderer gateRenderer = gate.GetComponent<Renderer>();
            if (!gate.gameObject.activeInHierarchy ||
                gateCollider == null ||
                !gateCollider.enabled ||
                gateRenderer == null ||
                gateRenderer.enabled)
            {
                throw new InvalidOperationException(
                    "PerformanceGate logic/collider or visual override is invalid.");
            }

            if (!string.Equals(
                    returnDoor.sceneName,
                    SashimiBoyConstants.Scenes.Street,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Return door no longer targets Street.");
            }

            ValidateLegacyVisualOverrides(scene);
            ValidatePerformanceConditions(controller);
            ValidateGeneratedPrefabSources(artRoot);
            ValidateMaterials(artRoot);
            ValidateColliders(artRoot);
            ValidateDjEquipment(artRoot);
            ValidateGrounding(artRoot);

            Light[] newLights =
                artRoot.GetComponentsInChildren<Light>(true);
            if (newLights.Length != stats.NewRealtimeLights ||
                newLights.Any(light =>
                    light.shadows != LightShadows.None))
            {
                throw new InvalidOperationException(
                    "Club art-pass lighting count or shadow mode is invalid.");
            }

            return new ValidationResult
            {
                AudioListenerCount = listeners.Length,
                EventSystemCount = eventSystems.Length,
                ClubControllerCount = controllers.Length,
                PerformanceGateCount = gates.Length,
                ReturnDoorCount = returnDoors.Length,
                SceneDoorCount = sceneDoors.Length,
                CanvasCount = canvases.Length,
                MeshColliderCount =
                    artRoot.GetComponentsInChildren<MeshCollider>(true).Length,
                NewLightCount = newLights.Length,
                MissingEquipmentCheckPassed = true,
                ReadyEquipmentCheckPassed = true,
                ReturnDoorCheckPassed = true,
                ExistingTransformsPreserved = true,
            };
        }

        private static void ValidateLegacyVisualOverrides(Scene scene)
        {
            string[] hiddenRenderers =
            {
                "Label_PerformanceGate",
                "Label_ReturnDoor",
                "PerformanceGate",
                "DJ_Booth",
            };
            for (int i = 0; i < hiddenRenderers.Length; i++)
            {
                GameObject target =
                    FindObject(scene, hiddenRenderers[i]);
                if (target == null ||
                    (target.activeInHierarchy &&
                     target.GetComponentsInChildren<Renderer>(true)
                         .Any(renderer => renderer.enabled)))
                {
                    throw new InvalidOperationException(
                        "Legacy visual override is invalid: " +
                        hiddenRenderers[i]);
                }
            }

            GameObject oldDjBooth = FindObject(scene, "DJ_Booth");
            if (oldDjBooth.GetComponentsInChildren<Collider>(true)
                .Any(collider => collider.enabled))
            {
                throw new InvalidOperationException(
                    "Hidden DJ_Booth placeholder collider is still active.");
            }
        }

        private static void ValidatePerformanceConditions(
            ClubController controller)
        {
            SaveData missing = SaveData.CreateNew();
            if (ProgressionQuery.HasAllPerformanceEquipment(missing))
            {
                throw new InvalidOperationException(
                    "A new save must not pass the performance gate.");
            }

            string missingList =
                ProgressionQuery.BuildMissingEquipmentText(missing);
            if (string.IsNullOrWhiteSpace(missingList))
            {
                throw new InvalidOperationException(
                    "Missing equipment text is empty.");
            }

            SaveData ready = SaveData.CreateNew();
            List<EquipmentRuntimeData> equipment =
                ContentDefaults.CreateEquipment();
            for (int i = 0; i < equipment.Count; i++)
            {
                ready.AddEquipment(equipment[i].equipmentId);
            }

            if (!ProgressionQuery.HasAllPerformanceEquipment(ready))
            {
                throw new InvalidOperationException(
                    "A fully equipped save must pass the performance gate.");
            }

            MethodInfo buildStatusText =
                typeof(ClubController).GetMethod(
                    "BuildStatusText",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            if (buildStatusText == null)
            {
                throw new InvalidOperationException(
                    "ClubController status builder is missing.");
            }

            FieldInfo canPerform =
                typeof(ClubController).GetField(
                    "canPerform",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            if (canPerform == null)
            {
                throw new InvalidOperationException(
                    "ClubController canPerform state is missing.");
            }

            canPerform.SetValue(controller, false);
            string missingStatus =
                buildStatusText.Invoke(
                    controller,
                    new object[] { missing }) as string;
            canPerform.SetValue(controller, true);
            string readyStatus =
                buildStatusText.Invoke(
                    controller,
                    new object[] { ready }) as string;
            canPerform.SetValue(controller, false);

            if (string.IsNullOrWhiteSpace(missingStatus) ||
                string.IsNullOrWhiteSpace(readyStatus) ||
                string.Equals(
                    missingStatus,
                    readyStatus,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Performance gate UI states are not distinct.");
            }
        }

        private static void ValidateRequiredHierarchy(Transform root)
        {
            string[] paths =
            {
                "Architecture/Entrance",
                "Architecture/DJArea",
                "Architecture/AudienceArea",
                "Architecture/BarArea",
                "Props/Tables",
                "Props/Drinks",
                "Props/Decorations",
                "Lighting",
                "ExistingGameplayAnchors",
            };
            for (int i = 0; i < paths.Length; i++)
            {
                if (root.Find(paths[i]) == null)
                {
                    throw new InvalidOperationException(
                        "Club art-pass hierarchy is missing " + paths[i]);
                }
            }
        }

        private static void ValidateGeneratedPrefabSources(GameObject artRoot)
        {
            Renderer[] renderers =
                artRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                GameObject outermost =
                    PrefabUtility.GetOutermostPrefabInstanceRoot(
                        renderers[i].gameObject);
                string path = outermost == null
                    ? string.Empty
                    : PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        outermost);
                if (string.IsNullOrWhiteSpace(path) ||
                    !path.StartsWith(
                        ClubPrefabRoot + "/",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Art-pass renderer is not sourced from a generated " +
                        "Club prefab: " + renderers[i].name);
                }
            }
        }

        private static void ValidateMaterials(GameObject artRoot)
        {
            Renderer[] renderers =
                artRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] materials = renderers[i].sharedMaterials;
                for (int j = 0; j < materials.Length; j++)
                {
                    Material material = materials[j];
                    if (material == null || material.shader == null)
                    {
                        throw new InvalidOperationException(
                            "Missing material or shader on " +
                            renderers[i].name);
                    }

                    string path = AssetDatabase.GetAssetPath(material);
                    if (!path.StartsWith(
                            "Assets/_SashimiBoy/Art/Generated/Materials/Club/",
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Unexpected material source on " +
                            renderers[i].name + ": " + path);
                    }

                    if (material.name.EndsWith(
                            " (Instance)",
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Instanced material found on " +
                            renderers[i].name);
                    }
                }
            }
        }

        private static void ValidateColliders(GameObject artRoot)
        {
            Physics.SyncTransforms();

            if (artRoot.GetComponentsInChildren<MeshCollider>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "MeshCollider is not allowed in the Club art pass.");
            }

            Transform doorFrame = FindDescendant(
                artRoot.transform,
                "DoorFrame_PF_Club_ClubDoorFrame");
            if (doorFrame == null ||
                doorFrame.GetComponentsInChildren<Collider>(true)
                    .Any(collider => collider.enabled))
            {
                throw new InvalidOperationException(
                    "Door frame colliders must remain disabled.");
            }

            Bounds[] protectedPaths =
            {
                new Bounds(
                    new Vector3(0f, 1.5f, -1.25f),
                    new Vector3(2.4f, 3f, 5.5f)),
                new Bounds(
                    new Vector3(0f, 1.5f, 1.9f),
                    new Vector3(3f, 3f, 1.4f)),
                new Bounds(
                    new Vector3(6.4f, 1.5f, -4.1f),
                    new Vector3(2.4f, 3f, 3f)),
            };

            Collider[] colliders =
                artRoot.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (!colliders[i].enabled ||
                    !colliders[i].gameObject.activeInHierarchy)
                {
                    continue;
                }

                for (int j = 0; j < protectedPaths.Length; j++)
                {
                    if (colliders[i].bounds.Intersects(protectedPaths[j]))
                    {
                        throw new InvalidOperationException(
                            "New collider blocks a protected route: " +
                            colliders[i].name);
                    }
                }
            }
        }

        private static void ValidateDjEquipment(GameObject artRoot)
        {
            string[] names =
            {
                "DJController_PF_Club_DJController",
                "DJMixer_PF_Club_DJMixer",
                "Turntable_Left_PF_Club_Turntable",
                "Turntable_Right_PF_Club_Turntable",
            };
            var bounds = new Dictionary<string, Bounds>();
            for (int i = 0; i < names.Length; i++)
            {
                Transform item = FindDescendant(
                    artRoot.transform,
                    names[i]);
                if (item == null)
                {
                    throw new InvalidOperationException(
                        "DJ equipment is missing: " + names[i]);
                }

                bounds.Add(names[i], GetRendererBounds(item.gameObject));
            }

            for (int i = 0; i < names.Length; i++)
            {
                for (int j = i + 1; j < names.Length; j++)
                {
                    if (HasMeaningfulIntersection(
                            bounds[names[i]],
                            bounds[names[j]],
                            0.002f))
                    {
                        throw new InvalidOperationException(
                            "DJ equipment bounds overlap: " +
                            names[i] + " / " + names[j]);
                    }
                }
            }

            Transform stand = FindDescendant(
                artRoot.transform,
                "DJStand_PF_Club_DJStand");
            float standTop = GetRendererBounds(stand.gameObject).max.y;
            for (int i = 0; i < names.Length; i++)
            {
                float itemBottom = bounds[names[i]].min.y;
                if (Mathf.Abs(itemBottom - standTop) > 0.02f)
                {
                    throw new InvalidOperationException(
                        "DJ equipment is not resting on the stand: " +
                        names[i]);
                }
            }
        }

        private static void ValidateGrounding(GameObject artRoot)
        {
            Transform entrance = artRoot.transform.Find(
                "Architecture/Entrance");
            Transform tables = artRoot.transform.Find("Props/Tables");
            Renderer[] renderers = entrance
                .GetComponentsInChildren<Renderer>(true)
                .Concat(
                    tables.GetComponentsInChildren<Renderer>(true))
                .ToArray();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].bounds.min.y < -0.025f)
                {
                    throw new InvalidOperationException(
                        "Placed art is below the floor: " +
                        renderers[i].name);
                }
            }
        }

        private static GameObject InstantiateCatalogPrefab(
            ClubAssetCatalog catalog,
            string assetId,
            Transform parent,
            string instanceName,
            Vector3 localPosition,
            Vector3 sceneRotation)
        {
            ClubAssetCatalogEntry entry = catalog.Find(assetId);
            if (entry == null || entry.generatedPrefab == null)
            {
                throw new InvalidOperationException(
                    "Generated Club prefab is missing for " + assetId);
            }

            GameObject instance =
                PrefabUtility.InstantiatePrefab(
                    entry.generatedPrefab,
                    parent.gameObject.scene) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    "Could not instantiate " + assetId);
            }

            instance.name = instanceName;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation =
                Quaternion.Euler(entry.defaultRotation + sceneRotation);
            instance.transform.localScale = Vector3.one;

            Transform model = instance.transform.Find("Model");
            if (model == null ||
                !Approximately(model.localScale, entry.defaultScale))
            {
                throw new InvalidOperationException(
                    "Generated prefab does not match ClubAssetCatalog " +
                    "defaultScale: " + assetId);
            }

            return instance;
        }

        private static GameObject PlacePrefab(
            GameObject prefab,
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localEuler)
        {
            GameObject instance =
                PrefabUtility.InstantiatePrefab(
                    prefab,
                    parent.gameObject.scene) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    "Could not instantiate " + prefab.name);
            }

            instance.name = name;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation =
                Quaternion.Euler(localEuler);
            instance.transform.localScale = Vector3.one;
            return instance;
        }

        private static void EnsureSingleMainCameraAudioListener(Scene scene)
        {
            Camera[] cameras = GetSceneComponents<Camera>(scene);
            Camera mainCamera = cameras.FirstOrDefault(
                camera => camera.CompareTag("MainCamera"));
            if (mainCamera == null)
            {
                throw new InvalidOperationException(
                    "Club Main Camera is missing.");
            }

            AudioListener listener =
                mainCamera.GetComponent<AudioListener>();
            if (listener == null)
            {
                listener =
                    mainCamera.gameObject.AddComponent<AudioListener>();
            }

            listener.enabled = true;
            AudioListener[] allListeners =
                GetSceneComponents<AudioListener>(scene);
            for (int i = 0; i < allListeners.Length; i++)
            {
                if (allListeners[i] != listener)
                {
                    UnityEngine.Object.DestroyImmediate(allListeners[i]);
                }
            }
        }

        private static void ConfigureArtPassPresentation(
            Scene scene,
            PlacementStats stats)
        {
            Camera[] cameras = GetSceneComponents<Camera>(scene);
            Camera mainCamera = cameras.FirstOrDefault(
                camera => camera.CompareTag("MainCamera"));
            if (mainCamera == null)
            {
                throw new InvalidOperationException(
                    "Club Main Camera is missing.");
            }

            KevinFirstPersonCameraRig firstPersonRig =
                GetSceneComponents<KevinFirstPersonCameraRig>(scene)
                    .FirstOrDefault();
            mainCamera.fieldOfView = firstPersonRig != null
                ? firstPersonRig.fieldOfView
                : 70f;
            stats.CameraFieldOfView = mainCamera.fieldOfView;

            DisableExistingRenderer(
                scene,
                "Label_PerformanceGate",
                false);
            DisableExistingRenderer(
                scene,
                "Label_ReturnDoor",
                false);
            DisableExistingRenderer(
                scene,
                "PerformanceGate",
                false);
            DisableExistingRenderer(
                scene,
                "DJ_Booth",
                true);
            stats.HiddenPlaceholderRendererCount = 4;
            stats.DisabledPlaceholderColliderCount = 1;
        }

        private static void DisableExistingRenderer(
            Scene scene,
            string objectName,
            bool disableColliders)
        {
            GameObject target = FindObject(scene, objectName);
            if (target == null)
            {
                throw new InvalidOperationException(
                    "Existing placeholder is missing: " + objectName);
            }

            Renderer[] renderers =
                target.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = false;
            }

            if (!disableColliders)
            {
                return;
            }

            Collider[] colliders =
                target.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }
        }

        private static void CreateExistingAnchor(
            Scene scene,
            Transform parent,
            string anchorName,
            string existingObjectName)
        {
            GameObject existing = FindObject(scene, existingObjectName);
            if (existing == null)
            {
                throw new InvalidOperationException(
                    "Existing gameplay object is missing: " +
                    existingObjectName);
            }

            Transform anchor = CreateChild(parent, anchorName);
            anchor.position = existing.transform.position;
            anchor.rotation = existing.transform.rotation;
        }

        private static void CreateSpotLight(
            Transform parent,
            string name,
            Vector3 position,
            Vector3 target,
            Color color,
            float intensity,
            float range,
            float angle)
        {
            GameObject lightObject = new GameObject(name);
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.localPosition = position;
            lightObject.transform.LookAt(target);

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = angle;
            light.innerSpotAngle = angle * 0.65f;
            light.shadows = LightShadows.None;
            light.lightmapBakeType = LightmapBakeType.Realtime;
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
            light.lightmapBakeType = LightmapBakeType.Realtime;
        }

        private static GameObject CreateSceneObject(
            Scene scene,
            string name,
            Transform parent)
        {
            GameObject gameObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            if (parent != null)
            {
                gameObject.transform.SetParent(parent, false);
            }

            return gameObject;
        }

        private static Transform CreateChild(
            Transform parent,
            string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static void DisableColliders(GameObject root)
        {
            Collider[] colliders =
                root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }
        }

        private static void SetStaticRecursive(GameObject root)
        {
            StaticEditorFlags flags =
                StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.ReflectionProbeStatic;
            Transform[] transforms =
                root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                GameObjectUtility.SetStaticEditorFlags(
                    transforms[i].gameObject,
                    flags);
            }
        }

        private static Bounds GetRendererBounds(GameObject root)
        {
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException(
                    "Renderer is missing under " + root.name);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static bool HasMeaningfulIntersection(
            Bounds left,
            Bounds right,
            float tolerance)
        {
            float x =
                Mathf.Min(left.max.x, right.max.x) -
                Mathf.Max(left.min.x, right.min.x);
            float y =
                Mathf.Min(left.max.y, right.max.y) -
                Mathf.Max(left.min.y, right.min.y);
            float z =
                Mathf.Min(left.max.z, right.max.z) -
                Mathf.Max(left.min.z, right.min.z);
            return x > tolerance &&
                   y > tolerance &&
                   z > tolerance;
        }

        private static bool Approximately(Vector3 left, Vector3 right)
        {
            return Mathf.Abs(left.x - right.x) < 0.0001f &&
                   Mathf.Abs(left.y - right.y) < 0.0001f &&
                   Mathf.Abs(left.z - right.z) < 0.0001f;
        }

        private static T[] GetSceneComponents<T>(Scene scene)
            where T : Component
        {
            var results = new List<T>();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                results.AddRange(
                    roots[i].GetComponentsInChildren<T>(true));
            }

            return results.ToArray();
        }

        private static void RequireCount<T>(
            T[] components,
            int expected)
            where T : Component
        {
            if (components.Length != expected)
            {
                throw new InvalidOperationException(
                    typeof(T).Name + " count is " +
                    components.Length + "; expected " + expected + ".");
            }
        }

        private static GameObject FindRootObject(
            Scene scene,
            string name)
        {
            return scene.GetRootGameObjects().FirstOrDefault(
                root => root.name == name);
        }

        private static GameObject FindObject(
            Scene scene,
            string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform match =
                    FindDescendant(roots[i].transform, name);
                if (match != null)
                {
                    return match.gameObject;
                }
            }

            return null;
        }

        private static Transform FindDescendant(
            Transform root,
            string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found =
                    FindDescendant(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static void EnsureFolder(string path)
        {
            string normalized = path.Replace('\\', '/');
            string[] parts = normalized.Split('/');
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

        private static string ComputeSha256(string assetPath)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(
                Path.GetFullPath(assetPath)))
            {
                byte[] hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "");
            }
        }

        private static void WriteReport(
            ClubAssetCatalog catalog,
            PlacementStats stats,
            ValidationResult validation)
        {
            var report = new StringBuilder();
            report.AppendLine("# Club Art Pass Report");
            report.AppendLine();
            report.AppendLine("- Source: `" + SourceScenePath + "`");
            report.AppendLine("- Output: `" + OutputScenePath + "`");
            report.AppendLine("- Source scene overwritten: No");
            report.AppendLine("- Build Settings changed: No");
            report.AppendLine("- Raw FBX placed directly: No");
            report.AppendLine();
            report.AppendLine("## Scene Prefab Instances");
            report.AppendLine();
            foreach (KeyValuePair<string, int> pair in
                     stats.PrefabCounts.OrderBy(pair => pair.Key))
            {
                report.AppendLine(
                    "- " + pair.Key + ": " + pair.Value);
            }

            report.AppendLine();
            report.AppendLine("## Materials");
            report.AppendLine();
            string[] usedAssetIds =
            {
                "BarTable",
                "Beer",
                "Bucket",
                "ClubDoorFrame",
                "DJController",
                "DJMixer",
                "DJStand",
                "Ice",
                "LongIslandIcedTea",
                "NeonSign",
                "SignBoard",
                "Turntable",
            };
            for (int i = 0; i < usedAssetIds.Length; i++)
            {
                ClubAssetCatalogEntry entry =
                    catalog.Find(usedAssetIds[i]);
                report.AppendLine(
                    "- " + usedAssetIds[i] + ": " +
                    entry.materialStatus +
                    (string.IsNullOrWhiteSpace(entry.notes)
                        ? string.Empty
                        : " (" + entry.notes.Replace("\n", " ") + ")"));
            }

            report.AppendLine();
            report.AppendLine("## Validation");
            report.AppendLine();
            report.AppendLine(
                "- Existing transforms preserved: " +
                YesNo(validation.ExistingTransformsPreserved));
            report.AppendLine(
                "- ClubController: " +
                validation.ClubControllerCount);
            report.AppendLine(
                "- ClubPerformanceGate: " +
                validation.PerformanceGateCount);
            report.AppendLine(
                "- ReturnToStreetDoor: " +
                validation.ReturnDoorCount);
            report.AppendLine(
                "- SceneDoor: " + validation.SceneDoorCount);
            report.AppendLine(
                "- AudioListener: " +
                validation.AudioListenerCount);
            report.AppendLine(
                "- EventSystem: " +
                validation.EventSystemCount);
            report.AppendLine(
                "- Screen-space Canvas: " +
                validation.CanvasCount);
            report.AppendLine(
                "- New realtime lights: " +
                validation.NewLightCount + " (shadows off)");
            report.AppendLine(
                "- Main Camera FOV: " + stats.CameraFieldOfView);
            report.AppendLine(
                "- Hidden legacy placeholder renderers: " +
                stats.HiddenPlaceholderRendererCount);
            report.AppendLine(
                "- Disabled legacy placeholder colliders: " +
                stats.DisabledPlaceholderColliderCount +
                " (DJ_Booth visual placeholder only)");
            report.AppendLine(
                "- MeshCollider: " +
                validation.MeshColliderCount);
            report.AppendLine(
                "- Missing-equipment condition: " +
                YesNo(validation.MissingEquipmentCheckPassed));
            report.AppendLine(
                "- Ready-equipment condition: " +
                YesNo(validation.ReadyEquipmentCheckPassed));
            report.AppendLine(
                "- Return-to-Street target: " +
                YesNo(validation.ReturnDoorCheckPassed));
            report.AppendLine();
            report.AppendLine("Neon emission remains disabled. " +
                              "The catalog does not identify a safe " +
                              "emissive material slot.");

            File.WriteAllText(
                Path.GetFullPath(ReportPath),
                report.ToString());
            AssetDatabase.ImportAsset(
                ReportPath,
                ImportAssetOptions.ForceSynchronousImport);
        }

        private static string YesNo(bool value)
        {
            return value ? "Passed" : "Failed";
        }

        private sealed class PlacementStats
        {
            public readonly Dictionary<string, int> PrefabCounts =
                new Dictionary<string, int>();

            public int NewRealtimeLights;
            public float CameraFieldOfView;
            public int HiddenPlaceholderRendererCount;
            public int DisabledPlaceholderColliderCount;

            public void Add(string prefabName, int count = 1)
            {
                if (!PrefabCounts.ContainsKey(prefabName))
                {
                    PrefabCounts.Add(prefabName, 0);
                }

                PrefabCounts[prefabName] += count;
            }
        }

        private sealed class ValidationResult
        {
            public int AudioListenerCount;
            public int EventSystemCount;
            public int ClubControllerCount;
            public int PerformanceGateCount;
            public int ReturnDoorCount;
            public int SceneDoorCount;
            public int CanvasCount;
            public int MeshColliderCount;
            public int NewLightCount;
            public bool MissingEquipmentCheckPassed;
            public bool ReadyEquipmentCheckPassed;
            public bool ReturnDoorCheckPassed;
            public bool ExistingTransformsPreserved;
        }

        private sealed class ExistingSceneSnapshot
        {
            private readonly Dictionary<string, TransformState> transforms =
                new Dictionary<string, TransformState>();

            public int ClubControllerCount;
            public int PerformanceGateCount;
            public int ReturnDoorCount;
            public int SceneDoorCount;
            public int SaveManagerCount;
            public int CameraCount;

            public static ExistingSceneSnapshot Capture(Scene scene)
            {
                var snapshot = new ExistingSceneSnapshot
                {
                    ClubControllerCount =
                        GetSceneComponents<ClubController>(scene).Length,
                    PerformanceGateCount =
                        GetSceneComponents<ClubPerformanceGate>(scene).Length,
                    ReturnDoorCount =
                        GetSceneComponents<ReturnToStreetDoor>(scene).Length,
                    SceneDoorCount =
                        GetSceneComponents<SceneDoor>(scene).Length,
                    SaveManagerCount =
                        GetSceneComponents<SaveManager>(scene).Length,
                    CameraCount =
                        GetSceneComponents<Camera>(scene).Length,
                };

                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    snapshot.CaptureTransform(
                        roots[i].transform,
                        roots[i].name);
                }

                return snapshot;
            }

            public void ValidateUnchanged(Scene scene)
            {
                foreach (KeyValuePair<string, TransformState> pair
                         in transforms)
                {
                    Transform transform =
                        FindByPath(scene, pair.Key);
                    if (transform == null ||
                        !pair.Value.Matches(transform))
                    {
                        throw new InvalidOperationException(
                            "Existing Club object changed: " + pair.Key);
                    }
                }
            }

            private void CaptureTransform(
                Transform transform,
                string path)
            {
                transforms.Add(
                    path,
                    TransformState.Capture(transform));
                for (int i = 0; i < transform.childCount; i++)
                {
                    Transform child = transform.GetChild(i);
                    CaptureTransform(
                        child,
                        path + "/" + child.name);
                }
            }

            private static Transform FindByPath(
                Scene scene,
                string path)
            {
                string[] parts = path.Split('/');
                GameObject root = scene.GetRootGameObjects()
                    .FirstOrDefault(item => item.name == parts[0]);
                if (root == null)
                {
                    return null;
                }

                Transform current = root.transform;
                for (int i = 1; i < parts.Length; i++)
                {
                    current = current.Find(parts[i]);
                    if (current == null)
                    {
                        return null;
                    }
                }

                return current;
            }
        }

        private struct TransformState
        {
            private Vector3 localPosition;
            private Quaternion localRotation;
            private Vector3 localScale;
            private bool activeSelf;

            public static TransformState Capture(Transform transform)
            {
                return new TransformState
                {
                    localPosition = transform.localPosition,
                    localRotation = transform.localRotation,
                    localScale = transform.localScale,
                    activeSelf = transform.gameObject.activeSelf,
                };
            }

            public bool Matches(Transform transform)
            {
                return Approximately(
                           localPosition,
                           transform.localPosition) &&
                       Quaternion.Angle(
                           localRotation,
                           transform.localRotation) < 0.001f &&
                       Approximately(
                           localScale,
                           transform.localScale) &&
                       activeSelf == transform.gameObject.activeSelf;
            }
        }
    }
}
