using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SashimiBoy.EquipmentShopTests
{
    public static class RuntimeReflection
    {
        private const BindingFlags AnyMember = BindingFlags.Instance |
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static Type RuntimeType(string fullName)
        {
            Type type = Type.GetType(fullName + ", Assembly-CSharp", false);
            if (type != null)
            {
                return type;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                type = assemblies[i].GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            throw new InvalidOperationException(
                $"Runtime type '{fullName}' was not found.");
        }

        public static Component AddComponent(
            GameObject gameObject,
            string fullTypeName)
        {
            return gameObject.AddComponent(RuntimeType(fullTypeName));
        }

        public static object Invoke(
            object target,
            string methodName,
            params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                AnyMember);
            if (method == null)
            {
                throw new MissingMethodException(
                    target.GetType().FullName,
                    methodName);
            }

            return method.Invoke(target, arguments);
        }

        public static object InvokeStatic(
            string fullTypeName,
            string methodName,
            params object[] arguments)
        {
            Type type = RuntimeType(fullTypeName);
            MethodInfo method = type.GetMethod(methodName, AnyMember);
            if (method == null)
            {
                throw new MissingMethodException(type.FullName, methodName);
            }

            return method.Invoke(null, arguments);
        }

        public static object GetField(object target, string fieldName)
        {
            return FindField(target.GetType(), fieldName).GetValue(target);
        }

        public static void SetField(
            object target,
            string fieldName,
            object value)
        {
            FindField(target.GetType(), fieldName).SetValue(target, value);
        }

        public static void SetSingleton(string fullTypeName, object value)
        {
            Type type = RuntimeType(fullTypeName);
            FieldInfo instanceField = type.GetField(
                "<Instance>k__BackingField",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (instanceField == null)
            {
                throw new MissingFieldException(
                    type.FullName,
                    "<Instance>k__BackingField");
            }

            instanceField.SetValue(null, value);
        }

        public static Component FindActiveComponent(string fullTypeName)
        {
            Type type = RuntimeType(fullTypeName);
            UnityEngine.Object[] candidates =
                Resources.FindObjectsOfTypeAll(type);
            for (int i = 0; i < candidates.Length; i++)
            {
                Component component = candidates[i] as Component;
                if (component != null &&
                    component.gameObject.scene.IsValid() &&
                    component.gameObject.activeInHierarchy)
                {
                    return component;
                }
            }

            return null;
        }

        public static Component FindComponentInScene(
            Scene scene,
            string fullTypeName)
        {
            Type type = RuntimeType(fullTypeName);
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Component component = roots[i].GetComponentInChildren(
                    type,
                    true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static FieldInfo FindField(Type type, string fieldName)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(
                    fieldName,
                    AnyMember | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field;
                }

                type = type.BaseType;
            }

            throw new MissingFieldException(fieldName);
        }
    }
}
