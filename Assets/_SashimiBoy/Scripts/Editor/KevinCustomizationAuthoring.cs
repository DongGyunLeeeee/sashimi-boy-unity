using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    public static class KevinCustomizationAuthoring
    {
        public const string CatalogPath = "Assets/_SashimiBoy/Art/Generated/Data/KevinFaceCatalog.asset";
        static readonly Color Ink = new Color(.065f, .09f, .10f);

        public static void BuildFaces()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<KevinFaceCatalog>(CatalogPath);
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<KevinFaceCatalog>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
            var body = PrefabUtility.LoadPrefabContents(KevinEmbodimentAuthoring.PrefabPath);
            try
            {
                var rig = body.GetComponent<KevinBodyRig>();
                var original = (SkinnedMeshRenderer)rig.headRenderers.Single();
                var bones = body.GetComponentsInChildren<Transform>(true);
                string[] ids = { "CuteFace", "PlainFace", "AmbiguousFace", "WesternFace" };
                string[] labels = { "귀여운 얼굴", "수수한 얼굴", "묘한 얼굴", "서구적인 얼굴" };
                catalog.choices = new KevinFaceChoice[ids.Length];
                for (int i = 0; i < ids.Length; i++)
                {
                    var renderer = original;
                    if (ids[i] != "CuteFace")
                        renderer = KevinEmbodimentAuthoring.BuildOwnerHead(rig.head,
                            bones.Single(t => t.name == "Neck"), bones.Single(t => t.name == "Chest"), ids[i]).GetComponent<SkinnedMeshRenderer>();
                    if (renderer.sharedMesh.vertexCount < 100) throw new InvalidOperationException("Missing head geometry: " + ids[i]);
                    catalog.choices[i] = new KevinFaceChoice { id = ids[i], displayName = labels[i], mesh = renderer.sharedMesh, material = renderer.sharedMaterial };
                    if (renderer != original) Object.DestroyImmediate(renderer.gameObject);
                }
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssetIfDirty(catalog);
                var appearance = body.GetComponent<KevinAppearance>() ?? body.AddComponent<KevinAppearance>();
                appearance.catalog = catalog;
                appearance.faceRenderer = original;
                PrefabUtility.SaveAsPrefabAsset(body, KevinEmbodimentAuthoring.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(body); }
        }

        public static void ApplyMenu(DayWorldSceneDirector director)
        {
            if (director.menuRoot == null) return;
            Transform menu = director.menuRoot.transform, canvas = menu.parent;
            Font font = menu.GetComponentInChildren<Text>(true).font;
            menu.GetComponent<Image>().color = Color.white;
            menu.Find("Title").gameObject.SetActive(false);
            var logo = Rect(menu, "OwnerLogo", new Vector2(.33f, .56f), new Vector2(560, 560)).gameObject;
            var image = logo.GetComponent<Image>() ?? logo.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_SashimiBoy/Art/Source/Branding/SashimiBoyLogo.png");
            if (image.sprite == null) throw new InvalidOperationException("Owner logo sprite is required.");
            image.preserveAspect = true; image.raycastTarget = false; image.color = Color.white;
            var subtitle = menu.Find("Subtitle").GetComponent<Text>();
            subtitle.text = "새로운 하루"; subtitle.color = Ink; subtitle.fontSize = 34;
            Rect(menu, "Subtitle", new Vector2(.73f, .65f), new Vector2(380, 65));
            PositionButton(director.newGameButton, new Vector2(.73f, .52f), "새 게임 시작하기");
            PositionButton(director.continueButton, new Vector2(.73f, .40f), "이어하기");
            var controls = menu.Find("Controls").GetComponent<Text>();
            controls.color = new Color(.31f, .36f, .36f); controls.fontSize = 21;
            Rect(menu, "Controls", new Vector2(.5f, .12f), new Vector2(1100, 100));

            var screen = director.GetComponent<KevinCustomizationScreen>() ?? director.gameObject.AddComponent<KevinCustomizationScreen>();
            director.customization = screen; screen.titlePanel = menu.gameObject;
            screen.catalog = AssetDatabase.LoadAssetAtPath<KevinFaceCatalog>(CatalogPath);
            var panel = Rect(canvas, "KevinCustomization", Vector2.one * .5f, new Vector2(1600, 900));
            (panel.GetComponent<Image>() ?? panel.gameObject.AddComponent<Image>()).color = Ink;
            screen.panel = panel.gameObject;
            Text(panel, font, "Heading", "케빈의 얼굴을 골라주세요", new Vector2(.5f, .91f), new Vector2(1100, 60), 38);
            Text(panel, font, "Hint", "선택한 얼굴로 첫째 날을 시작합니다.", new Vector2(.5f, .84f), new Vector2(1100, 45), 23);
            var portrait = Rect(panel, "Portrait", new Vector2(.29f, .47f), new Vector2(540, 600));
            screen.portrait = portrait.GetComponent<RawImage>() ?? portrait.gameObject.AddComponent<RawImage>();
            screen.portrait.raycastTarget = false;
            screen.selectedName = Text(panel, font, "SelectedFace", "", new Vector2(.71f, .74f), new Vector2(520, 50), 31);
            screen.choiceButtons = new Button[screen.catalog.choices.Length];
            for (int i = 0; i < screen.choiceButtons.Length; i++)
                screen.choiceButtons[i] = Button(panel, font, "Choose_" + screen.catalog.choices[i].id,
                    screen.catalog.choices[i].displayName, new Vector2(.71f, .64f - i * .093f), new Vector2(420, 65));
            screen.confirmButton = Button(panel, font, "ConfirmFace", "이 얼굴로 시작하기", new Vector2(.71f, .22f), new Vector2(420, 72));
            screen.cancelButton = Button(panel, font, "CancelFace", "돌아가기", new Vector2(.71f, .12f), new Vector2(420, 58));
            screen.cancelButton.GetComponent<Image>().color = new Color(.16f, .20f, .22f);

            var oldPreview = director.transform.Find("KevinCustomizationPreview");
            if (oldPreview != null) Object.DestroyImmediate(oldPreview.gameObject);
            var previewRoot = new GameObject("KevinCustomizationPreview"); previewRoot.transform.SetParent(director.transform, false);
            previewRoot.transform.position = new Vector3(50f, 0f, 0f);
            var actor = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(KevinEmbodimentAuthoring.PrefabPath), previewRoot.transform);
            actor.transform.localPosition = Vector3.zero;
            screen.preview = actor.GetComponent<KevinAppearance>();
            screen.previewRoot = previewRoot;
            foreach (var t in actor.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
            var camera = new GameObject("PortraitCamera").AddComponent<Camera>(); camera.transform.SetParent(previewRoot.transform, false);
            camera.transform.localPosition = new Vector3(.15f, 1.63f, 1.8f);
            camera.transform.LookAt(previewRoot.transform.position + new Vector3(0f, 1.47f, 0f));
            camera.orthographic = true; camera.orthographicSize = .49f; camera.nearClipPlane = .05f; camera.farClipPlane = 5f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.25f, .32f, .33f); camera.cullingMask = 1 << 31;
            screen.previewCamera = camera;
            var key = new GameObject("PortraitKey").AddComponent<Light>(); key.transform.SetParent(previewRoot.transform, false);
            key.transform.localPosition = new Vector3(-.7f, 2.1f, 1f); key.type = LightType.Point; key.range = 4f; key.intensity = 2.5f; key.cullingMask = 1 << 31;
            var fill = new GameObject("PortraitFill").AddComponent<Light>(); fill.transform.SetParent(previewRoot.transform, false);
            fill.transform.localPosition = new Vector3(.8f, 1.7f, .6f); fill.type = LightType.Point; fill.range = 3f; fill.intensity = 1.4f; fill.cullingMask = 1 << 31;
            panel.gameObject.SetActive(false); previewRoot.SetActive(false);
        }

        static void PositionButton(Button button, Vector2 anchor, string label)
        {
            Rect(button.transform.parent, button.name, anchor, new Vector2(350, 76));
            button.GetComponent<Image>().color = new Color(.12f, .36f, .35f);
            var text = button.GetComponentInChildren<Text>(); text.text = label; text.rectTransform.sizeDelta = new Vector2(330, 70); text.fontSize = 27;
        }
        static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 size)
        {
            var previous = parent.Find(name);
            var rect = previous != null ? (RectTransform)previous : new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = anchor; rect.anchoredPosition = Vector2.zero; rect.sizeDelta = size;
            return rect;
        }
        static Text Text(Transform parent, Font font, string name, string value, Vector2 anchor, Vector2 size, int fontSize)
        {
            var rect = Rect(parent, name, anchor, size); var text = rect.GetComponent<Text>() ?? rect.gameObject.AddComponent<Text>();
            text.font = font; text.text = value; text.color = Color.white; text.fontSize = fontSize; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
            return text;
        }
        static Button Button(Transform parent, Font font, string name, string label, Vector2 anchor, Vector2 size)
        {
            var rect = Rect(parent, name, anchor, size);
            (rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>()).color = new Color(.17f, .49f, .47f);
            var button = rect.GetComponent<Button>() ?? rect.gameObject.AddComponent<Button>();
            Text(rect, font, "Label", label, Vector2.one * .5f, size - new Vector2(16, 8), 25);
            return button;
        }
    }
}
