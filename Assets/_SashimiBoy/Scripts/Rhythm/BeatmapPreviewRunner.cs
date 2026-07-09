using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    /// <summary>
    /// Generic beatmap tester. It does not include stage 01 content; assign any BeatmapDefinition later.
    /// </summary>
    public sealed class BeatmapPreviewRunner : MonoBehaviour
    {
        public AudioClock audioClock;
        public BeatmapDefinition beatmap;
        public SliceInputRouter inputRouter;
        public Text debugText;
        public RhythmDifficulty difficulty = RhythmDifficulty.Normal;

        private int nextNoteIndex;
        private readonly SliceScoreCalculator score = new SliceScoreCalculator();

        private void OnEnable()
        {
            if (inputRouter != null)
            {
                inputRouter.OnInput += HandleInput;
            }
        }

        private void OnDisable()
        {
            if (inputRouter != null)
            {
                inputRouter.OnInput -= HandleInput;
            }
        }

        private void Start()
        {
            if (audioClock != null && !audioClock.IsRunning)
            {
                audioClock.Play();
            }
        }

        private void Update()
        {
            if (beatmap == null || audioClock == null)
            {
                return;
            }

            double songTime = audioClock.SongTimeMs;
            float window = RhythmJudge.GetMaxWindowMs(beatmap.bpm, difficulty);

            while (nextNoteIndex < beatmap.events.Count)
            {
                double noteMs = beatmap.events[nextNoteIndex].TimeMs(beatmap.bpm);
                if (songTime <= noteMs + window)
                {
                    break;
                }

                JudgeResult miss = RhythmJudge.JudgeOffset(beatmap.bpm, window + 1f, difficulty);
                score.AddJudge(miss);
                nextNoteIndex++;
                SetDebug($"MISS / Score {score.Score:0}");
            }
        }

        private void HandleInput(SliceInputType inputType)
        {
            if (beatmap == null || audioClock == null || nextNoteIndex >= beatmap.events.Count)
            {
                return;
            }

            double inputMs = audioClock.SongTimeMs;
            BeatmapEvent note = beatmap.events[nextNoteIndex];
            double noteMs = note.TimeMs(beatmap.bpm);
            float offset = SaveManager.Instance != null ? SaveManager.Instance.Current.globalJudgeOffsetMs : 0f;
            JudgeResult result = RhythmJudge.Judge(beatmap.bpm, inputMs, noteMs, offset, difficulty);

            if (result.grade == JudgeGrade.Whack)
            {
                SetDebug($"WHACK {result.offsetMs:0.0}ms");
                return;
            }

            score.AddJudge(result);
            nextNoteIndex++;
            SetDebug($"{result} / Score {score.Score:0}");
        }

        private void SetDebug(string message)
        {
            if (debugText != null)
            {
                debugText.text = message;
            }
        }
    }
}
