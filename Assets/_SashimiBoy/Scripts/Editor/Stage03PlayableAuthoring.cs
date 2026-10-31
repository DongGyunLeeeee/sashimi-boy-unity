using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using Object=UnityEngine.Object;
namespace SashimiBoy.EditorTools
{
    public static class Stage03PlayableAuthoring
    {
        public const string ScenePath="Assets/_SashimiBoy/Scenes/Stage03_Flounder.unity";
        public const string ChartPath="Assets/_SashimiBoy/Data/Generated/Stage03PlayableChart.asset";
        public const string MusicPath="Assets/_SashimiBoy/Audio/Music/Stage_03_Flounder/flounder1.wav";
        public const string ChartSource="Docs/Development/STAGE03_RHYTHM_ANALYSIS.json";
        [Serializable] private sealed class ChartInput {public float bpm;public double firstGridBeatSeconds;public string sourceSha256;public Stage03PhaseChart[] phases;}
        [MenuItem("Sashimi Boy/Stage 03/Apply Complete Playable Stage")]
        public static void ApplyBatch()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play mode first.");
            Stage03BuildInputGuard.ValidateMaterializedAssets();
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save unsaved scenes first.");
            byte[] salmon=File.ReadAllBytes(Stage01PlayableAuthoring.ScenePath),rockfish=File.ReadAllBytes(Stage02RockfishAuthoring.ScenePath);
            Stage03FilletCutAuthoring.BuildBatch();
            Stage03DayWorldAuthoring.ApplyBatch();
            BuildChart();
            if(!File.Exists(ScenePath))
            {
                if(!AssetDatabase.CopyAsset(Stage02RockfishAuthoring.ScenePath,ScenePath))throw new InvalidOperationException("Cannot create Stage3 workstation.");
                AssetDatabase.ImportAsset(ScenePath,ImportAssetOptions.ForceSynchronousImport);
            }
            Scene scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            var controller=Find<Stage03GameController>(scene);
            if(controller==null)controller=BuildScene(scene);
            CleanupLegacyDemo(scene);
            PlaceAssembly(controller,scene);
            ConfigureResultPanel(controller);
            if(controller.startPanel.transform.Find("Abort")==null)
                Button(controller.startPanel.transform,"Abort","식당으로 돌아가기",controller.titleText.font,new Vector2(.5f,.08f),controller.ReturnToShop);
            controller.chart=AssetDatabase.LoadAssetAtPath<Stage03Chart>(ChartPath);
            controller.clock.audioSource.clip=controller.chart.music;
            controller.clock.playOnStart=false;
            for(int i=0;i<4;i++)
            {
                string role=i<2?"FrontFillet":"BackFillet";
                controller.fillets[i].Bind(AssetDatabase.LoadAssetAtPath<Stage03FilletCutCache>(Stage03FilletCutAuthoring.CachePath(role)));
                EditorUtility.SetDirty(controller.fillets[i]);
            }
            EditorUtility.SetDirty(controller);
            var data=AssetDatabase.LoadAssetAtPath<StageDefinition>("Assets/_SashimiBoy/Data/Generated/Stage_03_Sole.asset");
            if(data==null)throw new InvalidOperationException("Existing Stage3 stable-ID definition missing.");
            data.displayName="가자미";data.sceneName=DayWorldRules.StageThreeScene;data.bpm=controller.chart.bpm;data.implementedInPrototype=true;
            EditorUtility.SetDirty(data);
            Scan(scene);
            EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Stage3 scene save failed.");
            var scenes=EditorBuildSettings.scenes.ToList();
            int index=scenes.FindIndex(s=>s.path==ScenePath);
            if(index<0)scenes.Add(new EditorBuildSettingsScene(ScenePath,true));else scenes[index].enabled=true;
            EditorBuildSettings.scenes=scenes.ToArray();AssetDatabase.SaveAssets();
            NormalizeOwnedOutputs();
            if(!File.ReadAllBytes(Stage01PlayableAuthoring.ScenePath).SequenceEqual(salmon)||!File.ReadAllBytes(Stage02RockfishAuthoring.ScenePath).SequenceEqual(rockfish))
                throw new InvalidOperationException("Stage1/2 scenes changed during Stage3 authoring.");
            Debug.Log("[Stage03Playable] Created 9-phase, four-fillet, 24-slice play and Day3 world. Stage1/2 bytes preserved.");
        }
        private static void CleanupLegacyDemo(Scene scene)
        {
            // The cloned Stage2 presenter loses its timing owner in Stage3. Its
            // early return leaves the old demonstration hand and knife visible
            // over the serving plate unless the new scene explicitly hides them.
            foreach(var presenter in All<BossDemoPresenter>(scene).ToArray())Object.DestroyImmediate(presenter);
            foreach(var transform in All<Transform>(scene).Where(t=>t.name=="Stage01_BossDemoVisuals"||t.name=="Stage01_BossKnife_Demo").ToArray())
            {
                transform.gameObject.SetActive(false);
                if(PrefabUtility.IsPartOfPrefabInstance(transform.gameObject))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(transform.gameObject);
                EditorUtility.SetDirty(transform.gameObject);
            }
        }
        private static void ConfigureResultPanel(Stage03GameController controller)
        {
            // Keep the completed dish visible in the center of the game camera.
            var panel=controller.resultPanel.GetComponent<RectTransform>();
            panel.anchorMin=new Vector2(.12f,.035f);panel.anchorMax=new Vector2(.88f,.31f);
            panel.offsetMin=panel.offsetMax=Vector2.zero;
            var text=controller.resultText;
            text.fontSize=24;text.resizeTextForBestFit=false;
            text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;
            var textRect=text.rectTransform;
            textRect.anchorMin=new Vector2(.05f,.30f);textRect.anchorMax=new Vector2(.95f,.95f);
            textRect.offsetMin=textRect.offsetMax=Vector2.zero;
            var button=(RectTransform)controller.resultPanel.transform.Find("Return");
            if(button==null)throw new InvalidOperationException("Stage3 result return button is missing.");
            button.anchorMin=button.anchorMax=new Vector2(.5f,.15f);button.anchoredPosition=Vector2.zero;
            EditorUtility.SetDirty(panel);EditorUtility.SetDirty(text);EditorUtility.SetDirty(button);
        }
        private static void BuildChart()
        {
            var input=JsonUtility.FromJson<ChartInput>(File.ReadAllText(ChartSource));
            string sha;
            using(var hash=SHA256.Create())sha=BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(MusicPath))).Replace("-","").ToLowerInvariant();
            if(sha!=input.sourceSha256)throw new InvalidOperationException("Approved vocal/chart source identity differs.");
            var chart=AssetDatabase.LoadAssetAtPath<Stage03Chart>(ChartPath);
            if(chart==null){chart=ScriptableObject.CreateInstance<Stage03Chart>();AssetDatabase.CreateAsset(chart,ChartPath);}
            chart.bpm=input.bpm;chart.firstGridBeatSeconds=input.firstGridBeatSeconds;chart.sourceSha256=sha;chart.phases=input.phases;
            chart.music=AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);chart.Validate();EditorUtility.SetDirty(chart);
        }
        private static Stage03GameController BuildScene(Scene scene)
        {
            var old=Find<Stage01ButcheryPresenter>(scene);var view=Find<Stage01CookingView>(scene);
            if(old==null||view==null)throw new InvalidOperationException("Reference workstation is incomplete.");
            Transform knife=old.knifeRoot,grip=old.knifeGripAnchor,contact=old.knifeContactAnchor;
            var body=view.body;var camera=view.gameCamera;
            Vector3 stance=view.standingPosition;float pitch=view.cookingPitch,height=view.eyeHeight;
            var oldHud=Find<Stage01SalmonHUD>(scene);Font font=oldHud.stageTitleText.font;
            var clock=Find<AudioClock>(scene);
            foreach(var go in new[]{old.assembly.gameObject,old.filletHalf,old.reservedFilletHalf})
                if(go!=null)Object.DestroyImmediate(go);
            foreach(var canvas in All<Canvas>(scene).ToArray())if(canvas!=null)Object.DestroyImmediate(canvas.gameObject);
            foreach(var script in All<MonoBehaviour>(scene).ToArray())
            {
                if(script==null)continue;
                string name=script.GetType().Name;
                if(name.StartsWith("Stage01")||name=="RockfishStageAccent"||name=="ProceduralSalmonView"||
                   name=="BeatmapPreviewRunner"||name=="KnifeVisualController"||name=="SliceInputRouter"||name=="SliceCuePresenter")
                    Object.DestroyImmediate(script);
            }
            var host=new GameObject("Stage03_Playable");
            var controller=host.AddComponent<Stage03GameController>();
            controller.clock=clock;
            var cooking=host.AddComponent<Stage03CookingView>();
            cooking.body=body;cooking.gameCamera=camera;cooking.knifeRoot=knife;cooking.grip=grip;cooking.contact=contact;
            cooking.standingPosition=stance;cooking.eyeHeight=height;cooking.cookingPitch=pitch;
            controller.cooking=cooking;
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Stage03FlounderAuthoring.Root+"/Prefabs/PF_Stage03_FlounderAssembly.prefab");
            var fish=(GameObject)PrefabUtility.InstantiatePrefab(prefab,host.transform);
            controller.assembly=fish.GetComponent<FlounderAssemblyView>();
            var board=All<Transform>(scene).Single(t=>t.name=="Stage01_BoardRoot");
            float boardTop=board.GetComponentsInChildren<Renderer>(true).Max(r=>r.bounds.max.y);
            controller.boardTop=boardTop;
            Bounds b=BoundsOf(controller.assembly.bodyStates[0]);
            fish.transform.localScale=Vector3.one*(.67f/Mathf.Max(b.size.x,b.size.z));
            b=BoundsOf(controller.assembly.bodyStates[0]);
            fish.transform.position+=new Vector3(-b.center.x,boardTop+.01f-b.min.y,-.12f-b.center.z);
            var assembly=controller.assembly;
            Bounds hb=assembly.head.GetComponentInChildren<MeshFilter>(true).sharedMesh.bounds;
            float fishScale=fish.transform.lossyScale.x;
            assembly.headStorageAnchor.position=new Vector3(.47f-hb.center.x*fishScale,boardTop+.008f-hb.min.y*fishScale,.20f-hb.center.z*fishScale);
            for(int i=0;i<4;i++)
            {
                Bounds fb=assembly.fillets[i].GetComponentInChildren<MeshFilter>(true).sharedMesh.bounds;
                assembly.filletStorageAnchors[i].position=new Vector3(-.36f+i*.23f-fb.center.x*fishScale,boardTop+.008f-fb.min.y*fishScale,.23f-fb.center.z*fishScale);
                assembly.filletStorageAnchors[i].rotation=Quaternion.identity;
            }
            assembly.filletWorkAnchor.position=new Vector3(0f,boardTop+.008f,-.12f);
            assembly.filletWorkAnchor.rotation=Quaternion.identity;
            controller.fillets=new Stage03FilletSurface[4];
            for(int i=0;i<4;i++)
            {
                var surface=assembly.fillets[i].gameObject.AddComponent<Stage03FilletSurface>();
                surface.meshFilter=assembly.fillets[i].GetComponentInChildren<MeshFilter>(true);
                surface.meshRenderer=surface.meshFilter.GetComponent<MeshRenderer>();
                surface.Bind(AssetDatabase.LoadAssetAtPath<Stage03FilletCutCache>(Stage03FilletCutAuthoring.CachePath(i<2?"FrontFillet":"BackFillet")));
                controller.fillets[i]=surface;
            }
            var plate=All<Transform>(scene).Single(t=>t.name=="CompletedFishPlate");
            Bounds pb=BoundsOf(plate.gameObject);
            controller.sliceRoot=new GameObject("Stage03_PlateSlices").transform;
            controller.sliceRoot.SetParent(host.transform,false);
            controller.plateSlots=new Transform[24];
            float sx=Mathf.Max(.15f,pb.size.x*.70f),sz=Mathf.Max(.12f,pb.size.z*.65f);
            Bounds sliceBounds=BoundsOf(assembly.sashimiSlicePrefab);
            controller.sliceUniformScale=Mathf.Min(sx/6f/sliceBounds.size.x,sz/4f/sliceBounds.size.z)*.90f;
            for(int i=0;i<24;i++)
            {
                var slot=new GameObject("PlateSlot_"+i).transform;slot.SetParent(host.transform,false);
                slot.position=new Vector3(pb.center.x+((i%6)/5f-.5f)*sx,pb.max.y+.006f,pb.center.z+((i/6)/3f-.5f)*sz);
                slot.rotation=Quaternion.Euler(0,(i%6-2.5f)*5f,0);controller.plateSlots[i]=slot;
            }
            BuildHUD(controller,font);
            PrefabUtility.RecordPrefabInstancePropertyModifications(fish.transform);
            foreach(var t in fish.GetComponentsInChildren<Transform>(true))PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            return controller;
        }
        private static void BuildHUD(Stage03GameController c,Font font)
        {
            var canvas=new GameObject("Stage03HUD",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            var cv=canvas.GetComponent<Canvas>();cv.renderMode=RenderMode.ScreenSpaceOverlay;cv.sortingOrder=100;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);scaler.matchWidthOrHeight=.5f;
            var top=Panel(canvas.transform,"Header",new Vector2(.5f,.90f),new Vector2(1100,140),new Color(.02f,.04f,.05f,.88f));
            c.titleText=Text(top,"Title","가자미 손질",font,30,new Vector2(.5f,.70f),new Vector2(1040,42));
            c.statusText=Text(top,"Status","",font,24,new Vector2(.5f,.30f),new Vector2(1040,40));
            var track=Panel(canvas.transform,"NoteLane",new Vector2(.5f,.18f),new Vector2(1050,52),new Color(.03f,.05f,.06f,.9f));
            c.noteLane=track;
            Panel(track,"HitLine",new Vector2(.5f,.5f),new Vector2(4,64),Color.white);
            c.noteMarker=Panel(track,"NextBeat",new Vector2(.5f,.5f),new Vector2(24,24),new Color(1,.78f,.15f));
            c.noteText=Text(canvas.transform,"Instruction","",font,25,new Vector2(.5f,.11f),new Vector2(1250,48));
            c.judgementText=Text(canvas.transform,"Judgement","",font,24,new Vector2(.5f,.25f),new Vector2(1200,65));
            var progress=Panel(top,"SongTrack",new Vector2(.5f,.04f),new Vector2(1060,7),new Color(.16f,.21f,.23f));
            var fill=Panel(progress,"SongProgress",new Vector2(0,.5f),new Vector2(1060,7),new Color(.3f,.78f,.75f));
            fill.pivot=new Vector2(0,.5f);c.songFill=fill.GetComponent<Image>();c.songFill.type=Image.Type.Filled;c.songFill.fillMethod=Image.FillMethod.Horizontal;c.songFill.fillAmount=0;
            c.startPanel=Overlay(canvas.transform,"StartPanel","가자미 손질\n\n머리 → 앞면 필렛 2개 → E 뒤집기 → 뒷면 필렛 2개\n필렛 4개를 썰어 회 24점의 한 접시를 완성하세요\n\n노란 박자가 중앙선에 닿을 때 Space\nEsc 일시 정지 · 실패 시 R로 현재 구간 재시도",font,out _);
            Button(c.startPanel.transform,"Start","손질 시작 · Enter",font,new Vector2(.5f,.21f),c.StartFromButton);
            Button(c.startPanel.transform,"Abort","식당으로 돌아가기",font,new Vector2(.5f,.08f),c.ReturnToShop);
            c.pausePanel=Overlay(canvas.transform,"PausePanel","일시 정지\n\n음악과 손질 진행이 멈췄습니다",font,out _);
            Button(c.pausePanel.transform,"Resume","계속하기 · Esc",font,new Vector2(.5f,.32f),c.TogglePause);
            Button(c.pausePanel.transform,"Abort","손질 중단 · 식당으로",font,new Vector2(.5f,.19f),c.ReturnToShop);
            c.failurePanel=Overlay(canvas.transform,"FailurePanel","이 구간의 손질을 다시 해보세요\n\n이미 끝낸 부위는 유지합니다\n현재 필렛의 조각과 점수는 재시도 시 초기화됩니다",font,out _);
            Button(c.failurePanel.transform,"Retry","현재 구간 재시도 · R",font,new Vector2(.5f,.30f),c.RetryFromButton);
            Button(c.failurePanel.transform,"Abort","손질 중단 · 식당으로",font,new Vector2(.5f,.16f),c.ReturnToShop);
            c.resultPanel=Overlay(canvas.transform,"ResultPanel","",font,out Text result);c.resultText=result;
            Button(c.resultPanel.transform,"Return","식당으로 돌아가기 · F",font,new Vector2(.5f,.18f),c.ReturnToShop);
            c.pausePanel.SetActive(false);c.failurePanel.SetActive(false);c.resultPanel.SetActive(false);
        }
        private static GameObject Overlay(Transform parent,string name,string value,Font font,out Text text)
        {
            var panel=Panel(parent,name,new Vector2(.5f,.5f),new Vector2(1180,570),new Color(.025f,.04f,.05f,.96f));
            text=Text(panel,"Text",value,font,27,new Vector2(.5f,.64f),new Vector2(1110,340));
            return panel.gameObject;
        }
        private static void Button(Transform parent,string name,string value,Font font,Vector2 anchor,UnityAction action)
        {
            var rect=Panel(parent,name,anchor,new Vector2(520,62),new Color(.12f,.35f,.39f));
            var button=rect.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=rect.GetComponent<Image>();
            UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick,action);
            Text(rect,"Label",value,font,25,new Vector2(.5f,.5f),new Vector2(500,58));
        }
        private static RectTransform Panel(Transform parent,string name,Vector2 anchor,Vector2 size,Color color)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image));var rect=go.GetComponent<RectTransform>();
            rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=anchor;rect.sizeDelta=size;rect.anchoredPosition=Vector2.zero;
            go.GetComponent<Image>().color=color;return rect;
        }
        private static Text Text(Transform parent,string name,string value,Font font,int size,Vector2 anchor,Vector2 dimensions)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Text));var rect=go.GetComponent<RectTransform>();
            rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=anchor;rect.sizeDelta=dimensions;rect.anchoredPosition=Vector2.zero;
            var text=go.GetComponent<Text>();text.font=font;text.fontSize=size;text.alignment=TextAnchor.MiddleCenter;text.color=Color.white;text.text=value;
            text.raycastTarget=false;return text;
        }
        public static void BuildPlayerBatch()
        {
            Stage03BuildInputGuard.ValidateMaterializedAssets();
            string output=Environment.GetEnvironmentVariable("SASHIMI_STAGE3_PLAYER");
            if(string.IsNullOrWhiteSpace(output))throw new InvalidOperationException("Set a unique SASHIMI_STAGE3_PLAYER output path.");
            if(File.Exists(output))throw new InvalidOperationException("Never overwrite a prepared player.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report=UnityEditor.BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),
                locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
            if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new InvalidOperationException("Stage3 Player build failed.");
            Debug.Log("[Stage03Playable] PLAYER "+output);
        }
        public static void Scan(Scene scene)
        {
            Stage01PlayableAuthoring.ScanScene(scene);
            foreach(var t in All<Transform>(scene))if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)throw new InvalidOperationException("Missing Script: "+t.name);
            if(All<AudioListener>(scene).Count(a=>a.enabled&&a.gameObject.activeInHierarchy)!=1)throw new InvalidOperationException("Stage3 needs exactly one AudioListener.");
            if(All<EventSystem>(scene).Count(e=>e.enabled&&e.gameObject.activeInHierarchy)!=1)throw new InvalidOperationException("Stage3 needs exactly one EventSystem.");
        }
        private static Bounds BoundsOf(GameObject go)
        {
            var renderers=go.GetComponentsInChildren<Renderer>(true);
            if(renderers.Length==0)throw new InvalidOperationException("Missing geometry "+go.name);
            var b=renderers[0].bounds;foreach(var r in renderers.Skip(1))b.Encapsulate(r.bounds);return b;
        }
        private static T Find<T>(Scene scene)where T:Component=>All<T>(scene).FirstOrDefault();
        private static void PlaceAssembly(Stage03GameController controller,Scene scene)
        {
            var fish=controller.assembly.transform;
            var assembly=controller.assembly;
            var board=All<Transform>(scene).Single(t=>t.name=="Stage01_BoardRoot");
            float top=board.GetComponentsInChildren<Renderer>(true).Max(r=>r.bounds.max.y);
            controller.boardTop=top;
            // The supplied fish's +Z length is placed along the already-tested
            // workstation +X. The existing blade +Z then crosses its cut planes.
            fish.position=Vector3.zero;fish.rotation=Quaternion.Euler(0,90,0);fish.localScale=Vector3.one;
            Bounds unscaled=BoundsOf(assembly.bodyStates[0]);
            fish.localScale=Vector3.one*(.67f/Mathf.Max(unscaled.size.x,unscaled.size.z));
            Bounds body=BoundsOf(assembly.bodyStates[0]);
            fish.position=new Vector3(-body.center.x,top+.01f-body.min.y,-.12f-body.center.z);
            float scale=fish.lossyScale.x;
            Bounds head=assembly.head.GetComponentInChildren<MeshFilter>(true).sharedMesh.bounds;
            assembly.headStorageAnchor.SetPositionAndRotation(new Vector3(.47f-head.center.x*scale,top+.008f-head.min.y*scale,.20f-head.center.z*scale),Quaternion.identity);
            for(int i=0;i<4;i++)
            {
                Bounds fillet=assembly.fillets[i].GetComponentInChildren<MeshFilter>(true).sharedMesh.bounds;
                assembly.filletStorageAnchors[i].SetPositionAndRotation(new Vector3(-.36f+i*.23f-fillet.center.x*scale,
                    top+.008f-fillet.min.y*scale,.23f-fillet.center.z*scale),Quaternion.identity);
            }
            assembly.filletWorkAnchor.SetPositionAndRotation(new Vector3(0,top+.008f,-.12f),Quaternion.Euler(0,-90,0));
            foreach(var transform in fish.GetComponentsInChildren<Transform>(true))
                PrefabUtility.RecordPrefabInstancePropertyModifications(transform);
        }
        private static void NormalizeOwnedOutputs()
        {
            var paths=new System.Collections.Generic.List<string>
            {
                ScenePath,ScenePath+".meta",ChartPath,ChartPath+".meta",
                "Assets/_SashimiBoy/Data/Generated/Stage_03_Sole.asset",
                "Assets/_SashimiBoy/Scenes/Street.unity","Assets/_SashimiBoy/Scenes/KevinHome.unity",
                Stage03DayWorldAuthoring.GeneratedRoot+".meta"
            };
            if(Directory.Exists(Stage03DayWorldAuthoring.GeneratedRoot))
                paths.AddRange(Directory.GetFiles(Stage03DayWorldAuthoring.GeneratedRoot,"*",SearchOption.AllDirectories));
            foreach(string path in paths)
            {
                if(!File.Exists(path)||!new[]{".meta",".asset",".mat",".prefab",".unity",".json"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase))continue;
                if(Path.GetExtension(path)==".asset"){using(var input=File.OpenRead(path))if(input.ReadByte()!='%')continue;}
                string before=File.ReadAllText(path);
                string after=string.Join("\n",File.ReadAllLines(path).Select(line=>line.TrimEnd(' ','\t')))+"\n";
                if(before!=after)
                {
                    AssetDatabase.ReleaseCachedFileHandles();
                    File.WriteAllText(path,after,new System.Text.UTF8Encoding(false));
                }
            }
        }
        private static System.Collections.Generic.IEnumerable<T> All<T>(Scene scene)where T:Component=>scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true));
    }
}
