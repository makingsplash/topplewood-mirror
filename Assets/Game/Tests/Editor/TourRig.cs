using System;
using System.Collections.Generic;
using Gameplay.Core;
using Gameplay.Core.Adapters;
using Gameplay.PlayScreen;
using Gameplay.Tutorial;

namespace Gameplay.Tests
{
    /// <summary>
    /// A real <see cref="TutorialRunner"/> on a real <see cref="GameModel"/> and <see cref="TutorialInputGate"/>, with a
    /// <see cref="FakeTourView"/> and in-memory progress (TASK-613). The field's rest is driven by hand through
    /// <see cref="GameModel.SetFieldAtRest"/>, exactly as the mechanic controller publishes it, and a release goes
    /// through <see cref="GameModel.RegisterAttempt"/>, exactly as the controller's ReleasePiece does.
    /// </summary>
    internal sealed class TourRig : IDisposable
    {
        public const string TourId = "test_tour";
        public static readonly Vec2 BottomPos = new(0f, 0f);
        public static readonly Vec2 TopPos = new(0f, 3f);

        public readonly GameModel Model = new();
        public readonly FakeTourView View = new();
        public readonly TutorialInputGate Gate = new();
        public readonly TourConfig Config = new();
        public readonly PlayScreenInteraction Interaction = new();
        public readonly MemoryProgress Progress = new();
        public readonly RecordingAnalytics Analytics = new();
        public readonly TutorialRunner Runner;
        private readonly LevelData _level;

        public TourRig(LevelData level = null)
        {
            _level = level ?? TwoBoxLevel();
            Model.LoadLevel(_level);
            var catalog = new TutorialCatalog(new Dictionary<string, ITourRecipe>
            {
                [_level.LevelId] = new AssembleTourRecipe(TourId),
            });
            Runner = new TutorialRunner(Model, new OpenGate(), Progress, Analytics, View, Gate,
                new NullTapTolerance(), catalog, Config, Interaction);
        }

        public TutorialContext Context() =>
            new(Model, View, Gate, new NullTapTolerance(), Config);

        /// <summary>A retry/restart: the campaign controller reloads the level in place, firing LevelLoaded.</summary>
        public void Reload() => Model.LoadLevel(_level);

        /// <summary>The verdict, exactly as the mechanic controller resolves it (TASK-634: the tour ends on Won).</summary>
        public void Resolve(GameOutcome outcome) => Model.Resolve(outcome);

        public void Release(int placementIndex)
        {
            var piece = Model.Pieces[placementIndex];
            piece.State = PieceLifecycle.Falling;
            Model.RegisterAttempt(piece.InstanceId);
        }

        /// <summary>Tap both pieces in order, landing each — the whole tap part of the tour.</summary>
        public void PlayBothTaps()
        {
            Model.SetFieldAtRest(true);
            Release(0);
            Model.SetFieldAtRest(true);
            Release(1);
        }

        public static InputIntent Tap(Vec2 p) => new(p.X, p.Y);

        /// <summary>Two boxes stacked: winning order hull-then-sail like sailboat, target = the stack.</summary>
        public static LevelData TwoBoxLevel(IReadOnlyList<int> winningOrder = null)
        {
            var box = new PieceType("box", Shape.Box(1f, 1f), new MaterialProfile(1f, 0.6f, 0.05f));
            var outline = new[] { new Vec2(-0.5f, -0.5f), new Vec2(0.5f, -0.5f), new Vec2(0.5f, 3.5f), new Vec2(-0.5f, 3.5f) };
            return new LevelData
            {
                LevelId = "two_boxes",
                Pieces = new List<PiecePlacement> { new(box, BottomPos), new(box, TopPos) },
                WinningOrder = winningOrder ?? new[] { 0, 1 },
                Target = new TargetFigure(new Polygon(outline), new List<TargetSlot>
                {
                    new("box", BottomPos, 0f),
                    new("box", new Vec2(0f, 1f), 0f),
                }),
            };
        }

        public void Dispose() => Runner.Dispose();

        public sealed class MemoryProgress : ITutorialProgressStore
        {
            private readonly HashSet<string> _done = new();

            public bool IsCompleted(string tutorialId) => _done.Contains(tutorialId);

            public void MarkCompleted(string tutorialId) => _done.Add(tutorialId);

            public void Reset(string tutorialId) => _done.Remove(tutorialId);
        }

        public sealed class RecordingAnalytics : ITutorialAnalytics
        {
            public readonly List<string> Steps = new();
            public int Completes;

            public void LogStep(string tutorialId, string stepId) => Steps.Add(stepId);

            public void LogComplete(string tutorialId) => Completes++;
        }

        private sealed class OpenGate : ITutorialGate
        {
            public bool Enabled => true;
        }
    }
}
