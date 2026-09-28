using UnityEngine;
namespace SashimiBoy
{
    // Small, deterministic kick accents after the body camera updates. Timing and hit windows are untouched.
    [DefaultExecutionOrder(400)]
    public sealed class RockfishStageAccent : MonoBehaviour
    {
        public Stage01SalmonTimingScaffold timing;
        public Camera gameCamera;
        private System.Collections.Generic.List<DistractionCue> cues;
        private void Awake()=>cues=ContentDefaults.FindStage(SashimiBoyConstants.StageIds.Rockfish).distractionCues;
        private void LateUpdate()
        {
            if(timing==null || gameCamera==null || timing.IsResultShown || timing.PhasePerformance.Failed)return;
            double beat=(timing.SongTimeSeconds-timing.firstDownbeatSec)/timing.BeatLengthSeconds;
            foreach(var cue in cues)
            {
                double progress=(beat-cue.beat)/cue.durationBeats;
                if(progress>=0d && progress<=1d)
                    gameCamera.transform.rotation*=Quaternion.Euler(0,0,Mathf.Sin((float)progress*Mathf.PI*4f)*cue.intensity*(1f-(float)progress));
            }
        }
    }
}
