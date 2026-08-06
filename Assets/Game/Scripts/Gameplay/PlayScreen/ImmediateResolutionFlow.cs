using System;
using Gameplay.Core;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// Production <see cref="ILevelResolutionFlow"/> (TASK-116, RFC §2.2): transitions immediately, exactly
    /// reproducing the pre-TASK-116 behaviour (<see cref="Gameplay.Adapters.CampaignProgressionController"/>
    /// used to advance/reload inline the moment the level resolved). No play screen, no hold — bit-for-bit the
    /// old flow. Bound only by the prod <c>GameplayLifetimeScope</c>, which is not in any build today
    /// (LoadingGame TODO), so this is the behaviour-preserving DI symmetry, not a shipped path.
    /// </summary>
    public sealed class ImmediateResolutionFlow : ILevelResolutionFlow
    {
        public void Present(GameOutcome outcome, Action applyTransition) => applyTransition?.Invoke();
    }
}
