namespace Gameplay.Tutorial
{
    /// <summary>
    /// Onboarding-funnel analytics seam (TASK-049 RFC §Q5). Prod <see cref="SayKitTutorialAnalytics"/> delegates
    /// to the SDK <c>AnalyticsService.Tutorial</c> (→ SayKit); bringup <see cref="NullTutorialAnalytics"/> is a
    /// no-op (no <c>GlobalService</c>/SayKit in the R5-isolated scope). <paramref name="tutorialId"/> MUST equal
    /// the id written to the progress store so the funnel and the save key stay one dictionary (human 15:21).
    /// </summary>
    public interface ITutorialAnalytics
    {
        void LogStep(string tutorialId, string stepId);

        void LogComplete(string tutorialId);
    }
}
