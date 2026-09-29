using System;
using System.Collections.Generic;

namespace SashimiBoy.Semantics
{
    public enum FishPhase { WholeFish, Head, Fins, Spine, PinBones, Slicing, Complete }
    public enum FishAction { Prepare, CutHead, RemoveFin, SeparateSpine, PullPinBone, SliceFillet, PhaseTransition, SplitFillet }
    public enum FishTarget { WholeFish, Head, Fins, Spine, PinBones, Fillet, Plate }
    public enum QualityGrade { Clean, Nasty, Slipped, Whack }
    public enum GateMetric { Points, Ratio }
    public enum ButcherySequence { LegacyPreparation, OwnerHeadFirst }
    public enum SemanticEventKind { ActionResolved, PhaseSucceeded, PhaseFailed, StageCompleted, RunReset, InputPenalized }
    public enum ResolveStatus { Accepted, NotRunning, StaleRun, UnexpectedNote, DuplicateInput, InvalidOutcome }

    [Serializable]
    public sealed class QualityWeights
    {
        public double clean = .75d, nasty = 1d, slipped = .35d, whack = 0d;
        public double Get(QualityGrade grade)
        {
            switch (grade)
            {
                case QualityGrade.Clean: return clean;
                case QualityGrade.Nasty: return nasty;
                case QualityGrade.Slipped: return slipped;
                case QualityGrade.Whack: return whack;
                default: throw new ArgumentOutOfRangeException(nameof(grade));
            }
        }
        internal QualityWeights Copy() => (QualityWeights)MemberwiseClone();
    }

    [Serializable]
    public sealed class SemanticNote
    {
        // References the existing expanded runtime schedule; never changes note time.
        public int noteId;
        public FishPhase phase;
        public FishAction action;
        public FishTarget target;
        public string anchorId;
        public string handAnimationId;
        public string cueStyle;
        public int repetitionCount = 1;
        internal SemanticNote Copy() => (SemanticNote)MemberwiseClone();
    }

    [Serializable]
    public sealed class PhaseGate
    {
        public FishPhase phase;
        public int firstNoteId, lastNoteId, expectedNoteCount;
        public GateMetric metric;
        // Explicit authoring required; negative sentinel must never pass validation.
        public double requiredQuality = -1d;
        public FishPhase nextPhase;
        public FishTarget target;
        public FishAction action;
        // Non-scoring transitions anchored to the last resolved note, not wall time.
        public string successTransition, failureTransition;
        internal PhaseGate Copy() => (PhaseGate)MemberwiseClone();
    }

    [Serializable]
    public sealed class SemanticChart
    {
        public ButcherySequence actionSequence;
        public QualityWeights weights;
        public SemanticNote[] notes;
        public PhaseGate[] phases;
        internal SemanticChart Copy()
        {
            var copy = new SemanticChart { actionSequence = actionSequence, weights = weights.Copy(),
                notes = new SemanticNote[notes.Length], phases = new PhaseGate[phases.Length] };
            for (int i = 0; i < notes.Length; i++) copy.notes[i] = notes[i].Copy();
            for (int i = 0; i < phases.Length; i++) copy.phases[i] = phases[i].Copy();
            return copy;
        }
    }

    public readonly struct SemanticValidationResult
    {
        public readonly string code, detail;
        public bool IsValid => code == "OK";
        public SemanticValidationResult(string code, string detail) { this.code = code; this.detail = detail; }
        public override string ToString() => code + ": " + detail;
    }

    public static class SemanticChartValidator
    {
        public static FishAction ActionFor(ButcherySequence sequence, int phase) =>
            sequence == ButcherySequence.OwnerHeadFirst
                ? new[] { FishAction.CutHead, FishAction.RemoveFin, FishAction.SeparateSpine, FishAction.SplitFillet, FishAction.PullPinBone, FishAction.SliceFillet }[phase]
                : (FishAction)phase;
        public static FishTarget TargetFor(ButcherySequence sequence, int phase) =>
            sequence == ButcherySequence.OwnerHeadFirst
                ? new[] { FishTarget.Head, FishTarget.Fins, FishTarget.Spine, FishTarget.Fillet, FishTarget.PinBones, FishTarget.Fillet }[phase]
                : (FishTarget)phase;
        public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public static SemanticValidationResult Validate(SemanticChart chart, int scheduledNoteCount)
        {
            if (chart == null || chart.weights == null || chart.notes == null || chart.phases == null)
                return Error("MISSING_CHART", "Chart, weights, notes and phase gates are required.");
            if (scheduledNoteCount <= 0 || chart.notes.Length != scheduledNoteCount)
                return Error("NOTE_COVERAGE", "Every expanded playable note must have exactly one semantic note.");
            if (!Enum.IsDefined(typeof(ButcherySequence), chart.actionSequence))
                return Error("ACTION_SEQUENCE", "Unknown authored action sequence.");
            for (int i = 0; i < 4; i++)
            {
                double weight = chart.weights.Get((QualityGrade)i);
                if (!Finite(weight) || weight < 0d || weight > 1d)
                    return Error("INVALID_WEIGHT", "Quality weights must be finite and within [0,1].");
            }
            if (chart.phases.Length != 6) return Error("PHASE_ORDER", "WholeFish through Slicing are required; Complete is terminal.");
            int cursor = 0;
            for (int p = 0; p < 6; p++)
            {
                PhaseGate gate = chart.phases[p];
                if (gate == null || gate.phase != (FishPhase)p || gate.nextPhase != (FishPhase)(p + 1))
                    return Error("PHASE_ORDER", "Phase/nextPhase must follow the complete preparation sequence.");
                if (gate.firstNoteId != cursor || gate.lastNoteId < cursor || gate.lastNoteId >= scheduledNoteCount ||
                    gate.expectedNoteCount != gate.lastNoteId - cursor + 1)
                    return Error("PHASE_RANGE", "Ranges must be nonempty, contiguous, disjoint and match expected count.");
                if (!Enum.IsDefined(typeof(GateMetric), gate.metric) || !Finite(gate.requiredQuality) ||
                    gate.requiredQuality < 0d || gate.requiredQuality > (gate.metric == GateMetric.Ratio ? 1d : gate.expectedNoteCount))
                    return Error("INVALID_THRESHOLD", "An explicit finite points/ratio threshold is required.");
                if (gate.action != ActionFor(chart.actionSequence, p) || gate.target != TargetFor(chart.actionSequence, p))
                    return Error("ACTION_TARGET", "Gate action/target does not match its phase.");
                if (string.IsNullOrWhiteSpace(gate.successTransition) || string.IsNullOrWhiteSpace(gate.failureTransition))
                    return Error("MISSING_TRANSITION", "Both success and failure payload IDs are required.");
                cursor = gate.lastNoteId + 1;
            }
            if (cursor != scheduledNoteCount) return Error("NOTE_COVERAGE", "Phase ranges must cover the schedule.");
            var ids = new HashSet<int>();
            int phaseIndex = 0;
            for (int i = 0; i < chart.notes.Length; i++)
            {
                SemanticNote note = chart.notes[i];
                if (note == null) return Error("MISSING_NOTE", "Null semantic note.");
                if (!ids.Add(note.noteId)) return Error("DUPLICATE_NOTE", "Duplicate note ID.");
                if (note.noteId != i) return Error("NOTE_ORDER", "Note IDs must reference the existing schedule in order.");
                while (i > chart.phases[phaseIndex].lastNoteId) phaseIndex++;
                if (note.phase != (FishPhase)phaseIndex) return Error("PHASE_ORDER", "Note phase is outside its range.");
                if (note.action != ActionFor(chart.actionSequence, phaseIndex) || note.target != TargetFor(chart.actionSequence, phaseIndex))
                    return Error("ACTION_TARGET", "Unknown or mismatched scoring action/target; transitions cannot score.");
                if (note.repetitionCount < 1) return Error("INVALID_REPETITION", "Repetition count must be positive.");
            }
            return new SemanticValidationResult("OK", "Validated against expanded schedule.");
        }
        private static SemanticValidationResult Error(string code, string detail) => new SemanticValidationResult(code, detail);
    }

    public readonly struct JudgementOutcome
    {
        public readonly long runId, physicalInputId;
        public readonly int noteId;
        public readonly QualityGrade grade;
        public readonly bool automaticMiss;
        public JudgementOutcome(long runId, int noteId, QualityGrade grade, long physicalInputId, bool automaticMiss)
        {
            this.runId = runId; this.noteId = noteId; this.grade = grade;
            this.physicalInputId = physicalInputId; this.automaticMiss = automaticMiss;
        }
    }

    public readonly struct SemanticEvent
    {
        public readonly SemanticEventKind kind;
        public readonly long runId;
        public readonly FishPhase phase, nextPhase;
        public readonly FishAction action;
        public readonly FishTarget target;
        public readonly int noteId;
        public readonly bool scoring;
        public readonly double points, ratio;
        public readonly string payload, anchorId, handAnimationId, cueStyle;
        public readonly int repetitionCount;
        public readonly QualityGrade grade;
        internal SemanticEvent(SemanticEventKind kind, long run, PhaseGate gate, int noteId,
            double points, double ratio, SemanticNote note = null, QualityGrade grade = QualityGrade.Whack)
        {
            this.kind = kind; runId = run; phase = gate.phase; nextPhase = kind == SemanticEventKind.PhaseFailed ? gate.phase : gate.nextPhase;
            action = note != null ? note.action : FishAction.PhaseTransition;
            target = gate.target; this.noteId = noteId; scoring = note != null;
            this.points = points; this.ratio = ratio; this.grade = grade;
            payload = kind == SemanticEventKind.PhaseFailed ? gate.failureTransition : gate.successTransition;
            anchorId = note?.anchorId; handAnimationId = note?.handAnimationId; cueStyle = note?.cueStyle;
            repetitionCount = note?.repetitionCount ?? 0;
        }
    }

    /// <summary>Pure gate machine. No clock, score, save or presentation dependencies.</summary>
    public sealed class PhasePerformanceTracker
    {
        public event Action<SemanticEvent> EventRaised;
        private SemanticChart chart;
        private readonly HashSet<long> consumedInputs = new HashSet<long>();
        private readonly HashSet<long> usedRuns = new HashSet<long>();
        private bool dispatching;
        public long RunId { get; private set; }
        public int NoteCursor { get; private set; }
        public int PhaseIndex { get; private set; }
        public double PhasePoints { get; private set; }
        public double TotalPoints { get; private set; }
        public double LastGatePoints { get; private set; }
        public double PhaseRatio => chart == null || PhaseIndex >= 6 ? 0d : PhasePoints / chart.phases[PhaseIndex].expectedNoteCount;
        public bool IsDispatching => dispatching;
        public bool IsRunning { get; private set; }
        public bool Failed { get; private set; }
        public bool Completed { get; private set; }

        // Empty physical edges cost quality without consuming a note, moving a part or
        // resolving a gate early. Share input identities with accepted note judgements.
        public ResolveStatus PenalizeEmptyInput(long runId, long physicalInputId, double penalty)
        {
            if (runId != RunId) return ResolveStatus.StaleRun;
            if (!IsRunning || dispatching) return ResolveStatus.NotRunning;
            if (physicalInputId <= 0 || !SemanticChartValidator.Finite(penalty) || penalty < 0d)
                return ResolveStatus.InvalidOutcome;
            if (!consumedInputs.Add(physicalInputId)) return ResolveStatus.DuplicateInput;
            PhasePoints -= penalty;
            TotalPoints -= penalty;
            Dispatch(new SemanticEvent(SemanticEventKind.InputPenalized, RunId,
                chart.phases[PhaseIndex], -1, PhasePoints, PhaseRatio));
            return ResolveStatus.Accepted;
        }

        public SemanticValidationResult Reset(SemanticChart authoredChart, int scheduledNoteCount, long runId)
        {
            if (dispatching) throw new InvalidOperationException("Reset must be queued until semantic dispatch returns.");
            chart = null; IsRunning = false; Failed = false; Completed = false;
            NoteCursor = 0; PhaseIndex = 0; PhasePoints = 0d; TotalPoints = 0d; LastGatePoints = 0d;
            consumedInputs.Clear(); RunId = runId;
            SemanticValidationResult result = SemanticChartValidator.Validate(authoredChart, scheduledNoteCount);
            if (!result.IsValid) return result;
            if (runId <= 0 || !usedRuns.Add(runId))
                return new SemanticValidationResult("INVALID_RUN_ID", "Every retry requires a fresh positive run identity.");
            chart = authoredChart.Copy(); IsRunning = true;
            Dispatch(new SemanticEvent(SemanticEventKind.RunReset, RunId, chart.phases[0], -1, 0d, 0d));
            return result;
        }

        public ResolveStatus Consume(JudgementOutcome outcome)
        {
            if (outcome.runId != RunId) return ResolveStatus.StaleRun;
            if (!IsRunning || dispatching) return ResolveStatus.NotRunning;
            if (outcome.noteId != NoteCursor) return ResolveStatus.UnexpectedNote;
            if (!Enum.IsDefined(typeof(QualityGrade), outcome.grade) ||
                (outcome.automaticMiss ? outcome.grade != QualityGrade.Whack || outcome.physicalInputId != 0 : outcome.physicalInputId <= 0))
                return ResolveStatus.InvalidOutcome;
            if (!outcome.automaticMiss && !consumedInputs.Add(outcome.physicalInputId)) return ResolveStatus.DuplicateInput;
            PhaseGate gate = chart.phases[PhaseIndex];
            SemanticNote note = chart.notes[NoteCursor++];
            double weight = chart.weights.Get(outcome.grade);
            PhasePoints += weight; TotalPoints += weight;
            double points = PhasePoints, ratio = PhaseRatio;
            bool last = outcome.noteId == gate.lastNoteId;
            bool success = (gate.metric == GateMetric.Ratio ? ratio : points) >= gate.requiredQuality;
            if (last)
            {
                LastGatePoints = points;
                if (!success) { Failed = true; IsRunning = false; }
                else if (PhaseIndex == 5) { Completed = true; IsRunning = false; }
            }
            // State is committed before callbacks; callbacks cannot resolve another note or reset this run.
            dispatching = true;
            try
            {
                EventRaised?.Invoke(new SemanticEvent(SemanticEventKind.ActionResolved, RunId, gate, outcome.noteId, points, ratio, note, outcome.grade));
                if (last)
                {
                    EventRaised?.Invoke(new SemanticEvent(success ? SemanticEventKind.PhaseSucceeded : SemanticEventKind.PhaseFailed,
                        RunId, gate, outcome.noteId, points, ratio));
                    if (Completed) EventRaised?.Invoke(new SemanticEvent(SemanticEventKind.StageCompleted, RunId, gate, outcome.noteId, points, ratio));
                }
            }
            finally
            {
                if (last && success) { PhaseIndex++; PhasePoints = 0d; }
                dispatching = false;
            }
            return ResolveStatus.Accepted;
        }
        private void Dispatch(SemanticEvent value)
        {
            dispatching = true;
            try { EventRaised?.Invoke(value); }
            finally { dispatching = false; }
        }
    }
}
