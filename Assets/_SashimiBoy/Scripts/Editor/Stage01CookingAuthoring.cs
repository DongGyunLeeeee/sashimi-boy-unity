using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    public static partial class Stage01PlayableAuthoring
    {
        private static void RestoreCookingWorkspace(Scene scene)
        {
            var previous = Find<Stage01CookingView>(scene);
            if (previous == null) return;
            for (int i = 0; i < previous.calibratedRoots.Length; i++)
            {
                Transform root = previous.calibratedRoots[i]; if (root == null) continue;
                root.position = previous.originalPositions[i]; root.localScale = previous.originalScales[i];
            }
        }

        private static void ApplyCookingWorkspace(Scene scene, Stage01ButcheryPresenter view, Stage01SalmonPresentationController presentation)
        {
            const float scale = .27f;
            var cooking = view.gameObject.AddComponent<Stage01CookingView>(); cooking.butchery = view;
            var roots = scene.GetRootGameObjects().Where(o => o == view.gameObject ||
                new[] { "Stage01_VisualPrototype", "Stage01_Counter_BackBand", "Stage01_KitchenSurround", "Floor" }.Contains(o.name)).Select(o=>o.transform).ToArray();
            cooking.calibratedRoots = roots;
            cooking.originalPositions = roots.Select(t=>t.position).ToArray(); cooking.originalScales = roots.Select(t=>t.localScale).ToArray();
            foreach (var root in roots) { root.position = root.position * scale + Vector3.up * .815f; root.localScale *= scale; }
            // With the head to Kevin's right, his knife hand and support hand do not cross.
            view.transform.rotation = Quaternion.Euler(0f,180f,0f);
            view.transform.position += Vector3.back * .28f;
            view.knifeRoot.rotation = Quaternion.identity;
            view.tweezersRoot.rotation = Quaternion.Euler(0f,90f,0f);
            var plate = All<Transform>(scene).First(t=>t.name=="CompletedFishPlate");
            Vector3 platePosition = plate.position; platePosition.x = -platePosition.x; platePosition.z = -platePosition.z - .28f; plate.position = platePosition;
            view.reservedFilletHalf.transform.position = view.filletHalf.transform.position + Vector3.forward * .30f;
            var tray = view.transform.Find("RemovedPartsTray");
            tray.position = new Vector3(1.05f,tray.position.y,.36f);
            view.wasteAnchor.position = new Vector3(.97f,view.wasteAnchor.position.y,.32f);
            // Original plate placement is reset by ApplyToStageScene before every reapply.
            view.motionScale = scale;
            presentation.cameraDrivenByBody = true;
            float plateShift = .24f - plate.position.z;
            plate.position += Vector3.forward * plateShift;
            view.transform.Find("PlateSlots").position += Vector3.forward * plateShift;
            var cabinet = GameObject.CreatePrimitive(PrimitiveType.Cube); cabinet.name = "PrepStationCabinet";
            cabinet.transform.SetParent(view.transform,false); cabinet.transform.SetPositionAndRotation(new Vector3(0f,.40f,.06f),Quaternion.identity);
            cabinet.transform.localScale = new Vector3(2.85f,.80f,1.26f) / scale;
            Object.DestroyImmediate(cabinet.GetComponent<Collider>());
            var cabinetMaterial = GetMaterial("PrepStationCabinet","Standard"); cabinetMaterial.color = new Color(.13f,.17f,.19f); cabinetMaterial.SetFloat("_Glossiness",.22f);
            AssetDatabase.SaveAssetIfDirty(cabinetMaterial); cabinet.GetComponent<Renderer>().sharedMaterial = cabinetMaterial;
            Object.DestroyImmediate(view.handRoot.gameObject);
            view.handRoot = Child(view.transform,"RightGripTarget");
            var actor = Instance(KevinEmbodimentAuthoring.PrefabPath, view.transform, "Kevin_CompleteBody");
            actor.transform.localScale = Vector3.one / scale; actor.transform.rotation = Quaternion.identity;
            actor.transform.position = cooking.standingPosition;
            cooking.body = actor.GetComponent<KevinBodyRig>();
            cooking.gameCamera = Find<Camera>(scene);
            cooking.gameCamera.orthographic = false; cooking.gameCamera.fieldOfView = 67f; cooking.gameCamera.nearClipPlane = .035f;
            cooking.gameCamera.transform.SetPositionAndRotation(new Vector3(.40f, cooking.eyeHeight, -.50f), Quaternion.Euler(cooking.cookingPitch,0f,0f));
            cooking.body.SetHeadHidden(true);
            BuildFocusedHud(presentation.hud, presentation.sliceCue, presentation.judgementFeedback);
            OwnerJudgementAuthoring.Apply(presentation.hud);
            EditorUtility.SetDirty(cooking.gameCamera); EditorUtility.SetDirty(view); EditorUtility.SetDirty(cooking);
        }

        private static void PlaceUi(RectTransform rect, Transform parent, Vector2 position, Vector2 size)
        {
            rect.SetParent(parent,false); rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.anchoredPosition = position; rect.sizeDelta = size; rect.localScale = Vector3.one;
        }

        private static void BuildFocusedHud(Stage01SalmonHUD hud, SliceCuePresenter cue, JudgementFeedbackView feedback)
        {
            var dock = Panel(hud.transform,"CookingFocusHUD",Vector2.zero,new Vector2(1040f,220f),new Color(.025f,.035f,.04f,.88f));
            dock.rectTransform.anchorMin = dock.rectTransform.anchorMax = new Vector2(.5f,.73f);
            hud.compactLayout = true; hud.focusHudRoot = dock.gameObject;
            PlaceUi(hud.fishProgressText.rectTransform,dock.transform,new Vector2(-95f,82f),new Vector2(720f,38f));
            hud.fishProgressText.fontSize = 29; hud.fishProgressText.alignment = TextAnchor.MiddleLeft;
            PlaceUi(cue.rhythmLane,dock.transform,new Vector2(80f,25f),new Vector2(790f,45f));
            cue.hitCursor.rectTransform.sizeDelta = new Vector2(5f,45f);
            hud.lastJudgementText = TextObject(dock.transform,"LastJudgement","박자에 맞춰 Space",new Vector2(-220f,25f),new Vector2(380f,54f),38);
            hud.lastJudgementText.alignment = TextAnchor.MiddleRight;
            PlaceUi(hud.scoreText.rectTransform,dock.transform,new Vector2(-80f,-24f),new Vector2(275f,38f));
            PlaceUi(hud.comboText.rectTransform,dock.transform,new Vector2(220f,-24f),new Vector2(235f,38f));
            hud.scoreText.fontSize = 29; hud.comboText.fontSize = 29;
            hud.scoreText.alignment = hud.comboText.alignment = TextAnchor.MiddleCenter;
            PlaceUi(hud.phaseQualityText.rectTransform,dock.transform,new Vector2(-375f,-72f),new Vector2(255f,36f));
            PlaceUi(hud.phaseRequirementText.rectTransform,dock.transform,new Vector2(340f,-72f),new Vector2(305f,36f));
            hud.phaseQualityText.fontSize = hud.phaseRequirementText.fontSize = 24;
            PlaceUi(hud.phaseRemainingText.rectTransform,dock.transform,new Vector2(377f,82f),new Vector2(240f,36f));
            hud.phaseRemainingText.fontSize = 23;
            var track = Panel(dock.transform,"QualityTrack",new Vector2(-5f,-73f),new Vector2(410f,17f),new Color(.18f,.22f,.24f));
            PlaceUi(hud.phaseQualityFill.rectTransform,track.transform,Vector2.zero,track.rectTransform.sizeDelta);
            hud.phaseQualityFill.fillMethod = Image.FillMethod.Horizontal;
            PlaceUi(hud.phaseThresholdMarker,track.transform,Vector2.zero,new Vector2(3f,29f));
            foreach (var child in dock.GetComponentsInChildren<Transform>(true))
            {
                bool duplicate = child.name == "Current" && child != hud.phaseQualityText.transform ||
                    child.name == "Required" && child != hud.phaseRequirementText.transform ||
                    child.name == "Remaining" && child != hud.phaseRemainingText.transform ||
                    child.name == "Fill" && child != hud.phaseQualityFill.transform ||
                    child.name == "PassLine" && child != hud.phaseThresholdMarker;
                if (duplicate) Object.DestroyImmediate(child.gameObject);
            }
            foreach (string name in new[] { "TopLeft_Status", "TopRight_Score", "TopCenter_Rhythm", "PhaseQualityGauge" })
            { Transform old = hud.transform.Find(name); if (old != null) old.gameObject.SetActive(false); }
            // Keep legacy badge wiring available, while one persistent label carries readable outcome near the hit line.
            if (feedback != null) { feedback.canvasGroup.alpha = 0f; feedback.canvasGroup.gameObject.SetActive(true);
                PlaceUi(feedback.GetComponent<RectTransform>(),hud.transform,new Vector2(0f,-1400f),new Vector2(220f,160f)); }
            foreach (var prompt in new[] { cue.spacePromptText,cue.nowText,cue.restPromptText })
                if (prompt != null) { PlaceUi(prompt.rectTransform,hud.transform,Vector2.zero,new Vector2(600f,32f));
                    prompt.rectTransform.anchorMin = prompt.rectTransform.anchorMax = new Vector2(.5f,.595f); prompt.fontSize = 21; }
            if (hud.noteEventText != null) hud.noteEventText.gameObject.SetActive(false);
            var dialogueParent = hud.dialogueText.transform.parent;
            PlaceUi(hud.dialogueText.rectTransform,hud.transform,Vector2.zero,new Vector2(920f,32f));
            hud.dialogueText.rectTransform.anchorMin = hud.dialogueText.rectTransform.anchorMax = new Vector2(.5f,.055f);
            hud.dialogueText.fontSize = 21; hud.dialogueText.alignment = TextAnchor.MiddleCenter;
            if (dialogueParent != hud.transform) dialogueParent.gameObject.SetActive(false);
            var result = hud.resultRoot.GetComponent<RectTransform>();
            PlaceUi(result,hud.transform,Vector2.zero,new Vector2(650f,400f)); result.anchorMin = result.anchorMax = new Vector2(.77f,.75f);
            PlaceUi(hud.resultText.rectTransform,result,new Vector2(0f,35f),new Vector2(590f,270f)); hud.resultText.fontSize = 30;
            EditorUtility.SetDirty(hud); EditorUtility.SetDirty(cue); if (feedback != null) EditorUtility.SetDirty(feedback);
        }
    }
}
