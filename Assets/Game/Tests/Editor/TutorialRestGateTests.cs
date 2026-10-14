using System.Collections.Generic;
using Gameplay.Core;
using Gameplay.Core.Adapters;
using Gameplay.Tutorial;
using NUnit.Framework;
using R3;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-524: a tour step shows and admits the next piece only once the field is at rest
    /// (<see cref="GameModel.FieldAtRest"/>), not on the previous piece's release — following the finger without a
    /// pause used to drop the sail onto a hull still in the air and lose level 1. Pure model + real
    /// <see cref="TutorialInputGate"/>; the field's rest is driven by hand through <see cref="GameModel.SetFieldAtRest"/>
    /// exactly as the mechanic controller publishes it, and a release goes through <see cref="GameModel.RegisterAttempt"/>
    /// exactly as the controller's ReleasePiece does.
    /// </summary>
    [TestFixture]
    public sealed class TutorialRestGateTests
    {
        private static readonly Vec2 BottomPos = new(0f, 0f);
        private static readonly Vec2 TopPos = new(0f, 3f);

        private GameModel _model;
        private RecordingView _view;
        private TutorialInputGate _gate;
        private TutorialContext _context;

        [SetUp]
        public void SetUp()
        {
            _model = new GameModel();
            _model.LoadLevel(TwoBoxLevel());
            _view = new RecordingView();
            _gate = new TutorialInputGate();
            _context = new TutorialContext(_model, _view, _gate, Observable.Empty<InputIntent>(), new NullTapTolerance());
        }

        [Test]
        public void Release_DropsFieldAtRest_BeforeReleaseObserversRun()
        {
            _model.SetFieldAtRest(true);
            var seenAtRelease = true;
            using var _ = _model.PieceReleased.Subscribe(__ => seenAtRelease = _model.FieldAtRest.CurrentValue);

            _model.RegisterAttempt(_model.Pieces[0].InstanceId);

            Assert.That(seenAtRelease, Is.False);
        }

        [Test]
        public void LoadLevel_ResetsFieldAtRest()
        {
            _model.SetFieldAtRest(true);

            _model.LoadLevel(TwoBoxLevel());

            Assert.That(_model.FieldAtRest.CurrentValue, Is.False);
        }

        [Test]
        public void AssembleInOrder_NextTargetNotArmedUntilFieldAtRest()
        {
            _model.SetFieldAtRest(true);
            using var sub = new AssembleInOrderStep(new[] { 0, 1 }).Enter(_context).Subscribe();

            Assert.That(_view.Highlights, Is.EqualTo(new[] { BottomPos }));
            Assert.That(_gate.Allows(Tap(BottomPos)), Is.True);
            Assert.That(_gate.Allows(Tap(TopPos)), Is.False);

            Release(0); // the bottom piece starts falling

            // Falling: the finger is gone and NO tap passes — not the top piece, not anything else.
            Assert.That(_view.Highlights.Count, Is.EqualTo(1), "the next piece must not be highlighted mid-fall");
            Assert.That(_view.Clears, Is.GreaterThan(0));
            Assert.That(_gate.Allows(Tap(TopPos)), Is.False, "the next piece must not be tappable mid-fall");
            Assert.That(_gate.Allows(Tap(new Vec2(5f, 5f))), Is.False, "the gate must not open to everything mid-fall");

            _model.SetFieldAtRest(true); // the bottom piece landed, the field is quiet

            Assert.That(_view.Highlights, Is.EqualTo(new[] { BottomPos, TopPos }));
            Assert.That(_gate.Allows(Tap(TopPos)), Is.True);
        }

        [Test]
        public void AssembleInOrder_WaitsForInitialRest()
        {
            using var sub = new AssembleInOrderStep(new[] { 0, 1 }).Enter(_context).Subscribe();

            Assert.That(_view.Highlights, Is.Empty);
            Assert.That(_gate.Allows(Tap(BottomPos)), Is.False);

            _model.SetFieldAtRest(true);

            Assert.That(_view.Highlights, Is.EqualTo(new[] { BottomPos }));
            Assert.That(_gate.Allows(Tap(BottomPos)), Is.True);
        }

        [Test]
        public void AssembleInOrder_CompletesOnLastRelease_AndDisposeReleasesTheGate()
        {
            _model.SetFieldAtRest(true);
            var completed = 0;
            var sub = new AssembleInOrderStep(new[] { 0, 1 }).Enter(_context).Subscribe(_ => completed++);

            Release(0);
            Assert.That(completed, Is.EqualTo(0));
            _model.SetFieldAtRest(true);
            Release(1);

            Assert.That(completed, Is.EqualTo(1), "completion contract unchanged: one emission after the LAST release");
            sub.Dispose();
            Assert.That(_gate.IsBlocked, Is.False);
            Assert.That(_gate.Allows(Tap(new Vec2(5f, 5f))), Is.True);
        }

        [Test]
        public void AssembleInOrder_DisposeMidWait_ReleasesTheBlock()
        {
            _model.SetFieldAtRest(true);
            var sub = new AssembleInOrderStep(new[] { 0, 1 }).Enter(_context).Subscribe();
            Release(0);
            Assert.That(_gate.IsBlocked, Is.True);

            sub.Dispose(); // LOSS-reload disposes without Exit (TutorialRunner.StopSequence)

            Assert.That(_gate.IsBlocked, Is.False);
        }

        private void Release(int placementIndex)
        {
            var piece = _model.Pieces[placementIndex];
            piece.State = PieceLifecycle.Falling;
            _model.RegisterAttempt(piece.InstanceId);
        }

        private static InputIntent Tap(Vec2 p) => new(p.X, p.Y);

        private static LevelData TwoBoxLevel()
        {
            var box = new PieceType("box", Shape.Box(1f, 1f), new MaterialProfile(1f, 0.6f, 0.05f));
            return new LevelData
            {
                LevelId = "two_boxes",
                Pieces = new List<PiecePlacement> { new(box, BottomPos), new(box, TopPos) },
            };
        }

        private sealed class RecordingView : ITutorialView
        {
            public readonly List<Vec2> Highlights = new();
            public int Clears;

            public void Highlight(Vec2 origin, float rotation, Shape shape, HighlightStyle style) =>
                Highlights.Add(origin);

            public void Highlight(int key, Vec2 origin, float rotation, Shape shape, HighlightStyle style) =>
                Highlights.Add(origin);

            public void Clear() => Clears++;
        }
    }
}
