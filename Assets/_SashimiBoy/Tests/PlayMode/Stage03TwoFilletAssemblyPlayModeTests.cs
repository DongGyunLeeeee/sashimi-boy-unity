#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SashimiBoy.Tests
{
    // Real generated bindings and part presentation in a disposable preview
    // clone. The controlled delta clock is not a rhythm/input/Player playthrough.
    public sealed class Stage03TwoFilletAssemblyPlayModeTests
    {
        private const string Prefab = "Assets/_SashimiBoy/Art/Generated/Stage03Flounder/Prefabs/PF_Stage03_FlounderAssembly.prefab";
        private Scene preview;
        private GameObject root;
        private Component view, head;
        private Component[] skins, whole, legacy, allParts;
        private Transform bodyPivot;
        private int originalPartCount;
        private readonly Dictionary<Transform, Pose> authored = new Dictionary<Transform, Pose>();
        private Quaternion authoredBodyRotation;
        private sealed class Pose
        {
            public Transform parent;
            public Vector3 position, scale;
            public Quaternion rotation;
        }
        static object Get(object target, string name) => RuntimeReflection.GetField(target, name);
        static object Call(object target, string name, params object[] args) => RuntimeReflection.Invoke(target, name, args);
        static bool Bool(object target, string name) => (bool)Call(target, "get_" + name);
        static int Int(object target, string name) => (int)Call(target, "get_" + name);
        bool Command(string name, params object[] args) => (bool)Call(view, name, args);
        Component Stored(int side) => (Component)Call(view, "GetStoredWholeFillet", side);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            preview = EditorSceneManager.NewPreviewScene();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            Assert.That(source, Is.Not.Null);
            root = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
            RuntimeReflection.InvokeStatic("SashimiBoy.EditorTools.FlounderPreviewDiagnostics", "PrepareRuntimeClone", root);
            Assert.That(root.GetComponentsInChildren<Transform>(true).Any(PrefabUtility.IsPartOfAnyPrefab), Is.False);
            root.transform.SetPositionAndRotation(new Vector3(1.7f, 2.1f, -.5f), Quaternion.Euler(0f, 37f, 0f));
            root.transform.localScale = Vector3.one * 1.2f;
            view = root.GetComponent(RuntimeReflection.RuntimeType("SashimiBoy.FlounderAssemblyView"));
            Assert.That(view, Is.Not.Null);
            ((Behaviour)view).enabled = false; // One explicit presentation clock owns this fixture.
            Assert.That(Bool(view, "TwoFilletPreparation"), Is.False, "Generated asset-library default must remain legacy.");
            Call(view, "BeginTwoFilletPreparation");
            head = (Component)Get(view, "head");
            skins = ((Array)Get(view, "skins")).Cast<Component>().ToArray();
            whole = ((Array)Get(view, "wholeFillets")).Cast<Component>().ToArray();
            legacy = ((Array)Get(view, "fillets")).Cast<Component>().ToArray();
            Assert.That(skins.Length, Is.EqualTo(2)); Assert.That(whole.Length, Is.EqualTo(2)); Assert.That(legacy.Length, Is.EqualTo(4));
            allParts = legacy.Concat(skins).Concat(whole).Append(head).ToArray();
            Assert.That(allParts.Distinct().Count(), Is.EqualTo(9));
            foreach (Component part in allParts)
            {
                RuntimeReflection.SetField(part, "separationDuration", .5f);
                authored.Add(part.transform, new Pose { parent = part.transform.parent, position = part.transform.localPosition,
                    rotation = part.transform.localRotation, scale = part.transform.localScale });
            }
            bodyPivot = (Transform)Get(view, "bodyPivot"); authoredBodyRotation = bodyPivot.localRotation;
            RuntimeReflection.SetField(view, "turnOverDuration", .5f);
            originalPartCount = root.GetComponentsInChildren(RuntimeReflection.RuntimeType("SashimiBoy.FlounderAssemblyPart"), true).Length;
            AssertReset();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            authored.Clear(); root = null; view = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator OrderedSkinAndWholeSideSeparation_RejectsDuplicatesAndKeepsDiscardedSkinOutOfEdibleStorage()
        {
            RuntimeReflection.SetField(head, "separationDuration", 10f);
            Assert.That(Command("TryRemoveSkin", 0), Is.False);
            Assert.That(Command("TryRemoveSkin", -1), Is.False);
            Assert.That(Command("TryRemoveSkin", 2), Is.False);
            Assert.That(Command("TrySeparateWholeFillet", 0), Is.False);
            Assert.That(Command("TryTurnOver"), Is.False);
            Assert.That(Command("TrySeparateFillet", 0), Is.False, "The old quarter command must not advance the selected two-side flow.");
            Assert.That(Stored(-1), Is.Null); Assert.That(Stored(0), Is.Null); Assert.That(Stored(2), Is.Null);
            Assert.That(Command("TryRemoveHead"), Is.True);
            Assert.That(Command("TryRemoveHead"), Is.False);
            AssertBody("HeadRemovedFish");
            Assert.That(Command("TryRemoveSkin", 1), Is.False);
            Assert.That(Command("TrySeparateWholeFillet", 0), Is.False);
            Assert.That(Command("TryRemoveSkin", 0), Is.True);
            Assert.That(Command("TryRemoveSkin", 0), Is.False);
            Assert.That(Int(view, "RemovedSkinCount"), Is.EqualTo(1));
            AssertBody("FilletRemoved1");
            Assert.That(Call(view, "get_ActiveBodyObject"), Is.SameAs(((GameObject[])Get(view, "skinnedBodyStates"))[0]));
            Advance(.6f);
            AssertStored(skins[0], false);
            Assert.That(Stored(0), Is.Null, "A stored discarded skin must never satisfy an edible whole-side lookup.");
            Assert.That(Command("TryTurnOver"), Is.False);
            Assert.That(Command("TrySeparateWholeFillet", 1), Is.False);
            Assert.That(Command("TrySeparateWholeFillet", 0), Is.True);
            Assert.That(Command("TrySeparateWholeFillet", 0), Is.False);
            Assert.That(Int(view, "SeparatedWholeFilletCount"), Is.EqualTo(1));
            AssertBody("FilletRemoved2");
            Advance(.6f);
            AssertStored(whole[0], true); Assert.That(Stored(0), Is.SameAs(whole[0]));
            Assert.That(Stored(1), Is.Null);
            Assert.That(Command("TryRemoveSkin", 1), Is.False);
            Assert.That(Command("TryTurnOver"), Is.True);
            Assert.That(Command("TryTurnOver"), Is.False);
            Advance(.1f);
            Assert.That(Bool(view, "IsTurningOver"), Is.True);
            Assert.That(Command("TryRemoveSkin", 1), Is.False, "Back skin cannot detach during the turn.");
            Advance(.6f);
            Assert.That(Bool(view, "IsBackFacing"), Is.True);
            Assert.That(Bool(view, "IsTurningOver"), Is.False);
            Assert.That(Command("TrySeparateWholeFillet", 1), Is.False);
            Assert.That(Command("TryRemoveSkin", 1), Is.True);
            Assert.That(Command("TryRemoveSkin", 1), Is.False);
            AssertBody("FilletRemoved3");
            Assert.That(Call(view, "get_ActiveBodyObject"), Is.SameAs(((GameObject[])Get(view, "skinnedBodyStates"))[1]));
            Advance(.6f);
            AssertStored(skins[1], false); Assert.That(Stored(1), Is.Null);
            Assert.That(Command("TrySeparateWholeFillet", 1), Is.True);
            Assert.That(Command("TrySeparateWholeFillet", 1), Is.False);
            Assert.That(Int(view, "RemovedSkinCount"), Is.EqualTo(2));
            Assert.That(Int(view, "SeparatedWholeFilletCount"), Is.EqualTo(2));
            Advance(.6f);
            AssertStored(whole[1], true); Assert.That(Stored(1), Is.SameAs(whole[1]));
            Assert.That(Bool(head, "IsStored"), Is.False);
            AssertBody("FilletRemoved4");
            Advance(10f);
            AssertBody("Hidden"); Assert.That(Bool(view, "HasBody"), Is.False);
            Assert.That(Call(view, "get_ActiveBodyObject"), Is.Null);
            AssertStored(head, true);
            Assert.That(legacy.All(p => !p.gameObject.activeSelf && !Bool(p, "IsDetached")), Is.True);
            Assert.That(skins.Concat(whole).All(p => Bool(p, "IsStored")), Is.True);
            Assert.That(skins.All(p => p != Stored(0) && p != Stored(1)), Is.True);
            AssertPartCount();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ResetDuringSkinFlightAndHalfTurn_RestoresAllAuthoredPartsAndRetainsTheSelectedMode()
        {
            foreach (bool duringTurn in new[] { false, true })
            {
                Assert.That(Command("TryRemoveHead"), Is.True);
                Assert.That(Command("TryRemoveSkin", 0), Is.True);
                if (duringTurn)
                {
                    Assert.That(Command("TrySeparateWholeFillet", 0), Is.True);
                    Assert.That(Command("TryTurnOver"), Is.True);
                }
                Advance(.1f);
                Assert.That(Bool(skins[0], "IsMoving"), Is.True);
                if (duringTurn) Assert.That(Bool(view, "IsTurningOver"), Is.True);
                Call(view, "ResetAssembly");
                AssertReset();
                Advance(15f); // A cancelled flight/turn cannot later hide or detach the reset fish.
                AssertReset();
                yield return null;
            }
        }

        void Advance(float delta)
        {
            Call(view, "AdvancePresentation", delta);
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(Finite(transform.position) && Finite(transform.localScale), Is.True, transform.name);
                Assert.That(Mathf.Min(transform.localScale.x, transform.localScale.y, transform.localScale.z), Is.GreaterThan(0f), transform.name);
            }
            AssertPartCount();
        }
        void AssertStored(Component part, bool visible)
        {
            var storage = (Transform)Get(part, "storageAnchor");
            Assert.That(Bool(part, "IsDetached") && Bool(part, "IsStored") && !Bool(part, "IsMoving"), Is.True, part.name);
            Assert.That(part.transform.parent, Is.SameAs(storage), part.name);
            Assert.That(Vector3.Distance(part.transform.position, storage.position), Is.LessThan(.00001f), part.name);
            Assert.That(part.gameObject.activeInHierarchy, Is.EqualTo(visible), part.name);
        }
        void AssertBody(string expected) => Assert.That(Call(view, "get_BodyState").ToString(), Is.EqualTo(expected));
        void AssertPartCount() => Assert.That(root.GetComponentsInChildren(RuntimeReflection.RuntimeType("SashimiBoy.FlounderAssemblyPart"), true).Length,
            Is.EqualTo(originalPartCount), "Repeated commands must not instantiate duplicate skin or edible pieces.");
        void AssertReset()
        {
            Assert.That(Bool(view, "TwoFilletPreparation"), Is.True);
            AssertBody("WholeFish");
            Assert.That(Int(view, "RemovedSkinCount"), Is.Zero); Assert.That(Int(view, "SeparatedWholeFilletCount"), Is.Zero);
            Assert.That(Int(view, "DetachedFilletCount"), Is.Zero);
            Assert.That(Bool(view, "HeadRemoved") || Bool(view, "IsBackFacing") || Bool(view, "IsTurningOver"), Is.False);
            Assert.That(Quaternion.Angle(bodyPivot.localRotation, authoredBodyRotation), Is.LessThan(.001f));
            Assert.That(Call(view, "get_ActiveBodyObject"), Is.SameAs(((GameObject[])Get(view, "bodyStates"))[0]));
            Assert.That(((GameObject[])Get(view, "skinnedBodyStates")).All(go => !go.activeSelf), Is.True);
            foreach (var pair in authored)
            {
                Assert.That(pair.Key.parent, Is.SameAs(pair.Value.parent));
                Assert.That(Vector3.Distance(pair.Key.localPosition, pair.Value.position), Is.LessThan(.00001f));
                Assert.That(Quaternion.Angle(pair.Key.localRotation, pair.Value.rotation), Is.LessThan(.001f));
                Assert.That(Vector3.Distance(pair.Key.localScale, pair.Value.scale), Is.LessThan(.00001f));
            }
            foreach (var part in allParts)
                Assert.That(Bool(part, "IsDetached") || Bool(part, "IsStored") || Bool(part, "IsMoving") || part.gameObject.activeSelf, Is.False, part.name);
            Assert.That(Stored(0), Is.Null); Assert.That(Stored(1), Is.Null);
            AssertPartCount();
        }
        static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}
#endif
