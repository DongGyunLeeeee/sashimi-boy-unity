using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SashimiBoy.Tests
{
    public sealed class DayWorldEditModeTests
    {
        static object Get(object value,string field)=>RuntimeReflection.GetField(value,field);
        static void Set(object value,string field,object data)=>RuntimeReflection.SetField(value,field,data);
        static object Rule(string name,params object[] args)=>RuntimeReflection.InvokeStatic("SashimiBoy.DayWorldRules",name,args);
        static object Fresh()
        {
            var save=RuntimeReflection.InvokeStatic("SashimiBoy.SaveData","CreateNew");
            Set(Get(save,"dayWorld"),"active",true);return save;
        }
        static void Beat(object save,string beat)=>Set(Get(save,"dayWorld"),"beat",Enum.Parse(RuntimeReflection.RuntimeType("SashimiBoy.DayWorldBeat"),beat));
        static object Equipment(string name)=>Enum.Parse(RuntimeReflection.RuntimeType("SashimiBoy.EquipmentId"),name);

        [Test]
        public void LegacySave_DoesNotStartStoryOrRemoveInventory()
        {
            var save=JsonUtility.FromJson("{\"version\":1,\"ownedEquipmentIds\":[\"SamplePackDrumKit\"],\"clearedStageIds\":[\"STAGE_01_SALMON\"]}",RuntimeReflection.RuntimeType("SashimiBoy.SaveData"));
            Assert.That(Rule("Active",save),Is.EqualTo(false));
            Assert.That(Rule("CanStart",save,"STAGE_01_SALMON"),Is.EqualTo(true));
            Assert.That(((IList)Get(save,"ownedEquipmentIds")).Contains("SamplePackDrumKit"),Is.True);
        }

        [Test]
        public void RequiredConversation_DoesNotAcceptWrongNpc_ReentryOrEarlyStageClear()
        {
            var save=Fresh();
            Assert.That(Rule("CanRecordClear",save,"STAGE_01_SALMON"),Is.EqualTo(false));
            Assert.That(Rule("FinishDialogue",save,"misuk"),Is.EqualTo(false));
            Rule("Wake",save);
            Assert.That(Rule("FinishDialogue",save,"cheolsu"),Is.EqualTo(false));
            Assert.That(Rule("FinishDialogue",save,"misuk"),Is.EqualTo(true));
            Assert.That(Rule("FinishDialogue",save,"misuk"),Is.EqualTo(false));
            Assert.That(Rule("CanRecordClear",save,"STAGE_02_ROCKFISH"),Is.EqualTo(false));
            Assert.That(Rule("CanRecordClear",save,"STAGE_01_SALMON"),Is.EqualTo(true));
            Rule("RecordClear",save,"STAGE_01_SALMON");
            Assert.That(Rule("CanRecordClear",save,"STAGE_01_SALMON"),Is.EqualTo(false));
            Assert.That(Rule("CanPurchase",save,"STAGE_01_SALMON"),Is.EqualTo(false));
        }

        [Test]
        public void Home_DoesNotPlaceUnownedEquipmentOrSleepBeforePractice_AndKeepsDay1Equipment()
        {
            var save=Fresh();var drum=Equipment("SamplePackDrumKit");Beat(save,"Placement");
            Assert.That(Rule("Place",save,drum),Is.EqualTo(false));
            ((IList)Get(save,"ownedEquipmentIds")).Add("SamplePackDrumKit");
            Assert.That(Rule("Place",save,Equipment("DawSoftware")),Is.EqualTo(false));
            Assert.That(Rule("Place",save,drum),Is.EqualTo(true));
            Assert.That(Rule("Sleep",save),Is.EqualTo(false));
            Assert.That(Rule("Practice",save,drum),Is.EqualTo(true));
            Assert.That(Rule("Sleep",save),Is.EqualTo(true));
            string once=JsonUtility.ToJson(save);
            Assert.That(Rule("Sleep",save),Is.EqualTo(false));
            Assert.That(JsonUtility.ToJson(save),Is.EqualTo(once));
            var restored=JsonUtility.FromJson(once,save.GetType());var progress=Get(restored,"dayWorld");
            Assert.That(Get(progress,"day"),Is.EqualTo(2));
            Assert.That(((IList)Get(progress,"placedEquipment")).Contains("SamplePackDrumKit"),Is.True);
        }

        [Test]
        public void LegacyStageTwoArrival_SaveCanEnterActualStage2_WithoutAwardingClear()
        {
            var save=Fresh();var progress=Get(save,"dayWorld");
            Set(progress,"day",2);((IList)Get(save,"unlockedStageIds")).Add("STAGE_02_ROCKFISH");
            Assert.That(Rule("CanStart",save,"STAGE_02_ROCKFISH"),Is.EqualTo(false));
            Rule("Wake",save);Assert.That(Rule("CanStart",save,"STAGE_02_ROCKFISH"),Is.EqualTo(false));
            Assert.That(Rule("FinishDialogue",save,"seongho"),Is.EqualTo(true));
            Set(progress,"reachedStageTwoBoundary",true);
            string once=JsonUtility.ToJson(save);var restored=JsonUtility.FromJson(once,save.GetType());
            Assert.That(Rule("CanStart",restored,"STAGE_02_ROCKFISH"),Is.EqualTo(true));
            Assert.That(Rule("CanStart",restored,"STAGE_01_SALMON"),Is.EqualTo(false));
            Assert.That(Get(Get(restored,"dayWorld"),"beat").ToString(),Is.EqualTo("Work"));
            Assert.That(Get(Get(restored,"dayWorld"),"day"),Is.EqualTo(2));
            Assert.That(((IList)Get(restored,"clearedStageIds")).Count,Is.Zero);
            Assert.That(JsonUtility.ToJson(restored),Is.EqualTo(once));
        }

        [Test]
        public void Scenario_UsesApprovedMinjaeReply_AndKeepsStageDirectionsOutOfSpeech()
        {
            object[] Lines(string npc)=>((IEnumerable)RuntimeReflection.InvokeStatic("SashimiBoy.DayWorldScenario","Lines",npc)).Cast<object>().ToArray();
            var minjae=Lines("minjae");
            Assert.That(minjae.Any(l=>(string)Get(l,"text")=="퇴근 중이니까." && (string)Get(l,"speaker")=="케빈"),Is.True);
            Assert.That(minjae.Any(l=>((string)Get(l,"text")).Contains("출근 중")),Is.False);
            var action=Lines("seongho")[0];
            Assert.That(Get(action,"kind").ToString(),Is.EqualTo("Action"));
            Assert.That(Get(action,"actionId"),Is.EqualTo("motorcycle_stop"));
            foreach(string npc in new[]{"misuk","cheolsu","seongho","minjae"})
                foreach(var line in Lines(npc))
                {
                    Assert.That(Get(line,"sourcePage"),Is.InRange(1,6));
                    if(Get(line,"kind").ToString()=="Thought") Assert.That(Get(line,"speaker"),Is.EqualTo("케빈 · 속마음"));
                }
        }

        [Test]
        public void Stage2_HasDedicatedMusicChartAndRockfishMeshes_WhileStage1KeepsItsSource()
        {
            string[] ids={"STAGE_01_SALMON","STAGE_02_ROCKFISH"};
            string[] names={"Stage01_Salmon","Stage02_Rockfish"};
            string[] music={"stage01_salmon_main","rockfish1"};
            string[] charts={"Stage01SemanticBeatmap","Stage02SemanticBeatmap"};
            for(int i=0;i<2;i++)
            {
                var scene=EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/"+names[i]+".unity",OpenSceneMode.Single);
                var timing=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren(RuntimeReflection.RuntimeType("SashimiBoy.Stage01SalmonTimingScaffold"),true)).Single();
                Assert.That(Get(timing,"stageId"),Is.EqualTo(ids[i]));
                Assert.That(((AudioClip)Get(timing,"musicClip")).name,Is.EqualTo(music[i]));
                var chart=(UnityEngine.Object)Get(timing,"semanticBeatmap");
                Assert.That(chart.name,Is.EqualTo(charts[i]));
                var provider=Get(timing,"notePatternProvider");RuntimeReflection.Invoke(provider,"Initialize",timing);
                Assert.That(RuntimeReflection.Invoke(chart,"MatchesSource",timing),Is.EqualTo(true));
                if(i==0)continue;
                var notes=(IList)Get(provider,"runtimeNotes");Assert.That(notes.Count,Is.EqualTo(222));
                Assert.That(notes.Cast<object>().Any(n=>(bool)Get(n,"repeated")),Is.False);
                Assert.That(Get(timing,"gameplayEndSec"),Is.LessThan((double)((AudioClip)Get(timing,"musicClip")).length));
                var view=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren(RuntimeReflection.RuntimeType("SashimiBoy.Stage01ButcheryPresenter"),true)).Single();
                var assembly=(Component)Get(view,"assembly");
                Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(assembly.gameObject),Does.EndWith("PF_Stage02_RockfishAssembly.prefab"));
                foreach(var part in new[]{"head","body","fins","fillet","spine"})
                    Assert.That(((Component)Get(assembly,part)).GetComponentsInChildren<MeshFilter>(true),Is.Not.Empty);
                string[] fields={"head","body","spine","fillet"},sources={"head","body","bone","fillet"};
                for(int p=0;p<fields.Length;p++)
                {
                    var part=(Component)Get(assembly,fields[p]);
                    AssertOwnerRockfishMesh(part.gameObject,sources[p]);
                }
                AssertOwnerRockfishMesh((GameObject)Get(view,"filletHalf"),"fillet_half");
                AssertOwnerRockfishMesh((GameObject)Get(view,"slicePrefab"),"piece");
                Assert.That(AssetDatabase.GetAssetPath((UnityEngine.Object)Get(view,"slicePrefab")),Does.EndWith("PF_RockfishSashimiSlice.prefab"));
                foreach(var component in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Component>(true)))
                {
                    Assert.That(component,Is.Not.Null,"Missing Script in Stage2");
                    using(var serialized=new SerializedObject(component))
                    {
                        var property=serialized.GetIterator();
                        while(property.Next(true))if(property.propertyType==SerializedPropertyType.ObjectReference&&property.objectReferenceValue==null)
                            Assert.That(property.objectReferenceInstanceIDValue,Is.Zero,component.name+"/"+property.propertyPath);
                    }
                }
            }
        }

        static void AssertOwnerRockfishMesh(GameObject part,string source)
        {
            var filter=part.GetComponentsInChildren<MeshFilter>(true).First();
            Assert.That(AssetDatabase.GetAssetPath(filter.sharedMesh),Does.EndWith("MS_Rockfish_Owner_"+source+".asset"));
            Assert.That(filter.sharedMesh.vertexCount,Is.InRange(1000,120000),"Use a reduced game copy of the supplied model.");
            Assert.That(AssetDatabase.GetAssetPath(filter.GetComponent<Renderer>().sharedMaterial.mainTexture),
                Is.EqualTo("Assets/_SashimiBoy/Art/Source/DayWorld/Fish/Stage02/rockfish_"+source+"/rockfish_"+source+"_basecolor.JPEG"));
        }

        [TestCase("Stage01_Salmon")][TestCase("Stage02_Rockfish")]
        public void OwnerJudgementHud_ReferencesFourOriginalPngSprites(string name)
        {
            var scene=EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/"+name+".unity",OpenSceneMode.Single);
            var hud=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren(RuntimeReflection.RuntimeType("SashimiBoy.Stage01SalmonHUD"),true)).Single();
            Assert.That(Get(hud,"judgementImage"),Is.Not.Null);Assert.That(Get(hud,"judgementDetail"),Is.Not.Null);
            var visuals=(IList)Get(Get(hud,"ownerJudgementVisuals"),"visuals");
            Assert.That(visuals.Count,Is.EqualTo(4));
            foreach(var item in visuals)
            {
                var sprite=(Sprite)Get(item,"sprite");
                string path=AssetDatabase.GetAssetPath(sprite);
                Assert.That(path,Is.EqualTo("Assets/_SashimiBoy/Art/Source/UI/Judgement/"+Get(item,"displayLabel").ToString().ToLowerInvariant()+".png"));
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                Assert.That(importer.textureType,Is.EqualTo(TextureImporterType.Sprite));Assert.That(importer.alphaIsTransparency,Is.True);
            }
        }

        [TestCase("Misuk")][TestCase("Cheolsu")][TestCase("Seongho")][TestCase("Minjae")]
        public void AppliedNpc_HasOwnerHeadAndBody_WithPositiveScaleAndMaterials(string id)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_SashimiBoy/Art/Generated/DayWorld/PF_"+id+".prefab");
            Assert.That(prefab,Is.Not.Null);
            var npc=prefab.GetComponent(RuntimeReflection.RuntimeType("SashimiBoy.DayWorldNpc"));
            Assert.That(Get(npc,"faceAnchor"),Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<Renderer>().Length,Is.GreaterThanOrEqualTo(2));
            Assert.That(prefab.GetComponentInChildren<SkinnedMeshRenderer>().bones.Length,Is.EqualTo(2),"The neck boundary stays attached during idle head movement.");
            foreach(var t in prefab.GetComponentsInChildren<Transform>()) Assert.That(Mathf.Min(t.localScale.x,t.localScale.y,t.localScale.z),Is.GreaterThan(0f));
            foreach(var renderer in prefab.GetComponentsInChildren<Renderer>()) foreach(var material in renderer.sharedMaterials) Assert.That(material,Is.Not.Null);
        }

        [TestCase("Bootstrap")][TestCase("Street")][TestCase("FishShopDialogue")][TestCase("EquipmentShop")][TestCase("Club")][TestCase("KevinHome")]
        public void AppliedWorldScene_HasSingleIntegrationRoot_AndNoMissingSerializedReferences(string name)
        {
            var scene=EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/"+name+".unity",OpenSceneMode.Single);
            Assert.That(scene.GetRootGameObjects().Count(g=>g.name=="DayWorld_Integration"),Is.EqualTo(1));
            var world=scene.GetRootGameObjects().Single(g=>g.name=="DayWorld_Integration").transform;
            if(name=="Street" || name=="FishShopDialogue" || name=="EquipmentShop" || name=="Club")
            {
                var doors=world.Find("MatchingDoors");
                Assert.That(doors,Is.Not.Null,name);
                foreach(Transform door in doors)
                {
                    string id=door.name.Replace("SharedDoor_","");
                    Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(door.gameObject),
                        Is.EqualTo("Assets/_SashimiBoy/Art/Generated/DayWorld/StreetDoors/PF_SharedDoor_"+id+".prefab"));
                    Assert.That(door.GetComponentsInChildren<MeshFilter>().Any(f=>f.sharedMesh.vertexCount>100),Is.True);
                }
            }
            foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject),Is.Zero,name+"/"+t.name);
                foreach(var component in t.GetComponents<Component>())
                {
                    if(component==null)continue;
                    using(var serialized=new SerializedObject(component))
                    {
                        var property=serialized.GetIterator();
                        while(property.Next(true))if(property.propertyType==SerializedPropertyType.ObjectReference && property.objectReferenceValue==null)
                            Assert.That(property.objectReferenceInstanceIDValue,Is.Zero,name+"/"+t.name+"/"+property.propertyPath);
                    }
                }
            }
        }
    }
}
