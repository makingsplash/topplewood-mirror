using Services;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Production <see cref="ITutorialAnalytics"/> (TASK-049 RFC §Q5): delegates to the SDK tutorial funnel
    /// <c>GlobalService.Analytics.Tutorial</c> (→ <c>SayKit.TrackTutorialStep/Completed</c>). Analytics resolves
    /// statically via <c>GlobalService</c> (not DI), so this impl is prod-only; bringup uses the no-op
    /// <see cref="NullTutorialAnalytics"/>. Guarded against a half-booted static service so it can never NRE
    /// (prod boot is an open TECH_DEBT — this path is code-verified, RFC §9).
    /// </summary>
    public sealed class SayKitTutorialAnalytics : ITutorialAnalytics
    {
        public void LogStep(string tutorialId, string stepId)
        {
            GlobalService.Analytics?.Tutorial?.LogTutorialStep(tutorialId, stepId);
        }

        public void LogComplete(string tutorialId)
        {
            // The SDK completion event carries no name; the step events above already tag the tutorialId.
            GlobalService.Analytics?.Tutorial?.LogTutorialComplete();
        }
    }
}
