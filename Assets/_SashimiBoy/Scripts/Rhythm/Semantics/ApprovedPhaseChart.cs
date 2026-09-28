using System;
using System.Collections.Generic;

namespace SashimiBoy.Semantics
{
    /// <summary>Owner v2 prototype values, not final musical phrase tuning.</summary>
    public static class ApprovedPhaseChart
    {
        public const string Version = "owner-issue39-approved-authoring-20260908-v2";

        public static SemanticChart Build(IReadOnlyList<double> times)
        {
            if (times == null || times.Count < 6)
                throw new ArgumentException("At least six existing playable notes are required.");
            for (int i = 0; i < times.Count; i++)
                if (!SemanticChartValidator.Finite(times[i]) || times[i] < 0 || (i > 0 && times[i] <= times[i - 1]))
                    throw new ArgumentException("Schedule must be finite, nonnegative and strictly ordered.");
            int count = times.Count;
            var chart = new SemanticChart { weights = new QualityWeights(),
                notes = new SemanticNote[count], phases = new PhaseGate[6] };
            for (int p = 0; p < 6; p++)
            {
                int start = (int)((long)p * count / 6);
                int end = (int)((long)(p + 1) * count / 6);
                chart.phases[p] = new PhaseGate { phase = (FishPhase)p, nextPhase = (FishPhase)(p + 1),
                    firstNoteId = start, lastNoteId = end - 1, expectedNoteCount = end - start,
                    metric = GateMetric.Ratio, requiredQuality = .60d, action = (FishAction)p, target = (FishTarget)p,
                    successTransition = "stage01.phase." + ((FishPhase)p) + ".success.v1",
                    failureTransition = "stage01.phase." + ((FishPhase)p) + ".failure.v1" };
                for (int i = start; i < end; i++)
                    chart.notes[i] = new SemanticNote { noteId = i, phase = (FishPhase)p,
                        action = (FishAction)p, target = (FishTarget)p, repetitionCount = 1 };
                // Optional anchor/animation/cue IDs remain empty until an actual consumer is connected.
            }
            ApplyPlayableActionSequence(chart);
            var validation = SemanticChartValidator.Validate(chart, count);
            if (!validation.IsValid) throw new ArgumentException(validation.ToString());
            return chart;
        }

        // Owner's 2026-09-09 correction. Phase IDs remain serialized milestones;
        // note times, six ranges, weights and thresholds remain the v2 tuning.
        public static bool ApplyPlayableActionSequence(SemanticChart chart)
        {
            var actions = new[] { FishAction.CutHead, FishAction.RemoveFin, FishAction.SeparateSpine,
                FishAction.SplitFillet, FishAction.PullPinBone, FishAction.SliceFillet };
            var targets = new[] { FishTarget.Head, FishTarget.Fins, FishTarget.Spine,
                FishTarget.Fillet, FishTarget.PinBones, FishTarget.Fillet };
            bool changed = chart.actionSequence != ButcherySequence.OwnerHeadFirst;
            chart.actionSequence = ButcherySequence.OwnerHeadFirst;
            foreach (var gate in chart.phases)
            {
                int p = (int)gate.phase;
                changed |= gate.action != actions[p] || gate.target != targets[p];
                gate.action = actions[p]; gate.target = targets[p];
            }
            foreach (var note in chart.notes)
            {
                int p = (int)note.phase;
                changed |= note.action != actions[p] || note.target != targets[p];
                note.action = actions[p]; note.target = targets[p];
            }
            return changed;
        }
    }
}
