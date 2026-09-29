using System;
using System.Collections.Generic;
using System.Threading;
using SashimiBoy.Semantics;
using UnityEngine;

namespace SashimiBoy
{
    public enum Stage01NoteInputKind
    {
        Hit,
        Empty
    }

    public readonly struct Stage01NoteInputOutcome
    {
        public readonly Stage01NoteInputKind kind;
        public readonly Stage01RuntimeNote note;
        public readonly JudgeResult judge;
        public readonly long physicalInputId;

        public Stage01NoteInputOutcome(
            Stage01NoteInputKind kind,
            Stage01RuntimeNote note,
            JudgeResult judge, long physicalInputId = 0)
        {
            this.kind = kind;
            this.note = note;
            this.judge = judge;
            this.physicalInputId = physicalInputId;
        }
    }

    public sealed class Stage01ActiveNoteTracker : MonoBehaviour
    {
        public Stage01NotePatternProvider provider;

        public event Action<Stage01RuntimeNote> NoteMissed;

        private int nextUnresolvedIndex;
        private static long nextRunId;
        private long generatedInputId;
        private readonly HashSet<long> consumedPhysicalInputs = new HashSet<long>();
        public long RunId { get; private set; }
        public bool ResolutionBlocked { get; set; }
        public event Action<JudgementOutcome> OutcomeResolved;

        public int HitCount { get; private set; }
        public int MissCount { get; private set; }
        public int EmptyHitCount { get; private set; }
        public bool IsInitialized => provider != null && provider.IsInitialized;
        public int UnresolvedCount
        {
            get
            {
                AdvancePastResolvedNotes();
                return Notes.Count - nextUnresolvedIndex;
            }
        }

        public Stage01RuntimeNote NextActiveNote
        {
            get
            {
                AdvancePastResolvedNotes();
                IReadOnlyList<Stage01RuntimeNote> notes = Notes;
                return nextUnresolvedIndex < notes.Count
                    ? notes[nextUnresolvedIndex]
                    : null;
            }
        }

        private IReadOnlyList<Stage01RuntimeNote> Notes =>
            provider != null
                ? provider.RuntimeNotes
                : Array.Empty<Stage01RuntimeNote>();

        public void Initialize(Stage01NotePatternProvider patternProvider)
        {
            RunId = Interlocked.Increment(ref nextRunId);
            consumedPhysicalInputs.Clear();
            generatedInputId = 0;
            ResolutionBlocked = false;
            provider = patternProvider;
            provider?.ResetStates();
            nextUnresolvedIndex = 0;
            HitCount = 0;
            MissCount = 0;
            EmptyHitCount = 0;
        }

        public void ProcessExpiredNotes(
            double songTimeSeconds,
            double lateWindowMilliseconds)
        {
            IReadOnlyList<Stage01RuntimeNote> notes = Notes;
            if (!SemanticChartValidator.Finite(songTimeSeconds) || !SemanticChartValidator.Finite(lateWindowMilliseconds) || lateWindowMilliseconds < 0d) return;
            double lateWindowSeconds = lateWindowMilliseconds / 1000d;
            AdvancePastResolvedNotes();
            while (!ResolutionBlocked && nextUnresolvedIndex < notes.Count)
            {
                Stage01RuntimeNote note = notes[nextUnresolvedIndex];
                if (songTimeSeconds <=
                    note.songTimeSeconds + lateWindowSeconds)
                {
                    break;
                }

                ResolveNextNoteAsMissed(note);
            }
        }

        public void ResolveRemainingNotesAsMissed()
        {
            IReadOnlyList<Stage01RuntimeNote> notes = Notes;
            AdvancePastResolvedNotes();
            while (!ResolutionBlocked && nextUnresolvedIndex < notes.Count)
            {
                ResolveNextNoteAsMissed(notes[nextUnresolvedIndex]);
            }
        }

        public Stage01NoteInputOutcome JudgeInput(
            double inputSongTimeSeconds,
            double nastyWindowMilliseconds,
            double smoothWindowMilliseconds,
            double slippedWindowMilliseconds)
        {
            return JudgePhysicalInput(inputSongTimeSeconds, nastyWindowMilliseconds,
                smoothWindowMilliseconds, slippedWindowMilliseconds, ++generatedInputId);
        }

        public Stage01NoteInputOutcome JudgePhysicalInput(
            double inputSongTimeSeconds, double nastyWindowMilliseconds,
            double smoothWindowMilliseconds, double slippedWindowMilliseconds, long physicalInputId)
        {
            if (!SemanticChartValidator.Finite(inputSongTimeSeconds) ||
                !SemanticChartValidator.Finite(nastyWindowMilliseconds) ||
                !SemanticChartValidator.Finite(smoothWindowMilliseconds) ||
                !SemanticChartValidator.Finite(slippedWindowMilliseconds) ||
                nastyWindowMilliseconds < 0d || smoothWindowMilliseconds < nastyWindowMilliseconds ||
                slippedWindowMilliseconds < smoothWindowMilliseconds || ResolutionBlocked || physicalInputId <= 0 ||
                !consumedPhysicalInputs.Add(physicalInputId))
                return new Stage01NoteInputOutcome(Stage01NoteInputKind.Empty, null, default);
            ProcessExpiredNotes(
                inputSongTimeSeconds,
                slippedWindowMilliseconds);
            // An expired note may have failed the gate during this same input edge.
            if (ResolutionBlocked)
                return new Stage01NoteInputOutcome(Stage01NoteInputKind.Empty, null, default);
            Stage01RuntimeNote note = NextActiveNote;
            if (note == null)
            {
                EmptyHitCount++;
                return new Stage01NoteInputOutcome(
                    Stage01NoteInputKind.Empty,
                    null,
                    default, physicalInputId);
            }

            double offsetMilliseconds =
                (inputSongTimeSeconds - note.songTimeSeconds) * 1000d;
            if (Math.Abs(offsetMilliseconds) > slippedWindowMilliseconds)
            {
                EmptyHitCount++;
                return new Stage01NoteInputOutcome(
                    Stage01NoteInputKind.Empty,
                    null,
                    default, physicalInputId);
            }

            JudgeResult judge = RhythmJudge.JudgeFixedWindows(
                offsetMilliseconds,
                nastyWindowMilliseconds,
                smoothWindowMilliseconds,
                slippedWindowMilliseconds);
            note.state = Stage01NoteState.Hit;
            HitCount++;
            nextUnresolvedIndex++;
            AdvancePastResolvedNotes();
            OutcomeResolved?.Invoke(new JudgementOutcome(RunId, note.sequenceIndex,
                ToQualityGrade(judge.grade), physicalInputId, false));
            return new Stage01NoteInputOutcome(
                Stage01NoteInputKind.Hit,
                note,
                judge, physicalInputId);
        }

        public int CopyUpcomingNotes(
            int count,
            List<Stage01RuntimeNote> destination)
        {
            destination.Clear();
            AdvancePastResolvedNotes();
            IReadOnlyList<Stage01RuntimeNote> notes = Notes;
            for (int i = nextUnresolvedIndex;
                i < notes.Count && destination.Count < count;
                i++)
            {
                if (notes[i].state == Stage01NoteState.Upcoming)
                {
                    destination.Add(notes[i]);
                }
            }

            return destination.Count;
        }

        private void AdvancePastResolvedNotes()
        {
            IReadOnlyList<Stage01RuntimeNote> notes = Notes;
            while (nextUnresolvedIndex < notes.Count &&
                notes[nextUnresolvedIndex].state != Stage01NoteState.Upcoming)
            {
                nextUnresolvedIndex++;
            }
        }

        public static QualityGrade ToQualityGrade(JudgeGrade grade)
        {
            // JudgementVisualLibrary labels legacy Smooth as CLEAN and Nasty as NASTY.
            switch (grade)
            {
                case JudgeGrade.Smooth: return QualityGrade.Clean;
                case JudgeGrade.Nasty: return QualityGrade.Nasty;
                case JudgeGrade.Slipped: return QualityGrade.Slipped;
                default: return QualityGrade.Whack;
            }
        }

        private void ResolveNextNoteAsMissed(Stage01RuntimeNote note)
        {
            note.state = Stage01NoteState.Missed;
            MissCount++;
            nextUnresolvedIndex++;
            NoteMissed?.Invoke(note);
            OutcomeResolved?.Invoke(new JudgementOutcome(RunId, note.sequenceIndex,
                QualityGrade.Whack, 0, true));
            AdvancePastResolvedNotes();
        }
    }
}
