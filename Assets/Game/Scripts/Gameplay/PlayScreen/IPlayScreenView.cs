using System.Threading;
using Cysharp.Threading.Tasks;
using Gameplay.Core;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// Adapter-port for the play-screen banner (TASK-116, RFC §2.3). The domain never imports this — only the
    /// Unity-side flow/sequence talk to it. <see cref="GameOutcome"/> is a pure domain enum, so referencing it
    /// here keeps the dependency one-directional (adapter → domain, never domain → adapter).
    /// </summary>
    public interface IPlayScreenView
    {
        /// <summary>Animate the WIN/LOSE banner in; completes when the intro animation finishes. Cancelling
        /// <paramref name="ct"/> throws <see cref="System.OperationCanceledException"/> (scope teardown mid-hold).</summary>
        UniTask ShowOutcomeAsync(GameOutcome outcome, CancellationToken ct);

        /// <summary>Hide the banner (called by the transition element just before the level advances/reloads).</summary>
        void HideOutcome();
    }
}
