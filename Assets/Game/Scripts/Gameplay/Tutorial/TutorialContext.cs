using Gameplay.Core;
using Gameplay.Core.Adapters;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Read-only handle a <see cref="ITutorialStep"/> receives on Enter (TASK-049 RFC §3): the domain model to
    /// observe, the tour view, the input gate and the tour's numbers. Steps observe — they never mutate the domain —
    /// so the headless-simulatable invariant is preserved.
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

        public TourConfig Config { get; }

        /// <summary>TASK-635: the HUD elements the hosting scope offers (<see cref="HudTapStep"/>); never null.</summary>
        public IHudTourTargets HudTargets { get; }

        public TutorialContext(GameModel model, ITourView view, ITutorialInputGate inputGate,
            IGameplayTapTolerance tapTolerance, TourConfig config, IHudTourTargets hudTargets = null)
        {
            Model = model;
            View = view;
            InputGate = inputGate;
            TapTolerance = tapTolerance;
            Config = config;
            HudTargets = hudTargets ?? HudTourTargets.Empty;
        }
    }
}
