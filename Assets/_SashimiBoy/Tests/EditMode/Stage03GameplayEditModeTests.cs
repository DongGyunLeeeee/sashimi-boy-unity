using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;

namespace SashimiBoy.Tests
{
    public sealed class Stage03GameplayEditModeTests
    {
        static object NewRun() => Activator.CreateInstance(RuntimeReflection.RuntimeType("SashimiBoy.Stage03RunState"));
        static object Call(object target, string method, params object[] args) => RuntimeReflection.Invoke(target, method, args);
        static T Value<T>(object target, string property) => (T)Call(target, "get_" + property);
        static void Satisfy(object run)
        {
            int needed = Value<int>(run, "RequiredHits");
            for (int i = 0; i < needed; i++) Assert.That(Call(run, "TryHit"), Is.EqualTo(true));
            Assert.That(Call(run, "TryHit"), Is.EqualTo(false), "Extra resolved input cannot add another cut.");
        }
        static void ReachFirstSlicingPhase(object run)
        {
            for (int phase = 0; phase < 5; phase++)
            {
                Assert.That(Value<int>(run, "Phase"), Is.EqualTo(phase));
                Satisfy(run);
                Assert.That(Call(run, "TryAdvance"), Is.EqualTo(phase != 2));
                if (phase == 2)
                {
                    Assert.That(Value<bool>(run, "WaitingForTurn"), Is.True);
                    Assert.That(Call(run, "TryHit"), Is.EqualTo(false));
                    Assert.That(Call(run, "FinishTurn"), Is.EqualTo(true));
                    Assert.That(Call(run, "FinishTurn"), Is.EqualTo(false));
                }
            }
        }

        [Test]
        public void Run_RequiresHeadAndFrontTwoBeforeTurn_AndExactlyFourTimesSixSlices()
        {
            var run = NewRun();
            Assert.That(Call(run, "TryAdvance"), Is.EqualTo(false));
            Assert.That(Call(run, "FinishTurn"), Is.EqualTo(false));
            ReachFirstSlicingPhase(run);
            Assert.That(Value<int>(run, "TotalSlices"), Is.Zero);
            for (int fillet = 0; fillet < 4; fillet++)
            {
                for (int cut = 0; cut < 5; cut++) Assert.That(Call(run, "TryHit"), Is.EqualTo(true));
                Assert.That(Call(run, "TryAdvance"), Is.EqualTo(false), "Five cuts cannot finish a six-piece fillet.");
                Assert.That(Value<bool>(run, "Completed"), Is.False);
                Assert.That(Call(run, "TryHit"), Is.EqualTo(true));
                Assert.That(Call(run, "TryHit"), Is.EqualTo(false));
                Assert.That(Call(run, "TryAdvance"), Is.EqualTo(true));
            }
            Assert.That(Value<bool>(run, "Completed"), Is.True);
            Assert.That(Value<int>(run, "TotalSlices"), Is.EqualTo(24));
            Assert.That(Value<int[]>(run, "Slices"), Is.EqualTo(new[] { 6, 6, 6, 6 }));
            Assert.That(Call(run, "TryHit"), Is.EqualTo(false));
            Assert.That(Call(run, "TryAdvance"), Is.EqualTo(false));
            Call(run, "Fail");
            Assert.That(Value<bool>(run, "Failed"), Is.False, "A finished plate cannot be converted into failure.");
            Assert.That(Call(run, "RetryPhase"), Is.EqualTo(false));
        }

        [Test]
        public void FailedFilletRetry_RetainsEarlierFillets_AndResetsOnlyCurrentCuts()
        {
            var run = NewRun();
            ReachFirstSlicingPhase(run);
            Satisfy(run);
            Assert.That(Call(run, "TryAdvance"), Is.EqualTo(true));
            for (int i = 0; i < 3; i++) Assert.That(Call(run, "TryHit"), Is.EqualTo(true));
            Call(run, "Fail");
            Assert.That(Value<int[]>(run, "Slices"), Is.EqualTo(new[] { 6, 3, 0, 0 }));
            Assert.That(Call(run, "TryHit"), Is.EqualTo(false));
            Assert.That(Call(run, "TryAdvance"), Is.EqualTo(false));
            Assert.That(Call(run, "RetryPhase"), Is.EqualTo(true));
            Assert.That(Value<int[]>(run, "Slices"), Is.EqualTo(new[] { 6, 0, 0, 0 }));
            Assert.That(Value<int>(run, "Phase"), Is.EqualTo(6));
            Assert.That(Value<bool>(run, "Turned"), Is.True);
            Assert.That(Call(run, "RetryPhase"), Is.EqualTo(false));
            for (int i = 0; i < 3; i++) { Satisfy(run); Assert.That(Call(run, "TryAdvance"), Is.EqualTo(true)); }
            Assert.That(Value<int>(run, "TotalSlices"), Is.EqualTo(24));
            Call(run, "Reset");
            Assert.That(Value<int>(run, "Phase"), Is.Zero);
            Assert.That(Value<int>(run, "TotalSlices"), Is.Zero);
            Assert.That(Value<bool>(run, "Turned"), Is.False);
            Assert.That(Value<bool>(run, "WaitingForTurn"), Is.False);
        }

        [Test]
        public void FailureDuringFrontWork_RetryDoesNotSkipTheRequiredTurn()
        {
            var run = NewRun();
            for (int i = 0; i < 2; i++) { Satisfy(run); Call(run, "TryAdvance"); }
            Assert.That(Value<int>(run, "Phase"), Is.EqualTo(2));
            Assert.That(Call(run, "TryHit"), Is.EqualTo(true));
            Call(run, "Fail");
            Assert.That(Call(run, "FinishTurn"), Is.EqualTo(false));
            Assert.That(Call(run, "RetryPhase"), Is.EqualTo(true));
            Satisfy(run);
            Assert.That(Call(run, "TryAdvance"), Is.EqualTo(false));
            Assert.That(Value<bool>(run, "WaitingForTurn"), Is.True);
            Assert.That(Value<bool>(run, "Turned"), Is.False);
            Assert.That(Value<int>(run, "Phase"), Is.EqualTo(2));
            Assert.That(Call(run, "TryHit"), Is.EqualTo(false));
        }
    }
}
