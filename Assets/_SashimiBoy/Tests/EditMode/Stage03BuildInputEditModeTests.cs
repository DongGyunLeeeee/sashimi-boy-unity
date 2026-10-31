using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor.SceneManagement;
namespace SashimiBoy.Tests
{
    public sealed class Stage03BuildInputEditModeTests
    {
        [Test]
        public void LfsPlaceholderIsRejected_WhileActualBinaryAndYamlAreNot()
        {
            string root=Path.Combine(Path.GetTempPath(),"SashimiLfsGuard-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string pointer=Path.Combine(root,"pointer.fbx"),binary=Path.Combine(root,"binary.fbx"),yaml=Path.Combine(root,"mesh.asset");
                File.WriteAllText(pointer,"version https://git-lfs.github.com/spec/v1\noid sha256:"+new string('a',64)+"\nsize 9999\n");
                File.WriteAllBytes(binary,new byte[]{75,97,121,100,97,114,97,0,0});
                File.WriteAllText(yaml,"%YAML 1.1\nMesh:\n");
                Assert.That(RuntimeReflection.InvokeStatic("SashimiBoy.EditorTools.Stage03BuildInputGuard","IsLfsPointer",pointer),Is.EqualTo(true));
                Assert.That(RuntimeReflection.InvokeStatic("SashimiBoy.EditorTools.Stage03BuildInputGuard","IsLfsPointer",binary),Is.EqualTo(false));
                Assert.That(RuntimeReflection.InvokeStatic("SashimiBoy.EditorTools.Stage03BuildInputGuard","IsLfsPointer",yaml),Is.EqualTo(false));
            }
            finally {foreach(string file in Directory.GetFiles(root))File.Delete(file);Directory.Delete(root);}
        }
        [Test]
        public void RealBuildInputsContainNoLfsPlaceholders()
        {
            Assert.DoesNotThrow(()=>RuntimeReflection.InvokeStatic("SashimiBoy.EditorTools.Stage03BuildInputGuard","ValidateMaterializedAssets"));
        }
        [Test]
        public void SuppliedFishAndWorkFilletCrossTheBlade_AndHeadCueIsInsideTheFish()
        {
            var scene=EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/Stage03_Flounder.unity",OpenSceneMode.Single);
            RuntimeReflection.InvokeStatic("SashimiBoy.EditorTools.Stage01PlayableAuthoring","ScanScene",scene);
            var controller=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MonoBehaviour>(true))
                .Single(c=>c.GetType().FullName=="SashimiBoy.Stage03GameController");
            var assembly=(Component)RuntimeReflection.GetField(controller,"assembly");
            var work=(Transform)RuntimeReflection.GetField(assembly,"filletWorkAnchor");
            var cooking=RuntimeReflection.GetField(controller,"cooking");
            var blade=(Transform)RuntimeReflection.GetField(cooking,"knifeRoot");
            Assert.That(Mathf.Abs(Vector3.Dot(blade.forward,work.forward)),Is.LessThan(.01f),"Knife length must cross the fillet length/cut normal.");
            Assert.That(Mathf.Abs(Vector3.Dot(blade.forward,assembly.transform.forward)),Is.LessThan(.01f),"The body uses the same longitudinal frame as slicing.");
            var whole=((GameObject[])RuntimeReflection.GetField(assembly,"bodyStates"))[0];
            var renderers=whole.GetComponentsInChildren<Renderer>(true);
            Bounds bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
            var cue=(Vector3)RuntimeReflection.Invoke(controller,"PreparationWorkPoint",0);
            Assert.That(cue.x,Is.InRange(bounds.min.x-.0001f,bounds.max.x+.0001f));
            Assert.That(cue.z,Is.InRange(bounds.min.z-.0001f,bounds.max.z+.0001f));
            Assert.That(cue.y,Is.EqualTo(bounds.max.y).Within(.0001f));
        }

        [Test]
        public void CompletedPlate_HasNoVisibleLegacyDemonstration_AndKeepsKnifeSlotsAndBaselineResultCard()
        {
            var scene=EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/Stage03_Flounder.unity",OpenSceneMode.Single);
            var transforms=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            foreach(var legacy in transforms.Where(t=>t.name=="Stage01_BossDemoVisuals"||t.name=="Stage01_BossKnife_Demo"))
                Assert.That(legacy.gameObject.activeInHierarchy,Is.False,"The old demonstration cannot occupy the serving plate or cover stored fillets: "+legacy.name);
            var scripts=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MonoBehaviour>(true)).ToArray();
            Assert.That(scripts.Any(c=>c!=null&&c.GetType().FullName=="SashimiBoy.BossDemoPresenter"),Is.False,"The copied presenter no longer has a timing owner.");
            var controller=scripts.Single(c=>c!=null&&c.GetType().FullName=="SashimiBoy.Stage03GameController");
            var cooking=RuntimeReflection.GetField(controller,"cooking");
            var knife=(Transform)RuntimeReflection.GetField(cooking,"knifeRoot");
            Assert.That(knife,Is.Not.Null);Assert.That(knife.name,Is.EqualTo("SashimiKnife_ProvidedAsset"));
            Assert.That(knife.gameObject.activeInHierarchy,Is.True,"Cleanup must preserve the actual cooking knife.");
            Assert.That(RuntimeReflection.GetField(cooking,"grip"),Is.Not.Null);
            Assert.That(RuntimeReflection.GetField(cooking,"contact"),Is.Not.Null);
            var plate=transforms.Single(t=>t.name=="CompletedFishPlate");
            var renderers=plate.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers,Is.Not.Empty);
            Bounds bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
            var slots=(Transform[])RuntimeReflection.GetField(controller,"plateSlots");
            Assert.That(slots.Length,Is.EqualTo(24));
            foreach(var slot in slots)
            {
                Assert.That(slot,Is.Not.Null);
                Assert.That(slot.position.x,Is.InRange(bounds.min.x,bounds.max.x));
                Assert.That(slot.position.z,Is.InRange(bounds.min.z,bounds.max.z));
                Assert.That(slot.position.y,Is.EqualTo(bounds.max.y+.006f).Within(.0001f));
            }
            var result=(GameObject)RuntimeReflection.GetField(controller,"resultPanel");
            var panel=result.GetComponent<RectTransform>();
            Assert.That(Vector2.Distance(panel.anchorMin,new Vector2(.77f,.75f)),Is.LessThan(.0001f));
            Assert.That(Vector2.Distance(panel.anchorMax,new Vector2(.77f,.75f)),Is.LessThan(.0001f),"Stage3 uses the shipped Stage1/2 right-side result card.");
            Assert.That(Vector2.Distance(panel.sizeDelta,new Vector2(650f,400f)),Is.LessThan(.0001f));
            var text=(Text)RuntimeReflection.GetField(controller,"resultText");
            Assert.That(text.fontSize,Is.EqualTo(30));
            Assert.That(Vector2.Distance(text.rectTransform.sizeDelta,new Vector2(590f,270f)),Is.LessThan(.0001f));
            var button=(RectTransform)result.transform.Find("Stage1Return");
            Assert.That(button,Is.Not.Null);
            Assert.That(button.anchoredPosition.y,Is.LessThan(text.rectTransform.anchoredPosition.y-text.rectTransform.sizeDelta.y*.5f),"The source return action remains below the result text.");
        }
    }
}
