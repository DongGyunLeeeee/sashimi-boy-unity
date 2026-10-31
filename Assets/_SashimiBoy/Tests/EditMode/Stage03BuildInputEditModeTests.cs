using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
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
    }
}
