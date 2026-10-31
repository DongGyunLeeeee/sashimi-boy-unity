using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SashimiBoy.Tests
{
    public sealed class Stage03HudParityEditModeTests
    {
        private const string Stage1 = "Assets/_SashimiBoy/Scenes/Stage01_Salmon.unity";
        private const string Stage2 = "Assets/_SashimiBoy/Scenes/Stage02_Rockfish.unity";
        private const string Stage3 = "Assets/_SashimiBoy/Scenes/Stage03_Flounder.unity";
        private Scene scene, baseline;
        private Component controller, view, shared, source, sourceCue;
        private byte[] stage1Bytes, stage2Bytes;

        [SetUp]
        public void SetUp()
        {
            stage1Bytes = File.ReadAllBytes(Stage1); stage2Bytes = File.ReadAllBytes(Stage2);
            scene = EditorSceneManager.OpenScene(Stage3, OpenSceneMode.Single);
            baseline = EditorSceneManager.OpenScene(Stage2, OpenSceneMode.Additive);
            controller = Find(scene, "SashimiBoy.Stage03GameController");
            source = Find(baseline, "SashimiBoy.Stage01SalmonHUD");
            sourceCue = Find(baseline, "SashimiBoy.SliceCuePresenter");
            BindGeneratedView();
        }

        [TearDown]
        public void TearDown()
        {
            // Discard only the test's in-memory copies; never save a baseline scene.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.That(File.ReadAllBytes(Stage1), Is.EqualTo(stage1Bytes), "Stage1 scene bytes must remain untouched.");
            Assert.That(File.ReadAllBytes(Stage2), Is.EqualTo(stage2Bytes), "Stage2 scene bytes must remain untouched.");
        }

        [Test]
        public void GeneratedHud_ReusesBaselineDockTypographyLaneJudgementSpritesAndResultCard()
        {
            var sourceCanvas = source.GetComponent<Canvas>();
            var canvas = shared.GetComponent<Canvas>();
            Assert.That(canvas.name, Is.EqualTo(sourceCanvas.name));
            Assert.That(canvas.renderMode, Is.EqualTo(sourceCanvas.renderMode));
            Assert.That(canvas.sortingOrder, Is.EqualTo(sourceCanvas.sortingOrder));
            var sourceScaler = source.GetComponent<CanvasScaler>();
            var scaler = shared.GetComponent<CanvasScaler>();
            Assert.That(scaler.uiScaleMode, Is.EqualTo(sourceScaler.uiScaleMode));
            Assert.That(scaler.referenceResolution, Is.EqualTo(sourceScaler.referenceResolution));
            Assert.That(scaler.screenMatchMode, Is.EqualTo(sourceScaler.screenMatchMode));
            Assert.That(scaler.matchWidthOrHeight, Is.EqualTo(sourceScaler.matchWidthOrHeight));
            Assert.That(sourceScaler.referenceResolution, Is.EqualTo(new Vector2(1920f, 1080f)));
            var sourceDock = Field<GameObject>(source, "focusHudRoot");
            var dock = Field<GameObject>(shared, "focusHudRoot");
            CompareRect(sourceDock.GetComponent<RectTransform>(), dock.GetComponent<RectTransform>());
            Assert.That(dock.activeSelf, Is.EqualTo(sourceDock.activeSelf));
            foreach (var rect in sourceDock.GetComponentsInChildren<RectTransform>(true))
            {
                string path = AnimationUtility.CalculateTransformPath(rect, source.transform);
                var clone = shared.transform.Find(path);
                Assert.That(clone, Is.Not.Null, "Missing baseline dock child: " + path);
                CompareRect(rect, clone.GetComponent<RectTransform>());
                var text = rect.GetComponent<Text>(); if (text != null) CompareText(text, clone.GetComponent<Text>());
                var image = rect.GetComponent<Image>(); if (image != null) CompareImage(image, clone.GetComponent<Image>());
            }
            CompareRect(Field<RectTransform>(sourceCue, "rhythmLane"), Field<RectTransform>(view, "rhythmLane"));
            CompareImage(Field<Image>(sourceCue, "hitCursor"), Field<Image>(view, "hitCursor"));
            var originalDots = Field<Image[]>(sourceCue, "upcomingNoteDots");
            var dots = Field<Image[]>(view, "noteDots");
            Assert.That(dots.Length, Is.EqualTo(4));
            for (int i = 0; i < dots.Length; i++)
            {
                CompareRect(originalDots[i].rectTransform, dots[i].rectTransform);
                CompareImage(originalDots[i], dots[i]);
                Assert.That(dots[i].transform.IsChildOf(dock.transform), Is.True);
            }
            foreach (var pair in new[] { new[] { "spacePromptText", "spacePrompt" }, new[] { "nowText", "nowPrompt" }, new[] { "restPromptText", "restPrompt" } })
            {
                var original = Field<Text>(sourceCue, pair[0]); var prompt = Field<Text>(view, pair[1]);
                CompareRect(original.rectTransform, prompt.rectTransform); CompareText(original, prompt);
            }
            var library = Field<UnityEngine.Object>(shared, "ownerJudgementVisuals");
            Assert.That(library, Is.SameAs(Field<UnityEngine.Object>(source, "ownerJudgementVisuals")));
            var definitions = (IList)RuntimeReflection.GetField(library, "visuals");
            Assert.That(definitions.Count, Is.EqualTo(4));
            var fileNames = definitions.Cast<object>().Select(d => AssetDatabase.GetAssetPath(Field<Sprite>(d, "sprite")))
                .Select(Path.GetFileNameWithoutExtension).ToArray();
            Assert.That(fileNames, Is.EquivalentTo(new[] { "clean", "nasty", "slipped", "whack" }));
            CompareImage(Field<Image>(source, "judgementImage"), Field<Image>(shared, "judgementImage"));
            CompareText(Field<Text>(source, "judgementDetail"), Field<Text>(shared, "judgementDetail"));
            Assert.That(Field<Image>(controller, "songFill"), Is.SameAs(Field<Image>(shared, "songProgressFill")));
            Assert.That(Field<RectTransform>(controller, "noteLane"), Is.SameAs(Field<RectTransform>(view, "rhythmLane")));
            Assert.That(Field<RectTransform>(controller, "noteMarker"), Is.SameAs(dots[0].rectTransform));
            Assert.That(Field<bool>(controller, "autoStart"), Is.True, "Stage3 starts with the same automatic music/countdown entry as the shipped stages.");

            var sourceResult = Field<GameObject>(source, "resultRoot");
            foreach (string field in new[] { "startRoot", "pauseRoot", "failureRoot" })
            {
                var card = Field<GameObject>(view, field);
                CompareRect(sourceResult.GetComponent<RectTransform>(), card.GetComponent<RectTransform>());
                CompareImage(sourceResult.GetComponent<Image>(), card.GetComponent<Image>());
                CompareText(Field<Text>(source, "resultText"), card.transform.Find("ResultText").GetComponent<Text>());
            }
            var result = Field<GameObject>(shared, "resultRoot");
            CompareRect(sourceResult.GetComponent<RectTransform>(), result.GetComponent<RectTransform>());
            CompareText(Field<Text>(source, "resultText"), Field<Text>(shared, "resultText"));
            Assert.That(result.GetComponent<RectTransform>().anchorMin, Is.EqualTo(new Vector2(.77f, .75f)));
            Assert.That(result.GetComponent<RectTransform>().sizeDelta, Is.EqualTo(new Vector2(650f, 400f)));
            Assert.That(result.transform.Find("Stage1Retry").gameObject.activeSelf, Is.False);
            CheckAction(result, "Stage1Return", "ReturnToShop");
            CheckAction(Field<GameObject>(view, "startRoot"), "Stage1Retry", "StartFromButton");
            CheckAction(Field<GameObject>(view, "pauseRoot"), "Stage1Retry", "TogglePause");
            CheckAction(Field<GameObject>(view, "failureRoot"), "Stage1Retry", "RetryFromButton");
            foreach (string field in new[] { "startRoot", "pauseRoot", "failureRoot" })
                CheckAction(Field<GameObject>(view, field), "Stage1Return", "ReturnToShop");
        }

        [Test]
        public void WorldGuide_ReusesBaselineScaleMaterialsBracketGeometryAndCueConfiguration()
        {
            var original = Field<GameObject>(sourceCue, "cueRoot");
            var root = Field<GameObject>(view, "worldCue");
            Assert.That(Vector3.Distance(original.transform.lossyScale, root.transform.lossyScale), Is.LessThan(.00001f));
            Assert.That(Quaternion.Angle(original.transform.rotation, root.transform.rotation), Is.LessThan(.001f));
            foreach (var renderer in original.GetComponentsInChildren<Renderer>(true))
            {
                string path = AnimationUtility.CalculateTransformPath(renderer.transform, original.transform);
                var copy = root.transform.Find(path).GetComponent<Renderer>();
                Assert.That(copy.sharedMaterials, Is.EqualTo(renderer.sharedMaterials), path);
                Assert.That(copy.transform.localPosition, Is.EqualTo(renderer.transform.localPosition), path);
                Assert.That(copy.transform.localRotation, Is.EqualTo(renderer.transform.localRotation), path);
                Assert.That(copy.transform.localScale, Is.EqualTo(renderer.transform.localScale), path);
                Assert.That(copy.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(renderer.GetComponent<MeshFilter>().sharedMesh), path);
            }
            foreach (string field in new[] { "wideBracketDistance", "closedBracketDistance", "laneHorizonBeats", "nowDuration", "restThresholdBeats" })
                Assert.That(RuntimeReflection.GetField(view, field), Is.EqualTo(RuntimeReflection.GetField(sourceCue, field)), field);
            foreach (string field in new[] { "leftBracket", "rightBracket", "forecastLeftBracket", "forecastRightBracket" })
            {
                var originalPart = Field<Transform>(sourceCue, field); var part = Field<Transform>(view, field);
                Assert.That(AnimationUtility.CalculateTransformPath(part, root.transform),
                    Is.EqualTo(AnimationUtility.CalculateTransformPath(originalPart, original.transform)), field);
            }
            Assert.That(Field<Renderer>(view, "cutGuide").name, Is.EqualTo(Field<Renderer>(sourceCue, "cutGuideLine").name));
            Assert.That(Field<Transform[]>(view, "ghostLines").Length, Is.EqualTo(Field<Transform[]>(sourceCue, "upcomingGhostLines").Length));
            Assert.That(Field<Renderer[]>(view, "ghostRenderers").Length, Is.EqualTo(Field<Renderer[]>(sourceCue, "upcomingGhostRenderers").Length));
            Assert.That(root.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
        }

        [Test]
        public void Reapply_IsIdempotentKeepsSourceBytesAndCannotRetainStageOneTimingOwnersOrForeignSceneReferences()
        {
            RuntimeReflection.InvokeStatic("SashimiBoy.EditorTools.Stage03HudAuthoring", "Apply", scene, controller);
            RuntimeReflection.InvokeStatic("SashimiBoy.EditorTools.Stage03HudAuthoring", "Apply", scene, controller);
            BindGeneratedView();
            var scripts = All<MonoBehaviour>(scene).Where(s => s != null).ToArray();
            Assert.That(scripts.Count(s => s.GetType().FullName == "SashimiBoy.Stage03HudView"), Is.EqualTo(1));
            Assert.That(All<Canvas>(scene).Count(c => c.name == source.name), Is.EqualTo(1));
            Assert.That(All<Canvas>(scene).Any(c => c.name == "Stage03HUD"), Is.False);
            Assert.That(All<Transform>(scene).Count(t => t.name == "Stage03_SliceCueWorld"), Is.EqualTo(1));
            Assert.That(All<Transform>(scene).Any(t => t.name == "Stage01_SliceCueWorld"), Is.False);
            Assert.That(((Behaviour)shared).enabled, Is.False, "The baseline presenter must not own Stage3 timing or its nine phases.");
            Assert.That(RuntimeReflection.GetField(shared, "butchery"), Is.Null);
            Assert.That(RuntimeReflection.GetField(shared, "timing"), Is.Null);
            foreach (var script in shared.GetComponentsInChildren<MonoBehaviour>(true))
                Assert.That(script == shared || script == view || script is UIBehaviour, Is.True, "Retained gameplay script: " + script.GetType().FullName);
            foreach (var root in new[] { shared.gameObject, Field<GameObject>(view, "worldCue") })
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                var serialized = new SerializedObject(component); var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var referenced = property.objectReferenceValue;
                    GameObject go = referenced as GameObject;
                    if (referenced is Component referenceComponent) go = referenceComponent.gameObject;
                    if (go != null && go.scene.IsValid()) Assert.That(go.scene, Is.EqualTo(scene), component.name + "." + property.propertyPath);
                }
            }
            RuntimeReflection.InvokeStatic("SashimiBoy.EditorTools.Stage01PlayableAuthoring", "ScanScene", scene);
            Assert.That(baseline.isDirty, Is.False, "Reusing the loaded source scene must not edit it in memory.");
            Assert.That(File.ReadAllBytes(Stage1), Is.EqualTo(stage1Bytes));
            Assert.That(File.ReadAllBytes(Stage2), Is.EqualTo(stage2Bytes));
        }

        private void BindGeneratedView()
        {
            view = Field<Component>(controller, "hudView");
            Assert.That(view, Is.Not.Null, "Generate Stage3 with the baseline HUD authoring before running this regression.");
            shared = Field<Component>(view, "shared");
        }
        private void CheckAction(GameObject card, string name, string action)
        {
            var button = card.transform.Find(name).GetComponent<Button>();
            var original = Field<GameObject>(source, "resultRoot").transform.Find(name).GetComponent<Button>();
            CompareRect(original.GetComponent<RectTransform>(), button.GetComponent<RectTransform>());
            CompareImage(original.GetComponent<Image>(), button.GetComponent<Image>());
            CompareText(original.GetComponentInChildren<Text>(true), button.GetComponentInChildren<Text>(true));
            Assert.That(button.onClick.GetPersistentEventCount(), Is.EqualTo(1));
            Assert.That(button.onClick.GetPersistentTarget(0), Is.SameAs(controller));
            Assert.That(button.onClick.GetPersistentMethodName(0), Is.EqualTo(action));
        }
        private static T Field<T>(object target, string field) => (T)RuntimeReflection.GetField(target, field);
        private static Component Find(Scene target, string name) => All<MonoBehaviour>(target).Single(c => c != null && c.GetType().FullName == name);
        private static System.Collections.Generic.IEnumerable<T> All<T>(Scene target) where T : Component => target.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true));
        private static void CompareRect(RectTransform expected, RectTransform actual)
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual.anchorMin, Is.EqualTo(expected.anchorMin), expected.name);
            Assert.That(actual.anchorMax, Is.EqualTo(expected.anchorMax), expected.name);
            Assert.That(actual.pivot, Is.EqualTo(expected.pivot), expected.name);
            Assert.That(actual.anchoredPosition, Is.EqualTo(expected.anchoredPosition), expected.name);
            Assert.That(actual.sizeDelta, Is.EqualTo(expected.sizeDelta), expected.name);
            Assert.That(actual.localScale, Is.EqualTo(expected.localScale), expected.name);
        }
        private static void CompareText(Text expected, Text actual)
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual.font, Is.SameAs(expected.font), expected.name);
            Assert.That(actual.fontSize, Is.EqualTo(expected.fontSize), expected.name);
            Assert.That(actual.fontStyle, Is.EqualTo(expected.fontStyle), expected.name);
            Assert.That(actual.color, Is.EqualTo(expected.color), expected.name);
            Assert.That(actual.alignment, Is.EqualTo(expected.alignment), expected.name);
            Assert.That(actual.lineSpacing, Is.EqualTo(expected.lineSpacing), expected.name);
            Assert.That(actual.resizeTextForBestFit, Is.EqualTo(expected.resizeTextForBestFit), expected.name);
            Assert.That(actual.horizontalOverflow, Is.EqualTo(expected.horizontalOverflow), expected.name);
            Assert.That(actual.verticalOverflow, Is.EqualTo(expected.verticalOverflow), expected.name);
            var shadow = expected.GetComponent<Shadow>();
            if (shadow != null)
            {
                Assert.That(actual.GetComponent<Shadow>(), Is.Not.Null);
                Assert.That(actual.GetComponent<Shadow>().effectColor, Is.EqualTo(shadow.effectColor));
                Assert.That(actual.GetComponent<Shadow>().effectDistance, Is.EqualTo(shadow.effectDistance));
            }
        }
        private static void CompareImage(Image expected, Image actual)
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual.color, Is.EqualTo(expected.color), expected.name);
            Assert.That(actual.sprite, Is.SameAs(expected.sprite), expected.name);
            Assert.That(actual.material, Is.SameAs(expected.material), expected.name);
            Assert.That(actual.type, Is.EqualTo(expected.type), expected.name);
            Assert.That(actual.preserveAspect, Is.EqualTo(expected.preserveAspect), expected.name);
        }
    }
}
