using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SashimiBoy.Tests
{
    public sealed class CurrentGameRevisionEditModeTests
    {
        [Test]
        public void Cheolsu_ActualSeatedMeshContactsTheChair_AndFacesItsTable()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/FishShopDialogue.unity", OpenSceneMode.Single);
            var root = scene.GetRootGameObjects().Single(g => g.name == "DayWorld_Integration").transform;
            var customer = root.GetComponentsInChildren<Transform>(true).Single(t => t.GetComponent(RuntimeReflection.RuntimeType("SashimiBoy.DayWorldNpc")) is Component npc && (string)RuntimeReflection.GetField(npc, "npcId") == "cheolsu");
            Bounds seat = root.Find("VenueAssets/DiningSeat_FrontRight-1").GetComponent<Renderer>().bounds;
            Vector3 towardTable = root.Find("VenueAssets/DiningTop_FrontRight").position - customer.position;
            towardTable.y = 0f;
            Assert.That(Vector3.Dot(customer.forward, towardTable.normalized), Is.GreaterThan(.99f));
            var body = customer.GetComponentInChildren<MeshFilter>(true);
            float contactY = float.PositiveInfinity;
            int contactSamples = 0;
            foreach (Vector3 local in body.sharedMesh.vertices)
            {
                // Read the actual central pelvis/thigh underside, independently of authoring offsets.
                if (Mathf.Abs(local.x) > .18f || Mathf.Abs(local.z) > .08f || local.y < .4f || local.y > .65f) continue;
                Vector3 world = body.transform.TransformPoint(local);
                contactY = Mathf.Min(contactY, world.y);
                Assert.That(world.x, Is.InRange(seat.min.x, seat.max.x));
                Assert.That(world.z, Is.InRange(seat.min.z, seat.max.z));
                contactSamples++;
            }
            Assert.That(contactSamples, Is.GreaterThan(100));
            Assert.That(contactY, Is.EqualTo(seat.max.y).Within(.003f), "The mesh must sit on the real chair, not hover above it.");
        }

        [Test]
        public void Home_HoldsTenExistingSizeInstruments_WithSeparateBedAndCenterAisle()
        {
            var scene=EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/KevinHome.unity",OpenSceneMode.Single);
            var root=scene.GetRootGameObjects().Single(g=>g.name=="DayWorld_Integration").transform;
            var slots=root.Find("HomeEquipmentSlots").Cast<Transform>().ToArray();
            Assert.That(slots.Length,Is.EqualTo(10));
            var floor=root.Find("HomeFloor").GetComponent<Renderer>().bounds;
            var bed=root.Find("Bed").GetComponent<Collider>().bounds;
            var footprints=slots.Select(s=>new Bounds(s.position+Vector3.up*.8f,
                Mathf.Abs(s.forward.x)>.5f ? new Vector3(1.6f,1.6f,2f) : new Vector3(2f,1.6f,1.6f))).ToArray();
            for(int i=0;i<footprints.Length;i++)
            {
                var b=footprints[i];
                Assert.That(b.min.x,Is.GreaterThan(floor.min.x+.15f));Assert.That(b.max.x,Is.LessThan(floor.max.x-.15f));
                Assert.That(b.min.z,Is.GreaterThan(floor.min.z+.1f));Assert.That(b.max.z,Is.LessThan(floor.max.z-.1f));
                Assert.That(b.Intersects(bed),Is.False,"Instrument reserve intersects bed: "+i);
                for(int j=i+1;j<footprints.Length;j++)Assert.That(b.Intersects(footprints[j]),Is.False,"Instrument reserves overlap: "+i+","+j);
                Vector3 approach=slots[i].position-slots[i].forward*1.3f+Vector3.up*.9f;
                Assert.That(footprints.Where((_,j)=>j!=i).Any(other=>other.Contains(approach)),Is.False,"Approach blocked by another instrument.");
            }
            foreach(string name in new[]{"ElectronicDrumKit","MidiKeyboardController","ModularSynthesizer","EffectsPedals","GuitarPedal","Loudspeaker","SpeakerBox","StageSpotlight","StackedSpeaker","StereoSpeaker","VintageSpeaker"})
            {
                var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_SashimiBoy/Art/Generated/DayWorld/Venues/PF_"+name+".prefab");
                var bounds=(Bounds)RuntimeReflection.InvokeStatic("SashimiBoy.EditorTools.DayWorldAssetAuthoring","HomeGeometryBounds",asset);
                Assert.That(bounds.size.x,Is.LessThan(2f),name);Assert.That(bounds.size.z,Is.LessThan(1.6f),name);
            }
            for(float z=-4.4f;z<2f;z+=.25f)
                Assert.That(footprints.Any(b=>b.Contains(new Vector3(0,.8f,z))),Is.False,"Center entrance aisle must stay clear.");
        }

        [Test]
        public void Street_UsesOriginalFacadeEntrances_WithoutAnExtraDoorInFront()
        {
            var scene=EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/Street.unity",OpenSceneMode.Single);
            var root=scene.GetRootGameObjects().Single(g=>g.name=="DayWorld_Integration").transform;
            Assert.That(root.Find("MatchingDoors").gameObject.activeInHierarchy,Is.False);
            foreach(string id in new[]{"FishShop","EquipmentShop","Club"})
            {
                Assert.That(root.Find("PF_"+id).gameObject.activeInHierarchy,Is.True);
                string room=id=="FishShop"?"FishShopDialogue":id;
                var door=root.Find("Door_To_"+room+"_DayWorld");
                Assert.That(door.GetComponent<Renderer>().enabled,Is.False);
                Assert.That(door.GetComponent<Collider>().enabled,Is.True);
            }
        }

        [TestCase("FishShopDialogue","FishShop")]
        [TestCase("EquipmentShop","EquipmentShop")]
        [TestCase("Club","Club")]
        public void InteriorDoor_OverlapsTheWallOpeningAndHeader(string room,string id)
        {
            var scene=EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/"+room+".unity",OpenSceneMode.Single);
            var root=scene.GetRootGameObjects().Single(g=>g.name=="DayWorld_Integration").transform;
            var door=root.Find("MatchingDoors/SharedDoor_"+id);
            var shell=root.Find("InteriorShell");
            Assert.That(door.gameObject.activeInHierarchy,Is.True);
            var jamb=door.Find("Jamb_Left").GetComponent<Renderer>().bounds;
            var side=shell.Find("Wall_EntryRight").GetComponent<Renderer>().bounds;
            Assert.That(jamb.Intersects(side),Is.True,"The visible door frame must meet the wall.");
            Assert.That(door.Find("Header").GetComponent<Renderer>().bounds.Intersects(shell.Find("DoorLintel").GetComponent<Renderer>().bounds),Is.True);
        }

        [TestCase("head")][TestCase("body")][TestCase("bone")]
        [TestCase("fillet")][TestCase("fillet_half")][TestCase("piece")]
        public void Rockfish_UsesAllFourOwnerSurfaceMaps_WithLinearDataImports(string part)
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/_SashimiBoy/Art/Generated/Stage02Rockfish/MAT_Rockfish_Owner_"+part+".mat");
            Assert.That(material.shader.name,Is.EqualTo("SashimiBoy/Stage02RockfishSurface"));
            Assert.That(ShaderUtil.ShaderHasError(material.shader),Is.False);
            Assert.That(material.shader.isSupported,Is.True);
            string prefix="Assets/_SashimiBoy/Art/Source/DayWorld/Fish/Stage02/rockfish_"+part+"/rockfish_"+part;
            foreach(var pair in new[]{new[]{"_MainTex","basecolor"},new[]{"_BumpMap","normal"},new[]{"_RoughnessMap","roughness"},new[]{"_MetallicMap","metallic"}})
            {
                string path=prefix+"_"+pair[1]+".JPEG";
                Assert.That(AssetDatabase.GetAssetPath(material.GetTexture(pair[0])),Is.EqualTo(path));
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                if(pair[1]!="basecolor")Assert.That(importer.sRGBTexture,Is.False,"Surface data must not receive a colour-space conversion.");
                if(pair[1]=="normal")Assert.That(importer.textureType,Is.EqualTo(TextureImporterType.NormalMap));
            }
            Assert.That(material.GetVector("_CutPlane").w,Is.EqualTo(part=="fillet_half"?1f:-1f));
        }

        [TestCase(1,"SamplePackDrumKit")]
        [TestCase(2,"DawSoftware")]
        public void Sleep_SavesPendingClear_ContinueIsIdempotent(int day,string equipment)
        {
            var save=RuntimeReflection.InvokeStatic("SashimiBoy.SaveData","CreateNew");
            var progress=RuntimeReflection.GetField(save,"dayWorld");
            RuntimeReflection.SetField(progress,"active",true);RuntimeReflection.SetField(progress,"day",day);
            RuntimeReflection.SetField(progress,"beat",Enum.Parse(RuntimeReflection.RuntimeType("SashimiBoy.DayWorldBeat"),"Sleep"));
            ((IList)RuntimeReflection.GetField(progress,"placedEquipment")).Add(equipment);
            ((IList)RuntimeReflection.GetField(progress,"practicedDays")).Add(day);
            Assert.That(RuntimeReflection.InvokeStatic("SashimiBoy.DayWorldRules","Sleep",save),Is.EqualTo(true));
            Assert.That(RuntimeReflection.GetField(progress,"pendingStageClear"),Is.EqualTo(day));
            string json=JsonUtility.ToJson(save);
            var restored=JsonUtility.FromJson(json,save.GetType());
            Assert.That(RuntimeReflection.InvokeStatic("SashimiBoy.DayWorldRules","Sleep",restored),Is.EqualTo(false));
            Assert.That(JsonUtility.ToJson(restored),Is.EqualTo(json));
            Assert.That(RuntimeReflection.InvokeStatic("SashimiBoy.DayWorldRules","ContinueAfterStageClear",restored),Is.EqualTo(day==1));
            var after=RuntimeReflection.GetField(restored,"dayWorld");
            Assert.That(RuntimeReflection.GetField(after,"pendingStageClear"),Is.EqualTo(day==1?0:2));
            Assert.That(RuntimeReflection.GetField(after,"nightsSlept"),Is.EqualTo(day));
            Assert.That(RuntimeReflection.GetField(after,"beat").ToString(),Is.EqualTo(day==1?"MorningConversation":"Complete"));
            string once=JsonUtility.ToJson(restored);
            Assert.That(RuntimeReflection.InvokeStatic("SashimiBoy.DayWorldRules","ContinueAfterStageClear",restored),Is.EqualTo(false));
            Assert.That(JsonUtility.ToJson(restored),Is.EqualTo(once));
        }
    }
}
