using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    public sealed class KevinCustomizationScreen : MonoBehaviour
    {
        public GameObject panel, titlePanel, previewRoot;
        public KevinFaceCatalog catalog;
        public KevinAppearance preview;
        public Camera previewCamera;
        public RawImage portrait;
        public Text selectedName;
        public Button[] choiceButtons;
        public Button confirmButton, cancelButton;
        private RenderTexture target;
        private int selected;
        private bool starting;
        public string SelectedFaceId => catalog.choices[selected].id;
        public bool IsOpen => panel != null && panel.activeSelf;

        private void Awake()
        {
            for (int i = 0; i < choiceButtons.Length; i++)
            {
                int index = i;
                choiceButtons[i].onClick.AddListener(() => SelectFace(index));
            }
            confirmButton.onClick.AddListener(Confirm);
            cancelButton.onClick.AddListener(Cancel);
            panel.SetActive(false);
            previewRoot.SetActive(false);
        }

        public void Open()
        {
            if (starting || SceneTransitionService.Instance.IsLoading) return;
            if (target == null)
            {
                target = new RenderTexture(720, 800, 24) { name = "KevinFacePreview", antiAliasing = 2 };
                target.Create();
                previewCamera.targetTexture = target;
                portrait.texture = target;
            }
            titlePanel.SetActive(false);
            panel.SetActive(true);
            previewRoot.SetActive(true);
            SelectFace(0);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            choiceButtons[0].Select();
        }

        public void SelectFace(int index)
        {
            if (index < 0 || index >= catalog.choices.Length || starting) return;
            selected = index;
            preview.ApplyFace(SelectedFaceId);
            selectedName.text = catalog.choices[index].displayName;
            for (int i = 0; i < choiceButtons.Length; i++)
                choiceButtons[i].GetComponent<Image>().color = i == index
                    ? new Color(.17f, .49f, .47f) : new Color(.16f, .20f, .22f);
        }

        public void Confirm()
        {
            if (!IsOpen || starting || SceneTransitionService.Instance.IsLoading) return;
            starting = true;
            confirmButton.interactable = false;
            // Selection is a draft until this button; cancelling never resets an existing save.
            DayWorldFlow.Instance.NewGameWithFace(SelectedFaceId);
        }

        public void Cancel()
        {
            if (!IsOpen || starting) return;
            panel.SetActive(false);
            previewRoot.SetActive(false);
            titlePanel.SetActive(true);
        }

        private void Update() { if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Cancel(); }
        private void OnDestroy()
        {
            if (target == null) return;
            if (previewCamera != null) previewCamera.targetTexture = null;
            target.Release();
            Destroy(target);
        }
    }
}
