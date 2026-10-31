using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SashimiBoy.Tests
{
    public sealed class Stage03NarrativeWorldEditModeTests
    {
        private static object Field(object value, string name) => RuntimeReflection.GetField(value, name);
        private static object[] Lines(string id) =>
            ((IEnumerable)RuntimeReflection.InvokeStatic("SashimiBoy.DayWorldScenario", "Lines", id)).Cast<object>().ToArray();
        private static string Text(object line) => (string)Field(line, "text");
        private static string Kind(object line) => Field(line, "kind").ToString();

        [Test]
        public void OwnerDay3Transcript_HasFiftyEntries_WithDistinctSpeechThoughtAndAction()
        {
            var junseok = Lines("junseok"); var cheolsu = Lines("cheolsu_day3"); var jaehoon = Lines("jaehoon");
            Assert.That(junseok.Length, Is.EqualTo(18));
            Assert.That(cheolsu.Length, Is.EqualTo(13));
            Assert.That(jaehoon.Length, Is.EqualTo(19));
            var all = junseok.Concat(cheolsu).Concat(jaehoon).ToArray();
            Assert.That(all.Count(line => Kind(line) == "Speech"), Is.EqualTo(45));
            Assert.That(all.Count(line => Kind(line) == "Thought"), Is.EqualTo(4));
            Assert.That(all.Count(line => Kind(line) == "Action"), Is.EqualTo(1));
            Assert.That(all.All(line => (int)Field(line, "sourcePage") == 0), Is.True,
                "The new source is the Owner chat, not a fabricated PDF page.");
            CollectionAssert.AreEqual(new[] {
                "(직장인의 사시미는 Ctrl, Alt… 뭐 그런 것들이었다.)",
                "(그 ‘아’ 뭐냐.)",
                "(갑자기 내 손에서 광어 냄새가 심해진 것 같다.)",
                "(‘아직’?)"
            }, all.Where(line => Kind(line) == "Thought").Select(Text).ToArray());
            var handshake = all.Single(line => Kind(line) == "Action");
            Assert.That((string)Field(handshake, "actionId"), Is.EqualTo("handshake"));
            Assert.That(Text(handshake), Is.EqualTo("둘이 악수한다."));
            Assert.That((string)Field(handshake, "speaker"), Is.Null.Or.Empty);
            Assert.That((float)Field(handshake, "autoAdvanceDelay"), Is.GreaterThan(0f));
            Assert.That(Text(junseok[0]), Is.EqualTo("어 케빈. 오랜만이다."));
            Assert.That(Text(cheolsu[1]), Is.EqualTo("음악 한다며."));
            Assert.That(Text(jaehoon[0]), Is.EqualTo("케빈?"));
            Assert.That(all.Any(line => Text(line).Contains("공연 잡았다는")), Is.False,
                "Future encounters must not leak into Day3.");
        }

        [Test]
        public void ExistingDayOneAndTwoTranscripts_StaySeparateFromCheolsuSecondEncounter()
        {
            foreach (string id in new[] { "misuk", "cheolsu", "seongho", "minjae" })
            {
                var lines = Lines(id);
                Assert.That(lines.Length, Is.GreaterThan(0), id);
                Assert.That(lines.All(line => (int)Field(line, "sourcePage") > 0), Is.True, id);
            }
            Assert.That(Lines("cheolsu").Any(line => Text(line) == "오늘 회가 두껍다."), Is.True);
            Assert.That(Lines("cheolsu").Any(line => Text(line) == "음악 한다며."), Is.False);
            Assert.That(Lines("cheolsu_day3").Any(line => Text(line) == "오늘 회가 두껍다."), Is.False);
            Assert.That(Lines("junseok_four").Length, Is.Zero);
            Assert.That(Lines("jaehoon_two").Length, Is.Zero);
        }

        [Test]
        public void AuthoredStreet_UsesTheTwoSuppliedNpcPrefabs_WithIndependentConversationAnchors()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/Street.unity", OpenSceneMode.Single);
            var world = scene.GetRootGameObjects().Single(g => g.name == "DayWorld_Integration").transform;
            var npcs = world.GetComponentsInChildren(RuntimeReflection.RuntimeType("SashimiBoy.DayWorldNpc"), true).Cast<Component>().ToArray();
            foreach (string id in new[] { "junseok", "jaehoon" })
            {
                var npc = npcs.Single(value => (string)Field(value, "npcId") == id);
                Assert.That((int)Field(npc, "day"), Is.EqualTo(3));
                Assert.That(Field(npc, "runner"), Is.Not.Null);
                var face = (Transform)Field(npc, "faceAnchor");
                Assert.That(face, Is.Not.Null);
                Assert.That(face.position.y, Is.InRange(1.5f, 1.75f));
                Assert.That(npc.GetComponent<BoxCollider>().size, Is.EqualTo(new Vector3(.65f, 1.75f, .55f)));
                var visual = npc.transform.Find("ProvidedModel");
                var source = PrefabUtility.GetCorrespondingObjectFromSource(visual.gameObject);
                string character = id == "junseok" ? "Junseok" : "Jaehoon";
                Assert.That(AssetDatabase.GetAssetPath(source),
                    Is.EqualTo("Assets/_SashimiBoy/Art/Generated/Stage03NpcAssets/Prefabs/PF_" + character + ".prefab"));
                Assert.That(visual.localScale, Is.EqualTo(Vector3.one));
                Assert.That(visual.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThan(0));
            }
            var director = world.GetComponent(RuntimeReflection.RuntimeType("SashimiBoy.DayWorldSceneDirector"));
            CollectionAssert.AreEquivalent(npcs, ((IEnumerable)Field(director, "npcs")).Cast<Component>());
        }

        [Test]
        public void PluginStation_UsesThirdReservedSlot_WithoutMovingExistingStationsOrBed()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/KevinHome.unity", OpenSceneMode.Single);
            var world = scene.GetRootGameObjects().Single(g => g.name == "DayWorld_Integration").transform;
            var stations = world.GetComponentsInChildren(RuntimeReflection.RuntimeType("SashimiBoy.HomeEquipmentStation"), true).Cast<Component>().ToArray();
            var plugin = stations.Single(value => Field(value, "equipmentId").ToString() == "Plugin");
            Assert.That(plugin.transform.position, Is.EqualTo(world.Find("HomeEquipmentSlots/Slot_03").position));
            Assert.That(plugin.GetComponent<BoxCollider>().isTrigger, Is.True);
            foreach (string name in new[] { "playerPosition", "rightHandTarget", "leftHandTarget", "practiceCamera", "equipmentVisual" })
                Assert.That(Field(plugin, name), Is.Not.Null, name);
            foreach (string id in new[] { "SamplePackDrumKit", "DawSoftware" })
                Assert.That(stations.Count(value => Field(value, "equipmentId").ToString() == id), Is.EqualTo(1));
            Assert.That(world.Find("Bed").position, Is.EqualTo(new Vector3(3.8f, .61f, 3.25f)));
            Assert.That(world.Find("HomeFloor").localScale, Is.EqualTo(new Vector3(10.8f, .1f, 9.2f)));
        }
    }
}
