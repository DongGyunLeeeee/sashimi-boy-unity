using System;
using System.Collections;
using UnityEngine;
using SashimiBoy.Semantics;

namespace SashimiBoy.Tests
{
    // Synthetic configuration only. Never writes an asset or production balance.
    public static class SemanticTestFixture
    {
        public static SemanticChart Chart(int count = 12, double threshold = .5d)
        {
            var chart = new SemanticChart { weights = new QualityWeights(),
                notes = new SemanticNote[count], phases = new PhaseGate[6] };
            for (int p = 0; p < 6; p++)
            {
                int start = p * count / 6, end = (p + 1) * count / 6 - 1;
                chart.phases[p] = new PhaseGate { phase = (FishPhase)p, nextPhase = (FishPhase)(p + 1),
                    firstNoteId = start, lastNoteId = end, expectedNoteCount = end - start + 1,
                    metric = GateMetric.Ratio, requiredQuality = threshold,
                    action = (FishAction)p, target = (FishTarget)p,
                    successTransition = "fixture-success-" + p, failureTransition = "fixture-failure-" + p };
                for (int n = start; n <= end; n++) chart.notes[n] = new SemanticNote {
                    noteId = n, phase = (FishPhase)p, action = (FishAction)p, target = (FishTarget)p };
            }
            return chart;
        }

        public static ScriptableObject Configure(Component timing, double threshold = .5d)
        {
            var provider = (Component)RuntimeReflection.GetField(timing, "notePatternProvider");
            var notes = (IList)RuntimeReflection.GetField(provider, "runtimeNotes");
            var asset = ScriptableObject.CreateInstance(RuntimeReflection.RuntimeType("SashimiBoy.SemanticBeatmapDefinition"));
            RuntimeReflection.SetField(asset, "chart", Chart(notes.Count, threshold));
            RuntimeReflection.Invoke(asset, "StampSource", timing);
            RuntimeReflection.SetField(timing, "semanticBeatmap", asset);
            if (!(bool)RuntimeReflection.Invoke(timing, "RetryStage"))
                throw new InvalidOperationException("Synthetic chart failed runtime validation.");
            return asset;
        }

        public static void ResolveAll(Component timing)
        {
            var provider = (Component)RuntimeReflection.GetField(timing, "notePatternProvider");
            var notes = (IList)RuntimeReflection.GetField(provider, "runtimeNotes");
            foreach (object note in notes)
                RuntimeReflection.Invoke(timing, "ResolveGameplayInput", (double)RuntimeReflection.GetField(note, "songTimeSeconds"));
        }
    }
}
