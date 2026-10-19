using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Gameplay.Core;
using Gameplay.PlayScreen;
using Gameplay.PlayScreen.Sequence;
using Gameplay.Sequence;
using Gameplay.View;
using NUnit.Framework;
using R3;
using SGS.Infrastructure.Sequence;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-634 plan §7 T4 + T6. The flow seam of the win beat, on the bringup <see cref="SequencedResolutionFlow"/>: the
    /// prod <see cref="GatedResolutionFlow"/> shows its screen through the static <c>Service.UI</c>, which an EditMode
    /// test must not touch (plan §7), while the bringup flow builds its banner through an injectable factory — so the
    /// "banner started" moment is observable. Both flows share the contract: ask <see cref="IWinBeat.TryBegin"/> on Won;
    /// false → the old path at once; true → the next step only when the beat completes, its subscription disposed.
    /// The 4 s ceiling runs on a real-time timer and is not exercised here (no fake time provider in the project).
    /// T6: the beat's timeline segments stay in order (a number turned in config must not tear the timeline).
    /// </summary>
    [TestFixture]
    public sealed class WinBeatFlowTests
    {
        private FakeBeat _beat;
        private FakeView _view;
        private SequencedResolutionFlow _flow;
        private PlayScreenInteraction _interaction;

        [SetUp]
        public void SetUp()
        {
            _beat = new FakeBeat();
            _view = new FakeView();
            _interaction = new PlayScreenInteraction();
            _flow = new SequencedResolutionFlow(new OutcomeFactory(), _view, new PlayScreenConfig(), _interaction, _beat);
        }

        [TearDown]
        public void TearDown() => _flow.Dispose();

        [Test]
        public void Won_BeatCannotBegin_BannerAtOnce()
        {
            _beat.CanBegin = false;

            _flow.Present(GameOutcome.Won, () => { }, false);

            Assert.That(_beat.TryBeginCalls, Is.EqualTo(1));
            Assert.That(_view.Shown, Is.EqualTo(1), "nothing to play → the plain path, no wait");
            Assert.That(_interaction.IsBusy.Value, Is.True);
        }

        [Test]
        public void Won_BeatPlays_BannerOnlyAfterItCompletes_SubscriptionDisposed()
        {
            _flow.Present(GameOutcome.Won, () => { }, false);

            Assert.That(_beat.Subscriptions, Is.EqualTo(1), "the cold beat is subscribed = played");
            Assert.That(_view.Shown, Is.EqualTo(0), "the banner waits for the beat");

            _beat.Complete();

            Assert.That(_view.Shown, Is.EqualTo(1));
            Assert.That(_beat.Disposals, Is.EqualTo(1), "the flow lets go of the finished beat");
        }

        [Test]
        public void Lost_NeverAsksTheBeat()
        {
            _flow.Present(GameOutcome.Lost, () => { }, false);

            Assert.That(_beat.TryBeginCalls, Is.EqualTo(0));
            Assert.That(_view.Shown, Is.EqualTo(1));
        }

        [Test]
        public void Teardown_MidBeat_KillsTheBeat_NoBanner()
        {
            _flow.Present(GameOutcome.Won, () => { }, false);

            _flow.Dispose();

            Assert.That(_beat.Disposals, Is.EqualTo(1));
            Assert.That(_view.Shown, Is.EqualTo(0));
        }

        [Test]
        public void WinBeatConfig_SegmentsInOrder()
        {
            var c = new WinBeatConfig();
            Assert.That(c.FlightStart, Is.GreaterThan(0f));
            Assert.That(c.SquashPeak, Is.GreaterThan(0f).And.LessThan(c.StretchPeak));
            Assert.That(c.StretchPeak, Is.LessThan(c.BounceEnd));
            Assert.That(c.FlightStart, Is.LessThanOrEqualTo(c.BounceEnd), "the flight takes off before the bounce settles");
            Assert.That(c.BounceEnd, Is.LessThan(c.FlightEnd));
            Assert.That(c.FlightEnd, Is.EqualTo(c.ReceiveStart), "the frame answers in the landing frame");
            Assert.That(c.ReceiveStart, Is.LessThan(c.ReceiveEnd));
            Assert.That(c.ReceiveEnd, Is.LessThanOrEqualTo(c.BeatEnd));
            Assert.That(c.BeatEnd, Is.LessThan(new PlayScreenConfig().MaxWinBeatSeconds), "the ceiling is a safety net");
        }

        private sealed class FakeBeat : IWinBeat
        {
            public bool CanBegin = true;
            public int TryBeginCalls;
            public int Subscriptions;
            public int Disposals;
            private Observer<Unit> _observer;

            public bool TryBegin(out Observable<Unit> beat)
            {
                TryBeginCalls++;
                beat = CanBegin
                    ? Observable.Create<Unit>(o =>
                    {
                        Subscriptions++;
                        _observer = o;
                        return Disposable.Create(() => Disposals++);
                    })
                    : null;
                return CanBegin;
            }

            public void Complete()
            {
                var o = _observer;
                _observer = null;
                o?.OnNext(Unit.Default);
                o?.OnCompleted();
            }
        }

        private sealed class FakeView : IPlayScreenView
        {
            public int Shown;

            // Never completes: the test only needs to see THAT the banner started.
            public UniTask ShowOutcomeAsync(GameOutcome outcome, CancellationToken ct)
            {
                Shown++;
                return new UniTaskCompletionSource().Task;
            }

            public void HideOutcome() { }
        }

        private sealed class OutcomeFactory : ISequenceFactory
        {
            public TElement CreateElement<TElement>(params object[] args) where TElement : ILevelSequenceElement =>
                throw new NotSupportedException();

            public TGameSequence CreateGameSequence<TGameSequence>(params object[] args) where TGameSequence : GameSequence =>
                (TGameSequence)(GameSequence)new LevelOutcomeSequence((IPlayScreenView)args[0], (PlayScreenConfig)args[1],
                    (GameOutcome)args[2], (Action)args[3], (CancellationToken)args[4]);
        }
    }
}
