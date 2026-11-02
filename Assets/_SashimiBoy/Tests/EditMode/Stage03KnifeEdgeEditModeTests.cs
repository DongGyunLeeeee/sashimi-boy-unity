using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SashimiBoy.Tests
{
    public sealed class Stage03KnifeEdgeEditModeTests
    {
        const string ScenePath = "Assets/_SashimiBoy/Scenes/Stage03_Flounder.unity";
        const string KnifePath = "Assets/_SashimiBoy/Art/Source/Environment/FishShop/Props/KitchenKnife/Models/KitchenKnife.fbx";

        [TearDown]
        public void TearDown() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        [Test]
        public void ActualSuppliedThinBevel_FacesFish_AndContactIsOnThatEdgeRatherThanTheSpine()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var cooking = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MonoBehaviour>(true))
                .Single(c => c != null && c.GetType().FullName == "SashimiBoy.Stage03CookingView");
            var knife = (Transform)RuntimeReflection.GetField(cooking, "knifeRoot");
            var contact = (Transform)RuntimeReflection.GetField(cooking, "contact");
            var grip = (Transform)RuntimeReflection.GetField(cooking, "grip");
            var points = ReadSuppliedPoints(knife);
            Assert.That(points.Count, Is.GreaterThan(0));
            // This checks the real mesh independently of the authoring report.
            var band = points.Where(p => Mathf.Abs(p.z - contact.localPosition.z) < .05f).ToArray();
            float min = band.Min(p => p.y), max = band.Max(p => p.y), width = max - min;
            var bevel = band.Where(p => p.y > max - width * .1f).ToArray();
            var spine = band.Where(p => p.y < min + width * .1f).ToArray();
            float sharpThickness = bevel.Max(p => p.x) - bevel.Min(p => p.x);
            float bluntThickness = spine.Max(p => p.x) - spine.Min(p => p.x);
            Assert.That(sharpThickness, Is.LessThan(bluntThickness * .7f), "The actual +Y edge tapers; -Y is the rounded spine.");
            var contactSection = points.Where(p => Mathf.Abs(p.z - contact.localPosition.z) < .005f).ToArray();
            float edgeY = contactSection.Max(p => p.y);
            Assert.That(contact.localPosition.y, Is.EqualTo(edgeY).Within(.001f), "Contact must be on the thin bevel, not merely an arbitrary low point.");
            Assert.That(Vector3.Dot(knife.up, Vector3.down), Is.GreaterThan(.999f), "The mesh's sharp +Y edge must face the fish.");
            Vector3 spineWorld = knife.TransformPoint(new Vector3(contact.localPosition.x, min, contact.localPosition.z));
            // These are real mesh/anchor distances in the knife frame. The
            // scene wrapper scales them (currently 0.27); local model metres
            // must not be compared with an unscaled world-space threshold.
            float localSpineGap = contact.localPosition.y - min;
            Assert.That(localSpineGap, Is.GreaterThan(0f));
            Vector3 expectedSpineOffset = knife.TransformVector(Vector3.down * localSpineGap);
            Vector3 actualSpineOffset = spineWorld - contact.position;
            Assert.That(Vector3.Dot(actualSpineOffset.normalized, Vector3.up), Is.GreaterThan(.999f),
                "The real dull spine stays above the cutting edge after the positive rotation and scene scale.");
            Assert.That(Vector3.Distance(actualSpineOffset, expectedSpineOffset), Is.LessThan(.000001f));
            Assert.That(contact.localPosition.y - grip.localPosition.y, Is.GreaterThan(0f));
            Vector3 expectedGripOffset = knife.TransformVector(grip.localPosition - contact.localPosition);
            Assert.That(expectedGripOffset.y, Is.GreaterThan(0f), "The supplied hilt must be above the real edge after rotation.");
            Assert.That(grip.position.y - contact.position.y, Is.EqualTo(expectedGripOffset.y).Within(.000001f),
                "The hilt's world clearance follows the actual source anchor and positive wrapper scale.");
            foreach (var node in knife.GetComponentsInChildren<Transform>(true))
                Assert.That(node.localScale.x > 0 && node.localScale.y > 0 && node.localScale.z > 0, Is.True);
        }

        [Test]
        public void RepeatedKnifeAuthoring_PreservesHiltAndLongAxis_WithoutAlternatingRollOrChangingSharedAssets()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var cooking = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MonoBehaviour>(true))
                .Single(c => c != null && c.GetType().FullName == "SashimiBoy.Stage03CookingView");
            var knife = (Transform)RuntimeReflection.GetField(cooking, "knifeRoot");
            var contact = (Transform)RuntimeReflection.GetField(cooking, "contact");
            var grip = (Transform)RuntimeReflection.GetField(cooking, "grip");
            string[] protectedPaths = { KnifePath, KnifePath + ".meta",
                "Assets/_SashimiBoy/Art/Generated/Prefabs/FishShop/PF_Prop_KitchenKnife.prefab",
                "Assets/_SashimiBoy/Scenes/Stage01_Salmon.unity", "Assets/_SashimiBoy/Scenes/Stage02_Rockfish.unity" };
            var before = protectedPaths.ToDictionary(p => p, Hash);
            Vector3 originalGrip = grip.localPosition, originalForward = knife.forward, originalPosition = knife.position;
            try
            {
                RuntimeReflection.InvokeStatic("SashimiBoy.EditorTools.Stage03KnifeAuthoring", "Bind", cooking);
                Quaternion firstRotation = knife.rotation;
                Vector3 firstContact = contact.localPosition;
                RuntimeReflection.InvokeStatic("SashimiBoy.EditorTools.Stage03KnifeAuthoring", "Bind", cooking);
                Assert.That(Quaternion.Angle(firstRotation, knife.rotation), Is.LessThan(.001f));
                Assert.That(Vector3.Distance(firstContact, contact.localPosition), Is.LessThan(.00001f));
                Assert.That(Vector3.Distance(originalGrip, grip.localPosition), Is.LessThan(.000001f));
                Assert.That(Vector3.Distance(originalForward, knife.forward), Is.LessThan(.000001f));
                Assert.That(Vector3.Distance(originalPosition, knife.position), Is.LessThan(.000001f));
                foreach (string path in protectedPaths) Assert.That(Hash(path), Is.EqualTo(before[path]), path);
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        }

        static List<Vector3> ReadSuppliedPoints(Transform knife)
        {
            var points = new List<Vector3>();
            foreach (var filter in knife.GetComponentsInChildren<MeshFilter>(true))
            {
                Assert.That(AssetDatabase.GetAssetPath(filter.sharedMesh), Is.EqualTo(KnifePath));
                using var data = MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh);
                using var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp);
                data[0].GetVertices(vertices);
                var matrix = knife.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (Vector3 p in vertices) points.Add(matrix.MultiplyPoint3x4(p));
            }
            return points;
        }
        static string Hash(string path)
        {
            using var hash = SHA256.Create(); using var input = File.OpenRead(path);
            return BitConverter.ToString(hash.ComputeHash(input));
        }
    }
}
