using Gameplay.Core;
using Gameplay.Core.Adapters;
using Gameplay.PlayScreen;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Read-only handle a <see cref="ITutorialStep"/> receives on Enter (TASK-049 RFC §3): the domain model to
    /// observe, the tour view, the input gate, the win interlude seam and the tour's numbers. Steps observe — they
    /// never mutate the domain — so the headless-simulatable invariant is preserved.
    /// </summary>
    public sealed class TutorialContext
    {
        public GameModel Model { get; }
        public ITourView View { get; }
        public ITutorialInputGate InputGate { get; }

        /// <summary>TASK-085: the screen-space tap halo, so a step's gate resolves taps with the SAME two-tier rule
        /// (and radius) as the domain — a near-miss on a small tapable piece passes the gate exactly when it would
        /// drop that piece in the domain.</summary>
        public IGameplayTapTolerance TapTolerance { get; }

        /// <summary>TASK-613: the seam a step arms to play its number between Won and the win screen.</summary>
        public WinInterlude Interlude { get; }

        public TourConfig Config { get; }

        public TutorialContext(GameModel model, ITourView view, ITutorialInputGate inputGate,
            IGameplayTapTolerance tapTolerance, WinInterlude interlude, TourConfig config)
        {
            Model = model;
            View = view;
            InputGate = inputGate;
            TapTolerance = tapTolerance;
            Interlude = interlude;
            Config = config;
        }
    }
}
