using System;
using System.Collections.Generic;
using UnityEngine;

namespace SashimiBoy
{
    public enum Stage01NoteState
    {
        Upcoming,
        Hit,
        Missed
    }

    public sealed class Stage01RuntimeNote
    {
        public int sequenceIndex;
        public int playbackBarIndex;
        public int sourceBarIndex;
        public int eighthStepInBar;
        public double songTimeSeconds;
        public bool repeated;
        public Stage01NoteState state;
    }

    public sealed class Stage01NotePatternProvider : MonoBehaviour
    {
        public Stage01NotePatternDefinition pattern;
        public Stage01SalmonTimingScaffold timing;

        private readonly List<Stage01RuntimeNote> runtimeNotes =
            new List<Stage01RuntimeNote>(256);

        public IReadOnlyList<Stage01RuntimeNote> RuntimeNotes => runtimeNotes;
        public double PatternStartTimeSeconds { get; private set; }
        public bool IsInitialized { get; private set; }

        public void Initialize(Stage01SalmonTimingScaffold timingSource)
        {
            timing = timingSource;
            BuildRuntimeNotes();
        }

        public void ResetStates()
        {
            for (int i = 0; i < runtimeNotes.Count; i++)
            {
                runtimeNotes[i].state = Stage01NoteState.Upcoming;
            }
        }

        public int CopyFirstPatternNotes(
            int count,
            List<Stage01RuntimeNote> destination)
        {
            destination.Clear();
            int copyCount = Mathf.Min(count, runtimeNotes.Count);
            for (int i = 0; i < copyCount; i++)
            {
                destination.Add(runtimeNotes[i]);
            }

            return copyCount;
        }

        private void BuildRuntimeNotes()
        {
            runtimeNotes.Clear();
            IsInitialized = false;
            if (timing == null || pattern == null || pattern.notes == null ||
                pattern.notes.Count == 0)
            {
                return;
            }

            PatternStartTimeSeconds = timing.GetBeatTimeSeconds(
                timing.FirstGameplayBeatIndex);
            int subdivisions = Mathf.Max(1, pattern.subdivisionsPerBeat);
            int stepsPerBar = 4 * subdivisions;
            double stepLength = timing.BeatLengthSeconds / subdivisions;

            var ordered = new List<Stage01PatternNote>(pattern.notes);
            ordered.Sort(ComparePatternNotes);
            AddPatternRange(
                ordered,
                1,
                pattern.manualBarCount,
                0,
                false,
                stepsPerBar,
                stepLength);

            int repeatStart = Mathf.Clamp(
                pattern.repeatFromBar,
                1,
                pattern.manualBarCount);
            int repeatBarCount = pattern.manualBarCount - repeatStart + 1;
            int playbackBarOffset = pattern.manualBarCount;
            while (PatternStartTimeSeconds +
                playbackBarOffset * stepsPerBar * stepLength <
                timing.gameplayEndSec)
            {
                AddPatternRange(
                    ordered,
                    repeatStart,
                    pattern.manualBarCount,
                    playbackBarOffset - (repeatStart - 1),
                    true,
                    stepsPerBar,
                    stepLength);
                playbackBarOffset += repeatBarCount;
            }

            for (int i = 0; i < runtimeNotes.Count; i++)
            {
                runtimeNotes[i].sequenceIndex = i;
                runtimeNotes[i].state = Stage01NoteState.Upcoming;
            }

            IsInitialized = runtimeNotes.Count > 0;
        }

        private void AddPatternRange(
            List<Stage01PatternNote> ordered,
            int firstSourceBar,
            int lastSourceBar,
            int playbackBarDelta,
            bool repeated,
            int stepsPerBar,
            double stepLength)
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                Stage01PatternNote source = ordered[i];
                if (source == null || source.barIndex < firstSourceBar ||
                    source.barIndex > lastSourceBar)
                {
                    continue;
                }

                int playbackBar = source.barIndex + playbackBarDelta;
                int globalStep = (playbackBar - 1) * stepsPerBar +
                    source.eighthStepInBar;
                double songTime = PatternStartTimeSeconds +
                    globalStep * stepLength;
                if (songTime >= timing.gameplayEndSec)
                {
                    continue;
                }

                runtimeNotes.Add(new Stage01RuntimeNote
                {
                    playbackBarIndex = playbackBar,
                    sourceBarIndex = source.barIndex,
                    eighthStepInBar = source.eighthStepInBar,
                    songTimeSeconds = songTime,
                    repeated = repeated,
                    state = Stage01NoteState.Upcoming
                });
            }
        }

        private static int ComparePatternNotes(
            Stage01PatternNote left,
            Stage01PatternNote right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int barComparison = left.barIndex.CompareTo(right.barIndex);
            return barComparison != 0
                ? barComparison
                : left.eighthStepInBar.CompareTo(right.eighthStepInBar);
        }
    }
}
