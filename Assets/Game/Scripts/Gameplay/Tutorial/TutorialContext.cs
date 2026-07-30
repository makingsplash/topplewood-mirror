using Gameplay.Core;
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

        /// <summary>Domain-facing input intents (the decorated <see cref="IGameplayInputSource"/> stream).</summary>
        public Observable<InputIntent> Inputs { get; }

        public TutorialContext(GameModel model, ITutorialView view, ITutorialInputGate inputGate,
            Observable<InputIntent> inputs)
        {
            Model = model;
            View = view;
            InputGate = inputGate;
            Inputs = inputs;
        }
    }
}
