using System.Collections.Generic;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// A single tutorial tour (TASK-049 RFC §6): a stable id plus its ordered steps. v1 carries one tour (the
    /// first-level smoke). The <see cref="Id"/> is the ONE key shared by the progress store and the analytics
    /// funnel (human 15:21) — do not diverge them.
    /// </summary>
    public sealed class TutorialDefinition
    {
        public string Id { get; }
        public IReadOnlyList<ITutorialStep> Steps { get; }

        public TutorialDefinition(string id, IReadOnlyList<ITutorialStep> steps)
        {
            Id = id;
            Steps = steps;
        }
    }
}
