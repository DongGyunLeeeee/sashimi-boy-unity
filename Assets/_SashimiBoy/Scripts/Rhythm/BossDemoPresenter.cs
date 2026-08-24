using System.Collections.Generic;
using UnityEngine;

namespace SashimiBoy
{
    public sealed class BossDemoPresenter : MonoBehaviour
    {
        private const int DemoNoteCount = 4;

        public Stage01SalmonTimingScaffold timing;
        public Stage01NotePatternProvider patternProvider;
        public ProceduralSalmonView salmon;
        public GameObject bossSilhouetteRoot;
        public KnifeVisualController bossKnife;
        public Stage01SalmonHUD hud;

        private readonly List<Stage01RuntimeNote> sourceNotes =
            new List<Stage01RuntimeNote>(DemoNoteCount);
        private readonly List<double> demoNoteTimes =
            new List<double>(DemoNoteCount);
        private int lastTriggeredDemoOrdinal = -1;
        private bool gameplayInputAcknowledged;

        public IReadOnlyList<double> DemoNoteTimes => demoNoteTimes;

        private void Update()
        {
            if (timing == null)
            {
                return;
            }

            if (demoNoteTimes.Count == 0 &&
                patternProvider != null &&
                patternProvider.IsInitialized)
            {
                BuildDemoPattern();
            }

            double songSec = timing.SongTimeSeconds;
            bool demoVisible = songSec >= timing.firstDownbeatSec &&
                songSec < timing.gameplayStartSec;
            if (demoVisible)
            {
                gameplayInputAcknowledged = false;
            }

            bool knifeVisible = demoVisible &&
                songSec < timing.CountdownStartSeconds;
            SetBossVisible(demoVisible, knifeVisible);
            RefreshCountdown(songSec);

            if (!knifeVisible || bossKnife == null ||
                demoNoteTimes.Count == 0)
            {
                return;
            }

            int nextDemoOrdinal = -1;
            for (int i = 0; i < demoNoteTimes.Count; i++)
            {
                double noteSec = demoNoteTimes[i];
                if (songSec >= noteSec && songSec <= noteSec + 0.12d &&
                    lastTriggeredDemoOrdinal < i)
                {
                    lastTriggeredDemoOrdinal = i;
                    if (salmon != null)
                    {
                        bossKnife.SetTargetWorld(
                            salmon.GetCutWorldPosition(i));
                    }

                    bossKnife.PlaySlice(JudgeGrade.Smooth);
                }

                if (nextDemoOrdinal < 0 && songSec < noteSec)
                {
                    nextDemoOrdinal = i;
                }
            }

            if (nextDemoOrdinal >= 0)
            {
                double nextSec = demoNoteTimes[nextDemoOrdinal];
                if (salmon != null)
                {
                    bossKnife.SetTargetWorld(
                        salmon.GetCutWorldPosition(nextDemoOrdinal));
                }

                float windup = Mathf.Clamp01((float)(
                    1d - (nextSec - songSec) / timing.BeatLengthSeconds));
                bossKnife.SetWindup(windup);
            }
        }

        public void Bind(
            Stage01SalmonTimingScaffold timingSource,
            Stage01SalmonHUD stageHud,
            Stage01NotePatternProvider stagePatternProvider,
            ProceduralSalmonView salmonView)
        {
            timing = timingSource;
            hud = stageHud;
            patternProvider = stagePatternProvider;
            salmon = salmonView;
            lastTriggeredDemoOrdinal = -1;
            gameplayInputAcknowledged = false;
            BuildDemoPattern();
        }

        public int CopyUpcomingDemoNotes(
            double songSec,
            int count,
            List<double> destination,
            out int firstOrdinal)
        {
            destination.Clear();
            firstOrdinal = -1;
            if (timing == null || demoNoteTimes.Count == 0 || count <= 0)
            {
                return 0;
            }

            double leadSeconds = timing.BeatLengthSeconds * 2d;
            if (songSec < demoNoteTimes[0] - leadSeconds ||
                songSec > demoNoteTimes[demoNoteTimes.Count - 1] + 0.14d)
            {
                return 0;
            }

            for (int i = 0;
                i < demoNoteTimes.Count && destination.Count < count;
                i++)
            {
                if (songSec > demoNoteTimes[i] + 0.14d)
                {
                    continue;
                }

                if (firstOrdinal < 0)
                {
                    firstOrdinal = i;
                }

                destination.Add(demoNoteTimes[i]);
            }

            return destination.Count;
        }

        public void NotifyGameplayInput()
        {
            gameplayInputAcknowledged = true;
            hud?.SetCountdownLabel(string.Empty);
        }

        private void BuildDemoPattern()
        {
            sourceNotes.Clear();
            demoNoteTimes.Clear();
            if (timing == null || patternProvider == null ||
                !patternProvider.IsInitialized)
            {
                return;
            }

            patternProvider.CopyFirstPatternNotes(
                DemoNoteCount,
                sourceNotes);
            if (sourceNotes.Count == 0)
            {
                return;
            }

            double demoStartSec = timing.GetBeatTimeSeconds(8);
            double sourceStartSec = sourceNotes[0].songTimeSeconds;
            for (int i = 0; i < sourceNotes.Count; i++)
            {
                demoNoteTimes.Add(
                    demoStartSec +
                    sourceNotes[i].songTimeSeconds - sourceStartSec);
            }
        }

        private void RefreshCountdown(double songSec)
        {
            if (hud == null)
            {
                return;
            }

            string label = string.Empty;
            if (songSec >= timing.CountdownStartSeconds &&
                songSec < timing.gameplayStartSec)
            {
                int elapsedBeats = Mathf.FloorToInt((float)(
                    (songSec - timing.CountdownStartSeconds) /
                    timing.BeatLengthSeconds));
                label = Mathf.Clamp(4 - elapsedBeats, 1, 4).ToString();
            }
            else if (!gameplayInputAcknowledged &&
                songSec >= timing.gameplayStartSec &&
                songSec < timing.gameplayStartSec + 0.32d)
            {
                label = "SLICE!";
            }

            hud.SetCountdownLabel(label);
        }

        private void SetBossVisible(bool silhouetteVisible, bool knifeVisible)
        {
            if (bossSilhouetteRoot != null &&
                bossSilhouetteRoot.activeSelf != silhouetteVisible)
            {
                bossSilhouetteRoot.SetActive(silhouetteVisible);
            }

            bossKnife?.SetVisible(knifeVisible);
        }
    }
}
