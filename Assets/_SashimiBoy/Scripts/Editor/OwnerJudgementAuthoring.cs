using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SashimiBoy.EditorTools
{
    public static class OwnerJudgementAuthoring
    {
        public const string Source = "Assets/_SashimiBoy/Art/Source/UI/Judgement";
        public const string LibraryPath = "Assets/_SashimiBoy/Data/Generated/OwnerJudgementVisuals.asset";
        private static readonly string[] Names = { "clean", "nasty", "slipped", "whack" };
        private static readonly JudgeGrade[] Grades = { JudgeGrade.Smooth, JudgeGrade.Nasty, JudgeGrade.Slipped, JudgeGrade.Whack };

        private static JudgementVisualLibrary BuildLibrary()
        {
            var library = AssetDatabase.LoadAssetAtPath<JudgementVisualLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<JudgementVisualLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            library.visuals.Clear();
            for (int i = 0; i < Names.Length; i++)
            {
                string path = Source + "/" + Names[i] + ".png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Missing owner judgement image: " + path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null) throw new InvalidOperationException("Judgement sprite import failed: " + path);
                library.visuals.Add(new JudgementVisualDefinition { grade = Grades[i], displayLabel = Names[i].ToUpperInvariant(), sprite = sprite });
            }
            EditorUtility.SetDirty(library);
            return library;
        }

        public static void Apply(Stage01SalmonHUD hud)
        {
            if (hud == null || hud.focusHudRoot == null || hud.lastJudgementText == null)
                throw new InvalidOperationException("Apply the existing cooking HUD first.");
            var root = hud.focusHudRoot.transform;
            hud.ownerJudgementVisuals = BuildLibrary();
            hud.judgementImage = Child<Image>(root, "OwnerJudgementImage", new Vector2(-340f, 0f), new Vector2(180f, 100f));
            hud.judgementImage.preserveAspect = true;
            hud.judgementImage.raycastTarget = false;
            hud.judgementImage.enabled = false;
            hud.judgementDetail = Child<Text>(root, "JudgementTimingDetail", new Vector2(-125f, 25f), new Vector2(170f, 45f));
            hud.judgementDetail.font = hud.lastJudgementText.font;
            hud.judgementDetail.fontSize = 27;
            hud.judgementDetail.alignment = TextAnchor.MiddleLeft;
            hud.judgementDetail.raycastTarget = false;
            hud.judgementDetail.enabled = false;
            hud.lastJudgementText.enabled = true;
            hud.lastJudgementText.text = "박자에 맞춰 Space";
            EditorUtility.SetDirty(hud);
        }

        private static T Child<T>(Transform root, string name, Vector2 position, Vector2 size) where T : Graphic
        {
            var existing = root.Find(name);
            var graphic = existing != null ? existing.GetComponent<T>() : new GameObject(name, typeof(RectTransform), typeof(T)).GetComponent<T>();
            var rect = graphic.rectTransform;
            rect.SetParent(root, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.anchoredPosition = position; rect.sizeDelta = size; rect.localScale = Vector3.one;
            return graphic;
        }

        [MenuItem("Sashimi Boy/Apply Owner Judgement Images Only")]
        public static void ApplyBatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save unsaved scenes first.");
            foreach (string name in new[] { "Stage01_Salmon", "Stage02_Rockfish" })
            {
                var scene = EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/" + name + ".unity", OpenSceneMode.Single);
                Apply(scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Stage01SalmonHUD>(true)).Single());
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save HUD in " + name);
                if (name == "Stage01_Salmon") Stage01PlayableAuthoring.NormalizeSceneWhitespace();
            }
            AssetDatabase.SaveAssets();
        }
    }
}
