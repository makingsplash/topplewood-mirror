namespace Gameplay.Tutorial
{
    /// <summary>
    /// Persistence seam for tutorial completion (TASK-049 RFC §2): "has this tutorial been played?" keyed by a
    /// stable <c>tutorialId</c>. The concrete store is a Unity-side adapter bound by the hosting scope — prod
    /// <see cref="MetaTutorialProgressStore"/> writes the SDK-canon <c>Data.Meta.CompletedTutorials</c>; bringup
    /// <see cref="PrefsTutorialProgressStore"/> owns its own Data-free PlayerPrefs list (RFC R5). The tutorial
    /// layer never touches <c>DataStorage</c> directly, so it stays bringup-portable and out of the domain.
    /// </summary>
    public interface ITutorialProgressStore
    {
        bool IsCompleted(string tutorialId);

        /// <summary>Mark the tutorial played and flush durably (survives a kill).</summary>
        void MarkCompleted(string tutorialId);

        /// <summary>Dev reset: forget the tutorial so it replays. Flushed durably.</summary>
        void Reset(string tutorialId);
    }
}
