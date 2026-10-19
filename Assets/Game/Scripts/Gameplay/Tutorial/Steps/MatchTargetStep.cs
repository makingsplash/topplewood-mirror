using System.Collections.Generic;
using Gameplay.Core;
using R3;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// The tour's closing "Совпало" beat (TASK-613 plan §3.7): "the figure you built IS the picture". It draws nothing
    /// on Enter — it ARMS <see cref="Gameplay.PlayScreen.WinInterlude"/> with the view's
    /// <see cref="ITourView.PlayMatch"/>, and the resolution flow plays it on the next <c>Won</c> instead of its plain
    /// settle-beat. The step completes when the number is over, so the runner marks the tour completed only AFTER the
    /// beat (the former mark on the last release, <c>TutorialRunner</c> before TASK-613, is gone by construction).
    ///
    /// Lost / restart instead of Won: nobody takes the number, the LevelLoaded reload disposes this step (the runner's
    /// StopSequence), which disarms it — the tour starts over. The flow abandoning the number early (its ceiling, or a
    /// scope teardown) also ends the beat: the win checkpoint is already written by then, so marking the tour is the
    /// consistent outcome, not a leak.
    /// </summary>
    public sealed class MatchTargetStep : ITutorialStep
    {
        public string Id => "match";

        public Observable<Unit> Enter(TutorialContext context)
        {
            return Observable.Create<Unit>(observer =>
            {
                var done = false;

                void Complete()
                {
                    if (done)
                        return;
                    done = true;
                    observer.OnNext(Unit.Default);
                    observer.OnCompleted();
                }

                var cue = BuildCue(context.Model.CurrentLevel);
                return context.Interlude.Arm(() => context.View.PlayMatch(cue)
                    .Do(onCompleted: _ => Complete(), onDispose: Complete));
            });
        }

        public void Exit(TutorialContext context)
        {
        }

        /// <summary>The level's target plus the shape of every kind the player is given (decoys are not part of the
        /// target, the same map the target card itself is built from).</summary>
        public static MatchCue BuildCue(LevelData level)
        {
            var shapes = new Dictionary<string, Shape>();
            if (level?.Pieces != null)
                foreach (var placement in level.Pieces)
                    if (placement.Type != null && !shapes.ContainsKey(placement.Type.Kind))
                        shapes[placement.Type.Kind] = placement.Type.Shape;
            return new MatchCue(level?.Target, shapes);
        }
    }
}
