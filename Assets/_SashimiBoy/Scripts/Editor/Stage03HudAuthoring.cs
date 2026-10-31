using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    /// <summary>Reuses the shipped workstation presentation without its Stage1/2 timing owners.</summary>
    public static class Stage03HudAuthoring
    {
        public const string WorldCueName = "Stage03_SliceCueWorld";

        public static void Apply(Scene scene, Stage03GameController controller)
        {
            if (!scene.IsValid() || !scene.isLoaded || controller == null || controller.gameObject.scene != scene)
                throw new InvalidOperationException("A loaded Stage3 scene and its controller are required.");
            byte[] salmon = File.ReadAllBytes(Stage01PlayableAuthoring.ScenePath);
            byte[] rockfish = File.ReadAllBytes(Stage02RockfishAuthoring.ScenePath);
            Scene active = SceneManager.GetActiveScene();
            Scene baseline = SceneManager.GetSceneByPath(Stage02RockfishAuthoring.ScenePath);
            bool opened = !baseline.IsValid() || !baseline.isLoaded;
            if (!opened && baseline.isDirty)
                throw new InvalidOperationException("The baseline Stage2 scene has unsaved changes.");
            if (opened) baseline = EditorSceneManager.OpenScene(Stage02RockfishAuthoring.ScenePath, OpenSceneMode.Additive);
            GameObject canvasClone = null, cueClone = null;
            bool applied = false;
            try
            {
                var source = All<Stage01SalmonHUD>(baseline).Single();
                var cue = All<SliceCuePresenter>(baseline).Single();
                if (source.GetComponent<Canvas>() == null || cue.cueRoot == null || cue.upcomingNoteDots.Length != 4)
                    throw new InvalidOperationException("The baseline workstation presentation is incomplete.");
                // Capture old presentation before cloning, so a reapply cannot delete its new roots.
                var oldCanvases = All<Canvas>(scene).Where(c => c.name == "Stage03HUD" || c.name == source.name ||
                    c.GetComponent<Stage03HudView>() != null).Select(c => c.gameObject).ToArray();
                var oldCues = All<Transform>(scene).Where(t => t.name == WorldCueName || t.name == cue.cueRoot.name)
                    .Select(t => t.gameObject).ToArray();
                canvasClone = Object.Instantiate(source.gameObject);
                canvasClone.name = source.name;
                SceneManager.MoveGameObjectToScene(canvasClone, scene);
                var shared = canvasClone.GetComponent<Stage01SalmonHUD>();
                StripGameplayScripts(canvasClone, shared);
                shared.enabled = false;
                shared.Bind(null, null);
                shared.butchery = null;
                shared.stageTitle = "STAGE 03";
                shared.fishLabel = "FLOUNDER / 가자미";
                shared.fishProgressPrefix = "";
                ClearForeignSceneReferences(canvasClone, scene);
                foreach (var button in canvasClone.GetComponentsInChildren<Button>(true)) ClearListeners(button);

                cueClone = Object.Instantiate(cue.cueRoot);
                cueClone.name = WorldCueName;
                SceneManager.MoveGameObjectToScene(cueClone, scene);
                // The source cue lives under the scaled workstation. Preserve that world scale
                // when its new timing-independent root is moved out of the source hierarchy.
                cueClone.transform.SetPositionAndRotation(cue.cueRoot.transform.position, cue.cueRoot.transform.rotation);
                cueClone.transform.localScale = cue.cueRoot.transform.lossyScale;
                StripGameplayScripts(cueClone, null);
                ClearForeignSceneReferences(cueClone, scene);

                var view = canvasClone.AddComponent<Stage03HudView>();
                view.controller = controller;
                view.shared = shared;
                view.rhythmLane = Remap(cue.rhythmLane, source.transform, canvasClone.transform);
                view.hitCursor = Remap(cue.hitCursor, source.transform, canvasClone.transform);
                view.noteDots = cue.upcomingNoteDots.Select(dot => Remap(dot, source.transform, canvasClone.transform)).ToArray();
                view.spacePrompt = Remap(cue.spacePromptText, source.transform, canvasClone.transform);
                view.nowPrompt = Remap(cue.nowText, source.transform, canvasClone.transform);
                view.restPrompt = Remap(cue.restPromptText, source.transform, canvasClone.transform);
                view.worldCue = cueClone;
                view.cutGuide = Remap(cue.cutGuideLine, cue.cueRoot.transform, cueClone.transform);
                view.leftBracket = Remap(cue.leftBracket, cue.cueRoot.transform, cueClone.transform);
                view.rightBracket = Remap(cue.rightBracket, cue.cueRoot.transform, cueClone.transform);
                view.forecastLeftBracket = Remap(cue.forecastLeftBracket, cue.cueRoot.transform, cueClone.transform);
                view.forecastRightBracket = Remap(cue.forecastRightBracket, cue.cueRoot.transform, cueClone.transform);
                view.ghostLines = cue.upcomingGhostLines.Select(t => Remap(t, cue.cueRoot.transform, cueClone.transform)).ToArray();
                view.ghostRenderers = cue.upcomingGhostRenderers.Select(r => Remap(r, cue.cueRoot.transform, cueClone.transform)).ToArray();
                view.wideBracketDistance = cue.wideBracketDistance;
                view.closedBracketDistance = cue.closedBracketDistance;
                view.laneHorizonBeats = cue.laneHorizonBeats;
                view.nowDuration = cue.nowDuration;
                view.restThresholdBeats = cue.restThresholdBeats;

                // Additional Stage3 states keep the exact baseline result-card typography,
                // anchors, image colors and button dimensions, while using Stage3 actions.
                view.startRoot = Card(shared, "Stage03StartCard", "가자미 손질\n\nSpace · 박자에 맞춰 손질\n앞면 필렛 2개 뒤 E · 뒤집기\n필렛 4개 · 회 24점 완성", "Enter · 손질 시작", controller.StartFromButton, controller.ReturnToShop, out var startText);
                view.startText = startText;
                view.pauseRoot = Card(shared, "Stage03PauseCard", "일시 정지\n\n음악과 손질 진행이 멈췄습니다", "Esc · 계속하기", controller.TogglePause, controller.ReturnToShop, out var pauseText);
                view.pauseText = pauseText;
                view.failureRoot = Card(shared, "Stage03FailureCard", "현재 구간을 다시 손질하세요\n\n완료한 부위는 유지됩니다", "R · 구간 재시도", controller.RetryFromButton, controller.ReturnToShop, out var failureText);
                view.failureText = failureText;
                var resultRetry = RequiredButton(shared.resultRoot, "Stage1Retry");
                resultRetry.gameObject.SetActive(false);
                Bind(RequiredButton(shared.resultRoot, "Stage1Return"), controller.ReturnToShop);
                shared.resultRoot.SetActive(false);
                shared.focusHudRoot.SetActive(true);
                view.startRoot.SetActive(false); view.pauseRoot.SetActive(false); view.failureRoot.SetActive(false);
                cueClone.SetActive(false);

                controller.hudView = view;
                controller.autoStart = true;
                controller.startPanel = view.startRoot; controller.pausePanel = view.pauseRoot;
                controller.failurePanel = view.failureRoot; controller.resultPanel = shared.resultRoot;
                controller.resultText = shared.resultText;
                controller.titleText = shared.fishProgressText; controller.statusText = shared.phaseRemainingText;
                controller.noteText = view.spacePrompt; controller.judgementText = shared.lastJudgementText;
                controller.noteLane = view.rhythmLane; controller.noteMarker = view.noteDots[0].rectTransform;
                controller.songFill = shared.songProgressFill;
                foreach (var old in oldCanvases.Concat(oldCues).Distinct()) if (old != null) Object.DestroyImmediate(old);
                EditorUtility.SetDirty(shared); EditorUtility.SetDirty(view); EditorUtility.SetDirty(controller);
                EditorSceneManager.MarkSceneDirty(scene);
                applied = true;
            }
            finally
            {
                if (!applied)
                {
                    if (canvasClone != null) Object.DestroyImmediate(canvasClone);
                    if (cueClone != null) Object.DestroyImmediate(cueClone);
                }
                if (opened && baseline.IsValid() && baseline.isLoaded) EditorSceneManager.CloseScene(baseline, true);
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                if (!File.ReadAllBytes(Stage01PlayableAuthoring.ScenePath).SequenceEqual(salmon) ||
                    !File.ReadAllBytes(Stage02RockfishAuthoring.ScenePath).SequenceEqual(rockfish))
                    throw new InvalidOperationException("Stage3 HUD authoring changed a Stage1/2 source scene.");
            }
        }

        private static GameObject Card(Stage01SalmonHUD shared, string name, string content, string primaryLabel,
            UnityAction primaryAction, UnityAction returnAction, out Text text)
        {
            var card = Object.Instantiate(shared.resultRoot, shared.resultRoot.transform.parent, false);
            card.name = name;
            text = Remap(shared.resultText, shared.resultRoot.transform, card.transform);
            text.text = content;
            var primary = RequiredButton(card, "Stage1Retry");
            primary.gameObject.SetActive(true); primary.GetComponentInChildren<Text>(true).text = primaryLabel;
            Bind(primary, primaryAction);
            var back = RequiredButton(card, "Stage1Return");
            back.GetComponentInChildren<Text>(true).text = "횟집으로";
            Bind(back, returnAction);
            return card;
        }

        private static Button RequiredButton(GameObject root, string name) => root.GetComponentsInChildren<Button>(true).Single(b => b.name == name);
        private static void Bind(Button button, UnityAction action)
        {
            ClearListeners(button);
            UnityEventTools.AddPersistentListener(button.onClick, action);
            EditorUtility.SetDirty(button);
        }
        private static void ClearListeners(Button button)
        {
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(button.onClick, i);
            button.onClick.RemoveAllListeners();
        }
        private static T Remap<T>(T source, Transform sourceRoot, Transform targetRoot) where T : Component
        {
            if (source == null || !source.transform.IsChildOf(sourceRoot))
                throw new InvalidOperationException("A baseline presentation reference is outside its cloned root: " + (source == null ? typeof(T).Name : source.name));
            string path = AnimationUtility.CalculateTransformPath(source.transform, sourceRoot);
            Transform target = path.Length == 0 ? targetRoot : targetRoot.Find(path);
            var component = target == null ? null : target.GetComponent<T>();
            if (component == null) throw new InvalidOperationException("A cloned presentation reference is missing: " + path);
            return component;
        }
        private static void StripGameplayScripts(GameObject root, Stage01SalmonHUD shared)
        {
            foreach (var script in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (script != null && script != shared && !(script is UIBehaviour)) Object.DestroyImmediate(script);
        }
        private static void ClearForeignSceneReferences(GameObject root, Scene targetScene)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) throw new InvalidOperationException("The baseline presentation has a Missing Script.");
                var serialized = new SerializedObject(component);
                var property = serialized.GetIterator();
                bool changed = false;
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var value = property.objectReferenceValue;
                    GameObject go = value as GameObject;
                    if (value is Component referenced) go = referenced.gameObject;
                    if (go == null || !go.scene.IsValid() || go.scene == targetScene) continue;
                    property.objectReferenceValue = null; changed = true;
                }
                if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        private static System.Collections.Generic.IEnumerable<T> All<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true));
    }
}
