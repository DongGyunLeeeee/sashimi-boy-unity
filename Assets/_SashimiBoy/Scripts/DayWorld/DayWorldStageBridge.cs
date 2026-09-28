using UnityEngine;
namespace SashimiBoy
{
    // Scene-entry/result adapter only. It does not author notes, score, fish or judgement.
    public sealed class DayWorldStageBridge : MonoBehaviour
    {
        private void Awake()
        {
            foreach(var hotkeys in FindObjectsByType<PrototypeDebugHotkeys>())hotkeys.enabled=false;
            foreach(var panel in FindObjectsByType<PrototypeDebugPanel>())panel.enabled=false;
        }
        private void Update()
        {
            if (!DayWorldFlow.Active || !Input.GetKeyDown(KeyCode.Escape) || DayWorldFlow.InputSuppressed) return;
            ReturnToShop();
        }
        public void ReturnToShop()
        {
            if(!DayWorldFlow.Active || DayWorldFlow.InputSuppressed) return;
            var timing = FindAnyObjectByType<Stage01SalmonTimingScaffold>();
            if (timing != null) timing.audioClock?.Stop();
            DayWorldFlow.Instance.SuppressInput();
            Stage01ShopReturn.Request();
        }
    }
}
