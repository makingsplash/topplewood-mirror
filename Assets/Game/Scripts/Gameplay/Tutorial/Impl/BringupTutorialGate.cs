namespace Gameplay.Tutorial
{
    /// <summary>
    /// Bringup <see cref="ITutorialGate"/> (TASK-049 RFC §Q4): the R5-isolated scope has no <c>Data.Debug</c>, so
    /// the gate defaults on — the tutorial always plays in bringup (until completed). A bringup-local toggle can
    /// be added later behind its own PlayerPrefs flag if the DDM needs one.
    /// </summary>
    public sealed class BringupTutorialGate : ITutorialGate
    {
        public bool Enabled => true;
    }
}
