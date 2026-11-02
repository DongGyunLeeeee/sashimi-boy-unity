using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SashimiBoy.Tests
{
    // Draft only: not compiled or executed by the drafting agent. No scene,
    // imported model, input, rhythm, score, save or Stage03 progression fixture.
    public sealed class FlounderAssemblyPlayModeTests
    {
        private readonly List<GameObject> ownedRoots = new List<GameObject>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject root in ownedRoots)
                if (root != null) UnityEngine.Object.Destroy(root);
            ownedRoots.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator OrderedCutsRejectDuplicates_ThenKeepSpineUntilAllFivePartsAreStored()
        {
            Fixture f = CreateFixture();
            // Presentation durations are independently configurable. The last
            // fillet arriving first must not hide a still-travelling head.
            RuntimeReflection.SetField(f.Head, "separationDuration", 3f);
            int initialPartCount = PartCount(f);
            AssertBody(f, "WholeFish", 0);
            Assert.That(Int(f.View, "DetachedFilletCount"), Is.Zero);
            Assert.That(Command(f.View, "TrySeparateFillet", 0), Is.False);
            Assert.That(Command(f.View, "TryTurnOver"), Is.False);
            Assert.That(Command(f.View, "TrySeparateFillet", -1), Is.False);
            Assert.That(Command(f.View, "TrySeparateFillet", 4), Is.False);

            Assert.That(Command(f.View, "TryRemoveHead"), Is.True);
            Assert.That(Command(f.View, "TryRemoveHead"), Is.False);
            Assert.That(Command(f.View, "TrySeparateFillet", 1), Is.False);
            Assert.That(Command(f.View, "TrySeparateFillet", 0), Is.True);
            Assert.That(Command(f.View, "TrySeparateFillet", 0), Is.False);
            Assert.That(Command(f.View, "TryTurnOver"), Is.False);
            Assert.That(Command(f.View, "TrySeparateFillet", 2), Is.False);
            Assert.That(Command(f.View, "TrySeparateFillet", 1), Is.True);
            Assert.That(Int(f.View, "DetachedFilletCount"), Is.EqualTo(2));
            Assert.That(f.DetachedRoot.childCount, Is.EqualTo(3),
                "Duplicate commands must not create extra detached objects.");
            Assert.That(PartCount(f), Is.EqualTo(initialPartCount));
            Assert.That(Command(f.View, "TrySeparateFillet", 2), Is.False);

            Assert.That(Command(f.View, "TryTurnOver"), Is.True);
            Assert.That(Command(f.View, "TryTurnOver"), Is.False);
            Assert.That(Command(f.View, "TrySeparateFillet", 2), Is.False);
            Advance(f, 0.5f);
            Assert.That(Bool(f.View, "IsTurningOver"), Is.True);
            Assert.That(Bool(f.View, "IsBackFacing"), Is.False);
            Assert.That(Command(f.View, "TrySeparateFillet", 2), Is.False);
            Advance(f, 0.5f);
            Assert.That(Bool(f.View, "IsTurningOver"), Is.False);
            Assert.That(Bool(f.View, "IsBackFacing"), Is.True);

            Assert.That(Command(f.View, "TrySeparateFillet", 2), Is.True);
            Assert.That(Command(f.View, "TrySeparateFillet", 3), Is.True);
            AssertBody(f, "FilletRemoved4", 5);
            Assert.That(RuntimeReflection.Invoke(f.View, "GetStoredFillet", 3), Is.Null);
            Advance(f, 1f);
            Assert.That(Bool(f.Fillets[3], "IsStored"), Is.True);
            Assert.That(Bool(f.Head, "IsStored"), Is.False);
            AssertBody(f, "FilletRemoved4", 5);

            Advance(f, 1f);
            foreach (Component part in f.Parts)
            {
                Assert.That(Bool(part, "IsStored"), Is.True, part.name);
                Assert.That(Bool(part, "IsMoving"), Is.False, part.name);
            }
            AssertBody(f, "Hidden", -1);
            Assert.That(Bool(f.View, "HasBody"), Is.False);
            Assert.That(Int(f.View, "DetachedFilletCount"), Is.EqualTo(4));
            Assert.That(PartCount(f), Is.EqualTo(initialPartCount));
            for (int i = 0; i < 4; i++)
            {
                Assert.That(RuntimeReflection.Invoke(f.View, "GetStoredFillet", i),
                    Is.SameAs(f.Fillets[i]));
                Assert.That(f.Fillets[i].transform.parent, Is.SameAs(f.StorageAnchors[i]));
                AssertVector(f.Fillets[i].transform.position, f.StorageAnchors[i].position);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator DetachedFlightsIgnoreBodyFlip_AndInvalidDeltaDoesNotAdvancePresentation()
        {
            Fixture turning = CreateFixture();
            Fixture control = CreateFixture();
            DetachFront(turning);
            DetachFront(control);
            Assert.That(Command(turning.View, "TryTurnOver"), Is.True);
            Quaternion beforeBody = turning.BodyPivot.localRotation;
            Vector3[] beforePosition = WorldPositions(turning.Parts);
            Quaternion[] beforeRotation = WorldRotations(turning.Parts);

            foreach (float invalid in new[] { 0f, -1f, float.NaN,
                float.PositiveInfinity, float.NegativeInfinity })
            {
                Advance(turning, invalid);
                AssertRotation(turning.BodyPivot.localRotation, beforeBody);
                for (int i = 0; i < turning.Parts.Length; i++)
                {
                    AssertVector(turning.Parts[i].transform.position, beforePosition[i]);
                    AssertRotation(turning.Parts[i].transform.rotation, beforeRotation[i]);
                }
                Assert.That(Bool(turning.View, "IsTurningOver"), Is.True);
                Assert.That(Bool(turning.View, "IsBackFacing"), Is.False);
                Assert.That(Bool(turning.Head, "IsMoving"), Is.True);
                Assert.That(Bool(turning.Head, "IsStored"), Is.False);
            }

            // Same clocks and authored roots; only the first body rotates.
            // Comparing two real assemblies avoids reproducing the flight math.
            Advance(turning, 0.25f);
            Advance(control, 0.25f);
            Assert.That(Quaternion.Angle(turning.BodyPivot.localRotation,
                control.BodyPivot.localRotation), Is.GreaterThan(1f));
            for (int i = 0; i < 3; i++)
            {
                Assert.That(turning.Parts[i].transform.parent, Is.SameAs(turning.DetachedRoot));
                AssertVector(turning.Parts[i].transform.position, control.Parts[i].transform.position);
                AssertRotation(turning.Parts[i].transform.rotation, control.Parts[i].transform.rotation);
            }
            Assert.That(Bool(control.View, "IsTurningOver"), Is.False);
            Assert.That(Int(control.View, "DetachedFilletCount"), Is.EqualTo(2));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ResetDuringFlightsAndFlipRestoresAuthoredPosesUnderTransformedRoot()
        {
            Fixture f = CreateFixture();
            DetachFront(f);
            Assert.That(Command(f.View, "TryTurnOver"), Is.True);
            Advance(f, 0.25f);
            Assert.That(Bool(f.View, "IsTurningOver"), Is.True);
            Assert.That(Bool(f.Head, "IsMoving"), Is.True);
            Assert.That(Bool(f.Fillets[0], "IsMoving"), Is.True);
            Assert.That(Quaternion.Angle(f.BodyPivot.localRotation,
                f.AuthoredBody.Rotation), Is.GreaterThan(1f));

            RuntimeReflection.Invoke(f.View, "ResetAssembly");
            AssertReset(f);
            // Cancelled animations must not resume on the next clock update.
            Advance(f, 10f);
            AssertReset(f);
            Assert.That(Command(f.View, "TryTurnOver"), Is.False);
            Assert.That(Command(f.View, "TrySeparateFillet", 0), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InstantiatedAssemblyOwnsItsBindingsAndDoesNotAdvanceTheOtherInstance()
        {
            Fixture original = CreateFixture();
            original.Root.SetActive(false);
            GameObject cloneRoot = UnityEngine.Object.Instantiate(original.Root);
            ownedRoots.Add(cloneRoot);
            original.Root.SetActive(true);
            Component cloneView = cloneRoot.GetComponent(
                RuntimeReflection.RuntimeType("SashimiBoy.FlounderAssemblyView"));
            Fixture clone = ReadBoundFixture(cloneRoot, cloneView);
            ((Behaviour)cloneView).enabled = true;
            cloneRoot.SetActive(true); // All serialized clone bindings exist before Awake.
            ((Behaviour)cloneView).enabled = false;

            Assert.That(clone.Head, Is.Not.SameAs(original.Head));
            Assert.That(clone.BodyPivot, Is.Not.SameAs(original.BodyPivot));
            for (int i = 0; i < 4; i++)
            {
                Assert.That(clone.Fillets[i], Is.Not.SameAs(original.Fillets[i]));
                Assert.That(clone.StorageAnchors[i], Is.Not.SameAs(original.StorageAnchors[i]));
            }
            Assert.That(Command(original.View, "TryRemoveHead"), Is.True);
            Assert.That(Command(original.View, "TrySeparateFillet", 0), Is.True);
            AssertBody(clone, "WholeFish", 0);
            Assert.That(Bool(clone.View, "HeadRemoved"), Is.False);
            Assert.That(Int(clone.View, "DetachedFilletCount"), Is.Zero);

            Vector3 originalHeadPosition = original.Head.transform.position;
            Assert.That(Command(clone.View, "TryRemoveHead"), Is.True);
            Advance(clone, 1f);
            Assert.That(Bool(clone.Head, "IsStored"), Is.True);
            Assert.That(Bool(original.Head, "IsMoving"), Is.True);
            Assert.That(Bool(original.Head, "IsStored"), Is.False);
            AssertVector(original.Head.transform.position, originalHeadPosition);
            Assert.That(Int(original.View, "DetachedFilletCount"), Is.EqualTo(1));
            RuntimeReflection.Invoke(clone.View, "ResetAssembly");
            Assert.That(Bool(original.View, "HeadRemoved"), Is.True);
            Assert.That(Bool(original.Fillets[0], "IsDetached"), Is.True);
            yield return null;
        }

        private Fixture CreateFixture()
        {
            var root = new GameObject("FlounderAssemblyFixture");
            root.SetActive(false);
            ownedRoots.Add(root);
            root.transform.SetPositionAndRotation(new Vector3(4f, 2f, -3f),
                Quaternion.Euler(19f, 37f, 13f));
            root.transform.localScale = new Vector3(1.4f, 0.8f, 1.2f);
            Transform body = Child(root.transform, "BodyPivot");
            body.localPosition = new Vector3(0.2f, 0.1f, -0.3f);
            body.localRotation = Quaternion.Euler(11f, 23f, 7f);
            Transform detached = Child(root.transform, "DetachedParts");
            detached.localRotation = Quaternion.Euler(3f, 9f, 5f);
            Transform storage = Child(root.transform, "StorageAnchors");
            Transform headAnchor = Child(storage, "HeadStorage");
            headAnchor.localPosition = new Vector3(2f, 0.3f, -1f);
            headAnchor.localRotation = Quaternion.Euler(4f, 30f, 8f);
            var anchors = new Transform[4];
            for (int i = 0; i < anchors.Length; i++)
            {
                anchors[i] = Child(storage, "FilletStorage" + i);
                anchors[i].localPosition = new Vector3(2f + i * 0.4f, 0.2f, 0.5f);
                anchors[i].localRotation = Quaternion.Euler(5f, 15f * i, 9f);
            }
            var states = new GameObject[6];
            string[] names = { "WholeFish", "HeadRemovedFish", "FilletRemoved1",
                "FilletRemoved2", "FilletRemoved3", "FilletRemoved4" };
            for (int i = 0; i < states.Length; i++)
                states[i] = Child(body, names[i]).gameObject;
            Component head = NewPart(body, "FishHead", 0);
            Type partType = RuntimeReflection.RuntimeType("SashimiBoy.FlounderAssemblyPart");
            Array fillets = Array.CreateInstance(partType, 4);
            for (int i = 0; i < 4; i++)
                fillets.SetValue(NewPart(body, i < 2 ? "FrontFillet" : "BackFillet", i + 1), i);
            Component view = RuntimeReflection.AddComponent(root, "SashimiBoy.FlounderAssemblyView");
            RuntimeReflection.SetField(view, "bodyPivot", body);
            RuntimeReflection.SetField(view, "detachedPartsRoot", detached);
            RuntimeReflection.SetField(view, "bodyStates", states);
            RuntimeReflection.SetField(view, "head", head);
            RuntimeReflection.SetField(view, "fillets", fillets);
            RuntimeReflection.SetField(view, "headStorageAnchor", headAnchor);
            RuntimeReflection.SetField(view, "filletStorageAnchors", anchors);
            foreach (string field in new[] { "filletWorkAnchor", "knifeAttachmentAnchor",
                "handAttachmentAnchor", "sashimiOutputAnchor", "plateOutputAnchor" })
                RuntimeReflection.SetField(view, field, Child(root.transform, field));
            GameObject slice = Child(root.transform, "SashimiSliceTemplate").gameObject;
            slice.SetActive(false);
            RuntimeReflection.SetField(view, "sashimiSlicePrefab", slice);
            RuntimeReflection.SetField(view, "turnOverDuration", 1f);
            Fixture fixture = ReadBoundFixture(root, view);
            root.SetActive(true); // Awake now sees a complete binding.
            ((Behaviour)view).enabled = false; // Tests own the presentation clock.
            return fixture;
        }

        private static Component NewPart(Transform parent, string id, int index)
        {
            Transform transform = Child(parent, id + index);
            transform.localPosition = new Vector3(0.1f * index, 0.03f * index, -0.07f * index);
            transform.localRotation = Quaternion.Euler(7f * index, 11f * index, 3f * index);
            transform.localScale = new Vector3(1f + index * 0.04f, 0.9f, 1.1f);
            Component part = RuntimeReflection.AddComponent(
                transform.gameObject, "SashimiBoy.FlounderAssemblyPart");
            RuntimeReflection.SetField(part, "stableId", id + index);
            RuntimeReflection.SetField(part, "separationDuration", 1f);
            return part;
        }

        private static Fixture ReadBoundFixture(GameObject root, Component view)
        {
            var f = new Fixture
            {
                Root = root, View = view,
                BodyPivot = (Transform)RuntimeReflection.GetField(view, "bodyPivot"),
                DetachedRoot = (Transform)RuntimeReflection.GetField(view, "detachedPartsRoot"),
                Head = (Component)RuntimeReflection.GetField(view, "head"),
                Bodies = (GameObject[])RuntimeReflection.GetField(view, "bodyStates"),
                StorageAnchors = (Transform[])RuntimeReflection.GetField(view, "filletStorageAnchors"),
                Fillets = new Component[4]
            };
            Array fillets = (Array)RuntimeReflection.GetField(view, "fillets");
            for (int i = 0; i < 4; i++) f.Fillets[i] = (Component)fillets.GetValue(i);
            f.Parts = new[] { f.Head, f.Fillets[0], f.Fillets[1], f.Fillets[2], f.Fillets[3] };
            f.AuthoredRoot = new LocalPose(root.transform);
            f.AuthoredBody = new LocalPose(f.BodyPivot);
            f.AuthoredParts = new LocalPose[5];
            for (int i = 0; i < 5; i++) f.AuthoredParts[i] = new LocalPose(f.Parts[i].transform);
            return f;
        }

        private static void DetachFront(Fixture f)
        {
            Assert.That(Command(f.View, "TryRemoveHead"), Is.True);
            Assert.That(Command(f.View, "TrySeparateFillet", 0), Is.True);
            Assert.That(Command(f.View, "TrySeparateFillet", 1), Is.True);
        }

        private static void AssertReset(Fixture f)
        {
            AssertBody(f, "WholeFish", 0);
            Assert.That(Int(f.View, "DetachedFilletCount"), Is.Zero);
            Assert.That(Bool(f.View, "HeadRemoved"), Is.False);
            Assert.That(Bool(f.View, "IsBackFacing"), Is.False);
            Assert.That(Bool(f.View, "IsTurningOver"), Is.False);
            f.AuthoredRoot.AssertMatches(f.Root.transform);
            f.AuthoredBody.AssertMatches(f.BodyPivot);
            for (int i = 0; i < f.Parts.Length; i++)
            {
                f.AuthoredParts[i].AssertMatches(f.Parts[i].transform);
                Assert.That(Bool(f.Parts[i], "IsDetached"), Is.False);
                Assert.That(Bool(f.Parts[i], "IsStored"), Is.False);
                Assert.That(Bool(f.Parts[i], "IsMoving"), Is.False);
                Assert.That(f.Parts[i].gameObject.activeSelf, Is.False);
            }
        }

        private static void AssertBody(Fixture f, string expected, int visible)
        {
            Assert.That(RuntimeReflection.Invoke(f.View, "get_BodyState").ToString(), Is.EqualTo(expected));
            for (int i = 0; i < f.Bodies.Length; i++)
                Assert.That(f.Bodies[i].activeSelf, Is.EqualTo(i == visible), f.Bodies[i].name);
        }

        private static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static void Advance(Fixture f, float delta) =>
            RuntimeReflection.Invoke(f.View, "AdvancePresentation", delta);
        private static bool Command(Component target, string name, params object[] args) =>
            Convert.ToBoolean(RuntimeReflection.Invoke(target, name, args));
        private static bool Bool(Component target, string name) =>
            Convert.ToBoolean(RuntimeReflection.Invoke(target, "get_" + name));
        private static int Int(Component target, string name) =>
            Convert.ToInt32(RuntimeReflection.Invoke(target, "get_" + name));
        private static int PartCount(Fixture f) => f.Root.GetComponentsInChildren(
            RuntimeReflection.RuntimeType("SashimiBoy.FlounderAssemblyPart"), true).Length;

        private static Vector3[] WorldPositions(Component[] parts)
        {
            var result = new Vector3[parts.Length];
            for (int i = 0; i < parts.Length; i++) result[i] = parts[i].transform.position;
            return result;
        }
        private static Quaternion[] WorldRotations(Component[] parts)
        {
            var result = new Quaternion[parts.Length];
            for (int i = 0; i < parts.Length; i++) result[i] = parts[i].transform.rotation;
            return result;
        }
        private static void AssertVector(Vector3 actual, Vector3 expected) =>
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.0001f));
        private static void AssertRotation(Quaternion actual, Quaternion expected) =>
            Assert.That(Quaternion.Angle(actual, expected), Is.LessThan(0.05f));

        private sealed class Fixture
        {
            public GameObject Root;
            public Component View, Head;
            public Component[] Fillets, Parts;
            public Transform BodyPivot, DetachedRoot;
            public Transform[] StorageAnchors;
            public GameObject[] Bodies;
            public LocalPose AuthoredRoot, AuthoredBody;
            public LocalPose[] AuthoredParts;
        }
        private readonly struct LocalPose
        {
            private readonly Transform parent;
            private readonly Vector3 position, scale;
            public readonly Quaternion Rotation;
            public LocalPose(Transform transform)
            {
                parent = transform.parent;
                position = transform.localPosition;
                Rotation = transform.localRotation;
                scale = transform.localScale;
            }
            public void AssertMatches(Transform transform)
            {
                Assert.That(transform.parent, Is.SameAs(parent));
                AssertVector(transform.localPosition, position);
                AssertRotation(transform.localRotation, Rotation);
                AssertVector(transform.localScale, scale);
            }
        }
    }
}
