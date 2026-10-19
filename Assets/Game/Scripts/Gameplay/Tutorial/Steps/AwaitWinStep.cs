using Gameplay.Core;
using R3;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// The tour's closing step (TASK-634, replaces the TASK-613 "Совпало" beat <c>MatchTargetStep</c>): shows nothing
    /// and completes on <c>Won</c> — the same event the win checkpoint is written on — so the tour is marked completed
    /// after the WIN, never on the last release. A <c>Lost</c> never completes it: the reload's StopSequence disposes
    /// the step and the tour replays from step 0. <see cref="GameModel.Outcome"/> replays its current value on
    /// Subscribe; on entry it is <c>None</c> (reset by <see cref="GameModel.LoadLevel"/>), and a synchronous Won would
    /// be held by the runner's reentrancy guard anyway. The win itself is drawn by the common win beat «В рамку».
    /// </summary>
    public sealed class AwaitWinStep : ITutorialStep
    {
        public string Id => "win";

        public Observable<Unit> Enter(TutorialContext context) =>
            context.Model.Outcome.Where(o => o == GameOutcome.Won).Take(1).AsUnitObservable();

        public void Exit(TutorialContext context)
        {
        }
    }
}
