using UnityEngine;
using SashimiBoy.Semantics;
using System;
using System.IO;
using System.Security.Cryptography;

namespace SashimiBoy
{
    [CreateAssetMenu(menuName = "Sashimi Boy/Rhythm/Semantic Beatmap", fileName = "SemanticBeatmapDefinition")]
    public sealed class SemanticBeatmapDefinition : ScriptableObject
    {
        public SemanticChart chart;
        public string authoringVersion;
        public string sourceScheduleIdentity;
        public AudioClip sourceMusic;
        public Stage01NotePatternDefinition sourcePattern;

        public void StampSource(Stage01SalmonTimingScaffold timing)
        {
            sourceScheduleIdentity = ComputeSourceIdentity(timing);
            sourceMusic = timing.musicClip;
            sourcePattern = timing.notePatternProvider.pattern;
            authoringVersion = timing.chartAuthoringVersion;
        }

        public bool MatchesSource(Stage01SalmonTimingScaffold timing)
        {
            if (timing == null || timing.notePatternProvider == null) return false;
            try
            {
                return authoringVersion == timing.chartAuthoringVersion && sourceMusic == timing.musicClip &&
                    sourcePattern == timing.notePatternProvider.pattern &&
                    sourceScheduleIdentity == ComputeSourceIdentity(timing);
            }
            catch (ArgumentException) { return false; } // Inconsistent schedules refuse start, never throw from Awake.
        }

        public static string ComputeSourceIdentity(Stage01SalmonTimingScaffold timing)
        {
            var valid = timing.ValidatePatternSource();
            if (!valid.IsValid) throw new ArgumentException(valid.ToString());
            var provider = timing.notePatternProvider;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write("stage01-expanded-schedule-v1");
                writer.Write(timing.bpm); writer.Write(timing.firstDownbeatSec);
                writer.Write(timing.gameplayStartSec); writer.Write(timing.gameplayEndSec);
                var clip = timing.musicClip;
                writer.Write(clip != null ? clip.name : "");
                writer.Write(clip != null ? clip.samples : 0); writer.Write(clip != null ? clip.frequency : 0);
                writer.Write(clip != null ? clip.channels : 0);
                var pattern = provider.pattern;
                writer.Write(pattern.manualBarCount); writer.Write(pattern.repeatFromBar);
                writer.Write(pattern.subdivisionsPerBeat); writer.Write(pattern.notes.Count);
                foreach (var note in pattern.notes)
                { writer.Write(note.barIndex); writer.Write(note.eighthStepInBar); }
                writer.Write(provider.RuntimeNotes.Count);
                for (int i = 0; i < provider.RuntimeNotes.Count; i++)
                {
                    var note = provider.RuntimeNotes[i];
                    if (note.sequenceIndex != i || !SemanticChartValidator.Finite(note.songTimeSeconds) ||
                        (i > 0 && note.songTimeSeconds <= provider.RuntimeNotes[i - 1].songTimeSeconds))
                        throw new ArgumentException("Inconsistent expanded schedule.");
                    writer.Write(note.sequenceIndex); writer.Write(note.songTimeSeconds);
                    writer.Write(note.sourceBarIndex); writer.Write(note.playbackBarIndex);
                    writer.Write(note.eighthStepInBar); writer.Write(note.repeated);
                }
                writer.Flush();
                using (var hash = SHA256.Create())
                    return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
