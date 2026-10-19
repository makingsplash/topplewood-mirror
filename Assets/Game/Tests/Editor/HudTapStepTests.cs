using System;
using Gameplay.Core;
using Gameplay.Tutorial;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-635 plan §5 T2/T7 + the hint tour end to end on the real runner: the HUD beat waits for its target, holds its
    /// focus, shuts the field (a field tap answers with the miss pulse), completes on the ACCEPTED activation and drops the
    /// gate and the focus on a mid-tour stop; the booster recipe chains it with a silent gate on winningOrder[0].
    /// </summary>
    [TestFixture]
    public sealed class HudTapStepTests
    {
        private FakeHudTarget _target;
        private TourRig _rig;

        [SetUp]
        public void SetUp()
        {
            _target = new FakeHudTarget("hint_button");
            _rig = new TourRig(recipe: new BoosterTourRecipe("hint_test", "hint_button"),
                hudTargets: new HudTourTargets(_target));
        }

        [TearDown]
        public void TearDown() => _rig.Dispose();

        [Test]
        public void TargetNotHosted_CompletesAtOnce_WithoutShowing()
        {
            var completed = 0;
            using var _ = new HudTapStep("absent").Enter(_rig.Context()).Subscribe(__ => completed++);

            Assert.That(completed, Is.EqualTo(1));
            Assert.That(_rig.View.HudTaps, Is.Empty);
        }

        [Test]
        public void NotReady_ShutsTheField_ShowsNothing_UntilReady()
        {
            using var _ = new HudTapStep("hint_button").Enter(_rig.Context()).Subscribe();

            Assert.That(_rig.Gate.Allows(TourRig.Tap(TourRig.BottomPos)), Is.False, "the field admits nothing");
            Assert.That(_rig.View.HudTaps, Is.Empty);
            Assert.That(_target.Holds, Is.EqualTo(0));

            _target.SetReady(true);

            Assert.That(_rig.View.HudTaps.Count, Is.EqualTo(1));
            Assert.That(_rig.View.HudTaps[0].Target, Is.SameAs(_target));
            Assert.That(_target.Holds, Is.EqualTo(1), "the button is exempt from the tour's dim");
        }

        [Test]
        public void FieldTap_PulsesTheMiss()
        {
            _target.SetReady(true);
            using var _ = new HudTapStep("hint_button").Enter(_rig.Context()).Subscribe();

            _rig.Gate.NotifyRejected(TourRig.Tap(TourRig.BottomPos));

            Assert.That(_rig.View.Misses, Is.EqualTo(1));
        }

        [Test]
        public void Activated_Completes_AndReleasesFocusAndGate()
        {
            _target.SetReady(true);
            var completed = 0;
            var sub = new HudTapStep("hint_button").Enter(_rig.Context()).Take(1).Subscribe(__ => completed++);

            _target.Activate();

            Assert.That(completed, Is.EqualTo(1));
            sub.Dispose();
            Assert.That(_target.Holds, Is.EqualTo(0));
            Assert.That(_rig.Gate.Allows(TourRig.Tap(TourRig.BottomPos)), Is.True);
        }

        [Test]
        public void DisposedBeforeActivation_DropsGateAndFocus()
        {
            _target.SetReady(true);
            var completed = 0;
            var sub = new HudTapStep("hint_button").Enter(_rig.Context()).Subscribe(__ => completed++);

            sub.Dispose(); // StopSequence on a LOSS reload: no Exit
            _target.Activate();

            Assert.That(completed, Is.EqualTo(0));
            Assert.That(_target.Holds, Is.EqualTo(0));
            Assert.That(_rig.Gate.Allows(TourRig.Tap(TourRig.BottomPos)), Is.True);
        }

        [Test]
        public void ReadyButCannotActivate_CompletesWithError_NoSoftlock()
        {
            _target.CanActivateValue.Value = false;
            _target.SetReady(true);
            var completed = 0;

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("cannot be activated"));
            using var _ = new HudTapStep("hint_button").Enter(_rig.Context()).Subscribe(__ => completed++);

            Assert.That(completed, Is.EqualTo(1));
            Assert.That(_rig.View.HudTaps, Is.Empty);
            Assert.That(_target.Holds, Is.EqualTo(0));
        }

        [Test]
        public void Recipe_NoOrder_BuildsNull_Else_HudBeatThenSilentFirstPiece()
        {
            var recipe = new BoosterTourRecipe("hint_test", "hint_button");

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("no winningOrder"));
            Assert.That(recipe.Build(TourRig.TwoBoxLevel(Array.Empty<int>())), Is.Null);

            var tour = recipe.Build(TourRig.TwoBoxLevel(new[] { 1, 0 }));
            Assert.That(tour.Id, Is.EqualTo("hint_test"));
            Assert.That(tour.Steps.Count, Is.EqualTo(2));
            Assert.That(tour.Steps[0], Is.TypeOf<HudTapStep>());
            Assert.That(((HudTapStep)tour.Steps[0]).TargetId, Is.EqualTo("hint_button"));
            var tap = (TapPieceStep)tour.Steps[1];
            Assert.That(tap.Id, Is.EqualTo("tap_1"));
            Assert.That(tap.PlacementIndex, Is.EqualTo(1), "winningOrder[0], not piece 0");
            Assert.That(tap.Silent, Is.True);
        }

        [Test]
        public void HintTour_OnTheRunner_EndsOnTheFirstPieceRelease_WithoutAWin()
        {
            _rig.Model.SetFieldAtRest(true);
            _rig.Runner.Start();
            Assert.That(_rig.Interaction.IsTourFocused.Value, Is.True);
            Assert.That(_rig.View.HudTaps, Is.Empty, "waits for the button");

            _target.SetReady(true);
            Assert.That(_rig.View.HudTaps.Count, Is.EqualTo(1));

            _target.Activate();
            Assert.That(_target.Holds, Is.EqualTo(0), "after the beat the button dims with the rest of the HUD");
            Assert.That(_rig.View.Taps, Is.Empty, "the silent beat shows nothing — the hint's outline points");
            Assert.That(_rig.Gate.Allows(TourRig.Tap(TourRig.TopPos)), Is.False, "only winningOrder[0] is admitted");
            Assert.That(_rig.Gate.Allows(TourRig.Tap(TourRig.BottomPos)), Is.True);

            _rig.Release(0);

            Assert.That(_rig.Progress.IsCompleted("hint_test"), Is.True, "no win step: the tour ends on the tap");
            Assert.That(_rig.Interaction.IsTourFocused.Value, Is.False);
        }

        [Test]
        public void HintTour_FirstMissOnThePieceBeat_BringsTheTourFinger()
        {
            _rig.Model.SetFieldAtRest(true);
            _target.SetReady(true);
            _rig.Runner.Start();
            _target.Activate();

            _rig.Gate.NotifyRejected(TourRig.Tap(TourRig.TopPos));

            Assert.That(_rig.View.Taps.Count, Is.EqualTo(1), "a miss shows the usual beat at once");
            Assert.That(_rig.View.Taps[0].Delay, Is.EqualTo(0f));
            Assert.That(_rig.View.Taps[0].Origin, Is.EqualTo(TourRig.BottomPos));

            _rig.Gate.NotifyRejected(TourRig.Tap(TourRig.TopPos));
            Assert.That(_rig.View.Taps.Count, Is.EqualTo(1), "shown once, then only pulses");
        }

        /// <summary>Hand-driven <see cref="IHudTourTarget"/>.</summary>
        internal sealed class FakeHudTarget : IHudTourTarget
        {
            private readonly ReactiveProperty<bool> _ready = new(false);
            public readonly ReactiveProperty<bool> CanActivateValue = new(true);
            private readonly Subject<Unit> _activated = new();
            public int Holds;

            public FakeHudTarget(string id) => Id = id;

            public string Id { get; }

            public ReadOnlyReactiveProperty<bool> Ready => _ready;

            public ReadOnlyReactiveProperty<bool> CanActivate => CanActivateValue;

            public Observable<Unit> Activated => _activated;

            public void SetReady(bool ready) => _ready.Value = ready;

            public void Activate() => _activated.OnNext(Unit.Default);

            public bool TryGetScreenCorners(Vector2[] corners4) => false;

            public IDisposable HoldFocus()
            {
                Holds++;
                return Disposable.Create(() => Holds--);
            }
        }
    }
}
