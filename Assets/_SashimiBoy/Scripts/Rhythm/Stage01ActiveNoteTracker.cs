using System;
using System.Collections.Generic;
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

        public Stage01NoteInputOutcome(
            Stage01NoteInputKind kind,
            Stage01RuntimeNote note,
            JudgeResult judge)
        {
            this.kind = kind;
            this.note = note;
            this.judge = judge;
        }
    }

    public sealed class Stage01ActiveNoteTracker : MonoBehaviour
    {
        public Stage01NotePatternProvider provider;

        public event Action<Stage01RuntimeNote> NoteMissed;

        private int nextUnresolvedIndex;

        public int HitCount { get; private set; }
        public int MissCount { get; private set; }
        public int EmptyHitCount { get; private set; }
        public bool IsInitialized => provider != null && provider.IsInitialized;

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
            double lateWindowSeconds = lateWindowMilliseconds / 1000d;
            AdvancePastResolvedNotes();
            while (nextUnresolvedIndex < notes.Count)
            {
                Stage01RuntimeNote note = notes[nextUnresolvedIndex];
                if (songTimeSeconds <=
                    note.songTimeSeconds + lateWindowSeconds)
                {
                    break;
                }

                note.state = Stage01NoteState.Missed;
                MissCount++;
                nextUnresolvedIndex++;
                NoteMissed?.Invoke(note);
                AdvancePastResolvedNotes();
            }
        }

        public Stage01NoteInputOutcome JudgeInput(
            double inputSongTimeSeconds,
            double nastyWindowMilliseconds,
            double smoothWindowMilliseconds,
            double slippedWindowMilliseconds)
        {
            ProcessExpiredNotes(
                inputSongTimeSeconds,
                slippedWindowMilliseconds);
            Stage01RuntimeNote note = NextActiveNote;
            if (note == null)
            {
                EmptyHitCount++;
                return new Stage01NoteInputOutcome(
                    Stage01NoteInputKind.Empty,
                    null,
                    default);
            }

            double offsetMilliseconds =
                (inputSongTimeSeconds - note.songTimeSeconds) * 1000d;
            if (Math.Abs(offsetMilliseconds) > slippedWindowMilliseconds)
            {
                EmptyHitCount++;
                return new Stage01NoteInputOutcome(
                    Stage01NoteInputKind.Empty,
                    null,
                    default);
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
            return new Stage01NoteInputOutcome(
                Stage01NoteInputKind.Hit,
                note,
                judge);
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
    }
}
