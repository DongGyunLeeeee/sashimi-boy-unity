using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SashimiBoy.Tests
{
    public sealed class SalmonAssemblyPlayModeTests
    {
        private const string AssemblyTypeName =
            "SashimiBoy.SalmonAssemblyView";
        private const string PieceTypeName =
            "SashimiBoy.SalmonAssemblyPieceView";
        private const string RoleTypeName =
            "SashimiBoy.SalmonAssemblyPieceRole";

        [UnityTest]
        public IEnumerator Pieces_CanShowHideDetachAndResetIndependently()
        {
            GameObject assemblyObject = new GameObject("RuntimeSalmonAssembly");
            GameObject partsRoot = new GameObject("Parts");
            partsRoot.transform.SetParent(assemblyObject.transform, false);
            GameObject pinBonesRoot = new GameObject("PinBones");
            pinBonesRoot.transform.SetParent(assemblyObject.transform, false);
            Component view = RuntimeReflection.AddComponent(
                assemblyObject,
                AssemblyTypeName);

            Component head = AddPiece(
                partsRoot.transform,
                "Head",
                "Head",
                new Vector3(0f, 0f, 1f),
                true);
            Component body = AddPiece(
                partsRoot.transform,
                "Body",
                "Body",
                Vector3.zero,
                true);
            Component fins = AddPiece(
                partsRoot.transform,
                "Fins",
                "Fins",
                new Vector3(0f, 0.4f, 0f),
                false);
            Component spine = AddPiece(
                partsRoot.transform,
                "Spine",
                "Spine",
                Vector3.zero,
                false);
            Component fillet = AddPiece(
                partsRoot.transform,
                "Fillet",
                "Fillet",
                Vector3.zero,
                false);
            Component pinBone0 = AddPiece(
                pinBonesRoot.transform,
                "PinBone.00",
                "PinBone",
                new Vector3(-0.1f, 0.3f, 0f),
                false);
            Component pinBone1 = AddPiece(
                pinBonesRoot.transform,
                "PinBone.01",
                "PinBone",
                new Vector3(0.1f, 0.3f, 0f),
                false);

            RuntimeReflection.SetField(view, "head", head);
            RuntimeReflection.SetField(view, "body", body);
            RuntimeReflection.SetField(view, "fins", fins);
            RuntimeReflection.SetField(view, "spine", spine);
            RuntimeReflection.SetField(view, "fillet", fillet);
            Array pinBones = Array.CreateInstance(
                RuntimeReflection.RuntimeType(PieceTypeName),
                2);
            pinBones.SetValue(pinBone0, 0);
            pinBones.SetValue(pinBone1, 1);
            RuntimeReflection.SetField(view, "pinBones", pinBones);
            yield return null;

            Assert.That(
                Property<IEnumerable>(view, "Pieces").Cast<object>().Count(),
                Is.EqualTo(7));
            AssertVisible(head, true);
            AssertVisible(body, true);
            AssertVisible(fins, false);
            AssertVisible(pinBone0, false);

            RuntimeReflection.Invoke(view, "SetPieceVisible", "Fins", true);
            RuntimeReflection.Invoke(view, "SetPieceVisible", "PinBone.00", true);
            AssertVisible(fins, true);
            AssertVisible(pinBone0, true);
            RuntimeReflection.Invoke(view, "SetPieceVisible", "Fins", false);
            AssertVisible(fins, false);

            Transform authoredParent = head.transform.parent;
            Vector3 authoredPosition = head.transform.localPosition;
            GameObject detachedRoot = new GameObject("DetachedPieces");
            RuntimeReflection.Invoke(
                view,
                "DetachPiece",
                "Head",
                detachedRoot.transform);
            Assert.That(head.transform.parent, Is.EqualTo(detachedRoot.transform));
            Assert.That(Property<bool>(head, "IsAttached"), Is.False);
            head.transform.position += Vector3.right * 3f;

            RuntimeReflection.Invoke(view, "SetPieceVisible", "Body", false);
            RuntimeReflection.Invoke(view, "SetPieceVisible", "PinBone.01", true);
            RuntimeReflection.Invoke(view, "ResetAssembly");
            Assert.That(head.transform.parent, Is.EqualTo(authoredParent));
            Assert.That(head.transform.localPosition, Is.EqualTo(authoredPosition));
            Assert.That(Property<bool>(head, "IsAttached"), Is.True);
            AssertVisible(head, true);
            AssertVisible(body, true);
            AssertVisible(fins, false);
            AssertVisible(pinBone0, false);
            AssertVisible(pinBone1, false);

            UnityEngine.Object.Destroy(assemblyObject);
            UnityEngine.Object.Destroy(detachedRoot);
            yield return null;
        }

        private static Component AddPiece(
            Transform parent,
            string id,
            string roleName,
            Vector3 localPosition,
            bool initiallyVisible)
        {
            GameObject pieceObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pieceObject.name = id;
            pieceObject.transform.SetParent(parent, false);
            pieceObject.transform.localPosition = localPosition;
            Component piece = RuntimeReflection.AddComponent(
                pieceObject,
                PieceTypeName);
            object role = Enum.Parse(
                RuntimeReflection.RuntimeType(RoleTypeName),
                roleName);
            RuntimeReflection.Invoke(
                piece,
                "Configure",
                id,
                role,
                pieceObject,
                initiallyVisible);
            return piece;
        }

        private static void AssertVisible(Component piece, bool expected)
        {
            Assert.That(Property<bool>(piece, "IsVisible"), Is.EqualTo(expected));
        }

        private static T Property<T>(object target, string name)
        {
            PropertyInfo property = target.GetType().GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null, target.GetType().FullName + "." + name);
            return (T)property.GetValue(target);
        }
    }
}
