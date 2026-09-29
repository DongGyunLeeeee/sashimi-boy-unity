using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Object=UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    public static class DayWorldAuthoring
    {
        const string Scenes="Assets/_SashimiBoy/Scenes/";
        const string Art=DayWorldAssetAuthoring.Output;
        const string Owned="DayWorld_Integration";
        static Font Font => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        static readonly Color Ink=new Color(.035f,.065f,.08f);
        [MenuItem("Sashimi Boy/Day 01 + Day 02/Apply World Integration")]
        public static void ApplyBatch()
        {
            Directory.CreateDirectory("Logs/DayWorld");
            DayWorldAssetAuthoring.BuildModels();
            ApplyScenesBatch();
        }
        public static void ApplyScenesBatch()
        {
            foreach(string name in new[]{"Bootstrap","Street","FishShopDialogue","EquipmentShop","Club","KevinHome"}) ApplyScene(name);
            var scenes=new List<EditorBuildSettingsScene>{new EditorBuildSettingsScene(Scenes+"Bootstrap.unity",true)};
            foreach(var scene in EditorBuildSettings.scenes) if(scene.path!=Scenes+"Bootstrap.unity")scenes.Add(scene);
            foreach(string name in new[]{"Street","FishShopDialogue","EquipmentShop","Club","KevinHome","Stage01_Salmon"})
                if(!scenes.Any(s=>s.path==Scenes+name+".unity"))scenes.Add(new EditorBuildSettingsScene(Scenes+name+".unity",true));
            EditorBuildSettings.scenes=scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[DayWorld] Applied existing world scenes and new KevinHome. Stage01 scene was not opened or regenerated. Stage02 availability is taken from the authored build scenes.");
        }
        static void ApplyScene(string name)
        {
            string path=Scenes+name+".unity";
            Scene scene=File.Exists(path)?EditorSceneManager.OpenScene(path,OpenSceneMode.Single):EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            foreach(var previous in scene.GetRootGameObjects().Where(g=>g.name==Owned))Object.DestroyImmediate(previous);
            var root=new GameObject(Owned);
            // Older interior generators keep their art root last. Do not fight their canonical order.
            root.transform.SetAsFirstSibling();
            var director=root.AddComponent<DayWorldSceneDirector>();
            var npcs=new List<DayWorldNpc>();var spawns=new List<Transform>();
            Disable(scene,"Street_PresentationUI","FishShop_PresentationUI","KevinCamera_StartUI","LocationHeader");
            foreach(var debug in Find<PrototypeDebugHotkeys>(scene))debug.enabled=false;
            foreach(var rig in Find<KevinFirstPersonCameraRig>(scene)){rig.startWithUiOpen=false;rig.lockCursorOnStart=true;}
            CreateUI(root.transform,director,name=="Bootstrap");
            DialogueRunner runner=CreateDialogue(root.transform);
            // Route the existing boss interaction into the same input owner and dialogue UI.
            foreach(var trigger in Find<DialogueTrigger>(scene))trigger.runner=runner;
            foreach(var oldRunner in Find<DialogueRunner>(scene).Where(r=>r!=runner))
            {if(oldRunner.dialogueUI!=null && oldRunner.dialogueUI.root!=null)oldRunner.dialogueUI.root.SetActive(false);oldRunner.enabled=false;}
            if(name=="Street")
            {
                RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight=new Color(.45f,.50f,.56f);
                foreach(var sunlight in Find<Light>(scene).Where(l=>l.type==LightType.Directional)){sunlight.intensity=1.2f;sunlight.color=new Color(1f,.94f,.84f);}
                foreach(var camera in Find<Camera>(scene)){camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.37f,.48f,.54f);}
                Disable(scene,"Fish_Market_Left","Equipment_Shop_Back","Club_Right","Sashimi_Shop_Front","FishShop_Facade","EquipmentShop_Facade","Club_Facade","Label_FishShop","Label_EquipmentShop","Label_Club");
                GameObject ground=FindNamed(scene,"Street_Ground");if(ground!=null)ground.transform.localScale=new Vector3(30f,.1f,20f);
                Building(scene,root.transform,"FishShop",new Vector3(-7f,.05f,5f),"Door_To_FishShopDialogue","횟집","FishShopDialogue",spawns);
                Building(scene,root.transform,"EquipmentShop",new Vector3(1f,.05f,6f),"Door_To_EquipmentShop","악기 상점","EquipmentShop",spawns);
                Building(scene,root.transform,"Club",new Vector3(8f,.05f,5f),"Door_To_Club","CLUB","Club",spawns);
                Cube(root.transform,"HomeFacade",new Vector3(-7f,1.5f,-6f),new Vector3(4.8f,3f,2.5f),new Color(.43f,.37f,.30f));
                Cube(root.transform,"HomeRoof",new Vector3(-7f,3.05f,-6f),new Vector3(5.1f,.18f,2.8f),Ink);
                Door(root.transform,"Door_To_Home",new Vector3(-7f,1.05f,-4.68f),0f,"케빈 집",DayWorldRules.Home,"Entry");
                Label(root.transform,"HomeSign","케빈의 집",new Vector3(-7f,2.55f,-4.62f),0f,.12f);
                npcs.Add(Npc(root.transform,"Misuk",new Vector3(-4.8f,.05f,-2.4f),0f,runner));
                npcs.Add(Npc(root.transform,"Seongho",new Vector3(-3.4f,.05f,.35f),180f,runner));
                npcs.Add(Npc(root.transform,"Minjae",new Vector3(3.25f,.05f,2.6f),180f,runner));
                spawns.Add(Spawn(root.transform,"KevinHome",new Vector3(-7f,.94f,-2.2f),90f));
                spawns.Add(Spawn(root.transform,"Entry",new Vector3(-7f,.94f,-2.2f),90f));
            }
            else if(name=="FishShopDialogue")
            {
                npcs.Add(Npc(root.transform,"Cheolsu",new Vector3(3.35f,.05f,1.1f),180f,runner));
                Cube(root.transform,"CustomerSeat",new Vector3(3.35f,.49f,1.12f),new Vector3(.8f,.14f,.75f),new Color(.25f,.18f,.12f));
                Cube(root.transform,"CustomerTable",new Vector3(3.35f,.73f,-.05f),new Vector3(1.5f,.12f,.8f),new Color(.45f,.30f,.18f));
                Cube(root.transform,"CustomerTableSupport",new Vector3(3.35f,.37f,-.05f),new Vector3(.15f,.7f,.45f),Ink);
                var plate=GameObject.CreatePrimitive(PrimitiveType.Cylinder);plate.name="CheolsuMealPlate";plate.transform.SetParent(root.transform,false);plate.transform.position=new Vector3(3.35f,.815f,-.05f);plate.transform.localScale=new Vector3(.55f,.015f,.32f);
                var sliceSource=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_SashimiBoy/Art/Source/Stage01/OwnerVisualRevision/SashimiSlice/salmonpiece.fbx");
                if(sliceSource==null) {string candidate=AssetDatabase.FindAssets("salmonpiece t:Model").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();if(candidate!=null)sliceSource=AssetDatabase.LoadAssetAtPath<GameObject>(candidate);}
                if(sliceSource!=null){var meal=(GameObject)PrefabUtility.InstantiatePrefab(sliceSource,root.transform);var b=DayWorldAssetAuthoring.BoundsOf(meal);meal.transform.localScale*=.3f/b.size.x;b=DayWorldAssetAuthoring.BoundsOf(meal);meal.transform.position+=new Vector3(3.35f-b.center.x,.84f-b.min.y,-.05f-b.center.z);}
                spawns.Add(Spawn(root.transform,"Entry",new Vector3(3.6f,.94f,-2.8f),320f));
                spawns.Add(Spawn(root.transform,"StageReturn",new Vector3(-2.1f,.94f,-3.6f),0f));
            }
            else if(name=="EquipmentShop")
            {
                var controller=Find<EquipmentShopController>(scene).Single();director.shopRoot=controller.gameObject;
                var closeText=controller.leaveButton.GetComponentInChildren<Text>();if(closeText!=null)closeText.text="둘러보기로 돌아가기";
                var interact=Cube(root.transform,"ShopService",new Vector3(0f,1.0f,1.45f),new Vector3(2.0f,.8f,.2f),Ink);
                var service=interact.AddComponent<DayWorldInteractable>();service.kind=DayWorldInteractionKind.Shop;service.director=director;
                Label(root.transform,"ShopPrompt","장비 구매 · E",new Vector3(0f,1.55f,1.30f),180f,.085f);
                spawns.Add(Spawn(root.transform,"Entry",new Vector3(0f,.94f,-2.4f),0f));
                // Existing instruments, counter, owner and interior art remain in their scene.
            }
            else if(name=="Club")
            {
                spawns.Add(Spawn(root.transform,"Entry",new Vector3(5.8f,.94f,-3.25f),320f));
                director.optionalVenuePanel=FindNamed(scene,"ClubPanel");
            }
            else if(name=="KevinHome")CreateHome(root.transform,director,spawns);
            DayWorldInteriorAuthoring.Apply(scene,root.transform);
            DayWorldHomeAuthoring.Apply(scene,root.transform);
            DayWorldVenueAuthoring.Apply(scene,root.transform);
            DayWorldStreetAuthoring.Apply(scene,root.transform);
            director.npcs=npcs.ToArray();director.spawns=spawns.ToArray();
            EnsureEventSystem(scene,root.transform);
            Scan(scene);
            UnityEngine.Canvas.ForceUpdateCanvases();
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,path);
        }
        static void CreateHome(Transform root,DayWorldSceneDirector director,List<Transform> spawns)
        {
            Cube(root,"HomeFloor",new Vector3(0f,0f,0f),new Vector3(10f,.1f,8f),new Color(.37f,.28f,.20f));
            Cube(root,"BackWall",new Vector3(0f,1.6f,4f),new Vector3(10f,3.2f,.15f),new Color(.65f,.62f,.52f));
            Cube(root,"LeftWall",new Vector3(-5f,1.6f,0f),new Vector3(.15f,3.2f,8f),new Color(.54f,.59f,.57f));
            Cube(root,"RightWall",new Vector3(5f,1.6f,0f),new Vector3(.15f,3.2f,8f),new Color(.54f,.59f,.57f));
            Cube(root,"FrontWallLeft",new Vector3(-2f,1.6f,-4f),new Vector3(6f,3.2f,.15f),new Color(.54f,.59f,.57f));
            Cube(root,"FrontWallRight",new Vector3(4f,1.6f,-4f),new Vector3(2f,3.2f,.15f),new Color(.54f,.59f,.57f));
            Door(root,"Door_To_Street",new Vector3(0f,1.05f,-3.9f),0f,"거리로 나가기","Street","KevinHome");
            Label(root,"HomeExit","거리로 · E",new Vector3(0f,2.35f,-3.8f),0f,.1f);
            Cube(root,"BedFrame",new Vector3(3f,.3f,1.65f),new Vector3(1.6f,.45f,2.35f),new Color(.21f,.16f,.12f));
            var bed=Cube(root,"Bed",new Vector3(3f,.61f,1.65f),new Vector3(1.5f,.2f,2.25f),new Color(.18f,.33f,.43f));
            var interact=bed.AddComponent<DayWorldInteractable>();interact.director=director;interact.kind=DayWorldInteractionKind.Bed;
            Cube(root,"Pillow",new Vector3(3f,.78f,2.4f),new Vector3(.85f,.14f,.5f),new Color(.77f,.78f,.71f));
            Label(root,"BedLabel","침대 · E",new Vector3(3f,1.25f,2.9f),180f,.08f);
            var player=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_SashimiBoy/Art/Generated/Prefabs/Characters/PF_Player_Kevin_FirstPerson.prefab"),root);
            player.transform.position=new Vector3(1.45f,.10f,1.35f);player.name="Kevin_Player";
            var rig=player.GetComponent<KevinFirstPersonCameraRig>();rig.startWithUiOpen=false;rig.pitchRoot.localRotation=Quaternion.Euler(25f,0f,0f);rig.yawRoot.localRotation=Quaternion.Euler(0f,90f,0f);
            var light=new GameObject("HomeWarmLight").AddComponent<Light>();light.transform.SetParent(root,false);light.type=LightType.Point;light.range=15f;light.intensity=2.7f;light.transform.position=new Vector3(0f,2.9f,0f);light.color=new Color(1f,.87f,.7f);
            RenderSettings.ambientLight=new Color(.47f,.49f,.52f);
            spawns.Add(Spawn(root,"Wake",new Vector3(1.45f,.10f,1.35f),90f));
            spawns[spawns.Count-1].rotation=Quaternion.Euler(34f,90f,0f);
            spawns.Add(Spawn(root,"Entry",new Vector3(0f,.10f,-2.8f),0f));
            var drum=Station(root,director,EquipmentId.SamplePackDrumKit,new Vector3(-2.5f,.05f,1.65f));
            drum.equipmentVisual=Instance(drum.transform,"DrumKit",new Vector3(-2.5f,.05f,1.65f),180f);
            drum.playerPosition.position=new Vector3(-2.5f,.10f,1.02f);
            drum.rightHandTarget.position=new Vector3(-2.24f,1.05f,1.48f);drum.leftHandTarget.position=new Vector3(-2.76f,1.05f,1.48f);
            var daw=Station(root,director,EquipmentId.DawSoftware,new Vector3(.1f,.05f,2.1f));
            var desk=new GameObject("DawWorkstation");desk.transform.SetParent(daw.transform,false);daw.equipmentVisual=desk;
            Cube(desk.transform,"Desk",new Vector3(.1f,.82f,2.1f),new Vector3(1.45f,.12f,.75f),new Color(.34f,.25f,.16f));
            Cube(desk.transform,"MonitorFrame",new Vector3(.1f,1.30f,2.27f),new Vector3(.92f,.55f,.09f),Ink);
            Cube(desk.transform,"DAWDisplay",new Vector3(.1f,1.30f,2.21f),new Vector3(.85f,.47f,.012f),new Color(.055f,.14f,.17f));
            for(int i=0;i<6;i++)Cube(desk.transform,"PatternRow"+i,new Vector3(-.23f+i*.12f,1.18f+(i%3)*.075f,2.195f),new Vector3(.08f,.04f,.01f),new Color(.28f,.7f,.64f));
            Cube(desk.transform,"Keyboard",new Vector3(.1f,.92f,1.95f),new Vector3(.7f,.04f,.22f),new Color(.18f,.20f,.22f));
            for(int i=0;i<4;i++)Cube(desk.transform,"DeskLeg"+i,new Vector3(i%2==0?-.52f:.72f,.42f,i<2?1.84f:2.36f),new Vector3(.08f,.8f,.08f),Ink);
            daw.playerPosition.position=new Vector3(.1f,.10f,1.51f);daw.rightHandTarget.position=new Vector3(.3f,.96f,1.92f);daw.leftHandTarget.position=new Vector3(-.1f,.96f,1.92f);
            director.equipmentStations=new[]{drum,daw};
            CreateHomePrompt(root);
        }
        static HomeEquipmentStation Station(Transform root,DayWorldSceneDirector director,EquipmentId id,Vector3 position)
        {
            var go=new GameObject("HomeStation_"+id);go.transform.SetParent(root,false);
            var station=go.AddComponent<HomeEquipmentStation>();station.equipmentId=id;
            var box=go.AddComponent<BoxCollider>();box.center=position+Vector3.up*.65f;box.size=new Vector3(1.5f,1.3f,1.5f);box.isTrigger=true;
            station.playerPosition=Spawn(go.transform,"PracticePosition",position+new Vector3(0f,.89f,-1f),0f);
            station.rightHandTarget=Spawn(go.transform,"RightHand",position+new Vector3(.24f,1f,-.1f),0f);
            station.leftHandTarget=Spawn(go.transform,"LeftHand",position+new Vector3(-.24f,1f,-.1f),0f);
            station.practiceCamera=Spawn(go.transform,"PracticeCamera",position+new Vector3(2.5f,1.85f,-2.3f),0f);
            station.practiceCamera.LookAt(position+new Vector3(0f,1.03f,-.30f));
            Label(go.transform,"StationName",ContentDefaults.FindEquipment(id).displayName+" · E",position+new Vector3(0f,1.7f,.65f),180f,.065f);
            return station;
        }
        static void Building(Scene scene,Transform root,string id,Vector3 position,string doorName,string sign,string destination,List<Transform> spawns)
        {
            var model=Instance(root,id,position,180f);Bounds bounds=DayWorldAssetAuthoring.BoundsOf(model);
            if(bounds.size.x>6.4f){model.transform.localScale*=6.4f/bounds.size.x;bounds=DayWorldAssetAuthoring.BoundsOf(model);}
            var blocker=model.AddComponent<BoxCollider>();blocker.center=model.transform.InverseTransformPoint(bounds.center);blocker.size=new Vector3(bounds.size.x, bounds.size.y,Mathf.Max(1f,bounds.size.z))/model.transform.lossyScale.x;
            Vector3 doorPosition=new Vector3(position.x,1.05f,bounds.min.z-.10f);
            var oldDoor=FindNamed(scene,doorName);if(oldDoor!=null)oldDoor.SetActive(false);
            Door(root,doorName+"_DayWorld",doorPosition,180f,sign+"으로 들어가기",destination,"Entry");
            root.Find(doorName+"_DayWorld").GetComponent<Renderer>().enabled=false; // The supplied FBX already contains the visible door.
            Label(root,id+"ReadableSign",sign,new Vector3(position.x,Mathf.Min(3.8f,bounds.max.y-.4f),bounds.min.z-.13f),180f,.16f);
            spawns.Add(Spawn(root,destination,doorPosition+new Vector3(0f,-.11f,-1.8f),180f));
        }
        static DayWorldNpc Npc(Transform root,string id,Vector3 position,float yaw,DialogueRunner runner)
        {var go=Instance(root,id,position,yaw);var npc=go.GetComponent<DayWorldNpc>();npc.runner=runner;return npc;}
        static GameObject Instance(Transform parent,string id,Vector3 position,float yaw)
        {var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Art+"/PF_"+id+".prefab");if(prefab==null)throw new InvalidOperationException("Missing prefab: "+id);var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);go.transform.SetPositionAndRotation(position,Quaternion.Euler(0f,yaw,0f));return go;}
        static Transform Spawn(Transform parent,string name,Vector3 position,float yaw)
        {var t=new GameObject(name).transform;t.SetParent(parent,false);t.SetPositionAndRotation(position,Quaternion.Euler(0f,yaw,0f));return t;}
        static GameObject Cube(Transform parent,string name,Vector3 position,Vector3 size,Color color)
        {var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.position=position;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=Material(name,color);return go;}
        static Material Material(string name,Color color)
        {string path=Art+"/MAT_World_"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m==null){m=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(m,path);}m.color=color;m.SetFloat("_Glossiness",.2f);EditorUtility.SetDirty(m);return m;}
        static void Door(Transform parent,string name,Vector3 position,float yaw,string prompt,string scene,string spawn)
        {var go=Cube(parent,name,position,new Vector3(1.25f,2f,.13f),new Color(.16f,.25f,.29f));go.transform.rotation=Quaternion.Euler(0f,yaw,0f);var door=go.AddComponent<SceneDoor>();door.prompt=prompt;door.sceneName=scene;door.destinationSpawn=spawn;}
        static void Label(Transform parent,string name,string text,Vector3 position,float yaw,float size)
        {var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.SetPositionAndRotation(position,Quaternion.Euler(0f,yaw+180f,0f));var label=go.AddComponent<TextMesh>();label.text=text;label.font=Font;label.fontSize=64;label.characterSize=size*.33f;label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;label.color=Color.white;go.GetComponent<Renderer>().sharedMaterial=Font.material;}
        static IEnumerable<T> Find<T>(Scene scene) where T:Component => scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true));
        static GameObject FindNamed(Scene scene,string name)=>Find<Transform>(scene).FirstOrDefault(t=>t.name==name)?.gameObject;
        static void Disable(Scene scene,params string[] names){foreach(string name in names){var go=FindNamed(scene,name);if(go!=null)go.SetActive(false);}}
        static void EnsureEventSystem(Scene scene,Transform root){if(!Find<EventSystem>(scene).Any(e=>e.gameObject.activeInHierarchy)){var go=new GameObject("DayWorldEventSystem",typeof(EventSystem),typeof(StandaloneInputModule));go.transform.SetParent(root,false);}}
        static void Scan(Scene scene)
        {
            foreach(var t in Find<Transform>(scene))if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)throw new InvalidOperationException("Missing script at "+scene.name+"/"+t.name);
            foreach(var r in Find<Renderer>(scene))if(r.sharedMaterials.Any(m=>m==null))throw new InvalidOperationException("Missing material at "+scene.name+"/"+r.name);
            if(Find<EventSystem>(scene).Count(e=>e.enabled&&e.gameObject.activeInHierarchy)!=1)throw new InvalidOperationException("EventSystem count: "+scene.name);
        }
        static Canvas Canvas(Transform parent,string name)
        {var go=new GameObject(name,typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));go.transform.SetParent(parent,false);var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=40;var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);return canvas;}
        static RectTransform Rect(Transform parent,string name,Vector2 anchor,Vector2 size)
        {var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);var rect=go.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=anchor;rect.sizeDelta=size;return rect;}
        static Text Text(Transform parent,string name,string value,Vector2 anchor,Vector2 size,int fontSize)
        {var rect=Rect(parent,name,anchor,size);var text=rect.gameObject.AddComponent<Text>();text.font=Font;text.text=value;text.color=Color.white;text.fontSize=fontSize;text.resizeTextMaxSize=Mathf.Max(40,fontSize);text.alignment=TextAnchor.MiddleCenter;text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;text.raycastTarget=false;return text;}
        static Button Button(Transform parent,string name,string label,Vector2 anchor,Vector2 size)
        {var rect=Rect(parent,name,anchor,size);rect.gameObject.AddComponent<Image>().color=new Color(.12f,.40f,.43f);var b=rect.gameObject.AddComponent<Button>();Text(rect,name+"Text",label,new Vector2(.5f,.5f),size-Vector2.one*12f,26);return b;}
        static void CreateUI(Transform root,DayWorldSceneDirector director,bool menu)
        {
            var canvas=Canvas(root,"DayWorldHUD");
            if(menu)
            {
                var panel=Rect(canvas.transform,"DayWorldMenu",new Vector2(.5f,.5f),new Vector2(1600,900));panel.gameObject.AddComponent<Image>().color=Ink;director.menuRoot=panel.gameObject;
                Text(panel,"Title","SASHIMI BOY",new Vector2(.5f,.7f),new Vector2(800,100),65);
                Text(panel,"Subtitle","첫째 날부터 둘째 날까지",new Vector2(.5f,.58f),new Vector2(700,60),27);
                director.newGameButton=Button(panel,"NewGame","새 게임",new Vector2(.4f,.4f),new Vector2(240,70));
                director.continueButton=Button(panel,"Continue","이어하기",new Vector2(.6f,.4f),new Vector2(240,70));
                Text(panel,"Controls","이동 WASD · 둘러보기 마우스 · 상호작용 E\n대화 Space · 대화/연습 취소 Esc · 손질 Space",new Vector2(.5f,.22f),new Vector2(1000,110),23);
                KevinCustomizationAuthoring.ApplyMenu(director);
                return;
            }
            var objective=Rect(canvas.transform,"Objective",new Vector2(.19f,.91f),new Vector2(530,130));objective.gameObject.AddComponent<Image>().color=new Color(Ink.r,Ink.g,Ink.b,.92f);
            director.dayText=Text(objective,"Day","첫째 날",new Vector2(.5f,.76f),new Vector2(490,40),27);director.dayText.color=new Color(.95f,.77f,.39f);
            director.objectiveText=Text(objective,"ObjectiveText","",new Vector2(.5f,.33f),new Vector2(490,68),24);
            var controls=Rect(canvas.transform,"ExplorationControls",new Vector2(.19f,.805f),new Vector2(530,34));controls.gameObject.AddComponent<Image>().color=new Color(Ink.r,Ink.g,Ink.b,.85f);director.explorationControls=controls.gameObject;
            Text(controls,"Controls","WASD 이동 · 마우스 시점 · E 사용",new Vector2(.5f,.5f),new Vector2(510,32),17);
            var fade=Rect(canvas.transform,"SleepFade",new Vector2(.5f,.5f),new Vector2(1600,900));fade.gameObject.AddComponent<Image>().color=Color.black;director.fade=fade.gameObject.AddComponent<CanvasGroup>();director.fade.alpha=0f;director.fade.blocksRaycasts=false;
            var complete=Rect(canvas.transform,"DayWorldComplete",new Vector2(.5f,.5f),new Vector2(900,340));complete.gameObject.AddComponent<Image>().color=Ink;
            Text(complete,"EndTitle","둘째 날을 마쳤습니다",new Vector2(.5f,.65f),new Vector2(850,100),42);
            Text(complete,"ToBeContinued","TO BE CONTINUED",new Vector2(.5f,.35f),new Vector2(800,70),30);director.completeRoot=complete.gameObject;complete.gameObject.SetActive(false);
        }
        static DialogueRunner CreateDialogue(Transform root)
        {
            var canvas=Canvas(root,"DayWorldDialogue");canvas.sortingOrder=60;
            var runner=canvas.gameObject.AddComponent<DialogueRunner>();var ui=canvas.gameObject.AddComponent<DialogueUI>();runner.dialogueUI=ui;
            var panel=Rect(canvas.transform,"DialoguePanel",new Vector2(.5f,.16f),new Vector2(1300,220));panel.gameObject.AddComponent<Image>().color=new Color(Ink.r,Ink.g,Ink.b,.97f);ui.root=panel.gameObject;
            ui.speakerText=Text(panel,"Speaker","",new Vector2(.5f,.79f),new Vector2(1220,40),25);ui.speakerText.color=new Color(.95f,.77f,.39f);
            ui.bodyText=Text(panel,"Dialogue","",new Vector2(.48f,.43f),new Vector2(1150,110),30);
            ui.nextButton=Button(panel,"Next","다음 · Space",new Vector2(.88f,.14f),new Vector2(220,42));
            Text(panel,"CancelHint","Esc · 대화 취소",new Vector2(.14f,.12f),new Vector2(260,35),17);
            return runner;
        }
        static void CreateHomePrompt(Transform root)
        {
            var canvas=Canvas(root,"HomeInteractionHUD");canvas.sortingOrder=35;
            var prompt=canvas.gameObject.AddComponent<InteractionPromptUI>();var p=Rect(canvas.transform,"Prompt",new Vector2(.5f,.79f),new Vector2(650,65));p.gameObject.AddComponent<Image>().color=Ink;prompt.root=p.gameObject;prompt.promptText=Text(p,"PromptText","",new Vector2(.5f,.5f),new Vector2(620,60),24);
            var toast=canvas.gameObject.AddComponent<ToastUI>();var t=Rect(canvas.transform,"Toast",new Vector2(.5f,.67f),new Vector2(900,100));t.gameObject.AddComponent<Image>().color=Ink;toast.root=t.gameObject;toast.messageText=Text(t,"ToastText","",new Vector2(.5f,.5f),new Vector2(850,95),24);
            var reticle=Text(canvas.transform,"Reticle","+",new Vector2(.5f,.5f),new Vector2(40,40),23);
            root.GetComponentInChildren<KevinFirstPersonCameraRig>().reticleRoot=reticle.gameObject;
        }
        public static void BuildWindowsBatch()
        {
            if(File.Exists(Stage02RockfishAuthoring.ScenePath)) Stage02RockfishAuthoring.AuditModelReapplyBatch();
            // This validation build also checks that the authoritative model generator is repeatable.
            string Hash(string path) { using(var sha=System.Security.Cryptography.SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path))); }
            var generatedBefore=Directory.GetFiles(Art,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>Hash(p));
            DayWorldAssetAuthoring.BuildModels();
            var changed=generatedBefore.Keys.Where(p=>!File.Exists(p)||Hash(p)!=generatedBefore[p]).ToArray();
            if(changed.Length>0 || Directory.GetFiles(Art,"*",SearchOption.AllDirectories).Length!=generatedBefore.Count)
                throw new InvalidOperationException("DayWorld model reapply changed generated bytes: "+string.Join(", ",changed));
            Debug.Log("[DayWorld] Model reapply byte stability: "+generatedBefore.Count+" files unchanged.");
            BuildWindowsPlayerBatch();
        }
        // Run after the model audits on the same source revision when a separate
        // batch process is needed to release large imported-model allocations.
        public static void BuildWindowsPlayerBatch()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            EditorUtility.UnloadUnusedAssetsImmediate();
            GC.Collect();
            string dir="Builds/DayWorldValidation-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(dir);
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),locationPathName=dir+"/SashimiBoyDayWorld.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development | BuildOptions.CompressWithLz4});
            if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("DayWorld build: "+report.summary.result);
            Debug.Log("[DayWorld] Windows build: "+Path.GetFullPath(dir+"/SashimiBoyDayWorld.exe"));
        }
        public static void AuditExistingFishShopGeneratorBatch()
        {
            string path=Scenes+"FishShopDialogue.unity";byte[] before=File.ReadAllBytes(path);
            Directory.CreateDirectory("Logs/DayWorld/GeneratorAudit");
            File.WriteAllBytes("Logs/DayWorld/GeneratorAudit/FishShop-before.unity",before);
            try
            {
                NewFishShopAssetsScenePipeline.RebuildFishShopArtPassBatch();
                File.WriteAllBytes("Logs/DayWorld/GeneratorAudit/FishShop-after.unity",File.ReadAllBytes(path));
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                File.WriteAllBytes(path,before);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            }
        }
    }
}
