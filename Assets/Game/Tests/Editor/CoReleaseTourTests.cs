using System.Collections.Generic;
using Gameplay.Core;
using Gameplay.Core.Adapters;
using Gameplay.Tutorial;
using NUnit.Framework;
using R3;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-526: the co-release tour lets the player place everything that comes BEFORE the taught group in the
    /// level's <see cref="LevelData.WinningOrder"/>, and only then calls the group. Before the fix the tour was the pair
    /// step alone: on grand_seesaw it admitted only the pans and locked the fulcrum and beam that the winning order puts
    /// first. The level here is shaped the same way — two pieces, then the group [2,3] — but carries no grand_seesaw
    /// index: the order comes from the level's data. Pure model + real <see cref="TutorialInputGate"/>; rest and release
    /// are driven exactly as the mechanic controller publishes them (same harness as <see cref="TutorialRestGateTests"/>).
    /// </summary>
    [TestFixture]
    public sealed class CoReleaseTourTests
    {
        private static readonly Vec2[] Positions = { new(0f, 0f), new(3f, 0f), new(6f, 0f), new(9f, 0f) };
        private const string LevelId = "pair_after_two";

        private GameModel _model;
        private TutorialInputGate _gate;
        private TutorialContext _context;

        [SetUp]
        public void SetUp()
        {
            _model = new GameModel();
            _model.LoadLevel(Level(new[] { 0, 1, 2, 3 }, new[] { 2, 3 }));
            _gate = new TutorialInputGate();
            _context = new TutorialContext(_model, new SilentView(), _gate, Observable.Empty<InputIntent>(),
                new NullTapTolerance());
        }

        [Test]
        public void PredecessorsOfGroup_IsTheWinningOrderPrefixBeforeTheGroup()
        {
            Assert.That(AssembleInOrderStep.PredecessorsOfGroup(Level(new[] { 1, 0, 3, 2 }, new[] { 2, 3 }), 0),
                Is.EqualTo(new[] { 1, 0 }));
            Assert.That(AssembleInOrderStep.PredecessorsOfGroup(Level(new[] { 2, 3, 0, 1 }, new[] { 2, 3 }), 0),
                Is.Empty, "a group first in the winning order has no lead-in");
        }

        [Test]
        public void PredecessorsOfGroup_NoWinningOrderOrNoGroup_IsEmpty()
        {
            Assert.That(AssembleInOrderStep.PredecessorsOfGroup(Level(null, new[] { 2, 3 }), 0), Is.Empty);
            Assert.That(AssembleInOrderStep.PredecessorsOfGroup(Level(new[] { 0, 1, 2, 3 }), 0), Is.Empty);
            Assert.That(AssembleInOrderStep.PredecessorsOfGroup(Level(new[] { 0, 1, 2, 3 }, new[] { 2, 3 }), 1),
                Is.Empty);
        }

        [Test]
        public void PredecessorsOfGroup_StopsBeforeAnotherGroupsMember()
        {
            // Teaching the SECOND group [2,3], whose way is crossed by the first group [0,1]: a one-finger step cannot
            // release 0 alone, so the prefix must not list it (the tour would hang on it).
            var level = Level(new[] { 0, 1, 2, 3 }, new[] { 0, 1 }, new[] { 2, 3 });

            Assert.That(AssembleInOrderStep.PredecessorsOfGroup(level, 1), Is.Empty);
        }

        [Test]
        public void LeadIn_AdmitsTheNextPredecessorAndNotTheGroup()
        {
            _model.SetFieldAtRest(true);
            var completed = 0;
            using var sub = AssembleInOrderStep.BeforeCoReleaseGroup().Enter(_context).Subscribe(_ => completed++);

            Assert.That(_gate.Allows(Tap(0)), Is.True, "the first predecessor is tappable");
            Assert.That(_gate.Allows(Tap(2)), Is.False, "the group waits for its predecessors");
            Assert.That(_gate.Allows(Tap(3)), Is.False, "the group waits for its predecessors");

            Release(0);
            _model.SetFieldAtRest(true);

            Assert.That(_gate.Allows(Tap(1)), Is.True, "the next predecessor is tappable");
            Assert.That(_gate.Allows(Tap(2)), Is.False);
            Assert.That(_gate.Allows(Tap(3)), Is.False);

            Release(1);

            Assert.That(completed, Is.EqualTo(1), "the lead-in ends with its last predecessor");
        }

        [Test]
        public void Tour_PlacesPredecessorsThenCallsThePair()
        {
            var tour = new TutorialDefinition("pair_tour",
                new ITutorialStep[] { AssembleInOrderStep.BeforeCoReleaseGroup(), new SimultaneousTapStep() });
            var progress = new MemoryProgress();
            var runner = new TutorialRunner(_model, new OnGate(), progress, new NullTutorialAnalytics(), new SilentView(),
                _gate, new NoInput(), new NullTapTolerance(),
                new TutorialCatalog(new Dictionary<string, TutorialDefinition> { [LevelId] = tour }));
            runner.Start();
            _model.SetFieldAtRest(true);

            Assert.That(_gate.Allows(Tap(0)), Is.True);
            Assert.That(_gate.Allows(Tap(2)), Is.False, "before the fix the pair was the only thing tappable here");

            Release(0);
            _model.SetFieldAtRest(true);
            Release(1);
            Assert.That(_gate.Allows(Tap(2)), Is.False, "the pair is not called onto a moving field");
            _model.SetFieldAtRest(true);

            Assert.That(_gate.Allows(Tap(2)), Is.True, "after the predecessors the pair is called");
            Assert.That(_gate.Allows(Tap(3)), Is.True);

            Release(2);
            Release(3);

            Assert.That(progress.Completed, Does.Contain("pair_tour"));
            runner.Dispose();
        }

        [Test]
        public void Tour_GroupFirstInOrder_CallsThePairAtOnce()
        {
            _model.LoadLevel(Level(new[] { 2, 3, 0, 1 }, new[] { 2, 3 }));
            _model.SetFieldAtRest(true);
            var completed = 0;
            using var sub = AssembleInOrderStep.BeforeCoReleaseGroup().Enter(_context).Subscribe(_ => completed++);

            Assert.That(completed, Is.EqualTo(1), "nothing precedes the group — the lead-in is a no-op");
        }

        private void Release(int placementIndex)
        {
            var piece = _model.Pieces[placementIndex];
            piece.State = PieceLifecycle.Falling;
            _model.RegisterAttempt(piece.InstanceId);
        }

        private static InputIntent Tap(int placementIndex) =>
            new(Positions[placementIndex].X, Positions[placementIndex].Y);

        private static LevelData Level(IReadOnlyList<int> winningOrder, params IReadOnlyList<int>[] groups)
        {
            var box = new PieceType("box", Shape.Box(1f, 1f), new MaterialProfile(1f, 0.6f, 0.05f));
            var pieces = new List<PiecePlacement>();
            foreach (var position in Positions)
                pieces.Add(new PiecePlacement(box, position));
            return new LevelData
            {
                LevelId = LevelId,
                Pieces = pieces,
                WinningOrder = winningOrder,
                CoReleaseGroups = groups.Length > 0 ? groups : null,
            };
        }

        private sealed class SilentView : ITutorialView
        {
            public void Highlight(Vec2 origin, float rotation, Shape shape, HighlightStyle style) { }

            public void Highlight(int key, Vec2 origin, float rotation, Shape shape, HighlightStyle style) { }

            public void Clear() { }
        }

        private sealed class OnGate : ITutorialGate
        {
            public bool Enabled => true;
        }

        private sealed class MemoryProgress : ITutorialProgressStore
        {
            public readonly HashSet<string> Completed = new();

            public bool IsCompleted(string tutorialId) => Completed.Contains(tutorialId);

            public void MarkCompleted(string tutorialId) => Completed.Add(tutorialId);

            public void Reset(string tutorialId) => Completed.Remove(tutorialId);
        }

        private sealed class NoInput : IGameplayInputSource
        {
            public Observable<InputIntent> Intents => Observable.Empty<InputIntent>();
        }
    }
}
