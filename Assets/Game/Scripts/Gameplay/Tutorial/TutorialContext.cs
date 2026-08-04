using Gameplay.Core;
using Gameplay.Core.Adapters;
using R3;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Read-only handle a <see cref="ITutorialStep"/> receives on Enter (TASK-049 RFC §3): the domain model to
    /// observe, the highlight view, the input gate, and the domain-facing input intent stream. Steps observe —
    /// they never mutate the domain — so the headless-simulatable invariant is preserved.
    /// </summary>
    public sealed class TutorialContext
    {
        public GameModel Model { get; }
        public ITutorialView View { get; }
        public ITutorialInputGate InputGate { get; }

        /// <summary>TASK-085: the screen-space tap halo, so a step's gate resolves taps with the SAME two-tier rule
        /// (and radius) as the domain — a near-miss on a small tapable piece passes the gate exactly when it would
        /// drop that piece in the domain.</summary>
        public IGameplayTapTolerance TapTolerance { get; }

        /// <summary>Domain-facing input intents (the decorated <see cref="IGameplayInputSource"/> stream).</summary>
        public Observable<InputIntent> Inputs { get; }

        public TutorialContext(GameModel model, ITutorialView view, ITutorialInputGate inputGate,
            Observable<InputIntent> inputs, IGameplayTapTolerance tapTolerance)
        {
            Model = model;
            View = view;
            InputGate = inputGate;
            Inputs = inputs;
            TapTolerance = tapTolerance;
        }
    }
}
