namespace Gameplay.Tutorial
{
    /// <summary>
    /// Show/hide gate for the tutorial system (TASK-049 RFC §Q4). Bound by the hosting scope: prod
    /// <see cref="ProdTutorialGate"/> reads the dev flag <c>Data.Debug.DisableTutorial</c>; bringup
    /// <see cref="BringupTutorialGate"/> defaults on (no <c>Data</c> in the R5-isolated scope). This gate is
    /// about "may the tutorial play at all", separate from per-step input blocking (<see cref="ITutorialInputGate"/>).
    /// </summary>
    public interface ITutorialGate
    {
        bool Enabled { get; }
    }
}
