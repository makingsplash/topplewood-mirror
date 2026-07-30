namespace Gameplay.Tutorial
{
    /// <summary>
    /// Bringup <see cref="ITutorialAnalytics"/> (TASK-049 RFC §Q5): no-op. The R5-isolated scope has no
    /// <c>GlobalService</c>/SayKit, so delegating to the SDK funnel would NRE — bringup deliberately does not
    /// emit tutorial analytics.
    /// </summary>
    public sealed class NullTutorialAnalytics : ITutorialAnalytics
    {
        public void LogStep(string tutorialId, string stepId)
        {
        }

        public void LogComplete(string tutorialId)
        {
        }
    }
}
