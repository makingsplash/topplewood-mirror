using DataStorage;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Production <see cref="ITutorialGate"/> (TASK-049 RFC §Q4): reads the existing dev flag
    /// <c>Data.Debug.DisableTutorial</c> so the tutorial is suppressible exactly as the surviving hook intends.
    /// </summary>
    public sealed class ProdTutorialGate : ITutorialGate
    {
        public bool Enabled => !Data.Debug.DisableTutorial.Value;
    }
}
