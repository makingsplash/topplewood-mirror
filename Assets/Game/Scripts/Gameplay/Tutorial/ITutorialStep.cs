using R3;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// One code-driven tutorial step (TASK-049 RFC §Q2): data-driven JSON tours are a follow-up until the
    /// condition vocabulary stabilises. <see cref="Enter"/> arms the step (highlight, input block, …) and
    /// RETURNS its completion signal — a reactive observable over domain state, never an Update poll. The
    /// runner subscribes to the first emission, calls <see cref="Exit"/>, then advances. Returning the signal
    /// from Enter (vs a separate property) keeps arm-and-observe atomic.
    /// </summary>
    public interface ITutorialStep
    {
        /// <summary>Stable step id for analytics (see <see cref="ITutorialAnalytics.LogStep"/>).</summary>
        string Id { get; }

        Observable<Unit> Enter(TutorialContext context);

        void Exit(TutorialContext context);
    }
}
