using System;
using System.Collections.Generic;
using NUnit.Framework;
using SashimiBoy.Semantics;

namespace SashimiBoy.Tests
{
    public sealed class PhasePerformanceEditModeTests
    {
        private PhasePerformanceTracker tracker;
        private List<SemanticEvent> events;
        [SetUp] public void Setup()
        {
            tracker = new PhasePerformanceTracker(); events = new List<SemanticEvent>();
            tracker.EventRaised += events.Add;
            Assert.That(tracker.Reset(SemanticTestFixture.Chart(), 12, 1).IsValid, Is.True);
            events.Clear();
        }
        private ResolveStatus Hit(int note, QualityGrade grade = QualityGrade.Clean, long input = -1, long run = 1) =>
            tracker.Consume(new JudgementOutcome(run, note, grade, input < 0 ? note + 1 : input, false));
        private int Count(SemanticEventKind kind) => events.FindAll(e => e.kind == kind).Count;

        [Test] public void EmptyEdges_ReduceGateQualityOnceWithoutConsumingNotesOrFailingEarly()
        {
            var chart = SemanticTestFixture.Chart(); chart.phases[0].requiredQuality = .60d;
            tracker.Reset(chart, 12, 2); events.Clear();
            Assert.That(Hit(0, QualityGrade.Clean, input: 10, run: 2), Is.EqualTo(ResolveStatus.Accepted));
            Assert.That(tracker.PenalizeEmptyInput(2, 11, .35d), Is.EqualTo(ResolveStatus.Accepted));
            Assert.That(tracker.PenalizeEmptyInput(2, 11, .35d), Is.EqualTo(ResolveStatus.DuplicateInput));
            Assert.That(tracker.PenalizeEmptyInput(2, 10, .35d), Is.EqualTo(ResolveStatus.DuplicateInput));
            Assert.That(tracker.PenalizeEmptyInput(1, 12, .35d), Is.EqualTo(ResolveStatus.StaleRun));
            Assert.That(tracker.PhasePoints, Is.EqualTo(.40d).Within(1e-12));
            Assert.That(tracker.NoteCursor, Is.EqualTo(1)); Assert.That(tracker.Failed, Is.False);
            Assert.That(Count(SemanticEventKind.ActionResolved), Is.EqualTo(1));
            Assert.That(Count(SemanticEventKind.InputPenalized), Is.EqualTo(1));
            Hit(1, QualityGrade.Clean, input: 12, run: 2);
            Assert.That(tracker.Failed, Is.True, "Two Clean hits pass 60%, but the extra bad edge puts this phase below it.");
            Assert.That(Count(SemanticEventKind.PhaseFailed), Is.EqualTo(1));
            Assert.That(tracker.PenalizeEmptyInput(2, 13, .35d), Is.EqualTo(ResolveStatus.NotRunning));
            tracker.Reset(chart, 12, 3);
            Assert.That(tracker.PhasePoints, Is.Zero);
        }

        [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)] [TestCase(-.1d)]
        public void InvalidPenalty_DoesNotConsumeIdentityOrChangeQuality(double value)
        {
            Assert.That(tracker.PenalizeEmptyInput(1, 4, value), Is.EqualTo(ResolveStatus.InvalidOutcome));
            Assert.That(tracker.PhasePoints, Is.Zero);
            Assert.That(tracker.PenalizeEmptyInput(1, 4, .35d), Is.EqualTo(ResolveStatus.Accepted));
            Assert.That(tracker.PhasePoints, Is.EqualTo(-.35d));
        }

        [TestCase(QualityGrade.Clean, .75d)]
        [TestCase(QualityGrade.Nasty, 1d)]
        [TestCase(QualityGrade.Slipped, .35d)]
        [TestCase(QualityGrade.Whack, 0d)]
        public void Weights_AccumulatePointsAndExpectedCountRatio(QualityGrade grade, double expected)
        {
            Assert.That(Hit(0, grade), Is.EqualTo(ResolveStatus.Accepted));
            Assert.That(tracker.TotalPoints, Is.EqualTo(expected));
            Assert.That(tracker.PhasePoints, Is.EqualTo(expected));
            Assert.That(tracker.PhaseRatio, Is.EqualTo(expected / 2));
            Assert.That(Count(SemanticEventKind.PhaseSucceeded), Is.Zero);
        }
        [TestCase(GateMetric.Ratio, .375d, true)]
        [TestCase(GateMetric.Ratio, .375001d, false)]
        [TestCase(GateMetric.Points, .75d, true)]
        [TestCase(GateMetric.Points, .750001d, false)]
        public void LastNote_EvaluatesThresholdExactlyOnce(GateMetric metric, double threshold, bool success)
        {
            var chart = SemanticTestFixture.Chart();
            chart.phases[0].metric = metric; chart.phases[0].requiredQuality = threshold;
            tracker.Reset(chart, 12, 2); events.Clear();
            Hit(0, run: 2);
            Assert.That(Count(SemanticEventKind.PhaseSucceeded) + Count(SemanticEventKind.PhaseFailed), Is.Zero);
            Hit(1, QualityGrade.Whack, run: 2);
            Assert.That(tracker.Failed, Is.EqualTo(!success));
            Assert.That(Count(success ? SemanticEventKind.PhaseSucceeded : SemanticEventKind.PhaseFailed), Is.EqualTo(1));
            Hit(1, run: 2);
            Assert.That(Count(SemanticEventKind.ActionResolved), Is.EqualTo(2));
        }
        [Test] public void DuplicateNoteInputAndFuturePhase_AreRejectedWithoutMutation()
        {
            Assert.That(Hit(2), Is.EqualTo(ResolveStatus.UnexpectedNote));
            Hit(0);
            Assert.That(Hit(0), Is.EqualTo(ResolveStatus.UnexpectedNote));
            Assert.That(Hit(1, input: 1), Is.EqualTo(ResolveStatus.DuplicateInput));
            Assert.That(tracker.NoteCursor, Is.EqualTo(1));
            Assert.That(tracker.TotalPoints, Is.EqualTo(.75d));
        }
        [Test] public void AutomaticMiss_FailsOnceAndNeverEmitsFurtherSuccessOrClear()
        {
            for (int n = 0; n < 2; n++)
                Assert.That(tracker.Consume(new JudgementOutcome(1, n, QualityGrade.Whack, 0, true)), Is.EqualTo(ResolveStatus.Accepted));
            Assert.That(tracker.Consume(new JudgementOutcome(1, 1, QualityGrade.Whack, 0, true)), Is.EqualTo(ResolveStatus.NotRunning));
            for (int n = 2; n < 12; n++) Hit(n);
            Assert.That(Count(SemanticEventKind.PhaseFailed), Is.EqualTo(1));
            Assert.That(Count(SemanticEventKind.PhaseSucceeded), Is.Zero);
            Assert.That(Count(SemanticEventKind.StageCompleted), Is.Zero);
            Assert.That(tracker.PhaseIndex, Is.Zero);
        }
        [Test] public void Completion_RequiresFinalPhaseAndEmitsNonScoringTransitionsOnce()
        {
            for (int n = 0; n < 11; n++) Hit(n);
            Assert.That(tracker.Completed, Is.False);
            Assert.That(Count(SemanticEventKind.StageCompleted), Is.Zero);
            Hit(11); Hit(11);
            Assert.That(tracker.Completed, Is.True);
            Assert.That(Count(SemanticEventKind.PhaseSucceeded), Is.EqualTo(6));
            Assert.That(Count(SemanticEventKind.StageCompleted), Is.EqualTo(1));
            Assert.That(tracker.TotalPoints, Is.EqualTo(9));
            foreach (var e in events)
            {
                Assert.That(e.scoring, Is.EqualTo(e.kind == SemanticEventKind.ActionResolved));
                if (!e.scoring) Assert.That(e.action, Is.EqualTo(FishAction.PhaseTransition));
            }
        }
        [TestCase(false)] [TestCase(true)]
        public void Retry_ResetsCursorQualityFlagsAndIgnoresOldRun(bool complete)
        {
            for (int n = 0; n < 12; n++) Hit(n, complete ? QualityGrade.Clean : QualityGrade.Whack);
            Assert.That(tracker.Reset(SemanticTestFixture.Chart(), 12, 2).IsValid, Is.True);
            Assert.That(tracker.PhaseIndex, Is.Zero); Assert.That(tracker.NoteCursor, Is.Zero);
            Assert.That(tracker.TotalPoints, Is.Zero); Assert.That(tracker.PhasePoints, Is.Zero);
            Assert.That(tracker.Completed || tracker.Failed, Is.False);
            Assert.That(Hit(0), Is.EqualTo(ResolveStatus.StaleRun));
            Assert.That(Hit(0, run: 2), Is.EqualTo(ResolveStatus.Accepted));
        }
        [Test] public void AuthoringMutation_DoesNotChangeRunningSnapshot()
        {
            var chart = SemanticTestFixture.Chart(); tracker.Reset(chart, 12, 2);
            chart.weights.clean = 0; chart.phases[0].requiredQuality = 1; chart.notes[0].action = FishAction.SliceFillet;
            Hit(0, run: 2); Hit(1, run: 2);
            Assert.That(tracker.TotalPoints, Is.EqualTo(1.5d)); Assert.That(tracker.PhaseIndex, Is.EqualTo(1));
        }
        [TestCase("missing")] [TestCase("weight")] [TestCase("duplicate")] [TestCase("order")]
        [TestCase("action")] [TestCase("target")] [TestCase("overlap")] [TestCase("range")]
        [TestCase("count")] [TestCase("threshold")] [TestCase("transition")] [TestCase("coverage")]
        [TestCase("repetition")] [TestCase("next")]
        public void InvalidData_RefusesStart(string invalid)
        {
            var chart = SemanticTestFixture.Chart();
            switch (invalid)
            {
                case "missing": chart.weights = null; break;
                case "weight": chart.weights.clean = double.NaN; break;
                case "duplicate": chart.notes[1].noteId = 0; break;
                case "order": chart.notes[0].phase = FishPhase.Slicing; break;
                case "action": chart.notes[0].action = (FishAction)99; break;
                case "target": chart.notes[0].target = (FishTarget)99; break;
                case "overlap": chart.phases[1].firstNoteId = 1; break;
                case "range": chart.phases[0].lastNoteId = -1; break;
                case "count": chart.phases[0].expectedNoteCount++; break;
                case "threshold": chart.phases[0].requiredQuality = double.PositiveInfinity; break;
                case "transition": chart.phases[0].failureTransition = null; break;
                case "coverage": chart.notes = new SemanticNote[0]; break;
                case "repetition": chart.notes[0].repetitionCount = 0; break;
                case "next": chart.phases[0].nextPhase = FishPhase.Complete; break;
            }
            Assert.That(tracker.Reset(chart, 12, 2).IsValid, Is.False);
            Assert.That(tracker.IsRunning, Is.False); Assert.That(Hit(0, run: 2), Is.EqualTo(ResolveStatus.NotRunning));
        }
        [Test] public void MixedWeights_SumAndRatioUseConfiguredData()
        {
            var chart = SemanticTestFixture.Chart(24);
            tracker.Reset(chart, 24, 2); events.Clear();
            Hit(0, QualityGrade.Clean, run: 2); Hit(1, QualityGrade.Nasty, run: 2);
            Hit(2, QualityGrade.Slipped, run: 2); Hit(3, QualityGrade.Whack, run: 2);
            Assert.That(tracker.TotalPoints, Is.EqualTo(2.1d).Within(1e-12));
            var success = events.Find(e => e.kind == SemanticEventKind.PhaseSucceeded);
            Assert.That(success.ratio, Is.EqualTo(.525d).Within(1e-12));
            chart.weights.clean = .6d;
            tracker.Reset(chart, 24, 3);
            Hit(0, run: 3);
            Assert.That(tracker.PhasePoints, Is.EqualTo(.6d));
        }
        [Test] public void ReusedRunIdentity_RefusesRetry()
        {
            Assert.That(tracker.Reset(SemanticTestFixture.Chart(), 12, 1).code, Is.EqualTo("INVALID_RUN_ID"));
            Assert.That(tracker.IsRunning, Is.False);
        }
        [Test] public void InvalidOutcome_DoesNotConsumeNoteOrInput()
        {
            Assert.That(Hit(0, (QualityGrade)99), Is.EqualTo(ResolveStatus.InvalidOutcome));
            Assert.That(Hit(0, input: 0), Is.EqualTo(ResolveStatus.InvalidOutcome));
            Assert.That(tracker.Consume(new JudgementOutcome(1, 0, QualityGrade.Clean, 0, true)), Is.EqualTo(ResolveStatus.InvalidOutcome));
            Assert.That(tracker.NoteCursor, Is.Zero);
            Assert.That(Hit(0), Is.EqualTo(ResolveStatus.Accepted));
        }
        [Test] public void ReentrantOutcome_IsRejected()
        {
            tracker.EventRaised += e => Assert.That(Hit(1), Is.EqualTo(ResolveStatus.NotRunning));
            Hit(0); Assert.That(tracker.NoteCursor, Is.EqualTo(1));
        }
    }
}
