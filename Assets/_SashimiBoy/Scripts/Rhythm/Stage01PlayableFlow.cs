using System.Collections;
using SashimiBoy.Semantics;
using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    public sealed class Stage01PlayableFlow : MonoBehaviour
    {
        public Stage01SalmonTimingScaffold timing;
        public Stage01SalmonHUD hud;
        public Stage01FailureEffect failureEffect;
        public Text failureText;
        public Button retryButton, returnButton;
        public float failureReturnSeconds = 1.4f;
        public bool IsReturning { get; private set; }
        private Coroutine failureRoutine;

        private void OnEnable()
        {
            if (timing != null)
            {
                timing.FailureReturnRequested += OnFailure;
                timing.SemanticEventRaised += OnSemanticEvent;
            }
            retryButton?.onClick.AddListener(Retry);
            returnButton?.onClick.AddListener(ReturnToShop);
        }

        private void OnDisable()
        {
            if (timing != null)
            {
                timing.FailureReturnRequested -= OnFailure;
                timing.SemanticEventRaised -= OnSemanticEvent;
            }
            retryButton?.onClick.RemoveListener(Retry);
            returnButton?.onClick.RemoveListener(ReturnToShop);
            if (failureRoutine != null) StopCoroutine(failureRoutine);
            failureRoutine = null;
        }

        private void Start()
        {
            ResetPresentation();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            if (timing == null || !timing.IsResultShown || IsReturning) return;
            if (Input.GetKeyDown(KeyCode.R)) Retry();
            else if (Input.GetKeyDown(KeyCode.F)) ReturnToShop();
        }

        private void OnSemanticEvent(SemanticEvent value)
        {
            if (value.kind == SemanticEventKind.RunReset) ResetPresentation();
        }

        private void ResetPresentation()
        {
            if (failureRoutine != null) StopCoroutine(failureRoutine);
            failureRoutine = null;
            IsReturning = false;
            if (failureEffect != null) failureEffect.amount = 0f;
            if (failureText != null) failureText.gameObject.SetActive(false);
            hud?.ResetForRetry();
            if (timing != null && timing.presentationController != null)
                timing.presentationController.ResetForRetry();
        }

        private void OnFailure(SemanticEvent value)
        {
            if (IsReturning) return;
            IsReturning = true;
            failureRoutine = StartCoroutine(FailAndReturn());
        }

        private IEnumerator FailAndReturn()
        {
            if (failureText != null)
            {
                failureText.text = "손질 실패\n횟집으로 돌아갑니다";
                failureText.gameObject.SetActive(true);
            }
            float elapsed = 0f;
            while (elapsed < failureReturnSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                if (failureEffect != null) failureEffect.amount = Mathf.Clamp01(elapsed / .75f);
                yield return null;
            }
            failureRoutine = null;
            Stage01ShopReturn.Request();
        }

        public void Retry()
        {
            if (timing == null || IsReturning || !timing.IsResultShown) return;
            if (timing.RetryStage()) timing.StartStagePlayback();
        }

        public void ReturnToShop()
        {
            if (timing == null || IsReturning || !timing.IsResultShown) return;
            IsReturning = true;
            timing.audioClock?.Stop();
            Stage01ShopReturn.Request();
        }
    }
}
