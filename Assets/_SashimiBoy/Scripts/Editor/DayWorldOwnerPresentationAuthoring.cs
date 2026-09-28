using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace SashimiBoy.EditorTools
{
    public static class DayWorldOwnerPresentationAuthoring
    {
        [MenuItem("Sashimi Boy/Day 01 + Day 02/Apply Logo Faces and Room Layouts")]
        public static void ApplyBatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before authoring.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save unsaved scenes first.");
            KevinCustomizationAuthoring.BuildFaces();
            DayWorldAssetAuthoring.BuildFlounderDisplay();
            foreach (string name in new[] { "Bootstrap", "KevinHome", "EquipmentShop", "FishShopDialogue", "Street" })
            {
                var scene = EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/" + name + ".unity", OpenSceneMode.Single);
                var root = scene.GetRootGameObjects().Single(g => g.name == "DayWorld_Integration").transform;
                if (name == "Bootstrap") KevinCustomizationAuthoring.ApplyMenu(root.GetComponent<DayWorldSceneDirector>());
                else if (name == "KevinHome")
                {
                    DayWorldInteriorAuthoring.Apply(scene, root);
                    DayWorldHomeAuthoring.Apply(scene, root);
                }
                else if (name == "Street") DayWorldVenueAuthoring.ApplyStreetDisplay(scene, root);
                else DayWorldVenueAuthoring.Apply(scene, root);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            AuditBatch();
            Debug.Log("[OwnerPresentation] Applied logo, four connected Kevin faces, compact home, furnished hall/kitchen and shopkeeper service to retained scenes.");
        }

        public static void AuditBatch()
        {
            string folder = "Logs/DayWorld/LogoFaceLayoutRevision";
            Directory.CreateDirectory(folder);
            foreach (string name in new[] { "Bootstrap", "KevinHome", "EquipmentShop", "FishShopDialogue", "Street", "Club", "Stage01_Salmon", "Stage02_Rockfish" })
            {
                var scene = EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/" + name + ".unity", OpenSceneMode.Single);
                var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
                foreach (var t in all)
                {
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0) throw new InvalidOperationException("Missing script: " + name + "/" + t.name);
                    foreach (var behaviour in t.GetComponents<MonoBehaviour>())
                    {
                        var so = new SerializedObject(behaviour); var property = so.GetIterator();
                        while (property.NextVisible(true))
                            if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0)
                                throw new InvalidOperationException("Broken reference: " + name + "/" + t.name + "/" + property.propertyPath);
                    }
                }
                if (all.Count(t => t.GetComponent<AudioListener>() != null && t.gameObject.activeInHierarchy && t.GetComponent<AudioListener>().enabled) != 1) throw new InvalidOperationException("AudioListener: " + name);
                if (all.Count(t => t.GetComponent<EventSystem>() != null && t.gameObject.activeInHierarchy) != 1) throw new InvalidOperationException("EventSystem: " + name);
                var lines = all.Select(t =>
                {
                    string path = t.name;
                    for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
                    return path + " | " + t.position + " | active=" + t.gameObject.activeInHierarchy + " | prefab=" + PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject);
                });
                File.WriteAllLines(folder + "/" + name + "-hierarchy.txt", lines);
                var dependencies = AssetDatabase.GetDependencies(scene.path, true).Where(p => p.StartsWith("Assets/_SashimiBoy/Art/Source/")).OrderBy(p => p);
                File.WriteAllLines(folder + "/" + name + "-source-usage.txt", dependencies);
            }
            Debug.Log("[OwnerPresentation] Eight scene script/reference, listener, event-system and source-dependency scans complete.");
        }
    }
}
