using DataStorage;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Production <see cref="ITutorialProgressStore"/> (TASK-049 RFC §Q1): completion IS the SDK-canon
    /// <c>Data.Meta.CompletedTutorials</c> (the pre-existing <c>PlayerPrefsListString</c>), reached through the
    /// new <c>Meta.IsTutorialCompleted</c>/<c>MarkTutorialCompleted</c>/<c>ResetTutorial</c> wrappers — that pair
    /// of methods IS the "TutorialsDataManager" the old comment referenced. Writes hit the SHARED
    /// <c>Data.Meta</c> instance and flush durably inside Meta (persistence R1 + flush). No second parallel
    /// store, no save bump.
    /// </summary>
    public sealed class MetaTutorialProgressStore : ITutorialProgressStore
    {
        public bool IsCompleted(string tutorialId) => Data.Meta.IsTutorialCompleted(tutorialId);

        public void MarkCompleted(string tutorialId) => Data.Meta.MarkTutorialCompleted(tutorialId);

        public void Reset(string tutorialId) => Data.Meta.ResetTutorial(tutorialId);
    }
}
