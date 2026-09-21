#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SashimiBoy.EditorTools
{
    // This read-only batch command opens every scene and prefab, including assets
    // absent from build settings. Nothing is saved or reserialized.
    public static class AutomationComponentInventory
    {
        [Serializable] public sealed class ComponentRecord
        {
            public string path;
            public string type;
            public bool active;
        }

        [Serializable] public sealed class AssetRecord
        {
            public string path;
            public string kind;
            public int activeAudioListeners;
            public int activeEventSystems;
            public int missingScripts;
            public int missingReferences;
            public ComponentRecord[] components;
        }

        [Serializable] public sealed class Inventory
        {
            public int schemaVersion = 1;
            public bool passed;
            public AssetRecord[] assets;
            public string[] errors;
        }

        public static AssetRecord Inspect(string path, string kind, GameObject[] roots)
        {
            var records = new List<ComponentRecord>();
            var asset = new AssetRecord { path = path, kind = kind };
            foreach (var root in roots)
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var gameObject = transform.gameObject;
                asset.missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
                foreach (var component in gameObject.GetComponents<Component>())
                {
                    if (component == null) continue;
                    bool active = IsActiveWithinRoot(transform, root.transform);
                    if (component is Behaviour behaviour) active &= behaviour.enabled;
                    if (component is AudioListener || component is EventSystem)
                    {
                        records.Add(new ComponentRecord {
                            path = ObjectPath(transform), type = component is AudioListener ? "AudioListener" : "EventSystem", active = active
                        });
                        if (active && component is AudioListener) asset.activeAudioListeners++;
                        if (active && component is EventSystem) asset.activeEventSystems++;
                    }
                    using (var serialized = new SerializedObject(component))
                    {
                        var property = serialized.GetIterator();
                        while (property.Next(true))
                            if (property.propertyType == SerializedPropertyType.ObjectReference &&
                                property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0)
                                asset.missingReferences++;
                    }
                }
            }
            asset.components = records.ToArray();
            return asset;
        }

        private static bool IsActiveWithinRoot(Transform value, Transform root)
        {
            for (var current = value; current != null; current = current.parent)
            {
                if (!current.gameObject.activeSelf) return false;
                if (current == root) return true;
            }
            return false;
        }

        private static string ObjectPath(Transform value)
        {
            var parts = new List<string>();
            for (var current = value; current != null; current = current.parent)
                parts.Add(current.name + "[" + current.GetSiblingIndex() + "]");
            parts.Reverse();
            return string.Join("/", parts);
        }

        public static void ScanBatch()
        {
            var assets = new List<AssetRecord>();
            var errors = new List<string>();
            var paths = AssetDatabase.GetAllAssetPaths().Where(path => path.StartsWith("Assets/", StringComparison.Ordinal) &&
                (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
                 path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))).OrderBy(path => path, StringComparer.Ordinal);
            foreach (var path in paths)
            {
                try
                {
                    AssetRecord record;
                    if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    {
                        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                        record = Inspect(path, "Scene", scene.GetRootGameObjects());
                    }
                    else
                    {
                        var root = PrefabUtility.LoadPrefabContents(path);
                        try { record = Inspect(path, "Prefab", new[] { root }); }
                        finally { PrefabUtility.UnloadPrefabContents(root); }
                    }
                    assets.Add(record);
                    if (record.activeAudioListeners > 1 || record.activeEventSystems > 1 ||
                        record.missingScripts > 0 || record.missingReferences > 0)
                        errors.Add(path + ": duplicate active component or broken reference");
                }
                catch (Exception exception)
                {
                    errors.Add(path + ": " + exception.GetType().Name);
                }
            }
            var report = new Inventory { passed = errors.Count == 0, assets = assets.ToArray(), errors = errors.ToArray() };
            Debug.Log("SASHIMI_COMPONENT_INVENTORY=" + JsonUtility.ToJson(report));
            EditorApplication.Exit(report.passed ? 0 : 1);
        }
    }
}
#endif
